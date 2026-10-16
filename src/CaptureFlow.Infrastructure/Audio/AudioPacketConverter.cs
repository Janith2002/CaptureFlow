using System.Buffers.Binary;
using NAudio.Dsp;
using NAudio.Wave;
namespace CaptureFlow.Infrastructure.Audio;
/// <summary>Decodes native PCM/float endpoint packets, downmixes to stereo, and continuously resamples with NAudio WDL.</summary>
public sealed class AudioPacketConverter
{
    private readonly WaveFormat format;
    private readonly WaveFormatEncoding encoding;
    private readonly int targetRate;
    private double effectiveInputRate;
    private readonly WdlResampler resampler = new();
    private readonly (float Left, float Right)[] weights;
    public AudioPacketConverter(WaveFormat format, int targetRate)
    {
        this.format = format; this.targetRate = targetRate; effectiveInputRate = format.SampleRate;
        encoding = ResolveEncoding(format);
        if (format.Channels < 1 || format.Channels > 32 || format.SampleRate <= 0 || targetRate <= 0 ||
            encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat) ||
            (encoding == WaveFormatEncoding.Pcm && format.BitsPerSample is not (8 or 16 or 24 or 32)) ||
            (encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample is not (32 or 64)))
            throw new NotSupportedException($"Unsupported endpoint audio format: {format}");
        weights = CreateWeights(format);
        resampler.SetMode(true, 2, false);
        resampler.SetFilterParms();
        resampler.SetFeedMode(true);
        resampler.SetRates(format.SampleRate, targetRate);
    }
    public void Reset() { resampler.Reset(); SetInputRate(format.SampleRate); }
    public void SetInputRate(double inputRate)
    {
        if (!double.IsFinite(inputRate) || Math.Abs(inputRate / format.SampleRate - 1) > .02) throw new ArgumentOutOfRangeException(nameof(inputRate));
        effectiveInputRate = inputRate;
        resampler.SetRates(inputRate, targetRate);
    }
    public float[] Convert(ReadOnlySpan<byte> packet, bool silent = false)
    {
        if (packet.Length % format.BlockAlign != 0) throw new ArgumentException("Audio packets must contain whole frames.");
        var frames = packet.Length / format.BlockAlign;
        if (frames == 0) return [];
        var stereo = new float[frames * 2];
        if (!silent)
        {
            var sampleBytes = format.BitsPerSample / 8;
            for (var frame = 0; frame < frames; frame++)
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var sample = Decode(packet.Slice(frame * format.BlockAlign + channel * sampleBytes, sampleBytes));
                stereo[frame * 2] += sample * weights[channel].Left;
                stereo[frame * 2 + 1] += sample * weights[channel].Right;
            }
        }
        if (effectiveInputRate == targetRate) return stereo;
        var prepared = resampler.ResamplePrepare(frames, 2, out Span<float> input);
        if (prepared < frames) throw new InvalidOperationException("The resampler did not accept the full capture packet.");
        stereo.CopyTo(input);
        var outputFrames = (int)Math.Ceiling(frames * (double)targetRate / effectiveInputRate) + 64;
        var output = new float[outputFrames * 2];
        var produced = resampler.ResampleOut(output, frames, outputFrames, 2);
        Array.Resize(ref output, produced * 2);
        return output;
    }
    private static WaveFormatEncoding ResolveEncoding(WaveFormat format)
    {
        var standard = format.AsStandardWaveFormat().Encoding;
        if (standard != WaveFormatEncoding.Extensible) return standard;
        Guid subtype;
        if (format is WaveFormatExtensible ext) subtype = ext.SubFormat;
        else
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) format.Serialize(writer);
            var bytes = stream.ToArray();
            if (bytes.Length < 44) return standard;
            subtype = new Guid(bytes.AsSpan(28, 16));
        }
        return subtype == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT ? WaveFormatEncoding.IeeeFloat :
            subtype == AudioMediaSubtypes.MEDIASUBTYPE_PCM ? WaveFormatEncoding.Pcm : standard;
    }    private float Decode(ReadOnlySpan<byte> bytes)
    {
        float value;
        if (encoding == WaveFormatEncoding.IeeeFloat)
            value = format.BitsPerSample == 32 ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes)) : (float)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes));
        else value = format.BitsPerSample switch
        {
            8 => (bytes[0] - 128) / 128f,
            16 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f,
            24 => ((bytes[0] | bytes[1] << 8 | bytes[2] << 16) << 8 >> 8) / 8388608f,
            32 => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648f,
            _ => throw new NotSupportedException()
        };
        return float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
    }
    private static (float Left, float Right)[] CreateWeights(WaveFormat format)
    {
        if (format.Channels == 1) return [(1, 1)];
        if (format.Channels == 2) return [(1, 0), (0, 1)];
        var mask = format is WaveFormatExtensible ext ? (int)ext.ChannelMask : 0;
        if (mask == 0 && format.Encoding == WaveFormatEncoding.Extensible)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true)) format.Serialize(writer);
            if (bytes.Length >= 28) mask = BinaryPrimitives.ReadInt32LittleEndian(bytes.ToArray().AsSpan(24, 4));
        }
        if (mask == 0) mask = format.Channels switch { 3 => 7, 4 => 51, 5 => 55, 6 => 63, 7 => 319, 8 => 1599, _ => 0 };
        var result = new (float Left, float Right)[format.Channels];
        var speakers = Enumerable.Range(0, 32).Where(bit => (mask & (1 << bit)) != 0).ToArray();
        for (var i = 0; i < result.Length; i++)
        {
            var speaker = i < speakers.Length ? speakers[i] : -1;
            result[i] = speaker switch { 0 or 6 => (1, 0), 1 or 7 => (0, 1), 4 or 9 => (.707f, 0), 5 or 10 => (0, .707f), 2 => (.707f, .707f), _ => (.5f, .5f) };
        }
        var left = Math.Max(1, result.Sum(w => w.Left)); var right = Math.Max(1, result.Sum(w => w.Right));
        for (var i = 0; i < result.Length; i++) result[i] = (result[i].Left / left, result[i].Right / right);
        return result;
    }
}




