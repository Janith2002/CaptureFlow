using CaptureFlow.Core.Audio;
namespace CaptureFlow.Application.Audio;
/// <summary>Serializes recording operations and only publishes Completed after a saved-file check.</summary>
public sealed class AudioRecordingService(IAudioCaptureBackend backend, IAudioExportBackend? exporter = null, CaptureFlow.Core.Recovery.IRecoveryJournal? recovery = null) : IAudioRecordingService
{
    private Guid? recoveryId;
    private CancellationTokenSource? heartbeatCancellation;
    private Task heartbeat = Task.CompletedTask;
    private void StartHeartbeat()
    {
        if (recoveryId is not Guid id) return;
        heartbeatCancellation = new(); var token = heartbeatCancellation.Token;
        heartbeat = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
                while (await timer.WaitForNextTickAsync(token)) await recovery!.UpdateAsync(id, State.ToString(), Duration.TotalSeconds, token: token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError("Audio recovery heartbeat failed: {0}", ex.Message); }
        });
    }
    private async Task StopHeartbeatAsync()
    {
        heartbeatCancellation?.Cancel(); await heartbeat.ConfigureAwait(false);
        heartbeatCancellation?.Dispose(); heartbeatCancellation = null;
    }
    private readonly SemaphoreSlim gate = new(1, 1);
    private IAudioCaptureSession? session;
    private TimeSpan lastDuration;
    private bool disposed;
    private AudioRecordingOptions? options;
    private CancellationTokenSource? exportCancellation;
    public string? WorkDirectory { get; private set; }
    public event EventHandler? StateChanged;
    public AudioRecordingState State { get; private set; } = AudioRecordingState.Idle;
    public TimeSpan Duration => session?.Duration ?? lastDuration;
    public string? SavedFilePath { get; private set; }
    public string? ErrorMessage { get; private set; }
    public Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync() => backend.GetDevicesAsync();
    public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync() => backend.GetOutputDevicesAsync();
    public async Task StartAsync(AudioRecordingOptions options)
    {
        if (State is AudioRecordingState.Saving or AudioRecordingState.Exporting) throw new InvalidOperationException("A recording is being finalized or exported.");
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (session is not null || State is AudioRecordingState.Saving or AudioRecordingState.Exporting) throw new InvalidOperationException("A recording is already active.");
            SavedFilePath = null; ErrorMessage = null; lastDuration = TimeSpan.Zero;
            WorkDirectory = null;
            recoveryId = null;
            exportCancellation?.Dispose(); exportCancellation = null;
            try
            {
                options.ValidateOutput();
                this.options = options;
                if (options.Mode != AudioRecordingMode.SystemAudioOnly && string.IsNullOrWhiteSpace(options.DeviceId)) throw new InvalidOperationException("Select an available microphone first.");
                if (!Enum.IsDefined(options.Mode)) throw new ArgumentException("Select a valid audio recording mode.");
                if (options.Mode != AudioRecordingMode.MicrophoneOnly && string.IsNullOrWhiteSpace(options.OutputDeviceId)) throw new InvalidOperationException("Select an available Windows playback device first.");
                if (string.IsNullOrWhiteSpace(options.OutputDirectory)) throw new IOException("Choose an output directory first.");
                var captureOptions = options;
                if (options.OutputFormat == AudioOutputFormat.Mp3)
                {
                    if (exporter is null) throw new InvalidOperationException("MP3 export is not configured.");
                    exportCancellation = new();
                    Directory.CreateDirectory(options.OutputDirectory);
                    exporter.CheckStorage(options.OutputDirectory);
                    await exporter.EnsureAvailableAsync(options.FfmpegPath, exportCancellation.Token).ConfigureAwait(false);
                    WorkDirectory = Path.Combine(Path.GetFullPath(options.OutputDirectory), ".captureflow-work", "audio-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(WorkDirectory);
                    captureOptions = options with { OutputDirectory = WorkDirectory, OutputFormat = AudioOutputFormat.Wav };
                }
                if (recovery?.RecordingEnabled == true)
                {
                    WorkDirectory ??= Path.Combine(Path.GetFullPath(options.OutputDirectory), ".captureflow-work", "audio-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(WorkDirectory);
                    captureOptions = options with { OutputDirectory = WorkDirectory, OutputFormat = AudioOutputFormat.Wav };
                    recoveryId = (await recovery.BeginAsync(options.Mode.ToString(), options.OutputDirectory, WorkDirectory, options.OutputFormat.ToString().ToUpperInvariant(), true).ConfigureAwait(false))?.Id;
                }
                session = await backend.CreateSessionAsync(captureOptions).ConfigureAwait(false);
                session.Ended += OnSessionEnded;
                await session.StartAsync().ConfigureAwait(false);
                Transition(AudioRecordingState.Recording);
                await CheckpointAsync().ConfigureAwait(false);
                StartHeartbeat();
            }
            catch (Exception ex) { await FailAsync(ex).ConfigureAwait(false); }
        }
        finally { gate.Release(); }
    }
    public async Task PauseAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != AudioRecordingState.Recording || session is null) return;
            try { await Task.Run(session.Pause).ConfigureAwait(false); Transition(AudioRecordingState.Paused); await CheckpointAsync().ConfigureAwait(false); }
            catch (Exception ex) { await FailAsync(ex).ConfigureAwait(false); }
        }
        finally { gate.Release(); }
    }
    public async Task ResumeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != AudioRecordingState.Paused || session is null) return;
            try { await Task.Run(session.Resume).ConfigureAwait(false); Transition(AudioRecordingState.Recording); await CheckpointAsync().ConfigureAwait(false); }
            catch (Exception ex) { await FailAsync(ex).ConfigureAwait(false); }
        }
        finally { gate.Release(); }
    }
    public async Task StopAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (session is null) return;
            Transition(AudioRecordingState.Saving);
            try
            {
                await StopHeartbeatAsync().ConfigureAwait(false);
                await CheckpointAsync().ConfigureAwait(false);
                var result = await Task.Run(session.StopAsync).ConfigureAwait(false);
                if (result.AudioBytes <= 0 || result.Duration <= TimeSpan.Zero || !File.Exists(result.FilePath) || new FileInfo(result.FilePath).Length <= 44)
                    throw new IOException("No valid audio file was saved. Check the selected devices and try again.");
                lastDuration = result.Duration;
                await ReleaseSessionAsync().ConfigureAwait(false);
                if (options!.OutputFormat == AudioOutputFormat.Mp3)
                {
                    var token = exportCancellation!.Token;
                    Transition(AudioRecordingState.Exporting);
                    await CheckpointAsync().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    var pending = Path.Combine(WorkDirectory!, "export.mp3.partial");
                    await exporter!.EncodeMp3Async(result.FilePath, pending, options, token).ConfigureAwait(false);
                    await exporter.ValidateMp3Async(pending, options.FfmpegPath, token).ConfigureAwait(false);
                    if (!File.Exists(pending) || new FileInfo(pending).Length == 0) throw new IOException("No valid MP3 file was exported.");
                    token.ThrowIfCancellationRequested();
                    var final = Path.Combine(Path.GetFullPath(options.OutputDirectory), RecordingFileName.Create(DateTimeOffset.UtcNow, Guid.NewGuid(), options.Mode, AudioOutputFormat.Mp3));
                    await PrepareOutputAsync(pending, final, token).ConfigureAwait(false);
                    File.Move(pending, final, overwrite: false);
                    SavedFilePath = final;
                }
                else if (recoveryId is not null)
                {
                    var final = Path.Combine(Path.GetFullPath(options.OutputDirectory), RecordingFileName.Create(DateTimeOffset.UtcNow, Guid.NewGuid(), options.Mode, AudioOutputFormat.Wav));
                    var pending = Path.Combine(WorkDirectory!, Guid.NewGuid().ToString("N") + ".wav");
                    await using (var input = File.OpenRead(result.FilePath))
                    await using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                    { await input.CopyToAsync(output).ConfigureAwait(false); output.Flush(true); }
                    await PrepareOutputAsync(pending, final, CancellationToken.None).ConfigureAwait(false);
                    File.Move(pending, final, false); SavedFilePath = final;
                }
                else SavedFilePath = result.FilePath;
                ErrorMessage = null;
                Transition(AudioRecordingState.Completed);
                try { await CheckpointAsync().ConfigureAwait(false); }
                catch (Exception ex) { ErrorMessage = "Recording saved, but recovery metadata update failed: " + ex.Message; }
            }
            catch (Exception ex) { await FailAsync(ex).ConfigureAwait(false); }
            finally { exportCancellation?.Dispose(); exportCancellation = null; }
        }
        finally { gate.Release(); }
    }
    public void SetVolume(float gain, bool muted) => session?.SetVolume(Math.Clamp(gain, 0, 1), muted);
    public void SetSystemVolume(float gain, bool muted) => session?.SetSystemVolume(Math.Clamp(gain, 0, 1), muted);
    public Task CancelExportAsync()
    {
        if (State == AudioRecordingState.Exporting)
        {
            try { exportCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        }
        return Task.CompletedTask;
    }
    private void OnSessionEnded(object? sender, EventArgs e) => _ = HandleUnexpectedEndAsync(sender);
    private async Task HandleUnexpectedEndAsync(object? sender)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(session, sender)) return;
            // An unrequested end (including device removal) never claims successful completion.
            try { await session!.StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { await FailAsync(ex).ConfigureAwait(false); return; }
            await FailAsync(new IOException("An audio device stopped unexpectedly. Check the selected devices and refresh the device lists.")).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private async Task FailAsync(Exception error)
    {
        System.Diagnostics.Trace.TraceError("Audio session failed: {0}: {1}", error.GetType().Name, error.Message);
        await StopHeartbeatAsync().ConfigureAwait(false);
        lastDuration = session?.Duration ?? lastDuration;
        try { await ReleaseSessionAsync().ConfigureAwait(false); }
        catch (Exception cleanupError) { error = new AggregateException(error, cleanupError); }
        SavedFilePath = null;
        exportCancellation?.Dispose(); exportCancellation = null;
        if (error is OperationCanceledException)
        {
            ErrorMessage = "MP3 export cancelled." + (WorkDirectory is null ? "" : " Intermediate WAV retained in " + WorkDirectory);
            Transition(AudioRecordingState.Cancelled);
            try { await CheckpointAsync().ConfigureAwait(false); } catch (Exception ex) { System.Diagnostics.Trace.TraceError("Recovery journal update failed: {0}", ex.Message); }
            return;
        }
        ErrorMessage = State == AudioRecordingState.Exporting ? "MP3 export failed: " + error.Message : error switch
        {
            UnauthorizedAccessException => "Access was denied. Check Windows Settings > Privacy & security > Microphone, enable microphone access for desktop apps, and check output-folder permissions.",
            _ => "Recording failed: " + error.Message + " Check the selected microphone / playback device connections, Windows microphone access for desktop apps, and folder permissions."
        };
        if (WorkDirectory is not null) ErrorMessage += " Intermediate files retained in " + WorkDirectory;
        Transition(AudioRecordingState.Error);
        try { await CheckpointAsync().ConfigureAwait(false); } catch (Exception ex) { System.Diagnostics.Trace.TraceError("Recovery journal update failed: {0}", ex.Message); }
    }
    private async Task ReleaseSessionAsync()
    {
        var current = session;
        session = null;
        if (current is null) return;
        current.Ended -= OnSessionEnded;
        await current.DisposeAsync().ConfigureAwait(false);
    }
    private void Transition(AudioRecordingState state) { State = state; StateChanged?.Invoke(this, EventArgs.Empty); }
    private Task CheckpointAsync() => recoveryId is Guid id ? recovery!.UpdateAsync(id, State.ToString(), Duration.TotalSeconds, SavedFilePath) : Task.CompletedTask;
    private async Task PrepareOutputAsync(string pending, string final, CancellationToken token)
    {
        if (recoveryId is not Guid id) return;
        await using var input = File.OpenRead(pending);
        await recovery!.PrepareOutputAsync(id, final, Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input, token)), token).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        disposed = true;
        try { exportCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        await StopAsync().ConfigureAwait(false);
        await gate.WaitAsync().ConfigureAwait(false);
        try { await ReleaseSessionAsync().ConfigureAwait(false); exportCancellation?.Dispose(); exportCancellation = null; }
        finally { gate.Release(); }
    }
}



