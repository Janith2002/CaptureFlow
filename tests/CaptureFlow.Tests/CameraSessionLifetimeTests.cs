using System.Buffers;
using System.Collections.Concurrent;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;

public class CameraSessionLifetimeTests
{
    private static readonly CameraFormat Format = new("fixture", 16, 16, 30, 1, "BGRA");
    [Fact] public async Task NativeControlsAndUnsubscriptionReturnToTheSameStaOwner()
    {
        using var context = new StaContext(); var native = new Resource(context);
        var owner = new CameraOwnerContext(context);
        var session = await owner.InvokeAsync(() => Task.FromResult(new CameraSessionLifetime(native, owner)));
        await Task.Run(() => session.ConfigureAsync(Format, default)); await Task.Run(() => session.StartAsync(default));
        await Task.Run(() => session.DisposeAsync().AsTask()); await session.DisposeAsync();
        Assert.Equal(new[] { "subscribe", "configure", "start", "unsubscribe", "dispose" }, native.Calls);
        Assert.Equal(1, native.Disposals); Assert.Equal(CameraLifecycleState.Disposed, session.LifecycleState);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.StartAsync(default));
    }
    [Fact] public async Task DisposalWaitsForAnInFlightNativeOperationAndAllCallersShareItsResult()
    {
        using var context = new StaContext(); var native = new Resource(context) { BlockConfigure = true }; var owner = new CameraOwnerContext(context);
        var session = await owner.InvokeAsync(() => Task.FromResult(new CameraSessionLifetime(native, owner)));
        var configure = session.ConfigureAsync(Format, default); await native.Entered.Task;
        var first = session.DisposeAsync().AsTask(); var second = session.DisposeAsync().AsTask();
        Assert.Same(first, second); Assert.False(first.IsCompleted); Assert.Equal(0, native.Disposals);
        native.Released.SetResult(); await Assert.ThrowsAsync<ObjectDisposedException>(() => configure); await first;
        Assert.Equal(1, native.Disposals);
    }
    [Fact] public async Task CancelledNativeControlDrainsBeforeDisposalAndDoesNotPublishCapturing()
    {
        using var context = new StaContext(); var native = new Resource(context) { BlockConfigure = true }; var owner = new CameraOwnerContext(context);
        var session = await owner.InvokeAsync(() => Task.FromResult(new CameraSessionLifetime(native, owner)));
        using var cancel = new CancellationTokenSource(); var configure = session.ConfigureAsync(Format, cancel.Token); await native.Entered.Task; cancel.Cancel();
        Assert.False(configure.IsCompleted); Assert.Equal(0, native.Disposals); native.Released.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => configure); Assert.Equal(CameraLifecycleState.Faulted, session.LifecycleState);
        await session.DisposeAsync(); Assert.Equal(1, native.Disposals);
    }
    [Fact] public async Task StaleCallbacksAfterClosingDoNotNotifyNewConsumers()
    {
        using var context = new StaContext(); var native = new Resource(context); var owner = new CameraOwnerContext(context);
        var session = await owner.InvokeAsync(() => Task.FromResult(new CameraSessionLifetime(native, owner))); var stale = native.Notification(); var notified = 0;
        session.Failed += (_, _) => notified++; await session.DisposeAsync(); stale();
        Assert.Equal(0, notified); Assert.Equal(CameraLifecycleState.Disposed, session.LifecycleState);
    }
    [Fact] public async Task CleanupFailureIsRetainedAndNativeDisposalIsNotRetried()
    {
        using var context = new StaContext(); var native = new Resource(context) { FailDispose = true }; var owner = new CameraOwnerContext(context);
        var session = await owner.InvokeAsync(() => Task.FromResult(new CameraSessionLifetime(native, owner)));
        await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask()); await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask());
        Assert.Equal(1, native.Disposals); Assert.Equal(CameraLifecycleState.Faulted, session.LifecycleState);
    }
    private sealed class Resource(StaContext context) : ICameraCaptureSession
    {
        private EventHandler<Exception>? failed;
        public List<string> Calls { get; } = []; public int Disposals; public bool BlockConfigure, FailDispose;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<Exception>? Failed { add { Check("subscribe"); failed += value; } remove { Check("unsubscribe"); failed -= value; } }
        public Action Notification() { var handler = failed; return () => handler?.Invoke(this, new IOException("old native callback")); }
        public IReadOnlyList<CameraFormat> SupportedFormats => [Format]; public VideoSize Size => new(16, 16);
        public async Task ConfigureAsync(CameraFormat format, CancellationToken token) { Check("configure"); Entered.TrySetResult(); if (BlockConfigure) await Released.Task; Assert.Same(context, SynchronizationContext.Current); }
        public Task StartAsync(CancellationToken token) { Check("start"); return Task.CompletedTask; }
        public ValueTask<VideoFrame> ReadFrameAsync(CancellationToken token) => ValueTask.FromResult(new VideoFrame(MemoryPool<byte>.Shared.Rent(1024), Size, TimeSpan.Zero));
        public ValueTask DisposeAsync() { Check("dispose"); Disposals++; if (FailDispose) throw new IOException("native close failed"); return ValueTask.CompletedTask; }
        private void Check(string operation) { Assert.Same(context, SynchronizationContext.Current); Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState()); Calls.Add(operation); }
    }
    private sealed class StaContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<Action> work = new(); private readonly Thread thread;
        public StaContext()
        {
            thread = new Thread(() => { SetSynchronizationContext(this); foreach (var action in work.GetConsumingEnumerable()) action(); }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        public override void Post(SendOrPostCallback callback, object? state) => work.Add(() => callback(state));
        public void Dispose() { work.CompleteAdding(); Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Owner context did not drain."); work.Dispose(); }
    }
}
