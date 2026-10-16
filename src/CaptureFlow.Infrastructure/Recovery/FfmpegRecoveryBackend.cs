using System.Globalization;
using System.Text.Json;
using CaptureFlow.Core.Recovery;
using CaptureFlow.Infrastructure.Ffmpeg;
namespace CaptureFlow.Infrastructure.Recovery;

public sealed class FfmpegRecoveryBackend : IRecoveryMediaBackend
{
    public async Task<RecoveredMedia> RecoverAsync(RecoveryCandidate candidate, string pendingPath, string ffmpeg, CancellationToken token)
    {
        if (candidate.IsLive) throw new InvalidOperationException("The recording is still active.");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!); FfmpegProcess.CheckStorage(Path.GetDirectoryName(pendingPath)!);
        var video = candidate.Files.FirstOrDefault(f => Path.GetFileName(f) == "screen.mp4") ?? candidate.Files.FirstOrDefault(f => Path.GetFileName(f) == "final.mp4");
        var audio = candidate.Files.FirstOrDefault(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            ?? candidate.Files.FirstOrDefault(f => f.EndsWith(".wav.partial", StringComparison.OrdinalIgnoreCase));
        RecoveredMedia? sourceVideo = null;
        List<string> args = ["-hide_banner", "-v", "error", "-nostdin", "-n"];
        if (video is not null)
        {
            var salvaged = Path.Combine(Path.GetDirectoryName(pendingPath)!, "fragments.mp4");
            try { await MediaSalvage.CopyFragmentsAsync(video, salvaged, token); }
            catch (IOException)
            {
                // A finalized ordinary MP4 is accepted only after probing and full decoding.
                await ValidateAsync(video, ffmpeg, token);
                await using var original = File.OpenRead(video);
                await using var copy = new FileStream(salvaged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                await original.CopyToAsync(copy, token);
            }
            sourceVideo = await ValidateAsync(salvaged, ffmpeg, token);
            args.AddRange(["-protocol_whitelist", "file,pipe", "-i", salvaged]);
        }
        if (audio is not null)
        {
            var repaired = Path.Combine(Path.GetDirectoryName(pendingPath)!, "samples.wav");
            await MediaSalvage.RepairWaveAsync(audio, repaired, token);
            args.AddRange(["-protocol_whitelist", "file,pipe", "-i", repaired]);
        }
        if (video is null && audio is null) throw new IOException("No supported temporary media remains.");
        if (candidate.Record.Format == "MP4")
        {
            if (video is null) throw new IOException("The video stream is missing; MP4 video recovery is unavailable.");
            args.AddRange(["-map", "0:v:0", "-c:v", "copy"]);
            if (audio is not null) args.AddRange(["-map", "1:a:0", "-c:a", "aac", "-b:a", "192k"]);
            else if (sourceVideo?.HasAudio == true) args.AddRange(["-map", "0:a:0", "-c:a", "copy"]);
            else args.Add("-an");
            args.AddRange(["-t", sourceVideo!.Duration.ToString("F6", CultureInfo.InvariantCulture)]);
            args.AddRange(["-movflags", "+faststart", "-f", "mp4"]);
        }
        else if (candidate.Record.Format == "MP3") args.AddRange(["-vn", "-c:a", "libmp3lame", "-b:a", "192k", "-f", "mp3"]);
        else args.AddRange(["-vn", "-c:a", "pcm_s16le", "-f", "wav"]);
        args.Add(pendingPath);
        await FfmpegProcess.RunAsync(ffmpeg, args, TimeSpan.FromMinutes(10), token);
        var validated = await ValidateAsync(pendingPath, ffmpeg, token);
        return validated with { Warning = "Partial recovery: only complete encoded fragments/samples are retained." + (candidate.Record.ExpectedAudio && !validated.HasAudio ? " The expected audio stream is missing." : "") };
    }
    public async Task<RecoveredMedia> ValidateAsync(string path, string ffmpeg, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var probe = FfmpegToolPaths.ProbeFor(ffmpeg);
        using var process = new FfmpegProcess(probe, ["-v", "error", "-protocol_whitelist", "file,pipe", "-show_entries", "format=duration:stream=codec_type", "-of", "json", path], false, true);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(TimeSpan.FromSeconds(30));
        using var reader = new StreamReader(process.Output);
        var jsonBuilder = new System.Text.StringBuilder(); var buffer = new char[4096]; int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), bounded.Token)) > 0)
        { if (jsonBuilder.Length + read > 65536) throw new IOException("Media metadata exceeds the recovery limit."); jsonBuilder.Append(buffer, 0, read); }
        var json = jsonBuilder.ToString();
        await process.FinishAsync(TimeSpan.FromSeconds(30), token);
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        var duration = double.Parse(root.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        var streams = root.GetProperty("streams").EnumerateArray().Select(s => s.GetProperty("codec_type").GetString()).ToArray();
        if (!double.IsFinite(duration) || duration <= 0 || !streams.Any(s => s is "audio" or "video")) throw new IOException("Recovered media has no valid duration or streams.");
        await FfmpegProcess.RunAsync(ffmpeg, ["-v", "error", "-xerror", "-nostdin", "-protocol_whitelist", "file,pipe", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"], TimeSpan.FromMinutes(10), token);
        return new(path, duration, streams.Contains("video"), streams.Contains("audio"), "");
    }
}
