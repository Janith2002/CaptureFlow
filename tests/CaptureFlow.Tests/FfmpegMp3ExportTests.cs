using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CaptureFlow.Core.Audio;
using CaptureFlow.Infrastructure.Audio;
using NAudio.Wave;

namespace CaptureFlow.Tests;

// Generated PCM tones exercise the real encoder. No physical capture is performed.
public class FfmpegMp3ExportTests
{
    [FfmpegTheory]
    [InlineData(128, 1, 48000)] [InlineData(192, 1, 48000)] [InlineData(256, 1, 48000)] [InlineData(320, 1, 48000)]
    [InlineData(128, 2, 48000)] [InlineData(192, 2, 48000)] [InlineData(256, 2, 48000)] [InlineData(320, 2, 48000)]
    [InlineData(320, 2, 16000)] [InlineData(192, 1, 44100)]
    public async Task RealMp3FixturesEncodeProbeAndFullyDecode(int bitrate, int channels, int rate)
    {
        using var fixture = new DirectoryFixture();
        var wav = Path.Combine(fixture.Path, "generated tone.wav");
        using (var writer = new WaveFileWriter(wav, new WaveFormat(rate, 16, channels)))
        {
            for (var i = 0; i < rate; i++) for (var channel = 0; channel < channels; channel++)
                writer.WriteSample((float)(.1 * Math.Sin(2 * Math.PI * 440 * i / rate)));
        }
        var output = Path.Combine(fixture.Path, "export.mp3.partial");
        var options = new AudioRecordingOptions("fixture", fixture.Path, OutputFormat: AudioOutputFormat.Mp3, Mp3BitrateKbps: bitrate, FfmpegPath: Executable.Find("ffmpeg.exe")!);
        var backend = new FfmpegAudioExportBackend();
        await backend.EnsureAvailableAsync(options.FfmpegPath, CancellationToken.None);
        await backend.EncodeMp3Async(wav, output, options, CancellationToken.None);
        await backend.ValidateMp3Async(output, options.FfmpegPath, CancellationToken.None);
        Assert.True(File.Exists(wav)); Assert.True(new FileInfo(output).Length > 0);
        var info = new ProcessStartInfo(Executable.Find("ffprobe.exe")!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", output }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var json = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        Assert.True(process.ExitCode == 0, await errors);
        using var probe = JsonDocument.Parse(await json);
        var stream = Assert.Single(probe.RootElement.GetProperty("streams").EnumerateArray());
        Assert.Equal("mp3", stream.GetProperty("codec_name").GetString()); Assert.Equal(channels, stream.GetProperty("channels").GetInt32());
        Assert.Equal("48000", stream.GetProperty("sample_rate").GetString());
        Assert.Equal(bitrate * 1000, int.Parse(stream.GetProperty("bit_rate").GetString()!, CultureInfo.InvariantCulture));
        Assert.InRange(double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture), 1, 1.1); // MPEG frame padding.
    }
    [Fact]
    public async Task MissingFfmpegIsActionable()
    {
        using var fixture = new DirectoryFixture();
        var error = await Assert.ThrowsAsync<IOException>(() => new FfmpegAudioExportBackend().EnsureAvailableAsync(Path.Combine(fixture.Path, "missing.exe"), CancellationToken.None));
        Assert.Contains("Cannot start FFmpeg", error.Message);
    }
    [FfmpegTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task CorruptInputOrMp3IsRejected(bool validating)
    {
        using var fixture = new DirectoryFixture();
        var bad = Path.Combine(fixture.Path, "invalid.mp3"); await File.WriteAllTextAsync(bad, "not audio");
        var backend = new FfmpegAudioExportBackend();
        var options = new AudioRecordingOptions("fixture", fixture.Path, OutputFormat: AudioOutputFormat.Mp3, FfmpegPath: Executable.Find("ffmpeg.exe")!);
        await Assert.ThrowsAsync<IOException>(() => validating ? backend.ValidateMp3Async(bad, options.FfmpegPath, CancellationToken.None)
            : backend.EncodeMp3Async(bad, Path.Combine(fixture.Path, "pending.mp3"), options, CancellationToken.None));
        Assert.Equal("not audio", await File.ReadAllTextAsync(bad));
    }
    [Fact]
    public async Task ExistingDestinationIsNotOverwritten()
    {
        using var fixture = new DirectoryFixture(); var path = Path.Combine(fixture.Path, "existing.mp3"); await File.WriteAllTextAsync(path, "preserve");
        await Assert.ThrowsAsync<IOException>(() => new FfmpegAudioExportBackend().EncodeMp3Async("unused.wav", path,
            new("fixture", fixture.Path, OutputFormat: AudioOutputFormat.Mp3), CancellationToken.None));
        Assert.Equal("preserve", await File.ReadAllTextAsync(path));
    }
    [FfmpegTheory]
    [InlineData(320)]
    public async Task CancellationTerminatesOwnedFfmpegProcess(int bitrate)
    {
        using var fixture = new DirectoryFixture(); var wav = Path.Combine(fixture.Path, "large.wav");
        using (var writer = new WaveFileWriter(wav, new WaveFormat(48000, 16, 2)))
        {
            var block = new byte[48000 * 4]; for (var i = 0; i < 120; i++) writer.Write(block);
        }
        var options = new AudioRecordingOptions("fixture", fixture.Path, OutputFormat: AudioOutputFormat.Mp3, Mp3BitrateKbps: bitrate, FfmpegPath: Executable.Find("ffmpeg.exe")!);
        using var cancellation = new CancellationTokenSource();
        var export = new FfmpegAudioExportBackend().EncodeMp3Async(wav, Path.Combine(fixture.Path, "cancel.mp3.partial"), options, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export.WaitAsync(TimeSpan.FromSeconds(10)));
        // All owned input/output process handles must be gone once cancellation completes.
        foreach (var path in Directory.GetFiles(fixture.Path)) using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }
    private sealed class DirectoryFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CaptureFlow MP3 integration", Guid.NewGuid().ToString("N"));
        public DirectoryFixture() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
