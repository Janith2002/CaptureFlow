using System.Buffers;
using CaptureFlow.Application.Video;
using CaptureFlow.Core.Audio;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Tests;
public class VideoConfigurationTests
{
    private static VideoRecordingConfiguration Options => new(new(VideoSourceKind.Monitor, "monitor"), Path.GetTempPath());
    [Theory]
    [InlineData(24)] [InlineData(30)] [InlineData(60)]
    public void SupportedFrameRatesValidate(int fps) => (Options with { FramesPerSecond = fps }).Validate();
    [Theory]
    [InlineData(0)] [InlineData(25)] [InlineData(120)]
    public void UnsupportedFrameRatesFail(int fps) => Assert.Throws<ArgumentOutOfRangeException>(() => (Options with { FramesPerSecond = fps }).Validate());
    [Theory]
    [InlineData(1920, 1080)] [InlineData(3840, 2160)]
    public void EvenDimensionsValidate(int width, int height) => (Options with { OutputWidth = width, OutputHeight = height }).Validate();
    [Theory]
    [InlineData(0, 1080)] [InlineData(1921, 1080)] [InlineData(1920, -1)] [InlineData(1920, null)]
    public void InvalidDimensionsFail(int width, int? height) => Assert.Throws<ArgumentException>(() => (Options with { OutputWidth = width, OutputHeight = height }).Validate());
    [Theory]
    [InlineData((VideoSourceKind)99)]
    public void FutureSourcesAreExplicitlyRejected(VideoSourceKind kind) => Assert.Throws<NotSupportedException>(() => (Options with { Source = new(kind, "target") }).Validate());
    [Theory]
    [InlineData(AudioRecordingMode.MicrophoneOnly)] [InlineData(AudioRecordingMode.SystemAudioOnly)] [InlineData(AudioRecordingMode.MicrophoneAndSystemAudio)]
    public void MissingRequiredAudioDevicesFail(AudioRecordingMode mode) => Assert.Throws<ArgumentException>(() =>
        (Options with { Audio = new("", "unused", Mode: mode) }).Validate());
    [Fact]
    public void SystemOnlyDoesNotRequireMicrophone() => (Options with { Audio = new("", "unused", Mode: AudioRecordingMode.SystemAudioOnly, OutputDeviceId: "output") }).Validate();
    [Theory]
    [InlineData(-1)] [InlineData(52)]
    public void InvalidQualityFails(int quality) => Assert.Throws<ArgumentOutOfRangeException>(() => (Options with { Quality = quality }).Validate());
    [Fact]
    public void MissingFfmpegPathFails() => Assert.Throws<ArgumentException>(() => (Options with { FfmpegPath = " " }).Validate());
    [Fact]
    public void FilenameIsUniqueMp4AndUsesUtc()
    {
        var timestamp = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.FromHours(5.5));
        var first = VideoRecordingFileName.Create(timestamp, Guid.NewGuid());
        Assert.StartsWith("CaptureFlow_Screen_20261013_063000_000_", first);
        Assert.EndsWith(".mp4", first);
        Assert.NotEqual(first, VideoRecordingFileName.Create(timestamp, Guid.NewGuid()));
        Assert.Equal(first, Path.GetFileName(first));
    }
    [Fact]
    public void PausedTimelineRemovesGapAndCannotResumeAfterStop()
    {
        var timeline = new RecordingTimeline(); timeline.Start(); timeline.Pause();
        var first = timeline.Intervals[0];
        Assert.Equal(timeline.DurationAt(first.EndQpc!.Value), timeline.DurationAt(first.EndQpc.Value + 1_000_000));
        timeline.Resume(); var second = timeline.Intervals[1];
        Assert.Equal(timeline.DurationAt(first.EndQpc.Value), timeline.DurationAt(second.StartQpc));
        timeline.Stop(); var frozen = timeline.Duration; timeline.Resume();
        Assert.Equal(2, timeline.Intervals.Count); Assert.Equal(frozen, timeline.Duration);
    }
    [Fact]
    public async Task CompositionSharesPixelsAndReleasesOwnerOnlyAfterLastLease()
    {
        var owner = new Owner();
        var primary = new VideoFrame(owner, new(2, 2), TimeSpan.Zero);
        var composed = await new SingleSourceVideoCompositor().ComposeAsync(primary, [], CancellationToken.None);
        primary.Dispose(); Assert.Equal(0, owner.Disposals); Assert.Equal(16, composed.Pixels.Length);
        composed.Dispose(); composed.Dispose(); Assert.Equal(1, owner.Disposals);
        Assert.Throws<ObjectDisposedException>(() => composed.Pixels);
    }
    private sealed class Owner : IMemoryOwner<byte>
    {
        public Memory<byte> Memory { get; } = new byte[16];
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }
}
