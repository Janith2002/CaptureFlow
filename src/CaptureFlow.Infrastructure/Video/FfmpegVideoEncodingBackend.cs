using CaptureFlow.Infrastructure.Ffmpeg;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CaptureFlow.Core.Video;
using System.Text.Json;
using CaptureFlow.Application.Video;

namespace CaptureFlow.Infrastructure.Video;

/// <summary>Streams owned BGRA frames directly into FFmpeg; never buffers an entire recording.</summary>
public sealed class FfmpegVideoEncodingBackend(IVideoEncoderCapabilities? capabilities = null, IVideoExportProcess? exportProcess = null) : IVideoEncodingBackend
{
    private readonly IVideoEncoderCapabilities capabilities = capabilities ?? new FfmpegEncoderCapabilities();
    private readonly IVideoExportProcess exportProcess = exportProcess ?? new FfmpegVideoExportProcess();
    private sealed record EncodingManifest(VideoEncoderPlan Plan, int BitrateKbps, string SoftwarePreset);
    private static string ManifestPath(string videoPath) => videoPath + ".encoding.json";
    public string? GetEncodingStatus(string videoPath) => File.Exists(ManifestPath(videoPath)) ? JsonSerializer.Deserialize<EncodingManifest>(File.ReadAllText(ManifestPath(videoPath)))?.Plan.Message : null;
    public static string CodecName(VideoEncoderChoice choice) => choice switch
    { VideoEncoderChoice.Software => "libx264", VideoEncoderChoice.Nvenc => "h264_nvenc", VideoEncoderChoice.QuickSync => "h264_qsv", VideoEncoderChoice.Amf => "h264_amf", _ => throw new ArgumentException("Resolve Auto before encoding.") };
    public static string[] CodecArguments(VideoEncoderChoice encoder, int bitrateKbps, string softwarePreset)
    {
        if (bitrateKbps is < VideoQualityPreferences.MinimumBitrateKbps or > VideoQualityPreferences.MaximumBitrateKbps) throw new ArgumentOutOfRangeException(nameof(bitrateKbps));
        var args = new List<string> { "-c:v", CodecName(encoder), "-threads", Math.Clamp(Environment.ProcessorCount / 2, 1, 8).ToString(CultureInfo.InvariantCulture), "-bf", "0" };
        if (encoder == VideoEncoderChoice.Software) args.AddRange(["-preset", softwarePreset]);
        args.AddRange(["-b:v", $"{bitrateKbps}k", "-maxrate", $"{bitrateKbps * 3 / 2}k", "-bufsize", $"{bitrateKbps * 2}k", "-pix_fmt", encoder == VideoEncoderChoice.Software ? "yuv420p" : "nv12"]);
        return args.ToArray();
    }
    public void CheckStorage(string outputDirectory)
    {
        FfmpegProcess.CheckStorage(outputDirectory);
    }

    public Task EnsureAvailableAsync(string executable, CancellationToken cancellationToken) =>
        RunAsync(executable, ["-hide_banner", "-version"], TimeSpan.FromSeconds(10), cancellationToken);

    public async Task<IVideoEncoder> CreateAsync(VideoRecordingConfiguration configuration, VideoSize sourceSize,
        string videoPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        configuration.Validate();
        RejectExistingOutput(videoPath);
        var output = configuration.QualityPreferences?.OutputSize(sourceSize);
        var width = output?.Width ?? configuration.OutputWidth ?? sourceSize.Width / 2 * 2;
        var height = output?.Height ?? configuration.OutputHeight ?? sourceSize.Height / 2 * 2;
        if (width < 2 || height < 2) throw new ArgumentException("Video dimensions are too small.");
        // Preserve aspect ratio when a monitor has a different aspect ratio from the selected output.
        var filter = $"scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2";
        EncodingManifest? manifest = null;
        if (configuration.QualityPreferences is { } preferences)
        {
            var bitrate = preferences.BitrateKbps(sourceSize);
            var available = preferences.Encoder == VideoEncoderChoice.Software ? new[] { new VideoEncoderCapability(VideoEncoderChoice.Software, true, "Software selected.") } :
                await capabilities.ProbeAsync(configuration.FfmpegPath, new(width, height), preferences.FramesPerSecond, bitrate, cancellationToken).ConfigureAwait(false);
            var plan = VideoEncoderPlan.Select(preferences.Encoder, available);
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.Hardware && !available.Any(c => c.Encoder == VideoEncoderChoice.Software && c.Available)) throw new NotSupportedException("libx264 is required for the recoverable capture intermediate.");
            manifest = new(plan, bitrate, preferences.SoftwarePreset);
            await using var metadata = new FileStream(ManifestPath(videoPath), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
            await JsonSerializer.SerializeAsync(metadata, manifest, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-filter_threads", "2",
            "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", $"{sourceSize.Width}x{sourceSize.Height}",
            "-framerate", configuration.QualityPreferences is null && configuration.Source.Kind == VideoSourceKind.Webcam && configuration.Source.CameraFormat is { } cameraFormat ? $"{cameraFormat.FrameRateNumerator}/{cameraFormat.FrameRateDenominator}" : configuration.FramesPerSecond.ToString(CultureInfo.InvariantCulture), "-i", "pipe:0", "-an", "-vf", filter };
        if (manifest?.Plan.Hardware == true) arguments.AddRange(["-c:v", "libx264", "-preset", "ultrafast", "-qp", "0", "-bf", "0", "-threads", "4", "-pix_fmt", "yuv420p"]);
        else if (manifest is not null) arguments.AddRange(CodecArguments(VideoEncoderChoice.Software, manifest.BitrateKbps, manifest.SoftwarePreset));
        else arguments.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-crf", configuration.Quality.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p"]);
        // Only intermediates are fragmented. Final export retains the ordinary MP4 format.
        arguments.AddRange(["-g", configuration.EffectiveFramesPerSecond.ToString(CultureInfo.InvariantCulture),
            "-movflags", "+empty_moov+frag_keyframe+default_base_moof", "-f", "mp4", videoPath]);
        cancellationToken.ThrowIfCancellationRequested();
        return new Encoder(new FfmpegProcess(configuration.FfmpegPath, arguments, true), sourceSize, manifest?.Plan.Message);
    }

    public async Task MuxAsync(VideoRecordingConfiguration configuration, string videoPath, string? audioPath,
        string finalPath, TimeSpan duration, CancellationToken cancellationToken)
    {
        RejectExistingOutput(finalPath);
        var manifest = File.Exists(ManifestPath(videoPath)) ? JsonSerializer.Deserialize<EncodingManifest>(await File.ReadAllTextAsync(ManifestPath(videoPath), cancellationToken).ConfigureAwait(false)) : null;
        if (manifest?.Plan.Hardware != true)
        { await ExportAsync(null, finalPath).ConfigureAwait(false); return; }
        var pending = finalPath + ".export-" + Guid.NewGuid().ToString("N") + ".mp4";
        try
        {
            var fallback = await EncoderExportFallback.RunAsync(configuration.QualityPreferences!.Encoder, manifest.Plan.Encoder, async selected =>
            {
                if (File.Exists(pending)) File.Delete(pending); // Only this attempt's unique staging file.
                CheckStorage(configuration.OutputDirectory);
                await ExportAsync(selected, pending).ConfigureAwait(false);
                await ValidateAsync(pending, configuration.FfmpegPath, cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested(); File.Move(pending, finalPath, false);
            if (fallback) manifest = manifest with { Plan = new(VideoEncoderChoice.Software, "Auto hardware export failed; software export succeeded from the retained intermediate.") };
            await File.WriteAllTextAsync(ManifestPath(videoPath), JsonSerializer.Serialize(manifest), cancellationToken).ConfigureAwait(false);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
        async Task ExportAsync(VideoEncoderChoice? encoder, string destination)
        {
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-filter_threads", "2", "-i", videoPath };
            if (audioPath is not null) args.AddRange(["-i", audioPath]);
            args.AddRange(["-map", "0:v:0"]);
            if (encoder is { } selected) args.AddRange(CodecArguments(selected, manifest!.BitrateKbps, manifest.SoftwarePreset));
            else args.AddRange(["-c:v", "copy"]);
            if (audioPath is not null) args.AddRange(["-map", "1:a:0", "-c:a", "aac", "-b:a", "192k", "-af", "apad"]);
            args.AddRange(["-t", duration.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture), "-movflags", "+faststart", "-f", "mp4", destination]);
            await exportProcess.RunAsync(configuration.FfmpegPath, args, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void RejectExistingOutput(string path)
    {
        if (File.Exists(path)) throw new IOException("The recording output already exists. Choose a unique filename: " + path);
    }

    // Decode every selected stream, with decoding errors fatal. Size alone cannot establish validity.
    public Task ValidateAsync(string filePath, string executable, CancellationToken cancellationToken) =>
        RunAsync(executable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-xerror", "-threads", "2", "-i", filePath,
            "-map", "0:v:0", "-map", "0:a?", "-f", "null", "-"], TimeSpan.FromMinutes(10), cancellationToken);

    private static Task RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken token) =>
        FfmpegProcess.RunAsync(executable, arguments, timeout, token);

    private sealed class Encoder(FfmpegProcess process, VideoSize size, string? description) : IVideoEncoder
    {
        public string? Description => description;
        private long count;
        private bool finished;
        private readonly object finalizationGate = new();
        private Task? finalization;
        public long FrameCount => Interlocked.Read(ref count);
        public async Task WriteFrameAsync(VideoFrame frame, CancellationToken cancellationToken)
        {
            if (finished) throw new InvalidOperationException("The encoder is already finalized.");
            if (frame.Size != size) throw new IOException("The capture dimensions changed while encoding.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await process.Input.WriteAsync(frame.Pixels, timeout.Token).ConfigureAwait(false);
                Interlocked.Increment(ref count);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new TimeoutException("FFmpeg stopped accepting video frames."); }
            catch (IOException ex) { throw new IOException("FFmpeg video encoding failed. " + process.ErrorTail, ex); }
        }
        public Task FinishAsync(CancellationToken cancellationToken)
        {
            // Repeated callers must observe the original result, including failures.
            lock (finalizationGate) return finalization ??= FinishCoreAsync(cancellationToken);
        }
        private async Task FinishCoreAsync(CancellationToken cancellationToken)
        {
            finished = true;
            process.Input.Close();
            await process.FinishAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        public ValueTask DisposeAsync() { process.Dispose(); return ValueTask.CompletedTask; }
    }

}
