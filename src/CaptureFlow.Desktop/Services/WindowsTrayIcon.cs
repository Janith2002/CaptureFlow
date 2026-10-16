using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace CaptureFlow.Desktop.Services;

/// <summary>Shell notification icon and WPF menu; no keyboard hooks or recording ownership.</summary>
public sealed class WindowsTrayIcon : IDisposable
{
    private const int Callback = 0x8001;
    private readonly HwndSource source;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly IntPtr idleIcon;
    private readonly IntPtr activeIcon;
    private readonly ContextMenu menu;
    private readonly Action open;
    private NotifyIconData data;
    private bool disposed;
    public bool Available { get; private set; }
    public WindowsTrayIcon(HwndSource source, ContextMenu menu, Action open)
    {
        this.source = source; this.menu = menu; this.open = open;
        idleIcon = CreateIcon(false);
        try { activeIcon = CreateIcon(true); }
        catch { DestroyIcon(idleIcon); throw; }
        data = new() { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = source.Handle, Id = 1,
            Flags = 1 | 2 | 4, CallbackMessage = Callback, Icon = idleIcon, Tip = "CaptureFlow — Ready", Info = "", InfoTitle = "" };
        source.AddHook(Hook); Add();
        menu.Closed += MenuClosed;
    }
    private void MenuClosed(object? sender, RoutedEventArgs e) { if (!disposed) PostMessage(source.Handle, 0, IntPtr.Zero, IntPtr.Zero); }
    private void Add() => Available = Shell_NotifyIcon(0, ref data);
    public void Update(string status, bool recording)
    {
        if (disposed) return;
        data.Tip = ("CaptureFlow — " + status)[..Math.Min(127, ("CaptureFlow — " + status).Length)];
        data.Icon = recording ? activeIcon : idleIcon;
        if (!Shell_NotifyIcon(1, ref data)) Add();
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == taskbarCreated && !disposed) Add();
        if (message == Callback && !disposed)
        {
            if (lParam.ToInt32() == 0x203) { open(); handled = true; }
            else if (lParam.ToInt32() is 0x205 or 0x7B)
            {
                SetForegroundWindow(source.Handle);
                menu.Placement = PlacementMode.MousePoint; menu.IsOpen = true; handled = true;
            }
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        menu.IsOpen = false; menu.Closed -= MenuClosed; Shell_NotifyIcon(2, ref data); source.RemoveHook(Hook);
        DestroyIcon(idleIcon); DestroyIcon(activeIcon); Available = false;
    }
    private static IntPtr CreateIcon(bool active)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var image = ApplicationBranding.LoadIconFrame(32);
            drawing.DrawImage(image, new Rect(0, 0, 32, 32));
            if (active) drawing.DrawEllipse(Brushes.Red, new Pen(Brushes.White, 1), new Point(26, 26), 5, 5);
        }
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream(); encoder.Save(png);
        var bytes = png.ToArray();
        var icon = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x30000, 32, 32, 0);
        if (icon == IntPtr.Zero) throw new InvalidOperationException("Could not create the CaptureFlow tray icon.");
        return icon;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr CreateIconFromResourceEx(byte[] bytes, uint length, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
}
