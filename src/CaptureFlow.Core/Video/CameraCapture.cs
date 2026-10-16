using System.Globalization;
namespace CaptureFlow.Core.Video;

public enum CameraLifecycleState { Uninitialized, Initializing, Ready, Capturing, Stopping, Disposing, Disposed, Faulted }

public sealed record CameraDevice(string Id, string Name);
/// <summary>A native format reported by one camera frame source, not an invented output preset.</summary>
public sealed record CameraFormat(string Id, int Width, int Height, uint FrameRateNumerator, uint FrameRateDenominator, string Subtype)
{
    public double FramesPerSecond => FrameRateDenominator == 0 ? 0 : FrameRateNumerator / (double)FrameRateDenominator;
    public string ResolutionLabel => $"{Width} × {Height}";
    public string FrameRateLabel => FramesPerSecond.ToString("0.###", CultureInfo.InvariantCulture) + " FPS";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Subtype) || Width < 2 || Height < 2 ||
            (long)Width * Height > 33_554_432 || FrameRateNumerator == 0 || FrameRateDenominator == 0 || FramesPerSecond is < 1 or > 240)
            throw new ArgumentException("The camera format is invalid or exceeds supported buffer/frame-rate limits.");
    }
}
public interface ICameraCaptureBackend
{
    Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<ICameraCaptureSession> OpenAsync(string deviceId, CancellationToken cancellationToken);
}
public interface ICameraCaptureSession : IVideoFrameSource
{
    CameraLifecycleState LifecycleState => CameraLifecycleState.Ready;
    IReadOnlyList<CameraFormat> SupportedFormats { get; }
    Task ConfigureAsync(CameraFormat format, CancellationToken cancellationToken);
}
/// <summary>Owns one physical camera. Recording borrows frames from the same session used for preview.</summary>
public interface ICameraPreviewService : IAsyncDisposable
{
    CameraLifecycleState LifecycleState => CameraLifecycleState.Uninitialized;
    event EventHandler<Exception>? Failed;
    bool IsPreviewing { get; }
    string? DeviceId { get; }
    CameraFormat? ActiveFormat { get; }
    Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CameraFormat>> SelectCameraAsync(string deviceId, CancellationToken cancellationToken = default);
    Task StartPreviewAsync(CameraFormat format, CancellationToken cancellationToken = default);
    Task StopPreviewAsync();
    ValueTask<VideoFrame> ReadPreviewFrameAsync(CancellationToken cancellationToken = default);
    Task<IVideoFrameSource> AcquireRecordingSourceAsync(VideoSourceOptions options, CancellationToken cancellationToken);
}
