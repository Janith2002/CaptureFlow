using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptureFlow.Application.Video;
using CaptureFlow.Core.Video;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace CaptureFlow.Desktop.ViewModels;

public partial class ScreenRecorderViewModel
{
    private readonly ICameraPreviewService cameras;
    private readonly DispatcherTimer previewTimer;
    private readonly CancellationTokenSource cameraCancellation = new();
    private Task cameraTransition = Task.CompletedTask;
    public Task CameraTransition => cameraTransition;
    private IReadOnlyList<CameraFormat> supportedCameraFormats = [];
    private bool suppressCameraSelection;
    private bool previewUpdatePending;
    private Task previewUpdate = Task.CompletedTask;
    private int previewGeneration;
    private WriteableBitmap? previewBitmap;
    private byte[] previewPixels = [];
    public ObservableCollection<CameraDevice> Cameras { get; } = [];
    public ObservableCollection<string> CameraResolutions { get; } = [];
    public ObservableCollection<CameraFormat> CameraFrameRates { get; } = [];
    [ObservableProperty] private CameraDevice? selectedCamera;
    [ObservableProperty] private string? selectedCameraResolution;
    [ObservableProperty] private CameraFormat? selectedCameraFormat;
    [ObservableProperty] private bool isCameraBusy;
    [ObservableProperty] private bool isCameraPreviewing;
    [ObservableProperty] private ImageSource? cameraPreview;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoCameras))]
    private string? cameraDiscoveryError;
    [ObservableProperty] private string cameraMessage = "Choose a camera mode, select a camera and start preview. Camera access begins when you load the selected device.";
    public bool IsWebcam => SelectedMode == "Webcam Only";
    public bool IsScreen => SelectedMode is "Screen Only" or "Full Screen" or "Screen + Webcam";
    public bool CanChangeCamera => CanEditPreferences && !IsCameraBusy;
    public bool HasNoCameras => Cameras.Count == 0 && CameraDiscoveryError is null;
    public bool HasNoPreview => CameraPreview is null;
    public string CameraStateLabel => IsCameraBusy ? "Opening camera…" : IsCameraPreviewing ? Status switch
    {
        VideoRecordingState.Recording => "Recording · live camera",
        VideoRecordingState.Paused => "Paused · preview stays live",
        VideoRecordingState.Completed => "Previewing · recording stopped",
        VideoRecordingState.Saving => "Saving · preview stays live",
        _ => "Previewing · no recording"
    } : "Stopped · camera preview unavailable";

    partial void OnSelectedModeChanged(string? oldValue, string newValue)
    {
        var value = newValue;
        if (disposed || !CanEditPreferences || !cameraTransition.IsCompleted)
        {
            selectedMode = oldValue ?? "Screen Only";
            CameraMessage = "Finish or cancel the current camera/recording operation before changing recording mode.";
            return;
        }
        OnPropertyChanged(nameof(IsWebcam)); OnPropertyChanged(nameof(IsScreen)); OnPropertyChanged(nameof(IsCombined)); OnPropertyChanged(nameof(IsCameraMode)); OnPropertyChanged(nameof(IsScreenOnly)); RefreshCommands();
        SourceSelectionChanged();
        if (Status == VideoRecordingState.Idle)
            StatusMessage = IsCameraMode ? "Choose video sources and start live preview before recording. Audio is optional." : "Select a screen, window or region and optional audio sources before recording.";
        if (!disposed) cameraTransition = ChangeVideoModeAsync();
    }
    private async Task ChangeVideoModeAsync()
    {
        if (screenPreview is not null && screenPreview.ActiveOptions is not null) { ResetPreview(); await screenPreview.StopAsync(); }
        if (IsCameraMode) { if (SelectedCamera is not null) await LoadCameraAsync(); }
        else
        {
            IsCameraBusy = true;
            ResetPreview();
            try { await ReleasePreviewAsync(); }
            catch (Exception ex) { CameraMessage = ex.Message; }
            finally { IsCameraBusy = false; }
        }
    }
    partial void OnSelectedCameraChanged(CameraDevice? value)
    {
        RefreshCommands();
        if (!disposed && !suppressCameraSelection && IsCameraMode && CanEditPreferences && cameraTransition.IsCompleted) cameraTransition = LoadCameraAsync();
    }
    partial void OnSelectedCameraResolutionChanged(string? value)
    {
        CameraFrameRates.Clear();
        foreach (var format in CameraFormatSelection.FrameRates(supportedCameraFormats, value ?? "")) CameraFrameRates.Add(format);
        SelectedCameraFormat = CameraFrameRates.OrderBy(f => Math.Abs(f.FramesPerSecond - 30)).FirstOrDefault();
    }
    partial void OnSelectedCameraFormatChanged(CameraFormat? value)
    {
        if (IsCameraPreviewing && value != cameras.ActiveFormat) CameraMessage = "Format changed. Click Start preview to apply it before recording.";
        RefreshCommands();
        NotifyOverlayBounds();
    }
    partial void OnIsCameraBusyChanged(bool value) { OnPropertyChanged(nameof(CameraStateLabel)); RefreshCommands(); }
    partial void OnIsCameraPreviewingChanged(bool value) { OnPropertyChanged(nameof(CameraStateLabel)); RefreshCommands(); }
    partial void OnCameraPreviewChanged(ImageSource? value) { OnPropertyChanged(nameof(HasNoPreview)); OnPropertyChanged(nameof(StudioPreviewWidth)); OnPropertyChanged(nameof(StudioPreviewHeight)); }
    private void RefreshCameraCommands()
    {
        OnPropertyChanged(nameof(CanChangeCamera)); OnPropertyChanged(nameof(HasNoCameras));
        LoadCameraCommand.NotifyCanExecuteChanged(); StartPreviewCommand.NotifyCanExecuteChanged(); StopPreviewCommand.NotifyCanExecuteChanged();
    }
    private bool CanLoadCamera() => IsCameraMode && CanChangeCamera && SelectedCamera is not null;
    private bool CanStartPreview() => CanLoadCamera() && SelectedCameraFormat is not null && (!IsCombined || SelectedScreenOptions is not null);
    private bool CanStopPreview() => IsCameraMode && CanChangeCamera && cameras.DeviceId is not null;
    public async Task StopCameraOperationsAsync(TimeSpan? timeout = null, TimeProvider? timeProvider = null)
    {
        cameraCancellation.Cancel(); ResetPreview();
        var tasks = new List<Task> { cameraTransition, previewUpdate };
        foreach (var command in new[] { LoadCameraCommand, StartPreviewCommand, StopPreviewCommand, StartScreenPreviewCommand, StopScreenPreviewCommand }) if (command.ExecutionTask is { } task) tasks.Add(task);
        await Task.WhenAll(tasks).WaitAsync(timeout ?? TimeSpan.FromSeconds(30), timeProvider ?? TimeProvider.System);
        await studioSave;
        if (screenPreview is not null && !screenPreview.IsRecording) await screenPreview.StopAsync();
    }
    [RelayCommand(CanExecute = nameof(CanLoadCamera))]
    private async Task LoadCameraAsync()
    {
        if (disposed || IsCameraBusy) return;
        var device = SelectedCamera;
        var token = cameraCancellation.Token;
        IsCameraBusy = true; ResetPreview();
        supportedCameraFormats = []; CameraResolutions.Clear(); CameraFrameRates.Clear(); SelectedCameraResolution = null; SelectedCameraFormat = null;
        try
        {
            await Task.Run(combined.StopPreviewAsync);
            if (device is null)
            {
                await cameras.StopPreviewAsync(); CameraMessage = CameraDiscoveryError ?? "No camera connected. Connect a built-in or USB webcam and refresh devices."; return;
            }
            CameraMessage = "Loading formats reported by " + device.Name + "…";
            supportedCameraFormats = await cameras.SelectCameraAsync(device.Id, token);
            foreach (var resolution in CameraFormatSelection.Resolutions(supportedCameraFormats)) CameraResolutions.Add(resolution);
            SelectedCameraResolution = CameraResolutions.Contains("1280 × 720") ? "1280 × 720" : CameraResolutions.FirstOrDefault();
            CameraMessage = "Camera loaded. Choose a reported resolution and frame rate, then Start preview. Only those device formats are listed.";
            if (presetCameraFormat is { } savedFormat)
            {
                presetCameraFormat = null;
                if (supportedCameraFormats.Contains(savedFormat)) { SelectedCameraResolution = savedFormat.ResolutionLabel; SelectedCameraFormat = savedFormat; }
                else { SelectedCameraFormat = null; CameraMessage = "The preset camera format is unavailable. Choose a reported supported format before preview or recording."; }
            }
        }
        catch (OperationCanceledException) { CameraMessage = "Camera loading cancelled."; }
        catch (Exception ex) { CameraMessage = ex.Message; }
        finally { IsCameraBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanStartPreview))]
    private async Task StartPreviewAsync()
    {
        var device = SelectedCamera; var format = SelectedCameraFormat;
        if (disposed || device is null || format is null) return;
        var token = cameraCancellation.Token;
        IsCameraBusy = true; ResetPreview();
        try
        {
            await Task.Run(combined.StopPreviewAsync);
            if (cameras.DeviceId != device.Id)
            {
                var reported = await cameras.SelectCameraAsync(device.Id, token);
                if (!reported.Contains(format)) throw new NotSupportedException("Camera formats changed. Reload the camera and select a reported format.");
            }
            await cameras.StartPreviewAsync(format, token);
            if (IsCombined) await Task.Run(() => combined.StartPreviewAsync(CombinedSourceOptions!, token), token);
            IsCameraPreviewing = true;
            if (Status == VideoRecordingState.Idle) StatusMessage = "Camera preview is ready. Choose microphone audio or record video only, then Start Recording.";
            CameraMessage = $"Live preview · {device.Name} · {format.ResolutionLabel} · {format.FrameRateLabel}. Preview continues after Stop; Stop preview releases capture.";
            previewTimer.Start();
        }
        catch (Exception ex)
        {
            var message = ex is OperationCanceledException ? "Preview cancelled." : ex.Message;
            try { await ReleasePreviewAsync(); } catch (Exception cleanup) { message += " Cleanup: " + cleanup.Message; }
            CameraMessage = message;
        }
        finally { IsCameraBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanStopPreview))]
    private async Task StopPreviewAsync()
    {
        IsCameraBusy = true; ResetPreview();
        try { await ReleasePreviewAsync(); CameraMessage = "Stopped. Video sources released. Start preview to reopen them."; }
        catch (Exception ex) { CameraMessage = ex.Message; }
        finally { IsCameraBusy = false; }
    }
    private async Task ReleasePreviewAsync()
    {
        try { await Task.Run(combined.StopPreviewAsync); }
        finally { await cameras.StopPreviewAsync(); }
    }
    private void ResetPreview()
    {
        previewGeneration++; previewTimer.Stop(); IsCameraPreviewing = false; CameraPreview = null;
        previewBitmap = null; previewPixels = [];
        OnPropertyChanged(nameof(PreviewPixelWidth)); OnPropertyChanged(nameof(PreviewPixelHeight)); NotifyOverlayBounds();
    }
    private async Task UpdatePreviewAsync()
    {
        if (disposed || previewUpdatePending || !IsCameraPreviewing) return;
        previewUpdatePending = true;
        var generation = previewGeneration;
        var token = cameraCancellation.Token;
        try
        {
            using var frame = await Task.Run(async () => IsCombined ? await combined.ReadPreviewFrameAsync(token) : IsWebcam ? await cameras.ReadPreviewFrameAsync(token) : screenPreview is null ? null : await screenPreview.ReadPreviewAsync(token), token);
            if (frame is null) return;
            if (disposed || generation != previewGeneration || !IsCameraPreviewing) return;
            if (previewBitmap is null || previewBitmap.PixelWidth != frame.Size.Width || previewBitmap.PixelHeight != frame.Size.Height)
            {
                previewBitmap = new WriteableBitmap(frame.Size.Width, frame.Size.Height, 96, 96, PixelFormats.Bgra32, null);
                previewPixels = new byte[checked(frame.Size.Width * frame.Size.Height * 4)]; CameraPreview = previewBitmap;
                OnPropertyChanged(nameof(PreviewPixelWidth)); OnPropertyChanged(nameof(PreviewPixelHeight)); NotifyOverlayBounds();
            }
            frame.Pixels.Span.CopyTo(previewPixels);
            previewBitmap.WritePixels(new Int32Rect(0, 0, frame.Size.Width, frame.Size.Height), previewPixels, checked(frame.Size.Width * 4), 0);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!disposed && generation == previewGeneration) CameraFailed(this, ex); }
        finally { previewUpdatePending = false; }
    }
    private void CameraFailed(object? sender, Exception error)
    {
        var generation = previewGeneration;
        dispatcher.BeginInvoke(() => { if (disposed || generation != previewGeneration) return; ResetPreview(); CameraMessage = error.Message; });
    }
}
