using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace CaptureFlow.Qa;

internal static class BrandingQa
{
    private static CaptureFlow.Desktop.Services.WindowsTrayIcon? inspectedTray;
    public static void VerifyTrayDisposed()
    {
        if (inspectedTray is null) throw new InvalidOperationException("Tray resources were not inspected.");
        var disposed = (bool)typeof(CaptureFlow.Desktop.Services.WindowsTrayIcon).GetField("disposed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(inspectedTray)!;
        if (!disposed || inspectedTray.Available) throw new InvalidOperationException("Tray owner did not dispose on shutdown.");
        inspectedTray.Dispose(); Console.WriteLine("PASS tray owner disposed on shutdown; repeated disposal is safe.");
    }
    [System.Runtime.InteropServices.DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr value);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static void VerifyWindow(Window window)
    {
        if (window.Icon is not BitmapFrame { Decoder: IconBitmapDecoder decoder } || decoder.Frames.Count != 7)
            throw new InvalidOperationException("Window did not load the approved multi-resolution ICO decoder.");
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (SendMessage(handle, 0x7F, new IntPtr(1), IntPtr.Zero) == IntPtr.Zero || SendMessage(handle, 0x7F, new IntPtr(0), IntPtr.Zero) == IntPtr.Zero)
            throw new InvalidOperationException("Window native large/small icon handles are missing.");
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(GetCurrentProcessExplicitAppUserModelID(out var identity));
        try
        {
            var value = System.Runtime.InteropServices.Marshal.PtrToStringUni(identity);
            if (value != CaptureFlow.Desktop.Services.ApplicationBranding.AppUserModelId) throw new InvalidOperationException("Unexpected taskbar process identity: " + value);
            Console.WriteLine("PASS window ICO decoder (7 frames), native small/large HICON and explicit taskbar identity: " + value);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(identity); }
        var integration = (CaptureFlow.Desktop.Services.WindowDesktopIntegration?)typeof(CaptureFlow.Desktop.MainWindow)
            .GetField("desktopIntegration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window);
        if (integration is null) throw new InvalidOperationException("Desktop integration has not finished initialization.");
        var tray = (CaptureFlow.Desktop.Services.WindowsTrayIcon?)typeof(CaptureFlow.Desktop.Services.WindowDesktopIntegration)
            .GetField("tray", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(integration);
        if (tray is null) throw new InvalidOperationException("Tray icon construction failed.");
        if (inspectedTray is not null && !ReferenceEquals(inspectedTray, tray)) throw new InvalidOperationException("Navigation created a duplicate tray owner.");
        if (inspectedTray is null)
        {
            inspectedTray = tray; var visual = new DrawingVisual(); using (var draw = visual.RenderOpen())
            {
                draw.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 100, 48)); var index = 0;
                foreach (var field in new[] { "idleIcon", "activeIcon" })
                {
                    var icon = (IntPtr)typeof(CaptureFlow.Desktop.Services.WindowsTrayIcon).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(tray)!;
                    if (icon == IntPtr.Zero) throw new InvalidOperationException("Tray native icon handle missing.");
                    var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    draw.DrawImage(bitmap, new Rect(8 + index++ * 48, 8, 32, 32));
                }
            }
            var bitmapOutput = new RenderTargetBitmap(100, 48, 96, 96, PixelFormats.Pbgra32); bitmapOutput.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmapOutput));
            var path = "artifacts/qa/icon-hotfix-tray-" + Guid.NewGuid().ToString("N") + ".png"; using var output = File.Create(path); encoder.Save(output);
            Console.WriteLine("PASS actual idle/active native tray icons created; render: " + Path.GetFullPath(path));
        }
        if (!integration.TrayAvailable) Console.WriteLine("BLOCKED shell tray registration: notification area unavailable. Window/icon checks remain separate.");
        else Console.WriteLine("PASS single existing desktop integration owns the approved ICO-based tray registration.");
    }
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, [System.Runtime.InteropServices.Out] IntPtr[] large, [System.Runtime.InteropServices.Out] IntPtr[] small, uint count);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
    private delegate bool ResourceCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "EnumResourceNamesW", SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, ResourceCallback callback, IntPtr parameter);
    public static int Verify(string executable)
    {
        var large = new IntPtr[1]; var small = new IntPtr[1];
        try
        {
            var module = LoadLibraryEx(Path.GetFullPath(executable), IntPtr.Zero, 2); var groups = 0;
            try { EnumResourceNames(module, new IntPtr(14), (m, t, n, p) => { groups++; return true; }, IntPtr.Zero); }
            finally { if (module != IntPtr.Zero) FreeLibrary(module); }
            var count = ExtractIconEx(Path.GetFullPath(executable), 0, large, small, 1);
            if (groups < 1 || count < 1 || large[0] == IntPtr.Zero || small[0] == IntPtr.Zero)
                throw new IOException($"Native executable icon extraction failed: groups={groups}, extracted={count}, large={large[0]}, small={small[0]}.");
            var visual = new DrawingVisual(); using (var draw = visual.RenderOpen())
            {
                draw.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 160, 80));
                for (var i = 0; i < 2; i++)
                {
                    var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(i == 0 ? small[0] : large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    draw.DrawImage(bitmap, new Rect(16 + i * 64, 16, bitmap.PixelWidth, bitmap.PixelHeight));
                    Console.WriteLine($"PASS native embedded icon: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
                }
            }
            var output = new RenderTargetBitmap(160, 80, 96, 96, PixelFormats.Pbgra32); output.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(output));
            Directory.CreateDirectory("artifacts/qa"); var path = "artifacts/qa/native-icons-" + Guid.NewGuid().ToString("N") + ".png";
            using var stream = File.Create(path); encoder.Save(stream); Console.WriteLine("Native icon render: " + Path.GetFullPath(path)); return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL branding: " + ex.Message); return 1; }
        finally { foreach (var icon in large.Concat(small).Where(p => p != IntPtr.Zero)) DestroyIcon(icon); }
    }
    public static int Generate()
    {
        var root = Path.GetFullPath("src/CaptureFlow.Desktop/Assets"); Directory.CreateDirectory(root);
        int[] sizes = [16, 24, 32, 48, 64, 128, 256]; var frames = sizes.Select(Render).ToArray();
        using var ico = new MemoryStream(); using var writer = new BinaryWriter(ico);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] % 256)); writer.Write((byte)(sizes[i] % 256)); writer.Write((ushort)0);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame); writer.Flush();
        File.WriteAllBytes(Path.Combine(root, "CaptureFlow.ico"), ico.ToArray()); File.WriteAllBytes(Path.Combine(root, "CaptureFlow.png"), Render(1024));
        var sheet = new DrawingVisual(); using (var drawing = sheet.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(16, 19, 28)), null, new Rect(0, 0, 840, 170));
            for (var i = 0; i < sizes.Length; i++)
            {
                using var stream = new MemoryStream(frames[i]); var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                drawing.DrawImage(image, new Rect(i * 120 + 12, 12, Math.Min(sizes[i], 96), Math.Min(sizes[i], 96)));
                drawing.DrawText(new FormattedText(sizes[i].ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, Brushes.White, 1), new Point(i * 120 + 12, 125));
            }
        }
        var preview = new RenderTargetBitmap(840, 170, 96, 96, PixelFormats.Pbgra32); preview.Render(sheet); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
        Directory.CreateDirectory("artifacts/qa"); using (var output = File.Create("artifacts/qa/branding-preview.png")) encoder.Save(output);
        Console.WriteLine("Generated approved S geometry: ICO 16/24/32/48/64/128/256; PNG 1024; contact sheet in artifacts/qa. No security settings changed."); return 0;
    }
    private static byte[] Render(int size)
    {
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
        {
            drawing.PushTransform(new ScaleTransform(size / 32d, size / 32d));
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(119, 117, 255)), null, new Rect(1, 1, 30, 30), 7, 7);
            drawing.PushTransform(new TranslateTransform(5, 4));
            drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(243, 245, 255)), 3.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
                Geometry.Parse("M 20,2 L 7,2 C 0,2 0,12 7,12 L 15,12 C 22,12 22,22 15,22 L 2,22"));
            drawing.Pop(); drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var memory = new MemoryStream(); encoder.Save(memory); return memory.ToArray();
    }
}
