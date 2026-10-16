using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CaptureFlow.Core.Video;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace CaptureFlow.Desktop.ViewModels;

public sealed record StudioSource(string Name, string Kind, string Status)
{ public string Icon => Kind switch { "Webcam" or "Webcam overlay" => "\uE714", "Microphone" => "\uE720", "System audio" => "\uE767", _ => "\uE7F4" }; }
public partial class ScreenRecorderViewModel
{
    private readonly IStudioPreferencesStore? studioStore;
    private bool loadingStudio;
    [ObservableProperty] private bool showStudioSources = true;
    partial void OnShowStudioSourcesChanged(bool value) { if (!disposed && !loadingStudio) studioSave = SaveStudioAsync(); }
    private Task studioSave = Task.CompletedTask;
    private readonly IScreenPreviewService? screenPreview;
    private CameraFormat? presetCameraFormat;
    public bool IsScreenOnly => !IsCameraMode;
    public string StudioPreviewState => IsCameraBusy ? "Changing source" : IsCameraPreviewing ? "Previewing · " + SelectedMode : "Preview stopped";
    public double StudioMinimumWidth => Math.Max(760, StudioSourcesWidth + (ShowStudioProperties ? StudioPropertiesWidth : 0) + 232);
    private bool CanStartScreenPreview() => CanEditPreferences && IsScreenOnly && screenPreview is not null && SelectedScreenOptions is not null;
    private bool CanStopScreenPreview() => CanEditPreferences && IsScreenOnly && screenPreview?.ActiveOptions is not null;
    // The main preview toolbar must also expose the camera prerequisite when the
    // responsive Inspector is hidden. Delegate to the existing lifecycle commands.
    private bool CanStartStudioPreview() => IsCameraMode ? StartPreviewCommand.CanExecute(null) : StartScreenPreviewCommand.CanExecute(null);
    private bool CanStopStudioPreview() => IsCameraMode ? StopPreviewCommand.CanExecute(null) : StopScreenPreviewCommand.CanExecute(null);
    [RelayCommand(CanExecute = nameof(CanStartStudioPreview))]
    private Task StartStudioPreviewAsync() => IsCameraMode ? StartPreviewCommand.ExecuteAsync(null) : StartScreenPreviewCommand.ExecuteAsync(null);
    [RelayCommand(CanExecute = nameof(CanStopStudioPreview))]
    private Task StopStudioPreviewAsync() => IsCameraMode ? StopPreviewCommand.ExecuteAsync(null) : StopScreenPreviewCommand.ExecuteAsync(null);
    [ObservableProperty] private StudioSource? selectedStudioSource;
    public bool IsSelectedCameraSource => SelectedStudioSource?.Kind is "Webcam" or "Webcam overlay";
    public bool IsSelectedAudioSource => SelectedStudioSource?.Kind is "Microphone" or "System audio";
    partial void OnSelectedStudioSourceChanged(StudioSource? value) { OnPropertyChanged(nameof(IsSelectedCameraSource)); OnPropertyChanged(nameof(IsSelectedAudioSource)); }
    [ObservableProperty] private string sourceToAdd = "Screen";
    public string[] SupportedSourceTypes { get; } = ["Screen", "Webcam", "Microphone", "System audio"];
    [RelayCommand(CanExecute = nameof(CanEditPreferences))] private async Task AddStudioSourceAsync()
    {
        switch (SourceToAdd)
        {
            case "Microphone": MicrophoneEnabled = true; break;
            case "System audio": if (IsWebcam) { StudioMessage = "Webcam Only supports microphone audio. System audio is available in screen modes."; return; } SystemAudioEnabled = true; break;
            case "Webcam": if (!IsCameraMode) { SelectedCamera = null; SelectedMode = "Screen + Webcam"; await CameraTransition; } break;
            case "Screen": if (IsWebcam) { SelectedMode = "Screen + Webcam"; await CameraTransition; } break;
        }
        StudioMessage = "Source enabled. Select its device and supported properties before starting."; RefreshStudio();
    }
    [RelayCommand(CanExecute = nameof(CanEditPreferences))] private async Task RemoveStudioSourceAsync()
    {
        switch (SelectedStudioSource?.Kind)
        {
            case "Microphone": MicrophoneEnabled = false; break;
            case "System audio": SystemAudioEnabled = false; break;
            case "Webcam overlay": SelectedMode = "Screen Only"; await CameraTransition; break;
            case "Entire Screen" or "Application Window" or "Custom Region" when IsCombined: SelectedMode = "Webcam Only"; await CameraTransition; break;
            default: StudioMessage = "The last video source cannot be removed. Choose another recording mode; audio-only modes remain on Audio Recorder."; return;
        }
        RefreshStudio();
    }
    [ObservableProperty] private bool previewActualSize;
    [ObservableProperty] private double previewDpiScale = 1;
    public double StudioPreviewWidth => PreviewActualSize ? PreviewPixelWidth / Math.Max(.5, PreviewDpiScale) : double.NaN;
    public double StudioPreviewHeight => PreviewActualSize ? PreviewPixelHeight / Math.Max(.5, PreviewDpiScale) : double.NaN;
    partial void OnPreviewDpiScaleChanged(double value) { OnPropertyChanged(nameof(StudioPreviewWidth)); OnPropertyChanged(nameof(StudioPreviewHeight)); }
    partial void OnPreviewActualSizeChanged(bool value) { OnPropertyChanged(nameof(StudioPreviewWidth)); OnPropertyChanged(nameof(StudioPreviewHeight)); }
    partial void OnShowStudioMixerChanged(bool value) { if (!disposed && !loadingStudio) studioSave = SaveStudioAsync(); }
    partial void OnShowStudioPropertiesChanged(bool value) { OnPropertyChanged(nameof(StudioMinimumWidth)); if (!disposed && !loadingStudio) studioSave = SaveStudioAsync(); }
    partial void OnStudioSourcesWidthChanged(double value) => OnPropertyChanged(nameof(StudioMinimumWidth));
    partial void OnStudioPropertiesWidthChanged(double value) => OnPropertyChanged(nameof(StudioMinimumWidth));
    [RelayCommand] private void FitPreview() => PreviewActualSize = false;
    [RelayCommand] private void ActualPreviewSize() => PreviewActualSize = true;
    [RelayCommand(CanExecute = nameof(CanStartScreenPreview))] private async Task StartScreenPreviewAsync()
    {
        if (screenPreview is null || IsCameraMode || SelectedScreenOptions is not { } options) return;
        IsBusy = true; ResetPreview();
        try { await Task.Run(() => screenPreview.StartAsync(options, cameraCancellation.Token)); IsCameraPreviewing = true; previewTimer.Start(); StudioMessage = "Live screen preview. This captures screen pixels; Stop preview releases the source."; }
        catch (Exception ex) { StudioMessage = "Preview unavailable: " + ex.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanStopScreenPreview))] private async Task StopScreenPreviewAsync()
    { ResetPreview(); try { if (screenPreview is not null) await screenPreview.StopAsync(); } catch (Exception ex) { StudioMessage = ex.Message; } }
    private CaptureFlow.Core.Audio.AudioLevels? microphoneLevels, systemLevels;
    [ObservableProperty] private double microphoneMeter;
    [ObservableProperty] private double systemMeter;
    [ObservableProperty] private string microphoneMeterText = "Inactive · no samples";
    [ObservableProperty] private string systemMeterText = "Inactive · no samples";
    private void ObserveAudioLevels(CaptureFlow.Core.Audio.AudioLevels levels)
    {
        if (disposed) return;
        ref var target = ref (levels.Microphone ? ref microphoneLevels : ref systemLevels);
        CaptureFlow.Core.Audio.AudioLevels? previous;
        do { previous = Volatile.Read(ref target); }
        while (Interlocked.CompareExchange(ref target, levels with { Peak = Math.Max(levels.Peak, previous?.Peak ?? 0) }, previous) != previous);
    }
    private void UpdateStudioMeters()
    {
        var mic = Interlocked.Exchange(ref microphoneLevels, null); var system = Interlocked.Exchange(ref systemLevels, null);
        bool Live(CaptureFlow.Core.Audio.AudioLevels? value) => Status == VideoRecordingState.Recording && value is not null && Stopwatch.GetElapsedTime(value.Timestamp).TotalSeconds < .5;
        string Label(CaptureFlow.Core.Audio.AudioLevels value) => $"Peak {CaptureFlow.Core.Audio.AudioLevels.Decibels(value.Peak):0} dB · RMS {CaptureFlow.Core.Audio.AudioLevels.Decibels(value.Rms):0} dB" + (value.Peak >= 1 ? " · CLIP" : "");
        MicrophoneMeter = Live(mic) ? CaptureFlow.Core.Audio.AudioLevels.Decibels(mic!.Peak) + 60 : 0;
        SystemMeter = Live(system) ? CaptureFlow.Core.Audio.AudioLevels.Decibels(system!.Peak) + 60 : 0;
        MicrophoneMeterText = Live(mic) ? Label(mic!) : "Inactive · no current samples";
        SystemMeterText = Live(system) ? Label(system!) : "Inactive · no current samples";
    }
    public ObservableCollection<StudioPreset> StudioPresets { get; } = [];
    [ObservableProperty] private StudioPreset? selectedStudioPreset;
    [ObservableProperty] private string presetName = "My recording setup";
    [ObservableProperty] private string studioMessage = "Presets save recording configurations, not arbitrary composited scenes. Device access starts only through existing camera/preview actions.";
    [ObservableProperty] private bool showStudioMixer = true;
    [ObservableProperty] private bool showStudioProperties = true;
    [ObservableProperty] private double studioSourcesWidth = 220;
    [ObservableProperty] private double studioPropertiesWidth = 300;
    [ObservableProperty] private double studioMixerHeight = 280;
    public IReadOnlyList<StudioSource> StudioSources => new[]
    {
        IsScreen ? new StudioSource(SelectedScreenSource == "Application Window" ? SelectedApplicationWindow?.DisplayName ?? "Select a window" : SelectedMonitor?.Name ?? "Select a monitor", SelectedScreenSource, SelectedScreenOptions is null ? "Unavailable / select source" : Status is VideoRecordingState.Recording or VideoRecordingState.Paused ? StatusLabel : IsCameraPreviewing ? "Live" : "Configured") : null,
        IsCameraMode ? new StudioSource(SelectedCamera?.Name ?? "Select a camera", IsCombined ? "Webcam overlay" : "Webcam", IsCameraPreviewing ? "Live" : SelectedCamera is null ? "Unavailable" : "Preview stopped") : null,
        MicrophoneEnabled ? new StudioSource(SelectedMicrophone?.Name ?? "Select microphone", "Microphone", SelectedMicrophone is null ? "Unavailable" : MicrophoneMuted ? "Muted" : "Enabled") : null,
        IsScreen && SystemAudioEnabled ? new StudioSource(SelectedOutputDevice?.Name ?? "Select playback device", "System audio", SelectedOutputDevice is null ? "Unavailable" : SystemAudioMuted ? "Muted" : "Enabled") : null
    }.OfType<StudioSource>().ToArray();
    public string StudioConfigurationSummary
    {
        get
        {
            try
            {
                var size = QualitySourceSize is { } source ? QualitySettings.OutputSize(source) : null;
                return $"{StatusLabel} · {ElapsedText} · {(size is null ? SelectedResolution : $"{size.Width} × {size.Height}")} · {SelectedFrameRate} configured · {SelectedEncoder} selected";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { return "Invalid quality settings · review source properties"; }
        }
    }
    public void RefreshStudio()
    {
        var selected = SelectedStudioSource?.Kind;
        OnPropertyChanged(nameof(StudioSources)); OnPropertyChanged(nameof(StudioConfigurationSummary)); OnPropertyChanged(nameof(IsScreenOnly)); OnPropertyChanged(nameof(StudioPreviewState));
        SelectedStudioSource = StudioSources.FirstOrDefault(s => s.Kind == selected) ?? StudioSources.FirstOrDefault();
    }
    public async Task LoadStudioAsync()
    {
        if (studioStore is null) return;
        loadingStudio = true;
        try
        {
            var value = await studioStore.LoadAsync(); value.Validate();
            StudioSourcesWidth = value.SourcesWidth; StudioPropertiesWidth = value.PropertiesWidth; StudioMixerHeight = value.MixerHeight;
            ShowStudioMixer = value.ShowMixer; ShowStudioProperties = value.ShowProperties; ShowStudioSources = value.ShowSources;
            StudioPresets.Clear(); foreach (var preset in value.Presets ?? []) StudioPresets.Add(preset);
        }
        catch (Exception ex) { StudioMessage = "Could not load workspace: " + ex.Message; }
        finally { loadingStudio = false; }
    }
    [RelayCommand] public Task SaveStudioAsync() => studioSave = SaveStudioCoreAsync();
    private async Task SaveStudioCoreAsync()
    {
        try { if (studioStore is not null) await studioStore.SaveAsync(new(StudioSourcesWidth, StudioPropertiesWidth, StudioMixerHeight, ShowStudioMixer, ShowStudioProperties, StudioPresets.ToArray(), ShowStudioSources)); }
        catch (Exception ex) { StudioMessage = "Could not save workspace: " + ex.Message; }
    }
    [RelayCommand] private async Task ResetWorkspaceAsync()
    {
        StudioSourcesWidth = 220; StudioPropertiesWidth = 300; StudioMixerHeight = 280; ShowStudioMixer = true; ShowStudioProperties = true; ShowStudioSources = true; await SaveStudioAsync();
    }
    [RelayCommand(CanExecute = nameof(CanEditPreferences))] private async Task SavePresetAsync()
    {
        try
        {
            var name = PresetName.Trim();
            var preset = new StudioPreset(Guid.NewGuid(), name, SelectedMode, SelectedScreenSource, SelectedMonitor?.Id, SelectedApplicationWindow?.Id,
                SelectedRegion, SelectedCamera?.Id, SelectedCameraFormat, MicrophoneEnabled, SelectedMicrophone?.Id, SystemAudioEnabled, SelectedOutputDevice?.Id, QualitySettings, SelectedCountdown, layouts.Current, MousePreferences, MicrophoneVolume, MicrophoneMuted, SystemAudioVolume, SystemAudioMuted);
            new StudioPreferences(Presets: StudioPresets.Append(preset).ToArray()).Validate();
            StudioPresets.Add(preset); SelectedStudioPreset = preset; await SaveStudioAsync();
        }
        catch (Exception ex) { StudioMessage = "Preset not saved: " + ex.Message; }
    }
    [RelayCommand(CanExecute = nameof(CanEditPreferences))] private async Task RenamePresetAsync()
    {
        if (SelectedStudioPreset is not { } old) return;
        try
        {
            var replacement = old with { Name = PresetName.Trim() };
            var values = StudioPresets.Select(p => p.Id == old.Id ? replacement : p).ToArray(); new StudioPreferences(Presets: values).Validate();
            StudioPresets[StudioPresets.IndexOf(old)] = replacement; SelectedStudioPreset = replacement; await SaveStudioAsync();
        }
        catch (Exception ex) { StudioMessage = "Preset not renamed: " + ex.Message; }
    }
    [RelayCommand(CanExecute = nameof(CanEditPreferences))] private async Task DeletePresetAsync()
    { if (SelectedStudioPreset is { } preset) { StudioPresets.Remove(preset); SelectedStudioPreset = null; await SaveStudioAsync(); } }
    [RelayCommand(CanExecute = nameof(CanEditPreferences))] private async Task ApplyPresetAsync()
    {
        if (SelectedStudioPreset is not { } preset) return;
        try
        {
            preset.Validate();
            // Validate identities before changing anything; never substitute another device.
            var monitor = Monitors.FirstOrDefault(d => d.Id == preset.MonitorId);
            var window = ApplicationWindows.FirstOrDefault(d => d.Id == preset.WindowId);
            var camera = Cameras.FirstOrDefault(d => d.Id == preset.CameraId);
            var mic = Microphones.FirstOrDefault(d => d.Id == preset.MicrophoneId);
            var output = OutputDevices.FirstOrDefault(d => d.Id == preset.OutputDeviceId);
            if (preset.Mode != "Webcam Only" && (preset.ScreenSource == "Application Window" ? window is null : monitor is null)) throw new InvalidOperationException("The saved screen source is unavailable. Refresh sources and select it explicitly.");
            if (preset.ScreenSource == "Custom Region" && preset.Mode != "Webcam Only" && (preset.Region is null || !preset.Region.Matches(monitor!))) throw new InvalidOperationException("Saved region no longer matches the monitor.");
            if (preset.Mode != "Screen Only" && camera is null || preset.Microphone && mic is null || preset.Mode != "Webcam Only" && preset.SystemAudio && output is null) throw new InvalidOperationException("A preset device is unavailable. No replacement device was selected.");
            IsBusy = true;
            try { await sourceChange; ResetPreview(); await ReleasePreviewAsync(); if (screenPreview is not null) await screenPreview.StopAsync(); }
            finally { IsBusy = false; }
            // Suppress automatic camera opens: loading a preset never accesses a physical device.
            suppressCameraSelection = true;
            try
            {
                SelectedCamera = null; SelectedMode = preset.Mode; await CameraTransition;
                supportedCameraFormats = []; CameraResolutions.Clear(); CameraFrameRates.Clear(); SelectedCameraResolution = null; SelectedCameraFormat = null;
                SelectedCamera = camera;
            }
            finally { suppressCameraSelection = false; }
            SelectedScreenSource = preset.ScreenSource; SelectedMonitor = monitor; SelectedApplicationWindow = window; SelectedRegion = preset.Region;
            SelectedMicrophone = mic; SelectedOutputDevice = output; MicrophoneEnabled = preset.Microphone; SystemAudioEnabled = preset.SystemAudio;
            SelectedQuality = preset.Quality.Preset.ToString(); SelectedResolution = ResolutionLabels[(int)preset.Quality.Resolution]; SelectedFrameRate = preset.Quality.FramesPerSecond + " FPS"; CustomBitrateText = preset.Quality.CustomBitrateKbps.ToString();
            var encoder = EncoderLabel(preset.Quality.Encoder); if (!EncoderOptions.Contains(encoder)) EncoderOptions.Add(encoder); SelectedEncoder = encoder;
            SelectedCountdown = preset.Countdown;
            presetCameraFormat = preset.CameraFormat;
            MicrophoneVolume = preset.MicrophoneVolume; MicrophoneMuted = preset.MicrophoneMuted; SystemAudioVolume = preset.SystemVolume; SystemAudioMuted = preset.SystemMuted;
            if (preset.Overlay is { } overlay)
            {
                synchronizingLayout = true;
                try { layouts.Current = overlay; OverlayVisible = overlay.Visible; OverlayWidthPercent = overlay.Width * 100; SelectedOverlayShape = overlay.Shape switch { OverlayShape.Circle => "Circle", OverlayShape.Rectangle => "Rectangle", _ => "Rounded rectangle" }; OverlayBorderEnabled = overlay.BorderEnabled; OverlayBorderThickness = overlay.BorderThickness; }
                finally { synchronizingLayout = false; } NotifyOverlayBounds();
            }
            if (preset.Mouse is { } mouse)
            {
                ShowMouseCursor = mouse.ShowCursor; HighlightCursor = mouse.Highlight; HighlightColor = mouse.HighlightColor; HighlightRadius = mouse.Radius; HighlightOpacity = mouse.Opacity;
                HighlightMouseClicks = mouse.ClickEffects; ShowLeftClicks = mouse.LeftClicks; ShowRightClicks = mouse.RightClicks; LeftClickColor = mouse.LeftColor; RightClickColor = mouse.RightColor; ClickDurationMilliseconds = mouse.ClickDurationMilliseconds;
            }
            StudioMessage = "Preset applied. Camera modes require Reload camera and a supported format, then Start preview. Explicit hardware encoding requires a new capability check.";
            RefreshStudio();
        }
        catch (Exception ex) { StudioMessage = "Preset not applied: " + ex.Message; }
    }
    [RelayCommand] private void OpenStudioOutput()
    { try { if (!Directory.Exists(OutputDirectory)) throw new DirectoryNotFoundException("The output folder does not exist yet."); Process.Start(new ProcessStartInfo(OutputDirectory) { UseShellExecute = true }); } catch (Exception ex) { StudioMessage = ex.Message; } }
}
