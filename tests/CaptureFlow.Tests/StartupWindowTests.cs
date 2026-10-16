using System.Windows;
using CaptureFlow.Desktop.Services;
namespace CaptureFlow.Tests;

public class StartupWindowTests
{
    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Minimized)]
    [InlineData(WindowState.Maximized)]
    public Task StartupPresentsHiddenWindowInTaskbarWithNormalState(WindowState initialState) => DesktopRecordingCommandTests.Sta(async () =>
    {
        var window = new Window { Width = 200, Height = 100, ShowInTaskbar = false, WindowState = initialState, Visibility = Visibility.Collapsed };
        try
        {
            StartupWindowPresentation.Show(window);
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.True(window.IsVisible);
            Assert.True(window.ShowInTaskbar);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Null(window.Owner);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task StartupReusesOneWindowAndDoesNotPreventLaterTrayHiding() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var window = new Window { Width = 200, Height = 100 };
        var loaded = 0; window.Loaded += (_, _) => loaded++;
        try
        {
            StartupWindowPresentation.Show(window);
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            StartupWindowPresentation.Show(window);
            Assert.Equal(handle, new System.Windows.Interop.WindowInteropHelper(window).Handle);
            Assert.Equal(1, loaded);
            window.Hide();
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });
}
