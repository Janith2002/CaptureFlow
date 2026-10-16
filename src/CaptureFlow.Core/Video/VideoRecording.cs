using System.Buffers;
using CaptureFlow.Core.Audio;
namespace CaptureFlow.Core.Video;
public enum VideoRecordingState { Idle, Starting, Recording, Paused, Saving, Completed, Cancelled, Error }
public enum VideoSourceKind { Monitor, Window, Webcam, ScreenAndWebcam, Region }
public sealed record MonitorDescriptor(string Id, string Name, int Width, int Height, bool IsPrimary);
public sealed record VideoSourceOptions(VideoSourceKind Kind, string Id, bool CaptureCursor = true, CameraFormat? CameraFormat = null, string? CameraId = null,
    VideoSourceKind ScreenKind = VideoSourceKind.Monitor, ScreenRegion? Region = null, ApplicationWindowDescriptor? Window = null, MouseEffectsPreferences? MouseEffects = null)
{
    public VideoSourceOptions ScreenSource => new(Kind == VideoSourceKind.ScreenAndWebcam ? ScreenKind : Kind, Id, CaptureCursor, Region: Region, Window: Window, MouseEffects: MouseEffects);
    public void ValidateScreen()
    {
        MouseEffects?.Validate();
        if (MouseEffects is { } effects && effects.ShowCursor != CaptureCursor) throw new ArgumentException("Cursor visibility and mouse preferences must agree.");
        var kind = Kind == VideoSourceKind.ScreenAndWebcam ? ScreenKind : Kind;
        if (kind is not (VideoSourceKind.Monitor or VideoSourceKind.Window or VideoSourceKind.Region)) throw new NotSupportedException("Unsupported screen capture source.");
        if (kind == VideoSourceKind.Window) { if (Window is null || Window.Id != Id) throw new ArgumentException("Select a valid application window."); Window.Validate(); }
        else if (Window is not null) throw new ArgumentException("Window identity applies only to Window capture.");
        if (kind == VideoSourceKind.Region) { if (Region is null || Region.MonitorId != Id) throw new ArgumentException("Select a region on the chosen monitor."); Region.Validate(); }
        else if (Region is not null) throw new ArgumentException("Crop geometry applies only to Custom Region capture.");
    }
}
public sealed record VideoSize(int Width, int Height);
public sealed record VideoRecordingConfiguration(VideoSourceOptions Source, string OutputDirectory, int FramesPerSecond = 30,
    int? OutputWidth = null, int? OutputHeight = null, int Quality = 23, AudioRecordingOptions? Audio = null, string FfmpegPath = "ffmpeg.exe", VideoQualityPreferences? QualityPreferences = null)
{
    public double EffectiveFramesPerSecond => QualityPreferences is not null ? FramesPerSecond : Source.Kind == VideoSourceKind.Webcam ? Source.CameraFormat?.FramesPerSecond ?? FramesPerSecond : FramesPerSecond;
    public void Validate()
    {
        if (!Enum.IsDefined(Source.Kind)) throw new NotSupportedException("Unsupported video source.");
        if (Source.Kind != VideoSourceKind.Webcam) Source.ValidateScreen();
        else if (Source.Region is not null || Source.Window is not null || Source.MouseEffects is not null) throw new ArgumentException("Webcam Only does not use a screen source or mouse effects.");
        if (string.IsNullOrWhiteSpace(Source.Id) || string.IsNullOrWhiteSpace(OutputDirectory)) throw new ArgumentException("Choose a video source and output directory.");
        if ((QualityPreferences is not null || Source.Kind != VideoSourceKind.Webcam) && FramesPerSecond is not (24 or 30 or 60)) throw new ArgumentOutOfRangeException(nameof(FramesPerSecond));
        if (QualityPreferences is { } preferences)
        {
            preferences.Validate();
            if (preferences.FramesPerSecond != FramesPerSecond) throw new ArgumentException("Quality settings and output FPS must match.");
            if (OutputWidth is not null || OutputHeight is not null) throw new ArgumentException("Use the quality resolution setting instead of manual dimensions.");
        }
        if (Source.Kind == VideoSourceKind.Webcam)
        {
            if (Source.CameraFormat is null) throw new ArgumentException("Choose a supported camera format.");
            Source.CameraFormat.Validate();
            if (Audio is not null && Audio.Mode != AudioRecordingMode.MicrophoneOnly) throw new NotSupportedException("Webcam Only currently supports optional microphone audio.");
        }
        else if (Source.Kind == VideoSourceKind.ScreenAndWebcam)
        {
            if (string.IsNullOrWhiteSpace(Source.CameraId) || Source.CameraFormat is null) throw new ArgumentException("Select a camera and supported format for Screen + Webcam.");
            Source.CameraFormat.Validate();
        }
        else if (Source.CameraFormat is not null) throw new ArgumentException("Camera formats apply only to camera sources.");
        if (Quality is < 0 or > 51) throw new ArgumentOutOfRangeException(nameof(Quality));
        if ((OutputWidth is null) != (OutputHeight is null) || OutputWidth is <= 0 || OutputHeight is <= 0 || OutputWidth % 2 == 1 || OutputHeight % 2 == 1) throw new ArgumentException("Output dimensions must be positive and even.");
        if (string.IsNullOrWhiteSpace(FfmpegPath)) throw new ArgumentException("Configure the FFmpeg executable path.");
        if (Audio is { } audio)
        {
            if (!Enum.IsDefined(audio.Mode)) throw new ArgumentException("Unsupported audio mode.");
            if (audio.SampleRate is not (16000 or 44100 or 48000)) throw new ArgumentException("Unsupported audio sample rate.");
            if (audio.Mode != AudioRecordingMode.SystemAudioOnly && string.IsNullOrWhiteSpace(audio.DeviceId)) throw new ArgumentException("Choose a microphone.");
            if (audio.Mode != AudioRecordingMode.MicrophoneOnly && string.IsNullOrWhiteSpace(audio.OutputDeviceId)) throw new ArgumentException("Choose a playback device.");
            if (!float.IsFinite(audio.Gain) || audio.Gain is < 0 or > 1 || !float.IsFinite(audio.SystemGain) || audio.SystemGain is < 0 or > 1) throw new ArgumentException("Audio gains must be between zero and one.");
        }
    }
}
/// <summary>Owned BGRA pixels. Leases share storage; the last Dispose returns it to its owner.</summary>
public sealed class VideoFrame : IDisposable
{
    private sealed class Shared(IMemoryOwner<byte> owner) { public IMemoryOwner<byte> Owner { get; } = owner; public int References = 1; }
    private readonly Shared shared;
    private bool disposed;
    public VideoSize Size { get; }
    public TimeSpan Timestamp { get; }
    public ReadOnlyMemory<byte> Pixels { get { ObjectDisposedException.ThrowIf(disposed, this); return shared.Owner.Memory[..checked(Size.Width * Size.Height * 4)]; } }
    public VideoFrame(IMemoryOwner<byte> owner, VideoSize size, TimeSpan timestamp) { shared = new(owner); Size = size; Timestamp = timestamp; }
    private VideoFrame(VideoFrame source) { shared = source.shared; Size = source.Size; Timestamp = source.Timestamp; Interlocked.Increment(ref shared.References); }
    public VideoFrame Lease() { ObjectDisposedException.ThrowIf(disposed, this); return new(this); }
    public void Dispose() { if (disposed) return; disposed = true; if (Interlocked.Decrement(ref shared.References) == 0) shared.Owner.Dispose(); }
}
public sealed record VideoOverlay(VideoFrame Frame, double X, double Y, double Width, double Height,
    OverlayShape Shape = OverlayShape.Rectangle, double BorderThickness = 0);
public interface IVideoCompositor { ValueTask<VideoFrame> ComposeAsync(VideoFrame primary, IReadOnlyList<VideoOverlay> overlays, CancellationToken cancellationToken); }
public interface IVideoSourceFactory
{
    Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken cancellationToken = default);
    Task<IVideoFrameSource> CreateAsync(VideoSourceOptions options, CancellationToken cancellationToken);
}
public interface IVideoFrameSource : IAsyncDisposable
{
    void SetRecordingActivity(bool active) { }
    event EventHandler<Exception>? Failed;
    VideoSize Size { get; }
    Task StartAsync(CancellationToken cancellationToken);
    ValueTask<VideoFrame> ReadFrameAsync(CancellationToken cancellationToken);
}
public interface IVideoEncoder : IAsyncDisposable
{
    string? Description => null;
    long FrameCount { get; }
    Task WriteFrameAsync(VideoFrame frame, CancellationToken cancellationToken);
    Task FinishAsync(CancellationToken cancellationToken);
}
public interface IVideoEncodingBackend
{
    string? GetEncodingStatus(string videoPath) => null;
    Task EnsureAvailableAsync(string executable, CancellationToken cancellationToken);
    Task<IVideoEncoder> CreateAsync(VideoRecordingConfiguration configuration, VideoSize sourceSize, string videoPath, CancellationToken cancellationToken);
    Task MuxAsync(VideoRecordingConfiguration configuration, string videoPath, string? audioPath, string finalPath, TimeSpan duration, CancellationToken cancellationToken);
    Task ValidateAsync(string filePath, string executable, CancellationToken cancellationToken);
    void CheckStorage(string outputDirectory);
}
public interface IVideoRecordingService : IAsyncDisposable
{
    string? EncodingStatus => null;
    event EventHandler? StateChanged;
    VideoRecordingState State { get; }
    TimeSpan Duration { get; }
    string? SavedFilePath { get; }
    string? WorkDirectory { get; }
    string? ErrorMessage { get; }
    Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken cancellationToken = default);
    Task StartAsync(VideoRecordingConfiguration configuration, CancellationToken cancellationToken = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync(CancellationToken cancellationToken = default);
    Task CancelAsync();
    void SetAudioVolume(float microphone, bool microphoneMuted, float system, bool systemMuted);
}
