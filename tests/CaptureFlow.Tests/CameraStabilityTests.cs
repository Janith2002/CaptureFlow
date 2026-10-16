using System.Buffers;
using CaptureFlow.Application.Video;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Tests;

public class CameraStabilityTests
{
    private static readonly CameraFormat Format = new("fixture", 16, 16, 30, 1, "BGRA");
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task StopOrShutdownDuringInitializationWaitsForTerminalOpenAndClosesExactlyOnce(bool shutdown)
    {
        var backend = new Backend { BlockOpen = true }; var camera = new CameraPreviewService(backend);
        var select = camera.SelectCameraAsync("fixture"); await backend.OpenEntered.Task;
        Assert.Equal(CameraLifecycleState.Initializing, camera.LifecycleState);
        var close = shutdown ? camera.DisposeAsync().AsTask() : camera.StopPreviewAsync();
        Assert.False(close.IsCompleted); Assert.False(select.IsCompleted); Assert.Equal(0, backend.Last.Disposals);
        backend.OpenReleased.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => select); await close;
        Assert.Equal(1, backend.Last.Disposals); Assert.Null(camera.DeviceId);
        Assert.Equal(shutdown ? CameraLifecycleState.Disposed : CameraLifecycleState.Uninitialized, camera.LifecycleState);
        await camera.DisposeAsync();
    }
    [Fact] public async Task CancellationDuringInitializationDoesNotDisposeAnInFlightResource()
    {
        var backend = new Backend { BlockOpen = true }; await using var camera = new CameraPreviewService(backend); using var cancel = new CancellationTokenSource();
        var select = camera.SelectCameraAsync("fixture", cancel.Token); await backend.OpenEntered.Task; cancel.Cancel();
        Assert.False(select.IsCompleted); Assert.Equal(0, backend.Last.Disposals);
        backend.OpenReleased.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => select); Assert.Equal(1, backend.Last.Disposals);
    }
    [Fact] public async Task ConcurrentCleanupCallersAwaitOneNativeDisposalAndPreventEarlyReuse()
    {
        var backend = new Backend(); var camera = new CameraPreviewService(backend); await camera.SelectCameraAsync("fixture"); backend.Last.BlockDispose = true;
        var first = camera.DisposeAsync().AsTask(); var second = camera.DisposeAsync().AsTask(); await backend.Last.DisposeEntered.Task;
        Assert.Same(first, second); Assert.False(second.IsCompleted); Assert.Equal(1, backend.Last.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => camera.SelectCameraAsync("another"));
        backend.Last.DisposeReleased.SetResult(); await Task.WhenAll(first, second); await camera.DisposeAsync(); Assert.Equal(1, backend.Last.Disposals);
    }
    [Fact] public async Task ReinitializationWaitsForPreviousCleanupAndUsesOnlyOnePhysicalOwner()
    {
        var backend = new Backend(); await using var camera = new CameraPreviewService(backend); await camera.SelectCameraAsync("first"); var old = backend.Last; old.BlockDispose = true;
        var next = camera.SelectCameraAsync("second"); await old.DisposeEntered.Task;
        Assert.Equal(1, backend.Opens); Assert.False(next.IsCompleted); old.DisposeReleased.SetResult(); await next;
        Assert.Equal(2, backend.Opens); Assert.Equal(1, old.Disposals); Assert.Equal("second", camera.DeviceId);
    }
    [Fact] public async Task RepeatedStartAndStopAreIdempotentAndFaultRecoveryReopensAfterCleanup()
    {
        var backend = new Backend(); await using var camera = new CameraPreviewService(backend);
        await camera.SelectCameraAsync("fixture"); var first = backend.Last;
        await camera.StartPreviewAsync(Format); await camera.StartPreviewAsync(Format); Assert.Equal(1, first.Starts); Assert.Equal(CameraLifecycleState.Capturing, camera.LifecycleState);
        first.Fail(); await camera.StopPreviewAsync(); await camera.StopPreviewAsync(); Assert.Equal(1, first.Disposals);
        await camera.SelectCameraAsync("fixture"); await camera.StartPreviewAsync(Format); Assert.Equal(2, backend.Opens); Assert.True(camera.IsPreviewing);
    }
    [Fact] public async Task StaleFailureAndDisposedRecordingLeaseCannotAffectTheNextCamera()
    {
        var backend = new Backend(); await using var camera = new CameraPreviewService(backend); await camera.SelectCameraAsync("first"); await camera.StartPreviewAsync(Format);
        var lease = await camera.AcquireRecordingSourceAsync(new(VideoSourceKind.Webcam, "first", CameraFormat: Format), default); var stale = backend.Last.CaptureFailure();
        var notifications = 0; lease.Failed += (_, _) => notifications++;
        await lease.DisposeAsync(); await camera.StopPreviewAsync(); await camera.SelectCameraAsync("second"); await camera.StartPreviewAsync(Format); stale();
        Assert.Equal(0, notifications); Assert.Equal("second", camera.DeviceId); Assert.True(camera.IsPreviewing); Assert.Equal(CameraLifecycleState.Capturing, camera.LifecycleState);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task InitializationAndDeviceRemovalFailuresReleaseResourcesAndAllowRecovery(bool removal)
    {
        var backend = new Backend(); await using var camera = new CameraPreviewService(backend);
        if (!removal) { backend.OpenError = new IOException("camera unavailable"); await Assert.ThrowsAsync<IOException>(() => camera.SelectCameraAsync("fixture")); backend.OpenError = null; }
        else { await camera.SelectCameraAsync("fixture"); await camera.StartPreviewAsync(Format); backend.Last.Fail(); await camera.StopPreviewAsync(); Assert.Equal(1, backend.Last.Disposals); }
        await camera.SelectCameraAsync("fixture"); await camera.StartPreviewAsync(Format); Assert.True(camera.IsPreviewing);
    }
    [Fact] public async Task StopDuringPreviewStartCancelsAndDrainsBeforeDisposal()
    {
        var backend = new Backend(); await using var camera = new CameraPreviewService(backend); await camera.SelectCameraAsync("fixture"); backend.Last.BlockStart = true;
        var start = camera.StartPreviewAsync(Format); await backend.Last.StartEntered.Task; var stop = camera.StopPreviewAsync();
        Assert.False(stop.IsCompleted); Assert.Equal(0, backend.Last.Disposals);
        backend.Last.StartReleased.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start); await stop; Assert.Equal(1, backend.Last.Disposals);
    }
    [Fact] public async Task ShutdownDuringCaptureDrainsAnInFlightReadBeforeClosingNativeResources()
    {
        var backend = new Backend(); var camera = new CameraPreviewService(backend); await camera.SelectCameraAsync("fixture"); await camera.StartPreviewAsync(Format);
        var lease = await camera.AcquireRecordingSourceAsync(new(VideoSourceKind.Webcam, "fixture", CameraFormat: Format), default); backend.Last.BlockRead = true;
        var read = lease.ReadFrameAsync(default).AsTask(); await backend.Last.ReadEntered.Task; var shutdown = camera.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted); Assert.Equal(0, backend.Last.Disposals);
        backend.Last.ReadReleased.SetResult(); using var frame = await read; await shutdown; await lease.DisposeAsync(); Assert.Equal(1, backend.Last.Disposals);
    }
    [Fact] public async Task CleanupFailureBlocksAnotherCameraInsteadOfAssumingOwnershipWasReleased()
    {
        var backend = new Backend(); var camera = new CameraPreviewService(backend); await camera.SelectCameraAsync("fixture"); backend.Last.DisposeError = new IOException("native close failed");
        await Assert.ThrowsAsync<IOException>(camera.StopPreviewAsync); await Assert.ThrowsAsync<IOException>(() => camera.SelectCameraAsync("second")); Assert.Equal(1, backend.Opens);
        await Assert.ThrowsAsync<IOException>(() => camera.DisposeAsync().AsTask()); Assert.Equal(1, backend.Last.Disposals);
    }
    [Fact] public async Task TimeoutQuarantinesPendingNativeWorkAndDoesNotForceDisposeIt()
    {
        var clock = new Clock(); var backend = new Backend { BlockOpen = true }; var camera = new CameraPreviewService(backend, TimeSpan.FromSeconds(10), clock);
        var select = camera.SelectCameraAsync("fixture"); await backend.OpenEntered.Task; clock.Fire();
        await Assert.ThrowsAsync<TimeoutException>(() => select); Assert.Equal(0, backend.Last.Disposals); Assert.Equal(CameraLifecycleState.Faulted, camera.LifecycleState);
        await Assert.ThrowsAsync<IOException>(() => camera.SelectCameraAsync("second")); Assert.Equal(1, backend.Opens);
        var shutdown = camera.DisposeAsync().AsTask(); backend.OpenReleased.SetResult(); await shutdown; Assert.Equal(1, backend.Last.Disposals);
    }
    [Fact] public async Task FailedCleanupOfCancelledUnpublishedCameraAlsoBlocksReuse()
    {
        var backend = new Backend { BlockOpen = true }; var camera = new CameraPreviewService(backend); using var cancel = new CancellationTokenSource();
        var select = camera.SelectCameraAsync("fixture", cancel.Token); await backend.OpenEntered.Task;
        backend.Last.DisposeError = new IOException("unpublished close failed"); cancel.Cancel(); backend.OpenReleased.SetResult();
        await Assert.ThrowsAsync<IOException>(() => select); await Assert.ThrowsAsync<IOException>(() => camera.SelectCameraAsync("second"));
        await Assert.ThrowsAsync<IOException>(() => camera.DisposeAsync().AsTask()); Assert.Equal(1, backend.Opens); Assert.Equal(1, backend.Last.Disposals);
    }
    [Fact] public async Task InvalidConcurrentRecordingClaimsDoNotChangeAnActiveCamera()
    {
        var backend = new Backend(); await using var camera = new CameraPreviewService(backend); await camera.SelectCameraAsync("fixture"); await camera.StartPreviewAsync(Format);
        await using var lease = await camera.AcquireRecordingSourceAsync(new(VideoSourceKind.Webcam, "fixture", CameraFormat: Format), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => camera.SelectCameraAsync("second"));
        await Assert.ThrowsAsync<InvalidOperationException>(camera.StopPreviewAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(() => camera.AcquireRecordingSourceAsync(new(VideoSourceKind.Webcam, "fixture", CameraFormat: Format), default));
        Assert.Equal(1, backend.Opens); Assert.Equal(CameraLifecycleState.Capturing, camera.LifecycleState);
    }
    private sealed class Backend : ICameraCaptureBackend
    {
        public bool BlockOpen; public Exception? OpenError; public int Opens; public Session Last = new();
        public TaskCompletionSource OpenEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), OpenReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<CameraDevice>>([]);
        public async Task<ICameraCaptureSession> OpenAsync(string id, CancellationToken token)
        { if (OpenError is not null) throw OpenError; Opens++; Last = new(); OpenEntered.TrySetResult(); if (BlockOpen) await OpenReleased.Task; return Last; }
    }
    private sealed class Session : ICameraCaptureSession
    {
        public event EventHandler<Exception>? Failed;
        public IReadOnlyList<CameraFormat> SupportedFormats => [Format]; public VideoSize Size => new(16, 16);
        public bool BlockDispose, BlockStart, BlockRead; public Exception? DisposeError; public int Disposals, Starts;
        public TaskCompletionSource DisposeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), DisposeReleased = new(TaskCreationOptions.RunContinuationsAsynchronously),
            StartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), StartReleased = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ReadReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Fail() => Failed?.Invoke(this, new IOException("camera disconnected"));
        public Action CaptureFailure() { var handler = Failed; return () => handler?.Invoke(this, new IOException("stale callback")); }
        public Task ConfigureAsync(CameraFormat format, CancellationToken token) => Task.CompletedTask;
        public async Task StartAsync(CancellationToken token) { Starts++; StartEntered.TrySetResult(); if (BlockStart) await StartReleased.Task; }
        public async ValueTask<VideoFrame> ReadFrameAsync(CancellationToken token) { ReadEntered.TrySetResult(); if (BlockRead) await ReadReleased.Task; return new(MemoryPool<byte>.Shared.Rent(1024), Size, TimeSpan.Zero); }
        public async ValueTask DisposeAsync() { Disposals++; DisposeEntered.TrySetResult(); if (BlockDispose) await DisposeReleased.Task; if (DisposeError is not null) throw DisposeError; }
    }
    private sealed class Clock : TimeProvider
    {
        private readonly List<ManualTimer> timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) { var timer = new ManualTimer(callback, state); timers.Add(timer); return timer; }
        public void Fire() { foreach (var timer in timers.ToArray()) timer.Fire(); }
        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            public void Fire() { if (!disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
