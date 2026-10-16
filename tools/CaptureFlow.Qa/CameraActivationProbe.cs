using System.Runtime.InteropServices;

// Activates the static enumeration factory only. Never creates MediaCapture or a reader.
internal static class CameraActivationProbe
{
    internal static int Check()
    {
        const string name = "Windows.Media.Capture.Frames.MediaFrameSourceGroup";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var handle));
        nint factory = 0;
        try
        {
            var iid = new Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
            return RoGetActivationFactory(handle, in iid, out factory);
        }
        finally { if (factory != 0) Marshal.Release(factory); WindowsDeleteString(handle); }
    }
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint handle);
    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint handle);
    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint handle, in Guid iid, out nint factory);
}
