using System.Buffers.Binary;
using System.Text;
using CaptureFlow.Application.Editing;
using CaptureFlow.Application.Recovery;
using CaptureFlow.Core.Editing;
using CaptureFlow.Core.Recovery;
using CaptureFlow.Infrastructure.Recovery;
using CaptureFlow.Infrastructure.Library;
using CaptureFlow.Core.Library;
using NAudio.Wave;
namespace CaptureFlow.Tests;

public class RecoveryTests
{
    internal sealed class Scratch : IDisposable
    {
        private readonly string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CaptureFlow-recovery-tests"));
        public string Path { get; }
        public Scratch() { Path = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public void Dispose() { if (!System.IO.Path.GetFullPath(Path).StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(); Directory.Delete(Path, true); }
    }
    private static Task<RecoveryRecord?> Begin(JsonRecoveryJournal journal, string folder, string format = "WAV") => journal.BeginAsync("MicrophoneOnly", folder, folder, format, true);
    [Theory] [InlineData("Error")] [InlineData("Cancelled")] [InlineData("Starting")] [InlineData("Paused")]
    public async Task AtomicPersistenceAndOwnerIdentity(string state)
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path);
        var record = (await Begin(journal, scratch.Path))!;
        await journal.UpdateAsync(record.Id, state, 4.5);
        var next = new JsonRecoveryJournal(scratch.Path); var candidate = Assert.Single(await next.ScanAsync());
        Assert.Equal(state, candidate.Record.State); Assert.Equal(4.5, candidate.Record.DurationSeconds);
        Assert.Equal(state is "Starting" or "Paused", candidate.IsLive);
        Assert.Empty(Directory.GetFiles(scratch.Path, "*.tmp", SearchOption.AllDirectories));
    }
    [Theory] [InlineData("Completed")] [InlineData("Recovered")] [InlineData("Dismissed")] [InlineData("EditorSaved")]
    public async Task TerminalNotificationsHiddenButMediaRetained(string state)
    {
        using var scratch = new Scratch(); var media = Path.Combine(scratch.Path, "keep.wav"); await File.WriteAllTextAsync(media, "user content");
        var journal = new JsonRecoveryJournal(scratch.Path); var record = (await Begin(journal, scratch.Path))!;
        await journal.UpdateAsync(record.Id, state, 1);
        Assert.Empty(await journal.ScanAsync()); Assert.Equal("user content", await File.ReadAllTextAsync(media));
    }
    [Fact] public async Task MissingTemporaryDirectoryReported()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path);
        var record = (await journal.BeginAsync("Monitor", scratch.Path, Path.Combine(scratch.Path, "missing"), "MP4", false))!;
        await journal.UpdateAsync(record.Id, "Error", 0);
        var candidate = Assert.Single(await journal.ScanAsync()); Assert.Empty(candidate.Files); Assert.Contains("missing", candidate.Warning);
    }
    [Fact] public async Task DeadOwnerIsRecoverable()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path);
        var record = (await Begin(journal, scratch.Path))!;
        await journal.SaveRecordAsync(record with { OwnerStartedTicks = 0 });
        Assert.False(Assert.Single(await journal.ScanAsync()).IsLive);
    }
    [Fact] public async Task ConcurrentUpdatesLeaveCompleteJson()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var record = (await Begin(journal, scratch.Path))!;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => journal.UpdateAsync(record.Id, "Error", i)));
        Assert.Single(await new JsonRecoveryJournal(scratch.Path).ScanAsync());
    }
    [Fact] public async Task CancelledAtomicUpdatePreservesPreviousRecord()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var record = (await Begin(journal, scratch.Path))!;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.UpdateAsync(record.Id, "Error", 12, token: cancellation.Token));
        Assert.Equal("Starting", Assert.Single(await journal.ScanAsync()).Record.State);
    }
    [Fact] public async Task LockedDestinationFailurePreservesPreviousMetadata()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var record = (await Begin(journal, scratch.Path))!;
        var path = Path.Combine(journal.Preferences.StorageLocation, record.Id.ToString("N"), "session.json");
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => journal.UpdateAsync(record.Id, "Error", 4));
        Assert.Equal("Starting", Assert.Single(await journal.ScanAsync()).Record.State);
    }
    [Theory] [InlineData(14)] [InlineData(301)]
    public async Task InvalidIntervalDoesNotChangePreferences(int seconds)
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path);
        await Assert.ThrowsAsync<ArgumentException>(() => journal.SavePreferencesAsync(journal.Preferences with { AutosaveSeconds = seconds }));
        Assert.Equal(60, journal.Preferences.AutosaveSeconds);
    }
    [Fact] public async Task InvalidAndInstallationLocationsRejected()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path);
        await Assert.ThrowsAsync<ArgumentException>(() => journal.SavePreferencesAsync(journal.Preferences with { StorageLocation = "relative" }));
        await Assert.ThrowsAsync<ArgumentException>(() => journal.SavePreferencesAsync(journal.Preferences with { StorageLocation = AppContext.BaseDirectory }));
    }
    [Fact] public async Task FileAsLocationFailsWithoutHidingOldSessions()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); await Begin(journal, scratch.Path);
        var file = Path.Combine(scratch.Path, "not-folder"); await File.WriteAllTextAsync(file, "keep");
        await Assert.ThrowsAnyAsync<IOException>(() => journal.SavePreferencesAsync(journal.Preferences with { StorageLocation = file }));
        Assert.Single(await journal.ScanAsync()); Assert.Equal("keep", await File.ReadAllTextAsync(file));
    }
    [Fact] public async Task PreferencesPersistAndRelocationKeepsOldSessions()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); await Begin(journal, scratch.Path);
        await journal.SavePreferencesAsync(journal.Preferences with { StorageLocation = Path.Combine(scratch.Path, "other"), AutosaveSeconds = 15, RecordingEnabled = false });
        var reopened = new JsonRecoveryJournal(scratch.Path); await reopened.LoadPreferencesAsync();
        Assert.False(reopened.RecordingEnabled); Assert.Equal(15, reopened.Preferences.AutosaveSeconds); Assert.Single(await reopened.ScanAsync());
        Assert.Null(await Begin(reopened, scratch.Path));
    }
    [Fact] public async Task EditorSnapshotRestoresUnsavedUndoRedoAndMissingMedia()
    {
        using var scratch = new Scratch(); var session = new EditorSession(); var original = session.Project;
        session.Import(new(Guid.NewGuid(), Path.Combine(scratch.Path, "missing.wav"), "WAV", 2, 0, 0, false, true, 123, 0));
        session.Apply(session.Project with { Name = "Unsaved" }); session.Undo();
        var journal = new JsonRecoveryJournal(scratch.Path); var id = Guid.NewGuid(); await journal.SaveEditorAsync(id, session.Snapshot());
        var restored = new EditorSession(); restored.Restore(await journal.LoadEditorAsync(id));
        Assert.True(restored.Dirty); Assert.True(restored.CanUndo); Assert.True(restored.CanRedo); Assert.Single(restored.Project.MissingSources);
        restored.Redo(); Assert.Equal("Unsaved", restored.Project.Name); restored.Undo(); restored.Undo(); Assert.Equal(original.Name, restored.Project.Name); Assert.False(restored.Dirty);
        await journal.DismissAsync(id); Assert.Empty(await journal.ScanAsync()); Assert.NotNull(await journal.LoadEditorAsync(id));
    }
    [Fact] public void EditorHistoryIsBoundedAndInvalidRestoreLeavesCurrentProject()
    {
        var session = new EditorSession(); for (var i = 0; i < 50; i++) session.Apply(session.Project with { Name = "Edit" + i });
        var snapshot = session.Snapshot(); Assert.Equal(20, snapshot.Undo.Length);
        Assert.Throws<ArgumentException>(() => session.Restore(snapshot with { Project = snapshot.Project with { Version = 99 } }));
        Assert.Equal("Edit49", session.Project.Name);
    }
    [Theory] [InlineData(1)] [InlineData(2)]
    public async Task InterruptedWaveHeaderRepairedIntoNewFile(int channels)
    {
        using var scratch = new Scratch(); var input = Path.Combine(scratch.Path, "capture.wav.partial"); var output = Path.Combine(scratch.Path, "copy.wav");
        using (var writer = new WaveFileWriter(input, new WaveFormat(48000, 16, channels))) writer.Write(new byte[48000 * 2 * channels]);
        var bytes = await File.ReadAllBytesAsync(input); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 0); await File.WriteAllBytesAsync(input, bytes);
        await MediaSalvage.RepairWaveAsync(input, output, default);
        using var wave = new WaveFileReader(output); Assert.Equal(1, wave.TotalTime.TotalSeconds); Assert.Equal(channels, wave.WaveFormat.Channels);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(input));
    }
    [Fact] public async Task IncompleteFragmentTailExcludedAndOriginalPreserved()
    {
        using var scratch = new Scratch(); var input = Path.Combine(scratch.Path, "screen.mp4"); var output = Path.Combine(scratch.Path, "copy.mp4");
        byte[] Box(string name, int size) { var bytes = new byte[size]; BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)size); Encoding.ASCII.GetBytes(name).CopyTo(bytes, 4); return bytes; }
        var prefix = Box("ftyp", 8).Concat(Box("moov", 8)).Concat(Box("moof", 8)).Concat(Box("mdat", 12)).ToArray();
        var bytes = prefix.Concat(Box("moof", 8)).Concat(Box("mdat", 32).Take(9)).ToArray(); await File.WriteAllBytesAsync(input, bytes);
        await MediaSalvage.CopyFragmentsAsync(input, output, default); Assert.Equal(prefix, await File.ReadAllBytesAsync(output)); Assert.Equal(bytes, await File.ReadAllBytesAsync(input));
    }
    [Fact] public async Task OrdinaryUnfinalizedMp4AndInvalidWaveRejected()
    {
        using var scratch = new Scratch(); var file = Path.Combine(scratch.Path, "invalid"); await File.WriteAllTextAsync(file, "incomplete ordinary MP4");
        await Assert.ThrowsAsync<IOException>(() => MediaSalvage.CopyFragmentsAsync(file, Path.Combine(scratch.Path, "new.mp4"), default));
        await Assert.ThrowsAsync<IOException>(() => MediaSalvage.RepairWaveAsync(file, Path.Combine(scratch.Path, "new.wav"), default));
    }
    private sealed class FakeMedia : IRecoveryMediaBackend
    {
        public int Encodes; public Exception? Failure;
        public Task<RecoveredMedia> ValidateAsync(string path, string ffmpeg, CancellationToken token) => Task.FromResult(new RecoveredMedia(path, 2, false, true, ""));
        public async Task<RecoveredMedia> RecoverAsync(RecoveryCandidate candidate, string path, string ffmpeg, CancellationToken token)
        { Encodes++; if (Failure is not null) throw Failure; token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "fixture", token); return new(path, 2, false, true, "partial"); }
    }
    private sealed class FailingRepository(IRecordingRepository inner) : IRecordingRepository
    {
        public bool Fail = true;
        public Task SaveAsync(IReadOnlyList<RecordingEntry> entries, CancellationToken token) => Fail ? Task.FromException(new IOException("Database unavailable")) : inner.SaveAsync(entries, token);
        public Task<IReadOnlyList<RecordingEntry>> GetFolderAsync(string folder, CancellationToken token) => inner.GetFolderAsync(folder, token);
        public Task BeginRenameAsync(PendingRename rename, CancellationToken token) => inner.BeginRenameAsync(rename, token);
        public Task CompleteRenameAsync(RecordingEntry entry, CancellationToken token) => inner.CompleteRenameAsync(entry, token);
        public Task CancelRenameAsync(Guid id, CancellationToken token) => inner.CancelRenameAsync(id, token);
        public Task<IReadOnlyList<PendingRename>> GetRenamesAsync(CancellationToken token) => inner.GetRenamesAsync(token);
        public Task RemoveAsync(Guid id, CancellationToken token) => inner.RemoveAsync(id, token);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task DatabaseFailureRetriesValidatedOutputWithoutDuplicateEncoding(bool changedExternally)
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var record = (await Begin(journal, scratch.Path))!; await journal.UpdateAsync(record.Id, "Error", 2);
        var backend = new FakeMedia(); var repository = new FailingRepository(new SqliteRecordingRepository(Path.Combine(scratch.Path, "library.db")));
        var service = new RecordingRecoveryService(journal, backend, repository); var candidate = Assert.Single(await journal.ScanAsync());
        await Assert.ThrowsAsync<IOException>(() => service.RecoverAsync(candidate, "unused"));
        candidate = Assert.Single(await journal.ScanAsync()); Assert.Equal("LibraryPending", candidate.Record.State); Assert.True(File.Exists(candidate.Record.RecoveredFile));
        repository.Fail = false;
        if (changedExternally)
        {
            await File.WriteAllTextAsync(candidate.Record.RecoveredFile!, "modified externally");
            await Assert.ThrowsAsync<IOException>(() => service.RecoverAsync(candidate, "unused"));
            Assert.Equal("modified externally", await File.ReadAllTextAsync(candidate.Record.RecoveredFile!));
            Assert.Empty(await repository.GetFolderAsync(scratch.Path, default));
        }
        else { await service.RecoverAsync(candidate, "unused"); Assert.Single(await repository.GetFolderAsync(scratch.Path, default)); Assert.Empty(await journal.ScanAsync()); }
        Assert.Equal(1, backend.Encodes);
    }
    [Fact] public async Task ActiveSessionCannotBeRecovered()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); await Begin(journal, scratch.Path);
        var backend = new FakeMedia(); var service = new RecordingRecoveryService(journal, backend, new SqliteRecordingRepository(Path.Combine(scratch.Path, "library.db")));
        var candidate = Assert.Single(await journal.ScanAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecoverAsync(candidate, "unused"));
        Assert.Equal(0, backend.Encodes);
    }
    [Fact] public async Task NormalPublishCrashAdoptsMatchingOutputWithoutReencoding()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var record = (await Begin(journal, scratch.Path))!;
        var final = Path.Combine(scratch.Path, "normal-completed.wav"); await File.WriteAllTextAsync(final, "already finalized");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(final)));
        await journal.PrepareOutputAsync(record.Id, final, hash);
        Assert.True(Assert.Single(await journal.ScanAsync()).IsLive);
        var candidate = Assert.Single(await journal.ScanAsync()); await journal.SaveRecordAsync(candidate.Record with { OwnerStartedTicks = 0 });
        var backend = new FakeMedia(); var repository = new SqliteRecordingRepository(Path.Combine(scratch.Path, "library.db"));
        var result = await new RecordingRecoveryService(journal, backend, repository).RecoverAsync(Assert.Single(await journal.ScanAsync()), "unused");
        Assert.Equal(final, result.Path); Assert.Equal(0, backend.Encodes); Assert.Single(await repository.GetFolderAsync(scratch.Path, default));
        Assert.Equal("already finalized", await File.ReadAllTextAsync(final));
    }
    [Fact] public async Task InvalidRecoveryMetadataIsReported()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var record = (await Begin(journal, scratch.Path))!;
        var path = Path.Combine(journal.Preferences.StorageLocation, record.Id.ToString("N"), "session.json"); await File.WriteAllTextAsync(path, "{ incomplete");
        var candidate = Assert.Single(await journal.ScanAsync());
        Assert.Equal(record.Id, candidate.Record.Id); Assert.Equal("Unreadable", candidate.Record.State);
        Assert.Contains("inspection failed", candidate.Warning); Assert.Equal("{ incomplete", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task CorruptJournalDoesNotHideHealthyIncompleteSessionOrOverwriteMetadata()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path);
        var damaged = (await Begin(journal, scratch.Path))!; var healthy = (await Begin(journal, scratch.Path))!;
        await journal.UpdateAsync(healthy.Id, "Error", 3);
        var path = Path.Combine(journal.Preferences.StorageLocation, damaged.Id.ToString("N"), "session.json");
        await File.WriteAllTextAsync(path, "{ corrupt journal");
        var sessions = await journal.ScanAsync(); Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, c => c.Record.Id == healthy.Id && c.Record.State == "Error" && !c.IsLive);
        Assert.Contains(sessions, c => c.Record.Id == damaged.Id && c.Record.State == "Unreadable" && c.Warning is not null);
        var backend = new FakeMedia(); var service = new RecordingRecoveryService(journal, backend, new SqliteRecordingRepository(Path.Combine(scratch.Path, "library.db")));
        await Assert.ThrowsAsync<IOException>(() => service.RecoverAsync(sessions.Single(c => c.Record.Id == damaged.Id), "unused"));
        Assert.Equal(0, backend.Encodes); Assert.Equal("{ corrupt journal", await File.ReadAllTextAsync(path));
    }
    [Theory] [InlineData("{}")] [InlineData("null")]
    public async Task MissingJournalFieldsAreVisibleFailuresWithoutAbortingHealthyScan(string invalid)
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path);
        var damaged = (await Begin(journal, scratch.Path))!; var healthy = (await Begin(journal, scratch.Path))!;
        var path = Path.Combine(journal.Preferences.StorageLocation, damaged.Id.ToString("N"), "session.json"); await File.WriteAllTextAsync(path, invalid);
        var sessions = await journal.ScanAsync(); Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, c => c.Record.Id == damaged.Id && c.Record.State == "Unreadable");
        Assert.Contains(sessions, c => c.Record.Id == healthy.Id && c.Record.State == "Starting" && c.IsLive);
        Assert.Equal(invalid, await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task ExclusiveRecoveryLeaseBlocksOtherInstancesAndIsReleased()
    {
        using var scratch = new Scratch(); var first = new JsonRecoveryJournal(scratch.Path); var record = (await Begin(first, scratch.Path))!;
        var second = new JsonRecoveryJournal(scratch.Path);
        using (await first.AcquireRecoveryAsync(record.Id))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => second.AcquireRecoveryAsync(record.Id));
            await Assert.ThrowsAnyAsync<IOException>(() => second.DismissAsync(record.Id));
        }
        using (await second.AcquireRecoveryAsync(record.Id)) { }
        Assert.Single(await first.ScanAsync());
    }
    [Fact] public async Task FailedUnknownLookupDoesNotPinFutureSessionToOldStorageLocation()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var id = Guid.NewGuid();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => journal.AcquireRecoveryAsync(id));
        var destination = Path.Combine(scratch.Path, "new-location");
        await journal.SavePreferencesAsync(journal.Preferences with { StorageLocation = destination });
        await journal.SaveRecordAsync(new(id, "Monitor", scratch.Path, scratch.Path, "MP4", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Error"));
        Assert.True(File.Exists(Path.Combine(destination, id.ToString("N"), "session.json")));
        Assert.False(File.Exists(Path.Combine(scratch.Path, "sessions", id.ToString("N"), "session.json")));
        Assert.Equal(id, Assert.Single(await journal.ScanAsync()).Record.Id);
    }
    [Fact] public async Task RecoveryPublishesToSqliteExactlyOnce()
    {
        using var scratch = new Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var record = (await Begin(journal, scratch.Path))!; await journal.UpdateAsync(record.Id, "Error", 2);
        var backend = new FakeMedia(); var repository = new SqliteRecordingRepository(Path.Combine(scratch.Path, "library.db")); var service = new RecordingRecoveryService(journal, backend, repository);
        var candidate = Assert.Single(await journal.ScanAsync()); var media = await service.RecoverAsync(candidate, "unused");
        Assert.True(File.Exists(media.Path)); Assert.Contains("Recovered_", media.Path); Assert.Empty(await journal.ScanAsync());
        Assert.Single(await repository.GetFolderAsync(scratch.Path, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecoverAsync(candidate, "unused")); Assert.Equal(1, backend.Encodes);
    }
    [Theory] [InlineData("disk")] [InlineData("permission")] [InlineData("cancel")]
    public async Task RecoveryFailureNeverPublishesOrDiscards(string failure)
    {
        using var scratch = new Scratch(); var original = Path.Combine(scratch.Path, "original.wav"); await File.WriteAllTextAsync(original, "keep");
        var journal = new JsonRecoveryJournal(scratch.Path); var record = (await Begin(journal, scratch.Path))!; await journal.UpdateAsync(record.Id, "Error", 2);
        var backend = new FakeMedia { Failure = failure == "disk" ? new IOException("disk full") : failure == "permission" ? new UnauthorizedAccessException("denied") : new OperationCanceledException() };
        var repository = new SqliteRecordingRepository(Path.Combine(scratch.Path, "db.sqlite")); var service = new RecordingRecoveryService(journal, backend, repository);
        var candidate = Assert.Single(await journal.ScanAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => service.RecoverAsync(candidate, "unused"));
        Assert.Empty(await repository.GetFolderAsync(scratch.Path, default)); Assert.Single(await journal.ScanAsync()); Assert.Equal("keep", await File.ReadAllTextAsync(original));
    }
}
