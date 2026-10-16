using System.Runtime.InteropServices;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;

namespace CaptureFlow.Tests;

public class CameraErrorsTests
{
    [Fact]
    public void UnnamedWinRtFileNotFoundIsEnumerationFailureNotProofOfMissingDllOrNoCamera()
    {
        var original = new FileNotFoundException("Unable to find the specified file.");
        var result = CameraErrors.Describe(original, enumerating: true);
        Assert.Equal(CameraFailureKind.EnumerationFailed, result.Kind);
        Assert.Same(original, result.InnerException);
        Assert.Contains("0x80070002", result.Message);
        Assert.Contains("empty camera list has not been confirmed", result.Message);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void AccessDeniedRemainsDistinctThroughInnerExceptions(bool enumerating)
    {
        var result = CameraErrors.Describe(new InvalidOperationException("wrapper", new UnauthorizedAccessException("denied")), enumerating);
        Assert.Equal(CameraFailureKind.AccessDenied, result.Kind);
        Assert.Contains("0x80070005", result.Message);
    }
    [Fact]
    public void SharingViolationReportsDeviceBusy()
    {
        var result = CameraErrors.Describe(new COMException("sharing violation", unchecked((int)0x80070020)), false);
        Assert.Equal(CameraFailureKind.DeviceBusy, result.Kind);
    }
    [Fact]
    public void HardwareStreamingFailureDoesNotGuaranteeAnotherApplicationIsUsingCamera()
    {
        var result = CameraErrors.Describe(new COMException("MFT start failed", unchecked((int)0xC00D3704)), false);
        Assert.Equal(CameraFailureKind.OpenFailed, result.Kind);
        Assert.Contains("driver", result.Message);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ActualLoaderFailuresReportRuntimeDependency(int variant)
    {
        Exception original = variant switch
        {
            0 => new FileNotFoundException("missing assembly", "missing-camera.dll"),
            1 => new DllNotFoundException("missing-native.dll"),
            2 => new FileLoadException("load failed"),
            3 => new BadImageFormatException("wrong architecture"),
            _ => new COMException("class not registered", unchecked((int)0x80040154))
        };
        var result = CameraErrors.Describe(new TypeInitializationException("camera", original), true);
        Assert.Equal(CameraFailureKind.RuntimeDependency, result.Kind);
        Assert.Same(original, result.InnerException!.InnerException);
        if (variant == 0) Assert.Contains("missing-camera.dll", result.Message);
    }
    [Fact]
    public void KnownCameraFailureIsNotReclassifiedWhenOpening()
    {
        var original = CameraErrors.Describe(new IOException("enumeration broke"), true);
        Assert.Same(original, CameraErrors.Describe(original, false));
    }
}
