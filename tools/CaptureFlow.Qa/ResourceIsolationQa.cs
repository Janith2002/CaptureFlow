using System.IO;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Infrastructure.Editing;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Application.Library;
using CaptureFlow.Core.Editing;
using System.Windows;
using System.Windows.Controls;
namespace CaptureFlow.Qa;
internal static class ResourceIsolationQa
{
    public static async Task RunAsync(EditorViewModel vm, string source, LibraryMediaPlayer player)
    {
        var snapshot = vm.Project;
        vm.ResetProject(snapshot); await Task.Delay(500); ResourceSnapshot.Log("empty-release-baseline");
        for (var i = 0; i < 24; i++) { vm.ResetProject(snapshot); await Task.Delay(50); if (i % 6 == 5) ResourceSnapshot.Log("empty-release-" + (i + 1)); }
        await Task.Delay(500); ResourceSnapshot.Log("empty-release-settled");
        var backend = new FfmpegEditorBackend();
        ResourceSnapshot.Log("probe-only-start");
        for (var i = 0; i < 24; i++) await backend.ProbeAsync(source, "ffmpeg.exe", default);
        await Task.Delay(500); ResourceSnapshot.Log("probe-only-end");
        ResourceSnapshot.Log("artwork-only-start");
        var media = await backend.ProbeAsync(source, "ffmpeg.exe", default);
        for (var i = 0; i < 6; i++)
        {
            var analysis = await backend.AnalyzeAsync(media, "ffmpeg.exe", default);
            // Only paths returned by this owned generated-source analysis, never shared/user media.
            foreach (var path in new[] { analysis.Thumbnail, analysis.Filmstrip }.OfType<string>()) File.Delete(path);
        }
        await Task.Delay(500); ResourceSnapshot.Log("artwork-only-end");
        ResourceSnapshot.Log("render-only-start");
        for (var i = 0; i < 4; i++) await backend.RenderAsync(snapshot, new EditorExportSettings("MP4", 640, 360), Path.GetDirectoryName(source)!, "ffmpeg.exe", null, default);
        await Task.Delay(500); ResourceSnapshot.Log("render-only-end");
        ResourceSnapshot.Log("playback-only-start");
        for (var i = 0; i < 8; i++)
        {
            player.Open(source, false);
            for (var wait = 0; wait < 100 && player.Session.State == PlaybackState.Loading; wait++) await Task.Delay(50);
            if (player.Session.State == PlaybackState.Error) throw new InvalidOperationException(player.Session.Error);
            player.SeekTo(TimeSpan.FromSeconds(.3)); player.CloseMedia(); await Task.Delay(150);
            ResourceSnapshot.Log("playback-only-" + (i + 1));
        }
        await Task.Delay(5000); ResourceSnapshot.Log("playback-settled-5s");
        await Task.Delay(10000); ResourceSnapshot.Log("playback-settled-15s");
        foreach (var order in new[] { "close-only", "source-before-close" })
        {
            var raw = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Volume = 0 };
            ResourceSnapshot.Log("raw-" + order + "-start");
            for (var i = 0; i < 4; i++)
            {
                var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                RoutedEventHandler onOpen = (_, _) => opened.TrySetResult();
                EventHandler<ExceptionRoutedEventArgs> onError = (_, e) => opened.TrySetException(e.ErrorException);
                raw.MediaOpened += onOpen; raw.MediaFailed += onError;
                try { raw.Source = new Uri(source); raw.Play(); await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { raw.MediaOpened -= onOpen; raw.MediaFailed -= onError; }
                if (order == "source-before-close") raw.Source = null;
                raw.Close(); await Task.Delay(150);
            }
            raw.Close(); raw.Source = null; await Task.Delay(2000); ResourceSnapshot.Log("raw-" + order + "-end");
        }
        await vm.ImportFileAsync(source); vm.SelectedClip = vm.Project.Clips.First();
    }
}
