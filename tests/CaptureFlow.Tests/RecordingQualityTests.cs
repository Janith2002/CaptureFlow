using CaptureFlow.Core.Video;
using CaptureFlow.Application.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;

public class RecordingQualityTests
{
    [Theory]
    [InlineData(VideoQualityPreset.Low, 3000)] [InlineData(VideoQualityPreset.Standard, 6000)]
    [InlineData(VideoQualityPreset.High, 10000)] [InlineData(VideoQualityPreset.Ultra, 16000)] [InlineData(VideoQualityPreset.Custom, 12000)]
    public void PresetsMapToDocumented1080p30Targets(VideoQualityPreset preset, int bitrate) => Assert.Equal(bitrate, new VideoQualityPreferences(Preset: preset).BitrateKbps(new(1920, 1080)));
    [Theory]
    [InlineData(1920, 1080, VideoOutputResolution.P720, 1280, 720)]
    [InlineData(1080, 1920, VideoOutputResolution.P720, 720, 1280)]
    [InlineData(3440, 1440, VideoOutputResolution.P1080, 1920, 802)]
    [InlineData(1600, 1200, VideoOutputResolution.P1080, 1440, 1080)]
    [InlineData(1921, 1081, VideoOutputResolution.Source, 1920, 1080)]
    [InlineData(3840, 2160, VideoOutputResolution.P2160, 3840, 2160)]
    [InlineData(2160, 3840, VideoOutputResolution.P2160, 2160, 3840)]
    public void OutputFitsAspectAndOrientationWithEvenDimensions(int width, int height, VideoOutputResolution resolution, int outputWidth, int outputHeight)
    {
        var output = new VideoQualityPreferences(Resolution: resolution).OutputSize(new(width, height));
        Assert.Equal(new(outputWidth, outputHeight), output);
        Assert.InRange(Math.Abs(output.Width / (double)output.Height - width / (double)height), 0, .01);
    }
    [Fact] public void FourKUnavailableForSmallerSources() => Assert.Throws<NotSupportedException>(() => new VideoQualityPreferences(Resolution: VideoOutputResolution.P2160).OutputSize(new(1920, 1080)));
    [Theory] [InlineData(24)] [InlineData(30)] [InlineData(60)]
    public void SupportedOutputFpsIsIndependentOfCameraNativeFps(int fps)
    {
        var options = new VideoRecordingConfiguration(new(VideoSourceKind.Webcam, "fixture", CameraFormat: new("reported", 640, 480, 15, 1, "BGRA")), "unused", fps, QualityPreferences: new(FramesPerSecond: fps));
        options.Validate(); Assert.Equal(fps, options.EffectiveFramesPerSecond);
    }
    [Theory] [InlineData(0)] [InlineData(25)] [InlineData(120)]
    public void UnsupportedOutputFpsRejected(int fps) => Assert.Throws<ArgumentOutOfRangeException>(() => new VideoQualityPreferences(FramesPerSecond: fps).Validate());
    [Theory] [InlineData(499)] [InlineData(100001)] [InlineData(int.MaxValue)]
    public void UnsafeCustomBitratesRejected(int bitrate) => Assert.Throws<ArgumentOutOfRangeException>(() => new VideoQualityPreferences(CustomBitrateKbps: bitrate).Validate());
    [Theory] [InlineData(500)] [InlineData(100000)]
    public void CustomBitrateBoundaryIsHonored(int bitrate) => Assert.Equal(bitrate, new VideoQualityPreferences(Preset: VideoQualityPreset.Custom, CustomBitrateKbps: bitrate).BitrateKbps(new(1920, 1080)));
    [Fact] public void ResolutionAndFpsIncreaseSuggestedBitrate()
    {
        var baseline = new VideoQualityPreferences();
        Assert.True(baseline.BitrateKbps(new(3840, 2160)) > baseline.BitrateKbps(new(1920, 1080)));
        Assert.True((baseline with { FramesPerSecond = 60 }).BitrateKbps(new(1920, 1080)) > baseline.BitrateKbps(new(1920, 1080)));
        Assert.Equal(76.44, baseline.EstimatedMegabytesPerMinute(new(1920, 1080), true), 2);
    }
    [Fact] public void ConfigurationRejectsConflictingResolutionAndFps()
    {
        var p = new VideoRecordingConfiguration(new(VideoSourceKind.Monitor, "fixture"), "unused", QualityPreferences: new());
        Assert.Throws<ArgumentException>(() => (p with { FramesPerSecond = 60 }).Validate());
        Assert.Throws<ArgumentException>(() => (p with { OutputWidth = 1280, OutputHeight = 720 }).Validate());
        Assert.Throws<ArgumentException>(() => new VideoQualityPreferences(Encoder: (VideoEncoderChoice)99).Validate());
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void AutoUsesOnlyVerifiedHardware(bool available)
    {
        var result = VideoEncoderPlan.Select(VideoEncoderChoice.Auto, [new(VideoEncoderChoice.Software, true, "tested"), new(VideoEncoderChoice.Nvenc, available, "actual test")]);
        Assert.Equal(available ? VideoEncoderChoice.Nvenc : VideoEncoderChoice.Software, result.Encoder);
    }
    [Theory] [InlineData(VideoEncoderChoice.Nvenc)] [InlineData(VideoEncoderChoice.QuickSync)] [InlineData(VideoEncoderChoice.Amf)]
    public void ExplicitUnsupportedHardwareNeverBecomesSoftware(VideoEncoderChoice choice) => Assert.Throws<NotSupportedException>(() => VideoEncoderPlan.Select(choice, [new(VideoEncoderChoice.Software, true, "available"), new(choice, false, "driver failure")]));
    [Fact] public async Task AutoExportFailureRetriesSoftwareOnce()
    {
        var calls = new List<VideoEncoderChoice>();
        var fellBack = await EncoderExportFallback.RunAsync(VideoEncoderChoice.Auto, VideoEncoderChoice.Nvenc, e =>
        { calls.Add(e); return e == VideoEncoderChoice.Software ? Task.CompletedTask : Task.FromException(new IOException("GPU lost")); }, default);
        Assert.True(fellBack); Assert.Equal(new[] { VideoEncoderChoice.Nvenc, VideoEncoderChoice.Software }, calls);
    }
    [Fact] public async Task ExplicitHardwareExportFailureDoesNotRetry()
    {
        var calls = 0;
        await Assert.ThrowsAsync<IOException>(() => EncoderExportFallback.RunAsync(VideoEncoderChoice.Nvenc, VideoEncoderChoice.Nvenc, _ => { calls++; throw new IOException("GPU failed"); }, default));
        Assert.Equal(1, calls);
    }
    [Fact] public async Task CancellationNeverStartsFallback()
    {
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EncoderExportFallback.RunAsync(VideoEncoderChoice.Auto, VideoEncoderChoice.Amf, _ => { calls++; throw new OperationCanceledException(); }, default));
        Assert.Equal(1, calls);
    }
    [Fact] public async Task FailedSoftwareFallbackIsReported()
    {
        await Assert.ThrowsAsync<IOException>(() => EncoderExportFallback.RunAsync(VideoEncoderChoice.Auto, VideoEncoderChoice.QuickSync, _ => throw new IOException("failed"), default));
    }
    [Fact] public async Task QualityPreferencesPersistAndCancelledWritesLeavePreviousFileIntact()
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-quality-test-" + Guid.NewGuid().ToString("N")); var file = Path.Combine(folder, "quality.json");
        try
        {
            var store = new JsonVideoQualityPreferencesStore(file); Assert.Equal(new VideoQualityPreferences(), await store.LoadAsync());
            var prefs = new VideoQualityPreferences(VideoQualityPreset.Custom, VideoOutputResolution.P1440, 60, 25000, VideoEncoderChoice.Amf);
            await store.SaveAsync(prefs); Assert.Equal(prefs, await new JsonVideoQualityPreferencesStore(file).LoadAsync());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(), new CancellationToken(true)));
            Assert.Equal(prefs, await store.LoadAsync()); Assert.Single(Directory.GetFiles(folder));
            await File.WriteAllTextAsync(file, "{"); await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.LoadAsync());
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Theory] [InlineData(VideoQualityPreset.Low)] [InlineData(VideoQualityPreset.Standard)] [InlineData(VideoQualityPreset.High)] [InlineData(VideoQualityPreset.Ultra)] [InlineData(VideoQualityPreset.Custom)]
    public void PauseResumeKeepsSameClockAndOutputRateForEveryPreset(VideoQualityPreset preset)
    {
        var p = new VideoQualityPreferences(Preset: preset); p.Validate();
        var timeline = new RecordingTimeline(); timeline.Start(); timeline.Pause();
        var before = timeline.Duration; var first = timeline.Intervals[0];
        Assert.Equal(before, timeline.DurationAt(first.EndQpc!.Value + System.Diagnostics.Stopwatch.Frequency * 5));
        timeline.Resume(); var next = timeline.Intervals[1];
        Assert.Equal(before, timeline.DurationAt(next.StartQpc)); timeline.Stop();
    }
    [Fact] public void QueueIsBoundedAndLagUsesHeldFramesInsteadOfFutureFrames()
    {
        Assert.Equal(1, VideoFramePacing.MaximumFramesInFlight); Assert.Equal(1, VideoFramePacing.MaximumRetainedFrames);
        Assert.Equal(60, VideoFramePacing.RequiredFrames(TimeSpan.FromSeconds(1), 60));
        Assert.True(VideoFramePacing.HoldPrevious(20, 30)); Assert.False(VideoFramePacing.HoldPrevious(29, 30));
        Assert.Throws<IOException>(() => VideoFramePacing.ValidateLag(0, 151, 30));
    }
    [Fact] public void CodecParametersHaveBoundedThreadsBitrateAndHardwareSpecificPixelFormats()
    {
        foreach (var encoder in new[] { VideoEncoderChoice.Software, VideoEncoderChoice.Nvenc, VideoEncoderChoice.QuickSync, VideoEncoderChoice.Amf })
        {
            var args = FfmpegVideoEncodingBackend.CodecArguments(encoder, 6000, "veryfast");
            Assert.Contains(FfmpegVideoEncodingBackend.CodecName(encoder), args); Assert.Contains("6000k", args);
            Assert.Contains(encoder == VideoEncoderChoice.Software ? "yuv420p" : "nv12", args);
            Assert.InRange(int.Parse(args[Array.IndexOf(args, "-threads") + 1]), 1, 8);
        }
    }
}
