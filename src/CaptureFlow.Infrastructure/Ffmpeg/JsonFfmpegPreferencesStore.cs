using System.Text.Json;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Ffmpeg;
public sealed class JsonFfmpegPreferencesStore(string path) : IFfmpegPreferencesStore
{
    public async Task<string> LoadAsync(CancellationToken token = default)
    {
        if (!File.Exists(path)) return "ffmpeg.exe";
        if (new FileInfo(path).Length > 65536) throw new IOException("External-tool preferences exceed their safety limit.");
        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<string>(input, cancellationToken: token) ?? throw new IOException("Invalid external-tool preferences.");
    }
    public async Task SaveAsync(string executable, CancellationToken token = default)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("Save an absolute external FFmpeg path.");
        executable = FfmpegToolPaths.ResolveExecutable(executable); _ = FfmpegToolPaths.ProbeFor(executable);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            { await JsonSerializer.SerializeAsync(output, executable, cancellationToken: token); output.Flush(true); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
