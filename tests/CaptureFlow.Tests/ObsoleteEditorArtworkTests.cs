using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureFlow.Core.Editing;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Tests;
public partial class EditorViewModelTests
{
    [Fact] public Task LateProbeCannotImportIntoAReplacedProject() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var backend = new PendingProbe(); await using var vm = new EditorViewModel(backend, new Store());
        var pending = vm.ImportFileAsync(EditorTimelineTests.Media().Path, true); await backend.Entered.Task;
        var replacement = EditorProject.Empty; vm.ResetProject(replacement); backend.Result.SetResult(EditorTimelineTests.Media()); await pending;
        Assert.Same(replacement, vm.Project); Assert.Empty(vm.Media); Assert.Equal(0, backend.Analyses); Assert.Contains("project changed", vm.Message); Assert.False(vm.IsBusy);
    });
    [Fact] public Task ObsoleteArtworkCompletionReleasesOwnedFilesWithoutWaitingForShutdown() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var scratch = new RecoveryTests.Scratch(); var source = Path.Combine(scratch.Path, "source.mp4"); await File.WriteAllBytesAsync(source, new byte[32]); var info = new FileInfo(source);
        var root = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-artwork"); Directory.CreateDirectory(root); var owned = Path.Combine(root, Guid.NewGuid().ToString("N") + ".jpg");
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr24, null, new byte[12], 6); var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(owned)) encoder.Save(file);
        var backend = new PendingArtwork(new(Guid.NewGuid(), info.FullName, "MP4", 1, 16, 16, true, true, info.Length, info.LastWriteTimeUtc.Ticks));
        await using var vm = new EditorViewModel(backend, new Store()); var import = vm.ImportFileAsync(source); await backend.Entered.Task;
        vm.ResetProject(EditorProject.Empty); backend.Result.SetResult(new(owned, [.2f])); await import;
        Assert.Empty(vm.Media); Assert.Empty(vm.Project.Media); Assert.False(File.Exists(owned)); Assert.True(File.Exists(source));
    });
    private sealed class PendingArtwork(EditorMedia media) : IEditorMediaBackend
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<EditorAnalysis> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<EditorMedia> ProbeAsync(string path, string ffmpeg, CancellationToken token) => Task.FromResult(media);
        public Task<EditorAnalysis> AnalyzeAsync(EditorMedia asset, string ffmpeg, CancellationToken token) { Entered.SetResult(); return Result.Task; }
        public Task<EditorRange[]> DetectSilenceAsync(EditorMedia asset, double thresholdDb, double minimumSeconds, string ffmpeg, CancellationToken token) => throw new NotSupportedException();
        public Task<string> RenderAsync(EditorProject project, EditorExportSettings settings, string outputFolder, string ffmpeg, IProgress<double>? progress, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class PendingProbe : IEditorMediaBackend
    {
        public int Analyses; public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<EditorMedia> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<EditorMedia> ProbeAsync(string path, string ffmpeg, CancellationToken token) { Entered.SetResult(); return Result.Task; }
        public Task<EditorAnalysis> AnalyzeAsync(EditorMedia asset, string ffmpeg, CancellationToken token) { Analyses++; return Task.FromResult(new EditorAnalysis(null, [])); }
        public Task<EditorRange[]> DetectSilenceAsync(EditorMedia asset, double thresholdDb, double minimumSeconds, string ffmpeg, CancellationToken token) => throw new NotSupportedException();
        public Task<string> RenderAsync(EditorProject project, EditorExportSettings settings, string outputFolder, string ffmpeg, IProgress<double>? progress, CancellationToken token) => throw new NotSupportedException();
    }
}
