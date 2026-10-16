using System.Collections.ObjectModel;
using System.Windows.Threading;
using CaptureFlow.Core.Audio;
using CaptureFlow.Core.Video;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaptureFlow.Core.Desktop;
using CaptureFlow.Application.Desktop;
namespace CaptureFlow.Desktop.ViewModels;
public partial class ScreenRecorderViewModel : ObservableObject, IDisposable
{
    private readonly IVideoRecordingService recorder;
    private readonly IAudioCaptureBackend audioBackend;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer timer;
    private CancellationTokenSource? startCancellation;
    private bool disposed;
    private readonly IRecordingCountdown countdown;
    private readonly RecordingExperienceViewModel? experience;
    public bool IsCountingDown => startCancellation is not null && countdown.IsRunning && countdown.Remaining > 0;
    partial void OnSelectedCountdownChanged(string value) { if (experience is not null) experience.SelectedCountdown = value; StartCommand.NotifyCanExecuteChanged(); }
    private void ExperienceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(RecordingExperienceViewModel.SelectedCountdown)) SelectedCountdown = experience!.SelectedCountdown; }
    private void CountdownChanged(object? sender, EventArgs e) { OnPropertyChanged(nameof(IsCountingDown)); if (IsCountingDown) StatusMessage = $"Recording starts in {countdown.Remaining}…"; }
    public event EventHandler? RecordingSaved;
    public string OutputDirectory { get; set; } = "";
    public ObservableCollection<MonitorDescriptor> Monitors { get; } = [];
    public ObservableCollection<MicrophoneDevice> Microphones { get; } = [];
    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = [];
    [ObservableProperty] private MonitorDescriptor? selectedMonitor;
    [ObservableProperty] private MicrophoneDevice? selectedMicrophone;
    [ObservableProperty] private AudioOutputDevice? selectedOutputDevice;
    [ObservableProperty] private string ffmpegPath = "ffmpeg.exe";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private VideoRecordingState status;
    [ObservableProperty] private string elapsedText = "00:00:00";
    [ObservableProperty] private string captureExclusionNotice = "";
    [ObservableProperty] private string statusMessage = "Choose Entire Screen, Application Window or Custom Region. Recording requires FFmpeg with libx264 and AAC.";
    [ObservableProperty] private string selectedMode = "Screen Only";
    [ObservableProperty] private string selectedCountdown = "3 seconds";
    [ObservableProperty] private bool microphoneEnabled = true;
    [ObservableProperty] private bool systemAudioEnabled = true;
    [ObservableProperty] private bool microphoneMuted;
    [ObservableProperty] private bool systemAudioMuted;
    [ObservableProperty] private double microphoneVolume = 80;
    [ObservableProperty] private double systemAudioVolume = 100;
    [ObservableProperty] private string selectedQuality = "High";
    [ObservableProperty] private string selectedResolution = "Original/source";
    [ObservableProperty] private string selectedFrameRate = "30 FPS";
    [ObservableProperty] private bool showMouseCursor = true;
    [ObservableProperty] private bool highlightMouseClicks;
    public string[] ModeOptions { get; } = ["Screen Only", "Webcam Only", "Screen + Webcam"];
    public string[] CountdownOptions { get; } = ["Off", "3 seconds", "5 seconds", "10 seconds"];
    public string[] QualityOptions { get; } = ["Low", "Standard", "High", "Ultra", "Custom"];
    public string[] ResolutionOptions { get; } = ResolutionLabels;
    public string[] FrameRateOptions { get; } = ["24 FPS", "30 FPS", "60 FPS"];
    public string StatusLabel => Status.ToString();
    public bool CanEditPreferences => !IsBusy && !IsCameraBusy && Status is not (VideoRecordingState.Starting or VideoRecordingState.Recording or VideoRecordingState.Paused or VideoRecordingState.Saving);
    public ScreenRecorderViewModel(IVideoRecordingService recorder, IAudioCaptureBackend audioBackend, ICameraPreviewService cameras,
        ICombinedVideoPreviewService combined, IOverlayLayoutStore layouts, IVideoQualityPreferencesStore? qualityStore = null, IVideoEncoderCapabilities? encoderCapabilities = null, IRecordingCountdown? countdown = null, RecordingExperienceViewModel? experience = null,
        IApplicationWindowDiscovery? windowDiscovery = null, IRegionSelection? regionSelection = null, IRegionPreferencesStore? regionStore = null, IMouseEffectsPreferencesStore? mouseStore = null, IStudioPreferencesStore? studioStore = null, IScreenPreviewService? screenPreview = null)
    {
        this.recorder = recorder; this.audioBackend = audioBackend; this.cameras = cameras; this.combined = combined; this.layouts = layouts;
        this.qualityStore = qualityStore; this.encoderCapabilities = encoderCapabilities;
        this.windowDiscovery = windowDiscovery; this.regionSelection = regionSelection; this.regionStore = regionStore;
        this.mouseStore = mouseStore; this.studioStore = studioStore; this.screenPreview = screenPreview;
        this.countdown = countdown ?? new RecordingCountdown(); this.experience = experience; this.countdown.Changed += CountdownChanged;
        if (experience is not null) { SelectedCountdown = experience.SelectedCountdown; experience.PropertyChanged += ExperienceChanged; }
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => UpdateTimer(), dispatcher);
        timer.Stop(); recorder.StateChanged += StateChanged;
        previewTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => { if (!previewUpdatePending) previewUpdate = UpdatePreviewAsync(); }, dispatcher);
        previewTimer.Stop(); cameras.Failed += CameraFailed;
        combined.Failed += CameraFailed;
    }
    private bool CanStart() => CanEditPreferences && CountdownOptions.Contains(SelectedCountdown) && !IsCameraBusy && !IsCheckingEncoders && ValidQuality && (IsWebcam || ValidMousePreferences) &&
        (IsCombined ? IsCameraPreviewing && combined.IsPreviewing && combined.ActiveOptions == CombinedSourceOptions : IsWebcam ? IsCameraPreviewing && SelectedCamera is not null && SelectedCameraFormat == cameras.ActiveFormat : IsScreen && SelectedScreenOptions is not null) &&
        (!MicrophoneEnabled || SelectedMicrophone is not null) && (!IsScreen || !SystemAudioEnabled || SelectedOutputDevice is not null) && !string.IsNullOrWhiteSpace(FfmpegPath);
    private bool CanPause() => !IsBusy && Status == VideoRecordingState.Recording;
    private bool CanResume() => !IsBusy && Status == VideoRecordingState.Paused;
    private bool CanStop() => !IsBusy && Status is VideoRecordingState.Recording or VideoRecordingState.Paused;
    private bool CanCancel() => startCancellation is not null || Status is VideoRecordingState.Starting or VideoRecordingState.Recording or VideoRecordingState.Paused or VideoRecordingState.Saving;
    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanEditPreferences));
        StartCommand.NotifyCanExecuteChanged(); PauseCommand.NotifyCanExecuteChanged(); ResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); RefreshDevicesCommand.NotifyCanExecuteChanged();
        RefreshCameraCommands();
        RefreshWindowsCommand.NotifyCanExecuteChanged(); SelectRegionCommand.NotifyCanExecuteChanged();
        QualityChanged();
        StartScreenPreviewCommand.NotifyCanExecuteChanged(); StopScreenPreviewCommand.NotifyCanExecuteChanged();
        StartStudioPreviewCommand.NotifyCanExecuteChanged(); StopStudioPreviewCommand.NotifyCanExecuteChanged();
        AddStudioSourceCommand.NotifyCanExecuteChanged(); RemoveStudioSourceCommand.NotifyCanExecuteChanged();
        SavePresetCommand.NotifyCanExecuteChanged(); ApplyPresetCommand.NotifyCanExecuteChanged(); RenamePresetCommand.NotifyCanExecuteChanged(); DeletePresetCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsBusyChanged(bool value) => RefreshCommands();
    partial void OnStatusChanged(VideoRecordingState value) { OnPropertyChanged(nameof(StatusLabel)); RefreshCommands(); }
    partial void OnSelectedMonitorChanged(MonitorDescriptor? value) { RestoreRegionForMonitor(); SourceSelectionChanged(); if (IsCombined && IsCameraPreviewing) CameraMessage = "Monitor changed. Start preview to apply the selected monitor before recording."; }
    partial void OnShowMouseCursorChanged(bool value) => MousePreferencesChanged();
    partial void OnSelectedMicrophoneChanged(MicrophoneDevice? value) => RefreshCommands();
    partial void OnSelectedOutputDeviceChanged(AudioOutputDevice? value) => RefreshCommands();
    partial void OnMicrophoneEnabledChanged(bool value) => RefreshCommands();
    partial void OnSystemAudioEnabledChanged(bool value) => RefreshCommands();
    partial void OnFfmpegPathChanged(string value) => RefreshCommands();
    partial void OnMicrophoneVolumeChanged(double value) => UpdateVolumes();
    partial void OnSystemAudioVolumeChanged(double value) => UpdateVolumes();
    partial void OnMicrophoneMutedChanged(bool value) => UpdateVolumes();
    partial void OnSystemAudioMutedChanged(bool value) => UpdateVolumes();
    private void UpdateVolumes() => recorder.SetAudioVolume((float)(MicrophoneVolume / 100), MicrophoneMuted, (float)(SystemAudioVolume / 100), SystemAudioMuted);
    [RelayCommand(CanExecute = nameof(CanEditPreferences))]
    private async Task RefreshDevicesAsync()
    {
        IsBusy = true;
        var errors = new List<string>();
        try
        {
            ResetPreview();
            await Task.Run(combined.StopPreviewAsync);
            var monitorId = SelectedMonitor?.Id; var micId = SelectedMicrophone?.Id; var outputId = SelectedOutputDevice?.Id;
            Monitors.Clear(); Microphones.Clear(); OutputDevices.Clear();
            try { foreach (var device in await recorder.GetMonitorsAsync()) Monitors.Add(device); }
            catch (Exception ex) { errors.Add("Monitors: " + ex.Message); }
            try { foreach (var device in await audioBackend.GetDevicesAsync()) Microphones.Add(device); }
            catch (Exception ex) { errors.Add("Microphones: " + ex.Message); }
            try { foreach (var device in await audioBackend.GetOutputDevicesAsync()) OutputDevices.Add(device); }
            catch (Exception ex) { errors.Add("Playback devices: " + ex.Message); }
            SelectedMonitor = Monitors.FirstOrDefault(d => d.Id == monitorId) ?? Monitors.FirstOrDefault(d => d.IsPrimary) ?? Monitors.FirstOrDefault();
            SelectedMicrophone = Microphones.FirstOrDefault(d => d.Id == micId) ?? Microphones.FirstOrDefault();
            SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == outputId) ?? OutputDevices.FirstOrDefault();
            var cameraId = SelectedCamera?.Id;
            suppressCameraSelection = true;
            try
            {
                Cameras.Clear();
                CameraDiscoveryError = null;
                try { foreach (var device in await cameras.GetDevicesAsync(cameraCancellation.Token)) Cameras.Add(device); }
                catch (Exception ex) { CameraDiscoveryError = ex.Message; CameraMessage = ex.Message; errors.Add("Cameras: " + ex.Message); }
                if (CameraDiscoveryError is null) CameraMessage = Cameras.Count == 0
                    ? "No camera connected. Connect a built-in or USB webcam and refresh devices."
                    : $"Detected {Cameras.Count} camera(s). Choose a camera mode to load its reported formats.";
                SelectedCamera = Cameras.FirstOrDefault(d => d.Id == cameraId) ?? Cameras.FirstOrDefault();
            }
            finally { suppressCameraSelection = false; }
            StatusMessage = errors.Count > 0 ? string.Join(" ", errors) : $"Detected {Monitors.Count} monitor(s). Choose audio sources or turn both off for video only.";
        }
        catch (Exception ex) { StatusMessage = "Could not refresh video/audio devices: " + ex.Message; }
        finally { IsBusy = false; }
        if (IsCameraMode) await LoadCameraAsync();
    }
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        using var cancellation = new CancellationTokenSource();
        startCancellation = cancellation; IsBusy = true; ElapsedText = "00:00:00";
        try
        {
            var seconds = new RecordingExperienceViewModel { SelectedCountdown = SelectedCountdown }.CountdownSeconds;
            await countdown.RunAsync(new(seconds, UsesMonitor ? SelectedMonitor?.Id : null), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            AudioRecordingOptions? audio = null;
            var includeSystemAudio = IsScreen && SystemAudioEnabled;
            if (MicrophoneEnabled || includeSystemAudio)
            {
                var mode = !MicrophoneEnabled ? AudioRecordingMode.SystemAudioOnly : !includeSystemAudio ? AudioRecordingMode.MicrophoneOnly : AudioRecordingMode.MicrophoneAndSystemAudio;
                audio = new(SelectedMicrophone?.Id ?? "", OutputDirectory, 48000, (float)(MicrophoneVolume / 100), MicrophoneMuted,
                    mode, SelectedOutputDevice?.Id, (float)(SystemAudioVolume / 100), SystemAudioMuted, LevelObserver: ObserveAudioLevels);
            }
            var source = IsCombined ? CombinedSourceOptions! : IsWebcam ? new VideoSourceOptions(VideoSourceKind.Webcam, SelectedCamera!.Id, CameraFormat: SelectedCameraFormat) : SelectedScreenOptions!;
            var quality = QualitySettings;
            var configuration = new VideoRecordingConfiguration(source, OutputDirectory, quality.FramesPerSecond, Audio: audio, FfmpegPath: FfmpegPath, QualityPreferences: quality);
            await Task.Run(() => recorder.StartAsync(configuration, cancellation.Token));
            ApplyState();
        }
        catch (OperationCanceledException) { Status = VideoRecordingState.Cancelled; StatusMessage = "Recording start cancelled."; }
        catch (Exception ex) { Status = VideoRecordingState.Error; StatusMessage = ex.Message; }
        finally { startCancellation = null; IsBusy = false; OnPropertyChanged(nameof(IsCountingDown)); }
    }
    [RelayCommand(CanExecute = nameof(CanPause))] private Task PauseAsync() => RunAsync(recorder.PauseAsync);
    [RelayCommand(CanExecute = nameof(CanResume))] private Task ResumeAsync() => RunAsync(recorder.ResumeAsync);
    [RelayCommand(CanExecute = nameof(CanStop))] private Task StopAsync() => RunAsync(() => recorder.StopAsync());
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        startCancellation?.Cancel();
        if (countdown.IsRunning) { countdown.Cancel(); return; }
        await RunAsync(recorder.CancelAsync);
    }
    private async Task RunAsync(Func<Task> operation)
    {
        IsBusy = true;
        try { await Task.Run(operation); ApplyState(); }
        catch (Exception ex) { Status = VideoRecordingState.Error; StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }
    private void StateChanged(object? sender, EventArgs e)
    {
        if (disposed) return;
        var state = recorder.State; var message = recorder.ErrorMessage; var saved = recorder.SavedFilePath;
        dispatcher.BeginInvoke(() => ApplyState(state, message, saved));
    }
    private void ApplyState() => ApplyState(recorder.State, recorder.ErrorMessage, recorder.SavedFilePath);
    private void ApplyState(VideoRecordingState state, string? error, string? saved)
    {
        if (disposed) return;
        EncodingStatus = recorder.EncodingStatus ?? "";
        var wasCompleted = Status == VideoRecordingState.Completed;
        Status = state; UpdateTimer();
        if (state == VideoRecordingState.Recording) timer.Start(); else timer.Stop();
        StatusMessage = state switch
        {
            VideoRecordingState.Starting => "Opening the selected video source, FFmpeg and audio devices…",
            VideoRecordingState.Recording => IsCombined ? "Recording screen + webcam. Overlay changes apply to subsequent frames; audio gain and mute are live." : IsWebcam ? "Recording the selected webcam. Microphone gain and mute affect the saved audio." : $"Recording {SelectedScreenSource}. Volume and mute affect the saved audio.",
            VideoRecordingState.Paused => "Paused. Paused time is omitted from video and audio.",
            VideoRecordingState.Saving => "Finalizing, muxing and checking the MP4…",
            VideoRecordingState.Completed => "Saved MP4: " + saved,
            VideoRecordingState.Error or VideoRecordingState.Cancelled => error ?? "Recording cancelled.",
            _ => StatusMessage
        };
        OnPropertyChanged(nameof(CameraStateLabel));
        if (state == VideoRecordingState.Completed && !wasCompleted) RecordingSaved?.Invoke(this, EventArgs.Empty);
    }
    private void UpdateTimer()
    {
        var elapsed = recorder.Duration;
        ElapsedText = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        UpdateStudioMeters();
        OnPropertyChanged(nameof(StudioConfigurationSummary));
    }
    public void Dispose() { if (disposed) return; disposed = true; sourceSelectionCancellation.Cancel(); sourceSelectionCancellation.Dispose(); startCancellation?.Cancel(); countdown.Changed -= CountdownChanged; if (experience is not null) experience.PropertyChanged -= ExperienceChanged; qualityCancellation.Cancel(); qualityCancellation.Dispose(); cameraCancellation.Cancel(); cameraCancellation.Dispose(); previewTimer.Stop(); timer.Stop(); cameras.Failed -= CameraFailed; combined.Failed -= CameraFailed; recorder.StateChanged -= StateChanged; }
}


