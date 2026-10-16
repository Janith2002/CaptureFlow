using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using CaptureFlow.Core.Desktop;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.Services;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Infrastructure.Desktop;

internal static class ShortcutTrayQa
{
    public static int Run()
    {
        var settings = new ShortcutPreferences(true, true, Enum.GetValues<ShortcutAction>()
            .Select((a, i) => new ShortcutBinding(a, ShortcutModifiers.Control | ShortcutModifiers.Shift, 132 + i)).ToArray());
        new JsonShortcutPreferencesStore(Environment.GetEnvironmentVariable("CAPTUREFLOW_SHORTCUT_PREFERENCES")!).SaveAsync(settings).GetAwaiter().GetResult();
        // Test native registration/conflict/cleanup without intercepting any recording shortcut.
        using (var first = new HwndSource(new HwndSourceParameters("CaptureFlow QA hotkeys 1") { Width = 1, Height = 1 }))
        using (var second = new HwndSource(new HwndSourceParameters("CaptureFlow QA hotkeys 2") { Width = 1, Height = 1 }))
        using (var owner = new WindowsGlobalShortcuts(first))
        using (var competitor = new WindowsGlobalShortcuts(second))
        {
            try
            {
                owner.Register(settings.Bindings[0]);
                try { competitor.Register(settings.Bindings[0]); throw new Exception("Conflict was not detected."); }
                catch (InvalidOperationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 1409 })
                { Console.WriteLine("PASS native RegisterHotKey conflict with error 1409 and useful diagnostic."); }
                owner.Unregister(ShortcutAction.StartStop); competitor.Register(settings.Bindings[0]); competitor.Unregister(ShortcutAction.StartStop);
                Console.WriteLine("PASS native unregister releases combination for another window.");
            }
            catch (Exception ex) { Console.WriteLine("BLOCKED/FAIL native shortcut check: " + ex); return 2; }
        }
        var app = new App(); app.InitializeComponent();
        var started = false; var failed = false; var completed = false;
        app.DispatcherUnhandledException += (_, e) => { Console.WriteLine("FAIL unhandled: " + e.Exception); e.Handled = true; failed = true; app.Shutdown(1); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += async (_, _) =>
        {
            if (started || app.MainWindow is not MainWindow window) return;
            started = true; timer.Stop();
            Window? other = null;
            try
            {
                await window.DesktopInitialization;
                var dashboard = (DashboardViewModel)window.DataContext;
                Check(dashboard.Shortcuts.Message.StartsWith("All four"), dashboard.Shortcuts.Message);
                var integration = (WindowDesktopIntegration)typeof(MainWindow).GetField("desktopIntegration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                Check(integration.TrayAvailable, "Windows notification area is unavailable.");
                Console.WriteLine("PASS real application startup, branded tray icon registered and isolated custom hotkeys applied.");
                dashboard.SelectedPage = "Settings"; dashboard.SelectedSettingsTab = 4;
                await Task.Delay(200);
                dashboard.Shortcuts.StartStop = dashboard.Shortcuts.PauseResume;
                await dashboard.Shortcuts.SaveCommand.ExecuteAsync(null);
                Check(dashboard.Shortcuts.Message.Contains("different key combination"), "Duplicate settings were not rejected.");
                dashboard.Shortcuts.StartStop = settings.Bindings[0].Display;
                await dashboard.Shortcuts.SaveCommand.ExecuteAsync(null);
                Check(dashboard.Shortcuts.Message.StartsWith("Saved."), dashboard.Shortcuts.Message);
                Console.WriteLine("PASS real Settings duplicate validation, save and re-registration.");
                var tray = typeof(WindowDesktopIntegration).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(integration)!;
                var menu = (ContextMenu)tray.GetType().GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tray)!;
                Check(PostMessage(new WindowInteropHelper(window).Handle, 0x8001, new IntPtr(1), new IntPtr(0x205)), "Tray callback could not be posted.");
                await WaitAsync(() => menu.IsOpen, "native tray context menu callback");
                Check(menu.Style is not null, "Tray dark style was not resolved."); menu.IsOpen = false;
                Console.WriteLine("PASS native tray callback opens the themed WPF context menu.");
                var menuItems = menu.Items.OfType<MenuItem>().ToArray();
                Check(menuItems.Select(i => (string)i.Header).SequenceEqual(new[] { "Open CaptureFlow", "Start Recording", "Pause/Resume Recording", "Stop Recording", "Open Recordings", "Exit" }), "Tray menu actions differ.");
                menuItems[4].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(dashboard.IsRecordings && window.IsVisible, "Tray Open Recordings did not route to the existing library.");
                Console.WriteLine("PASS tray Open Recordings routing; no capture started.");
                window.WindowState = WindowState.Minimized;
                Check(!window.IsVisible, "Minimize did not hide to tray.");
                menuItems[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(window.IsVisible && window.WindowState != WindowState.Minimized, "Tray Open did not restore.");
                window.Close(); Check(!window.IsVisible, "Close preference did not hide to tray.");
                menuItems[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Console.WriteLine("PASS minimize-to-tray, close-to-tray and restore preserve the running window.");
                other = new Window { Title = "CaptureFlow QA — keyboard focus target", Width = 360, Height = 150, Content = new TextBlock { Text = "Safe QA focus window. No capture is started.", Margin = new Thickness(20) } };
                other.Show(); other.Activate(); await Task.Delay(300);
                var otherHandle = new WindowInteropHelper(other).Handle;
                Check(GetForegroundWindow() == otherHandle, "BLOCKED: QA window cannot obtain interactive keyboard focus.");
                SendCombination(135); // Ctrl+Shift+F24 is only Show/Hide; never send the recording combinations.
                await WaitAsync(() => !window.IsVisible, "unfocused show/hide hotkey");
                other.Activate(); await Task.Delay(100); SendCombination(135);
                await WaitAsync(() => window.IsVisible, "hidden restore hotkey");
                window.WindowState = WindowState.Minimized; other.Activate(); await Task.Delay(100); SendCombination(135);
                await WaitAsync(() => window.IsVisible && window.WindowState != WindowState.Minimized, "minimized restore hotkey");
                Console.WriteLine("PASS real WM_HOTKEY from Windows SendInput while unfocused, hidden and minimized; physical keyboard presses remain manual.");
                dashboard.Shortcuts.Enabled = false; await dashboard.Shortcuts.SaveCommand.ExecuteAsync(null);
                using (var source = new HwndSource(new HwndSourceParameters("QA disabled hotkey check") { Width = 1, Height = 1 }))
                using (var released = new WindowsGlobalShortcuts(source))
                    foreach (var binding in settings.Bindings) { released.Register(binding); released.Unregister(binding.Action); }
                Console.WriteLine("PASS disabling shortcuts releases all four native registrations.");
                other.Close(); other = null;
                dashboard.Shortcuts.Enabled = true; await dashboard.Shortcuts.SaveCommand.ExecuteAsync(null);
                window.Closed += (_, _) =>
                {
                    try
                    {
                        Check(!integration.TrayAvailable, "Tray icon was not disposed.");
                        using var source = new HwndSource(new HwndSourceParameters("QA exit cleanup check") { Width = 1, Height = 1 });
                        using var released = new WindowsGlobalShortcuts(source);
                        foreach (var binding in settings.Bindings) { released.Register(binding); released.Unregister(binding.Action); }
                        Console.WriteLine("PASS explicit tray Exit really closes application, removes tray icon and unregisters all shortcuts."); completed = true;
                    }
                    catch (Exception ex) { failed = true; Console.WriteLine("FAIL cleanup: " + ex); }
                };
                menuItems[5].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            catch (Exception ex)
            {
                failed = true; Console.WriteLine("FAIL/BLOCKED desktop integration: " + ex);
                other?.Close(); window.RequestExit();
            }
        };
        timer.Start(); app.Run();
        return failed || !completed ? 1 : 0;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task WaitAsync(Func<bool> condition, string label)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(40);
        Check(condition(), "BLOCKED/FAIL Windows input delivery: " + label);
    }
    private static void SendCombination(ushort key)
    {
        Input[] inputs = [Key(17, false), Key(16, false), Key(key, false), Key(key, true), Key(16, true), Key(17, true)];
        Check(SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length, "BLOCKED: Windows did not accept the QA keyboard input.");
    }
    private static Input Key(ushort key, bool up) => new() { Type = 1, Union = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = up ? 2u : 0 } } };
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
