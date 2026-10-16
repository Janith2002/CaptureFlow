using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;

internal static class WindowSourceQa
{
    public static int RunFixture(bool regionScene = false)
    {
        var app = new Application();
        var scene = new Grid { Background = Brushes.DarkSlateBlue };
        var marker = new Border { Background = Brushes.Lime, Width = 160, Height = 96, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        scene.Children.Add(new TextBlock { Text = "CaptureFlow QA — generated content only", Foreground = Brushes.White, FontSize = 24, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Top });
        scene.Children.Add(new Border { Background = Brushes.Red, Width = 200, Height = 136, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        scene.Children.Add(marker);
        var window = new Window { Title = "CaptureFlow QA synthetic window", Width = 640, Height = 480, Background = Brushes.DarkSlateBlue, Content = scene };
        if (regionScene)
        {
            window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize; window.Topmost = true;
            window.Loaded += (_, _) =>
            {
                var area = CaptureFlow.Desktop.Services.DesktopWorkAreas.Enumerate(true).Single(a => a.Primary);
                CaptureFlow.Desktop.Services.DesktopWorkAreas.Place(new System.Windows.Interop.WindowInteropHelper(window).Handle, new(area.Left, area.Top, area.Width, area.Height));
                var dpi = VisualTreeHelper.GetDpi(window).DpiScaleX; marker.Width = 160 / dpi; marker.Height = 96 / dpi;
            };
        }
        app.Run(window);
        return 0;
    }
    public static async Task VerifyAsync()
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add(typeof(WindowSourceQa).Assembly.Location); start.ArgumentList.Add("--window-source-fixture");
        using var fixture = Process.Start(start) ?? throw new IOException("Cannot launch controlled QA window.");
        var discovery = new WindowsApplicationWindowDiscovery();
        ApplicationWindowDescriptor? selected = null;
        try
        {
            for (var i = 0; i < 30 && selected is null; i++)
            { await Task.Delay(200); selected = (await discovery.GetWindowsAsync()).FirstOrDefault(w => w.ProcessId == fixture.Id); }
            if (selected is null) throw new IOException("BLOCKED: synthetic application window could not be enumerated.");
            var options = new VideoSourceOptions(VideoSourceKind.Window, selected.Id, Window: selected);
            // Construct and release the HWND-specific WGC item/frame pool; deliberately never call StartAsync.
            await using (var source = await new WindowsGraphicsCaptureSourceFactory().CreateAsync(options, default))
            { if (source.Size.Width < 2 || source.Size.Height < 2) throw new IOException("Invalid HWND capture-item dimensions."); }
            await using (var unstartedEffects = await new WindowsGraphicsCaptureSourceFactory().CreateAsync(options with { MouseEffects = new(Highlight: true, ClickEffects: true) }, default))
            { if (unstartedEffects.Size.Width < 2) throw new IOException("Invalid effect source."); }
            Console.WriteLine("PASS mouse-enabled WGC source disposed before StartAsync; no listener or capture started.");
            var handle = new IntPtr(selected.Handle); ShowWindow(handle, 6); await Task.Delay(200);
            if ((await discovery.GetWindowsAsync()).Any(w => w.ProcessId == fixture.Id)) throw new IOException("Minimized fixture was not filtered out.");
            try { await using var unexpected = await new WindowsGraphicsCaptureSourceFactory().CreateAsync(options, default); throw new Exception("Minimized window was accepted."); }
            catch (IOException) { }
            ShowWindow(handle, 9); await Task.Delay(200);
            SetWindowPos(handle, IntPtr.Zero, 0, 0, selected.Width + 240, selected.Height + 240, 0x16); await Task.Delay(200);
            var resized = (await discovery.GetWindowsAsync()).Single(w => w.ProcessId == fixture.Id);
            if (resized.Id != selected.Id || resized.Width == selected.Width && resized.Height == selected.Height) throw new IOException("Window resize metadata or identity is incorrect.");
            try { await using var unexpected = await new WindowsGraphicsCaptureSourceFactory().CreateAsync(options, default); throw new Exception("Stale pre-resize dimensions were accepted."); }
            catch (IOException) { }
            await using (var refreshed = await new WindowsGraphicsCaptureSourceFactory().CreateAsync(new(VideoSourceKind.Window, resized.Id, Window: resized), default))
            { if (refreshed.Size.Width < 2 || refreshed.Size.Height < 2) throw new IOException("Refreshed window capture dimensions are invalid."); }
            PostMessage(handle, 0x10, IntPtr.Zero, IntPtr.Zero); await fixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if ((await discovery.GetWindowsAsync()).Any(w => w.ProcessId == fixture.Id)) throw new IOException("Closed window remained in discovery.");
            try { await using var unexpected = await new WindowsGraphicsCaptureSourceFactory().CreateAsync(options, default); throw new Exception("Destroyed window was accepted."); }
            catch (IOException) { }
            Console.WriteLine("PASS controlled native window discovery, HWND capture-item creation/disposal, minimize filtering, resize metadata and closure rejection. WGC StartAsync never called.");
        }
        finally
        {
            if (!fixture.HasExited) { fixture.Kill(entireProcessTree: true); await fixture.WaitForExitAsync(); }
        }
    }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
