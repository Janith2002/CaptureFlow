using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CaptureFlow.Application.Audio;
using CaptureFlow.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace CaptureFlow.Infrastructure.Audio;
[SupportedOSPlatform("windows")]
public sealed class NAudioMicrophoneCaptureBackend : IAudioCaptureBackend
{
    public Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync() => Task.Run<IReadOnlyList<MicrophoneDevice>>(() =>
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<MicrophoneDevice>();
        using var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        foreach (var endpoint in endpoints)
        {
            using (endpoint) devices.Add(new(endpoint.ID, endpoint.FriendlyName));
        }
        return devices.OrderBy(d => d.Name).ToArray();
    });
    public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync() => Task.FromResult<IReadOnlyList<AudioOutputDevice>>([]);
    public Task<IAudioCaptureSession> CreateSessionAsync(AudioRecordingOptions options) => Task.Run<IAudioCaptureSession>(() =>
    {
        if (options.Mode != AudioRecordingMode.MicrophoneOnly) throw new NotSupportedException("This capture path only records a microphone.");
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(options.DeviceId);
        try
        {
            if (device.State != DeviceState.Active || device.DataFlow != DataFlow.Capture)
                throw new InvalidOperationException("The selected microphone is no longer available. Refresh the microphone list.");
            return new NAudioMicrophoneCaptureSession(device, options);
        }
        catch { device.Dispose(); throw; }
    });
}
[SupportedOSPlatform("windows")]
internal sealed class NAudioMicrophoneCaptureSession : IAudioCaptureSession
{
    private readonly object sync = new();
    private readonly MMDevice device;
    private readonly WasapiRecorder capture;
    private readonly WaveFileWriter writer;
    private readonly FileStream outputStream;
    private readonly CaptureDataAvailableHandler dataHandler;
    private readonly string pendingPath;
    private readonly string finalPath;
    private readonly TaskCompletionSource firstBuffer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<AudioRecordingResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long audioBytes;
    private bool paused;
    private bool accepting;
    private bool stopRequested;
    private bool started;
    private float gain;
    private bool muted;
    private int finishing;
    private Exception? writeError;
    public event EventHandler? Ended;
    public TimeSpan Duration => TimeSpan.FromSeconds(Interlocked.Read(ref audioBytes) / (double)capture.WaveFormat.AverageBytesPerSecond);
    public NAudioMicrophoneCaptureSession(MMDevice device, AudioRecordingOptions options)
    {
        this.device = device;
        if (options.SampleRate is not (16000 or 44100 or 48000)) throw new ArgumentOutOfRangeException(nameof(options.SampleRate));
        Directory.CreateDirectory(options.OutputDirectory);
        finalPath = Path.Combine(Path.GetFullPath(options.OutputDirectory), RecordingFileName.Create(DateTimeOffset.UtcNow, Guid.NewGuid()));
        pendingPath = finalPath + ".partial";
        capture = new WasapiRecorderBuilder().WithDevice(device).WithSharedMode()
            .WithFormat(new WaveFormat(options.SampleRate, 16, 1)).Build();
        try
        {
            var stream = new FileStream(pendingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            outputStream = stream;
            try { writer = new WaveFileWriter(stream, capture.WaveFormat); }
            catch { stream.Dispose(); throw; }
        }
        catch { capture.Dispose(); throw; }
        gain = Math.Clamp(options.Gain, 0, 1); muted = options.Muted;
        dataHandler = (buffer, flags, _, _) => OnDataAvailable(buffer, flags);
        capture.DataAvailable += dataHandler;
        capture.RecordingStopped += OnRecordingStopped;
    }
    public async Task StartAsync()
    {
        try
        {
            lock (sync) { accepting = true; started = true; }
            await Task.Run(capture.StartRecording).ConfigureAwait(false);
            var ready = await Task.WhenAny(firstBuffer.Task, completion.Task).WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            if (ready == completion.Task) { await completion.Task.ConfigureAwait(false); throw new IOException("The microphone stopped during startup."); }
            await firstBuffer.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            capture.StopRecording();
            await FinishAsync(ex).ConfigureAwait(false);
            throw;
        }
    }
    // Pause excludes actual incoming buffers from the file; the device remains open.
    public void Pause() { lock (sync) paused = true; }
    public void Resume() { lock (sync) paused = false; }
    public void SetVolume(float value, bool isMuted) { Volatile.Write(ref gain, Math.Clamp(value, 0, 1)); Volatile.Write(ref muted, isMuted); }
    public void SetSystemVolume(float value, bool isMuted) { } // No system source in a microphone-only session.
    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags)
    {
        lock (sync)
        {
            if (!accepting || paused || buffer.Length == 0) return;
            try
            {
                if (audioBytes + buffer.Length > uint.MaxValue - 1024 * 1024) throw new IOException("The WAV size limit was reached. Start a new recording.");
                var currentGain = Volatile.Read(ref gain);
                var currentMuted = Volatile.Read(ref muted);
                if (currentMuted || currentGain != 1 || (flags & AudioClientBufferFlags.Silent) != 0)
                {
                    var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
                    try
                    {
                        var processed = rented.AsSpan(0, buffer.Length);
                        buffer.CopyTo(processed);
                        if (currentMuted || (flags & AudioClientBufferFlags.Silent) != 0) processed.Clear();
                        else
                        {
                            var samples = MemoryMarshal.Cast<byte, short>(processed);
                            for (var i = 0; i < samples.Length; i++) samples[i] = (short)(samples[i] * currentGain);
                        }
                        writer.Write(processed);
                    }
                    finally { ArrayPool<byte>.Shared.Return(rented); }
                }
                else writer.Write(buffer);
                Interlocked.Add(ref audioBytes, buffer.Length);
                firstBuffer.TrySetResult();
            }
            catch (Exception ex)
            {
                writeError = ex; accepting = false;
                capture.StopRecording();
            }
        }
    }
    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        Exception? error = args.Exception;
        lock (sync)
        {
            if (!stopRequested && error is null) error = new IOException("The microphone disconnected or stopped unexpectedly.");
        }
        _ = Task.Run(() => FinishAsync(error));
    }
    private async Task FinishAsync(Exception? error)
    {
        if (Interlocked.Exchange(ref finishing, 1) != 0) return;
        AudioRecordingResult? result = null;
        try
        {
            // Never join the capture thread from inside its callback.
            await capture.DisposeAsync().ConfigureAwait(false);
            long bytes;
            lock (sync)
            {
                accepting = false;
                WavRecordingFinalization.Close(writer, outputStream);
                bytes = audioBytes;
                error ??= writeError;
                if (!stopRequested && error is null) error = new IOException("The microphone disconnected or stopped unexpectedly.");
            }
            if (error is not null) throw error;
            if (bytes <= 0) throw new IOException("No microphone audio was captured. The recording was not saved.");
            using (var reader = new WaveFileReader(pendingPath))
            {
                if (reader.Length != bytes || reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm)
                    throw new IOException("The WAV file could not be validated. The recording was not saved.");
            }
            File.Move(pendingPath, finalPath, overwrite: false);
            result = new(finalPath, Duration, bytes);
        }
        catch (Exception ex)
        {
            // Keep failed captures outside the library under .partial; never advertise them as completed WAV files.
            try { lock (sync) { accepting = false; writer.Dispose(); } }
            catch { /* The original write/finalization error is reported below. */ }
            error = ex;
        }
        finally
        {
            capture.DataAvailable -= dataHandler;
            capture.RecordingStopped -= OnRecordingStopped;
            try { outputStream.Dispose(); }
            catch (Exception ex) { error ??= ex; }
            try { device.Dispose(); }
            catch (Exception ex) { error ??= ex; }
            if (error is not null) completion.TrySetException(error);
            else if (result is not null) completion.TrySetResult(result);
            else completion.TrySetException(new IOException("The recording did not produce a valid saved file."));
            Ended?.Invoke(this, EventArgs.Empty);
        }
    }
    public async Task<AudioRecordingResult> StopAsync()
    {
        lock (sync) stopRequested = true;
        if (!completion.Task.IsCompleted)
        {
            capture.StopRecording();
            if (!started) await FinishAsync(new IOException("Recording never started.")).ConfigureAwait(false);
        }
        return await completion.Task.ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        catch { /* StopAsync reports failures; disposal still observes the completion task and releases resources. */ }
    }
}






