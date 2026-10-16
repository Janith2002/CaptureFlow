using CaptureFlow.Core.Video;
using CaptureFlow.Application.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;
public class CameraDiscoveryLifetimeTests
{
    [Fact] public async Task PreCancelledDiscoveryNeverInvokesBackend()
    {
        var backend = new Discovery(); await using var service = new CameraPreviewService(backend); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetDevicesAsync(cancel.Token)); Assert.Equal(0, backend.Calls); Assert.Equal(0, backend.Opens);
        // Also verifies the native adapter refuses cancellation before reaching any Windows API.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WindowsCameraCaptureBackend().GetDevicesAsync(cancel.Token));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task LateEnumerationResultsAreNotPublishedAfterCancellationOrDisposal(bool cancelRequest)
    {
        var backend = new Discovery(); await using var service = new CameraPreviewService(backend); using var cancel = new CancellationTokenSource();
        var query = service.GetDevicesAsync(cancel.Token); Assert.Equal(1, backend.Calls);
        if (cancelRequest) cancel.Cancel(); else await service.DisposeAsync();
        backend.Result.SetResult([new("mock-id", "Mock camera")]);
        if (cancelRequest) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        else await Assert.ThrowsAsync<ObjectDisposedException>(() => query);
        Assert.Equal(0, backend.Opens); Assert.Null(service.DeviceId); Assert.False(service.IsPreviewing);
    }
    [Fact] public async Task DisposedDiscoveryNeverStartsNewBackendWork()
    { var backend = new Discovery(); var service = new CameraPreviewService(backend); await service.DisposeAsync(); await Assert.ThrowsAsync<ObjectDisposedException>(() => service.GetDevicesAsync()); Assert.Equal(0, backend.Calls); }
    [Fact] public async Task EnumerationFailureCanBeRetriedWithoutOpeningCamera()
    {
        var backend = new Discovery(); await using var service = new CameraPreviewService(backend); backend.Result.SetException(new IOException("mock enumeration unavailable"));
        await Assert.ThrowsAsync<IOException>(() => service.GetDevicesAsync()); backend.Result = new(TaskCreationOptions.RunContinuationsAsynchronously); backend.Result.SetResult([]);
        Assert.Empty(await service.GetDevicesAsync()); Assert.Equal(2, backend.Calls); Assert.Equal(0, backend.Opens);
    }
    private sealed class Discovery : ICameraCaptureBackend
    {
        public int Calls, Opens; public TaskCompletionSource<IReadOnlyList<CameraDevice>> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) { Calls++; return Result.Task; }
        public Task<ICameraCaptureSession> OpenAsync(string deviceId, CancellationToken token) { Opens++; throw new InvalidOperationException("Discovery must never open physical devices."); }
    }
}
