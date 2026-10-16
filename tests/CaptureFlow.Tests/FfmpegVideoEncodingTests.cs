using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;

public class FfmpegVideoEncodingTests
{
    [Fact]
    public async Task MissingExecutableProducesActionableError()
    {
        var exception = await Assert.ThrowsAsync<IOException>(() => new FfmpegVideoEncodingBackend().EnsureAvailableAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ffmpeg.exe"), CancellationToken.None));
        Assert.Contains("Cannot start FFmpeg", exception.Message);
    }

    // These are real FFmpeg pipeline tests with explicitly generated test pixels and PCM.
    // They do not validate Windows Graphics Capture or physical audio devices.
    [FfmpegTheory]
    [InlineData(30, false)] [InlineData(60, false)] [InlineData(30, true)] [InlineData(60, true)]
    [InlineData(30, true, true)]
    [InlineData(30, true, false, true)] [InlineData(60, true, false, true)]
    [InlineData(30, false, false, false, VideoSourceKind.Window)] [InlineData(30, false, false, false, VideoSourceKind.Region)]
    [InlineData(30, true, false, true, VideoSourceKind.Window)] [InlineData(30, true, false, true, VideoSourceKind.Region)]
    public async Task ActualFfmpegEncodesMuxesAndDecodesExpectedStreams(int fps, bool withAudio, bool fractionalWebcam = false, bool combined = false, VideoSourceKind screenKind = VideoSourceKind.Monitor)
    {
        using var directory = new TestDirectory();
        var backend = new FfmpegVideoEncodingBackend();
        var options = new VideoRecordingConfiguration(new(VideoSourceKind.Monitor, "test-fixture"), directory.Path,
            fps, 128, 72, FfmpegPath: Executable.Find("ffmpeg.exe")!);
        if (fractionalWebcam) options = options with { Source = new(VideoSourceKind.Webcam, "camera-fixture", CameraFormat: new("fractional", 64, 48, 30000, 1001, "BGRA")) };
        if (combined) options = options with { Source = new(VideoSourceKind.ScreenAndWebcam, "screen-fixture", CameraFormat: new("slow-camera", 16, 12, 15, 1, "BGRA"), CameraId: "camera-fixture") };
        if (screenKind != VideoSourceKind.Monitor)
        {
            var id = options.Source.Id;
            options = options with { Source = options.Source with
            {
                Kind = combined ? VideoSourceKind.ScreenAndWebcam : screenKind, ScreenKind = screenKind,
                Window = screenKind == VideoSourceKind.Window ? new(id, 12345, 123, 456, "Synthetic fixture", "Fixture", 64, 48) : null,
                Region = screenKind == VideoSourceKind.Region ? new(id, 20, 20, 64, 48, 128, 96) : null
            } };
        }
        options.Validate();
        var video = Path.Combine(directory.Path, "video input.mp4");
        await backend.EnsureAvailableAsync(options.FfmpegPath, CancellationToken.None);
        await using (var encoder = await backend.CreateAsync(options, new(64, 48), video, CancellationToken.None))
        {
            for (var i = 0; i < fps; i++)
            {
                using var owner = MemoryPool<byte>.Shared.Rent(64 * 48 * 4);
                owner.Memory.Span[..(64 * 48 * 4)].Fill((byte)(i * 3));
                using var frame = new VideoFrame(new BorrowedMemory(owner.Memory), new(64, 48), TimeSpan.FromSeconds(i / (double)fps));
                if (combined)
                {
                    using var camera = OverlayCompositorTests.Frame(16, 12, 0, 255, 0);
                    using var composite = await new BgraVideoCompositor().ComposeAsync(frame, [new(camera, 40, 20, 16, 12, OverlayShape.RoundedRectangle, 1)], CancellationToken.None);
                    await encoder.WriteFrameAsync(composite, CancellationToken.None);
                }
                else await encoder.WriteFrameAsync(frame, CancellationToken.None);
            }
            await encoder.FinishAsync(CancellationToken.None);
            Assert.Equal(fps, encoder.FrameCount);
        }
        string? audio = null;
        if (withAudio)
        {
            audio = Path.Combine(directory.Path, "audio input.wav");
            WriteTestWav(audio); // Deliberately short; muxer pads to video duration.
        }
        var output = Path.Combine(directory.Path, "final output.mp4");
        await backend.MuxAsync(options, video, audio, output, TimeSpan.FromSeconds(fps / options.EffectiveFramesPerSecond), CancellationToken.None);
        await backend.ValidateAsync(output, options.FfmpegPath, CancellationToken.None);
        await backend.ValidateAsync(video, options.FfmpegPath, CancellationToken.None);
        Assert.True(File.Exists(video));
        if (withAudio) Assert.True(File.Exists(audio));
        using var probe = await ProbeAsync(output);
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var videoStream = Assert.Single(streams, s => s.GetProperty("codec_type").GetString() == "video");
        Assert.True(new FileInfo(output).Length > 0);
        var expectedDuration = fps / options.EffectiveFramesPerSecond;
        Assert.InRange(double.Parse(videoStream.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture), expectedDuration - .04, expectedDuration + .04);
        var timestamps = probe.RootElement.GetProperty("frames").EnumerateArray()
            .Where(f => f.GetProperty("media_type").GetString() == "video")
            .Select(f => double.Parse(f.GetProperty("best_effort_timestamp_time").GetString()!, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(fps, timestamps.Length);
        Assert.InRange(timestamps[0], 0, .00001);
        for (var i = 1; i < timestamps.Length; i++)
            Assert.InRange(timestamps[i] - timestamps[i - 1], 1 / options.EffectiveFramesPerSecond - .00001, 1 / options.EffectiveFramesPerSecond + .00001);
        Assert.Equal("h264", videoStream.GetProperty("codec_name").GetString());
        Assert.Equal(128, videoStream.GetProperty("width").GetInt32());
        Assert.Equal(72, videoStream.GetProperty("height").GetInt32());
        Assert.Equal(fractionalWebcam ? "30000/1001" : $"{fps}/1", videoStream.GetProperty("avg_frame_rate").GetString());
        Assert.Equal(fps.ToString(), videoStream.GetProperty("nb_frames").GetString());
        Assert.Equal(withAudio ? 1 : 0, streams.Count(s => s.GetProperty("codec_type").GetString() == "audio"));
        if (withAudio)
        {
            var audioStream = streams.Single(s => s.GetProperty("codec_type").GetString() == "audio");
            Assert.Equal("aac", audioStream.GetProperty("codec_name").GetString());
            Assert.Equal(2, audioStream.GetProperty("channels").GetInt32());
            Assert.Equal("48000", audioStream.GetProperty("sample_rate").GetString());
            Assert.InRange(double.Parse(audioStream.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture), expectedDuration - .05, expectedDuration + .05);
        }
        if (audio is not null)
        {
            using var audioProbe = await ProbeAsync(audio);
            var wav = Assert.Single(audioProbe.RootElement.GetProperty("streams").EnumerateArray());
            Assert.Equal("pcm_s16le", wav.GetProperty("codec_name").GetString());
            Assert.Equal("48000", wav.GetProperty("sample_rate").GetString());
            Assert.Equal("0.250000", wav.GetProperty("duration").GetString());
            var original = await DecodeAudioAsync(audio);
            Assert.Equal(48000 / 4 * 2, original.Length);
            Assert.InRange(original.Max(v => Math.Abs((int)v)), 999, 1000);
            var decoded = await DecodeAudioAsync(output);
            var tone = decoded.Take(48000 / 5 * 2).Select(v => v / 32768d).ToArray();
            var rms = Math.Sqrt(tone.Select(v => v * v).Average());
            Assert.InRange(rms, .015, .03); // Audible controlled tone, far below clipping.
            Assert.All(decoded.Skip(48000 / 2 * 2), v => Assert.InRange((int)v, -5, 5)); // Expected padding, not unexplained silence.
        }
    }
    [FfmpegTheory]
    [InlineData("Not a video")]
    public async Task CorruptMp4IsRejected(string contents)
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "corrupt.mp4"); File.WriteAllText(path, contents);
        await Assert.ThrowsAsync<IOException>(() => new FfmpegVideoEncodingBackend().ValidateAsync(path, Executable.Find("ffmpeg.exe")!, CancellationToken.None));
    }
    [FfmpegTheory]
    [InlineData(30)]
    public async Task DisposingEncoderWaitingForFramesTerminatesItsProcessPromptly(int fps)
    {
        using var directory = new TestDirectory();
        var options = new VideoRecordingConfiguration(new(VideoSourceKind.Monitor, "fixture"), directory.Path, fps, FfmpegPath: Executable.Find("ffmpeg.exe")!);
        var encoder = await new FfmpegVideoEncodingBackend().CreateAsync(options, new(64, 48), Path.Combine(directory.Path, "unstarted.mp4"), CancellationToken.None);
        await Task.Run(async () => { await encoder.DisposeAsync(); await encoder.DisposeAsync(); }).WaitAsync(TimeSpan.FromSeconds(10));
        // Cleanup releases any output handle; fixture directory can be removed immediately.
        foreach (var file in Directory.GetFiles(directory.Path)) using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }
    [FfmpegTheory]
    [InlineData(30)]
    public async Task FailedFinalizationCannotLaterReportSuccess(int fps)
    {
        using var directory = new TestDirectory();
        var options = new VideoRecordingConfiguration(new(VideoSourceKind.Monitor, "fixture"), directory.Path, fps, FfmpegPath: Executable.Find("ffmpeg.exe")!);
        // A missing parent directory forces an actual nonzero FFmpeg exit.
        var output = Path.Combine(directory.Path, "missing", "output.mp4");
        await using var encoder = await new FfmpegVideoEncodingBackend().CreateAsync(options, new(64, 48), output, CancellationToken.None);
        using var frame = OverlayCompositorTests.Frame(64, 48, 0, 255, 0);
        await encoder.WriteFrameAsync(frame, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => encoder.FinishAsync(CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => encoder.FinishAsync(CancellationToken.None));
        Assert.False(File.Exists(output));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExistingEncodeOrMuxDestinationIsRejectedAndPreserved(bool mux)
    {
        using var directory = new TestDirectory();
        var output = Path.Combine(directory.Path, "existing.mp4");
        await File.WriteAllTextAsync(output, "preserve this file");
        var options = new VideoRecordingConfiguration(new(VideoSourceKind.Monitor, "fixture"), directory.Path);
        var backend = new FfmpegVideoEncodingBackend();
        var error = await Assert.ThrowsAsync<IOException>(async () =>
        {
            if (mux) await backend.MuxAsync(options, "unused.mp4", null, output, TimeSpan.FromSeconds(1), CancellationToken.None);
            else await backend.CreateAsync(options, new(64, 48), output, CancellationToken.None);
        });
        Assert.Contains("already exists", error.Message);
        Assert.Equal("preserve this file", await File.ReadAllTextAsync(output));
    }
    private static async Task<JsonDocument> ProbeAsync(string path)
    {
        var info = new ProcessStartInfo(Executable.Find("ffprobe.exe")!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-v", "error", "-show_streams", "-show_frames", "-of", "json", path }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        Assert.True(process.ExitCode == 0, await errors);
        return JsonDocument.Parse(await output);
    }
    private static async Task<short[]> DecodeAudioAsync(string path)
    {
        var info = new ProcessStartInfo(Executable.Find("ffmpeg.exe")!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-v", "error", "-nostdin", "-xerror", "-i", path, "-map", "0:a:0", "-f", "s16le", "-acodec", "pcm_s16le", "pipe:1" }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        using var pcm = new MemoryStream();
        var output = process.StandardOutput.BaseStream.CopyToAsync(pcm);
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        await output;
        Assert.True(process.ExitCode == 0, await errors);
        var bytes = pcm.ToArray();
        return Enumerable.Range(0, bytes.Length / 2).Select(i => System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2))).ToArray();
    }
    private static void WriteTestWav(string path)
    {
        const int rate = 48000, frames = rate / 4, bytes = frames * 4;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(bytes + 36); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4);
        writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes);
        for (var i = 0; i < frames; i++) { var sample = (short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 1000); writer.Write(sample); writer.Write(sample); }
    }
    private sealed class BorrowedMemory(Memory<byte> memory) : IMemoryOwner<byte>
    {
        public Memory<byte> Memory => memory;
        public void Dispose() { }
    }
    private sealed class TestDirectory : IDisposable
    {
        private readonly string? retainedRoot = Environment.GetEnvironmentVariable("CAPTUREFLOW_QA_OUTPUT_ROOT");
        public string Path { get; }
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(retainedRoot ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CaptureFlow FFmpeg tests"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose() { if (retainedRoot is null) Directory.Delete(Path, true); }
    }
}
public sealed class FfmpegTheoryAttribute : TheoryAttribute
{
    public FfmpegTheoryAttribute()
    {
        if (Executable.Find("ffmpeg.exe") is null || Executable.Find("ffprobe.exe") is null)
            Skip = "FFmpeg integration tests require ffmpeg.exe and ffprobe.exe on PATH.";
    }
}
internal static class Executable
{
    public static string? Find(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(p => Path.Combine(p.Trim('"'), name)).FirstOrDefault(File.Exists);
}
