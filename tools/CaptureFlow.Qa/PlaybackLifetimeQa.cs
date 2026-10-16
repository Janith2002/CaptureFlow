using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CaptureFlow.Infrastructure.Ffmpeg;
namespace CaptureFlow.Qa;

// A fresh-process, generated-file experiment. No CaptureFlow App/DI, device discovery,
// recording, preferences or LibraryMediaPlayer is constructed.
internal static class PlaybackLifetimeQa
{
    public static int Run(bool directPlayer, bool diagnosticGc, bool openCloseOnly = false, int cyclesPerRound = 4)
    {
        var folder = Path.GetFullPath("artifacts/qa/m693/playback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); var source = Path.Combine(folder, "generated.mp4");
        var info = new ProcessStartInfo(FfmpegToolPaths.ResolveExecutable("ffmpeg.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=30", "-t", "1", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", source }) info.ArgumentList.Add(arg);
        using (var child = Process.Start(info)!)
        using (var errors = child.StandardError)
        {
            Console.WriteLine($"OWNED_CHILD fixture-encoder pid={child.Id}; active=1; no other child launch sites in this control.");
            var read = errors.ReadToEndAsync();
            if (!child.WaitForExit(15000)) { child.Kill(true); child.WaitForExit(); throw new TimeoutException("Owned fixture encoder timed out."); }
            var detail = read.GetAwaiter().GetResult(); if (child.ExitCode != 0) throw new IOException(detail);
            Console.WriteLine("OWNED_CHILD fixture-encoder exited; active=0 for all following playback snapshots. This tracks control-owned children, not system-wide media services.");
        }
        var dispatcher = Dispatcher.CurrentDispatcher;
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var failed = false;
        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                Console.WriteLine($"Minimal WPF: mode={(directPlayer ? "MediaPlayer" : "MediaElement")}; runtime={Environment.Version}; processors={Environment.ProcessorCount}; apartment={Thread.CurrentThread.GetApartmentState()}; generated video only; diagnosticGc={diagnosticGc}; reusedOpenCloseOnly={openCloseOnly}.");
                ResourceSnapshot.Log("minimal-start");
                MediaPlayer? shared = null;
                if (openCloseOnly)
                {
                    shared = new MediaPlayer(); ResourceSnapshot.Log("after-player-construction");
                    shared.Volume = 0; ResourceSnapshot.Log("after-volume-before-open");
                }
                var weak = new List<WeakReference>();
                for (var round = 1; round <= 3; round++)
                {
                    for (var i = 0; i < cyclesPerRound; i++) weak.Add(shared is null ? await OpenCloseAsync(source, directPlayer) : await OpenCloseOnlyAsync(shared, source));
                    await Task.Delay(1500); ResourceSnapshot.Log("minimal-round-" + round);
                }
                await Task.Delay(5000); ResourceSnapshot.Log("minimal-settled-5s");
                await Task.Delay(25000); ResourceSnapshot.Log("minimal-settled-30s");
                ResourceSnapshot.LogThreadOwners();
                if (shared is not null) Console.WriteLine("Open/Close control reuses one player; never calls Play, seek or CaptureFlow code. Its wrapper is intentionally held until the control ends.");
                Console.WriteLine($"Live weak-reference entries before diagnostic collection: {weak.Count(w => w.IsAlive)}/{weak.Count}; unique players created={(openCloseOnly ? 1 : weak.Count)}; weak counts alone do not prove a leak.");
                if (diagnosticGc)
                {
                    // Optional forensic control, never used by the application or ordinary soak.
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    await Task.Delay(1500); ResourceSnapshot.Log("minimal-diagnostic-gc");
                    Console.WriteLine($"Live weak-reference entries after diagnostic collection: {weak.Count(w => w.IsAlive)}/{weak.Count}.");
                }
                using (File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                shared?.Close(); shared = null;
                Console.WriteLine("PASS generated source released; all handlers detached; no physical capture. Resource growth is diagnostic, not asserted away.");
            }
            catch (Exception ex) { failed = true; Console.WriteLine("FAIL minimal playback: " + ex); }
            finally { app.Shutdown(failed ? 1 : 0); }
        });
        app.Run(); ResourceSnapshot.Log("after-dispatcher-shutdown");
        return failed ? 1 : 0;
    }

    private static async Task<WeakReference> OpenCloseOnlyAsync(MediaPlayer player, string source)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler onOpen = (_, _) => opened.TrySetResult();
        EventHandler<ExceptionEventArgs> onFail = (_, e) => opened.TrySetException(e.ErrorException);
        player.MediaOpened += onOpen; player.MediaFailed += onFail;
        try { player.Open(new Uri(source)); await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { player.MediaOpened -= onOpen; player.MediaFailed -= onFail; player.Close(); }
        return new(player);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> OpenCloseAsync(string source, bool direct)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (direct)
        {
            var player = new MediaPlayer { Volume = 0 };
            EventHandler onOpen = (_, _) => opened.TrySetResult();
            EventHandler<ExceptionEventArgs> onFail = (_, e) => opened.TrySetException(e.ErrorException);
            player.MediaOpened += onOpen; player.MediaFailed += onFail;
            try { player.Open(new Uri(source)); player.Play(); await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)); player.Position = TimeSpan.FromSeconds(.2); }
            finally { player.MediaOpened -= onOpen; player.MediaFailed -= onFail; player.Close(); }
            return new(player);
        }
        else
        {
            var element = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Volume = 0 };
            var host = new Window { Content = element, Width = 360, Height = 280, ShowInTaskbar = false, Title = "Generated-media lifetime QA" };
            RoutedEventHandler onOpen = (_, _) => opened.TrySetResult();
            EventHandler<ExceptionRoutedEventArgs> onFail = (_, e) => opened.TrySetException(e.ErrorException);
            element.MediaOpened += onOpen; element.MediaFailed += onFail;
            try { host.Show(); element.Source = new Uri(source); element.Play(); await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)); element.Position = TimeSpan.FromSeconds(.2); }
            finally { element.MediaOpened -= onOpen; element.MediaFailed -= onFail; element.Close(); element.Source = null; host.Content = null; host.Close(); }
            return new(element);
        }
    }
}
