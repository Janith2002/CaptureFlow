using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CaptureFlow.Core.Desktop;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Desktop.Services;
public sealed class FloatingToolbarController : IAsyncDisposable
{
    private readonly RecordingExperienceViewModel preferences;
    private readonly FloatingToolbarViewModel model;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private FloatingToolbarWindow? window;
    private HwndSource? source;
    private bool placing, disposed;
    private Task save = Task.CompletedTask;
    public FloatingToolbarController(RecordingExperienceViewModel preferences, FloatingToolbarViewModel model)
    { this.preferences = preferences; this.model = model; preferences.PropertyChanged += PreferencesChanged; saveTimer.Tick += SaveTick; }
    public void Update(bool sessionVisible)
    {
        if (disposed) return;
        if (!sessionVisible || !preferences.ShowToolbar) { window?.Hide(); return; }
        if (window is null)
        {
            window = new(model); window.HideRequested += HideRequested; window.LocationChanged += Moved;
            window.SourceInitialized += Initialized; window.SizeChanged += Resized;
            placing = true; window.Show(); Place(); placing = false;
        }
        else if (!window.IsVisible) { placing = true; window.Show(); Place(); placing = false; }
        window.Topmost = preferences.ToolbarTopmost;
    }
    private void Initialized(object? sender, EventArgs e)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window!).Handle); source.AddHook(Hook);
        model.CaptureNotice = WindowCaptureExclusion.TryExclude(source.Handle) ? "Windows accepted capture exclusion; output verification pending." : "Warning: toolbar may appear in screen recordings. Hide it or move it off the recorded display.";
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    { if (message is 0x7E or 0x2E0) window?.Dispatcher.BeginInvoke(Place); return IntPtr.Zero; }
    private void Place()
    {
        if (window is null || source is null || disposed) return;
        placing = true;
        try { DesktopWorkAreas.Place(source.Handle, ToolbarPlacement.Resolve(preferences.Anchor, DesktopWorkAreas.Enumerate(), 410, model.Collapsed ? 70 : 152)); }
        catch (Exception ex) { model.CaptureNotice = "Toolbar placement: " + ex.Message; }
        finally { placing = false; }
    }
    private void Resized(object sender, SizeChangedEventArgs e) { if (!placing) Place(); }
    private void Moved(object? sender, EventArgs e)
    {
        if (placing || source is null || disposed) return;
        try { preferences.Anchor = ToolbarPlacement.Capture(DesktopWorkAreas.Bounds(source.Handle), DesktopWorkAreas.Enumerate()); saveTimer.Stop(); saveTimer.Start(); }
        catch (Exception ex) { preferences.Message = "Could not save toolbar position: " + ex.Message; }
    }
    private void SaveTick(object? sender, EventArgs e) { saveTimer.Stop(); save = preferences.SaveAsync(); }
    private void PreferencesChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(preferences.ShowToolbar) && !preferences.ShowToolbar) window?.Hide(); if (e.PropertyName == nameof(preferences.ToolbarTopmost) && window is not null) window.Topmost = preferences.ToolbarTopmost; }
    private void HideRequested() { preferences.ShowToolbar = false; save = preferences.SaveAsync(); }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        preferences.PropertyChanged -= PreferencesChanged; saveTimer.Stop(); saveTimer.Tick -= SaveTick;
        if (window is not null) { window.HideRequested -= HideRequested; window.LocationChanged -= Moved; window.SourceInitialized -= Initialized; window.SizeChanged -= Resized; source?.RemoveHook(Hook); window.Close(); }
        await save; await preferences.SaveAsync(); window = null; source = null;
    }
}
