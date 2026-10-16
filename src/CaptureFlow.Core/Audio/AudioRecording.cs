namespace CaptureFlow.Core.Audio;
public enum AudioRecordingState { Idle, Recording, Paused, Saving, Completed, Error, Exporting, Cancelled }
public enum AudioOutputFormat { Wav, Mp3 }
public enum AudioRecordingMode { MicrophoneOnly, SystemAudioOnly, MicrophoneAndSystemAudio }
public sealed record MicrophoneDevice(string Id, string Name);
public sealed record AudioOutputDevice(string Id, string Name);
public sealed record AudioRecordingOptions(string DeviceId, string OutputDirectory, int SampleRate = 48000,
    float Gain = 1, bool Muted = false, AudioRecordingMode Mode = AudioRecordingMode.MicrophoneOnly,
    string? OutputDeviceId = null, float SystemGain = 1, bool SystemMuted = false, CaptureFlow.Core.Video.RecordingTimeline? SharedTimeline = null,
    AudioOutputFormat OutputFormat = AudioOutputFormat.Wav, int Mp3BitrateKbps = 192, string FfmpegPath = "ffmpeg.exe", [property: System.Text.Json.Serialization.JsonIgnore] Action<AudioLevels>? LevelObserver = null)
{
    public void ValidateOutput()
    {
        if (!Enum.IsDefined(OutputFormat)) throw new ArgumentException("Choose WAV or MP3.");
        if (OutputFormat == AudioOutputFormat.Mp3)
        {
            if (Mp3BitrateKbps is not (128 or 192 or 256 or 320)) throw new ArgumentOutOfRangeException(nameof(Mp3BitrateKbps), "Choose 128, 192, 256 or 320 kbps.");
            if (string.IsNullOrWhiteSpace(FfmpegPath)) throw new ArgumentException("Configure the FFmpeg executable path.");
        }
    }
}
public interface IAudioExportBackend
{
    Task EnsureAvailableAsync(string executable, CancellationToken cancellationToken);
    void CheckStorage(string outputDirectory);
    Task EncodeMp3Async(string wavPath, string pendingPath, AudioRecordingOptions options, CancellationToken cancellationToken);
    Task ValidateMp3Async(string pendingPath, string executable, CancellationToken cancellationToken);
}
public sealed record AudioRecordingResult(string FilePath, TimeSpan Duration, long AudioBytes);
public interface IAudioCaptureBackend
{
    Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync();
    Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync();
    Task<IAudioCaptureSession> CreateSessionAsync(AudioRecordingOptions options);
}
public interface IAudioCaptureSession : IAsyncDisposable
{
    event EventHandler? Ended;
    TimeSpan Duration { get; }
    Task StartAsync();
    void Pause();
    void Resume();
    void SetVolume(float gain, bool muted);
    void SetSystemVolume(float gain, bool muted);
    Task<AudioRecordingResult> StopAsync();
}
public interface IAudioRecordingService : IAsyncDisposable
{
    event EventHandler? StateChanged;
    AudioRecordingState State { get; }
    TimeSpan Duration { get; }
    string? SavedFilePath { get; }
    string? ErrorMessage { get; }
    Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync();
    Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync();
    Task StartAsync(AudioRecordingOptions options);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task CancelExportAsync();
    void SetVolume(float gain, bool muted);
    void SetSystemVolume(float gain, bool muted);
}

