namespace CaptureFlow.Core.Audio;
public sealed record AudioLevels(bool Microphone, double Peak, double Rms, long Timestamp)
{
    public static AudioLevels Measure(bool microphone, ReadOnlySpan<float> samples, float gain, bool muted, long timestamp)
    {
        double peak = 0, sum = 0;
        foreach (var input in samples)
        {
            var value = muted || !float.IsFinite(input) ? 0 : input * Math.Clamp(gain, 0, 1);
            peak = Math.Max(peak, Math.Abs(value)); sum += value * value;
        }
        return new(microphone, peak, samples.IsEmpty ? 0 : Math.Sqrt(sum / samples.Length), timestamp);
    }
    public static double Decibels(double linear) => linear <= 0 ? -60 : Math.Clamp(20 * Math.Log10(linear), -60, 0);
}
