using System.ComponentModel;
using System.Runtime.InteropServices;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;

/// <summary>Draws one owned Windows cursor after effects using a bounded GDI patch, including monochrome AND/XOR cursors.</summary>
internal sealed class WindowsCursorRenderer : IDisposable
{
    private const int Limit = 256;
    private readonly byte[] patch = new byte[Limit * Limit * 4];
    private nint dc, bitmap, previousBitmap, bits, cursor, original;
    private int width, height, hotX, hotY;
    private bool disposed;
    public void Draw(Span<byte> pixels, VideoSize size, MousePoint position, nint handle)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (handle == 0 || !MouseCoordinates.Contains(position, size)) return;
        if (original != handle) UpdateCursor(handle);
        EnsureSurface();
        var cursorLeft = (int)Math.Round(position.X) - hotX; var cursorTop = (int)Math.Round(position.Y) - hotY;
        var left = Math.Max(0, cursorLeft); var top = Math.Max(0, cursorTop);
        var copyWidth = Math.Min(size.Width, cursorLeft + width) - left; var copyHeight = Math.Min(size.Height, cursorTop + height) - top;
        if (copyWidth <= 0 || copyHeight <= 0) return;
        for (var y = 0; y < copyHeight; y++) pixels.Slice(((top + y) * size.Width + left) * 4, copyWidth * 4).CopyTo(patch.AsSpan(y * Limit * 4));
        Marshal.Copy(patch, 0, bits, copyHeight * Limit * 4);
        if (!DrawIconEx(dc, cursorLeft - left, cursorTop - top, cursor, width, height, 0, 0, 3)) throw Error("Cannot render the Windows cursor.");
        if (!GdiFlush()) throw Error("Cannot synchronize cursor rendering.");
        Marshal.Copy(bits, patch, 0, copyHeight * Limit * 4);
        for (var y = 0; y < copyHeight; y++)
        {
            var row = pixels.Slice(((top + y) * size.Width + left) * 4, copyWidth * 4); patch.AsSpan(y * Limit * 4, row.Length).CopyTo(row);
            for (var x = 3; x < row.Length; x += 4) row[x] = 255;
        }
    }
    private void UpdateCursor(nint handle)
    {
        var copy = CopyIcon(handle); if (copy == 0) throw Error("Cannot copy the current Windows cursor.");
        try
        {
            if (!GetIconInfo(copy, out var info)) throw Error("Cannot read Windows cursor shape.");
            try
            {
                if (GetObject(info.Color != 0 ? info.Color : info.Mask, Marshal.SizeOf<Bitmap>(), out var shape) == 0) throw Error("Cannot read cursor dimensions.");
                var newHeight = info.Color == 0 ? shape.Height / 2 : shape.Height;
                if (shape.Width is < 1 or > Limit || newHeight is < 1 or > Limit || info.HotX >= shape.Width || info.HotY >= newHeight)
                    throw new NotSupportedException("Mouse effects support Windows cursors up to 256 × 256 pixels. Disable effects for this cursor size.");
                if (cursor != 0) DestroyIcon(cursor);
                cursor = copy; copy = 0; original = handle; width = shape.Width; height = newHeight; hotX = (int)info.HotX; hotY = (int)info.HotY;
            }
            finally { if (info.Color != 0) DeleteObject(info.Color); if (info.Mask != 0) DeleteObject(info.Mask); }
        }
        finally { if (copy != 0) DestroyIcon(copy); }
    }
    private void EnsureSurface()
    {
        if (dc != 0) return;
        dc = CreateCompatibleDC(0); if (dc == 0) throw Error("Cannot create cursor rendering context.");
        var info = new BitmapInfo { Header = new() { Size = (uint)Marshal.SizeOf<BitmapHeader>(), Width = Limit, Height = -Limit, Planes = 1, BitCount = 32 } };
        bitmap = CreateDIBSection(dc, ref info, 0, out bits, 0, 0);
        if (bitmap == 0) { DeleteDC(dc); dc = 0; throw Error("Cannot allocate cursor rendering surface."); }
        previousBitmap = SelectObject(dc, bitmap);
        if (previousBitmap == 0 || previousBitmap == -1) { DeleteObject(bitmap); DeleteDC(dc); bitmap = dc = 0; throw Error("Cannot select cursor surface."); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (dc != 0) { SelectObject(dc, previousBitmap); if (bitmap != 0) DeleteObject(bitmap); DeleteDC(dc); }
        if (cursor != 0) DestroyIcon(cursor);
        dc = bitmap = cursor = original = 0;
    }
    private static Win32Exception Error(string message) => new(Marshal.GetLastWin32Error(), message);
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { [MarshalAs(UnmanagedType.Bool)] public bool Icon; public uint HotX, HotY; public nint Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XPels, YPels; public uint ColorsUsed, ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapHeader Header; public uint Color; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)] private static extern int GetObject(nint obj, int size, out Bitmap bitmap);
}
