using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CaptureFlow.Application.Video;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;

internal static class CameraStabilityQa
{
    // One limited lifecycle test. No App/host startup, audio discovery, preview, frame reader or recording.
    public static int Run()
    {
        var app = new System.Windows.Application(); var result = 1;
        var window = new Window { Title = "CaptureFlow camera lifecycle QA", Width = 480, Height = 160,
            Content = new TextBlock { Text = "Authorized camera initialization and cleanup only.\nNo preview or recording.", Margin = new Thickness(24) } };
        var trace = new CameraTrace(); Trace.Listeners.Add(trace); var started = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += async (_, _) =>
        {
            if (started) return; started = true; timer.Stop();
            try
            {
                var backend = new WindowsCameraCaptureBackend(new DispatcherSynchronizationContext(window.Dispatcher));
                var camera = new CameraPreviewService(backend, TimeSpan.FromSeconds(15));
                var devices = await camera.GetDevicesAsync().WaitAsync(TimeSpan.FromSeconds(15));
                var selected = devices.FirstOrDefault();
                if (selected is null) { Console.WriteLine("BLOCKED: no camera available; no device opened."); result = 2; return; }
                for (var cycle = 1; cycle <= 2; cycle++)
                {
                    // Cycle 2 is permitted only after the first close has returned and its native success was observed.
                    var elapsed = Stopwatch.StartNew();
                    var formats = await camera.SelectCameraAsync(selected.Id);
                    Check(camera.LifecycleState == CameraLifecycleState.Ready && formats.Count > 0 && !camera.IsPreviewing, "Initialization did not reach Ready without preview.");
                    Console.WriteLine($"PASS cycle {cycle} initialized; elapsed_ms={elapsed.Elapsed.TotalMilliseconds:F1}; preview=False.");
                    elapsed.Restart(); await camera.StopPreviewAsync();
                    Check(camera.LifecycleState == CameraLifecycleState.Uninitialized && camera.DeviceId is null && trace.SuccessfulCloses == cycle,
                        "Cleanup did not confirm one successful native close; reuse is prohibited.");
                    Console.WriteLine($"PASS cycle {cycle} disposed; elapsed_ms={elapsed.Elapsed.TotalMilliseconds:F1}; native_closes={trace.SuccessfulCloses}.");
                }
                await camera.DisposeAsync();
                Check(trace.Initializations == 2 && trace.SuccessfulCloses == 2 && trace.OwnerThreads.Distinct().Count() == 1,
                    "Native lifecycle counts or owner thread differed.");
                Console.WriteLine("PASS limited physical lifecycle test: initial open/close and one conditional reuse, same STA owner; no preview, frame reader, images, video, microphone or screen recording.");
                result = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL limited camera lifecycle test; stopped with no repeat or additional cleanup attempt: " + ex);
                result = 1;
            }
            finally { app.Shutdown(result); }
        };
        timer.Start(); app.Run(window); Trace.Listeners.Remove(trace); trace.Dispose(); return result;
    }
    private static void Check(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    private sealed class CameraTrace : TraceListener
    {
        public int Initializations, SuccessfulCloses;
        public List<int> OwnerThreads { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message is null) return;
            Console.WriteLine($"{DateTime.UtcNow:O} {message}");
            if (message.Contains("Camera initializing:", StringComparison.Ordinal)) { Initializations++; OwnerThreads.Add(Environment.CurrentManagedThreadId); }
            if (message.Contains("Camera native close:", StringComparison.Ordinal) && message.Contains("errors=0", StringComparison.Ordinal))
            { SuccessfulCloses++; OwnerThreads.Add(Environment.CurrentManagedThreadId); }
        }
    }
}
