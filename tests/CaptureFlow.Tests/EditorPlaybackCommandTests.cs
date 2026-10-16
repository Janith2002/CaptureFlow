using CaptureFlow.Application.Library;
using CaptureFlow.Core.Editing;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Tests;

public class EditorPlaybackCommandTests
{
    [Theory]
    [InlineData(PlaybackState.Empty, true, false, false)]
    [InlineData(PlaybackState.Loading, false, false, false)]
    [InlineData(PlaybackState.Stopped, true, false, false)]
    [InlineData(PlaybackState.Playing, false, true, true)]
    [InlineData(PlaybackState.Paused, true, false, true)]
    [InlineData(PlaybackState.Ended, true, false, true)]
    [InlineData(PlaybackState.Error, true, false, false)]
    public Task ReadyTimelineTransportMatrix(PlaybackState state, bool play, bool pause, bool stop) => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var fixture = new Fixture(); await using var vm = fixture.Create();
        await vm.ImportFileAsync("generated", true); await vm.PreviewCommand.ExecuteAsync(null);
        vm.UpdatePreviewPlaybackState(state, "test decode error");
        Assert.Equal(play, vm.PlayPreviewCommand.CanExecute(null));
        Assert.Equal(pause, vm.PausePreviewCommand.CanExecute(null));
        Assert.Equal(stop, vm.StopPreviewCommand.CanExecute(null));
        vm.SelectedClip = null; Assert.Equal(play, vm.PlayPreviewCommand.CanExecute(null)); // Playback is timeline-wide, not selection-dependent.
    });

    [Fact] public Task EmptyAndBinOnlyStayDisabledAndTimelineAdditionRaisesAvailability() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var fixture = new Fixture(); await using var vm = fixture.Create();
        Assert.False(vm.PlayPreviewCommand.CanExecute(null)); Assert.Contains("Add a clip", vm.PreviewPlaybackHint);
        await vm.ImportFileAsync("generated"); Assert.False(vm.PlayPreviewCommand.CanExecute(null));
        var notifications = 0; vm.PlayPreviewCommand.CanExecuteChanged += (_, _) => notifications++;
        vm.AddClipCommand.Execute(null);
        Assert.True(vm.PlayPreviewCommand.CanExecute(null)); Assert.True(notifications > 0);
        Assert.False(vm.PausePreviewCommand.CanExecute(null)); Assert.False(vm.StopPreviewCommand.CanExecute(null));
    });

    [Fact] public Task PlayRendersStaleTimelineAndBusyOrNativeLoadingPreventsRepeatStart() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var fixture = new Fixture(); fixture.Backend.Gate = new(); await using var vm = fixture.Create();
        await vm.ImportFileAsync("generated", true);
        var opens = 0; vm.PreviewRequested += _ => { opens++; vm.UpdatePreviewPlaybackState(PlaybackState.Loading); };
        var pending = vm.PlayPreviewCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy); Assert.False(vm.PlayPreviewCommand.CanExecute(null));
        fixture.Backend.Gate.SetResult(); await pending;
        Assert.Equal(1, opens); Assert.False(vm.PreviewStale); Assert.False(vm.PlayPreviewCommand.CanExecute(null));
        vm.UpdatePreviewPlaybackState(PlaybackState.Playing); Assert.True(vm.PausePreviewCommand.CanExecute(null));
    });

    [Fact] public Task PauseResumeStopAndErrorRetryUseTheAttachedTransportWithoutRerendering() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var fixture = new Fixture(); await using var vm = fixture.Create(); await vm.ImportFileAsync("generated", true);
        var starts = 0; vm.PreviewRequested += _ => { starts++; vm.UpdatePreviewPlaybackState(PlaybackState.Playing); };
        vm.PausePreviewRequested += () => vm.UpdatePreviewPlaybackState(PlaybackState.Paused);
        vm.PlayPreviewRequested += () => vm.UpdatePreviewPlaybackState(PlaybackState.Playing);
        vm.StopPreviewRequested += () => vm.UpdatePreviewPlaybackState(PlaybackState.Stopped);
        await vm.PlayPreviewCommand.ExecuteAsync(null); vm.PausePreviewCommand.Execute(null);
        Assert.True(vm.PlayPreviewCommand.CanExecute(null)); Assert.False(vm.PausePreviewCommand.CanExecute(null));
        await vm.PlayPreviewCommand.ExecuteAsync(null); Assert.Equal(1, fixture.Backend.Renders);
        vm.Playhead = 1; vm.StopPreviewCommand.Execute(null); Assert.Equal(0, vm.Playhead); Assert.True(vm.PlayPreviewCommand.CanExecute(null));
        vm.UpdatePreviewPlaybackState(PlaybackState.Error, "decoder unavailable"); Assert.Contains("decoder unavailable", vm.Message);
        await vm.PlayPreviewCommand.ExecuteAsync(null); Assert.Equal(2, starts); Assert.Equal(1, fixture.Backend.Renders);
        Assert.DoesNotContain("decoder unavailable", vm.Message);
    });

    [Fact] public Task EditingAndProjectCloseInvalidatePlaybackWithoutLeavingStalePlayingState() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var fixture = new Fixture(); await using var vm = fixture.Create(); await vm.ImportFileAsync("generated", true);
        await vm.PreviewCommand.ExecuteAsync(null); vm.UpdatePreviewPlaybackState(PlaybackState.Playing);
        vm.Playhead = 1; vm.SplitCommand.Execute(null);
        Assert.True(vm.PreviewStale); Assert.Equal(PlaybackState.Empty, vm.PreviewPlaybackState); Assert.True(vm.PlayPreviewCommand.CanExecute(null));
        vm.ResetProject(EditorProject.Empty);
        Assert.False(vm.PlayPreviewCommand.CanExecute(null)); Assert.False(vm.PausePreviewCommand.CanExecute(null)); Assert.False(vm.StopPreviewCommand.CanExecute(null));
    });

    private sealed class Fixture : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-command-test-" + Guid.NewGuid().ToString("N"));
        public Backend Backend { get; }
        public Fixture() { Directory.CreateDirectory(folder); var path = Path.Combine(folder, "preview.mp4"); File.WriteAllText(path, "unit transport marker, not decoded media"); Backend = new(path); }
        public EditorViewModel Create() => new(Backend, new Store());
        public void Dispose() => Directory.Delete(folder, true);
    }
    public sealed class Backend(string preview) : IEditorMediaBackend
    {
        public int Renders; public TaskCompletionSource? Gate;
        public Task<EditorMedia> ProbeAsync(string path, string ffmpeg, CancellationToken token) => Task.FromResult(EditorTimelineTests.Media());
        public Task<EditorAnalysis> AnalyzeAsync(EditorMedia media, string ffmpeg, CancellationToken token) => Task.FromResult(new EditorAnalysis(null, []));
        public Task<EditorRange[]> DetectSilenceAsync(EditorMedia media, double db, double minimum, string ffmpeg, CancellationToken token) => Task.FromResult<EditorRange[]>([]);
        public async Task<string> RenderAsync(EditorProject project, EditorExportSettings settings, string folder, string ffmpeg, IProgress<double>? progress, CancellationToken token)
        { Renders++; if (Gate is not null) await Gate.Task.WaitAsync(token); return preview; }
    }
    private sealed class Store : IEditorProjectStore
    {
        public Task SaveAsync(EditorProject project, string path, CancellationToken token) => Task.CompletedTask;
        public Task<EditorProject> LoadAsync(string path, CancellationToken token) => Task.FromResult(EditorProject.Empty);
    }
}
