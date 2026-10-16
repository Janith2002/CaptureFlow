using NAudio.Wave;

namespace CaptureFlow.Infrastructure.Audio;

/// <summary>Finalizes the RIFF header before releasing the exclusive recording stream.</summary>
public static class WavRecordingFinalization
{
    public static void Close(WaveFileWriter writer, Stream output)
    {
        // NAudio 3 leaves caller-owned streams open. Validation must not race our own lock.
        try { writer.Dispose(); }
        finally { output.Dispose(); }
    }
}
