using System.Collections.ObjectModel;
using CaptureFlow.Core.Video;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace CaptureFlow.Desktop.ViewModels;

public partial class ScreenRecorderViewModel
{
    private readonly IVideoQualityPreferencesStore? qualityStore;
    private readonly IVideoEncoderCapabilities? encoderCapabilities;
    private readonly CancellationTokenSource qualityCancellation = new();
    private string? verifiedConfiguration;
    private IReadOnlyList<VideoEncoderCapability> encoderResults = [];
    [ObservableProperty] private string customBitrateText = "12000";
    [ObservableProperty] private string? selectedSuggestedBitrate;
    [ObservableProperty] private string selectedEncoder = "Auto";
    [ObservableProperty] private bool isCheckingEncoders;
    [ObservableProperty] private string qualityPreferencesMessage = "Click Save quality settings to persist video preferences.";
    [ObservableProperty] private string encoderMessage = "Auto checks real encoder support before recording. Hardware export retains a software intermediate for safe fallback.";
    [ObservableProperty] private string encodingStatus = "";
    public bool IsCustomQuality => SelectedQuality == "Custom";
    public bool CanEditQuality => CanEditPreferences && !IsCheckingEncoders;
    public string EncoderVerificationNotice
    {
        get
        {
            try
            {
                var encoder = QualitySettings.Encoder;
                if (encoder is VideoEncoderChoice.Auto or VideoEncoderChoice.Software) return "";
                if (verifiedConfiguration != ConfigurationKey) return "Run Check available encoders for the current resolution, FPS, bitrate and FFmpeg path before starting this hardware encoder.";
                if (!encoderResults.Any(c => c.Encoder == encoder && c.Available)) return $"{SelectedEncoder} is unavailable. Select Auto or Software, or resolve the reported runtime error and check again.";
                if (!encoderResults.Any(c => c.Encoder == VideoEncoderChoice.Software && c.Available)) return "libx264 is required for the retained capture intermediate. Install a compatible FFmpeg build and check again.";
                return "";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { return ex.Message; }
        }
    }
    public ObservableCollection<string> EncoderOptions { get; } = ["Auto", "Software"];
    public string[] BitrateOptions
    {
        get
        {
            try { if (QualitySourceSize is { } source) return new[] { VideoQualityPreset.Low, VideoQualityPreset.Standard, VideoQualityPreset.High, VideoQualityPreset.Ultra }
                .Select(p => (QualitySettings with { Preset = p }).BitrateKbps(source)).Distinct().Order().Select(b => b.ToString()).ToArray(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { }
            return ["2000", "4000", "6000", "10000", "16000"];
        }
    }
    private static readonly string[] ResolutionLabels = ["Original/source", "720p", "1080p", "1440p", "4K"];
    private static string EncoderLabel(VideoEncoderChoice encoder) => encoder switch { VideoEncoderChoice.Nvenc => "NVIDIA NVENC", VideoEncoderChoice.QuickSync => "Intel Quick Sync", VideoEncoderChoice.Amf => "AMD AMF", _ => encoder.ToString() };
    private VideoQualityPreferences QualitySettings => new(Enum.Parse<VideoQualityPreset>(SelectedQuality),
        (VideoOutputResolution)Array.IndexOf(ResolutionLabels, SelectedResolution),
        int.TryParse(SelectedFrameRate?.Split(' ')[0], out var fps) ? fps : throw new ArgumentException("Choose 24, 30 or 60 FPS."),
        int.TryParse(CustomBitrateText, out var bitrate) ? bitrate : throw new ArgumentException("Custom bitrate must be an integer between 500 and 100000 kbps."),
        Enum.GetValues<VideoEncoderChoice>().Single(e => EncoderLabel(e) == SelectedEncoder));
    private VideoSize? QualitySourceSize => IsWebcam ? SelectedCameraFormat is { } camera ? new(camera.Width, camera.Height) : null :
        SelectedScreenSize;
    private string ConfigurationKey => QualitySourceSize is { } size ? $"{FfmpegPath}|{QualitySettings.OutputSize(size)}|{QualitySettings.FramesPerSecond}|{QualitySettings.BitrateKbps(size)}" : "No source";
    private bool ValidQuality
    {
        get
        {
            try
            {
                var preferences = QualitySettings; preferences.Validate();
                if (QualitySourceSize is not { } source) return false;
                preferences.OutputSize(source);
                if (preferences.Encoder is not (VideoEncoderChoice.Auto or VideoEncoderChoice.Software))
                    return verifiedConfiguration == ConfigurationKey && encoderResults.Any(c => c.Encoder == preferences.Encoder && c.Available) && encoderResults.Any(c => c.Encoder == VideoEncoderChoice.Software && c.Available);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or NotSupportedException) { return false; }
        }
    }
    public string QualitySummary
    {
        get
        {
            try
            {
                if (QualitySourceSize is not { } source) return "Choose a monitor or supported camera format to calculate output size and bitrate.";
                var p = QualitySettings; var output = p.OutputSize(source); var bitrate = p.BitrateKbps(source);
                var scaling = output == source ? "Source pixels retained." : output.Width > source.Width || output.Height > source.Height ? "Upscaling: output gains no additional source detail." : "Scaled to fit; source aspect ratio is preserved (even-pixel rounding).";
                var camera = IsCameraMode && SelectedCameraFormat is { } format ? format.FramesPerSecond < p.FramesPerSecond ? $" Camera runs at {format.FrameRateLabel}; frames are repeated for {p.FramesPerSecond} FPS output." : format.FramesPerSecond > p.FramesPerSecond ? $" Camera frames are sampled down from {format.FrameRateLabel}." : " Camera FPS matches output." : "";
                return $"{output.Width} × {output.Height} · {p.FramesPerSecond} FPS · target {bitrate:N0} kbps. Estimated output: {p.EstimatedMegabytesPerMinute(source, MicrophoneEnabled || IsScreen && SystemAudioEnabled):N1} MB/min (not guaranteed; excludes intermediate storage). {scaling}{camera}";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or NotSupportedException) { return ex.Message; }
        }
    }
    private void QualityChanged()
    {
        OnPropertyChanged(nameof(IsCustomQuality)); OnPropertyChanged(nameof(QualitySummary)); OnPropertyChanged(nameof(CanEditQuality)); OnPropertyChanged(nameof(BitrateOptions)); OnPropertyChanged(nameof(EncoderVerificationNotice));
        SaveQualitySettingsCommand.NotifyCanExecuteChanged(); CheckEncodersCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
    }
    partial void OnSelectedQualityChanged(string value) => QualityChanged();
    partial void OnSelectedResolutionChanged(string value) => QualityChanged();
    partial void OnSelectedFrameRateChanged(string value) => QualityChanged();
    partial void OnCustomBitrateTextChanged(string value) => QualityChanged();
    partial void OnSelectedSuggestedBitrateChanged(string? value) { if (value is not null) CustomBitrateText = value; }
    partial void OnSelectedEncoderChanged(string value) => QualityChanged();
    partial void OnIsCheckingEncodersChanged(bool value) => RefreshCommands();
    public async Task LoadQualityPreferencesAsync()
    {
        if (qualityStore is null) return;
        try
        {
            var p = await qualityStore.LoadAsync(qualityCancellation.Token);
            if (disposed) return;
            SelectedQuality = p.Preset.ToString(); SelectedResolution = ResolutionLabels[(int)p.Resolution];
            SelectedFrameRate = $"{p.FramesPerSecond} FPS"; CustomBitrateText = p.CustomBitrateKbps.ToString();
            var encoder = EncoderLabel(p.Encoder); if (!EncoderOptions.Contains(encoder)) EncoderOptions.Add(encoder);
            SelectedEncoder = encoder; QualityPreferencesMessage = "Saved video quality preferences loaded. Explicit hardware choices need a fresh encoder check.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { QualityPreferencesMessage = "Could not load video quality preferences: " + ex.Message; }
    }
    [RelayCommand(CanExecute = nameof(CanEditQuality))]
    private async Task SaveQualitySettingsAsync()
    {
        try
        {
            var p = QualitySettings; p.Validate();
            if (QualitySourceSize is { } source) p.OutputSize(source);
            if (qualityStore is null) throw new InvalidOperationException("Quality preference storage is unavailable.");
            await qualityStore.SaveAsync(p, qualityCancellation.Token);
            QualityPreferencesMessage = "Video quality, output resolution, FPS, custom bitrate and encoder preference saved.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { QualityPreferencesMessage = "Could not save quality settings: " + ex.Message; }
    }
    [RelayCommand(CanExecute = nameof(CanEditQuality))]
    private async Task CheckEncodersAsync()
    {
        IsCheckingEncoders = true; EncoderMessage = "Testing software, NVIDIA NVENC, Intel Quick Sync and AMD AMF with real synthetic MP4 encode/decode jobs. No devices are captured.";
        try
        {
            if (encoderCapabilities is null || QualitySourceSize is not { } source) throw new InvalidOperationException("Choose a source and configure FFmpeg before checking encoders.");
            var p = QualitySettings; var key = ConfigurationKey;
            var results = await encoderCapabilities.ProbeAsync(FfmpegPath, p.OutputSize(source), p.FramesPerSecond, p.BitrateKbps(source), qualityCancellation.Token);
            if (disposed) return;
            encoderResults = results; verifiedConfiguration = key;
            var selected = SelectedEncoder;
            EncoderOptions.Clear(); EncoderOptions.Add("Auto"); EncoderOptions.Add("Software");
            foreach (var c in results.Where(c => c.Available && c.Encoder != VideoEncoderChoice.Software)) EncoderOptions.Add(EncoderLabel(c.Encoder));
            if (!EncoderOptions.Contains(selected)) EncoderOptions.Add(selected);
            SelectedEncoder = selected;
            EncoderMessage = string.Join("\n", results.Select(c => $"{EncoderLabel(c.Encoder)}: {(c.Available ? c.Detail : "Unavailable. " + c.Detail[..Math.Min(c.Detail.Length, 300)])}"));
        }
        catch (OperationCanceledException) { EncoderMessage = "Encoder checks cancelled."; }
        catch (Exception ex) { EncoderMessage = "Encoder checks failed: " + ex.Message; verifiedConfiguration = null; }
        finally { IsCheckingEncoders = false; QualityChanged(); }
    }
    public async Task StopQualityOperationsAsync()
    {
        qualityCancellation.Cancel();
        foreach (var command in new[] { CheckEncodersCommand, SaveQualitySettingsCommand })
            if (command.IsRunning && command.ExecutionTask is { } task) await task;
    }
}
