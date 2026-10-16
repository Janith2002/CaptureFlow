using System.Buffers;
using CaptureFlow.Application.Video;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Tests;

public class CameraPreviewServiceTests
{
    private static readonly CameraFormat Hd30 = new("hd30", 1280, 720, 30, 1, "NV12");
    private static readonly CameraFormat Hd60 = new("hd60", 1280, 720, 60, 1, "NV12");
    private static readonly CameraFormat FullHd30 = new("fullhd", 1920, 1080, 30000, 1001, "MJPG");
    [Theory]
    [InlineData(CameraFailureKind.AccessDenied)] [InlineData(CameraFailureKind.DeviceBusy)]
    [InlineData(CameraFailureKind.RuntimeDependency)] [InlineData(CameraFailureKind.EnumerationFailed)]
    public async Task EnumerationErrorsArePreservedAndNeverBecomeEmptyDeviceLists(CameraFailureKind kind)
    {
        var error = new CameraOperationException(kind, "enumeration failed", new IOException("original"));
        var backend = new Backend { DiscoveryError = error };
        await using var service = new CameraPreviewService(backend);
        Assert.Same(error, await Assert.ThrowsAsync<CameraOperationException>(() => service.GetDevicesAsync()));
        Assert.Equal(0, backend.Opens);
        Assert.Null(service.DeviceId);
        Assert.False(service.IsPreviewing);
    }
    [Fact]
    public async Task DiscoveryCancellationIsNotReportedAsDeviceAbsence()
    {
        var backend = new Backend { DiscoveryError = new OperationCanceledException() };
        await using var service = new CameraPreviewService(backend);
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetDevicesAsync());
        Assert.Equal(0, backend.Opens);
    }
    [Fact]
    public async Task DiscoveryDoesNotOpenCameraAndSelectionUsesStableDeviceId()
    {
        var backend = new Backend(); await using var service = new CameraPreviewService(backend);
        var devices = await service.GetDevicesAsync(); Assert.Equal(2, devices.Count); Assert.Equal(0, backend.Opens);
        var formats = await service.SelectCameraAsync("usb");
        Assert.Equal("usb", backend.SelectedId); Assert.Equal("usb", service.DeviceId); Assert.Equal(3, formats.Count);
        Assert.False(service.IsPreviewing);
    }
    [Fact]
    public async Task PreviewAndRecordingShareOneCaptureAndStopRecordingKeepsPreview()
    {
        var backend = new Backend(); await using var service = new CameraPreviewService(backend);
        await service.SelectCameraAsync("built-in"); await service.StartPreviewAsync(Hd30);
        await using (var recording = await service.AcquireRecordingSourceAsync(new(VideoSourceKind.Webcam, "built-in", CameraFormat: Hd30), CancellationToken.None))
        {
            await recording.StartAsync(CancellationToken.None);
            using var previewFrame = await service.ReadPreviewFrameAsync();
            using var recordingFrame = await recording.ReadFrameAsync(CancellationToken.None);
            Assert.Equal(previewFrame.Size, recordingFrame.Size); Assert.Equal(1, backend.Opens); Assert.Equal(1, backend.Last.StartCount);
        }
        Assert.True(service.IsPreviewing); Assert.Equal(0, backend.Last.Disposals);
        await service.StopPreviewAsync(); Assert.False(service.IsPreviewing); Assert.Null(service.DeviceId); Assert.Equal(1, backend.Last.Disposals);
    }
    [Fact]
    public async Task CameraAndFormatChangesAndSecondRecordingAreRejectedWhileLeased()
    {
        var backend = new Backend(); await using var service = new CameraPreviewService(backend);
        await service.SelectCameraAsync("usb"); await service.StartPreviewAsync(Hd30);
        await using var lease = await service.AcquireRecordingSourceAsync(new(VideoSourceKind.Webcam, "usb", CameraFormat: Hd30), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SelectCameraAsync("built-in"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartPreviewAsync(Hd60));
        await Assert.ThrowsAsync<InvalidOperationException>(service.StopPreviewAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcquireRecordingSourceAsync(new(VideoSourceKind.Webcam, "usb", CameraFormat: Hd30), CancellationToken.None));
        Assert.Equal(1, backend.Opens);
    }
    [Fact]
    public async Task UnsupportedFormatIsRejectedBeforeConfiguringNativeDevice()
    {
        var backend = new Backend(); await using var service = new CameraPreviewService(backend);
        await service.SelectCameraAsync("usb");
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StartPreviewAsync(Hd30 with { Width = 3840 }));
        Assert.Equal(0, backend.Last.ConfigureCount);
    }
    [Theory]
    [InlineData("other", false)] [InlineData("usb", true)]
    public async Task RecordingRequiresMatchingPreviewDeviceAndFormat(string device, bool wrongFormat)
    {
        var backend = new Backend(); await using var service = new CameraPreviewService(backend);
        await service.SelectCameraAsync("usb"); await service.StartPreviewAsync(Hd30);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcquireRecordingSourceAsync(new(VideoSourceKind.Webcam, device, CameraFormat: wrongFormat ? Hd60 : Hd30), CancellationToken.None));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PermissionOrInUseFailuresLeaveNoSelectedCamera(bool denied)
    {
        var backend = new Backend { OpenError = denied ? new UnauthorizedAccessException("camera permission denied") : new IOException("camera in use") };
        await using var service = new CameraPreviewService(backend);
        await Assert.ThrowsAnyAsync<Exception>(() => service.SelectCameraAsync("usb"));
        Assert.Null(service.DeviceId); Assert.False(service.IsPreviewing);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ConfigureOrStartFailureDisposesTheDevice(bool configure)
    {
        var backend = new Backend(); await using var service = new CameraPreviewService(backend);
        await service.SelectCameraAsync("usb"); backend.Last.ConfigureFailure = configure; backend.Last.StartFailure = !configure;
        await Assert.ThrowsAsync<IOException>(() => service.StartPreviewAsync(Hd30));
        Assert.Equal(1, backend.Last.Disposals); Assert.False(service.IsPreviewing); Assert.Null(service.DeviceId);
    }
    [Fact]
    public async Task SelectingAnotherCameraReleasesThePreviousOne()
    {
        var backend = new Backend(); await using var service = new CameraPreviewService(backend);
        await service.SelectCameraAsync("built-in"); var first = backend.Last; await service.StartPreviewAsync(Hd30);
        await service.SelectCameraAsync("usb"); Assert.Equal(1, first.Disposals); Assert.False(service.IsPreviewing); Assert.Equal("usb", service.DeviceId);
    }
    [Fact]
    public async Task ChangingPreviewFormatReconfiguresOnePhysicalSession()
    {
        var backend = new Backend(); await using var service = new CameraPreviewService(backend);
        await service.SelectCameraAsync("usb"); await service.StartPreviewAsync(Hd30); await service.StartPreviewAsync(Hd60);
        Assert.Equal(1, backend.Opens); Assert.Equal(2, backend.Last.ConfigureCount); Assert.Equal(Hd60, service.ActiveFormat);
    }
    [Fact]
    public async Task DelayedFailureFromPreviousCameraDoesNotStopItsSuccessor()
    {
        var backend = new Backend(); await using var service = new CameraPreviewService(backend);
        await service.SelectCameraAsync("built-in");
        var delayedFailure = backend.Last.CaptureFailureNotification();
        await service.SelectCameraAsync("usb"); await service.StartPreviewAsync(Hd30);
        delayedFailure(); Assert.True(service.IsPreviewing); Assert.Equal("usb", service.DeviceId);
    }
    [Fact]
    public async Task ReadFailureStopsPreviewAndReleasesDevice()
    {
        var backend = new Backend(); await using var service = new CameraPreviewService(backend);
        await service.SelectCameraAsync("usb"); await service.StartPreviewAsync(Hd30); backend.Last.ReadFailure = true;
        await Assert.ThrowsAsync<IOException>(async () => { using var frame = await service.ReadPreviewFrameAsync(); });
        // Wait behind the serialized failure cleanup, without polling.
        await service.StopPreviewAsync();
        Assert.False(service.IsPreviewing); Assert.Equal(1, backend.Last.Disposals); Assert.Null(service.DeviceId);
    }
    [Fact]
    public async Task NoDevicesProducesAnEmptyDiscoveryWithoutCapture()
    {
        var backend = new Backend { NoDevices = true }; await using var service = new CameraPreviewService(backend);
        Assert.Empty(await service.GetDevicesAsync()); Assert.Equal(0, backend.Opens);
    }
    [Fact]
    public async Task ShutdownAwaitsPendingInitializationAndClosesNewlyOpenedSession()
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend { PendingOpen = opened.Task };
        var service = new CameraPreviewService(backend);
        var selection = service.SelectCameraAsync("usb");
        await backend.OpenEntered.Task;
        var shutdown = service.DisposeAsync().AsTask(); Assert.False(shutdown.IsCompleted);
        opened.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => selection); await shutdown;
        Assert.Equal(1, backend.Last.Disposals); Assert.Null(service.DeviceId);
    }
    [Fact]
    public async Task DeviceFailurePropagatesToRecordingAndReleaseClosesFaultedCamera()
    {
        var backend = new Backend(); await using var service = new CameraPreviewService(backend);
        await service.SelectCameraAsync("usb"); await service.StartPreviewAsync(Hd30);
        var lease = await service.AcquireRecordingSourceAsync(new(VideoSourceKind.Webcam, "usb", CameraFormat: Hd30), CancellationToken.None);
        Exception? observed = null; lease.Failed += (_, ex) => observed = ex;
        backend.Last.Fail(); Assert.NotNull(observed); Assert.False(service.IsPreviewing);
        await lease.DisposeAsync(); await lease.DisposeAsync(); Assert.Equal(1, backend.Last.Disposals); Assert.Null(service.DeviceId);
    }
    [Fact]
    public async Task ShutdownAndLeaseDisposalAreIdempotent()
    {
        var backend = new Backend(); var service = new CameraPreviewService(backend);
        await service.SelectCameraAsync("usb"); await service.StartPreviewAsync(Hd30);
        var lease = await service.AcquireRecordingSourceAsync(new(VideoSourceKind.Webcam, "usb", CameraFormat: Hd30), CancellationToken.None);
        await service.DisposeAsync(); await service.DisposeAsync(); await lease.DisposeAsync(); await lease.DisposeAsync();
        Assert.Equal(1, backend.Last.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.SelectCameraAsync("usb"));
    }
    [Fact]
    public async Task CancellationDuringOpenDisposesSessionBeforePublishingSelection()
    {
        using var cancellation = new CancellationTokenSource();
        var backend = new Backend { OnOpen = cancellation.Cancel }; await using var service = new CameraPreviewService(backend);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SelectCameraAsync("usb", cancellation.Token));
        Assert.Equal(1, backend.Last.Disposals); Assert.Null(service.DeviceId);
    }
    [Fact]
    public void FormatSelectorsOnlyOfferReportedResolutionRatePairs()
    {
        CameraFormat[] formats = [Hd30, Hd60, FullHd30, Hd30 with { Id = "other-subtype", Subtype = "MJPG" }];
        Assert.Equal(new[] { "1920 × 1080", "1280 × 720" }, CameraFormatSelection.Resolutions(formats));
        Assert.Equal(new[] { Hd30, Hd60 }, CameraFormatSelection.FrameRates(formats, "1280 × 720"));
        Assert.Single(CameraFormatSelection.FrameRates(formats, "1920 × 1080"));
        Assert.Empty(CameraFormatSelection.FrameRates(formats, "3840 × 2160"));
        Assert.Equal(29.97002997, FullHd30.FramesPerSecond, 8);
    }
    [Theory]
    [InlineData(0, 30, 1)] [InlineData(1280, 0, 1)] [InlineData(1280, 30, 0)] [InlineData(1280, 300, 1)]
    public void InvalidDeviceFormatsAreRejected(int width, uint numerator, uint denominator) => Assert.Throws<ArgumentException>(() =>
        (Hd30 with { Width = width, FrameRateNumerator = numerator, FrameRateDenominator = denominator }).Validate());
    [Fact]
    public void WebcamConfigurationUsesReportedFractionalFrameRateAndUniqueWebcamFilename()
    {
        var configuration = new VideoRecordingConfiguration(new(VideoSourceKind.Webcam, "usb", CameraFormat: FullHd30), Path.GetTempPath());
        configuration.Validate(); Assert.Equal(FullHd30.FramesPerSecond, configuration.EffectiveFramesPerSecond);
        Assert.StartsWith("CaptureFlow_Webcam_", VideoRecordingFileName.Create(DateTimeOffset.UtcNow, Guid.NewGuid(), VideoSourceKind.Webcam));
        Assert.Throws<ArgumentException>(() => (configuration with { Source = new(VideoSourceKind.Webcam, "usb") }).Validate());
    }
    [Theory]
    [InlineData(CaptureFlow.Core.Audio.AudioRecordingMode.SystemAudioOnly)]
    [InlineData(CaptureFlow.Core.Audio.AudioRecordingMode.MicrophoneAndSystemAudio)]
    public void WebcamSystemAudioIsExplicitlyPending(CaptureFlow.Core.Audio.AudioRecordingMode mode)
    {
        var configuration = new VideoRecordingConfiguration(new(VideoSourceKind.Webcam, "usb", CameraFormat: Hd30), Path.GetTempPath(),
            Audio: new("microphone", Path.GetTempPath(), Mode: mode, OutputDeviceId: "output"));
        Assert.Throws<NotSupportedException>(configuration.Validate);
    }
    private sealed class Backend : ICameraCaptureBackend
    {
        public int Opens { get; private set; }
        public string? SelectedId { get; private set; }
        public Session Last { get; private set; } = new();
        public Exception? OpenError { get; init; }
        public Action? OnOpen { get; init; }
        public bool NoDevices { get; init; }
        public Exception? DiscoveryError { get; init; }
        public Task? PendingOpen { get; init; }
        public TaskCompletionSource OpenEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) => DiscoveryError is not null
            ? Task.FromException<IReadOnlyList<CameraDevice>>(DiscoveryError)
            : Task.FromResult<IReadOnlyList<CameraDevice>>(NoDevices ? [] : [new("built-in", "Integrated camera"), new("usb", "USB camera")]);
        public async Task<ICameraCaptureSession> OpenAsync(string deviceId, CancellationToken cancellationToken)
        {
            if (OpenError is not null) throw OpenError;
            Opens++; SelectedId = deviceId; Last = new(); OpenEntered.TrySetResult();
            if (PendingOpen is not null) await PendingOpen;
            OnOpen?.Invoke(); return Last;
        }
    }
    private sealed class Session : ICameraCaptureSession
    {
        public event EventHandler<Exception>? Failed;
        public IReadOnlyList<CameraFormat> SupportedFormats => [Hd30, Hd60, FullHd30];
        public VideoSize Size => new(2, 2);
        public bool ConfigureFailure { get; set; }
        public bool StartFailure { get; set; }
        public bool ReadFailure { get; set; }
        public int StartCount { get; private set; }
        public int ConfigureCount { get; private set; }
        public int Disposals { get; private set; }
        public void Fail() => Failed?.Invoke(this, new IOException("camera disconnected"));
        public Action CaptureFailureNotification() { var handler = Failed; return () => handler?.Invoke(this, new IOException("delayed disconnect")); }
        public Task ConfigureAsync(CameraFormat format, CancellationToken cancellationToken)
        { ConfigureCount++; if (ConfigureFailure) throw new IOException("unsupported format"); return Task.CompletedTask; }
        public Task StartAsync(CancellationToken cancellationToken)
        { StartCount++; if (StartFailure) throw new IOException("camera cannot start"); return Task.CompletedTask; }
        public ValueTask<VideoFrame> ReadFrameAsync(CancellationToken cancellationToken)
        { if (ReadFailure) throw new IOException("camera no longer delivers frames"); return ValueTask.FromResult(new VideoFrame(MemoryPool<byte>.Shared.Rent(16), Size, TimeSpan.Zero)); }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
