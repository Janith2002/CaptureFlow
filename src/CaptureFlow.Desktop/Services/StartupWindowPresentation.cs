using System.Windows;
namespace CaptureFlow.Desktop.Services;

public static class StartupWindowPresentation
{
    // Startup only. Later minimize/hide-to-tray behavior remains owned by desktop integration.
    public static void Show(Window window)
    {
        window.Owner = null;
        window.ShowInTaskbar = true;
        window.WindowState = WindowState.Normal;
        if (!window.IsVisible) window.Show();
    }
}
