namespace CaptureFlow.Core.Video;

public enum CameraFailureKind { AccessDenied, DeviceBusy, RuntimeDependency, EnumerationFailed, OpenFailed, Disconnected }

/// <summary>Actionable camera failure; the original exception and HRESULT remain available.</summary>
public sealed class CameraOperationException(CameraFailureKind kind, string message, Exception innerException)
    : IOException(message, innerException)
{
    public CameraFailureKind Kind { get; } = kind;
}
