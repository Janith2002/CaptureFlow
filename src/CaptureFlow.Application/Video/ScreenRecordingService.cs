using CaptureFlow.Core.Audio;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Application.Video;
/// <summary>Orchestrates capture, composition, encoding, and optional existing audio capture on one active timeline.</summary>
public sealed class ScreenRecordingService(IVideoSourceFactory sources, IVideoEncodingBackend encoding, IVideoCompositor compositor, IAudioCaptureBackend audioBackend, CaptureFlow.Core.Recovery.IRecoveryJournal? recovery = null) : IVideoRecordingService
{
    private Guid? recoveryId;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IVideoFrameSource? source;
    private IVideoEncoder? encoder;
    private IAudioCaptureSession? audio;
    private VideoRecordingConfiguration? configuration;
    private RecordingTimeline? timeline;
    private TimeSpan lastDuration;
    private CancellationTokenSource? workerCancellation;
    private CancellationTokenSource? operationCancellation;
    private volatile bool stopRequested;
    private CancellationTokenRegistration startupCancellation;
    private Task worker = Task.CompletedTask;
    private Exception? backgroundError;
    private string? videoPath;
    private bool disposed;
    private int frameGeneration;
    public event EventHandler? StateChanged;
    public VideoRecordingState State { get; private set; }
    public TimeSpan Duration => timeline?.Duration ?? lastDuration;
    public string? SavedFilePath { get; private set; }
    public string? WorkDirectory { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? EncodingStatus { get; private set; }
    public Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken cancellationToken = default) => sources.GetMonitorsAsync(cancellationToken);
    public async Task StartAsync(VideoRecordingConfiguration options, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (source is not null || State is VideoRecordingState.Starting or VideoRecordingState.Saving) throw new InvalidOperationException("A video recording is already active.");
            SavedFilePath = null; WorkDirectory = null; ErrorMessage = null; EncodingStatus = null; backgroundError = null; lastDuration = TimeSpan.Zero;
            recoveryId = null;
            worker = Task.CompletedTask;
            stopRequested = false;
            operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationToken = operationCancellation.Token;
            Transition(VideoRecordingState.Starting);
            try
            {
                options.Validate();
                configuration = options;
                Directory.CreateDirectory(options.OutputDirectory);
                encoding.CheckStorage(options.OutputDirectory);
                await encoding.EnsureAvailableAsync(options.FfmpegPath, cancellationToken).ConfigureAwait(false);
                WorkDirectory = Path.Combine(Path.GetFullPath(options.OutputDirectory), ".captureflow-work", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(WorkDirectory);
                recoveryId = (await recoveryBeginAsync(options).ConfigureAwait(false))?.Id;
                videoPath = Path.Combine(WorkDirectory, "screen.mp4");
                timeline = new();
                workerCancellation = new();
                source = await sources.CreateAsync(options.Source, cancellationToken).ConfigureAwait(false);
                source.SetRecordingActivity(false);
                source.Failed += CaptureFailed;
                await source.StartAsync(cancellationToken).ConfigureAwait(false);
                encoder = await encoding.CreateAsync(options, source.Size, videoPath, cancellationToken).ConfigureAwait(false);
                EncodingStatus = encoder.Description;
                if (options.Audio is not null)
                {
                    audio = await audioBackend.CreateSessionAsync(options.Audio with { OutputDirectory = WorkDirectory, SharedTimeline = timeline }).ConfigureAwait(false);
                    audio.Ended += AudioEnded;
                    // The backend startup is bounded; await its completion before disposing its resources.
                    await audio.StartAsync().ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (backgroundError is not null) throw backgroundError;
                timeline.Start();
                source.SetRecordingActivity(true);
                worker = Task.Run(WriteVideoAsync);
                var activeWorker = workerCancellation;
                var activeOperation = operationCancellation;
                startupCancellation = cancellationToken.Register(() =>
                {
                    try { activeWorker.Cancel(); } catch (ObjectDisposedException) { }
                    _ = CancelOperationAsync(activeOperation);
                });
                Transition(VideoRecordingState.Recording);
                await CheckpointAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { await FinishAsync(false, ex is OperationCanceledException, ex, CancellationToken.None).ConfigureAwait(false); }
        }
        finally { gate.Release(); }
    }
    private async Task WriteVideoAsync()
    {
        var currentTimeline = timeline!;
        var token = workerCancellation!.Token;
        var lastDiskCheck = DateTime.UtcNow;
        var lastCheckpoint = DateTime.UtcNow;
        VideoFrame? held = null;
        var generation = Volatile.Read(ref frameGeneration);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var required = VideoFramePacing.RequiredFrames(currentTimeline.Duration, configuration!.EffectiveFramesPerSecond);
                if (stopRequested && encoder!.FrameCount >= required) break;
                if (encoder!.FrameCount < required)
                {
                    var currentGeneration = Volatile.Read(ref frameGeneration);
                    if (currentGeneration != generation) { held?.Dispose(); held = null; generation = currentGeneration; }
                    VideoFramePacing.ValidateLag(encoder.FrameCount, required, configuration.EffectiveFramesPerSecond);
                    if (held is null || !VideoFramePacing.HoldPrevious(encoder.FrameCount, required))
                    {
                        using var frame = await source!.ReadFrameAsync(token).ConfigureAwait(false);
                        var next = await compositor.ComposeAsync(frame, [], token).ConfigureAwait(false);
                        held?.Dispose(); held = next;
                    }
                    await encoder.WriteFrameAsync(held, token).ConfigureAwait(false);
                }
                else await Task.Delay(3, token).ConfigureAwait(false);
                if ((DateTime.UtcNow - lastDiskCheck).TotalSeconds >= 1) { encoding.CheckStorage(configuration!.OutputDirectory); lastDiskCheck = DateTime.UtcNow; }
                if ((DateTime.UtcNow - lastCheckpoint).TotalSeconds >= 30) { await CheckpointAsync().ConfigureAwait(false); lastCheckpoint = DateTime.UtcNow; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            backgroundError = ex;
            _ = FinishAfterFailureAsync(currentTimeline, ex);
        }
        finally { held?.Dispose(); }
    }
    private void CaptureFailed(object? sender, Exception ex) { if (!ReferenceEquals(sender, source) || disposed) return; backgroundError = ex; _ = FinishAfterFailureAsync(timeline!, ex); }
    private void AudioEnded(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, audio) || disposed) return;
        if (State is VideoRecordingState.Saving or VideoRecordingState.Cancelled) return;
        var ex = new IOException("An audio device stopped unexpectedly. Intermediate recordings will be retained.");
        backgroundError = ex; _ = FinishAfterFailureAsync(timeline!, ex);
    }
    private async Task FinishAfterFailureAsync(RecordingTimeline current, Exception error)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { if (ReferenceEquals(timeline, current) && source is not null) await FinishAsync(false, false, error, CancellationToken.None).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    public async Task PauseAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != VideoRecordingState.Recording) return;
            timeline!.Pause();
            source?.SetRecordingActivity(false);
            Interlocked.Increment(ref frameGeneration);
            if (audio is not null) await Task.Run(audio.Pause).ConfigureAwait(false);
            Transition(VideoRecordingState.Paused);
            await CheckpointAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { await FinishAsync(false, false, ex, CancellationToken.None).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    public async Task ResumeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != VideoRecordingState.Paused) return;
            if (audio is not null) await Task.Run(audio.Resume).ConfigureAwait(false);
            source?.SetRecordingActivity(true);
            Interlocked.Increment(ref frameGeneration);
            timeline!.Resume(); Transition(VideoRecordingState.Recording);
            await CheckpointAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { await FinishAsync(false, false, ex, CancellationToken.None).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operationCancellation?.Token ?? CancellationToken.None);
            if (source is not null) await FinishAsync(true, false, backgroundError, linked.Token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task CancelAsync()
    {
        // Cancel pending pipe writes/muxing immediately; serialized cleanup then owns all resources.
        var currentOperation = operationCancellation;
        try { currentOperation?.Cancel(); } catch (ObjectDisposedException) { }
        await CancelOperationAsync(currentOperation).ConfigureAwait(false);
    }
    private async Task CancelOperationAsync(CancellationTokenSource? currentOperation)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A cancellation callback queued for a previous recording must not cancel its successor.
            if (ReferenceEquals(currentOperation, operationCancellation) && source is not null)
                await FinishAsync(false, true, null, CancellationToken.None).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public void SetAudioVolume(float microphone, bool microphoneMuted, float system, bool systemMuted)
    {
        audio?.SetVolume(microphone, microphoneMuted); audio?.SetSystemVolume(system, systemMuted);
    }
    private async Task FinishAsync(bool publish, bool cancelled, Exception? error, CancellationToken token)
    {
        Transition(VideoRecordingState.Saving);
        try { await CheckpointAsync().ConfigureAwait(false); } catch (Exception ex) { error ??= ex; }
        timeline?.Stop(); lastDuration = timeline?.Duration ?? lastDuration;
        stopRequested = true;
        if (!publish || error is not null) workerCancellation?.Cancel();
        try { await worker.ConfigureAwait(false); } catch (Exception ex) { error ??= ex; }
        error ??= backgroundError;
        string? audioPath = null;
        try
        {
            if (audio is not null)
            {
                var currentAudio = audio; audio = null;
                currentAudio.Ended -= AudioEnded;
                try { audioPath = (await Task.Run(currentAudio.StopAsync).ConfigureAwait(false)).FilePath; }
                catch (Exception ex) { error ??= ex; }
                try { await currentAudio.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { error ??= ex; }
            }
            if (source is not null)
            {
                var currentSource = source; source = null;
                currentSource.Failed -= CaptureFailed;
                try { await currentSource.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { error ??= ex; }
            }
            if (encoder is not null)
            {
                try { await encoder.FinishAsync(token).ConfigureAwait(false); }
                catch (Exception ex) { error ??= ex; }
            }
            if (publish && error is null && encoder is { FrameCount: > 0 })
            {
                var output = Path.Combine(Path.GetFullPath(configuration!.OutputDirectory), VideoRecordingFileName.Create(DateTimeOffset.UtcNow, Guid.NewGuid(), configuration.Source.Kind));
                var pending = Path.Combine(WorkDirectory!, "final.mp4");
                var duration = TimeSpan.FromSeconds(encoder.FrameCount / (double)configuration.EffectiveFramesPerSecond);
                await encoding.MuxAsync(configuration, videoPath!, audioPath, pending, duration, token).ConfigureAwait(false);
                EncodingStatus = encoding.GetEncodingStatus(videoPath!) ?? EncodingStatus;
                await encoding.ValidateAsync(pending, configuration.FfmpegPath, token).ConfigureAwait(false);
                if (!File.Exists(pending) || new FileInfo(pending).Length < 100) throw new IOException("No valid MP4 output was saved.");
                token.ThrowIfCancellationRequested();
                if (recoveryId is Guid id)
                {
                    await using var input = File.OpenRead(pending);
                    await recovery!.PrepareOutputAsync(id, output, Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input, token)), token).ConfigureAwait(false);
                }
                File.Move(pending, output, overwrite: false);
                SavedFilePath = output; lastDuration = duration;
            }
            else if (publish && error is null) error = new IOException("No actual video frames were encoded.");
        }
        catch (Exception ex) { error ??= ex; cancelled |= ex is OperationCanceledException; }
        finally
        {
            // Independent cleanup attempts: one failing resource cannot leave another running.
            if (audio is not null) { audio.Ended -= AudioEnded; try { await audio.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { error ??= ex; } audio = null; }
            if (source is not null) { source.Failed -= CaptureFailed; try { await source.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { error ??= ex; } source = null; }
            if (encoder is not null) { try { await encoder.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { error ??= ex; } encoder = null; }
            startupCancellation.Unregister();
            workerCancellation?.Dispose(); workerCancellation = null; timeline = null;
            operationCancellation?.Dispose(); operationCancellation = null;
        }
        cancelled |= token.IsCancellationRequested || error is OperationCanceledException;
        if (error is not null && error is not OperationCanceledException) System.Diagnostics.Trace.TraceError("Video session failed: {0}: {1}", error.GetType().Name, error.Message);
        var retained = WorkDirectory is null ? "" : " Intermediate files retained in " + WorkDirectory;
        if (cancelled) { SavedFilePath = null; ErrorMessage = "Recording cancelled." + retained; Transition(VideoRecordingState.Cancelled); }
        else if (error is not null) { SavedFilePath = null; ErrorMessage = error.Message + retained; Transition(VideoRecordingState.Error); }
        else Transition(VideoRecordingState.Completed);
        try { await CheckpointAsync().ConfigureAwait(false); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError("Recovery journal update failed: {0}", ex.Message); ErrorMessage = (ErrorMessage ?? "Recording saved.") + " Recovery metadata update failed: " + ex.Message; }
    }
    private Task<CaptureFlow.Core.Recovery.RecoveryRecord?> recoveryBeginAsync(VideoRecordingConfiguration options) => recovery is null
        ? Task.FromResult<CaptureFlow.Core.Recovery.RecoveryRecord?>(null)
        : recovery.BeginAsync(options.Source.Kind.ToString(), options.OutputDirectory, WorkDirectory!, "MP4", options.Audio is not null);
    private Task CheckpointAsync() => recoveryId is Guid id ? recovery!.UpdateAsync(id, State.ToString(), Duration.TotalSeconds, SavedFilePath) : Task.CompletedTask;
    private void Transition(VideoRecordingState state) { State = state; StateChanged?.Invoke(this, EventArgs.Empty); }
    private void RequestCancellation()
    {
        // Another caller can finish cleanup between reading and cancelling the token source.
        try { operationCancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }
    public async ValueTask DisposeAsync()
    {
        RequestCancellation();
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            if (source is not null) await FinishAsync(false, true, null, CancellationToken.None).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}

