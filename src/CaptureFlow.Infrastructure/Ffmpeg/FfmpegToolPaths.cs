namespace CaptureFlow.Infrastructure.Ffmpeg;

/// <summary>External tools only. Never search the working/source directory or silently replace an explicit path.</summary>
public static class FfmpegToolPaths
{
    public static string ResolveExecutable(string executable, string? searchPath = null)
    {
        if (string.IsNullOrWhiteSpace(executable) || executable.Contains('"')) throw new IOException("Enter an executable path without quotes or command-line arguments.");
        var name = Path.GetFileName(executable);
        if (!string.Equals(name, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase) && !string.Equals(name, "ffprobe.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose ffmpeg.exe or ffprobe.exe from an external FFmpeg installation.");
        if (Path.IsPathFullyQualified(executable))
        { var path = Path.GetFullPath(executable); if (File.Exists(path)) return path; throw new FileNotFoundException("The configured external executable is missing. Restore that installation or choose another absolute path.", path); }
        if (executable != name) throw new IOException("Relative tool paths depend on the working directory. Choose an absolute external executable path.");
        foreach (var folder in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var directory = folder.Trim().Trim('"'); if (!Path.IsPathFullyQualified(directory)) continue;
            var path = Path.Combine(directory, name); if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new FileNotFoundException("FFmpeg tools were not found on PATH. Install FFmpeg externally with libx264, AAC and libmp3lame, then select its absolute ffmpeg.exe path in Settings > External tools.");
    }
    public static string ProbeFor(string ffmpeg)
    {
        var executable = ResolveExecutable(ffmpeg);
        var probe = Path.Combine(Path.GetDirectoryName(executable)!, "ffprobe.exe");
        if (!File.Exists(probe)) throw new FileNotFoundException("Matching ffprobe.exe is missing beside ffmpeg.exe. Use the pair from the same external distribution.", probe);
        return probe;
    }
    public static async Task<string> VerifyPairAsync(string ffmpeg, CancellationToken token = default)
    {
        var executable = ResolveExecutable(ffmpeg); var probe = ProbeFor(executable);
        async Task<string> VersionAsync(string path)
        {
            token.ThrowIfCancellationRequested(); using var process = new FfmpegProcess(path, ["-version"], false, true);
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(TimeSpan.FromSeconds(10));
            using var reader = new StreamReader(process.Output); var text = await reader.ReadToEndAsync(bounded.Token);
            await process.FinishAsync(TimeSpan.FromSeconds(10), token);
            var line = text.Split('\n')[0].Trim(); var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts[1] != "version") throw new IOException("The selected executable did not identify as an FFmpeg tool.");
            return parts[2];
        }
        var version = await VersionAsync(executable); var probeVersion = await VersionAsync(probe);
        if (version != probeVersion) throw new IOException("FFmpeg and FFprobe versions differ. Select matching binaries from one installation.");
        return version;
    }
}
