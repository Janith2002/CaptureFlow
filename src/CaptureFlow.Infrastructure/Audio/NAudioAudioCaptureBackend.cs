using System.Diagnostics;
using System.Runtime.Versioning;
using CaptureFlow.Application.Audio;
using CaptureFlow.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace CaptureFlow.Infrastructure.Audio;
[SupportedOSPlatform("windows")]
public sealed class NAudioAudioCaptureBackend(NAudioMicrophoneCaptureBackend microphoneBackend) : IAudioCaptureBackend
{
    public Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync() => microphoneBackend.GetDevicesAsync();
    public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync() => Task.Run<IReadOnlyList<AudioOutputDevice>>(() =>
    {
        using var enumerator = new MMDeviceEnumerator();
        using var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var devices = new List<AudioOutputDevice>();
        foreach (var endpoint in endpoints) using (endpoint) devices.Add(new(endpoint.ID, endpoint.FriendlyName));
        return devices.OrderBy(d => d.Name).ToArray();
    });
    public Task<IAudioCaptureSession> CreateSessionAsync(AudioRecordingOptions options) => options.Mode == AudioRecordingMode.MicrophoneOnly && options.SharedTimeline is null
        ? microphoneBackend.CreateSessionAsync(options)
        : Task.Run<IAudioCaptureSession>(() => new WasapiMixedCaptureSession(options));
}
[SupportedOSPlatform("windows")]
internal sealed class WasapiMixedCaptureSession : IAudioCaptureSession
{
    private const double BufferDelayQpc = .25 * AudioTimelineMixer.QpcUnitsPerSecond;
    private readonly object sync = new();
    private readonly List<Source> sources = [];
    private readonly int rate;
    private readonly bool combined;
    private readonly CaptureFlow.Core.Video.RecordingTimeline? sharedTimeline;
    private readonly FileStream stream;
    private readonly WaveFileWriter writer;
    private readonly string pendingPath;
    private readonly string finalPath;
    private readonly CancellationTokenSource workerCancellation = new();
    private readonly TaskCompletionSource firstMicrophone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<AudioRecordingResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AudioTimelineMixer? mixer;
    private Task worker = Task.CompletedTask;
    private long durationFrames;
    private bool active;
    private bool stopping;
    private bool stopRequested;
    private Exception? failure;
    private int finishing;
    private float microphoneGain;
    private float systemGain;
    private bool microphoneMuted;
    private bool systemMuted;
    private readonly Action<AudioLevels>? levelObserver;
    public event EventHandler? Ended;
    private static double NowQpc => Stopwatch.GetTimestamp() * (AudioTimelineMixer.QpcUnitsPerSecond / Stopwatch.Frequency);
    public TimeSpan Duration => TimeSpan.FromSeconds(Interlocked.Read(ref durationFrames) / (double)rate);
    public WasapiMixedCaptureSession(AudioRecordingOptions options)
    {
        if (!Enum.IsDefined(options.Mode)) throw new ArgumentException("Unsupported recording mode.");
        if (options.SampleRate is not (16000 or 44100 or 48000)) throw new ArgumentOutOfRangeException(nameof(options.SampleRate));
        sharedTimeline = options.SharedTimeline; levelObserver = options.LevelObserver;
        rate = options.SampleRate; combined = options.Mode == AudioRecordingMode.MicrophoneAndSystemAudio;
        microphoneGain = options.Gain; microphoneMuted = options.Muted;
        systemGain = options.SystemGain; systemMuted = options.SystemMuted;
        Directory.CreateDirectory(options.OutputDirectory);
        finalPath = Path.Combine(Path.GetFullPath(options.OutputDirectory), RecordingFileName.Create(DateTimeOffset.UtcNow, Guid.NewGuid(), options.Mode));
        pendingPath = finalPath + ".partial";
        try
        {
            if (options.Mode != AudioRecordingMode.SystemAudioOnly) sources.Add(CreateSource(options.DeviceId, true));
            if (options.Mode != AudioRecordingMode.MicrophoneOnly) sources.Add(CreateSource(options.OutputDeviceId!, false));
            stream = new FileStream(pendingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            try { writer = new WaveFileWriter(stream, new WaveFormat(rate, 16, 2)); }
            catch { stream.Dispose(); throw; }
        }
        catch
        {
            foreach (var source in sources) { source.Capture.Dispose(); source.Device.Dispose(); }
            throw;
        }
    }
    private Source CreateSource(string id, bool microphone)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(id);
        WasapiRecorder? capture = null;
        try
        {
            if (device.State != DeviceState.Active || device.DataFlow != (microphone ? DataFlow.Capture : DataFlow.Render))
                throw new IOException($"The selected {(microphone ? "microphone" : "playback device")} is unavailable. Refresh devices.");
            var builder = new WasapiRecorderBuilder().WithDevice(device).WithSharedMode().WithBufferLength(50);
            if (!microphone) builder.WithLoopbackCapture();
            capture = builder.Build(); // Native endpoint format, converted/resampled independently below.
            var source = new Source(device, capture, new AudioPacketConverter(capture.WaveFormat, rate), microphone);
            source.DataHandler = (buffer, flags, position, qpc) => Receive(source, buffer, flags, position, qpc);
            source.StoppedHandler = (_, args) => SourceStopped(source, args.Exception);
            capture.DataAvailable += source.DataHandler;
            capture.RecordingStopped += source.StoppedHandler;
            return source;
        }
        catch { capture?.Dispose(); device.Dispose(); throw; }
    }
    public async Task StartAsync()
    {
        try
        {
            foreach (var source in sources)
            {
                lock (sync) { if (failure is not null) throw failure; }
                await Task.Run(source.Capture.StartRecording).ConfigureAwait(false);
            }
            var timeout = Stopwatch.StartNew();
            while (sources.Any(s => s.Capture.CaptureState != CaptureState.Capturing))
            {
                lock (sync) { if (failure is not null) throw failure; }
                if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("The selected audio devices did not start.");
                await Task.Delay(10).ConfigureAwait(false);
            }
            lock (sync)
            {
                if (failure is not null) throw failure;
                mixer = new(rate, combined, NowQpc, sharedTimeline);
                active = true;
                worker = Task.Run(WriteLoopAsync);
            }
            // Loopback can legitimately produce no packets while Windows playback is silent.
            if (sources.Any(s => s.IsMicrophone))
            {
                var ready = await Task.WhenAny(firstMicrophone.Task, completion.Task).WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                if (ready == completion.Task) { await completion.Task.ConfigureAwait(false); throw new IOException("Audio capture stopped during startup."); }
            }
        }
        catch (Exception ex) { await FinishAsync(ex).ConfigureAwait(false); throw; }
    }
    public void Pause() { lock (sync) { mixer?.Pause(NowQpc); Interlocked.Exchange(ref durationFrames, mixer?.FramesAt(NowQpc) ?? 0); } }
    public void Resume()
    {
        lock (sync)
        {
            mixer?.Resume(NowQpc);
            foreach (var source in sources) source.Converter.Reset();
        }
    }
    public void SetVolume(float gain, bool muted) { Volatile.Write(ref microphoneGain, gain); Volatile.Write(ref microphoneMuted, muted); }
    public void SetSystemVolume(float gain, bool muted) { Volatile.Write(ref systemGain, gain); Volatile.Write(ref systemMuted, muted); }
    private void Receive(Source source, ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long position, long qpc)
    {
        try
        {
            lock (sync)
            {
                if (!active || mixer is null || buffer.Length == 0) return;
                var packetDuration = buffer.Length / (double)source.Capture.WaveFormat.AverageBytesPerSecond * AudioTimelineMixer.QpcUnitsPerSecond;
                var timestamp = qpc > 0 && (flags & AudioClientBufferFlags.TimestampError) == 0 ? qpc : NowQpc - packetDuration;
                var discontinuity = (flags & AudioClientBufferFlags.DataDiscontinuity) != 0 ||
                    (source.LastEndQpc is { } previous && Math.Abs(timestamp - previous) > .02 * AudioTimelineMixer.QpcUnitsPerSecond);
                if (discontinuity) { source.Converter.Reset(); source.Clock.Reset(); source.NextOutputQpc = null; }
                if ((flags & AudioClientBufferFlags.TimestampError) == 0 && qpc > 0 && source.Clock.Observe(position, timestamp) is { } effectiveRate)
                    source.Converter.SetInputRate(effectiveRate);
                var stereo = source.Converter.Convert(buffer, (flags & AudioClientBufferFlags.Silent) != 0);
                if (levelObserver is not null)
                {
                    var levels = AudioLevels.Measure(source.IsMicrophone, stereo,
                        source.IsMicrophone ? Volatile.Read(ref microphoneGain) : Volatile.Read(ref systemGain),
                        source.IsMicrophone ? Volatile.Read(ref microphoneMuted) : Volatile.Read(ref systemMuted), Stopwatch.GetTimestamp());
                    // Observers only publish a latest-value snapshot, never UI work or capture controls.
                    try { levelObserver(levels); } catch (Exception ex) { Trace.TraceWarning("Audio level observer: " + ex.Message); }
                }
                source.LastEndQpc = timestamp + packetDuration;
                // Keep the resampler's fractional-frame state continuous across adjacent packets.
                var outputTimestamp = source.NextOutputQpc is { } next && Math.Abs(next - timestamp) < .002 * AudioTimelineMixer.QpcUnitsPerSecond ? next : timestamp;
                source.NextOutputQpc = outputTimestamp + stereo.Length / 2d * AudioTimelineMixer.QpcUnitsPerSecond / rate;
                mixer.Add(source.IsMicrophone, outputTimestamp, stereo,
                    source.IsMicrophone ? Volatile.Read(ref microphoneGain) : Volatile.Read(ref systemGain),
                    source.IsMicrophone ? Volatile.Read(ref microphoneMuted) : Volatile.Read(ref systemMuted));
                if (source.IsMicrophone) firstMicrophone.TrySetResult();
            }
        }
        catch (Exception ex) { RequestFailure(ex); }
    }
    private void SourceStopped(Source source, Exception? error)
    {
        lock (sync)
        {
            if (stopping) { failure ??= error; return; }
        }
        RequestFailure(error ?? new IOException($"The selected {(source.IsMicrophone ? "microphone" : "playback device")} stopped unexpectedly."));
    }
    private void RequestFailure(Exception error)
    {
        bool shouldFinish;
        lock (sync) { failure ??= error; shouldFinish = active; }
        if (shouldFinish) _ = Task.Run(() => FinishAsync(error));
    }
    private async Task WriteLoopAsync()
    {
        var lastDeviceCheck = Stopwatch.GetTimestamp();
        try
        {
            while (!workerCancellation.IsCancellationRequested)
            {
                await Task.Delay(20, workerCancellation.Token).ConfigureAwait(false);
                lock (sync) { if (mixer is not null) { Interlocked.Exchange(ref durationFrames, mixer.FramesAt(NowQpc)); WriteThrough(mixer.FramesAt(NowQpc - BufferDelayQpc)); } }
                if (Stopwatch.GetElapsedTime(lastDeviceCheck) > TimeSpan.FromSeconds(1))
                {
                    foreach (var source in sources)
                        if (source.Device.State != DeviceState.Active) throw new IOException("A selected audio device disconnected or was disabled.");
                    lastDeviceCheck = Stopwatch.GetTimestamp();
                }
            }
        }
        catch (OperationCanceledException) when (workerCancellation.IsCancellationRequested) { }
        catch (Exception ex) { RequestFailure(ex); }
    }
    private void WriteThrough(long targetFrames)
    {
        while (mixer!.WrittenFrames < targetFrames)
        {
            var count = (int)Math.Min(rate / 50, targetFrames - mixer.WrittenFrames);
            if ((mixer.WrittenFrames + count) * 4 > uint.MaxValue - 1024 * 1024) throw new IOException("The WAV size limit was reached. Start a new recording.");
            writer.Write(mixer.Render(count));
        }
    }
    public async Task<AudioRecordingResult> StopAsync()
    {
        lock (sync) { stopRequested = true; mixer?.Stop(NowQpc); Interlocked.Exchange(ref durationFrames, mixer?.FramesAt(NowQpc) ?? 0); }
        _ = Task.Run(() => FinishAsync(null));
        return await completion.Task.ConfigureAwait(false);
    }
    private async Task FinishAsync(Exception? error)
    {
        if (Interlocked.Exchange(ref finishing, 1) != 0) return;
        AudioRecordingResult? result = null;
        lock (sync) { stopping = true; mixer?.Stop(NowQpc); failure ??= error; }
        try
        {
            // Allow delayed packets from before Stop to arrive; timestamp clipping rejects post-Stop audio.
            if (stopRequested && error is null) await Task.Delay(250).ConfigureAwait(false);
            foreach (var source in sources) source.Capture.StopRecording();
            foreach (var source in sources)
            {
                try { await source.Capture.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { lock (sync) failure ??= ex; }
            }
            await workerCancellation.CancelAsync().ConfigureAwait(false);
            await worker.ConfigureAwait(false);
            lock (sync)
            {
                active = false;
                error = failure;
                if (error is null && !stopRequested) error = new IOException("Recording stopped unexpectedly.");
                if (error is null && mixer is not null) WriteThrough(mixer.FramesAt(NowQpc));
                WavRecordingFinalization.Close(writer, stream);
            }
            if (error is not null) throw error;
            var bytes = (mixer?.WrittenFrames ?? 0) * 4;
            if (bytes <= 0) throw new IOException("No recording duration was captured.");
            using (var reader = new WaveFileReader(pendingPath))
                if (reader.Length != bytes || reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.Channels != 2)
                    throw new IOException("The saved WAV failed validation.");
            File.Move(pendingPath, finalPath, overwrite: false);
            Interlocked.Exchange(ref durationFrames, mixer!.WrittenFrames);
            result = new(finalPath, TimeSpan.FromSeconds(bytes / (rate * 4d)), bytes);
        }
        catch (Exception ex)
        {
            error = ex;
            try { writer.Dispose(); } catch { }
        }
        finally
        {
            lock (sync) active = false;
            foreach (var source in sources)
            {
                source.Capture.DataAvailable -= source.DataHandler;
                source.Capture.RecordingStopped -= source.StoppedHandler;
                try { source.Device.Dispose(); } catch (Exception ex) { error ??= ex; }
            }
            try { stream.Dispose(); } catch (Exception ex) { error ??= ex; }
            workerCancellation.Dispose();
            if (error is not null) completion.TrySetException(error);
            else if (result is not null) completion.TrySetResult(result);
            else completion.TrySetException(new IOException("No valid audio file was saved."));
            Ended?.Invoke(this, EventArgs.Empty);
        }
    }
    public async ValueTask DisposeAsync() { try { await StopAsync().ConfigureAwait(false); } catch { } }
    private sealed class Source(MMDevice device, WasapiRecorder capture, AudioPacketConverter converter, bool microphone)
    {
        public MMDevice Device { get; } = device;
        public WasapiRecorder Capture { get; } = capture;
        public AudioPacketConverter Converter { get; } = converter;
        public bool IsMicrophone { get; } = microphone;
        public double? LastEndQpc { get; set; }
        public double? NextOutputQpc { get; set; }
        public AudioClockTracker Clock { get; } = new(capture.WaveFormat.SampleRate);
        public CaptureDataAvailableHandler? DataHandler { get; set; }
        public EventHandler<StoppedEventArgs>? StoppedHandler { get; set; }
    }
}



