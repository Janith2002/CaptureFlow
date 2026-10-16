using CaptureFlow.Core.Video;
namespace CaptureFlow.Tests;

public class ReleaseTimingTests
{
    [Theory] [InlineData(24)] [InlineData(30)] [InlineData(60)]
    public void LongRecordingClockExcludesPauseAndRemainsStableAfterStop(int fps)
    {
        double clock = 8_000_000_000_000;
        var timeline = new RecordingTimeline(() => clock); timeline.Start();
        clock += TimeSpan.FromHours(6).Ticks; timeline.Pause();
        clock += TimeSpan.FromMinutes(47).Ticks;
        Assert.Equal(TimeSpan.FromHours(6), timeline.Duration);
        timeline.Resume(); clock += TimeSpan.FromHours(18).Ticks; timeline.Stop(); clock += TimeSpan.FromDays(10).Ticks;
        Assert.Equal(TimeSpan.FromHours(24), timeline.Duration);
        Assert.Equal(86400L * fps, VideoFramePacing.RequiredFrames(timeline.Duration, fps));
        Assert.Throws<IOException>(() => VideoFramePacing.ValidateLag(0, 5L * fps + 1, fps));
    }
    [Fact] public void FrameCountsBeyondInt32RemainLongAndDoNotOverflow()
    { Assert.Equal(3_784_320_000L, VideoFramePacing.RequiredFrames(TimeSpan.FromDays(730), 60)); }
}
