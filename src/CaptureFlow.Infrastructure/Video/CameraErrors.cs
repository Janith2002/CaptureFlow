using CaptureFlow.Core.Video;

namespace CaptureFlow.Infrastructure.Video;

public static class CameraErrors
{
    public static CameraOperationException Describe(Exception error, bool enumerating)
    {
        if (error is CameraOperationException known) return known;
        var chain = new List<Exception>();
        for (Exception? current = error; current is not null; current = current.InnerException) chain.Add(current);
        var kind = enumerating ? CameraFailureKind.EnumerationFailed : CameraFailureKind.OpenFailed;
        if (chain.Any(e => e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005))) kind = CameraFailureKind.AccessDenied;
        else if (chain.Any(e => e.HResult == unchecked((int)0x80070020))) kind = CameraFailureKind.DeviceBusy;
        else if (chain.Any(e => e is DllNotFoundException or FileLoadException or BadImageFormatException ||
                     e is FileNotFoundException { FileName.Length: > 0 } || e.HResult == unchecked((int)0x80040154))) kind = CameraFailureKind.RuntimeDependency;
        var message = kind switch
        {
            CameraFailureKind.AccessDenied => "Camera access denied. Check Windows camera privacy permissions and execution restrictions.",
            CameraFailureKind.DeviceBusy => "Camera is in use by another application. Close that application and retry.",
            CameraFailureKind.RuntimeDependency => "A camera runtime dependency could not be loaded. Check the named dependency and Windows Media runtime installation.",
            CameraFailureKind.EnumerationFailed => "Windows camera enumeration failed. An empty camera list has not been confirmed. Check Windows runtime availability and execution restrictions.",
            _ => "Cannot open camera. Check its connection and driver; refresh devices and retry."
        };
        // A hardware MFT start failure can indicate contention OR a driver fault; do not assert busy.
        if (chain.Any(e => e.HResult == unchecked((int)0xC00D3704))) message += " The camera driver could not start streaming; check other camera applications and the driver.";
        if (enumerating && kind == CameraFailureKind.EnumerationFailed && chain.Any(e => e is FileNotFoundException { FileName: null or "" }))
            message += " If launched by a sandbox or automation runner, retry Refresh Devices in the normal CaptureFlow desktop app. This error does not identify a missing DLL or a disabled camera.";
        var details = string.Join(" -> ", chain.Select(e => $"{e.GetType().Name} (0x{e.HResult:X8}): {e.Message}" +
            (e is FileNotFoundException missing && !string.IsNullOrEmpty(missing.FileName) ? $" File: {missing.FileName}" : "")));
        return new(kind, message + " " + details, error);
    }
}
