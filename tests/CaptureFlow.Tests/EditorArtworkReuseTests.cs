using CaptureFlow.Core.Editing;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Tests;
public partial class EditorViewModelTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public Task NewProjectMediaIdsReuseUnchangedArtworkButChangedSourcesDoNot(bool lengthChange) => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var scratch = new RecoveryTests.Scratch(); var path = Path.Combine(scratch.Path, "source.mp4"); await File.WriteAllBytesAsync(path, new byte[32]);
        var backend = new ReuseBackend(); await using var vm = new EditorViewModel(backend, new Store());
        await vm.ImportFileAsync(path); var first = vm.Media.Single(); Assert.Equal(1, backend.Analyses);
        vm.ResetProject(EditorProject.Empty); await vm.ImportFileAsync(path); var second = vm.Media.Single();
        Assert.NotEqual(first.Media.Id, second.Media.Id); Assert.Equal(1, backend.Analyses); Assert.Same(first.Waveform, second.Waveform);
        var oldStamp = File.GetLastWriteTimeUtc(path);
        if (lengthChange) { await File.WriteAllBytesAsync(path, new byte[64]); File.SetLastWriteTimeUtc(path, oldStamp); }
        else File.SetLastWriteTimeUtc(path, oldStamp.AddSeconds(2));
        vm.ResetProject(EditorProject.Empty); await vm.ImportFileAsync(path); Assert.Equal(2, backend.Analyses); Assert.NotSame(first.Waveform, vm.Media.Single().Waveform);
    });
    private sealed class ReuseBackend : IEditorMediaBackend
    {
        public int Analyses;
        public Task<EditorMedia> ProbeAsync(string path, string ffmpeg, CancellationToken token)
        { var info = new FileInfo(path); return Task.FromResult(new EditorMedia(Guid.NewGuid(), info.FullName, "MP4", 10, 16, 16, true, true, info.Length, info.LastWriteTimeUtc.Ticks)); }
        public Task<EditorAnalysis> AnalyzeAsync(EditorMedia media, string ffmpeg, CancellationToken token) { Analyses++; return Task.FromResult(new EditorAnalysis(null, [.1f, .5f])); }
        public Task<EditorRange[]> DetectSilenceAsync(EditorMedia media, double thresholdDb, double minimumSeconds, string ffmpeg, CancellationToken token) => throw new NotSupportedException();
        public Task<string> RenderAsync(EditorProject project, EditorExportSettings settings, string outputFolder, string ffmpeg, IProgress<double>? progress, CancellationToken token) => throw new NotSupportedException();
    }
}
