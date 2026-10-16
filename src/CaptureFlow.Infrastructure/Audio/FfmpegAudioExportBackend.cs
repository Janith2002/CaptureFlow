using CaptureFlow.Core.Audio;
using CaptureFlow.Infrastructure.Ffmpeg;
using System.Globalization;

namespace CaptureFlow.Infrastructure.Audio;

public sealed class FfmpegAudioExportBackend : IAudioExportBackend
{
    // Share the video backend's storage policy and process runner; capture remains unchanged.
    public void CheckStorage(string outputDirectory) => FfmpegProcess.CheckStorage(outputDirectory);
    public Task EnsureAvailableAsync(string executable, CancellationToken cancellationToken) => FfmpegProcess.RunAsync(executable, ["-hide_banner", "-version"], TimeSpan.FromSeconds(10), cancellationToken);
    public Task EncodeMp3Async(string wavPath, string pendingPath, AudioRecordingOptions options, CancellationToken cancellationToken)
    {
        options.ValidateOutput();
        if (options.OutputFormat != AudioOutputFormat.Mp3) throw new ArgumentException("MP3 export configuration required.");
        if (File.Exists(pendingPath)) throw new IOException("The MP3 staging file already exists. Use a unique filename.");
        CheckStorage(Path.GetDirectoryName(Path.GetFullPath(pendingPath))!);
        // 48 kHz supports every advertised MPEG-1 bitrate, including when capture is 16 kHz.
        return RunAsync(options.FfmpegPath, ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-i", wavPath,
            "-map", "0:a:0", "-vn", "-c:a", "libmp3lame", "-b:a", options.Mp3BitrateKbps.ToString(CultureInfo.InvariantCulture) + "k",
            "-ar", "48000", "-id3v2_version", "3", "-write_xing", "1", "-f", "mp3", pendingPath], cancellationToken);
    }
    public Task ValidateMp3Async(string pendingPath, string executable, CancellationToken cancellationToken) =>
        RunAsync(executable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-xerror", "-f", "mp3", "-i", pendingPath,
            "-map", "0:a:0", "-f", "null", "-"], cancellationToken);
    private static Task RunAsync(string executable, string[] arguments, CancellationToken token) =>
        FfmpegProcess.RunAsync(executable, arguments, TimeSpan.FromMinutes(10), token);
}
