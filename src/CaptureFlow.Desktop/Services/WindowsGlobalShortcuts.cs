using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using CaptureFlow.Core.Desktop;
namespace CaptureFlow.Desktop.Services;

public sealed class WindowsGlobalShortcuts : IGlobalShortcutRegistrar
{
    private readonly HwndSource source;
    private readonly HashSet<ShortcutAction> registered = [];
    private bool disposed;
    public event Action<ShortcutAction>? Invoked;
    public WindowsGlobalShortcuts(HwndSource source) { this.source = source; source.AddHook(Hook); }
    private static int Id(ShortcutAction action) => 0x4300 + (int)action;
    public void Register(ShortcutBinding binding)
    {
        ObjectDisposedException.ThrowIf(disposed, this); binding.Validate();
        if (!RegisterHotKey(source.Handle, Id(binding.Action), (uint)binding.Modifiers | 0x4000, (uint)binding.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Could not register {binding.Display} ({binding.Action}). " +
                (error == 1409 ? "Another application already uses this shortcut. Choose a different combination in Settings." : new Win32Exception(error).Message), new Win32Exception(error));
        }
        registered.Add(binding.Action);
    }
    public void Unregister(ShortcutAction action) { if (registered.Remove(action)) UnregisterHotKey(source.Handle, Id(action)); }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x312)
        {
            var action = (ShortcutAction)(wParam.ToInt32() - 0x4300);
            if (registered.Contains(action)) { handled = true; Invoked?.Invoke(action); }
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (disposed) return;
        foreach (var action in registered.ToArray()) Unregister(action);
        source.RemoveHook(Hook); disposed = true;
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
