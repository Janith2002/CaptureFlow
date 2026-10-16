using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Ffmpeg;
namespace CaptureFlow.Infrastructure.Video;

public sealed class FfmpegEncoderCapabilities(string? temporaryRoot = null) : IVideoEncoderCapabilities
{
    public async Task<IReadOnlyList<VideoEncoderCapability>> ProbeAsync(string executable, VideoSize outputSize, int fps, int bitrateKbps, CancellationToken cancellationToken)
    {
        new VideoQualityPreferences(FramesPerSecond: fps, CustomBitrateKbps: bitrateKbps).Validate();
        if (outputSize.Width < 2 || outputSize.Height < 2 || outputSize.Width % 2 != 0 || outputSize.Height % 2 != 0 || (long)outputSize.Width * outputSize.Height > 33_554_432)
            throw new ArgumentException("Encoder probes require supported even dimensions.");
        cancellationToken.ThrowIfCancellationRequested();
        var folder = Path.Combine(temporaryRoot ?? Path.GetTempPath(), "CaptureFlow-encoder-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var results = new List<VideoEncoderCapability>();
            foreach (var encoder in new[] { VideoEncoderChoice.Software, VideoEncoderChoice.Nvenc, VideoEncoderChoice.QuickSync, VideoEncoderChoice.Amf })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = Path.Combine(folder, encoder + ".mp4");
                try
                {
                    var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "lavfi", "-i", $"color=c=black:s={outputSize.Width}x{outputSize.Height}:r={fps}", "-frames:v", "8", "-an" };
                    args.AddRange(FfmpegVideoEncodingBackend.CodecArguments(encoder, bitrateKbps, "veryfast"));
                    args.AddRange(["-f", "mp4", file]);
                    await FfmpegProcess.RunAsync(executable, args, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
                    await FfmpegProcess.RunAsync(executable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-xerror", "-threads", "2", "-i", file, "-map", "0:v:0", "-f", "null", "-"], TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
                    results.Add(new(encoder, true, $"Real 8-frame MP4 encode and decode succeeded at {outputSize.Width} × {outputSize.Height}, {fps} FPS."));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                { results.Add(new(encoder, false, ex.Message)); }
            }
            return results;
        }
        finally { Directory.Delete(folder, true); }
    }
}
