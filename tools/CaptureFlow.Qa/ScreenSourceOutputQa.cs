using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CaptureFlow.Core.Audio;
using CaptureFlow.Core.Video;
using CaptureFlow.Application.Video;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.Services;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Infrastructure.Video;
using CaptureFlow.Infrastructure.Editing;
using CaptureFlow.Core.Editing;

internal static class ScreenSourceOutputQa
{
    public static int Run(bool region, bool mouseEffects = false, bool visibleCursor = false, int fps = 30, bool limited = false)
    {
        var app = new App(); app.InitializeComponent(); var started = false; var failed = false; var completed = false;
        app.DispatcherUnhandledException += (_, e) => { failed = true; Console.WriteLine("FAIL unhandled: " + e.Exception); e.Handled = true; app.Shutdown(1); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (started || app.MainWindow is not MainWindow main) return; started = true; timer.Stop();
            Process? fixture = null;
            Point? originalPointer = null; Point? testPointer = null;
            try
            {
                await main.DesktopInitialization;
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                start.ArgumentList.Add(typeof(ScreenSourceOutputQa).Assembly.Location); start.ArgumentList.Add("--window-source-fixture");
                if (region) start.ArgumentList.Add("--region-scene");
                fixture = Process.Start(start) ?? throw new Exception("BLOCKED: cannot launch controlled source window.");
                var discovery = new WindowsApplicationWindowDiscovery(); ApplicationWindowDescriptor? window = null;
                for (var i = 0; i < 30 && window is null; i++) { await Task.Delay(200); window = (await discovery.GetWindowsAsync()).FirstOrDefault(w => w.ProcessId == fixture.Id); }
                if (window is null) throw new Exception("BLOCKED: controlled window unavailable.");
                await Task.Delay(500); // Allow the opaque generated scene to reach DWM.
                window = (await discovery.GetWindowsAsync()).Single(w => w.ProcessId == fixture.Id);
                var screens = new WindowsGraphicsCaptureSourceFactory(); var folder = Path.Combine(Path.GetTempPath(), "CaptureFlowSourceQA", Guid.NewGuid().ToString("N"));
                VideoSourceOptions options;
                if (region)
                {
                    var area = DesktopWorkAreas.Enumerate(true).Single(a => a.Primary); var monitor = (await screens.GetMonitorsAsync()).Single(m => m.Id == area.Id);
                    var x = area.Left + (area.Width - 160) / 2; var y = area.Top + (area.Height - 96) / 2;
                    var requested = RegionSelectionGeometry.FromDesktopPixels(monitor.Id, area.Left, area.Top, monitor.Width, monitor.Height, x, y, x + 160, y + 96);
                    var select = new WpfRegionSelection().SelectAsync(monitor, requested, default);
                    var overlay = app.Windows.OfType<RegionSelectionWindow>().Single(); await Task.Delay(200);
                    ((Button)overlay.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var selected = await select ?? throw new Exception("Region confirmation failed.");
                    Check(!app.Windows.OfType<RegionSelectionWindow>().Any(), "Selection overlay did not close before capture.");
                    options = new(VideoSourceKind.Region, monitor.Id, false, Region: selected);
                }
                else options = new(VideoSourceKind.Window, window.Id, mouseEffects && visibleCursor, Window: window, MouseEffects: mouseEffects ? new(visibleCursor, true, "#0000FF", 24, 1, true, true, true, "#FF0000", "#FF00FF", 500) : null);
                if (mouseEffects && region) throw new IOException("Mouse QA uses only the generated application window; choose one QA mode.");
                main.Hide();
                if (mouseEffects)
                {
                    SetForegroundWindow(new IntPtr(window.Handle));
                    if (GetForegroundWindow() != new IntPtr(window.Handle)) throw new IOException("BLOCKED: Windows did not give the generated QA window foreground focus; no capture or input injection started.");
                    if (DwmGetWindowRect(new IntPtr(window.Handle), 9, out var rect, Marshal.SizeOf<Rect>()) != 0)
                        throw new IOException("BLOCKED: cannot locate the controlled QA window.");
                    if (GetPhysicalCursorPos(out var original)) originalPointer = original;
                    testPointer = new Point { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 };
                    if (!SetPhysicalCursorPos(testPointer.Value.X, testPointer.Value.Y)) throw new IOException("BLOCKED: cannot position mouse in generated content.");
                }
                var factory = new RestrictedSourceFactory(screens, options);
                await using (var recorder = new ScreenRecordingService(factory, new FfmpegVideoEncodingBackend(), new SingleSourceVideoCompositor(), new NoAudio()))
                {
                    await recorder.StartAsync(new(options, folder, fps, FfmpegPath: "ffmpeg.exe")); Check(recorder.State == VideoRecordingState.Recording, recorder.ErrorMessage ?? "Recording did not start.");
                    if (mouseEffects) { InjectClick(new IntPtr(window.Handle), 2, 4); await Task.Delay(150); InjectClick(new IntPtr(window.Handle), 8, 16); }
                    await Task.Delay(600); await recorder.PauseAsync(); var paused = recorder.Duration; await Task.Delay(350);
                    Check(recorder.State == VideoRecordingState.Paused && Math.Abs((recorder.Duration - paused).TotalSeconds) < .02, "Pause timer drift.");
                    await recorder.ResumeAsync(); await Task.Delay(700); await recorder.StopAsync();
                    Check(recorder.State == VideoRecordingState.Completed && recorder.SavedFilePath is not null, recorder.ErrorMessage ?? "Stop failed.");
                    var file = recorder.SavedFilePath!; var metadata = await new FfmpegEditorBackend().ProbeAsync(file, "ffmpeg.exe", default);
                    Check(metadata.HasVideo && !metadata.HasAudio && Math.Abs(metadata.Duration - recorder.Duration.TotalSeconds) < .15, "Unexpected media streams/duration.");
                    if (region)
                    {
                        Check(metadata.Width == 160 && metadata.Height == 96, "Region output dimensions differ from selected pixels.");
                        await CheckGreenPixelsAsync(file);
                    }
                    if (mouseEffects) await CheckMousePixelsAsync(file, metadata.Width, metadata.Height, visibleCursor);
                    Check(factory.Disposals == 1, "Capture session was not released after stop.");
                    Console.WriteLine($"PASS real {(region ? "region" : "controlled application-window")} WGC → MP4; {metadata.Width}x{metadata.Height}; duration={metadata.Duration:0.000}s; pause/resume/stop and capture disposal; output={file}");
                    main.Show();
                    var player = new LibraryMediaPlayer(); var playback = new Window { Title = "CaptureFlow QA playback", Width = 600, Height = 420, Content = player }; playback.Show(); player.Open(file);
                    try
                    {
                        for (var i = 0; i < 30 && player.Session.State is CaptureFlow.Application.Library.PlaybackState.Loading or CaptureFlow.Application.Library.PlaybackState.Empty; i++) await Task.Delay(100);
                        Check(player.Session.State is CaptureFlow.Application.Library.PlaybackState.Playing or CaptureFlow.Application.Library.PlaybackState.Ended, player.Session.Error ?? "MP4 playback failed.");
                        await Task.Delay(200); Console.WriteLine("PASS real MP4 opened/played in WPF. Perceptual A/V sync is not tested; audio capture was disabled.");
                    }
                    finally { player.CloseMedia(); playback.Close(); }
                }
                if (!limited)
                {
                    await VerifyCancellationAsync(screens, options, folder);
                    if (!region) await VerifyWindowFailuresAsync(screens, discovery, fixture.Id, folder);
                }
                Console.WriteLine("QA capture was limited to generated content. No webcam, microphone or system audio capture started.");
                completed = true;
            }
            catch (Exception ex) { failed = true; Console.WriteLine("FAIL source output QA: " + ex); }
            finally
            {
                if (fixture is not null) { if (!fixture.HasExited) { fixture.Kill(entireProcessTree: true); await fixture.WaitForExitAsync(); } fixture.Dispose(); }
                if (originalPointer is { } before && testPointer is { } test && GetPhysicalCursorPos(out var current) && current.X == test.X && current.Y == test.Y)
                    SetPhysicalCursorPos(before.X, before.Y); // Preserve any intervening user movement.
                ((DashboardViewModel)main.DataContext).Editor?.Session.MarkSaved(); main.RequestExit();
            }
        };
        timer.Start(); app.Run(); return failed || !completed ? 1 : 0;
    }
    private static async Task VerifyCancellationAsync(WindowsGraphicsCaptureSourceFactory screens, VideoSourceOptions options, string folder)
    {
        var factory = new RestrictedSourceFactory(screens, options);
        await using var recorder = new ScreenRecordingService(factory, new FfmpegVideoEncodingBackend(), new SingleSourceVideoCompositor(), new NoAudio());
        await recorder.StartAsync(new(options, folder)); Check(recorder.State == VideoRecordingState.Recording, recorder.ErrorMessage ?? "Cancellation test did not start.");
        await Task.Delay(200); await recorder.CancelAsync();
        Check(recorder.State == VideoRecordingState.Cancelled && recorder.SavedFilePath is null && factory.Disposals == 1, "Cancellation published an output or leaked capture.");
        Console.WriteLine("PASS native recording cancellation; no completed output and capture resources released.");
    }
    private static async Task VerifyWindowFailuresAsync(WindowsGraphicsCaptureSourceFactory screens, WindowsApplicationWindowDiscovery discovery, int processId, string folder)
    {
        foreach (var action in new[] { "resize", "minimize", "close" })
        {
            var selected = (await discovery.GetWindowsAsync()).Single(w => w.ProcessId == processId);
            var options = new VideoSourceOptions(VideoSourceKind.Window, selected.Id, false, Window: selected);
            var factory = new RestrictedSourceFactory(screens, options);
            await using var recorder = new ScreenRecordingService(factory, new FfmpegVideoEncodingBackend(), new SingleSourceVideoCompositor(), new NoAudio());
            await recorder.StartAsync(new(options, folder)); Check(recorder.State == VideoRecordingState.Recording, recorder.ErrorMessage ?? "Window failure test did not start.");
            await Task.Delay(200); var handle = new IntPtr(selected.Handle);
            if (action == "resize") SetWindowPos(handle, IntPtr.Zero, 0, 0, selected.Width + 120, selected.Height + 120, 0x16);
            else if (action == "minimize") ShowWindow(handle, 6);
            else PostMessage(handle, 0x10, IntPtr.Zero, IntPtr.Zero);
            for (var i = 0; i < 60 && recorder.State != VideoRecordingState.Error; i++) await Task.Delay(100);
            Check(recorder.State == VideoRecordingState.Error && recorder.SavedFilePath is null && factory.Disposals == 1, action + " did not stop safely: " + recorder.ErrorMessage);
            Check(recorder.WorkDirectory is not null && File.Exists(Path.Combine(recorder.WorkDirectory, "screen.mp4")), "Failure discarded intermediate media.");
            using (var released = File.Open(Path.Combine(recorder.WorkDirectory!, "screen.mp4"), FileMode.Open, FileAccess.Read, FileShare.None)) { }
            Console.WriteLine("PASS native window " + action + " stops with Error, retained intermediate, no desktop fallback and released capture/encoder resources.");
            if (action == "minimize") { ShowWindow(handle, 9); await Task.Delay(300); }
        }
    }
    private static async Task CheckGreenPixelsAsync(string file)
    {
        var info = new ProcessStartInfo("ffmpeg.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-nostdin", "-i", file, "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; var errors = process.StandardError.ReadToEndAsync(); var buffer = new byte[48000]; long pixels = 0;
        var read = Task.Run(async () => { using var reader = new BinaryReader(process.StandardOutput.BaseStream); int offset = 0, count; while ((count = await process.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset))) > 0) { var total = offset + count; var end = total / 3 * 3; for (var i = 0; i < end; i += 3) { if (buffer[i] > 20 || buffer[i + 1] < 235 || buffer[i + 2] > 20) throw new IOException("Region includes pixels outside the green QA marker or an overlay."); pixels++; } offset = total - end; Array.Copy(buffer, end, buffer, 0, offset); } });
        try { await Task.WhenAny(process.WaitForExitAsync(), read); if (read.IsFaulted) await read; await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); await read; Check(process.ExitCode == 0 && pixels > 0, await errors); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        Console.WriteLine("PASS every decoded region pixel matches the green marker; outside markers and selection overlay absent.");
    }
    private static void Check(bool success, string error) { if (!success) throw new IOException(error); }
    private static void InjectClick(nint fixture, uint down, uint up)
    {
        if (GetForegroundWindow() != fixture || !GetPhysicalCursorPos(out var point) || GetAncestor(WindowFromPoint(point), 2) != fixture)
            throw new IOException("BLOCKED: mouse/focus left the generated fixture; input injection cancelled.");
        MouseEvent(down, 0, 0, 0, 0); MouseEvent(up, 0, 0, 0, 0);
    }
    private static async Task CheckMousePixelsAsync(string file, int width, int height, bool visibleCursor)
    {
        var info = new ProcessStartInfo("ffmpeg.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-nostdin", "-i", file, "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; var errors = process.StandardError.ReadToEndAsync(); var frame = new byte[checked(width * height * 3)];
        var blue = 0; var red = 0; var magenta = 0; var black = 0;
        try
        {
            int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(frame.AsMemory(0, 1))) > 0)
            {
                await process.StandardOutput.BaseStream.ReadExactlyAsync(frame.AsMemory(1));
                var center = ((height / 2 - 12) * width + width / 2 - 12) * 3; if (frame[center + 2] > 200 && frame[center] < 50 && frame[center + 1] < 50) blue++;
                for (var y = height / 2 - 38; y <= height / 2 + 38; y++) for (var x = width / 2 - 38; x <= width / 2 + 38; x++)
                {
                    var p = (y * width + x) * 3;
                    if (frame[p] > 80 && frame[p + 1] < 70 && frame[p + 2] < 70) red++;
                    if (frame[p] > 140 && frame[p + 1] < 100 && frame[p + 2] > 140) magenta++;
                    if (frame[p] < 50 && frame[p + 1] < 50 && frame[p + 2] < 50) black++;
                }
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); Check(process.ExitCode == 0, await errors);
            Check(blue > 20 && red > 0 && magenta > 0, $"Missing native mouse effects in decoded MP4: highlight frames={blue}, left pixels={red}, right pixels={magenta}.");
            Check(visibleCursor ? black > 0 : black == 0, "Native cursor visibility did not match the requested effect configuration.");
            Console.WriteLine($"PASS real WGC exported highlight, cursor visibility ({visibleCursor}) and native left/right injected mouse effects; decoded blue frames={blue}, red ring pixels={red}, magenta ring pixels={magenta}, cursor pixels={black}. Controlled OS input, not manual physical mouse verification.");
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowRect(nint window, int attribute, out Rect rect, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool SetPhysicalCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void MouseEvent(uint flags, uint x, uint y, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    private sealed class RestrictedSourceFactory(WindowsGraphicsCaptureSourceFactory backend, VideoSourceOptions expected) : IVideoSourceFactory
    {
        public int Disposals;
        public Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken token = default) => backend.GetMonitorsAsync(token);
        public async Task<IVideoFrameSource> CreateAsync(VideoSourceOptions options, CancellationToken token)
        { if (options != expected || options.Kind is not (VideoSourceKind.Window or VideoSourceKind.Region)) throw new IOException("QA capture source changed unexpectedly."); return new TrackedSource(await backend.CreateAsync(options, token), () => Disposals++); }
    }
    private sealed class TrackedSource(IVideoFrameSource inner, Action disposed) : IVideoFrameSource
    {
        public event EventHandler<Exception>? Failed { add => inner.Failed += value; remove => inner.Failed -= value; }
        public VideoSize Size => inner.Size;
        public void SetRecordingActivity(bool active) => inner.SetRecordingActivity(active);
        public Task StartAsync(CancellationToken token) => inner.StartAsync(token);
        public ValueTask<VideoFrame> ReadFrameAsync(CancellationToken token) => inner.ReadFrameAsync(token);
        public async ValueTask DisposeAsync() { await inner.DisposeAsync(); disposed(); }
    }
    private sealed class NoAudio : IAudioCaptureBackend
    {
        public Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync() => Task.FromResult<IReadOnlyList<MicrophoneDevice>>([]);
        public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync() => Task.FromResult<IReadOnlyList<AudioOutputDevice>>([]);
        public Task<IAudioCaptureSession> CreateSessionAsync(AudioRecordingOptions options) => throw new InvalidOperationException("QA source test must not capture audio.");
    }
}
