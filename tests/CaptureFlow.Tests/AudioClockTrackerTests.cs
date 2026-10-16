using CaptureFlow.Infrastructure.Audio;
namespace CaptureFlow.Tests;
public class AudioClockTrackerTests
{
    [Fact]
    public void SmallEndpointDriftIsEstimatedGraduallyWithoutLargeRateJumps()
    {
        var clock = new AudioClockTracker(48000);
        clock.Observe(0, 0);
        for (var i = 1; i <= 100; i++) clock.Observe(i * 48005L, i * AudioTimelineMixer.QpcUnitsPerSecond);
        Assert.InRange(clock.EffectiveRate, 48004.99, 48005.01);
    }
    [Fact]
    public void DiscontinuousPositionsDoNotChangeTheResamplingRate()
    {
        var clock = new AudioClockTracker(48000);
        clock.Observe(0, 0);
        Assert.Null(clock.Observe(96000, AudioTimelineMixer.QpcUnitsPerSecond));
        Assert.Equal(48000d, clock.EffectiveRate);
        clock.Reset(); Assert.Equal(48000d, clock.EffectiveRate);
    }
    [Fact]
    public void InvalidOrBackwardTimestampsAreIgnored()
    {
        var clock = new AudioClockTracker(48000);
        Assert.Null(clock.Observe(-1, 0)); Assert.Null(clock.Observe(0, double.NaN));
        clock.Observe(1000, 10000);
        Assert.Null(clock.Observe(0, 0)); Assert.Equal(48000d, clock.EffectiveRate);
    }
}
