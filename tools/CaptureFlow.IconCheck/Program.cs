using System.Security.Cryptography;
if (args.Length != 2) { Console.Error.WriteLine("Usage: IconCheck <exe> <approved.ico>"); return 2; }
try
{
    var icon = File.ReadAllBytes(args[1]);
    if (BitConverter.ToUInt16(icon, 0) != 0 || BitConverter.ToUInt16(icon, 2) != 1) throw new InvalidDataException("Invalid ICO header.");
    var count = BitConverter.ToUInt16(icon, 4); var frames = CaptureFlowIconResources.Frames(Path.GetFullPath(args[0]));
    if (frames.Length != count) throw new InvalidDataException("Incorrect native icon frame count.");
    for (var i = 0; i < count; i++)
    {
        var entry = 6 + 16 * i; var size = icon[entry] == 0 ? 256 : icon[entry];
        var length = BitConverter.ToInt32(icon, entry + 8); var offset = BitConverter.ToInt32(icon, entry + 12);
        if (!SHA256.HashData(icon.AsSpan(offset, length)).SequenceEqual(SHA256.HashData(frames[i]))) throw new InvalidDataException("EXE frame differs from approved ICO: " + size);
        Console.WriteLine($"PASS exact native icon frame: {size}x{size}, {length} bytes");
    }
    Console.WriteLine($"PASS one RT_GROUP_ICON and {count} byte-identical RT_ICON payloads: {Path.GetFullPath(args[0])}"); return 0;
}
catch (Exception ex) { Console.Error.WriteLine("FAIL resource inspection: " + ex); return 1; }
