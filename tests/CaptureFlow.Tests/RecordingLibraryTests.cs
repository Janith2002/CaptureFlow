using CaptureFlow.Application.Library;
using CaptureFlow.Core.Library;
using CaptureFlow.Infrastructure.Library;
namespace CaptureFlow.Tests;

public class RecordingLibraryTests
{
    [Fact]
    public async Task DiscoveryPersistsMetadataAndDoesNotDuplicateRepeatedImportsOrFolderSeparators()
    {
        using var fixture = new Fixture(); await fixture.Add("CaptureFlow_Screen_fixture.mp4"); await fixture.Add("song.MP3"); await fixture.Add("voice.wav"); await fixture.Add("pending.mp3.partial");
        var first = await fixture.Service.DiscoverAsync(fixture.Folder, default);
        var second = await fixture.Service.DiscoverAsync(fixture.Folder + Path.DirectorySeparatorChar, default);
        Assert.Equal(3, first.Count); Assert.Equal(first.Select(e => e.Id).Order(), second.Select(e => e.Id).Order());
        Assert.All(first, e => { Assert.Equal(TimeSpan.FromSeconds(3), e.Duration); Assert.Equal(32, e.Bytes); Assert.False(e.IsMissing); });
        Assert.Equal(3, fixture.Metadata.Calls);
        var reopened = new SqliteRecordingRepository(fixture.Database);
        Assert.Equal(3, (await reopened.GetFolderAsync(fixture.Folder, default)).Count);
    }
    [Theory]
    [InlineData("CaptureFlow_Screen_test.mp4", RecordingType.Screen)] [InlineData("CaptureFlow_Webcam_test.mp4", RecordingType.Webcam)]
    [InlineData("CaptureFlow_ScreenWebcam_test.mp4", RecordingType.ScreenAndWebcam)] [InlineData("imported.mp4", RecordingType.OtherVideo)]
    [InlineData("a.mp3", RecordingType.Audio)] [InlineData("b.wav", RecordingType.Audio)]
    public void ImportedTypesAreInferredConservatively(string path, RecordingType type) => Assert.Equal(type, RecordingLibraryQuery.InferType(path));
    [Theory]
    [InlineData("All Recordings", 5)] [InlineData("Screen Recordings", 1)] [InlineData("Webcam Recordings", 1)]
    [InlineData("Screen + Webcam", 1)] [InlineData("Audio Recordings", 1)]
    public void FiltersAndCaseInsensitiveFilenameSearch(string filter, int count)
    {
        var entries = Enum.GetValues<RecordingType>().Select((type, i) => Entry("Example" + i + ".mp4", type, i));
        Assert.Equal(count, RecordingLibraryQuery.Apply(entries, "eXamPle", filter, "Filename").Count());
        Assert.Empty(RecordingLibraryQuery.Apply(entries, "unmatched", filter, "Filename"));
    }
    [Theory]
    [InlineData("Newest first", "b.mp4")] [InlineData("Oldest first", "a.mp4")] [InlineData("Filename", "a.mp4")]
    [InlineData("Duration", "b.mp4")] [InlineData("File size", "b.mp4")]
    public void SortsUseNumericMetadata(string sort, string expected)
    {
        var entries = new[] { Entry("b.mp4", RecordingType.Screen, 2), Entry("a.mp4", RecordingType.Webcam, 1) };
        Assert.Equal(expected, RecordingLibraryQuery.Apply(entries, "", "All Recordings", sort).First().Name);
    }
    [Fact]
    public async Task RenamePreservesIdentityTypeAndFilesystemAndDatabaseAgree()
    {
        using var fixture = new Fixture(); await fixture.Add("CaptureFlow_Webcam_test.mp4"); var original = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
        var renamed = await fixture.Service.RenameAsync(original, "a new name.mp4", default);
        Assert.False(File.Exists(original.FullPath)); Assert.True(File.Exists(renamed.FullPath)); Assert.Equal(original.Id, renamed.Id);
        var rediscovered = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
        Assert.Equal(renamed.FullPath, rediscovered.FullPath); Assert.Equal(RecordingType.Webcam, rediscovered.Type);
        Assert.Empty(await fixture.Repository.GetRenamesAsync(default));
    }
    [Theory]
    [InlineData("../escape.mp4")] [InlineData("changed.wav")] [InlineData("CON.mp4")] [InlineData("bad?.mp4")] [InlineData("")]
    public async Task UnsafeNamesAreRejectedWithoutMovingMedia(string name)
    {
        using var fixture = new Fixture(); await fixture.Add("source.mp4"); var original = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.RenameAsync(original, name, default)); Assert.True(File.Exists(original.FullPath));
    }
    [Fact]
    public async Task RenameCollisionAndChangedFileNeverOverwriteFiles()
    {
        using var fixture = new Fixture(); await fixture.Add("source.mp4"); await fixture.Add("target.mp4"); var source = (await fixture.Service.DiscoverAsync(fixture.Folder, default)).Single(e => e.Name == "source.mp4");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.RenameAsync(source, "target.mp4", default));
        await File.AppendAllTextAsync(source.FullPath, "externally changed");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.RenameAsync(source, "third.mp4", default));
        Assert.Equal(32, new FileInfo(Path.Combine(fixture.Folder, "target.mp4")).Length);
    }
    [Fact]
    public async Task RenameDatabaseFailureRollsBackFilesystem()
    {
        using var fixture = new Fixture(); await fixture.Add("source.mp4"); var entry = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
        var service = new RecordingLibraryService(new FailingRepository(fixture.Repository), fixture.Metadata, fixture.Actions);
        await Assert.ThrowsAsync<IOException>(() => service.RenameAsync(entry, "renamed.mp4", default));
        Assert.True(File.Exists(entry.FullPath)); Assert.False(File.Exists(Path.Combine(fixture.Folder, "renamed.mp4"))); Assert.Empty(await fixture.Repository.GetRenamesAsync(default));
    }
    [Fact]
    public async Task PendingRenameAfterCrashRecoversWithoutDuplicateOrLostType()
    {
        using var fixture = new Fixture(); await fixture.Add("CaptureFlow_Screen_test.mp4"); var entry = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
        var target = Path.Combine(fixture.Folder, "renamed.mp4"); await fixture.Repository.BeginRenameAsync(new(entry.Id, entry.FullPath, target), default); File.Move(entry.FullPath, target);
        var recovered = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
        Assert.Equal(entry.Id, recovered.Id); Assert.Equal(target, recovered.FullPath); Assert.Equal(RecordingType.Screen, recovered.Type);
    }
    [Fact]
    public async Task DeleteRequiresConfirmationAndUsesInjectedRecycleAction()
    {
        using var fixture = new Fixture(); await fixture.Add("audio.wav"); var entry = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
        Assert.False(await fixture.Service.DeleteAsync(entry, false, default)); Assert.True(File.Exists(entry.FullPath)); Assert.Equal(0, fixture.Actions.Recycles);
        Assert.True(await fixture.Service.DeleteAsync(entry, true, default)); Assert.Equal(1, fixture.Actions.Recycles);
        Assert.True(File.Exists(Path.Combine(fixture.Recycled, entry.Name))); Assert.Empty(await fixture.Repository.GetFolderAsync(fixture.Folder, default));
    }
    [Fact]
    public async Task ExternallyMissingFilesStayVisibleAndAreNeverAutomaticallyDeleted()
    {
        using var fixture = new Fixture(); await fixture.Add("audio.wav"); var entry = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
        File.Move(entry.FullPath, Path.Combine(fixture.Recycled, "moved.wav"));
        var missing = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default)); Assert.True(missing.IsMissing); Assert.Equal(entry.Id, missing.Id); Assert.Equal(0, fixture.Actions.Recycles);
        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.Service.RenameAsync(missing, "new.wav", default));
    }
    [Fact]
    public async Task CancellationAndInvalidDatabaseAreReportedWithoutChangingMedia()
    {
        using var fixture = new Fixture(); await fixture.Add("audio.wav");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.DiscoverAsync(fixture.Folder, cancellation.Token));
        var bad = Path.Combine(fixture.Root, "not-a-db"); await File.WriteAllTextAsync(bad, "not sqlite");
        var service = new RecordingLibraryService(new SqliteRecordingRepository(bad), fixture.Metadata, fixture.Actions);
        await Assert.ThrowsAnyAsync<Exception>(() => service.DiscoverAsync(fixture.Folder, default)); Assert.True(File.Exists(Path.Combine(fixture.Folder, "audio.wav")));
    }
    [Fact]
    public async Task DeleteDatabaseFailureRetainsRecoverableFileAndRefreshMarksMissing()
    {
        using var fixture = new Fixture(); await fixture.Add("audio.wav"); var entry = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
        var service = new RecordingLibraryService(new FailingRepository(fixture.Repository, true), fixture.Metadata, fixture.Actions);
        await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(entry, true, default));
        Assert.True(File.Exists(Path.Combine(fixture.Recycled, "audio.wav")));
        Assert.True(Assert.Single(await service.DiscoverAsync(fixture.Folder, default)).IsMissing);
    }
    [Fact]
    public async Task AmbiguousCrashRecoveryNeverOverwritesEitherFile()
    {
        using var fixture = new Fixture(); await fixture.Add("source.mp4"); var entry = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
        await fixture.Add("target.mp4"); var target = Path.Combine(fixture.Folder, "target.mp4");
        await fixture.Repository.BeginRenameAsync(new(entry.Id, entry.FullPath, target), default);
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.DiscoverAsync(fixture.Folder, default));
        Assert.True(File.Exists(entry.FullPath)); Assert.True(File.Exists(target)); Assert.Single(await fixture.Repository.GetRenamesAsync(default));
    }
    [Fact]
    public async Task CancelDuringMetadataReadReleasesDiscoveryGate()
    {
        using var fixture = new Fixture(); await fixture.Add("source.mp4"); fixture.Metadata.Block = true;
        using var cancellation = new CancellationTokenSource(); var load = fixture.Service.DiscoverAsync(fixture.Folder, cancellation.Token);
        await fixture.Metadata.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        fixture.Metadata.Block = false; Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
    }
    [Fact]
    public async Task DeleteRejectsExternalReplacementEvenAfterConfirmation()
    {
        using var fixture = new Fixture(); await fixture.Add("source.wav"); var entry = Assert.Single(await fixture.Service.DiscoverAsync(fixture.Folder, default));
        await File.AppendAllTextAsync(entry.FullPath, "replacement");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.DeleteAsync(entry, true, default));
        Assert.Equal(0, fixture.Actions.Recycles); Assert.True(File.Exists(entry.FullPath));
    }
    [Fact]
    public void PlaybackStatesSeekingVolumeErrorsAndCleanup()
    {
        var player = new PlaybackSession(); player.Play(); Assert.Equal(PlaybackState.Empty, player.State);
        player.Load(); Assert.Equal(PlaybackState.Loading, player.State); player.Opened(TimeSpan.FromSeconds(10)); player.Play(); Assert.Equal(PlaybackState.Playing, player.State);
        player.Seek(TimeSpan.FromSeconds(20)); Assert.Equal(TimeSpan.FromSeconds(10), player.Position);
        player.Pause(); Assert.Equal(PlaybackState.Paused, player.State); player.Play(); player.Stop(); Assert.Equal(TimeSpan.Zero, player.Position);
        player.Play(); player.End(); Assert.Equal(PlaybackState.Ended, player.State); player.Play(); Assert.Equal(TimeSpan.Zero, player.Position);
        player.Stop(); player.End(); Assert.Equal(PlaybackState.Stopped, player.State);
        player.SetVolume(2); Assert.Equal(1, player.Volume); player.Muted = true; Assert.True(player.Muted); Assert.Throws<ArgumentException>(() => player.SetVolume(double.NaN));
        player.Fail("corrupt"); Assert.Equal(PlaybackState.Error, player.State); player.Close(); Assert.Null(player.Error); Assert.Equal(PlaybackState.Empty, player.State);
    }
    private static RecordingEntry Entry(string name, RecordingType type, int value) => new(Guid.NewGuid(), name, "folder", type, "MP4", TimeSpan.FromSeconds(value), value * 100, DateTimeOffset.UnixEpoch.AddSeconds(value), DateTimeOffset.UnixEpoch);
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "CaptureFlow library tests", Guid.NewGuid().ToString("N"));
        public string Folder => Path.Combine(Root, "media"); public string Recycled => Path.Combine(Root, "simulated recycle"); public string Database => Path.Combine(Root, "library.db");
        public SqliteRecordingRepository Repository { get; } public Metadata Metadata { get; } = new(); public Actions Actions { get; } public RecordingLibraryService Service { get; }
        public Fixture() { Directory.CreateDirectory(Folder); Directory.CreateDirectory(Recycled); Repository = new(Database); Actions = new(Recycled); Service = new(Repository, Metadata, Actions); }
        public Task Add(string name) => File.WriteAllBytesAsync(Path.Combine(Folder, name), new byte[32]); // Catalog fixtures, not playable recordings.
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class Metadata : IMediaMetadataReader
    {
        public int Calls; public bool Block;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<TimeSpan?> ReadDurationAsync(string path, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; Entered.TrySetResult(); if (Block) await Task.Delay(Timeout.Infinite, token); return TimeSpan.FromSeconds(3); }
    }
    private sealed class Actions(string recycle) : IRecordingFileActions
    {
        public int Recycles; public void Rename(string from, string to) => File.Move(from, to, false);
        public Task RecycleAsync(string path, CancellationToken token) { token.ThrowIfCancellationRequested(); Recycles++; File.Move(path, Path.Combine(recycle, Path.GetFileName(path)), false); return Task.CompletedTask; }
    }
    private sealed class FailingRepository(IRecordingRepository inner, bool failRemove = false) : IRecordingRepository
    {
        public Task<IReadOnlyList<RecordingEntry>> GetFolderAsync(string folder, CancellationToken token) => inner.GetFolderAsync(folder, token);
        public Task SaveAsync(IReadOnlyList<RecordingEntry> entries, CancellationToken token) => inner.SaveAsync(entries, token);
        public Task BeginRenameAsync(PendingRename rename, CancellationToken token) => inner.BeginRenameAsync(rename, token);
        public Task CompleteRenameAsync(RecordingEntry entry, CancellationToken token) => failRemove ? inner.CompleteRenameAsync(entry, token) : Task.FromException(new IOException("database update failed"));
        public Task CancelRenameAsync(Guid id, CancellationToken token) => inner.CancelRenameAsync(id, token);
        public Task<IReadOnlyList<PendingRename>> GetRenamesAsync(CancellationToken token) => inner.GetRenamesAsync(token);
        public Task RemoveAsync(Guid id, CancellationToken token) => failRemove ? Task.FromException(new IOException("database remove failed")) : inner.RemoveAsync(id, token);
    }
}
