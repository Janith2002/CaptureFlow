using CaptureFlow.Core.Video;
namespace CaptureFlow.Application.Video;

/// <summary>Retries only Auto hardware exports from a retained intermediate, never from lost live frames.</summary>
public static class EncoderExportFallback
{
    public static async Task<bool> RunAsync(VideoEncoderChoice requested, VideoEncoderChoice selected,
        Func<VideoEncoderChoice, Task> export, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { await export(selected).ConfigureAwait(false); return false; }
        catch (Exception ex) when (requested == VideoEncoderChoice.Auto && selected is VideoEncoderChoice.Nvenc or VideoEncoderChoice.QuickSync or VideoEncoderChoice.Amf && ex is IOException or TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await export(VideoEncoderChoice.Software).ConfigureAwait(false); return true;
        }
        catch (Exception ex) when (requested is VideoEncoderChoice.Nvenc or VideoEncoderChoice.QuickSync or VideoEncoderChoice.Amf && ex is IOException or TimeoutException)
        { throw new IOException($"{requested} export failed. Explicit hardware selections do not fall back. " + ex.Message, ex); }
    }
}
