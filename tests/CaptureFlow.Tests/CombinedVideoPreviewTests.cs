using CaptureFlow.Application.Video;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;
public class CombinedVideoPreviewTests
{
    [Fact] public async Task RecordingLeaseForwardsMousePauseBoundariesToTheSingleScreenSource()
    {
        await using var f = new Fixture(); await f.StartAsync();
        await using (var lease = await f.Combined.AcquireRecordingSourceAsync(f.Options, default))
        { lease.SetRecordingActivity(false); lease.SetRecordingActivity(true); lease.SetRecordingActivity(false); Assert.Equal(new[] { false, true, false }, f.Screens.Source.MouseActivity); }
        Assert.True(f.Screens.Source.MouseActivity.Last()); Assert.Equal(1, f.Screens.Created);
    }
    [Theory] [InlineData(VideoSourceKind.Monitor)] [InlineData(VideoSourceKind.Window)] [InlineData(VideoSourceKind.Region)]
    public async Task AllScreenSourcesComposeRelativeToSelectedFrameAndReuseOneSession(VideoSourceKind kind)
    {
        await using var f = new Fixture();
        var window = kind == VideoSourceKind.Window ? new ApplicationWindowDescriptor("monitor", 12345, 123, 456, "Fixture", "Fixture", 80, 60) : null;
        var region = kind == VideoSourceKind.Region ? new ScreenRegion("monitor", 40, 60, 80, 60, 1920, 1080) : null;
        var options = f.Options with { ScreenKind = kind, Window = window, Region = region };
        await f.Cameras.SelectCameraAsync("camera"); await f.Cameras.StartPreviewAsync(f.Format);
        await f.Combined.StartPreviewAsync(options);
        Assert.Equal(options.ScreenSource, f.Screens.LastOptions);
        await using (var lease = await f.Combined.AcquireRecordingSourceAsync(options, default))
        {
            using var recorded = await lease.ReadFrameAsync(default); using var preview = await f.Combined.ReadPreviewFrameAsync();
            Assert.Equal(new VideoSize(80, 60), recorded.Size); Assert.Equal(recorded.Pixels.ToArray(), preview.Pixels.ToArray());
            Assert.Equal(1, f.Camera.Opens); Assert.Equal(1, f.Screens.Created);
        }
        await f.Combined.StopPreviewAsync(); Assert.Equal(1, f.Screens.Source.Disposals);
    }
    [Fact]
    public async Task PreviewAndRecordingUseOneScreenAndCameraAndPreviewReusesRecordedPixels()
    {
        await using var f = new Fixture(); await f.StartAsync();
        await using (var lease = await f.Combined.AcquireRecordingSourceAsync(f.Options, CancellationToken.None))
        {
            using var recorded = await lease.ReadFrameAsync(CancellationToken.None);
            var reads = f.Screens.Source.Reads;
            using var preview = await f.Combined.ReadPreviewFrameAsync();
            Assert.Equal(recorded.Pixels.ToArray(), preview.Pixels.ToArray()); Assert.Equal(reads, f.Screens.Source.Reads);
            Assert.Equal(1, f.Screens.Created); Assert.Equal(1, f.Camera.Opens);
            await Assert.ThrowsAsync<InvalidOperationException>(f.Combined.StopPreviewAsync);
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Combined.AcquireRecordingSourceAsync(f.Options, CancellationToken.None));
        }
        Assert.True(f.Combined.IsPreviewing); Assert.False(f.Combined.IsRecording);
        await f.Combined.StopPreviewAsync(); Assert.Equal(1, f.Screens.Source.Disposals);
        await f.Cameras.StopPreviewAsync(); Assert.Equal(1, f.Camera.Session.Disposals);
    }
    [Fact]
    public async Task DynamicLayoutAndVisibilityChangeTheActualComposedPixels()
    {
        await using var f = new Fixture(); await f.StartAsync();
        f.Layouts.Current = new(0, 0, .25, OverlayShape.Rectangle, BorderEnabled: false);
        using var initial = await f.Combined.ReadPreviewFrameAsync(); Assert.Equal((byte)255, OverlayCompositorTests.Pixel(initial, 2, 2, 2));
        f.Layouts.Current = f.Layouts.Current with { X = .7, Y = .7 };
        using var moved = await f.Combined.ReadPreviewFrameAsync(); Assert.Equal((byte)20, OverlayCompositorTests.Pixel(moved, 2, 2, 2));
        f.Layouts.Current = f.Layouts.Current with { Visible = false };
        using var hidden = await f.Combined.ReadPreviewFrameAsync(); Assert.All(hidden.Pixels.ToArray().Where((_, i) => i % 4 != 3), b => Assert.Equal((byte)20, b));
    }
    [Fact]
    public async Task SlowerBlockedCameraReadDoesNotHoldUpScreenFrameComposition()
    {
        await using var f = new Fixture(); f.Camera.Session.BlockAfterFirstFrame = true;
        await f.StartAsync(); await f.Camera.Session.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var result = await f.Combined.ReadPreviewFrameAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(new VideoSize(80, 60), result.Size); Assert.Equal(1, f.Screens.Source.Reads);
    }
    [Fact]
    public async Task LayoutChangeInvalidatesCachedRecordedPreview()
    {
        await using var f = new Fixture(); await f.StartAsync();
        await using var lease = await f.Combined.AcquireRecordingSourceAsync(f.Options, CancellationToken.None);
        using var recorded = await lease.ReadFrameAsync(CancellationToken.None);
        var reads = f.Screens.Source.Reads;
        f.Layouts.Current = new(0, 0, .25, OverlayShape.Rectangle, BorderEnabled: false);
        using var edited = await f.Combined.ReadPreviewFrameAsync();
        Assert.Equal(reads + 1, f.Screens.Source.Reads); Assert.Equal((byte)255, OverlayCompositorTests.Pixel(edited, 2, 2, 2));
    }
    [Fact]
    public async Task CancellationDuringScreenStartupReleasesScreenAndCameraLease()
    {
        await using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        await f.Cameras.SelectCameraAsync("camera"); await f.Cameras.StartPreviewAsync(f.Format);
        f.Screens.Source.BlockStart = true;
        var start = f.Combined.StartPreviewAsync(f.Options, cancellation.Token);
        await f.Screens.Source.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.False(f.Combined.IsPreviewing); Assert.Equal(1, f.Screens.Source.Disposals);
        await f.Cameras.StopPreviewAsync(); Assert.Equal(1, f.Camera.Session.Disposals);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task NativeSourceFailurePropagatesAndCleanupReleasesBothDevices(bool camera)
    {
        await using var f = new Fixture(); await f.StartAsync();
        var lease = await f.Combined.AcquireRecordingSourceAsync(f.Options, CancellationToken.None);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lease.Failed += (_, _) => failed.TrySetResult();
        if (camera) f.Camera.Session.Fail(); else f.Screens.Source.Fail();
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(3)); Assert.False(f.Combined.IsPreviewing);
        await lease.DisposeAsync(); Assert.Equal(1, f.Screens.Source.Disposals); Assert.Equal(1, f.Camera.Session.Disposals);
    }
    [Fact]
    public async Task StartupFailureReleasesAcquiredScreenAndCameraLease()
    {
        await using var f = new Fixture(); f.Screens.Source.StartFailure = true;
        await f.Cameras.SelectCameraAsync("camera"); await f.Cameras.StartPreviewAsync(f.Format);
        await Assert.ThrowsAsync<IOException>(() => f.Combined.StartPreviewAsync(f.Options));
        Assert.Equal(1, f.Screens.Source.Disposals); Assert.False(f.Combined.IsPreviewing);
        await f.Cameras.StopPreviewAsync(); Assert.Equal(1, f.Camera.Session.Disposals);
    }
    [Fact]
    public async Task CompositionCleanupStillReleasesCameraWhenScreenDisposalFails()
    {
        await using var f = new Fixture(); await f.StartAsync(); f.Screens.Source.DisposeFailure = true;
        await Assert.ThrowsAsync<IOException>(f.Combined.StopPreviewAsync);
        await f.Cameras.StopPreviewAsync(); Assert.Equal(1, f.Camera.Session.Disposals);
    }
    [Fact]
    public async Task MismatchedSelectionCannotAcquireRecordingLease()
    {
        await using var f = new Fixture(); await f.StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Combined.AcquireRecordingSourceAsync(f.Options with { Id = "different-monitor" }, CancellationToken.None));
    }
    [Theory]
    [InlineData(30)] [InlineData(60)]
    public void CombinedFrameRateIsScreenCadenceAndAudioModesAreAccepted(int fps)
    {
        var format = new CameraFormat("15fps", 640, 480, 15, 1, "NV12");
        var options = new VideoRecordingConfiguration(new(VideoSourceKind.ScreenAndWebcam, "monitor", CameraFormat: format, CameraId: "camera"), Path.GetTempPath(), fps);
        options.Validate(); Assert.Equal(fps, options.EffectiveFramesPerSecond);
        foreach (var mode in Enum.GetValues<CaptureFlow.Core.Audio.AudioRecordingMode>())
            (options with { Audio = new("mic", Path.GetTempPath(), Mode: mode, OutputDeviceId: "system") }).Validate();
        Assert.StartsWith("CaptureFlow_ScreenWebcam_", VideoRecordingFileName.Create(DateTimeOffset.UtcNow, Guid.NewGuid(), VideoSourceKind.ScreenAndWebcam));
        Assert.Throws<ArgumentException>(() => (options with { Source = options.Source with { CameraId = null } }).Validate());
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public CameraFormat Format { get; } = new("native", 16, 12, 15, 1, "BGRA");
        public CameraBackend Camera { get; } = new();
        public Screens Screens { get; } = new();
        public OverlayLayoutStore Layouts { get; } = new();
        public CameraPreviewService Cameras { get; }
        public CombinedVideoPreviewService Combined { get; }
        public VideoSourceOptions Options => new(VideoSourceKind.ScreenAndWebcam, "monitor", CameraFormat: Format, CameraId: "camera");
        public Fixture() { Cameras = new(Camera); Combined = new(Screens, Cameras, new BgraVideoCompositor(), Layouts); }
        public async Task StartAsync() { await Cameras.SelectCameraAsync("camera"); await Cameras.StartPreviewAsync(Format); await Combined.StartPreviewAsync(Options); }
        public async ValueTask DisposeAsync() { try { await Combined.DisposeAsync(); } finally { await Cameras.DisposeAsync(); } }
    }
    private sealed class Screens : IMonitorCaptureBackend
    {
        public Source Source { get; } = new(); public int Created { get; private set; } public VideoSourceOptions? LastOptions { get; private set; }
        public Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MonitorDescriptor>>([]);
        public Task<IVideoFrameSource> CreateAsync(VideoSourceOptions options, CancellationToken cancellationToken) { Created++; LastOptions = options; return Task.FromResult<IVideoFrameSource>(Source); }
    }
    private sealed class Source : IVideoFrameSource
    {
        public event EventHandler<Exception>? Failed;
        public VideoSize Size => new(80, 60); public int Reads { get; private set; } public int Disposals { get; private set; }
        public List<bool> MouseActivity { get; } = [];
        public void SetRecordingActivity(bool active) => MouseActivity.Add(active);
        public bool StartFailure { get; set; } public bool DisposeFailure { get; set; }
        public bool BlockStart { get; set; }
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Fail() => Failed?.Invoke(this, new IOException("monitor disconnected"));
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            StartEntered.TrySetResult(); if (StartFailure) throw new IOException("screen start failed");
            if (BlockStart) await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        public ValueTask<VideoFrame> ReadFrameAsync(CancellationToken cancellationToken) { Reads++; return ValueTask.FromResult(OverlayCompositorTests.Frame(80, 60, 20, 20, 20)); }
        public ValueTask DisposeAsync() { Disposals++; if (DisposeFailure) throw new IOException("screen cleanup failed"); return ValueTask.CompletedTask; }
    }
    private sealed class CameraBackend : ICameraCaptureBackend
    {
        public CameraSession Session { get; } = new(); public int Opens { get; private set; }
        public Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CameraDevice>>([new("camera", "Fixture")]);
        public Task<ICameraCaptureSession> OpenAsync(string deviceId, CancellationToken cancellationToken) { Opens++; return Task.FromResult<ICameraCaptureSession>(Session); }
    }
    private sealed class CameraSession : ICameraCaptureSession
    {
        private int reads;
        public event EventHandler<Exception>? Failed;
        public IReadOnlyList<CameraFormat> SupportedFormats => [new("native", 16, 12, 15, 1, "BGRA")];
        public VideoSize Size => new(16, 12); public int Disposals { get; private set; }
        public bool BlockAfterFirstFrame { get; set; }
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ConfigureAsync(CameraFormat format, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Fail() => Failed?.Invoke(this, new IOException("camera disconnected"));
        public async ValueTask<VideoFrame> ReadFrameAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref reads) > 1 && BlockAfterFirstFrame) { Blocked.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
            return OverlayCompositorTests.Frame(16, 12, 0, 0, 255);
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
