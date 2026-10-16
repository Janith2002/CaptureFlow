using System.Windows;
using CaptureFlow.Application.Audio;
using CaptureFlow.Application.Video;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
using CaptureFlow.Core.Audio;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Infrastructure.Audio;
using System.IO;
using CaptureFlow.Core.Library;
using CaptureFlow.Application.Library;
using CaptureFlow.Infrastructure.Library;
using CaptureFlow.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CaptureFlow.Desktop;
public partial class App : System.Windows.Application
{
    private IHost? host;
    private System.Diagnostics.TextWriterTraceListener? cameraDiagnostics;
    public App()
    {
        StartupDiagnostics.Record("application-constructed");
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedException;
    }
    private void OnDispatcherException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        => StartupDiagnostics.Record("dispatcher-unhandled-exception", exception: e.Exception); // Deliberately not marked handled.
    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        => StartupDiagnostics.Record("domain-unhandled-exception", new { e.IsTerminating }, e.ExceptionObject as Exception);
    private void OnUnobservedException(object? sender, UnobservedTaskExceptionEventArgs e)
        => StartupDiagnostics.Record("task-unobserved-exception", exception: e.Exception); // Do not silently SetObserved.
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartupDiagnostics.Record("startup-enter");
        try
        {
            ApplicationBranding.InitializeIdentity();
            try
            {
                var diagnostics = Environment.GetEnvironmentVariable("CAPTUREFLOW_CAMERA_DIAGNOSTICS") ?? Path.Combine(CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root, "Logs", "camera-lifecycle.log");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(diagnostics))!);
                // The owned diagnostic log contains lifecycle/exception details, never captured media or input history.
                if (File.Exists(diagnostics) && new FileInfo(diagnostics).Length > 1_048_576) diagnostics += "." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                cameraDiagnostics = new System.Diagnostics.TextWriterTraceListener(diagnostics);
                System.Diagnostics.Trace.Listeners.Add(cameraDiagnostics); System.Diagnostics.Trace.AutoFlush = true;
                System.Diagnostics.Trace.TraceInformation("CaptureFlow lifecycle diagnostics initialized.");
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Could not open camera diagnostics: {ex}"); }
            StartupDiagnostics.Record("service-registration-start");
            host = Host.CreateApplicationBuilder().BuildCaptureFlowHost();
            StartupDiagnostics.Record("service-registration-complete");
            await host.StartAsync();
            StartupDiagnostics.Record("host-started");
            StartupDiagnostics.Record("main-window-construction-start");
            var window = host.Services.GetRequiredService<MainWindow>();
            StartupDiagnostics.Record("main-window-construction-complete");
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Loaded += (_, _) => StartupDiagnostics.Record("window-loaded", WindowStateDetails(window));
            window.ContentRendered += (_, _) => StartupDiagnostics.Record("window-content-rendered", WindowStateDetails(window));
            StartupDiagnostics.Record("startup-preferences-start");
            await host.Services.GetRequiredService<RecordingExperienceViewModel>().LoadAsync();
            await host.Services.GetRequiredService<ScreenRecorderViewModel>().LoadQualityPreferencesAsync();
            await host.Services.GetRequiredService<ScreenRecorderViewModel>().LoadRegionPreferencesAsync();
            await host.Services.GetRequiredService<ScreenRecorderViewModel>().LoadMousePreferencesAsync();
            await host.Services.GetRequiredService<ScreenRecorderViewModel>().LoadStudioAsync();
            await host.Services.GetRequiredService<EditorViewModel>().LoadWorkspaceAsync();
            await host.Services.GetRequiredService<ExternalToolsViewModel>().LoadAsync();
            StartupDiagnostics.Record("startup-preferences-complete");
            StartupDiagnostics.Record("window-show-start");
            StartupWindowPresentation.Show(window);
            StartupDiagnostics.Record("window-show-returned", WindowStateDetails(window));
            _ = window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => StartupDiagnostics.Record("dispatcher-idle", WindowStateDetails(window))));
            await host.Services.GetRequiredService<RecoveryViewModel>().LoadAsync();
            await host.Services.GetRequiredService<AudioRecorderViewModel>().RefreshDevicesCommand.ExecuteAsync(null);
            await host.Services.GetRequiredService<ScreenRecorderViewModel>().RefreshDevicesCommand.ExecuteAsync(null);
            StartupDiagnostics.Record("startup-complete");
        }
        catch (Exception ex) { StartupDiagnostics.Record("startup-failed", exception: ex); MessageBox.Show("CaptureFlow could not start. " + ex.Message + "\n\nDiagnostics: " + StartupDiagnostics.LogPath, "CaptureFlow", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    private static object WindowStateDetails(Window window) => new { window.IsVisible, window.Visibility, window.ShowInTaskbar, state = window.WindowState.ToString(), owner = window.Owner?.Title, window.Left, window.Top, window.ActualWidth, window.ActualHeight };
    protected override void OnExit(ExitEventArgs e)
    {
        StartupDiagnostics.Record("shutdown-start", new { e.ApplicationExitCode });
        // Normal window close already drains recording services while the dispatcher remains alive.
        // This fallback must not deadlock that dispatcher if a driver has stalled during cleanup.
        try { if (host is IAsyncDisposable disposable) disposable.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError($"CaptureFlow host shutdown failed or timed out: {ex}"); }
        if (cameraDiagnostics is not null) { System.Diagnostics.Trace.Listeners.Remove(cameraDiagnostics); cameraDiagnostics.Dispose(); }
        base.OnExit(e);
        StartupDiagnostics.Record("shutdown-complete", new { e.ApplicationExitCode });
        DispatcherUnhandledException -= OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedException;
    }
}
internal static class HostRegistration
{
    public static IHost BuildCaptureFlowHost(this HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IFfmpegPreferencesStore>(_ => new CaptureFlow.Infrastructure.Ffmpeg.JsonFfmpegPreferencesStore(
            Environment.GetEnvironmentVariable("CAPTUREFLOW_FFMPEG_PREFERENCES") ?? Path.Combine(CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root, "external-tools.json")));
        builder.Services.AddSingleton<ExternalToolsViewModel>();
        builder.Services.AddSingleton<CaptureFlow.Core.Recovery.IRecoveryJournal>(_ => new CaptureFlow.Infrastructure.Recovery.JsonRecoveryJournal(
            Environment.GetEnvironmentVariable("CAPTUREFLOW_RECOVERY_ROOT") ?? Path.Combine(CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root, "Recovery")));
        builder.Services.AddSingleton<CaptureFlow.Core.Recovery.IRecoveryMediaBackend, CaptureFlow.Infrastructure.Recovery.FfmpegRecoveryBackend>();
        builder.Services.AddSingleton<CaptureFlow.Application.Recovery.RecordingRecoveryService>();
        builder.Services.AddSingleton<RecoveryViewModel>();
        builder.Services.AddSingleton<CaptureFlow.Core.Desktop.IRecordingExperienceStore>(_ => new CaptureFlow.Infrastructure.Desktop.JsonRecordingExperienceStore(
            Environment.GetEnvironmentVariable("CAPTUREFLOW_EXPERIENCE_PREFERENCES") ?? Path.Combine(CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root, "recording-experience.json")));
        builder.Services.AddSingleton<RecordingExperienceViewModel>();
        builder.Services.AddSingleton<CaptureFlow.Core.Desktop.ICountdownPresentation, WpfCountdownPresentation>();
        builder.Services.AddSingleton<CaptureFlow.Core.Desktop.IRecordingCountdown, CaptureFlow.Application.Desktop.RecordingCountdown>();
        builder.Services.AddSingleton<NAudioMicrophoneCaptureBackend>();
        builder.Services.AddSingleton<IAudioCaptureBackend, NAudioAudioCaptureBackend>();
        builder.Services.AddSingleton<IAudioExportBackend, FfmpegAudioExportBackend>();
        builder.Services.AddSingleton<IAudioRecordingService, AudioRecordingService>();
        builder.Services.AddSingleton<AudioRecorderViewModel>();
        builder.Services.AddSingleton<ICameraCaptureBackend>(_ => new WindowsCameraCaptureBackend(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher)));
        builder.Services.AddSingleton<ICameraPreviewService, CameraPreviewService>();
        builder.Services.AddSingleton<WindowsGraphicsCaptureSourceFactory>();
        builder.Services.AddSingleton<IMonitorCaptureBackend>(services => services.GetRequiredService<WindowsGraphicsCaptureSourceFactory>());
        builder.Services.AddSingleton<IApplicationWindowDiscovery, WindowsApplicationWindowDiscovery>();
        builder.Services.AddSingleton<IRegionSelection, WpfRegionSelection>();
        builder.Services.AddSingleton<IRegionPreferencesStore>(_ => new JsonRegionPreferencesStore(Environment.GetEnvironmentVariable("CAPTUREFLOW_REGION_PREFERENCES") ?? Path.Combine(CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root, "screen-region.json")));
        builder.Services.AddSingleton<IMouseEffectsPreferencesStore>(_ => new JsonMouseEffectsPreferencesStore(Environment.GetEnvironmentVariable("CAPTUREFLOW_MOUSE_PREFERENCES") ?? Path.Combine(CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root, "mouse-effects.json")));
        builder.Services.AddSingleton<IOverlayLayoutStore, OverlayLayoutStore>();
        builder.Services.AddSingleton<ICombinedVideoPreviewService, CombinedVideoPreviewService>();
        builder.Services.AddSingleton<IVideoSourceFactory, VideoSourceFactory>();
        builder.Services.AddSingleton<IScreenPreviewService, CaptureFlow.Application.Video.ScreenPreviewService>();
        builder.Services.AddSingleton<IVideoEncoderCapabilities, FfmpegEncoderCapabilities>();
        builder.Services.AddSingleton<IVideoQualityPreferencesStore>(_ => new JsonVideoQualityPreferencesStore(
            Environment.GetEnvironmentVariable("CAPTUREFLOW_QUALITY_PREFERENCES") ?? Path.Combine(CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root, "video-quality.json")));
        builder.Services.AddSingleton<IVideoEncodingBackend, FfmpegVideoEncodingBackend>();
        builder.Services.AddSingleton<IVideoCompositor, BgraVideoCompositor>();
        builder.Services.AddSingleton<IVideoRecordingService, ScreenRecordingService>();
        builder.Services.AddSingleton<ScreenRecorderViewModel>();
        builder.Services.AddSingleton<IStudioPreferencesStore>(_ => new JsonStudioPreferencesStore(
            Environment.GetEnvironmentVariable("CAPTUREFLOW_STUDIO_PREFERENCES") ?? Path.Combine(CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root, "studio.json")));
        builder.Services.AddSingleton<CaptureFlow.Core.Editing.IEditorMediaBackend, CaptureFlow.Infrastructure.Editing.FfmpegEditorBackend>();
        builder.Services.AddSingleton<CaptureFlow.Core.Editing.IEditorProjectStore, CaptureFlow.Infrastructure.Editing.JsonEditorProjectStore>();
        builder.Services.AddSingleton<EditorViewModel>();
        builder.Services.AddSingleton<CaptureFlow.Core.Editing.IEditorWorkspaceStore>(_ => new CaptureFlow.Infrastructure.Editing.JsonEditorWorkspaceStore(
            Environment.GetEnvironmentVariable("CAPTUREFLOW_EDITOR_WORKSPACE") ?? Path.Combine(CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root, "editor-workspace.json")));
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<CaptureFlow.Core.Desktop.IShortcutPreferencesStore>(_ => new CaptureFlow.Infrastructure.Desktop.JsonShortcutPreferencesStore(
            Environment.GetEnvironmentVariable("CAPTUREFLOW_SHORTCUT_PREFERENCES") ?? Path.Combine(CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root, "shortcuts.json")));
        builder.Services.AddSingleton<ShortcutSettingsViewModel>();
        var libraryRoot = CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root;
        var databasePath = Environment.GetEnvironmentVariable("CAPTUREFLOW_LIBRARY_DATABASE") ?? Path.Combine(libraryRoot, "recordings.db");
        builder.Services.AddSingleton<IRecordingRepository>(_ => new SqliteRecordingRepository(databasePath));
        builder.Services.AddSingleton<IMediaMetadataReader, WindowsShellMetadataReader>();
        builder.Services.AddSingleton<IRecordingFileActions, WindowsRecordingFileActions>();
        builder.Services.AddSingleton<IRecordingLibraryService, RecordingLibraryService>();
        builder.Services.AddSingleton<IRecordingThumbnailProvider>(_ => new FfmpegRecordingThumbnailProvider(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "thumbnails")));
        builder.Services.AddSingleton<MainWindow>();
        return builder.Build();
    }
}






