using System.Diagnostics;
using CaptureFlow.Core.Recovery;
using CaptureFlow.Infrastructure.Recovery;
using NAudio.Wave;
namespace CaptureFlow.Tests;

public class FfmpegRecoveryTests
{
    [FfmpegTheory] [InlineData("WAV")] [InlineData("MP3")] [InlineData("MP4")]
    public async Task SyntheticRecoveryProducesFullyDecodedMedia(string format)
    {
        using var scratch = new RecoveryTests.Scratch(); var wav = Path.Combine(scratch.Path, "capture.wav");
        using (var writer = new WaveFileWriter(wav, new WaveFormat(48000, 16, 2)))
            for (var i = 0; i < 96000; i++) { writer.WriteSample((float)(.15 * Math.Sin(i * 2 * Math.PI * 440 / 48000))); writer.WriteSample(0); }
        var ffmpeg = Executable.Find("ffmpeg.exe")!;
        if (format == "MP4") await RunAsync(ffmpeg, ["-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc2=size=160x96:rate=30", "-t", "2", "-c:v", "libx264", "-preset", "ultrafast", "-g", "30", "-movflags", "+empty_moov+frag_keyframe+default_base_moof", Path.Combine(scratch.Path, "screen.mp4")]);
        var files = Directory.GetFiles(scratch.Path); var before = await File.ReadAllBytesAsync(wav);
        var record = new RecoveryRecord(Guid.NewGuid(), "Monitor", scratch.Path, scratch.Path, format, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Error", ExpectedAudio: true);
        var output = Path.Combine(scratch.Path, "job", "pending." + format.ToLowerInvariant());
        var backend = new FfmpegRecoveryBackend(); var media = await backend.RecoverAsync(new(record, files, 0, false), output, ffmpeg, default);
        Assert.True(media.HasAudio); Assert.Equal(format == "MP4", media.HasVideo); Assert.InRange(media.Duration, 1.9, 2.15);
        Assert.Contains("Partial", media.Warning); Assert.Equal(before, await File.ReadAllBytesAsync(wav));
        foreach (var file in Directory.GetFiles(scratch.Path, "*", SearchOption.AllDirectories)) using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }
    [FfmpegTheory] [InlineData(true)]
    public async Task InterruptedOwnedSyntheticFfmpegRetainsCompleteFragments(bool interrupted)
    {
        Assert.True(interrupted); using var scratch = new RecoveryTests.Scratch(); var video = Path.Combine(scratch.Path, "screen.mp4");
        var ffmpeg = Executable.Find("ffmpeg.exe")!;
        var info = StartInfo(ffmpeg, ["-v", "error", "-nostdin", "-n", "-re", "-f", "lavfi", "-i", "testsrc2=size=160x96:rate=30", "-t", "60", "-c:v", "libx264", "-preset", "ultrafast", "-g", "30", "-movflags", "+empty_moov+frag_keyframe+default_base_moof", "-flush_packets", "1", video]);
        using var process = Process.Start(info)!; var errors = process.StandardError.ReadToEndAsync();
        try
        {
            var watch = Stopwatch.StartNew();
            while ((!File.Exists(video) || new FileInfo(video).Length < 20000) && watch.Elapsed < TimeSpan.FromSeconds(12))
            { Assert.False(process.HasExited, process.HasExited ? await errors : ""); await Task.Delay(100); }
            Assert.True(File.Exists(video) && new FileInfo(video).Length >= 20000, "Synthetic encoder did not produce sufficient fragments within 12 seconds.");
        }
        finally { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); await errors; }
        var originalLength = new FileInfo(video).Length;
        var record = new RecoveryRecord(Guid.NewGuid(), "Monitor", scratch.Path, scratch.Path, "MP4", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Error", ExpectedAudio: true);
        var media = await new FfmpegRecoveryBackend().RecoverAsync(new(record, [video], originalLength, false), Path.Combine(scratch.Path, "job", "recovered.mp4"), ffmpeg, default);
        Assert.True(media.HasVideo); Assert.False(media.HasAudio); Assert.Contains("missing", media.Warning); Assert.True(media.Duration > .9);
        Assert.Equal(originalLength, new FileInfo(video).Length);
        using (File.Open(video, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }
    [FfmpegTheory] [InlineData(true)]
    public async Task InvalidMediaFailsValidationAndCancelledRecoveryKeepsSource(bool invalid)
    {
        Assert.True(invalid); using var scratch = new RecoveryTests.Scratch(); var path = Path.Combine(scratch.Path, "broken.mp4"); await File.WriteAllTextAsync(path, "invalid media");
        var backend = new FfmpegRecoveryBackend();
        await Assert.ThrowsAsync<IOException>(() => backend.ValidateAsync(path, Executable.Find("ffmpeg.exe")!, default));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.ValidateAsync(path, Executable.Find("ffmpeg.exe")!, cancellation.Token));
        Assert.Equal("invalid media", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task MissingFfmpegIsReported()
    {
        using var scratch = new RecoveryTests.Scratch();
        await Assert.ThrowsAsync<IOException>(() => new FfmpegRecoveryBackend().ValidateAsync(Path.Combine(scratch.Path, "missing.wav"), Path.Combine(scratch.Path, "missing-ffmpeg.exe"), default));
    }
    private static ProcessStartInfo StartInfo(string executable, string[] args)
    { var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }; foreach (var arg in args) info.ArgumentList.Add(arg); return info; }
    private static async Task RunAsync(string executable, string[] args)
    {
        using var process = Process.Start(StartInfo(executable, args))!; var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); Assert.True(process.ExitCode == 0, await errors); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}
