using CaptureFlow.Application.Audio;
using CaptureFlow.Core.Audio;
namespace CaptureFlow.Tests;
public class AudioRecordingServiceTests
{
    [Theory]
    [InlineData(AudioRecordingMode.MicrophoneOnly)] [InlineData(AudioRecordingMode.SystemAudioOnly)] [InlineData(AudioRecordingMode.MicrophoneAndSystemAudio)]
    public async Task ProtectedAudioPreservesIntermediateAndCompletesUniqueWav(AudioRecordingMode mode)
    {
        using var scratch = new RecoveryTests.Scratch(); using var file = new SavedWavFixture();
        var journal = new CaptureFlow.Infrastructure.Recovery.JsonRecoveryJournal(Path.Combine(scratch.Path, "recovery"));
        var session = new TestSession { Result = file.Result };
        await using var service = new AudioRecordingService(new TestBackend(session), recovery: journal);
        await service.StartAsync(new("fake", scratch.Path, Mode: mode, OutputDeviceId: "fake-output"));
        Assert.NotNull(service.WorkDirectory); Assert.True(Assert.Single(await journal.ScanAsync()).IsLive);
        await service.PauseAsync(); Assert.Equal("Paused", Assert.Single(await journal.ScanAsync()).Record.State);
        await service.ResumeAsync(); await service.StopAsync();
        Assert.Equal(AudioRecordingState.Completed, service.State); Assert.Empty(await journal.ScanAsync());
        Assert.True(File.Exists(file.Path)); Assert.True(File.Exists(service.SavedFilePath)); Assert.NotEqual(file.Path, service.SavedFilePath); Assert.Equal(1, session.DisposeCount);
    }
    [Fact] public async Task ProtectedAudioStopFailureRetainsWorkAndReportsError()
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new CaptureFlow.Infrastructure.Recovery.JsonRecoveryJournal(Path.Combine(scratch.Path, "recovery"));
        var session = new TestSession { StopError = new IOException("disk full") };
        await using var service = new AudioRecordingService(new TestBackend(session), recovery: journal);
        await service.StartAsync(new("fake", scratch.Path));
        var retained = Path.Combine(service.WorkDirectory!, "capture.wav.partial"); await File.WriteAllTextAsync(retained, "retained test samples");
        await service.StopAsync(); Assert.Equal(AudioRecordingState.Error, service.State); Assert.Equal("Error", Assert.Single(await journal.ScanAsync()).Record.State);
        Assert.Equal("retained test samples", await File.ReadAllTextAsync(retained)); Assert.Equal(1, session.DisposeCount);
    }
    private static readonly AudioRecordingOptions Options = new("real-device-id-in-test-double", Path.GetTempPath());
    [Theory]
    [InlineData(AudioRecordingMode.MicrophoneOnly)] [InlineData(AudioRecordingMode.SystemAudioOnly)] [InlineData(AudioRecordingMode.MicrophoneAndSystemAudio)]
    public async Task StartPauseResumeStopProducesExpectedStatesAndReleasesSession(AudioRecordingMode mode)
    {
        using var file = new SavedWavFixture();
        var session = new TestSession { Result = file.Result };
        await using var service = new AudioRecordingService(new TestBackend(session));
        var states = new List<AudioRecordingState>();
        service.StateChanged += (_, _) => states.Add(service.State);
        Assert.Equal(AudioRecordingState.Idle, service.State);
        await service.StartAsync(Options with { Mode = mode, OutputDeviceId = "output-device" }); await service.PauseAsync(); await service.ResumeAsync(); await service.StopAsync();
        Assert.Equal(new[] { AudioRecordingState.Recording, AudioRecordingState.Paused, AudioRecordingState.Recording, AudioRecordingState.Saving, AudioRecordingState.Completed }, states);
        Assert.Equal(file.Path, service.SavedFilePath);
        Assert.Equal(1, session.DisposeCount);
        Assert.Equal(TimeSpan.FromSeconds(1), service.Duration);
    }
    [Fact]
    public async Task PauseFreezesCapturedDurationAndStopWorksWhilePaused()
    {
        using var file = new SavedWavFixture();
        var session = new TestSession { Result = file.Result };
        await using var service = new AudioRecordingService(new TestBackend(session));
        await service.StartAsync(Options);
        session.AddCapturedDuration(TimeSpan.FromSeconds(1));
        await service.PauseAsync();
        session.AddCapturedDuration(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(1), service.Duration);
        await service.StopAsync();
        Assert.Equal(AudioRecordingState.Completed, service.State);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartFailureNeverClaimsSuccessAndReleasesAnyCreatedSession(bool factoryFails)
    {
        var session = new TestSession { StartError = new UnauthorizedAccessException("microphone denied") };
        var backend = new TestBackend(session) { CreateError = factoryFails ? new IOException("device missing") : null };
        await using var service = new AudioRecordingService(backend);
        await service.StartAsync(Options);
        Assert.Equal(AudioRecordingState.Error, service.State);
        Assert.Null(service.SavedFilePath);
        Assert.NotNull(service.ErrorMessage);
        Assert.Equal(factoryFails ? 0 : 1, session.DisposeCount);
    }
    [Fact]
    public async Task StopFailureTransitionsThroughSavingToError()
    {
        var session = new TestSession { StopError = new IOException("disk full") };
        await using var service = new AudioRecordingService(new TestBackend(session));
        var states = new List<AudioRecordingState>();
        service.StateChanged += (_, _) => states.Add(service.State);
        await service.StartAsync(Options); await service.StopAsync();
        Assert.Equal(new[] { AudioRecordingState.Recording, AudioRecordingState.Saving, AudioRecordingState.Error }, states);
        Assert.Null(service.SavedFilePath); Assert.Contains("disk full", service.ErrorMessage);
        Assert.Equal(1, session.DisposeCount);
    }
    [Theory]
    [InlineData(0, true)]
    [InlineData(16000, false)]
    public async Task EmptyOrMissingFileNeverCompletes(long bytes, bool fileExists)
    {
        using var file = new SavedWavFixture();
        var session = new TestSession { Result = new(fileExists ? file.Path : file.Path + ".missing", TimeSpan.FromSeconds(1), bytes) };
        await using var service = new AudioRecordingService(new TestBackend(session));
        await service.StartAsync(Options); await service.StopAsync();
        Assert.Equal(AudioRecordingState.Error, service.State); Assert.Null(service.SavedFilePath);
    }
    [Theory]
    [InlineData(AudioRecordingMode.MicrophoneOnly)] [InlineData(AudioRecordingMode.SystemAudioOnly)] [InlineData(AudioRecordingMode.MicrophoneAndSystemAudio)]
    public async Task UnexpectedDeviceDisconnectBecomesErrorAndReleasesSession(AudioRecordingMode mode)
    {
        var session = new TestSession { StopError = new IOException("microphone disconnected") };
        await using var service = new AudioRecordingService(new TestBackend(session));
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StateChanged += (_, _) => { if (service.State == AudioRecordingState.Error) failed.TrySetResult(); };
        await service.StartAsync(Options with { Mode = mode, OutputDeviceId = "output-device" }); await service.PauseAsync();
        session.EndUnexpectedly(); await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("disconnected", service.ErrorMessage); Assert.Null(service.SavedFilePath); Assert.Equal(1, session.DisposeCount);
    }
    [Fact]
    public async Task DuplicateStartDoesNotReplaceActiveRecording()
    {
        var backend = new TestBackend(new TestSession());
        await using var service = new AudioRecordingService(backend);
        await service.StartAsync(Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(Options));
        Assert.Equal(AudioRecordingState.Recording, service.State); Assert.Equal(1, backend.Created);
    }
    [Fact]
    public async Task PauseResumeAndStopBeforeStartAreHarmless()
    {
        await using var service = new AudioRecordingService(new TestBackend(new TestSession()));
        await service.PauseAsync(); await service.ResumeAsync(); await service.StopAsync();
        Assert.Equal(AudioRecordingState.Idle, service.State);
    }
    [Fact]
    public async Task SavingRemainsVisibleUntilFileFinalizationFinishes()
    {
        using var file = new SavedWavFixture();
        var finished = new TaskCompletionSource<AudioRecordingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new TestSession { StopTask = finished.Task };
        await using var service = new AudioRecordingService(new TestBackend(session));
        await service.StartAsync(Options);
        var stop = service.StopAsync();
        Assert.Equal(AudioRecordingState.Saving, service.State); Assert.Null(service.SavedFilePath);
        finished.SetResult(file.Result); await stop;
        Assert.Equal(AudioRecordingState.Completed, service.State);
    }
    [Fact]
    public async Task NewRecordingClearsPreviousSavedPathAndError()
    {
        using var file = new SavedWavFixture();
        var session = new TestSession { Result = file.Result };
        await using var service = new AudioRecordingService(new TestBackend(session));
        await service.StartAsync(Options); await service.StopAsync();
        session.StartError = new IOException("disconnected");
        await service.StartAsync(Options);
        Assert.Equal(AudioRecordingState.Error, service.State); Assert.Null(service.SavedFilePath);
    }
    [Fact]
    public async Task ClosingAnActiveRecordingFinalizesAndDisposesIt()
    {
        using var file = new SavedWavFixture();
        var session = new TestSession { Result = file.Result };
        var service = new AudioRecordingService(new TestBackend(session));
        await service.StartAsync(Options); await service.DisposeAsync(); await service.DisposeAsync();
        Assert.Equal(AudioRecordingState.Completed, service.State); Assert.Equal(1, session.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(Options));
    }
    [Fact]
    public async Task VolumeAndMuteAreForwardedToTheActiveSession()
    {
        var session = new TestSession();
        await using var service = new AudioRecordingService(new TestBackend(session));
        await service.StartAsync(Options); service.SetVolume(2, true);
        Assert.Equal(1f, session.Gain); Assert.True(session.Muted);
    }
    [Theory]
    [InlineData(AudioRecordingMode.SystemAudioOnly, "", "")]
    [InlineData(AudioRecordingMode.MicrophoneAndSystemAudio, "", "output")]
    [InlineData(AudioRecordingMode.MicrophoneAndSystemAudio, "microphone", "")]
    public async Task MissingRequiredDevicesFailBeforeCreatingAnyCapture(AudioRecordingMode mode, string microphone, string output)
    {
        var backend = new TestBackend(new TestSession());
        await using var service = new AudioRecordingService(backend);
        await service.StartAsync(Options with { Mode = mode, DeviceId = microphone, OutputDeviceId = output });
        Assert.Equal(AudioRecordingState.Error, service.State); Assert.Equal(0, backend.Created); Assert.Null(service.SavedFilePath);
    }
    [Fact]
    public async Task SystemOnlyDoesNotRequireAMicrophone()
    {
        var backend = new TestBackend(new TestSession());
        await using var service = new AudioRecordingService(backend);
        await service.StartAsync(Options with { Mode = AudioRecordingMode.SystemAudioOnly, DeviceId = "", OutputDeviceId = "output" });
        Assert.Equal(AudioRecordingState.Recording, service.State); Assert.Equal(1, backend.Created);
    }
    [Fact]
    public async Task SystemVolumeAndMuteAreForwardedToTheActiveSession()
    {
        var session = new TestSession();
        await using var service = new AudioRecordingService(new TestBackend(session));
        await service.StartAsync(Options with { Mode = AudioRecordingMode.SystemAudioOnly, DeviceId = "", OutputDeviceId = "output" });
        service.SetSystemVolume(.3f, true);
        Assert.Equal(.3f, session.SystemGain); Assert.True(session.SystemMuted);
        Assert.Equal(0f, session.Gain); Assert.False(session.Muted);
    }
    [Fact]
    public async Task RepeatedCyclesUseFreshSessionsAndReleaseEachExactlyOnce()
    {
        var backend = new TestBackend(new TestSession());
        await using var service = new AudioRecordingService(backend);
        var names = new HashSet<string>();
        for (var cycle = 0; cycle < 10; cycle++)
        {
            using var file = new SavedWavFixture();
            var session = new TestSession { Result = file.Result };
            backend.Session = session;
            await service.StartAsync(Options);
            Assert.Null(service.SavedFilePath);
            await service.PauseAsync(); await service.ResumeAsync(); await service.StopAsync(); await service.StopAsync();
            Assert.Equal(AudioRecordingState.Completed, service.State);
            Assert.True(names.Add(service.SavedFilePath!));
            Assert.Equal(1, session.DisposeCount);
            Assert.Equal(TimeSpan.FromSeconds(1), service.Duration);
        }
        Assert.Equal(10, backend.Created);
    }
    private sealed class TestBackend(TestSession session) : IAudioCaptureBackend
    {
        public TestSession Session { get; set; } = session;
        public Exception? CreateError { get; init; }
        public int Created { get; private set; }
        public Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync() => Task.FromResult<IReadOnlyList<MicrophoneDevice>>([]);
        public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync() => Task.FromResult<IReadOnlyList<AudioOutputDevice>>([]);
        public Task<IAudioCaptureSession> CreateSessionAsync(AudioRecordingOptions options)
        {
            if (CreateError is not null) throw CreateError;
            Created++; return Task.FromResult<IAudioCaptureSession>(Session);
        }
    }
    private sealed class TestSession : IAudioCaptureSession
    {
        public event EventHandler? Ended;
        public TimeSpan Duration { get; private set; }
        public Exception? StartError { get; set; }
        public Exception? StopError { get; init; }
        public AudioRecordingResult Result { get; init; } = new("missing.wav", TimeSpan.Zero, 0);
        public Task<AudioRecordingResult>? StopTask { get; init; }
        public int DisposeCount { get; private set; }
        public bool Paused { get; private set; }
        public float Gain { get; private set; }
        public bool Muted { get; private set; }
        public Task StartAsync() => StartError is null ? Task.CompletedTask : Task.FromException(StartError);
        public void Pause() => Paused = true;
        public void Resume() => Paused = false;
        public void SetVolume(float gain, bool muted) { Gain = gain; Muted = muted; }
        public float SystemGain { get; private set; }
        public bool SystemMuted { get; private set; }
        public void SetSystemVolume(float gain, bool muted) { SystemGain = gain; SystemMuted = muted; }
        public void AddCapturedDuration(TimeSpan duration) { if (!Paused) Duration += duration; }
        public Task<AudioRecordingResult> StopAsync() => StopError is not null ? Task.FromException<AudioRecordingResult>(StopError) : StopTask ?? Task.FromResult(Result);
        public void EndUnexpectedly() => Ended?.Invoke(this, EventArgs.Empty);
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
    private sealed class SavedWavFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".wav");
        public AudioRecordingResult Result => new(Path, TimeSpan.FromSeconds(1), 16000);
        public SavedWavFixture()
        {
            // A valid test WAV header + PCM samples. Test data is never surfaced in the application library.
            using var writer = new BinaryWriter(File.Create(Path));
            writer.Write("RIFF"u8); writer.Write(16036); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(16000); writer.Write(new byte[16000]);
        }
        public void Dispose() => File.Delete(Path);
    }
}





