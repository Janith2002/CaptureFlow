using System.Buffers;
using CaptureFlow.Application.Video;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Tests;
public class ScreenPreviewTests
{
    [Fact] public async Task RecordingAndPreviewShareOneSourceAndPreviewCannotConsumeRecordingFrames()
    {
        var backend = new Backend(); await using var service = new ScreenPreviewService(backend); var options = new VideoSourceOptions(VideoSourceKind.Monitor, "fixture");
        await service.StartAsync(options, default);
        using (var before = await service.ReadPreviewAsync(default)) Assert.NotNull(before);
        await using (var lease = await service.AcquireAsync(options, default))
        {
            lease.SetRecordingActivity(false); Assert.False(backend.Source.Activity);
            using var recorded = await lease.ReadFrameAsync(default); var reads = backend.Source.Reads;
            for (var i = 0; i < 100; i++) { using var preview = await service.ReadPreviewAsync(default); Assert.Equal(recorded.Pixels.ToArray(), preview!.Pixels.ToArray()); }
            Assert.Equal(reads, backend.Source.Reads); Assert.Equal(1, backend.Creates);
            await Assert.ThrowsAsync<InvalidOperationException>(service.StopAsync);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcquireAsync(options, default));
        }
        Assert.True(backend.Source.Activity);
        using (var after = await service.ReadPreviewAsync(default)) Assert.NotNull(after);
        await service.StopAsync(); await service.StopAsync(); Assert.Equal(1, backend.Source.Disposals); Assert.Null(service.ActiveOptions);
    }
    [Fact] public async Task FailedStartReleasesNativeSourceAndSupportsSafeReuse()
    {
        var backend = new Backend { FailStart = true }; await using var service = new ScreenPreviewService(backend); var options = new VideoSourceOptions(VideoSourceKind.Monitor, "fixture");
        await Assert.ThrowsAsync<IOException>(() => service.StartAsync(options, default)); Assert.Equal(1, backend.Source.Disposals); Assert.Null(service.ActiveOptions);
        backend.FailStart = false; await service.StartAsync(options, default); Assert.Equal(options, service.ActiveOptions);
    }
    [Fact] public async Task CancellationAndDisposedServiceCannotOpenCapture()
    {
        var backend = new Backend(); var service = new ScreenPreviewService(backend); var options = new VideoSourceOptions(VideoSourceKind.Monitor, "fixture");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StartAsync(options, cancellation.Token)); Assert.Equal(0, backend.Creates);
        await service.DisposeAsync(); await service.DisposeAsync(); await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(options, default)); Assert.Equal(0, backend.Creates);
    }
    [Fact] public async Task SourceIdentityCannotSilentlyChangeDuringAcquisition()
    {
        var backend = new Backend(); await using var service = new ScreenPreviewService(backend); await service.StartAsync(new(VideoSourceKind.Monitor, "fixture"), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcquireAsync(new(VideoSourceKind.Monitor, "other"), default)); Assert.Equal("fixture", service.ActiveOptions!.Id);
    }
    private sealed class Backend : IMonitorCaptureBackend
    {
        public int Creates; public bool FailStart; public Source Source = new(false);
        public Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<MonitorDescriptor>>([]);
        public Task<IVideoFrameSource> CreateAsync(VideoSourceOptions options, CancellationToken token) { Creates++; Source = new(FailStart); return Task.FromResult<IVideoFrameSource>(Source); }
    }
    private sealed class Source(bool fail) : IVideoFrameSource
    {
        public event EventHandler<Exception>? Failed { add { } remove { } }
        public int Reads, Disposals; public bool Activity = true; public VideoSize Size => new(16, 16);
        public void SetRecordingActivity(bool active) => Activity = active;
        public Task StartAsync(CancellationToken token) => fail ? Task.FromException(new IOException("controlled start failure")) : Task.CompletedTask;
        public ValueTask<VideoFrame> ReadFrameAsync(CancellationToken token) { Reads++; var memory = MemoryPool<byte>.Shared.Rent(1024); memory.Memory.Span[..1024].Fill((byte)Reads); return ValueTask.FromResult(new VideoFrame(memory, Size, TimeSpan.Zero)); }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
