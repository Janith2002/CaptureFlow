using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;

public class FfmpegRecordingQualityTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [FfmpegTheory]
    [InlineData(VideoQualityPreset.Low, 24)] [InlineData(VideoQualityPreset.Low, 30)] [InlineData(VideoQualityPreset.Low, 60)]
    [InlineData(VideoQualityPreset.Standard, 24)] [InlineData(VideoQualityPreset.Standard, 30)] [InlineData(VideoQualityPreset.Standard, 60)]
    [InlineData(VideoQualityPreset.High, 24)] [InlineData(VideoQualityPreset.High, 30)] [InlineData(VideoQualityPreset.High, 60)]
    [InlineData(VideoQualityPreset.Ultra, 24)] [InlineData(VideoQualityPreset.Ultra, 30)] [InlineData(VideoQualityPreset.Ultra, 60)]
    [InlineData(VideoQualityPreset.Custom, 24)] [InlineData(VideoQualityPreset.Custom, 30)] [InlineData(VideoQualityPreset.Custom, 60)]
    public async Task SoftwarePresetsProduceDecodableMp4AtRequestedRate(VideoQualityPreset preset, int fps)
    {
        using var directory = new Fixture(); var backend = new FfmpegVideoEncodingBackend();
        var config = directory.Configuration(new(preset, FramesPerSecond: fps, Encoder: VideoEncoderChoice.Software));
        var intermediate = Path.Combine(directory.Path, "intermediate.mp4"); var final = Path.Combine(directory.Path, "final.mp4");
        await EncodeAsync(backend, config, intermediate, new(128, 72));
        await backend.MuxAsync(config, intermediate, null, final, TimeSpan.FromSeconds(.5), default);
        await backend.ValidateAsync(final, config.FfmpegPath, default);
        using var probe = await Probe(final);
        var stream = probe.RootElement.GetProperty("streams")[0];
        Assert.Equal("h264", stream.GetProperty("codec_name").GetString());
        Assert.Equal($"{fps}/1", stream.GetProperty("avg_frame_rate").GetString());
        Assert.Equal(128, stream.GetProperty("width").GetInt32()); Assert.Equal(72, stream.GetProperty("height").GetInt32());
        Assert.InRange(double.Parse(stream.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture), .49, .51);
        Assert.True(File.Exists(intermediate));
    }
    [FfmpegTheory] [InlineData(320, 240, 960, 720)] [InlineData(240, 320, 720, 960)]
    public async Task ScalingKeepsSourceAspectAndOrientation(int width, int height, int outputWidth, int outputHeight)
    {
        using var directory = new Fixture(); var backend = new FfmpegVideoEncodingBackend();
        var config = directory.Configuration(new(Resolution: VideoOutputResolution.P720, Encoder: VideoEncoderChoice.Software));
        var file = Path.Combine(directory.Path, "scaled.mp4"); await EncodeAsync(backend, config, file, new(width, height));
        await backend.ValidateAsync(file, config.FfmpegPath, default); using var probe = await Probe(file);
        Assert.Equal(outputWidth, probe.RootElement.GetProperty("streams")[0].GetProperty("width").GetInt32());
        Assert.Equal(outputHeight, probe.RootElement.GetProperty("streams")[0].GetProperty("height").GetInt32());
    }
    [FfmpegTheory] [InlineData(true)] [InlineData(false)]
    public async Task RealAutoFallbackRetainsFramesAndExplicitHardwareFailureNeverPublishes(bool auto)
    {
        using var directory = new Fixture(); var process = new FailingHardwareExport(); var backend = new FfmpegVideoEncodingBackend(new ClaimedHardware(), process);
        var config = directory.Configuration(new(Encoder: auto ? VideoEncoderChoice.Auto : VideoEncoderChoice.Nvenc));
        var intermediate = Path.Combine(directory.Path, "retained.mp4"); var final = Path.Combine(directory.Path, "final.mp4");
        await EncodeAsync(backend, config, intermediate, new(128, 72));
        if (auto)
        {
            await backend.MuxAsync(config, intermediate, null, final, TimeSpan.FromSeconds(.5), default);
            await backend.ValidateAsync(final, config.FfmpegPath, default);
            Assert.Contains("software export succeeded", backend.GetEncodingStatus(intermediate)); Assert.Equal(2, process.Calls);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => backend.MuxAsync(config, intermediate, null, final, TimeSpan.FromSeconds(.5), default));
            Assert.False(File.Exists(final)); Assert.Equal(1, process.Calls);
        }
        Assert.True(File.Exists(intermediate));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.export-*.mp4"));
        await backend.ValidateAsync(intermediate, Executable.Find("ffmpeg.exe")!, default);
    }
    [FfmpegTheory] [InlineData(VideoEncoderChoice.Nvenc)] [InlineData(VideoEncoderChoice.QuickSync)] [InlineData(VideoEncoderChoice.Amf)]
    public async Task AvailableHardwareActuallyEncodesAndDecodesBeforeItIsReportedSupported(VideoEncoderChoice choice)
    {
        using var directory = new Fixture(); var probe = new FfmpegEncoderCapabilities();
        var results = await probe.ProbeAsync(Executable.Find("ffmpeg.exe")!, new(128, 72), 30, 6000, default);
        var capability = results.Single(c => c.Encoder == choice);
        output.WriteLine($"{choice}: {(capability.Available ? "PASS real encode/decode" : "UNAVAILABLE: " + capability.Detail)}");
        if (!capability.Available) return; // Not a hardware PASS; explicit availability result is written above.
        var backend = new FfmpegVideoEncodingBackend(new FixedCapabilities(results)); var config = directory.Configuration(new(Encoder: choice));
        var intermediate = Path.Combine(directory.Path, "hardware-intermediate.mp4"); var final = Path.Combine(directory.Path, "hardware-final.mp4");
        await EncodeAsync(backend, config, intermediate, new(128, 72));
        await backend.MuxAsync(config, intermediate, null, final, TimeSpan.FromSeconds(.5), default);
        await backend.ValidateAsync(final, config.FfmpegPath, default);
        Assert.Contains(choice.ToString(), backend.GetEncodingStatus(intermediate));
    }
    [Fact] public async Task MissingRuntimeIsNotReportedAsSupported()
    {
        var result = await new FfmpegEncoderCapabilities().ProbeAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"), new(128, 72), 30, 6000, default);
        Assert.All(result, c => { Assert.False(c.Available); Assert.Contains("Cannot start FFmpeg", c.Detail); });
    }
    [Fact] public async Task CancelledCapabilityProbePropagatesCancellation()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FfmpegEncoderCapabilities().ProbeAsync("unused", new(128, 72), 30, 6000, new CancellationToken(true)));
    }
    [FfmpegTheory] [InlineData(30)]
    public async Task CancellingRunningProbeReleasesProcessAndRemovesItsOwnedFixtures(int fps)
    {
        using var directory = new Fixture(); var root = Path.Combine(directory.Path, "probes");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FfmpegEncoderCapabilities(root).ProbeAsync(Executable.Find("ffmpeg.exe")!, new(1280, 720), fps, 6000, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10)));
        if (Directory.Exists(root)) Assert.Empty(Directory.GetFileSystemEntries(root));
    }
    [FfmpegTheory] [InlineData(VideoSourceKind.Monitor)] [InlineData(VideoSourceKind.Webcam)] [InlineData(VideoSourceKind.ScreenAndWebcam)]
    public async Task QualityExportKeepsAudioAndVideoDurationAcrossSourceKinds(VideoSourceKind kind)
    {
        using var directory = new Fixture(); var backend = new FfmpegVideoEncodingBackend();
        var config = directory.Configuration(new(FramesPerSecond: 24, Encoder: VideoEncoderChoice.Software));
        if (kind != VideoSourceKind.Monitor) config = config with { Source = new(kind, "synthetic-source", CameraFormat: new("reported-15fps", 128, 72, 15, 1, "BGRA"), CameraId: kind == VideoSourceKind.ScreenAndWebcam ? "synthetic-camera" : null) };
        var intermediate = Path.Combine(directory.Path, "video.mp4"); var wav = Path.Combine(directory.Path, "tone.wav"); var final = Path.Combine(directory.Path, "with-audio.mp4");
        using (var writer = new NAudio.Wave.WaveFileWriter(wav, new NAudio.Wave.WaveFormat(48000, 16, 1)))
            for (var i = 0; i < 24000; i++) writer.WriteSample((float)(Math.Sin(i * 440 * 2 * Math.PI / 48000) * .1));
        await EncodeAsync(backend, config, intermediate, new(128, 72));
        await backend.MuxAsync(config, intermediate, wav, final, TimeSpan.FromSeconds(.5), default); await backend.ValidateAsync(final, config.FfmpegPath, default);
        using var probe = await Probe(final); var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = Assert.Single(streams, s => s.GetProperty("codec_type").GetString() == "video");
        var audio = Assert.Single(streams, s => s.GetProperty("codec_type").GetString() == "audio");
        Assert.Equal("24/1", video.GetProperty("avg_frame_rate").GetString()); Assert.Equal("aac", audio.GetProperty("codec_name").GetString());
        foreach (var stream in streams) Assert.InRange(double.Parse(stream.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture), .49, .53);
        // This is structural timestamp/duration verification, not perceptual lip-sync validation.
    }
    private static async Task EncodeAsync(FfmpegVideoEncodingBackend backend, VideoRecordingConfiguration config, string path, VideoSize size)
    {
        await using var encoder = await backend.CreateAsync(config, size, path, default);
        for (var i = 0; i < config.FramesPerSecond / 2; i++)
        {
            using var frame = OverlayCompositorTests.Frame(size.Width, size.Height, (byte)(i * 7), 180, 40);
            await encoder.WriteFrameAsync(frame, default);
        }
        await encoder.FinishAsync(default);
    }
    private static async Task<JsonDocument> Probe(string path)
    {
        var start = new ProcessStartInfo(Executable.Find("ffprobe.exe")!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-show_streams", "-of", "json", path }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var json = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        Assert.True(process.ExitCode == 0, await errors); return JsonDocument.Parse(await json);
    }
    private sealed class ClaimedHardware : IVideoEncoderCapabilities
    {
        public Task<IReadOnlyList<VideoEncoderCapability>> ProbeAsync(string executable, VideoSize size, int fps, int bitrateKbps, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<VideoEncoderCapability>>([new(VideoEncoderChoice.Software, true, "fixture"), new(VideoEncoderChoice.Nvenc, true, "fixture")]);
    }
    private sealed class FailingHardwareExport : IVideoExportProcess
    {
        public int Calls;
        public Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
        {
            Calls++; var args = arguments.ToArray(); var codec = Array.IndexOf(args, "-c:v") + 1;
            if (args[codec] != "libx264") args[codec] = "captureflow_nonexistent_fixture_encoder";
            // A real FFmpeg process fails on this test codec; the real software fallback then exports and decodes the retained frames.
            return new FfmpegVideoExportProcess().RunAsync(executable, args, token);
        }
    }
    private sealed class FixedCapabilities(IReadOnlyList<VideoEncoderCapability> results) : IVideoEncoderCapabilities
    {
        public Task<IReadOnlyList<VideoEncoderCapability>> ProbeAsync(string executable, VideoSize size, int fps, int bitrateKbps, CancellationToken token) => Task.FromResult(results);
    }
    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CaptureFlow quality FFmpeg", Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Path);
        public VideoRecordingConfiguration Configuration(VideoQualityPreferences p) => new(new(VideoSourceKind.Monitor, "synthetic-fixture"), Path, p.FramesPerSecond, FfmpegPath: Executable.Find("ffmpeg.exe")!, QualityPreferences: p);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
