using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CaptureFlow.Core.Audio;
using CaptureFlow.Core.Video;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NAudio.CoreAudioApi;

// Explicit opt-in hardware QA. Runs existing production services, never uploads media.
internal static class HardwareRecordingQa
{
    private static readonly string Output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CaptureFlowHardwareQA", Guid.NewGuid().ToString("N"));
    private static readonly List<object> Results = [];
    private static int failures;
    public static int Run(bool releaseOnly = false)
    {
        System.IO.Directory.CreateDirectory(Output);
        Console.WriteLine("Local hardware QA output: " + Output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try { await ExecuteAsync(releaseOnly); }
            catch (Exception ex) { Log("Harness", "FAIL", ex.ToString()); }
            finally
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(Output, "results.json"), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(failures == 0 ? 0 : 1);
            }
        };
        return app.Run();
    }
    private static void Log(string mode, string status, string details)
    {
        Results.Add(new { mode, status, details });
        if (status == "FAIL") failures++;
        Console.WriteLine($"{status} {mode}: {details}");
    }
    private static async Task ExecuteAsync(bool releaseOnly)
    {
        // Reuse the application's exact DI registrations without constructing recorder view models.
        var registration = typeof(CaptureFlow.Desktop.App).Assembly.GetType("CaptureFlow.Desktop.HostRegistration")!;
        var host = (IHost)registration.GetMethod("BuildCaptureFlowHost", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [Host.CreateApplicationBuilder()])!;
        await host.StartAsync();
        var audioBackend = host.Services.GetRequiredService<IAudioCaptureBackend>();
        var audio = host.Services.GetRequiredService<IAudioRecordingService>();
        var video = host.Services.GetRequiredService<IVideoRecordingService>();
        var cameras = host.Services.GetRequiredService<ICameraPreviewService>();
        var combined = host.Services.GetRequiredService<ICombinedVideoPreviewService>();
        var microphones = await audioBackend.GetDevicesAsync();
        var outputs = await audioBackend.GetOutputDevicesAsync();
        var monitors = await video.GetMonitorsAsync();
        IReadOnlyList<CameraDevice> cameraDevices = [];
        try { cameraDevices = (await cameras.GetDevicesAsync()).OrderBy(d => d.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)).ToArray(); }
        catch (Exception ex) { Log("Camera discovery", "FAIL", ex.ToString()); }
        string? playbackId = null;
        using (var enumerator = new MMDeviceEnumerator())
        {
            try { using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); playbackId = endpoint.ID; }
            catch (Exception ex) { Log("Playback endpoint", "BLOCKED", ex.Message); }
        }
        var microphone = microphones.FirstOrDefault();
        var playback = outputs.FirstOrDefault(d => d.Id == playbackId);
        Log("Inventory", "PASS", $"monitors={monitors.Count}; cameras={cameraDevices.Count}; microphones={microphones.Count}; outputs={outputs.Count}");
        foreach (var device in cameraDevices) Console.WriteLine("Camera: " + device.Name);
        foreach (var device in microphones) Console.WriteLine("Microphone: " + device.Name);
        foreach (var device in outputs) Console.WriteLine("Playback: " + device.Name);
        try
        {
            if (releaseOnly)
            {
                if (microphone is null || playback is null || cameraDevices.Count == 0 || monitors.Count == 0)
                    throw new IOException("Required device unavailable for reuse checks.");
                using var tone = new LocalTone(Output);
                await audio.StartAsync(new(microphone.Id, Output, Mode: AudioRecordingMode.MicrophoneAndSystemAudio, OutputDeviceId: playback.Id));
                if (audio.State != AudioRecordingState.Recording) throw new IOException(audio.ErrorMessage);
                await Task.Delay(500);
                await audio.StopAsync();
                if (audio.State != AudioRecordingState.Completed || audio.SavedFilePath is null) throw new IOException(audio.ErrorMessage);
                await ValidateAsync("Microphone/playback reuse", audio.SavedFilePath, false, audio.Duration.TotalSeconds, .5);
                var formats = await cameras.SelectCameraAsync(cameraDevices[0].Id);
                Log("Camera reuse", "PASS", $"Reopened native camera exclusively in a new process: {formats.Count} reported formats; no preview started.");
                await cameras.StopPreviewAsync();
                var reuseMonitor = monitors.FirstOrDefault(d => d.IsPrimary) ?? monitors[0];
                var reuseScene = ShowControlledScreen(reuseMonitor);
                try
                {
                    await using var source = await host.Services.GetRequiredService<IMonitorCaptureBackend>().CreateAsync(new(VideoSourceKind.Monitor, reuseMonitor.Id), CancellationToken.None);
                    await source.StartAsync(CancellationToken.None);
                    using var frame = await source.ReadFrameAsync(CancellationToken.None);
                    if (frame.Pixels.Length == 0) throw new IOException("No reused screen capture frame.");
                    Log("Screen reuse", "PASS", "Recreated native WGC session and read a real controlled-screen frame in a new process.");
                }
                finally { reuseScene.Close(); }
                return;
            }
            foreach (var mode in Enum.GetValues<AudioRecordingMode>())
            foreach (var outputFormat in Enum.GetValues<AudioOutputFormat>())
            {
                if (mode != AudioRecordingMode.SystemAudioOnly && microphone is null || mode != AudioRecordingMode.MicrophoneOnly && playback is null)
                { Log(mode.ToString(), "BLOCKED", "Required endpoint is unavailable."); continue; }
                try
                {
                    using var tone = mode == AudioRecordingMode.MicrophoneOnly ? null : new LocalTone(Output);
                    await audio.StartAsync(new(microphone?.Id ?? "", Output, Mode: mode, OutputDeviceId: playback?.Id, OutputFormat: outputFormat));
                    if (audio.State != AudioRecordingState.Recording) throw new IOException(audio.ErrorMessage);
                    await Task.Delay(2000);
                    await audio.PauseAsync();
                    if (audio.State != AudioRecordingState.Paused) throw new IOException("Pause did not enter Paused.");
                    var paused = audio.Duration;
                    await Task.Delay(1000);
                    if (Math.Abs((audio.Duration - paused).TotalSeconds) > .1) throw new IOException("Paused timer advanced.");
                    await audio.ResumeAsync();
                    if (audio.State != AudioRecordingState.Recording) throw new IOException("Resume did not enter Recording.");
                    await Task.Delay(2000);
                    await audio.StopAsync();
                    if (audio.State != AudioRecordingState.Completed || audio.SavedFilePath is null) throw new IOException(audio.ErrorMessage);
                    await ValidateAsync(mode + " " + outputFormat, audio.SavedFilePath, false, audio.Duration.TotalSeconds);
                }
                catch (Exception ex) { Log(mode.ToString(), "FAIL", ex.ToString()); await audio.StopAsync(); }
            }
            var monitor = monitors.FirstOrDefault(d => d.IsPrimary) ?? monitors.FirstOrDefault();
            Window? scene = null;
            try
            {
                if (monitor is not null)
                {
                    try { scene = ShowControlledScreen(monitor); }
                    catch (Exception ex) { Log("Controlled screen", "BLOCKED", ex.ToString()); }
                }
                foreach (var mode in new[] { VideoSourceKind.Webcam, VideoSourceKind.Monitor, VideoSourceKind.ScreenAndWebcam })
                foreach (var fps in mode == VideoSourceKind.Monitor ? new[] { 30, 60 } : new[] { 30 })
                {
                    if (mode != VideoSourceKind.Webcam && scene is null || mode != VideoSourceKind.Monitor && cameraDevices.Count == 0)
                    { Log(mode.ToString(), "BLOCKED", "Required device or controlled desktop scene unavailable."); continue; }
                    try
                    {
                        CameraFormat? format = null;
                        if (mode != VideoSourceKind.Monitor)
                        {
                            var formats = await cameras.SelectCameraAsync(cameraDevices[0].Id).WaitAsync(TimeSpan.FromSeconds(20));
                            format = formats.Where(f => f.Width <= 1280 && f.Height <= 720 && f.FramesPerSecond <= 30.1)
                                .OrderByDescending(f => (long)f.Width * f.Height).ThenByDescending(f => f.FramesPerSecond).FirstOrDefault() ?? formats.FirstOrDefault();
                            if (format is null) throw new IOException("No reported camera format.");
                            await cameras.StartPreviewAsync(format);
                        }
                        var source = new VideoSourceOptions(mode, mode == VideoSourceKind.Webcam ? cameraDevices[0].Id : monitor!.Id,
                            CameraFormat: format, CameraId: mode == VideoSourceKind.ScreenAndWebcam ? cameraDevices[0].Id : null);
                        if (mode == VideoSourceKind.ScreenAndWebcam) await combined.StartPreviewAsync(source);
                        var audioOptions = microphone is null ? null : new AudioRecordingOptions(microphone.Id, Output);
                        await video.StartAsync(new(source, Output, fps, 1280, 720, Audio: audioOptions)).WaitAsync(TimeSpan.FromSeconds(30));
                        if (video.State != VideoRecordingState.Recording) throw new IOException(video.ErrorMessage);
                        await Task.Delay(2000);
                        await video.PauseAsync();
                        if (video.State != VideoRecordingState.Paused) throw new IOException("Pause did not enter Paused.");
                        var paused = video.Duration;
                        await Task.Delay(1000);
                        if (Math.Abs((video.Duration - paused).TotalSeconds) > .05) throw new IOException("Paused timer advanced.");
                        await video.ResumeAsync();
                        if (video.State != VideoRecordingState.Recording) throw new IOException("Resume did not enter Recording.");
                        await Task.Delay(2000);
                        await video.StopAsync();
                        if (video.State != VideoRecordingState.Completed || video.SavedFilePath is null) throw new IOException(video.ErrorMessage);
                        await ValidateAsync(mode + " " + fps + "FPS", video.SavedFilePath, true, video.Duration.TotalSeconds);
                    }
                    catch (Exception ex) { Log(mode.ToString(), "FAIL", ex.ToString()); await video.CancelAsync(); if (mode != VideoSourceKind.Monitor) return; }
                    finally { await combined.StopPreviewAsync(); await cameras.StopPreviewAsync(); }
                }
            }
            finally { scene?.Close(); }
        }
        finally
        {
            var cleanupSucceeded = true;
            foreach (var resource in new IAsyncDisposable[] { video, combined, cameras, audio })
            {
                try { await resource.DisposeAsync(); }
                catch (Exception ex) { cleanupSucceeded = false; Log("Cleanup", "FAIL", ex.ToString()); }
            }
            await host.StopAsync();
            if (host is IAsyncDisposable disposable) await disposable.DisposeAsync();
            if (cleanupSucceeded) Log("Cleanup", "PASS", "Production service shutdown completed; verify native endpoint reuse separately.");
        }
    }
    private static async Task ValidateAsync(string mode, string path, bool video, double duration, double expectedDuration = 4)
    {
        if (!System.IO.File.Exists(path) || new System.IO.FileInfo(path).Length == 0) throw new IOException("Empty or missing output.");
        var probe = await RunProcessAsync("ffprobe.exe", ["-v", "error", "-show_streams", "-show_format", "-show_frames", "-of", "json", path]);
        System.IO.File.WriteAllText(path + ".ffprobe.json", probe);
        await RunProcessAsync("ffmpeg.exe", ["-v", "error", "-nostdin", "-xerror", "-i", path, "-map", "0", "-f", "null", "-"]);
        using var json = JsonDocument.Parse(probe);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (!streams.Any(s => s.GetProperty("codec_type").GetString() == (video ? "video" : "audio"))) throw new IOException("Expected stream missing.");
        if (Math.Abs(duration - expectedDuration) > .5) throw new IOException("Unexpected active duration: " + duration);
        var fileDuration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        if (Math.Abs(fileDuration - duration) > .2) throw new IOException("Container duration disagrees with active timer.");
        if (video)
        {
            var stream = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
            if (stream.GetProperty("codec_name").GetString() != "h264" || stream.GetProperty("width").GetInt32() != 1280 || stream.GetProperty("height").GetInt32() != 720) throw new IOException("Unexpected video codec/resolution.");
            var times = json.RootElement.GetProperty("frames").EnumerateArray().Where(f => f.GetProperty("media_type").GetString() == "video")
                .Select(f => double.Parse(f.GetProperty("best_effort_timestamp_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (times.Length < 2 || times.Zip(times.Skip(1), (a, b) => b - a).Any(delta => delta <= 0 || delta > .1)) throw new IOException("Unexpected frame timestamp gap.");
        }
        Log(mode, "PASS", $"Real capture finalized and fully decoded: {path}; service active duration={duration:F3}s. Synchronization perception still requires observation.");
        // Validate playback of audio exports as well as video.
        {
            var playback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var media = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Source = new Uri(path), Volume = .2 };
            var window = new Window { Title = "CaptureFlow local QA playback", Width = 640, Height = 400, Content = media, Topmost = true };
            media.MediaEnded += (_, _) => playback.TrySetResult();
            media.MediaFailed += (_, e) => playback.TrySetException(e.ErrorException);
            window.Show(); media.Play();
            try { await playback.Task.WaitAsync(TimeSpan.FromSeconds(20)); Log(mode + " playback", "PASS", "Local WPF playback reached MediaEnded without MediaFailed; human visual/audio acceptance not measured."); }
            finally { media.Stop(); media.Close(); window.Close(); }
        }
    }
    private static async Task<string> RunProcessAsync(string name, string[] arguments)
    {
        var info = new ProcessStartInfo(name) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        if (process.ExitCode != 0) throw new IOException(name + ": " + await errors);
        return await output;
    }
    private static Window ShowControlledScreen(MonitorDescriptor monitor)
    {
        // Refuse unknown screen geometry; never silently capture an uncovered desktop.
        MonitorInfo info = default;
        MonitorCallback callback = (handle, _, _, _) =>
        {
            var candidate = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (GetMonitorInfo(handle, ref candidate) && candidate.Device == monitor.Id) info = candidate;
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0) || info.Size == 0) throw new IOException("Cannot establish safe monitor bounds.");
        var label = new TextBlock { Foreground = Brushes.White, FontSize = 64, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var window = new Window { Title = "CaptureFlow controlled QA scene", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, Background = Brushes.Black, Content = label };
        window.Show();
        if (!SetWindowPos(new WindowInteropHelper(window).Handle, new nint(-1), info.Bounds.Left, info.Bounds.Top, info.Bounds.Right - info.Bounds.Left, info.Bounds.Bottom - info.Bounds.Top, 0x0040)) { window.Close(); throw new IOException("Cannot cover recording monitor safely."); }
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => label.Text = $"CAPTUREFLOW QA\n{clock.Elapsed.TotalSeconds:F1} seconds";
        window.Closed += (_, _) => timer.Stop(); timer.Start();
        return window;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo { public int Size; public Rect Bounds, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    private delegate bool MonitorCallback(nint monitor, nint hdc, nint rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    private sealed class LocalTone : IDisposable
    {
        public LocalTone(string directory)
        {
            var path = System.IO.Path.Combine(directory, "controlled-playback-tone.wav");
            using (var writer = new BinaryWriter(System.IO.File.Create(path)))
            {
                const int rate = 48000, bytes = rate * 4;
                writer.Write("RIFF"u8); writer.Write(bytes + 36); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes);
                for (var i = 0; i < rate; i++) { var sample = (short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 2000); writer.Write(sample); writer.Write(sample); }
            }
            if (!PlaySound(path, 0, 0x20009)) throw new IOException("Cannot play local controlled loopback stimulus.");
        }
        public void Dispose() => PlaySound(null, 0, 0);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern bool PlaySound(string? sound, nint module, uint flags);
    }
}
