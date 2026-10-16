using CaptureFlow.Application.Audio;
using CaptureFlow.Core.Audio;
using NAudio.Wave;

namespace CaptureFlow.Tests;

public class Mp3ExportTests
{
    [Theory]
    [InlineData(128)] [InlineData(192)] [InlineData(256)] [InlineData(320)]
    public void SupportedBitratesValidate(int bitrate) => new AudioRecordingOptions("mic", "out", OutputFormat: AudioOutputFormat.Mp3, Mp3BitrateKbps: bitrate).ValidateOutput();
    [Theory]
    [InlineData(0)] [InlineData(127)] [InlineData(193)] [InlineData(500)]
    public void InvalidBitratesAreRejected(int bitrate) => Assert.Throws<ArgumentOutOfRangeException>(() => new AudioRecordingOptions("mic", "out", OutputFormat: AudioOutputFormat.Mp3, Mp3BitrateKbps: bitrate).ValidateOutput());
    [Fact]
    public void DefaultsKeepWavAnd192KbpsAndRejectUnknownFormats()
    {
        var options = new AudioRecordingOptions("mic", "out");
        Assert.Equal(AudioOutputFormat.Wav, options.OutputFormat); Assert.Equal(192, options.Mp3BitrateKbps);
        Assert.Throws<ArgumentException>(() => (options with { OutputFormat = (AudioOutputFormat)99 }).ValidateOutput());
        Assert.Throws<ArgumentException>(() => (options with { OutputFormat = AudioOutputFormat.Mp3, FfmpegPath = " " }).ValidateOutput());
    }
    [Theory]
    [InlineData(AudioRecordingMode.MicrophoneOnly)] [InlineData(AudioRecordingMode.SystemAudioOnly)] [InlineData(AudioRecordingMode.MicrophoneAndSystemAudio)]
    public void Mp3NamesKeepModeUtcAndUniqueExtension(AudioRecordingMode mode)
    {
        var now = new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.FromHours(5.5));
        var name = RecordingFileName.Create(now, Guid.NewGuid(), mode, AudioOutputFormat.Mp3);
        Assert.EndsWith(".mp3", name); Assert.Contains("20261013_123000_000", name);
        Assert.Equal(name, Path.GetFileName(name)); Assert.NotEqual(name, RecordingFileName.Create(now, Guid.NewGuid(), mode, AudioOutputFormat.Mp3));
    }
    [Theory]
    [InlineData(AudioRecordingMode.MicrophoneOnly)] [InlineData(AudioRecordingMode.SystemAudioOnly)] [InlineData(AudioRecordingMode.MicrophoneAndSystemAudio)]
    public async Task EveryModeUsesExistingCaptureThenExportLifecycle(AudioRecordingMode mode)
    {
        using var fixture = new Fixture();
        var states = new List<AudioRecordingState>(); fixture.Service.StateChanged += (_, _) => states.Add(fixture.Service.State);
        await fixture.Service.StartAsync(fixture.Options with { Mode = mode });
        await fixture.Service.PauseAsync(); await fixture.Service.ResumeAsync(); await fixture.Service.StopAsync();
        Assert.Equal(new[] { AudioRecordingState.Recording, AudioRecordingState.Paused, AudioRecordingState.Recording, AudioRecordingState.Saving, AudioRecordingState.Exporting, AudioRecordingState.Completed }, states);
        Assert.Equal(AudioOutputFormat.Wav, fixture.Capture.Options!.OutputFormat);
        Assert.Contains(".captureflow-work", fixture.Capture.Options.OutputDirectory);
        Assert.EndsWith(".mp3", fixture.Service.SavedFilePath); Assert.True(File.Exists(fixture.Service.SavedFilePath));
        Assert.True(File.Exists(fixture.Capture.FilePath)); Assert.Equal(1, fixture.Capture.Disposals);
        Assert.Equal(TimeSpan.FromSeconds(1), fixture.Service.Duration);
    }
    [Theory]
    [InlineData("ensure")] [InlineData("storage")] [InlineData("encode")] [InlineData("validate")] [InlineData("empty")]
    public async Task FailuresNeverPublishAndRetainCapturedWav(string failure)
    {
        using var fixture = new Fixture(); fixture.Export.Failure = failure;
        await fixture.Service.StartAsync(fixture.Options);
        await fixture.Service.StopAsync();
        Assert.Equal(AudioRecordingState.Error, fixture.Service.State); Assert.Null(fixture.Service.SavedFilePath);
        if (failure is "ensure" or "storage") Assert.Null(fixture.Capture.Options);
        else { Assert.True(File.Exists(fixture.Capture.FilePath)); Assert.Equal(1, fixture.Capture.Disposals); Assert.Contains("retained", fixture.Service.ErrorMessage); }
        Assert.Empty(Directory.GetFiles(fixture.Directory, "*.mp3"));
    }
    [Fact]
    public async Task CompletedWaitsForValidationAndCannotStartDuringExport()
    {
        using var fixture = new Fixture(); fixture.Export.BlockValidation = true;
        await fixture.Service.StartAsync(fixture.Options);
        var stop = fixture.Service.StopAsync(); await fixture.Export.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(AudioRecordingState.Exporting, fixture.Service.State); Assert.Null(fixture.Service.SavedFilePath);
        Assert.Equal(1, fixture.Capture.Disposals);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.StartAsync(fixture.Options));
        fixture.Export.Continue.TrySetResult(); await stop;
        Assert.Equal(AudioRecordingState.Completed, fixture.Service.State);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancelOrShutdownInterruptsExportAndKeepsWav(bool shutdown)
    {
        using var fixture = new Fixture(); fixture.Export.BlockValidation = true;
        await fixture.Service.StartAsync(fixture.Options);
        var stop = fixture.Service.StopAsync(); await fixture.Export.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (shutdown) await fixture.Service.DisposeAsync(); else await fixture.Service.CancelExportAsync();
        await stop.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(AudioRecordingState.Cancelled, fixture.Service.State); Assert.Null(fixture.Service.SavedFilePath);
        Assert.True(File.Exists(fixture.Capture.FilePath)); Assert.Empty(Directory.GetFiles(fixture.Directory, "*.mp3"));
    }
    [Fact]
    public async Task WavNeverInvokesFfmpeg()
    {
        using var fixture = new Fixture(); fixture.Export.Failure = "ensure";
        await fixture.Service.StartAsync(fixture.Options with { OutputFormat = AudioOutputFormat.Wav }); await fixture.Service.StopAsync();
        Assert.Equal(AudioRecordingState.Completed, fixture.Service.State); Assert.EndsWith(".wav", fixture.Service.SavedFilePath);
    }
    [Fact]
    public async Task OutputFolderThatIsAFileFailsWithoutTouchingItOrOpeningCapture()
    {
        using var fixture = new Fixture(); var path = Path.Combine(fixture.Directory, "existing-file"); await File.WriteAllTextAsync(path, "preserve");
        await fixture.Service.StartAsync(fixture.Options with { OutputDirectory = path });
        Assert.Equal(AudioRecordingState.Error, fixture.Service.State); Assert.Null(fixture.Capture.Options);
        Assert.Equal("preserve", await File.ReadAllTextAsync(path));
    }
    [Fact]
    public async Task InvalidFormatDoesNotCreateCapture()
    {
        using var fixture = new Fixture(); await fixture.Service.StartAsync(fixture.Options with { OutputFormat = (AudioOutputFormat)99 });
        Assert.Equal(AudioRecordingState.Error, fixture.Service.State); Assert.Null(fixture.Capture.Options);
    }
    [Fact]
    public async Task NewExportAfterFailureClearsErrorsAndUsesUniquePathsWithoutDeletingIntermediates()
    {
        using var fixture = new Fixture(); fixture.Export.Failure = "encode";
        await fixture.Service.StartAsync(fixture.Options); await fixture.Service.StopAsync(); var failedWav = fixture.Capture.FilePath;
        fixture.Export.Failure = null;
        await fixture.Service.StartAsync(fixture.Options); await fixture.Service.StopAsync(); var first = fixture.Service.SavedFilePath;
        await fixture.Service.StartAsync(fixture.Options); await fixture.Service.StopAsync();
        Assert.Equal(AudioRecordingState.Completed, fixture.Service.State); Assert.Null(fixture.Service.ErrorMessage);
        Assert.NotEqual(first, fixture.Service.SavedFilePath); Assert.True(File.Exists(first)); Assert.True(File.Exists(failedWav));
        Assert.Equal(3, fixture.Capture.Disposals);
    }
    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "CaptureFlow MP3 unit tests", Guid.NewGuid().ToString("N"));
        public Capture Capture { get; } = new(); public Export Export { get; } = new();
        public AudioRecordingService Service { get; }
        public AudioRecordingOptions Options => new("mic", Directory, OutputDeviceId: "speaker", OutputFormat: AudioOutputFormat.Mp3);
        public Fixture() { System.IO.Directory.CreateDirectory(Directory); Service = new(Capture, Export); }
        public void Dispose() { Service.DisposeAsync().AsTask().GetAwaiter().GetResult(); System.IO.Directory.Delete(Directory, true); }
    }
    private sealed class Capture : IAudioCaptureBackend, IAudioCaptureSession
    {
        public AudioRecordingOptions? Options { get; private set; } public string FilePath { get; private set; } = "";
        public int Disposals { get; private set; } public TimeSpan Duration => TimeSpan.FromSeconds(1);
        public event EventHandler? Ended { add { } remove { } }
        public Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync() => Task.FromResult<IReadOnlyList<MicrophoneDevice>>([]);
        public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync() => Task.FromResult<IReadOnlyList<AudioOutputDevice>>([]);
        public Task<IAudioCaptureSession> CreateSessionAsync(AudioRecordingOptions options)
        {
            Options = options; FilePath = Path.Combine(options.OutputDirectory, "fixture.wav");
            using var writer = new WaveFileWriter(FilePath, new WaveFormat(48000, 16, 1)); writer.Write(new byte[96000]);
            return Task.FromResult<IAudioCaptureSession>(this);
        }
        public Task StartAsync() => Task.CompletedTask; public void Pause() { } public void Resume() { }
        public void SetVolume(float gain, bool muted) { } public void SetSystemVolume(float gain, bool muted) { }
        public Task<AudioRecordingResult> StopAsync() => Task.FromResult(new AudioRecordingResult(FilePath, Duration, 96000));
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class Export : IAudioExportBackend
    {
        public string? Failure { get; set; } public bool BlockValidation { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task EnsureAvailableAsync(string executable, CancellationToken token) => Failure == "ensure" ? Task.FromException(new IOException("FFmpeg missing")) : Task.CompletedTask;
        public void CheckStorage(string output) { if (Failure == "storage") throw new IOException("disk full"); }
        public Task EncodeMp3Async(string wav, string pending, AudioRecordingOptions options, CancellationToken token)
        {
            if (Failure == "encode") throw new IOException("encoding failed");
            File.WriteAllBytes(pending, Failure == "empty" ? [] : new byte[200]); return Task.CompletedTask; // Explicit lifecycle double, not real MP3.
        }
        public async Task ValidateMp3Async(string pending, string executable, CancellationToken token)
        {
            Entered.TrySetResult(); if (BlockValidation) await Continue.Task.WaitAsync(token);
            if (Failure == "validate") throw new IOException("validation failed");
        }
    }
}
