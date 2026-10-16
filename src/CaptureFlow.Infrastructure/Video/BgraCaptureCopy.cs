using System.Runtime.InteropServices;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;

/// <summary>Copies only the selected pixels from a mapped, padded GPU texture into a packed encoder frame.</summary>
public static class BgraCaptureCopy
{
    public static void Copy(IntPtr source, int rowPitch, VideoSize sourceSize, ScreenRegion? region, byte[] destination)
    {
        if (source == IntPtr.Zero || sourceSize.Width < 2 || sourceSize.Height < 2 || rowPitch < checked(sourceSize.Width * 4))
            throw new ArgumentException("Invalid mapped capture texture.");
        if (region is not null)
        {
            region.Validate();
            if (region.MonitorWidth != sourceSize.Width || region.MonitorHeight != sourceSize.Height)
                throw new IOException("Capture dimensions changed; select the region again.");
        }
        var width = region?.Width ?? sourceSize.Width; var height = region?.Height ?? sourceSize.Height;
        var bytesPerRow = checked(width * 4);
        if (destination.Length < checked(bytesPerRow * height)) throw new ArgumentException("The frame buffer is too small.");
        for (var row = 0; row < height; row++)
            Marshal.Copy(source + checked(rowPitch * (row + (region?.Y ?? 0)) + (region?.X ?? 0) * 4), destination, row * bytesPerRow, bytesPerRow);
    }
}
