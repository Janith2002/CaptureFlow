using System.Resources;
using System.Security.Cryptography;
using CaptureFlow.Core.Recovery;
using CaptureFlow.Infrastructure.Recovery;
using CaptureFlow.Infrastructure.Desktop;
using CaptureFlow.Infrastructure.Ffmpeg;
namespace CaptureFlow.Tests;

public class ReleasePreparationTests
{
    [Fact] public void DefaultApplicationIdentityDoesNotInventALegalPublisher()
    {
        var assembly = typeof(CaptureFlow.Desktop.App).Assembly;
        Assert.Equal("CaptureFlow", assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyProductAttribute), false)
            .Cast<System.Reflection.AssemblyProductAttribute>().Single().Product);
        Assert.Empty(assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyCompanyAttribute), false));
        Assert.Equal("CaptureFlow", assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyTitleAttribute), false)
            .Cast<System.Reflection.AssemblyTitleAttribute>().Single().Title);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static async Task<RecoveryRecord> BeginAsync(JsonRecoveryJournal journal, string root) => (await journal.BeginAsync("Monitor", root, root, "MP4", false))!;
    [Fact] public async Task CompletedJournalArchivesDoNotExhaustActiveSessionCapacity()
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new JsonRecoveryJournal(scratch.Path);
        for (var i = 0; i < 1001; i++)
        { var record = await BeginAsync(journal, scratch.Path); await journal.UpdateAsync(record.Id, "Completed", 1); }
        var next = await BeginAsync(journal, scratch.Path); Assert.Equal("Starting", next.State);
        Assert.Equal(1001, Directory.EnumerateDirectories(Path.Combine(journal.Preferences.StorageLocation, "archive")).Count());
    }
    [Fact] public async Task CancelledCleanupLeavesJournalAndMediaUntouched()
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var record = await BeginAsync(journal, scratch.Path);
        await journal.UpdateAsync(record.Id, "Error", 1);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.CleanupAsync(true, cancellation.Token));
        Assert.True(File.Exists(Path.Combine(journal.Preferences.StorageLocation, record.Id.ToString("N"), "session.json")));
    }
    [Theory] [InlineData("Completed")] [InlineData("Recovered")] [InlineData("EditorSaved")]
    public async Task AutomaticArchiveMovesOnlyJournalAndPreservesMediaAndUnknownFiles(string state)
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new JsonRecoveryJournal(scratch.Path);
        var record = await BeginAsync(journal, scratch.Path); var folder = Path.Combine(journal.Preferences.StorageLocation, record.Id.ToString("N"));
        var original = Path.Combine(folder, "original.wav"); var project = Path.Combine(folder, "user.cfedit"); await File.WriteAllTextAsync(original, "preserve"); await File.WriteAllTextAsync(project, "project");
        await journal.UpdateAsync(record.Id, state, 2);
        Assert.False(File.Exists(Path.Combine(folder, "session.json"))); Assert.True(File.Exists(Path.Combine(journal.Preferences.StorageLocation, "archive", record.Id.ToString("N"), "session.json")));
        Assert.Equal("preserve", await File.ReadAllTextAsync(original)); Assert.Equal("project", await File.ReadAllTextAsync(project)); Assert.Empty(await journal.ScanAsync());
    }
    [Theory] [InlineData("Recording")] [InlineData("Paused")] [InlineData("Error")] [InlineData("Cancelled")] [InlineData("Dismissed")]
    public async Task PotentiallyRecoverableAndActiveSessionsAreNeverArchivedOrCleaned(string state)
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var record = await BeginAsync(journal, scratch.Path);
        await journal.SavePreferencesAsync(journal.Preferences with { TerminalRetentionDays = 0 });
        await journal.UpdateAsync(record.Id, state, 2); var result = await journal.CleanupAsync(true);
        Assert.Equal(0, result.FilesRemoved); Assert.True(File.Exists(Path.Combine(journal.Preferences.StorageLocation, record.Id.ToString("N"), "session.json")));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ConfirmedCleanupDeletesOnlyVerifiedAgedMetadataNeverFinalOrIntermediate(bool confirmed)
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new JsonRecoveryJournal(Path.Combine(scratch.Path, "recovery"));
        await journal.SavePreferencesAsync(journal.Preferences with { TerminalRetentionDays = 0 });
        var original = Path.Combine(scratch.Path, "original.mp4"); var bytes = new byte[200]; await File.WriteAllBytesAsync(original, bytes);
        var record = await BeginAsync(journal, scratch.Path); await journal.PrepareOutputAsync(record.Id, original, Hash(bytes)); await journal.UpdateAsync(record.Id, "Completed", 2, original);
        var result = await journal.CleanupAsync(confirmed); Assert.Equal(confirmed ? 1 : 0, result.FilesRemoved);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(original)); Assert.True(Directory.Exists(scratch.Path));
    }
    [Theory] [InlineData("missing")] [InlineData("changed")] [InlineData("young")] [InlineData("nohash")]
    public async Task UncertainOrYoungFinishedSessionsStayProtected(string kind)
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new JsonRecoveryJournal(scratch.Path);
        await journal.SavePreferencesAsync(journal.Preferences with { TerminalRetentionDays = kind == "young" ? 30 : 0 });
        var final = Path.Combine(scratch.Path, "output.mp4"); var data = new byte[200]; await File.WriteAllBytesAsync(final, data);
        var record = await BeginAsync(journal, scratch.Path);
        if (kind != "nohash") await journal.PrepareOutputAsync(record.Id, final, kind == "changed" ? "bad-fingerprint" : Hash(data));
        await journal.UpdateAsync(record.Id, "Completed", 2, kind == "missing" ? Path.Combine(scratch.Path, "missing.mp4") : final);
        Assert.Equal(0, (await journal.CleanupAsync(true)).FilesRemoved); Assert.Equal(data, await File.ReadAllBytesAsync(final));
    }
    [Fact] public async Task OrphanedMediaAndUserProjectsInManagedTreeArePreserved()
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var orphan = Path.Combine(journal.Preferences.StorageLocation, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(orphan);
        var file = Path.Combine(orphan, "orphan.mp4"); await File.WriteAllTextAsync(file, "do not delete");
        var stats = await journal.InventoryAsync(); Assert.Contains(stats.Items, item => item.Classification.StartsWith("Orphaned")); Assert.True(stats.ManagedBytes > 0);
        await journal.CleanupAsync(true); Assert.Equal("do not delete", await File.ReadAllTextAsync(file));
    }
    [Theory] [InlineData("../escape/session.json")] [InlineData("../root2/session.json")]
    public void PathTraversalAndPrefixSiblingAreRejected(string relative)
    {
        using var scratch = new RecoveryTests.Scratch();
        Assert.Throws<IOException>(() => JsonRecoveryJournal.ValidateManagedPath(scratch.Path, Path.Combine(scratch.Path, relative)));
    }
    [Fact] public async Task SavedEditorAutosaveResumesAsVisibleActiveJournalAfterArchiving()
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); var id = Guid.NewGuid(); var project = CaptureFlow.Core.Editing.EditorProject.Empty;
        await journal.SaveEditorAsync(id, new(project, project, [], [])); await journal.UpdateAsync(id, "EditorSaved", 0);
        Assert.Empty(await journal.ScanAsync()); Assert.Equal(project.Id, (await journal.LoadEditorAsync(id)).Project.Id);
        var edit = project with { Name = "new unsaved edit" }; await journal.SaveEditorAsync(id, new(edit, project, [project], []));
        Assert.Equal("Editor", Assert.Single(await journal.ScanAsync()).Record.State); Assert.Equal("new unsaved edit", (await journal.LoadEditorAsync(id)).Project.Name);
    }
    [Fact] public async Task SavedAutosaveCleanupPreservesExplicitProjectAndImportedMedia()
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new JsonRecoveryJournal(scratch.Path); await journal.SavePreferencesAsync(journal.Preferences with { TerminalRetentionDays = 0 });
        var id = Guid.NewGuid(); var project = CaptureFlow.Core.Editing.EditorProject.Empty; await journal.SaveEditorAsync(id, new(project, project, [], []));
        var original = Path.Combine(scratch.Path, "explicit.cfedit"); await new CaptureFlow.Infrastructure.Editing.JsonEditorProjectStore().SaveAsync(project, original, default);
        var bytes = await File.ReadAllBytesAsync(original); await journal.PrepareOutputAsync(id, original, Hash(bytes)); await journal.UpdateAsync(id, "EditorSaved", 0, original);
        var result = await journal.CleanupAsync(true); Assert.Equal(2, result.FilesRemoved); Assert.Equal(bytes, await File.ReadAllBytesAsync(original));
    }
    [Fact] public async Task LegacyFinishedIndexIsMigratedWithoutDeletingData()
    {
        using var scratch = new RecoveryTests.Scratch(); var old = new JsonRecoveryJournal(scratch.Path);
        await old.SavePreferencesAsync(old.Preferences with { AutoArchiveFinished = false });
        var record = await BeginAsync(old, scratch.Path); await old.UpdateAsync(record.Id, "Completed", 2);
        await old.SavePreferencesAsync(old.Preferences with { AutoArchiveFinished = true, TerminalRetentionDays = 7 });
        var restarted = new JsonRecoveryJournal(scratch.Path); await restarted.LoadPreferencesAsync();
        Assert.Equal(7, restarted.Preferences.TerminalRetentionDays); Assert.True(File.Exists(Path.Combine(restarted.Preferences.StorageLocation, "archive", record.Id.ToString("N"), "session.json")));
        Assert.NotNull(await BeginAsync(restarted, scratch.Path));
    }
    [Theory] [InlineData(-1)] [InlineData(3651)]
    public async Task InvalidRetentionPreferenceIsRejected(int days)
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new JsonRecoveryJournal(scratch.Path);
        await Assert.ThrowsAsync<ArgumentException>(() => journal.SavePreferencesAsync(journal.Preferences with { TerminalRetentionDays = days }));
    }
    [Fact] public void UserDataPathsAreAbsoluteAndIndependentOfWorkingDirectory()
    {
        using var scratch = new RecoveryTests.Scratch(); Assert.Equal(Path.Combine(scratch.Path, "CaptureFlow"), ApplicationDataPaths.ForUser(scratch.Path));
        Assert.True(Path.IsPathFullyQualified(ApplicationDataPaths.Root)); Assert.Throws<IOException>(() => ApplicationDataPaths.ForUser("relative"));
    }
    [Theory] [InlineData("tools/ffmpeg.exe")] [InlineData("ffmpeg.exe -version")] [InlineData("\"ffmpeg.exe\"")]
    public void RelativeArgumentAndQuotedToolPathsAreRejected(string executable) => Assert.ThrowsAny<IOException>(() => FfmpegToolPaths.ResolveExecutable(executable, ""));
    [Fact] public void MissingToolDoesNotSearchWorkingDirectoryOrReplaceExplicitPath()
    {
        Assert.Throws<FileNotFoundException>(() => FfmpegToolPaths.ResolveExecutable("ffmpeg.exe", ""));
        using var scratch = new RecoveryTests.Scratch(); Assert.Throws<FileNotFoundException>(() => FfmpegToolPaths.ResolveExecutable(Path.Combine(scratch.Path, "ffmpeg.exe")));
    }
    [Fact] public async Task FirstRunAndExternalPathPersistenceDoNotWriteOnLoad()
    {
        using var scratch = new RecoveryTests.Scratch(); var path = Path.Combine(scratch.Path, "preferences", "external.json"); var store = new JsonFfmpegPreferencesStore(path);
        Assert.Equal("ffmpeg.exe", await store.LoadAsync()); Assert.False(File.Exists(path));
        var exe = Path.Combine(scratch.Path, "ffmpeg.exe"); await File.WriteAllTextAsync(exe, "fixture file never executed");
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.SaveAsync(exe));
        await File.WriteAllTextAsync(Path.Combine(scratch.Path, "ffprobe.exe"), "fixture file never executed");
        await store.SaveAsync(exe); Assert.Equal(exe, await new JsonFfmpegPreferencesStore(path).LoadAsync());
    }
    [FfmpegTheory] [InlineData(true)]
    public async Task ActualExternalPairVersionsMatch(bool verify)
    { Assert.True(verify); Assert.False(string.IsNullOrWhiteSpace(await FfmpegToolPaths.VerifyPairAsync(Executable.Find("ffmpeg.exe")!))); }
    [Fact] public void ExecutableIconResourceContainsSevenProperPngFramesAndHighResolutionBrandAsset()
    {
        using var resources = typeof(CaptureFlow.Desktop.App).Assembly.GetManifestResourceStream("CaptureFlow.Desktop.g.resources")!; Assert.NotNull(resources);
        using var reader = new ResourceReader(resources); var entries = reader.Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value);
        Assert.Contains("assets/captureflow.ico", entries.Keys); Assert.Contains("assets/captureflow.png", entries.Keys);
        using var icon = new BinaryReader((Stream)entries["assets/captureflow.ico"]!); Assert.Equal(0, icon.ReadUInt16()); Assert.Equal(1, icon.ReadUInt16()); Assert.Equal(7, icon.ReadUInt16());
        foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            Assert.Equal(size % 256, icon.ReadByte()); Assert.Equal(size % 256, icon.ReadByte()); icon.ReadUInt16(); Assert.Equal(1, icon.ReadUInt16()); Assert.Equal(32, icon.ReadUInt16());
            var length = icon.ReadInt32(); var offset = icon.ReadInt32(); var next = icon.BaseStream.Position; icon.BaseStream.Position = offset;
            var bytes = icon.ReadBytes(length); Assert.Equal(new byte[] { 137, 80, 78, 71 }, bytes.Take(4));
            Assert.Equal(size, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16))); Assert.Equal(size, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)));
            icon.BaseStream.Position = next;
        }
        using var png = (Stream)entries["assets/captureflow.png"]!; var header = new byte[24]; png.ReadExactly(header);
        Assert.Equal(1024, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16))); Assert.Equal(1024, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)));
    }
}
