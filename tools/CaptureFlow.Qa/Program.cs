using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CaptureFlow.Desktop;
using CaptureFlow.Qa;
using CaptureFlow.Desktop.ViewModels;

// Runs isolated startup/UI checks by default. Explicit hardware/source-output switches also exercise capture.
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--system-observation")) return WindowsPerformanceObservation.RunAsync().GetAwaiter().GetResult();
        if (args.Contains("--inspect-startup-process")) return StartupWindowInspection.Run(int.Parse(args[1]), args[2]);
        if (args.Contains("--production-startup")) return ProductionStartupQa.RunAsync(args.Contains("--inspect-editor")).GetAwaiter().GetResult();
        // Standalone probes do not load CaptureFlow.Desktop or execute blocked app code.
        if (args.Contains("--playback-lifetime")) return PlaybackLifetimeQa.Run(args.Contains("--direct-player") || args.Contains("--open-close-only"), args.Contains("--diagnostic-gc"), args.Contains("--open-close-only"), args.Contains("--short-lifetime") ? 1 : 4);
        if (args.Contains("--camera-discovery")) return CameraDiscovery(args);
        return RunApplication(args);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int CameraDiscovery(string[] args)
        {
            try
            {
                Console.WriteLine($"WinRT projection: {typeof(Windows.Media.Capture.Frames.MediaFrameSourceGroup).Assembly.Location}");
                Console.WriteLine($"WinRT activation HRESULT: 0x{CameraActivationProbe.Check():X8}");
                Console.WriteLine($"Metadata-only enumeration before application startup: apartment={Thread.CurrentThread.GetApartmentState()}; synchronizationContext={SynchronizationContext.Current?.GetType().Name ?? "none"}; no MediaCapture/reader construction.");
                var devices = new CaptureFlow.Infrastructure.Video.WindowsCameraCaptureBackend().GetDevicesAsync().GetAwaiter().GetResult();
                Console.WriteLine($"PASS camera enumeration: {devices.Count} camera(s); no capture started.");
                if (args.Contains("--compare-apartments")) Task.Run(async () =>
                {
                    var apartment = Thread.CurrentThread.GetApartmentState();
                    var other = await new CaptureFlow.Infrastructure.Video.WindowsCameraCaptureBackend().GetDevicesAsync();
                    Console.WriteLine($"PASS metadata-only enumeration apartment={apartment}: {other.Count} camera(s); no capture started.");
                }).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                for (Exception? current = ex; current is not null; current = current.InnerException)
                    Console.WriteLine($"{current.GetType().FullName}: HRESULT=0x{current.HResult:X8}; FileName={(current as System.IO.FileNotFoundException)?.FileName}");
                return 1;
            }
        }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int RunApplication(string[] args)
    {
        Environment.SetEnvironmentVariable("CAPTUREFLOW_STARTUP_DIAGNOSTICS", System.IO.Path.GetFullPath("artifacts/qa/m696/harness-startup-" + Guid.NewGuid().ToString("N") + ".jsonl"));
        var startup = Stopwatch.StartNew();
        if (args.Contains("--generate-branding")) return BrandingQa.Generate();
        if (args.Contains("--verify-branding")) return BrandingQa.Verify(args[Array.IndexOf(args, "--verify-branding") + 1]);
        if (args.Contains("--window-source-fixture")) return WindowSourceQa.RunFixture(args.Contains("--region-scene"));
        Environment.SetEnvironmentVariable("CAPTUREFLOW_RECOVERY_ROOT", System.IO.Path.GetFullPath("artifacts/qa/recovery-" + Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable("CAPTUREFLOW_FFMPEG_PREFERENCES", System.IO.Path.GetFullPath("artifacts/qa/external-tools-" + Guid.NewGuid().ToString("N") + ".json"));
        Environment.SetEnvironmentVariable("CAPTUREFLOW_CAMERA_DIAGNOSTICS", System.IO.Path.GetFullPath("artifacts/qa/camera-lifecycle-" + Guid.NewGuid().ToString("N") + ".log"));
        Environment.SetEnvironmentVariable("CAPTUREFLOW_MOUSE_PREFERENCES", System.IO.Path.GetFullPath("artifacts/qa/mouse-" + Guid.NewGuid().ToString("N") + ".json"));
        Environment.SetEnvironmentVariable("CAPTUREFLOW_REGION_PREFERENCES", System.IO.Path.GetFullPath("artifacts/qa/region-" + Guid.NewGuid().ToString("N") + ".json"));
        Environment.SetEnvironmentVariable("CAPTUREFLOW_EXPERIENCE_PREFERENCES", System.IO.Path.GetFullPath("artifacts/qa/experience-" + Guid.NewGuid().ToString("N") + ".json"));
        Environment.SetEnvironmentVariable("CAPTUREFLOW_QUALITY_PREFERENCES", System.IO.Path.GetFullPath("artifacts/qa/quality-" + Guid.NewGuid().ToString("N") + ".json"));
        Environment.SetEnvironmentVariable("CAPTUREFLOW_SHORTCUT_PREFERENCES", System.IO.Path.GetFullPath("artifacts/qa/shortcuts-" + Guid.NewGuid().ToString("N") + ".json"));
        Environment.SetEnvironmentVariable("CAPTUREFLOW_LIBRARY_DATABASE", System.IO.Path.GetFullPath("artifacts/qa/library-" + Guid.NewGuid().ToString("N") + ".db"));
        Environment.SetEnvironmentVariable("CAPTUREFLOW_LIBRARY_INITIAL_FOLDER", System.IO.Path.GetFullPath(args.Contains("--library-ui") ? "artifacts/qa/library-ui" : args.Contains("--mp3-ui") ? "artifacts/qa/mp3-ui" : "artifacts/qa/runtime-output"));
        new CaptureFlow.Infrastructure.Desktop.JsonShortcutPreferencesStore(Environment.GetEnvironmentVariable("CAPTUREFLOW_SHORTCUT_PREFERENCES")!).SaveAsync(CaptureFlow.Core.Desktop.ShortcutPreferences.Defaults with { Enabled = false }).GetAwaiter().GetResult();
        if (args.Contains("--recovery-ui")) RecoveryUiQa.Prepare();
        if (args.Contains("--performance-synthetic")) return PerformanceQa.RunAsync().GetAwaiter().GetResult();
        Environment.SetEnvironmentVariable("CAPTUREFLOW_STUDIO_PREFERENCES", System.IO.Path.GetFullPath("artifacts/qa/studio-" + Guid.NewGuid().ToString("N") + ".json"));
        if (args.Contains("--built-exe-smoke")) return PublishedSmokeQa.RunAsync(args[Array.IndexOf(args, "--built-exe-smoke") + 1], requireBundledRuntime: false).GetAwaiter().GetResult();
        if (args.Contains("--published-smoke")) return PublishedSmokeQa.RunAsync(args[Array.IndexOf(args, "--published-smoke") + 1], args.Contains("--verify-tools")).GetAwaiter().GetResult();
        if (args.Contains("--encoder-probe"))
        {
            try
            {
                var results = new CaptureFlow.Infrastructure.Video.FfmpegEncoderCapabilities().ProbeAsync("ffmpeg.exe", new(1920, 1080), 30, 10000, default).GetAwaiter().GetResult();
                foreach (var capability in results) Console.WriteLine($"{(capability.Available ? "PASS" : "UNAVAILABLE")} {capability.Encoder}: {capability.Detail}");
                Console.WriteLine("Synthetic frames only; no camera, screen or audio captured.");
                return results.Any(c => c.Encoder == CaptureFlow.Core.Video.VideoEncoderChoice.Software && c.Available) ? 0 : 1;
            }
            catch (Exception ex) { Console.WriteLine("FAIL encoder checks: " + ex); return 1; }
        }
        Environment.SetEnvironmentVariable("CAPTUREFLOW_EDITOR_WORKSPACE", System.IO.Path.GetFullPath("artifacts/qa/editor-workspace-" + Guid.NewGuid().ToString("N") + ".json"));
        if (args.Contains("--editor-ui")) return EditorUiQa.Run(args.Contains("--release-qa"), args.Contains("--resource-isolation"), args.Contains("--resource-repeat") ? 3 : 1, args.Contains("--managed-lifetime"));
        if (args.Contains("--camera-stability-native"))
        {
            if (!args.Contains("--authorize-camera-initialization")) { Console.WriteLine("BLOCKED: explicit camera initialization/cleanup authorization required."); return 2; }
            return CameraStabilityQa.Run();
        }
        if (args.Contains("--source-output-qa"))
        {
            if (args.Contains("--region") && !args.Contains("--scene-ready")) { Console.WriteLine("BLOCKED: region capture requires a confirmed non-sensitive desktop."); return 2; }
            return ScreenSourceOutputQa.Run(args.Contains("--region"), args.Contains("--mouse-effects"), args.Contains("--visible-cursor"), args.Contains("--fps60") ? 60 : 30, args.Contains("--limited"));
        }
        if (args.Contains("--screen-sources-ui")) return ScreenSourcesQa.Run();
        if (args.Contains("--mouse-effects-ui")) return ScreenSourcesQa.Run(mouseEffects: true);
        if (args.Contains("--shortcuts-tray")) return ShortcutTrayQa.Run();
        if (args.Contains("--floating-toolbar")) return FloatingToolbarQa.Run();
        if (args.Contains("--library-ui")) { LibraryUiQa.PrepareAsync().GetAwaiter().GetResult(); return LibraryUiQa.Run(); }
        if (args.Contains("--library-fileops")) return LibraryFileOperationsQa.RunAsync().GetAwaiter().GetResult();
        if (args.Contains("--hardware-tests"))
        {
            if (!args.Contains("--scene-ready")) { Console.WriteLine("BLOCKED: confirm a non-sensitive scene before hardware capture."); return 2; }
            return HardwareRecordingQa.Run();
        }
        if (args.Contains("--hardware-release-check"))
        {
            if (!args.Contains("--scene-ready")) { Console.WriteLine("BLOCKED: confirm safe scene before device reuse checks."); return 2; }
            return HardwareRecordingQa.Run(releaseOnly: true);
        }


        var app = new App();
        app.InitializeComponent();
        var errors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var pages = new[] { "Dashboard", "Screen Recorder", "Audio Recorder", "Recordings", "Settings" };
        var index = 0;
        var ticks = 0;
        var failed = false;
        app.DispatcherUnhandledException += (_, e) => { Console.WriteLine("FAIL unhandled: " + e.Exception); failed = true; e.Handled = true; app.Shutdown(1); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        var advancing = false;
        timer.Tick += async (_, _) =>
        {
            if (advancing) return; advancing = true;
            try {
            if (++ticks > 20) { Console.WriteLine("FAIL startup/shutdown timeout"); failed = true; app.Shutdown(1); return; }
            if (app.MainWindow is not MainWindow window || window.DataContext is not DashboardViewModel dashboard) return;
            if (args.Contains("--icon-hotfix-ui") && PublishedSmokeQa.AcknowledgeOwnedTrayWarning(Environment.ProcessId)) return;
            if (index < pages.Length)
            {
                if (index == 0)
                {
                    dashboard.OutputFolder = args.Contains("--recovery-ui") ? Environment.GetEnvironmentVariable("CAPTUREFLOW_LIBRARY_INITIAL_FOLDER")! : System.IO.Path.GetFullPath(args.Contains("--mp3-ui") ? "artifacts/qa/mp3-ui" : "artifacts/qa/runtime-output");
                    Console.WriteLine("PASS real App startup and MainWindow DI resolution");
                    if (args.Contains("--performance-ui"))
                    {
                        var bounds = CaptureFlow.Desktop.Services.DesktopWorkAreas.Bounds(new System.Windows.Interop.WindowInteropHelper(window).Handle);
                        var fits = CaptureFlow.Desktop.Services.DesktopWorkAreas.Enumerate().Any(a => bounds.Left >= a.Left - 1 && bounds.Top >= a.Top - 1 && bounds.Left + bounds.Width <= a.Left + a.Width + 1 && bounds.Top + bounds.Height <= a.Top + a.Height + 1);
                        if (!fits) throw new InvalidOperationException("Startup window extends beyond its desktop work area.");
                        Console.WriteLine("PASS startup window fits a monitor work area.");
                        await PerformanceQa.IdleAsync(window, startup);
                    }
                }
                dashboard.NavigateCommand.Execute(pages[index]);
                if (args.Contains("--mp3-ui") && pages[index] == "Audio Recorder")
                {
                    var audio = dashboard.Audio;
                    if (!audio.FormatOptions.SequenceEqual(new[] { "WAV", "MP3" }) || audio.SelectedMp3Bitrate != 192 || !audio.Mp3BitrateOptions.SequenceEqual(new[] { 128, 192, 256, 320 }))
                    { failed = true; Console.WriteLine("FAIL audio format/bitrate defaults"); }
                    audio.SelectedMicrophone = new CaptureFlow.Core.Audio.MicrophoneDevice("qa-command-only", "Command binding test (never opened)");
                    audio.SelectedFormat = "MP3";
                    if (!audio.IsMp3 || !audio.StartCommand.CanExecute(null)) { failed = true; Console.WriteLine("FAIL MP3 selection command routing"); }
                    audio.SelectedMp3Bitrate = 193;
                    if (audio.StartCommand.CanExecute(null)) { failed = true; Console.WriteLine("FAIL unsupported MP3 bitrate accepted"); }
                    audio.SelectedMp3Bitrate = 192;
                    Console.WriteLine("PASS WAV/MP3 selector, default bitrate and command validation; no capture command executed.");
                }
                if (args.Contains("--quality-ui") && pages[index] == "Settings")
                    await QualityUiQa.VerifyAsync(window, dashboard);
                if (args.Contains("--recovery-ui") && pages[index] == "Settings") await RecoveryUiQa.VerifyAsync(window, dashboard);
                window.UpdateLayout();
                var expectedView = new[] { "DashboardPage", "ScreenRecorderPage", "AudioRecorderPage", "RecordingsLibrary", "SettingsPage" }[index];
                var visiblePage = Descendants(window).OfType<UserControl>().Any(c => c.GetType().Name == expectedView && c.IsVisible);
                var controls = Descendants(window).OfType<Button>().Where(b => b.IsVisible).ToArray();
                if (args.Contains("--release-qa")) await ReleaseWorkspaceQa.VerifyAsync(window, pages[index], Descendants(window).OfType<UserControl>().Single(c => c.GetType().Name == expectedView && c.IsVisible));
                if (args.Contains("--2026-ui") && pages[index] == "Screen Recorder")
                {
                    foreach (var command in new System.Windows.Input.ICommand[] { dashboard.Screen.StartCommand, dashboard.Screen.PauseCommand, dashboard.Screen.ResumeCommand, dashboard.Screen.StopCommand })
                        if (!controls.Any(b => ReferenceEquals(b.Command, command))) throw new InvalidOperationException("Recording studio command binding missing.");
                    Console.WriteLine("PASS studio controls route to the existing Start/Pause/Resume/Stop commands; none executed.");
                    Console.WriteLine("Native WPF DPI scale: " + VisualTreeHelper.GetDpi(window).DpiScaleX);
                    if (args.Contains("--studio-ui")) await StudioUiQa.VerifyAsync(window, dashboard);
                    if (args.Contains("--premium-ui")) await PremiumVisualQa.CaptureAsync(window, Descendants(window).OfType<CaptureFlow.Desktop.Views.ScreenRecorderPage>().Single(), "studio");
                }
                if (args.Contains("--icon-hotfix-ui")) { await window.DesktopInitialization; BrandingQa.VerifyWindow(window); }
                Console.WriteLine($"PASS page {pages[index]}: title={dashboard.PageTitle}, command buttons={controls.Count(b => b.Command is not null)}");
                if (args.Contains("--2026-ui"))
                {
                    window.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    var path = System.IO.Path.GetFullPath("artifacts/qa/ui-2026-" + pages[index].Replace(" ", "-") + "-" + Guid.NewGuid().ToString("N") + ".png");
                    using (var output = System.IO.File.Create(path)) encoder.Save(output);
                    Console.WriteLine("Rendered owned WPF UI only: " + path);
                }
                if (dashboard.PageTitle != pages[index] || !visiblePage) { failed = true; Console.WriteLine("FAIL expected page is not visible: " + expectedView); }
                index++;
            }
            else
            {
                timer.Stop();
                Console.WriteLine($"Binding errors: {errors.Messages.Count}");
                Console.WriteLine($"Discovered monitors={dashboard.Screen.Monitors.Count}, cameras={dashboard.Screen.Cameras.Count}, microphones={dashboard.Audio.Microphones.Count}, outputs={dashboard.Audio.OutputDevices.Count}");
                Console.WriteLine("Audio discovery: " + dashboard.Audio.StatusMessage);
                Console.WriteLine("Video discovery: " + dashboard.Screen.StatusMessage);
                if (args.Contains("--mp3-ui"))
                {
                    var mp3 = dashboard.Recordings.FirstOrDefault(r => r.Format == "MP3");
                    if (mp3 is null || mp3.Kind != "Audio" || mp3.Duration == "Unavailable") { failed = true; Console.WriteLine("FAIL MP3 library format/duration metadata"); }
                    else Console.WriteLine("PASS MP3 library: format=" + mp3.Format + "; kind=" + mp3.Kind + "; duration=" + mp3.Duration);
                }
                if (dashboard.Screen.CameraDiscoveryError is not null && dashboard.Screen.HasNoCameras)
                { failed = true; Console.WriteLine("FAIL discovery error incorrectly displayed as no camera connected"); }
                foreach (var message in errors.Messages) Console.WriteLine(message);
                failed |= errors.Messages.Count > 0;
                window.Close();
            }
            }
            catch (Exception ex) { failed = true; timer.Stop(); Console.WriteLine("FAIL QA: " + ex); if (app.MainWindow is MainWindow closingWindow) closingWindow.RequestExit(); else app.Shutdown(1); }
            finally { advancing = false; }
        };
        timer.Start();
        var result = app.Run();
        if (args.Contains("--icon-hotfix-ui") && !failed && result == 0) BrandingQa.VerifyTrayDisposed();
        Console.WriteLine($"App exited: {result}; capture commands were never executed.");
        return failed || result != 0 ? 1 : 0;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); }
    }
}






