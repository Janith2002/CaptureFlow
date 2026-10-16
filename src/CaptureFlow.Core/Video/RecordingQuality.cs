namespace CaptureFlow.Core.Video;

public enum VideoQualityPreset { Low, Standard, High, Ultra, Custom }
public enum VideoOutputResolution { Source, P720, P1080, P1440, P2160 }
public enum VideoEncoderChoice { Auto, Software, Nvenc, QuickSync, Amf }
public sealed record VideoQualityPreferences(VideoQualityPreset Preset = VideoQualityPreset.High,
    VideoOutputResolution Resolution = VideoOutputResolution.Source, int FramesPerSecond = 30,
    int CustomBitrateKbps = 12000, VideoEncoderChoice Encoder = VideoEncoderChoice.Auto)
{
    public const int MinimumBitrateKbps = 500, MaximumBitrateKbps = 100000;
    public void Validate()
    {
        if (!Enum.IsDefined(Preset) || !Enum.IsDefined(Resolution) || !Enum.IsDefined(Encoder)) throw new ArgumentException("Choose a supported quality, resolution and encoder.");
        if (FramesPerSecond is not (24 or 30 or 60)) throw new ArgumentOutOfRangeException(nameof(FramesPerSecond), "Choose 24, 30 or 60 FPS.");
        if (CustomBitrateKbps is < MinimumBitrateKbps or > MaximumBitrateKbps) throw new ArgumentOutOfRangeException(nameof(CustomBitrateKbps), "Bitrate must be between 500 and 100000 kbps.");
    }
    public VideoSize OutputSize(VideoSize source)
    {
        Validate();
        if (source.Width < 2 || source.Height < 2 || (long)source.Width * source.Height > 33_554_432) throw new ArgumentException("Unsupported source dimensions.");
        if (Resolution == VideoOutputResolution.Source) return new(source.Width / 2 * 2, source.Height / 2 * 2);
        var box = Resolution switch { VideoOutputResolution.P720 => new VideoSize(1280, 720), VideoOutputResolution.P1080 => new(1920, 1080), VideoOutputResolution.P1440 => new(2560, 1440), _ => new(3840, 2160) };
        if (source.Height > source.Width) box = new(box.Height, box.Width);
        if (Resolution == VideoOutputResolution.P2160 && (source.Width < box.Width || source.Height < box.Height))
            throw new NotSupportedException("4K output requires a source with at least 3840 × 2160 pixels (or portrait equivalent).");
        var scale = Math.Min(box.Width / (double)source.Width, box.Height / (double)source.Height);
        return new(Math.Max(2, (int)(source.Width * scale) / 2 * 2), Math.Max(2, (int)(source.Height * scale) / 2 * 2));
    }
    public int BitrateKbps(VideoSize source)
    {
        var size = OutputSize(source);
        if (Preset == VideoQualityPreset.Custom) return CustomBitrateKbps;
        var basis = Preset switch { VideoQualityPreset.Low => 3000, VideoQualityPreset.Standard => 6000, VideoQualityPreset.High => 10000, _ => 16000 };
        var pixels = size.Width * (double)size.Height / (1920 * 1080);
        var fpsFactor = FramesPerSecond == 60 ? 1.7 : FramesPerSecond / 30d;
        return Math.Clamp((int)Math.Round(basis * Math.Pow(pixels, .75) * fpsFactor / 250) * 250, MinimumBitrateKbps, MaximumBitrateKbps);
    }
    public double EstimatedMegabytesPerMinute(VideoSize source, bool audio) => (BitrateKbps(source) + (audio ? 192 : 0)) * 60d / 8000;
    public string SoftwarePreset => Preset switch { VideoQualityPreset.Low => "ultrafast", VideoQualityPreset.Ultra => "fast", _ => "veryfast" };
}
public sealed record VideoEncoderCapability(VideoEncoderChoice Encoder, bool Available, string Detail);
public interface IVideoEncoderCapabilities
{
    Task<IReadOnlyList<VideoEncoderCapability>> ProbeAsync(string executable, VideoSize outputSize, int fps, int bitrateKbps, CancellationToken cancellationToken);
}
public interface IVideoQualityPreferencesStore
{
    Task<VideoQualityPreferences> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(VideoQualityPreferences preferences, CancellationToken cancellationToken = default);
}
public interface IVideoExportProcess
{
    Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
public sealed record VideoEncoderPlan(VideoEncoderChoice Encoder, string Message)
{
    public bool Hardware => Encoder is not (VideoEncoderChoice.Auto or VideoEncoderChoice.Software);
    public static VideoEncoderPlan Select(VideoEncoderChoice requested, IReadOnlyList<VideoEncoderCapability> capabilities)
    {
        if (!Enum.IsDefined(requested)) throw new ArgumentException("Unsupported encoder.");
        if (requested == VideoEncoderChoice.Auto)
        {
            var hardware = capabilities.FirstOrDefault(c => c.Available && c.Encoder is VideoEncoderChoice.Nvenc or VideoEncoderChoice.QuickSync or VideoEncoderChoice.Amf);
            if (hardware is not null) return new(hardware.Encoder, $"Auto: {hardware.Encoder} export verified; recoverable intermediate retained.");
            requested = VideoEncoderChoice.Software;
        }
        var capability = capabilities.FirstOrDefault(c => c.Encoder == requested);
        if (capability?.Available != true) throw new NotSupportedException($"{requested} is unavailable for this configuration. {capability?.Detail ?? "Run encoder checks first."}");
        return new(requested, requested == VideoEncoderChoice.Software ? "Software encoding (libx264)." : $"{requested} export verified; explicit selection never falls back silently.");
    }
}
/// <summary>One held frame and one write at a time; missing clock slots repeat the previous image.</summary>
public static class VideoFramePacing
{
    public const int MaximumFramesInFlight = 1, MaximumRetainedFrames = 1, MaximumLagSeconds = 5;
    public static long RequiredFrames(TimeSpan duration, double fps) => Math.Max(1, (long)Math.Ceiling(duration.TotalSeconds * fps));
    public static bool HoldPrevious(long written, long required) => required - written > 1;
    public static void ValidateLag(long written, long required, double fps)
    { if (required - written > fps * MaximumLagSeconds) throw new IOException("The encoder cannot keep up. Choose a lower frame rate, resolution or quality. Intermediate recordings are retained."); }
}
