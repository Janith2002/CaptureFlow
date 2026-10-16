namespace CaptureFlow.Infrastructure.Audio;
/// <summary>Estimates small physical endpoint clock drift from WASAPI frame positions and QPC timestamps.</summary>
public sealed class AudioClockTracker(int nominalRate)
{
    private long? anchorPosition;
    private double anchorQpc;
    public double EffectiveRate { get; private set; } = nominalRate;
    public void Reset() { anchorPosition = null; EffectiveRate = nominalRate; }
    public double? Observe(long devicePosition, double qpc)
    {
        if (devicePosition < 0 || !double.IsFinite(qpc)) return null;
        if (anchorPosition is null || devicePosition < anchorPosition || qpc <= anchorQpc)
        { anchorPosition = devicePosition; anchorQpc = qpc; return null; }
        var seconds = (qpc - anchorQpc) / AudioTimelineMixer.QpcUnitsPerSecond;
        if (seconds < 1) return null;
        var measured = (devicePosition - anchorPosition.Value) / seconds;
        anchorPosition = devicePosition; anchorQpc = qpc;
        // Large changes indicate a discontinuity, not clock drift; never distort the stream to follow them.
        if (Math.Abs(measured / nominalRate - 1) > .02) return null;
        EffectiveRate += (measured - EffectiveRate) * .1;
        return EffectiveRate;
    }
}
