using CaptureFlow.Desktop.Services;
namespace CaptureFlow.Desktop;

internal static class EntryPoint
{
    [STAThread]
    public static int Main()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        StartupDiagnostics.Record("process-entry", new { executable = Environment.ProcessPath, startedUtc = process.StartTime.ToUniversalTime(), interactive = Environment.UserInteractive });
        try
        {
            var app = new App();
            app.InitializeComponent();
            StartupDiagnostics.Record("application-initialized");
            return app.Run();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Record("entry-failed", exception: ex);
            System.Windows.MessageBox.Show($"CaptureFlow could not start. {ex.Message}\n\nDiagnostics: {StartupDiagnostics.LogPath}", "CaptureFlow", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return 1;
        }
    }
}
