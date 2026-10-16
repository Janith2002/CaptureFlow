using CaptureFlow.Core.Editing;
using CaptureFlow.Application.Editing;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Tests;
public partial class EditorViewModelTests
{
    [Fact] public Task MediaDropSnapsAndRejectsLockedOrIncompatibleTargetsAtomically() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var backend = new Backend(); await using var vm = new EditorViewModel(backend, new Store()); await vm.ImportFileAsync(backend.Media.Path, true);
        var project = vm.Project; var media = project.Media.Single(); Assert.Equal(10, vm.ProposeDrop(media.Id, 0, 9.9).Start);
        vm.SnappingEnabled = false; Assert.Throws<ArgumentException>(() => vm.ProposeDrop(media.Id, 0, 9.9)); Assert.Same(project, vm.Project);
        vm.SnappingEnabled = true; vm.DropMedia(media.Id, 0, 9.9); Assert.Equal(2, vm.Project.Clips.Length); vm.UndoCommand.Execute(null); Assert.Single(vm.Project.Clips);
        vm.ToggleTrackLockCommand.Execute("0"); Assert.Throws<InvalidOperationException>(() => vm.ProposeDrop(media.Id, 0, 20));
    });
    [Fact] public Task ObsoletePreviewCannotPublishAfterProjectReset() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var backend = new Backend { Controlled = true }; await using var vm = new EditorViewModel(backend, new Store()); await vm.ImportFileAsync(backend.Media.Path, true);
        var published = false; vm.PreviewRequested += _ => published = true; var pending = vm.PreviewCommand.ExecuteAsync(null); await backend.Entered.Task;
        vm.ResetProject(EditorProject.Empty); backend.Release.SetResult(); await pending; Assert.False(published); Assert.Null(vm.PreviewPath); Assert.True(vm.PreviewStale); Assert.Empty(vm.Project.Clips);
    });
    [Fact] public Task MediaSearchSelectionTrackMuteLockAndSnappingUseExistingCommands() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var backend = new Backend(); await using var vm = new EditorViewModel(backend, new Store()); await vm.ImportFileAsync(backend.Media.Path, true);
        vm.MediaSearch = "no matching filename"; Assert.Empty(vm.FilteredMedia.Cast<object>()); vm.MediaSearch = "synthetic"; Assert.Single(vm.FilteredMedia.Cast<object>());
        vm.ToggleTrackMuteCommand.Execute("0"); Assert.True(vm.Project.Tracks[0].Muted); vm.ToggleTrackLockCommand.Execute("0"); Assert.False(vm.HasSelection); Assert.False(vm.DeleteCommand.CanExecute(null));
        vm.ToggleTrackLockCommand.Execute("0"); var clip = vm.Project.Clips.Single(); vm.SnappingEnabled = false; vm.CompleteGesture(vm.ProposeGesture(clip, TimelineGesture.Move, 1, 0)); Assert.Equal(1, vm.Project.Clips.Single().Start); vm.UndoCommand.Execute(null); Assert.Equal(0, vm.Project.Clips.Single().Start);
    });
    [Fact] public Task ShutdownAutosavesDirtyProjectAndRestoreRetainsUndoWithoutWritingExplicitProject() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new CaptureFlow.Infrastructure.Recovery.JsonRecoveryJournal(scratch.Path);
        var backend = new Backend(); var vm = new EditorViewModel(backend, new Store(), journal);
        await vm.ImportFileAsync(backend.Media.Path, true); var original = vm.Project;
        await vm.DisposeAsync(); var candidate = Assert.Single(await journal.ScanAsync());
        var snapshot = await journal.LoadEditorAsync(candidate.Record.Id);
        await using var restored = new EditorViewModel(backend, new Store(), journal);
        restored.RestoreAutosave(snapshot, candidate.Record.Id);
        Assert.Equal(original.Id, restored.Project.Id); Assert.Single(restored.Media); Assert.True(restored.Session.Dirty); Assert.True(restored.CanUndo);
        Assert.Contains("missing", restored.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(scratch.Path, "*.cfedit", SearchOption.AllDirectories));
    });
    [Fact] public Task DisabledAutosaveLeavesNoSnapshotOnShutdown() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new CaptureFlow.Infrastructure.Recovery.JsonRecoveryJournal(scratch.Path);
        await journal.SavePreferencesAsync(journal.Preferences with { EditorAutosaveEnabled = false });
        var backend = new Backend(); var vm = new EditorViewModel(backend, new Store(), journal); await vm.ImportFileAsync(backend.Media.Path, true);
        await vm.DisposeAsync(); Assert.Empty(await journal.ScanAsync());
    });
    [Fact] public Task UndoRedoImportReconcilesMediaPanelWithProject() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var backend = new Backend(); await using var vm = new EditorViewModel(backend, new Store());
        await vm.ImportFileAsync(backend.Media.Path); Assert.Single(vm.Media);
        vm.UndoCommand.Execute(null); Assert.Empty(vm.Project.Media); Assert.Empty(vm.Media); Assert.Null(vm.SelectedMedia);
        vm.RedoCommand.Execute(null); Assert.Single(vm.Project.Media); Assert.Single(vm.Media);
    });
    [Fact] public Task ImportCommandsCreateOneClipAndUndoRedoKeepOriginalSourceReference() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var backend = new Backend(); await using var vm = new EditorViewModel(backend, new Store());
        await vm.ImportFileAsync(backend.Media.Path, true); Assert.Single(vm.Project.Clips); Assert.NotNull(vm.SelectedClip); Assert.True(vm.HasSelection);
        var original = vm.Project.Media.Single(); vm.TrimIn = 1; vm.TrimOut = 4; vm.ClipVolume = .5; vm.FadeIn = .2;
        vm.ApplyClipCommand.Execute(null); Assert.Equal(3, vm.Project.Duration); Assert.Equal(original, vm.Project.Media.Single()); Assert.True(vm.PreviewStale);
        vm.Playhead = 1; vm.SplitCommand.Execute(null); Assert.Equal(2, vm.Project.Clips.Length);
        vm.UndoCommand.Execute(null); Assert.Single(vm.Project.Clips); vm.RedoCommand.Execute(null); Assert.Equal(2, vm.Project.Clips.Length);
        await vm.PreviewCommand.ExecuteAsync(null); Assert.False(vm.PreviewStale); Assert.Equal("MP4", backend.Settings!.Format);
        vm.CopyCommand.Execute(null); vm.Playhead = vm.Project.Duration + 1; vm.PasteCommand.Execute(null); Assert.Equal(3, vm.Project.Clips.Length);
    });
    [Fact] public Task FailedImportAndCancelledExportReleaseBusyStateAndNeverSignalCompletion() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var backend = new Backend(); await using var vm = new EditorViewModel(backend, new Store()) { OutputFolder = Path.GetTempPath() };
        backend.Fail = true; await vm.ImportFileAsync(backend.Media.Path); Assert.False(vm.IsBusy); Assert.Contains("fixture failure", vm.Message);
        backend.Fail = false; await vm.ImportFileAsync(backend.Media.Path, true); backend.Block = true; var completed = false; vm.Exported += _ => completed = true;
        var run = vm.ExportCommand.ExecuteAsync(null); Assert.True(vm.IsBusy); Assert.False(vm.ApplyClipCommand.CanExecute(null)); Assert.False(vm.ExportCommand.CanExecute(null));
        vm.CancelCommand.Execute(null); await run; Assert.False(vm.IsBusy); Assert.False(completed); Assert.Equal(1, backend.Cleanups); Assert.Contains("Cancelled", vm.Message);
    });
    [Fact] public Task ShutdownCancelsAndAwaitsOwnedRender() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var backend = new Backend(); var vm = new EditorViewModel(backend, new Store()); await vm.ImportFileAsync(backend.Media.Path, true);
        backend.Block = true; var run = vm.PreviewCommand.ExecuteAsync(null); await vm.DisposeAsync(); await run;
        Assert.Equal(1, backend.Cleanups); Assert.False(vm.CanEdit);
    });
    private sealed class Backend : IEditorMediaBackend
    {
        public bool Controlled; public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public EditorMedia Media { get; } = EditorTimelineTests.Media(); public bool Fail, Block; public int Cleanups; public EditorExportSettings? Settings;
        public Task<EditorMedia> ProbeAsync(string path, string ffmpeg, CancellationToken token) => Fail ? throw new IOException("fixture failure") : Task.FromResult(Media);
        public Task<EditorAnalysis> AnalyzeAsync(EditorMedia media, string ffmpeg, CancellationToken token) => Task.FromResult(new EditorAnalysis(null, []));
        public Task<EditorRange[]> DetectSilenceAsync(EditorMedia media, double db, double minimum, string ffmpeg, CancellationToken token) => Task.FromResult<EditorRange[]>([]);
        public async Task<string> RenderAsync(EditorProject project, EditorExportSettings settings, string folder, string ffmpeg, IProgress<double>? progress, CancellationToken token)
        { Settings = settings; try { if (Controlled) { Entered.SetResult(); await Release.Task; } if (Block) await Task.Delay(Timeout.Infinite, token); return Path.GetFullPath("synthetic-preview.mp4"); } finally { Cleanups++; } }
    }
    private sealed class Store : IEditorProjectStore
    {
        public Task SaveAsync(EditorProject project, string path, CancellationToken token) => Task.CompletedTask;
        public Task<EditorProject> LoadAsync(string path, CancellationToken token) => Task.FromResult(EditorProject.Empty);
    }
}

