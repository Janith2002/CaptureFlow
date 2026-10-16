using System.Buffers.Binary;
using System.Text;
namespace CaptureFlow.Infrastructure.Recovery;

public static class MediaSalvage
{
    /// <summary>Copies complete top-level fragments, never mutating the interrupted original.</summary>
    public static async Task CopyFragmentsAsync(string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        long end = 0; bool movie = false, fragment = false; var header = new byte[16];
        while (input.Position + 8 <= input.Length)
        {
            token.ThrowIfCancellationRequested(); var start = input.Position;
            await input.ReadExactlyAsync(header.AsMemory(0, 8), token);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.ASCII.GetString(header, 4, 4);
            if (size == 1) { if (input.Position + 8 > input.Length) break; await input.ReadExactlyAsync(header.AsMemory(8, 8), token); var wide = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8)); if (wide > long.MaxValue) break; size = (long)wide; }
            if (size < input.Position - start || size > input.Length - start) break;
            if (type == "moov") movie = true;
            if (type == "moof") fragment = true;
            if (type == "mdat" && fragment && movie) { end = start + size; fragment = false; }
            input.Position = start + size;
        }
        if (end == 0) throw new IOException("No complete MP4 fragments found. An interrupted ordinary MP4 without a finalized movie header cannot be recovered safely.");
        input.Position = 0; await CopyPrefixAsync(input, destination, end, token);
    }
    public static async Task RepairWaveAsync(string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(input, Encoding.ASCII, true);
        if (input.Length < 44 || new string(reader.ReadChars(4)) != "RIFF") throw new IOException("Not a supported RIFF WAV file.");
        _ = reader.ReadUInt32(); if (new string(reader.ReadChars(4)) != "WAVE") throw new IOException("Not a WAV file.");
        byte[]? format = null; long offset = 0, length = 0;
        for (var chunks = 0; chunks < 32 && input.Position + 8 <= input.Length; chunks++)
        {
            token.ThrowIfCancellationRequested(); var type = new string(reader.ReadChars(4)); var size = reader.ReadUInt32();
            if (type == "data") { offset = input.Position; length = size == 0 || size == uint.MaxValue ? input.Length - offset : Math.Min(size, input.Length - offset); break; }
            if (size > 1048576 || size > input.Length - input.Position) throw new IOException("Invalid WAV header.");
            if (type == "fmt ") format = reader.ReadBytes((int)size); else input.Position += size;
            if ((size & 1) != 0) input.Position++;
        }
        if (format is null || format.Length < 16 || BinaryPrimitives.ReadUInt16LittleEndian(format) != 1 || BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14)) != 16)
            throw new IOException("Recovery supports PCM 16-bit WAV intermediates only.");
        var align = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(12));
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(2));
        if (align == 0 || align != channels * 2 || channels is < 1 or > 2 || offset == 0) throw new IOException("Invalid PCM WAV format.");
        length -= length % align;
        if (length == 0 || length > uint.MaxValue - 36) throw new IOException("No complete samples, or WAV exceeds supported RIFF size.");
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (var writer = new BinaryWriter(output, Encoding.ASCII, true))
        { writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write((uint)(length + 36)); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write(format, 0, 16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write((uint)length); }
        input.Position = offset; await CopyAsync(input, output, length, token);
    }
    private static async Task CopyPrefixAsync(Stream input, string destination, long length, CancellationToken token)
    { await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write); await CopyAsync(input, output, length, token); }
    private static async Task CopyAsync(Stream input, Stream output, long remaining, CancellationToken token)
    {
        var buffer = new byte[65536];
        while (remaining > 0) { var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token); if (read == 0) throw new EndOfStreamException(); await output.WriteAsync(buffer.AsMemory(0, read), token); remaining -= read; }
    }
}
