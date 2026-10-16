using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CaptureFlow.Application.Library;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Desktop.Views;

// Generated QA fixtures only. Never opens capture or modifies existing recordings.
internal static class LibraryUiQa
{
    public static async Task PrepareAsync()
    {
        var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine("artifacts/qa/library-ui", Guid.NewGuid().ToString("N")));
        System.IO.Directory.CreateDirectory(folder); Environment.SetEnvironmentVariable("CAPTUREFLOW_LIBRARY_INITIAL_FOLDER", folder);
        foreach (var format in new[] { "mp4", "mp3", "wav" })
        {
            var info = new ProcessStartInfo("ffmpeg.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            var args = format == "mp4" ? new[] { "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=30:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p" }
                : new[] { "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:a", format == "mp3" ? "libmp3lame" : "pcm_s16le" };
            foreach (var arg in new[] { "-v", "error", "-nostdin", "-n" }.Concat(args).Append(System.IO.Path.Combine(folder, "generated-qa." + format))) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!; var errors = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); if (process.ExitCode != 0) throw new InvalidOperationException(await errors); }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        }
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(folder, "corrupt-qa.mp4"), "Generated corrupt fixture for error handling.");
    }
    public static int Run()
    {
        var app = new App(); app.InitializeComponent();
        var bindingErrors = new BindingErrors(); PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var failed = false; var started = false;
        app.DispatcherUnhandledException += (_, e) => { Console.WriteLine("FAIL unhandled: " + e.Exception); failed = true; e.Handled = true; app.Shutdown(1); };
        var startup = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        startup.Tick += async (_, _) =>
        {
            if (started || app.MainWindow is not MainWindow window || window.DataContext is not DashboardViewModel dashboard) return;
            started = true; startup.Stop();
            try
            {
                dashboard.OutputFolder = Environment.GetEnvironmentVariable("CAPTUREFLOW_LIBRARY_INITIAL_FOLDER")!;
                dashboard.NavigateCommand.Execute("Recordings");
                try { await WaitAsync(() => !dashboard.IsLibraryBusy && dashboard.Recordings.Count >= 4); }
                catch (Exception ex) { throw new InvalidOperationException($"Catalog status: {dashboard.LibraryStatus}; rows={dashboard.Recordings.Count}; busy={dashboard.IsLibraryBusy}", ex); }
                var control = Descendants(window).OfType<RecordingsLibrary>().Single();
                var player = Descendants(control).OfType<LibraryMediaPlayer>().Single();
                Console.WriteLine("PASS catalog loaded: " + dashboard.Recordings.Count);
                foreach (var format in new[] { "MP4", "MP3", "WAV" })
                {
                    var entry = dashboard.Recordings.First(e => e.Format == format && !e.Name.StartsWith("corrupt"));
                    dashboard.SelectedRecording = entry; dashboard.PlayRecordingCommand.Execute(null);
                    await WaitAsync(() => player.Session.State is PlaybackState.Playing or PlaybackState.Error);
                    if (player.Session.State == PlaybackState.Error) throw new InvalidOperationException(player.Session.Error);
                    player.Pause(); if (player.Session.State != PlaybackState.Paused) throw new InvalidOperationException("Pause failed");
                    if (format == "MP4")
                    {
                        player.ToggleFullscreen(); await Task.Delay(100); if (!player.IsFullscreen) throw new InvalidOperationException("Fullscreen did not open");
                        player.ToggleFullscreen(); if (player.IsFullscreen) throw new InvalidOperationException("Fullscreen did not restore player");
                        Console.WriteLine("PASS fullscreen entry/exit on Windows");
                    }
                    player.SeekTo(TimeSpan.FromMilliseconds(400)); player.Play();
                    await WaitAsync(() => player.Session.State is PlaybackState.Ended or PlaybackState.Error);
                    if (player.Session.State != PlaybackState.Ended) throw new InvalidOperationException(player.Session.Error);
                    Console.WriteLine("PASS built-in " + format + " playback, pause, seek, resume and MediaEnded");
                    player.Stop(); player.CloseMedia();
                    using (System.IO.File.Open(entry.FullPath, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None)) { }
                    Console.WriteLine("PASS released media handle: " + format);
                }
                var corrupt = dashboard.Recordings.First(e => e.Name.StartsWith("corrupt"));
                player.Open(corrupt.FullPath); await WaitAsync(() => player.Session.State == PlaybackState.Error);
                Console.WriteLine("PASS corrupted media reports Error: " + player.Session.Error); player.CloseMedia();
                var video = dashboard.Recordings.First(e => e.Format == "MP4" && !e.Name.StartsWith("corrupt"));
                await WaitAsync(() => video.Thumbnail is not null);
                Console.WriteLine("PASS asynchronous video thumbnail");
                dashboard.SearchText = video.Name; if (dashboard.RecordingsView.Cast<object>().Count() != 1) throw new InvalidOperationException("Search failed");
                dashboard.SearchText = ""; dashboard.SelectedFilter = "Audio Recordings";
                if (dashboard.RecordingsView.Cast<RecordingItem>().Any(e => e.Kind != "Audio")) throw new InvalidOperationException("Filter failed");
                dashboard.SelectedFilter = "All Recordings";
                Console.WriteLine("PASS live search/filter bindings");
                var realRows = dashboard.Recordings.ToArray();
                var stressRows = Enumerable.Range(0, 5000).Select(i => new RecordingItem(video.Entry with { Id = Guid.NewGuid(), FullPath = System.IO.Path.Combine(dashboard.OutputFolder, $"ui-stress-{i:00000}.mp4") }));
                dashboard.Recordings.ReplaceAll(stressRows); window.UpdateLayout();
                var list = Descendants(control).OfType<ListView>().Single();
                var containers = Descendants(list).OfType<ListViewItem>().Count();
                if (containers >= 100 || containers == 0) throw new InvalidOperationException("List virtualization failed: " + containers);
                Console.WriteLine($"PASS virtualization: 5000 synthetic UI rows, {containers} realized containers");
                dashboard.Recordings.ReplaceAll(realRows); window.UpdateLayout();
                if (bindingErrors.Count != 0) throw new InvalidOperationException("WPF binding errors: " + bindingErrors.Count);
                Console.WriteLine("PASS zero WPF binding errors and populated bulk refresh");
                await dashboard.DisposeAsync(); await dashboard.DisposeAsync();
                Console.WriteLine("PASS idempotent library disposal");
            }
            catch (Exception ex) { Console.WriteLine("FAIL library UI: " + ex); failed = true; }
            finally { window.Close(); }
        };
        startup.Start(); var result = app.Run(); Console.WriteLine("Library UI QA exit: " + result); return failed || result != 0 ? 1 : 0;
    }
    private static async Task WaitAsync(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready()) { if (watch.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Expected library/player state not reached."); await Task.Delay(100); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private sealed class BindingErrors : TraceListener
    {
        public int Count { get; private set; }
        public override void Write(string? text) { }
        public override void WriteLine(string? text) { if (!string.IsNullOrWhiteSpace(text)) { Count++; Console.WriteLine("Binding error: " + text); } }
    }
}
