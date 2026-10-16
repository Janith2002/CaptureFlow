using System.Buffers;
using CaptureFlow.Application.Video;
using CaptureFlow.Core.Audio;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Tests;
public class ScreenRecordingServiceTests
{
    [Theory] [InlineData(VideoSourceKind.Monitor)] [InlineData(VideoSourceKind.Webcam)] [InlineData(VideoSourceKind.ScreenAndWebcam)]
    public async Task ProtectedVideoCancellationAndShutdownRetainJournalAndReleaseResources(VideoSourceKind kind)
    {
        var root = Path.Combine(Path.GetTempPath(), "CaptureFlow-protected-video-" + Guid.NewGuid().ToString("N"));
        var journal = new CaptureFlow.Infrastructure.Recovery.JsonRecoveryJournal(root);
        try
        {
            await using var fixture = new Fixture(journal); var format = new CameraFormat("fixture", 2, 2, 30, 1, "BGRA");
            var options = fixture.Options with { Source = new(kind, "test-monitor", CameraFormat: kind == VideoSourceKind.Monitor ? null : format, CameraId: kind == VideoSourceKind.ScreenAndWebcam ? "fixture" : null) };
            await fixture.Service.StartAsync(options); await fixture.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await fixture.Service.PauseAsync(); Assert.Equal("Paused", Assert.Single(await journal.ScanAsync()).Record.State);
            await fixture.Service.ResumeAsync(); await fixture.Service.DisposeAsync();
            var candidate = Assert.Single(await journal.ScanAsync()); Assert.Equal("Cancelled", candidate.Record.State); Assert.False(candidate.IsLive);
            Assert.Equal(kind.ToString(), candidate.Record.Mode); Assert.NotEmpty(candidate.Files);
            Assert.Equal(1, fixture.Source.Disposals); Assert.Equal(1, fixture.Encoder.Disposals);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory] [InlineData(VideoSourceKind.Monitor)] [InlineData(VideoSourceKind.Webcam)] [InlineData(VideoSourceKind.ScreenAndWebcam)]
    public async Task RepeatedPauseResumeStopRequestsPreserveOneRecordingAndReleaseResources(VideoSourceKind kind)
    {
        await using var f = new Fixture(); var format = new CameraFormat("fixture", 2, 2, 30, 1, "BGRA");
        var options = f.Options with { Source = new(kind, "test-monitor", CameraFormat: kind == VideoSourceKind.Monitor ? null : format, CameraId: kind == VideoSourceKind.ScreenAndWebcam ? "fixture" : null) };
        await f.Service.StartAsync(options); await f.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartAsync(options));
        await f.Service.PauseAsync(); await f.Service.PauseAsync(); Assert.Equal(VideoRecordingState.Paused, f.Service.State);
        await f.Service.ResumeAsync(); await f.Service.ResumeAsync(); await f.Service.StopAsync(); await f.Service.StopAsync();
        Assert.Equal(VideoRecordingState.Completed, f.Service.State); Assert.Equal(1, f.Sources.Created); Assert.Equal(1, f.Source.Disposals); Assert.Equal(1, f.Encoder.Disposals);
    }
    [Fact] public async Task DelayedFailureFromDisposedSourceCannotCancelItsSuccessor()
    {
        await using var f = new Fixture(); await f.Service.StartAsync(f.Options); var stale = f.Source.FailureNotification(); await f.Service.CancelAsync();
        f.Sources.Next = new Source(); await f.Service.StartAsync(f.Options); stale(); Assert.Null(f.Service.ErrorMessage); await f.Service.CancelAsync();
        Assert.Equal(VideoRecordingState.Cancelled, f.Service.State); Assert.DoesNotContain("stale", f.Service.ErrorMessage ?? ""); Assert.Equal(1, f.Sources.Next.Disposals);
    }
    [Fact] public async Task MouseActivityFollowsTheExistingRecordingSessionAndPauseResumeBoundaries()
    {
        await using var f = new Fixture(); await f.Service.StartAsync(f.Options);
        Assert.Equal(new[] { false, true }, f.Source.MouseActivity);
        await f.Service.PauseAsync(); Assert.False(f.Source.MouseActivity.Last());
        await f.Service.ResumeAsync(); Assert.True(f.Source.MouseActivity.Last());
        await f.Service.CancelAsync(); Assert.Equal(1, f.Source.Disposals); Assert.Equal(VideoRecordingState.Cancelled, f.Service.State);
    }
    [Theory]
    [InlineData(VideoQualityPreset.Low)] [InlineData(VideoQualityPreset.Standard)] [InlineData(VideoQualityPreset.High)] [InlineData(VideoQualityPreset.Ultra)] [InlineData(VideoQualityPreset.Custom)]
    public async Task QualityPresetsPreservePauseResumeAndReleaseHeldFrames(VideoQualityPreset preset)
    {
        await using var f = new Fixture();
        await f.Service.StartAsync(f.Options with { FramesPerSecond = 24, QualityPreferences = new(Preset: preset, FramesPerSecond: 24) });
        await f.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await f.Service.PauseAsync(); var paused = f.Service.Duration; await Task.Delay(30); Assert.Equal(paused, f.Service.Duration);
        await f.Service.ResumeAsync(); await f.Service.StopAsync();
        Assert.Equal(VideoRecordingState.Completed, f.Service.State); Assert.Equal(0, f.Source.ActiveOwners); Assert.InRange(f.Source.MaximumOwners, 1, 2);
    }
    [Fact] public async Task SlowEncoderRepeatsPreviouslyCapturedPixelsAndKeepsMemoryBounded()
    {
        await using var f = new Fixture(); f.Encoder.DelayFirstWrite = true;
        await f.Service.StartAsync(f.Options); await f.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await f.Service.StopAsync();
        Assert.Equal(VideoRecordingState.Completed, f.Service.State);
        Assert.True(f.Source.Reads < f.Encoder.FrameCount); Assert.Equal(f.Encoder.Pixels[0], f.Encoder.Pixels[1]);
        Assert.Equal(0, f.Source.ActiveOwners); Assert.InRange(f.Source.MaximumOwners, 1, 2);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task CombinedCancellationAndMuxFailureRetainIntermediatesAndCleanup(bool cancel)
    {
        await using var f = new Fixture(); if (!cancel) f.Encoding.Failure = "mux";
        var options = f.Options with { Source = new(VideoSourceKind.ScreenAndWebcam, "monitor", CameraFormat: new("15fps", 2, 2, 15, 1, "BGRA"), CameraId: "camera") };
        await f.Service.StartAsync(options); await f.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (cancel) await f.Service.CancelAsync(); else await f.Service.StopAsync();
        Assert.Equal(cancel ? VideoRecordingState.Cancelled : VideoRecordingState.Error, f.Service.State);
        Assert.Null(f.Service.SavedFilePath); Assert.True(File.Exists(Path.Combine(f.Service.WorkDirectory!, "screen.mp4")));
        Assert.Equal(1, f.Source.Disposals); Assert.Equal(1, f.Encoder.Disposals);
    }
    [Theory]
    [InlineData(null)] [InlineData(AudioRecordingMode.MicrophoneOnly)] [InlineData(AudioRecordingMode.SystemAudioOnly)] [InlineData(AudioRecordingMode.MicrophoneAndSystemAudio)]
    public async Task CombinedModeUsesExistingLifecycleAndAllAudioChoices(AudioRecordingMode? mode)
    {
        await using var f = new Fixture();
        var options = f.Options with
        {
            Source = new(VideoSourceKind.ScreenAndWebcam, "monitor", CameraFormat: new("15fps", 2, 2, 15, 1, "BGRA"), CameraId: "camera"),
            Audio = mode is { } audioMode ? new("microphone", f.Directory, Mode: audioMode, OutputDeviceId: "system") : null
        };
        await f.Service.StartAsync(options); await f.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await f.Service.PauseAsync(); Assert.Equal(VideoRecordingState.Paused, f.Service.State);
        var paused = f.Service.Duration; await Task.Delay(20); Assert.Equal(paused, f.Service.Duration);
        if (mode is not null) Assert.NotNull(f.Audio.Options?.SharedTimeline);
        await f.Service.ResumeAsync(); await f.Service.StopAsync();
        Assert.Equal(VideoRecordingState.Completed, f.Service.State); Assert.StartsWith("CaptureFlow_ScreenWebcam_", Path.GetFileName(f.Service.SavedFilePath));
        Assert.Equal(mode is not null, f.Encoding.AudioPath is not null); Assert.Equal(1, f.Source.Disposals); Assert.Equal(1, f.Encoder.Disposals);
    }
    [Theory]
    [InlineData(false, 30)] [InlineData(true, 30)] [InlineData(true, 60)] [InlineData(false, 120)]
    public async Task WebcamReusesRecordingStatesAndOptionalMicrophonePipeline(bool microphone, int fps)
    {
        await using var f = new Fixture();
        var format = new CameraFormat("fixture", 2, 2, (uint)fps, 1, "BGRA");
        var options = f.Options with
        {
            Source = new(VideoSourceKind.Webcam, "test-camera", CameraFormat: format),
            Audio = microphone ? new AudioRecordingOptions("microphone", f.Directory) : null
        };
        await f.Service.StartAsync(options); await f.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(VideoRecordingState.Recording, f.Service.State);
        await f.Service.PauseAsync(); Assert.Equal(VideoRecordingState.Paused, f.Service.State);
        await f.Service.ResumeAsync(); await f.Service.StopAsync();
        Assert.Equal(VideoRecordingState.Completed, f.Service.State);
        Assert.StartsWith("CaptureFlow_Webcam_", Path.GetFileName(f.Service.SavedFilePath));
        Assert.Equal(microphone, f.Encoding.AudioPath is not null);
        Assert.Equal(1, f.Source.Disposals); Assert.Equal(1, f.Encoder.Disposals);
    }
    [Fact]
    public async Task StartPauseResumeStopPublishesOnlyValidatedOutputAndReleasesResources()
    {
        await using var f = new Fixture();
        var states = new List<VideoRecordingState>();
        f.Service.StateChanged += (_, _) => states.Add(f.Service.State);
        await f.Service.StartAsync(f.Options); await f.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await f.Service.PauseAsync(); var frozen = f.Service.Duration;
        await Task.Delay(30); Assert.Equal(frozen, f.Service.Duration);
        await f.Service.ResumeAsync(); await f.Service.StopAsync();
        Assert.Equal(new[] { VideoRecordingState.Starting, VideoRecordingState.Recording, VideoRecordingState.Paused,
            VideoRecordingState.Recording, VideoRecordingState.Saving, VideoRecordingState.Completed }, states);
        Assert.True(File.Exists(f.Service.SavedFilePath)); Assert.True(f.Encoding.Validated);
        Assert.Equal(1, f.Source.Disposals); Assert.Equal(1, f.Encoder.Disposals);
    }
    [Fact]
    public async Task StopWhilePausedIsSupported()
    {
        await using var f = new Fixture(); await f.Service.StartAsync(f.Options);
        await f.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await f.Service.PauseAsync(); await f.Service.StopAsync();
        Assert.Equal(VideoRecordingState.Completed, f.Service.State);
    }
    [Fact]
    public async Task DuplicateStartAndCommandsBeforeStartDoNotReplaceSession()
    {
        await using var f = new Fixture(); await f.Service.PauseAsync(); await f.Service.ResumeAsync(); await f.Service.StopAsync(); await f.Service.CancelAsync();
        Assert.Equal(VideoRecordingState.Idle, f.Service.State);
        await f.Service.StartAsync(f.Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartAsync(f.Options));
        Assert.Equal(1, f.Sources.Created); Assert.Equal(VideoRecordingState.Recording, f.Service.State);
    }
    [Theory]
    [InlineData("availability")] [InlineData("capture")] [InlineData("encoder")] [InlineData("storage")]
    public async Task StartupFailuresNeverPublishAndDisposeAcquiredResources(string failure)
    {
        await using var f = new Fixture();
        f.Encoding.Failure = failure; f.Source.StartFailure = failure == "capture";
        await f.Service.StartAsync(f.Options);
        Assert.Equal(VideoRecordingState.Error, f.Service.State); Assert.Null(f.Service.SavedFilePath);
        Assert.NotNull(f.Service.ErrorMessage);
        Assert.Equal(f.Sources.Created, f.Source.Disposals);
    }
    [Theory]
    [InlineData("mux")] [InlineData("validation")] [InlineData("finish")]
    public async Task ExportFailureRetainsIntermediatesAndDoesNotPublish(string failure)
    {
        await using var f = new Fixture(); f.Encoding.Failure = failure; f.Encoder.FinishFailure = failure == "finish";
        await f.Service.StartAsync(f.Options); await f.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await f.Service.StopAsync();
        Assert.Equal(VideoRecordingState.Error, f.Service.State); Assert.Null(f.Service.SavedFilePath);
        Assert.True(File.Exists(Path.Combine(f.Service.WorkDirectory!, "screen.mp4")));
        Assert.Empty(Directory.GetFiles(f.Directory, "*.mp4"));
        Assert.Equal(1, f.Source.Disposals); Assert.Equal(1, f.Encoder.Disposals);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CaptureOrEncoderRuntimeFailureCleansUp(bool encoder)
    {
        await using var f = new Fixture();
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Service.StateChanged += (_, _) => { if (f.Service.State == VideoRecordingState.Error) failed.TrySetResult(); };
        f.Encoder.WriteFailure = encoder;
        await f.Service.StartAsync(f.Options);
        if (!encoder) f.Source.Fail();
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(f.Service.SavedFilePath); Assert.Equal(1, f.Source.Disposals); Assert.Equal(1, f.Encoder.Disposals);
    }
    [Fact]
    public async Task CancelActiveRecordingRetainsIntermediatesAndNeverPublishes()
    {
        await using var f = new Fixture(); await f.Service.StartAsync(f.Options);
        await f.Service.CancelAsync();
        Assert.Equal(VideoRecordingState.Cancelled, f.Service.State); Assert.Null(f.Service.SavedFilePath);
        Assert.True(Directory.Exists(f.Service.WorkDirectory)); Assert.Equal(1, f.Source.Disposals); Assert.Equal(1, f.Encoder.Disposals);
    }
    [Fact]
    public async Task CancelInterruptsBlockedStartup()
    {
        await using var f = new Fixture(); f.Encoding.BlockAvailability = true;
        var start = f.Service.StartAsync(f.Options);
        await f.Encoding.AvailabilityEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await f.Service.CancelAsync().WaitAsync(TimeSpan.FromSeconds(3)); await start;
        Assert.Equal(VideoRecordingState.Cancelled, f.Service.State); Assert.Equal(0, f.Sources.Created);
    }
    [Fact]
    public async Task CancelInterruptsMuxingAndLeavesOriginalVideo()
    {
        await using var f = new Fixture(); f.Encoding.BlockMux = true;
        await f.Service.StartAsync(f.Options); await f.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stop = f.Service.StopAsync();
        await f.Encoding.MuxEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await f.Service.CancelAsync().WaitAsync(TimeSpan.FromSeconds(3)); await stop;
        Assert.Equal(VideoRecordingState.Cancelled, f.Service.State); Assert.Null(f.Service.SavedFilePath);
        Assert.True(File.Exists(Path.Combine(f.Service.WorkDirectory!, "screen.mp4")));
        Assert.Equal(1, f.Encoder.Disposals);
    }
    [Theory]
    [InlineData(AudioRecordingMode.MicrophoneOnly)] [InlineData(AudioRecordingMode.SystemAudioOnly)] [InlineData(AudioRecordingMode.MicrophoneAndSystemAudio)]
    public async Task OptionalAudioUsesSharedTimelineAndExistingBackend(AudioRecordingMode mode)
    {
        await using var f = new Fixture();
        await f.Service.StartAsync(f.Options with { Audio = new("microphone", f.Directory, Mode: mode, OutputDeviceId: "output") });
        Assert.NotNull(f.Audio.Options?.SharedTimeline); Assert.Equal(mode, f.Audio.Options?.Mode);
        Assert.Equal(f.Service.WorkDirectory, f.Audio.Options?.OutputDirectory);
        f.Service.SetAudioVolume(.4f, true, .6f, false);
        Assert.Equal(.4f, f.Audio.Session.Microphone); Assert.True(f.Audio.Session.Muted); Assert.Equal(.6f, f.Audio.Session.System);
        await f.Service.PauseAsync(); Assert.True(f.Audio.Session.Paused);
        await f.Service.ResumeAsync(); Assert.False(f.Audio.Session.Paused);
        await f.Encoder.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3)); await f.Service.StopAsync();
        Assert.Equal(VideoRecordingState.Completed, f.Service.State);
        Assert.NotNull(f.Encoding.AudioPath); Assert.Equal(1, f.Audio.Session.Disposals);
    }
    [Fact]
    public async Task DisposalIsIdempotentAndPreventsNewStarts()
    {
        await using var f = new Fixture(); await f.Service.StartAsync(f.Options);
        await f.Service.DisposeAsync(); await f.Service.DisposeAsync();
        Assert.Equal(1, f.Source.Disposals); Assert.Equal(1, f.Encoder.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => f.Service.StartAsync(f.Options));
    }
    [Fact]
    public async Task CleanupOfOtherResourcesContinuesIfCaptureDisposalThrows()
    {
        await using var f = new Fixture(); f.Source.DisposeFailure = true;
        await f.Service.StartAsync(f.Options); await f.Service.CancelAsync();
        Assert.Equal(1, f.Source.Disposals); Assert.Equal(1, f.Encoder.Disposals); Assert.Null(f.Service.SavedFilePath);
    }
    [Fact]
    public async Task AudioDisconnectIsReportedAndCleansUpEveryResource()
    {
        await using var f = new Fixture();
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Service.StateChanged += (_, _) => { if (f.Service.State == VideoRecordingState.Error) failed.TrySetResult(); };
        await f.Service.StartAsync(f.Options with { Audio = new("microphone", f.Directory) });
        f.Audio.Session.Fail(); await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(f.Service.SavedFilePath); Assert.Equal(1, f.Audio.Session.Disposals);
        Assert.Equal(1, f.Source.Disposals); Assert.Equal(1, f.Encoder.Disposals);
    }

    // Test doubles establish orchestration behavior; these files are not real MP4s or capture validation.
    private sealed class Fixture : IAsyncDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "CaptureFlow-video-tests", Guid.NewGuid().ToString("N"));
        public Source Source { get; } = new();
        public Encoder Encoder { get; } = new();
        public Sources Sources { get; }
        public Encoding Encoding { get; }
        public Audio Audio { get; } = new();
        public ScreenRecordingService Service { get; }
        public VideoRecordingConfiguration Options => new(new(VideoSourceKind.Monitor, "test-monitor"), Directory);
        public Fixture(CaptureFlow.Core.Recovery.IRecoveryJournal? recovery = null)
        {
            Sources = new(Source); Encoding = new(Encoder);
            Service = new(Sources, Encoding, new SingleSourceVideoCompositor(), Audio, recovery);
        }
        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
    private sealed class Sources(Source source) : IVideoSourceFactory
    {
        public Source? Next;
        public int Created { get; private set; }
        public Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MonitorDescriptor>>([new("test-monitor", "Test", 2, 2, true)]);
        public Task<IVideoFrameSource> CreateAsync(VideoSourceOptions options, CancellationToken cancellationToken) { Created++; return Task.FromResult<IVideoFrameSource>(Next ?? source); }
    }
    private sealed class Source : IVideoFrameSource
    {
        public event EventHandler<Exception>? Failed;
        public VideoSize Size => new(2, 2);
        public bool StartFailure { get; set; }
        public bool DisposeFailure { get; set; }
        public int Disposals { get; private set; }
        public List<bool> MouseActivity { get; } = [];
        public void SetRecordingActivity(bool active) => MouseActivity.Add(active);
        public Task StartAsync(CancellationToken cancellationToken) => StartFailure ? Task.FromException(new IOException("capture failure")) : Task.CompletedTask;
        public int Reads, ActiveOwners, MaximumOwners;
        public ValueTask<VideoFrame> ReadFrameAsync(CancellationToken cancellationToken)
        {
            Reads++; ActiveOwners++; MaximumOwners = Math.Max(MaximumOwners, ActiveOwners);
            return ValueTask.FromResult(new VideoFrame(new Pixels(this, (byte)Reads), Size, TimeSpan.Zero));
        }
        private sealed class Pixels(Source source, byte value) : IMemoryOwner<byte>
        {
            public Memory<byte> Memory { get; } = Enumerable.Repeat(value, 16).ToArray();
            public void Dispose() => source.ActiveOwners--;
        }
        public void Fail() => Failed?.Invoke(this, new IOException("display disconnected"));
        public Action FailureNotification() { var handler = Failed; return () => handler?.Invoke(this, new IOException("stale capture failure")); }
        public ValueTask DisposeAsync() { Disposals++; if (DisposeFailure) throw new IOException("capture cleanup failure"); return ValueTask.CompletedTask; }
    }
    private sealed class Encoder : IVideoEncoder
    {
        private long count;
        public long FrameCount => Interlocked.Read(ref count);
        public bool WriteFailure { get; set; }
        public bool FinishFailure { get; set; }
        public int Disposals { get; private set; }
        public TaskCompletionSource FirstFrame { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool DelayFirstWrite;
        public List<byte> Pixels = [];
        public async Task WriteFrameAsync(VideoFrame frame, CancellationToken cancellationToken)
        {
            if (WriteFailure) throw new IOException("FFmpeg failed");
            if (DelayFirstWrite && count == 0) await Task.Delay(150, cancellationToken);
            Pixels.Add(frame.Pixels.Span[0]); Interlocked.Increment(ref count); FirstFrame.TrySetResult();
        }
        public Task FinishAsync(CancellationToken cancellationToken) => FinishFailure ? Task.FromException(new IOException("FFmpeg finalization failed")) : Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class Encoding(Encoder encoder) : IVideoEncodingBackend
    {
        public string? Failure { get; set; }
        public bool BlockAvailability { get; set; }
        public bool BlockMux { get; set; }
        public bool Validated { get; private set; }
        public string? AudioPath { get; private set; }
        public TaskCompletionSource AvailabilityEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MuxEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task EnsureAvailableAsync(string executable, CancellationToken cancellationToken)
        {
            AvailabilityEntered.TrySetResult();
            if (BlockAvailability) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Failure == "availability") throw new IOException("FFmpeg missing");
        }
        public Task<IVideoEncoder> CreateAsync(VideoRecordingConfiguration configuration, VideoSize size, string path, CancellationToken cancellationToken)
        {
            if (Failure == "encoder") throw new IOException("encoder startup failure");
            File.WriteAllBytes(path, new byte[128]); return Task.FromResult<IVideoEncoder>(encoder);
        }
        public async Task MuxAsync(VideoRecordingConfiguration configuration, string videoPath, string? audioPath, string finalPath, TimeSpan duration, CancellationToken cancellationToken)
        {
            AudioPath = audioPath; MuxEntered.TrySetResult();
            if (BlockMux) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Failure == "mux") throw new IOException("mux failed");
            File.Copy(videoPath, finalPath);
        }
        public Task ValidateAsync(string path, string executable, CancellationToken cancellationToken)
        {
            if (Failure == "validation") throw new IOException("invalid MP4");
            Validated = true; return Task.CompletedTask;
        }
        public void CheckStorage(string path) { if (Failure == "storage") throw new IOException("disk full"); }
    }
    private sealed class Audio : IAudioCaptureBackend
    {
        public AudioRecordingOptions? Options { get; private set; }
        public AudioSession Session { get; } = new();
        public Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync() => Task.FromResult<IReadOnlyList<MicrophoneDevice>>([]);
        public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync() => Task.FromResult<IReadOnlyList<AudioOutputDevice>>([]);
        public Task<IAudioCaptureSession> CreateSessionAsync(AudioRecordingOptions options)
        { Options = options; Session.Directory = options.OutputDirectory; return Task.FromResult<IAudioCaptureSession>(Session); }
    }
    private sealed class AudioSession : IAudioCaptureSession
    {
        public event EventHandler? Ended;
        public void Fail() => Ended?.Invoke(this, EventArgs.Empty);
        public TimeSpan Duration => TimeSpan.Zero;
        public string Directory { get; set; } = "";
        public int Disposals { get; private set; }
        public bool Paused { get; private set; }
        public float Microphone { get; private set; }
        public float System { get; private set; }
        public bool Muted { get; private set; }
        public Task StartAsync() => Task.CompletedTask;
        public void Pause() => Paused = true;
        public void Resume() => Paused = false;
        public void SetVolume(float gain, bool muted) { Microphone = gain; Muted = muted; }
        public void SetSystemVolume(float gain, bool muted) => System = gain;
        public Task<AudioRecordingResult> StopAsync()
        {
            var path = Path.Combine(Directory, "test.wav"); File.WriteAllBytes(path, new byte[128]);
            return Task.FromResult(new AudioRecordingResult(path, TimeSpan.Zero, 128));
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
