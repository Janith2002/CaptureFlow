using System.Security.Cryptography;
using System.Text;
using CaptureFlow.Core.Library;
using CaptureFlow.Infrastructure.Ffmpeg;

namespace CaptureFlow.Infrastructure.Library;

public sealed class FfmpegRecordingThumbnailProvider(string cacheFolder) : IRecordingThumbnailProvider
{
    private readonly SemaphoreSlim workers = new(2, 2);
    public async Task<string?> GetAsync(RecordingEntry entry, string ffmpeg, CancellationToken token)
    {
        if (entry.Format != "MP4" || entry.IsMissing) return null;
        await workers.WaitAsync(token).ConfigureAwait(false);
        string? pending = null;
        try
        {
            Directory.CreateDirectory(cacheFolder);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.FullPath + "|" + entry.Bytes + "|" + entry.Modified.UtcTicks)));
            var final = Path.Combine(cacheFolder, hash + ".png");
            if (File.Exists(final)) return final;
            pending = Path.Combine(cacheFolder, hash + "-" + Guid.NewGuid().ToString("N") + ".partial");
            await FfmpegProcess.RunAsync(ffmpeg, ["-v", "error", "-nostdin", "-n", "-i", entry.FullPath, "-frames:v", "1", "-vf", "scale=320:-2", "-c:v", "png", "-f", "image2", pending], TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
            if (!File.Exists(pending) || new FileInfo(pending).Length == 0) return null;
            if (!File.Exists(final)) File.Move(pending, final, overwrite: false);
            return final;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException) { return null; }
        finally { if (pending is not null && File.Exists(pending)) { try { File.Delete(pending); } catch (IOException) { } } workers.Release(); }
    }
}
