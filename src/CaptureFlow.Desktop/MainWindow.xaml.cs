using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using CaptureFlow.Infrastructure.Video;
using CaptureFlow.Core.Audio;
using CaptureFlow.Core.Video;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Desktop.Services;
using CaptureFlow.Application.Desktop;
namespace CaptureFlow.Desktop;
public partial class MainWindow : Window
{
    private readonly IAudioRecordingService recorder;
    private readonly AudioRecorderViewModel audio;
    private readonly IVideoRecordingService video;
    private readonly ICameraPreviewService cameras;
    private readonly ICombinedVideoPreviewService combined;
    private readonly ScreenRecorderViewModel screen;
    private bool closing;
    private bool readyToClose;
    private bool explicitExit;
    private WindowDesktopIntegration? desktopIntegration;
    public Task DesktopInitialization { get; private set; } = Task.CompletedTask;
    private readonly DashboardViewModel dashboard;
    private readonly CaptureFlow.Core.Desktop.IRecordingCountdown countdown;
    public MainWindow(DashboardViewModel viewModel, IAudioRecordingService recorder, IVideoRecordingService video, ICameraPreviewService cameras, ICombinedVideoPreviewService combined, CaptureFlow.Core.Desktop.IRecordingCountdown countdown)
    {
        InitializeComponent(); DataContext = viewModel; dashboard = viewModel;
        this.recorder = recorder; audio = viewModel.Audio; this.video = video; this.cameras = cameras; this.combined = combined; screen = viewModel.Screen;
        this.countdown = countdown;
        Closing += OnClosing;
        Loaded += FitInitialWorkArea;
        SourceInitialized += (_, _) =>
        {
            var darkCaption = 1;
            _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref darkCaption, sizeof(int));
            if (!WindowCaptureExclusion.TryExclude(new WindowInteropHelper(this).Handle))
                screen.CaptureExclusionNotice = "Windows could not exclude CaptureFlow from screen capture. Minimize this window or move it off the recorded monitor to avoid capturing the preview/editor.";
            desktopIntegration = new(this, dashboard, HwndSource.FromHwnd(new WindowInteropHelper(this).Handle), countdown);
            DesktopInitialization = desktopIntegration.InitializeAsync();
        };
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private void FitInitialWorkArea(object sender, RoutedEventArgs e)
    {
        Loaded -= FitInitialWorkArea;
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var areas = DesktopWorkAreas.Enumerate();
            var anchor = CaptureFlow.Core.Desktop.ToolbarPlacement.Capture(DesktopWorkAreas.Bounds(handle), areas);
            var rectangle = CaptureFlow.Core.Desktop.ToolbarPlacement.Resolve(anchor, areas, ActualWidth, ActualHeight);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            SetCurrentValue(WidthProperty, Math.Max(MinWidth, rectangle.Width / dpi.DpiScaleX));
            SetCurrentValue(HeightProperty, Math.Max(MinHeight, rectangle.Height / dpi.DpiScaleY));
            DesktopWorkAreas.Place(handle, rectangle);
        }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError("Initial window placement failed: {0}", ex.Message); }
    }
    public void RequestExit() { explicitExit = true; Close(); }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (readyToClose) return;
        e.Cancel = true;
        if (closing) return;
        if (WindowClosePolicy.HideInsteadOfClose(dashboard.Shortcuts.AppliedMinimizeToTray, explicitExit, desktopIntegration?.TrayAvailable == true)) { Hide(); return; }
        if (dashboard.Editor?.ConfirmDiscard() == false) { explicitExit = false; return; }
        var wasCountingDown = countdown.IsRunning;
        if (wasCountingDown) countdown.Cancel();
        if (WindowClosePolicy.NeedsConfirmation(desktopIntegration?.Active == true, !wasCountingDown && desktopIntegration?.Busy == true) &&
            MessageBox.Show(this, "Finish the current recording and exit CaptureFlow? Pending starts will be cancelled; exporting recordings will finish before exit.", "Exit CaptureFlow", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        { explicitExit = false; return; }
        closing = true;
        IsEnabled = false;
        try
        {
            try { await screen.StopScreenSelectionOperationsAsync(); await DesktopInitialization; if (desktopIntegration is not null) await desktopIntegration.FinalizeAsync(); }
            finally
            {
                desktopIntegration?.Dispose();
                var cleanupErrors = new List<Exception>();
                try { await screen.StopQualityOperationsAsync(); } catch (Exception ex) { cleanupErrors.Add(ex); }
                try { await screen.StopCameraOperationsAsync(); } catch (Exception ex) { cleanupErrors.Add(ex); }
                screen.Dispose();
                try { await Task.WhenAll(dashboard.DisposeAsync().AsTask(), Task.Run(DisposeRecordingResourcesAsync)); } catch (Exception ex) { cleanupErrors.Add(ex); }
                if (cleanupErrors.Count > 0) throw new AggregateException("CaptureFlow cleanup reported errors.", cleanupErrors);
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "CaptureFlow shutdown", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { audio.Dispose(); readyToClose = true; _ = Dispatcher.BeginInvoke(Close); }
    }
    private async Task DisposeRecordingResourcesAsync()
    {
        var errors = new List<Exception>();
        IAsyncDisposable[] resources = [video, combined, cameras, recorder];
        foreach (var resource in resources)
        {
            try { await resource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new AggregateException("Some recording resources failed during shutdown.", errors);
    }
}






