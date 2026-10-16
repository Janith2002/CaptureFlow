using System.Buffers;
using System.Diagnostics;
using System.IO;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Qa;

/// <summary>Generated pixels only. Metrics refer to this managed process, not FFmpeg's CPU/memory.</summary>
internal static class PerformanceQa
{
    public static async Task<int> RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "qa", "performance", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        Console.WriteLine($"Conditions: {Environment.OSVersion}; .NET {Environment.Version}; logical CPUs={Environment.ProcessorCount}; 1280x720 generated BGRA; Software/Standard; 5-second outputs; unpaced producer; no devices. Root={root}");
        using var process = Process.GetCurrentProcess();
        try
        {
            var backend = new FfmpegVideoEncodingBackend(); var pixels = new byte[1280 * 720 * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 160; pixels[i + 3] = 255; }
            for (var cycle = 0; cycle < 6; cycle++)
            {
                var fps = cycle % 2 == 0 ? 30 : 60;
                var options = new VideoRecordingConfiguration(new(VideoSourceKind.Monitor, "synthetic-no-device"), root, fps,
                    QualityPreferences: new(VideoQualityPreset.Standard, FramesPerSecond: fps, Encoder: VideoEncoderChoice.Software));
                var source = Path.Combine(root, $"capture-{cycle}.mp4"); var output = Path.Combine(root, $"output-{cycle}.mp4");
                process.Refresh(); var before = process.PrivateMemorySize64; var handlesBefore = process.HandleCount; var peak = before;
                var allocated = GC.GetTotalAllocatedBytes(); var cpu = process.TotalProcessorTime; var wall = Stopwatch.StartNew(); List<double> writes = [];
                await using (var encoder = await backend.CreateAsync(options, new(1280, 720), source, default))
                {
                    for (var frame = 0; frame < fps * 5; frame++)
                    {
                        pixels[0] = (byte)frame; using var image = new VideoFrame(new BorrowedPixels(pixels), new(1280, 720), TimeSpan.FromSeconds(frame / (double)fps));
                        var write = Stopwatch.StartNew(); await encoder.WriteFrameAsync(image, default); writes.Add(write.Elapsed.TotalMilliseconds);
                        if (frame % 30 == 0) { process.Refresh(); peak = Math.Max(peak, process.PrivateMemorySize64); }
                    }
                    await encoder.FinishAsync(default);
                }
                var encodeSeconds = wall.Elapsed.TotalSeconds;
                await backend.MuxAsync(options, source, null, output, TimeSpan.FromSeconds(5), default);
                await backend.ValidateAsync(output, "ffmpeg.exe", default); wall.Stop(); process.Refresh(); writes.Sort();
                Console.WriteLine($"cycle={cycle + 1} fps={fps} frames={fps * 5} encode_s={encodeSeconds:F3} encode_frames_per_s={fps * 5 / encodeSeconds:F1} total_export_decode_s={wall.Elapsed.TotalSeconds:F3} managed_cpu_ms={(process.TotalProcessorTime - cpu).TotalMilliseconds:F1} private_start_mb={before / 1048576d:F1} private_peak_sample_mb={peak / 1048576d:F1} private_end_mb={process.PrivateMemorySize64 / 1048576d:F1} allocated_mb={(GC.GetTotalAllocatedBytes() - allocated) / 1048576d:F2} handles={handlesBefore}->{process.HandleCount} write_p95_ms={writes[(int)(writes.Count * .95)]:F2} write_max_ms={writes[^1]:F2}");
                using (File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                using (File.Open(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); process.Refresh();
                Console.WriteLine($"diagnostic_post_gc_cycle={cycle + 1} handles={process.HandleCount} threads={process.Threads.Count} private_mb={process.PrivateMemorySize64 / 1048576d:F1}; forced GC in QA only, never production");
            }
            // Cancellation on a live, owned encoder: disposal must release its output handle.
            var cancelled = Path.Combine(root, "cancelled.mp4");
            var config = new VideoRecordingConfiguration(new(VideoSourceKind.Monitor, "synthetic-no-device"), root);
            var owned = await backend.CreateAsync(config, new(1280, 720), cancelled, default);
            using (var first = new VideoFrame(new BorrowedPixels(pixels), new(1280, 720), TimeSpan.Zero)) await owned.WriteFrameAsync(first, default);
            using var token = new CancellationTokenSource(); token.Cancel(); var latency = Stopwatch.StartNew();
            try { using var frame = new VideoFrame(new BorrowedPixels(pixels), new(1280, 720), TimeSpan.Zero); await owned.WriteFrameAsync(frame, token.Token); throw new InvalidOperationException("Cancelled write unexpectedly succeeded."); }
            catch (OperationCanceledException) { }
            finally { await owned.DisposeAsync(); }
            Console.WriteLine($"owned_encoder_cancellation_and_dispose_ms={latency.Elapsed.TotalMilliseconds:F1}");
            using (File.Open(cancelled, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Console.WriteLine("PASS six synthetic encoder/export/decode sessions and cancelled owned encoder. No capture performed; fixtures retained."); return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL performance QA: " + ex); return 1; }
    }
    private sealed class BorrowedPixels(byte[] bytes) : IMemoryOwner<byte>
    { public Memory<byte> Memory => bytes; public void Dispose() { } }
    public static async Task IdleAsync(System.Windows.Window window, Stopwatch startup)
    {
        Console.WriteLine($"managed_entry_to_first_navigation_ms={startup.Elapsed.TotalMilliseconds:F1}; dpi={System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX:F2}; window_dip={window.ActualWidth:F0}x{window.ActualHeight:F0}");
        await Task.Delay(2000); using var process = Process.GetCurrentProcess(); process.Refresh();
        var cpu = process.TotalProcessorTime; var wall = Stopwatch.StartNew(); var privateStart = process.PrivateMemorySize64;
        var gap = 0d; var last = Stopwatch.GetTimestamp();
        var heartbeat = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        heartbeat.Tick += (_, _) => { var now = Stopwatch.GetTimestamp(); gap = Math.Max(gap, Stopwatch.GetElapsedTime(last, now).TotalMilliseconds); last = now; };
        heartbeat.Start(); await Task.Delay(10000); heartbeat.Stop(); wall.Stop(); process.Refresh();
        var singleCpu = (process.TotalProcessorTime - cpu).TotalSeconds / wall.Elapsed.TotalSeconds * 100;
        Console.WriteLine($"idle_wall_s={wall.Elapsed.TotalSeconds:F2} cpu_one_core_percent={singleCpu:F2} cpu_machine_percent={singleCpu / Environment.ProcessorCount:F3} private_start_mb={privateStart / 1048576d:F1} private_end_mb={process.PrivateMemorySize64 / 1048576d:F1} working_set_mb={process.WorkingSet64 / 1048576d:F1} max_dispatcher_tick_gap_ms={gap:F1}; warm desktop QA process, no capture; OS={Environment.OSVersion}; CPUs={Environment.ProcessorCount}");
    }
}
