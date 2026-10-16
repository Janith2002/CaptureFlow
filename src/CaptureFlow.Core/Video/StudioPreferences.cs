namespace CaptureFlow.Core.Video;

public sealed record StudioPreset(Guid Id, string Name, string Mode, string ScreenSource,
    string? MonitorId, string? WindowId, ScreenRegion? Region, string? CameraId, CameraFormat? CameraFormat,
    bool Microphone, string? MicrophoneId, bool SystemAudio, string? OutputDeviceId,
    VideoQualityPreferences Quality, string Countdown, OverlayLayout? Overlay = null, MouseEffectsPreferences? Mouse = null,
    double MicrophoneVolume = 80, bool MicrophoneMuted = false, double SystemVolume = 100, bool SystemMuted = false)
{
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 80) throw new ArgumentException("Preset names require 1–80 characters.");
        if (Mode is not ("Screen Only" or "Webcam Only" or "Screen + Webcam")) throw new ArgumentException("Unsupported recording mode.");
        if (ScreenSource is not ("Entire Screen" or "Application Window" or "Custom Region")) throw new ArgumentException("Unsupported screen source.");
        if (Countdown is not ("Off" or "3 seconds" or "5 seconds" or "10 seconds")) throw new ArgumentException("Unsupported countdown.");
        Quality.Validate(); Region?.Validate(); CameraFormat?.Validate(); Overlay?.Validate(); Mouse?.Validate();
        if (!double.IsFinite(MicrophoneVolume) || MicrophoneVolume is < 0 or > 100 || !double.IsFinite(SystemVolume) || SystemVolume is < 0 or > 100) throw new ArgumentException("Audio volume must be 0–100%.");
    }
}
public sealed record StudioPreferences(double SourcesWidth = 220, double PropertiesWidth = 300,
    double MixerHeight = 280, bool ShowMixer = true, bool ShowProperties = true, IReadOnlyList<StudioPreset>? Presets = null, bool ShowSources = true)
{
    public void Validate()
    {
        if (!double.IsFinite(SourcesWidth) || SourcesWidth is < 160 or > 600 ||
            !double.IsFinite(PropertiesWidth) || PropertiesWidth is < 220 or > 700 ||
            !double.IsFinite(MixerHeight) || MixerHeight is < 180 or > 600) throw new ArgumentException("Workspace panel sizes are out of range.");
        if ((Presets?.Count ?? 0) > 100) throw new ArgumentException("Keep at most 100 recording presets.");
        foreach (var preset in Presets ?? []) preset.Validate();
        if ((Presets ?? []).Select(p => p.Id).Distinct().Count() != (Presets?.Count ?? 0)) throw new ArgumentException("Duplicate preset identity.");
        if ((Presets ?? []).Select(p => p.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != (Presets?.Count ?? 0)) throw new ArgumentException("Duplicate preset name.");
    }
}
public interface IStudioPreferencesStore
{
    Task<StudioPreferences> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StudioPreferences preferences, CancellationToken cancellationToken = default);
}
