using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CaptureFlow.Core.Editing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CaptureFlow.Desktop.ViewModels;

public partial class EditorViewModel
{
    private readonly Dispatcher exportDispatcher = Dispatcher.CurrentDispatcher;
    private readonly Stopwatch exportClock = new();
    private DispatcherTimer? exportTimer;
    private int exportGeneration;
    [ObservableProperty] private bool exportStatusVisible;
    [ObservableProperty] private string exportState = "";
    [ObservableProperty] private string exportJobFormat = "";
    [ObservableProperty] private string exportOutputPath = "";
    [ObservableProperty] private string exportDetails = "";
    [ObservableProperty] private string exportTechnicalDetails = "";
    [ObservableProperty] private string exportElapsed = "00:00";
    [ObservableProperty] private string exportFileSize = "";
    [ObservableProperty] private double? exportFraction;
    public bool ExportActive => ExportState is "Preparing" or "Exporting" or "Finalizing" or "Cancelling";
    public bool ExportIndeterminate => ExportActive && (ExportFraction is null || ExportState != "Exporting");
    public double ExportProgressValue => ExportState == "Completed" ? 1 : Math.Min(.99, ExportFraction ?? 0);
    public string ExportFilename => string.IsNullOrEmpty(ExportOutputPath) ? "Allocating a new output filename" : Path.GetFileName(ExportOutputPath);
    public string ExportHeading => ExportState switch { "Completed" => "Export completed", "Failed" => "Export failed", "Cancelled" => "Export cancelled", _ => ExportState };
    public string ExportProgressText => ExportState == "Completed" ? "100%" : ExportState == "Exporting" && ExportFraction is { } fraction ? $"{Math.Min(99, fraction * 100):0}%" : "";
    private bool CanDismissExport => ExportStatusVisible && !ExportActive;
    private bool CanOpenExport => ExportState == "Completed" && File.Exists(ExportOutputPath);
    private bool CanRetryExport => ExportState == "Failed" && CanRender;

    private void RefreshExportPresentation()
    {
        foreach (var name in new[] { nameof(ExportActive), nameof(ExportIndeterminate), nameof(ExportProgressValue), nameof(ExportFilename), nameof(ExportHeading), nameof(ExportProgressText) }) OnPropertyChanged(name);
        DismissExportStatusCommand.NotifyCanExecuteChanged(); RetryExportCommand.NotifyCanExecuteChanged();
        CancelExportCommand.NotifyCanExecuteChanged();
        OpenExportFileCommand.NotifyCanExecuteChanged(); OpenExportFolderCommand.NotifyCanExecuteChanged();
    }
    private void StopExportClock() { exportTimer?.Stop(); exportClock.Stop(); }
    private void UpdateExportElapsed() => ExportElapsed = exportClock.Elapsed.ToString(@"hh\:mm\:ss");

    private Task RunExportAsync() => RunAsync(async token =>
    {
        var generation = ++exportGeneration;
        var snapshot = Project; var settings = ExportSettings; var folder = OutputFolder; var ffmpeg = FfmpegPath;
        ExportStatusVisible = true; ExportState = "Preparing"; ExportJobFormat = settings.Format;
        ExportOutputPath = ""; ExportFileSize = ""; ExportFraction = null;
        ExportDetails = "Preparing a new file; source media will remain unchanged."; ExportTechnicalDetails = "";
        exportClock.Restart(); UpdateExportElapsed();
        exportTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => UpdateExportElapsed(), exportDispatcher);
        exportTimer.Start(); RefreshExportPresentation();
        Trace.TraceInformation("Editor export {0}: Preparing {1}; folder={2}", generation, settings.Format, folder);
        var status = new ExportProgressSink<EditorExportProgress>(update =>
        {
            void Apply()
            {
                if (generation != exportGeneration || !ExportActive || disposed || ExportState == "Cancelling") return;
                if (update.Stage is not ("Preparing" or "Exporting" or "Finalizing")) return;
                ExportState = update.Stage;
                if (update.OutputPath is not null) ExportOutputPath = update.OutputPath;
                ExportFraction = update.Fraction is { } fraction && double.IsFinite(fraction) && fraction is >= 0 and <= 1 ? fraction : null;
                ExportDetails = update.Stage == "Finalizing" ? "Validating streams, duration and decoded output before publishing." : "Writing a new export; source media is unchanged.";
                RefreshExportPresentation();
            }
            if (exportDispatcher.CheckAccess()) Apply(); else exportDispatcher.BeginInvoke(Apply);
        });
        try
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Choose an output folder before exporting.");
            // Snapshot everything used by the worker; only progress callbacks touch the Dispatcher.
            var path = backend is IEditorExportBackend detailed
                ? await Task.Run(() => detailed.RenderExportAsync(snapshot, settings, folder, ffmpeg, status, token), token)
                : await backend.RenderAsync(snapshot, settings, folder, ffmpeg,
                    new ExportProgressSink<double>(v => status.Report(new("Exporting", Fraction: double.IsFinite(snapshot.Duration) && snapshot.Duration > 0 ? v : null))), token);
            token.ThrowIfCancellationRequested();
            var info = await Task.Run(() =>
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length == 0) throw new IOException("The exporter returned without a nonempty output file. No completion was confirmed.");
                if (!string.Equals(file.Extension, "." + settings.Format, StringComparison.OrdinalIgnoreCase)) throw new IOException("The exported filename does not match the requested format.");
                return (file.FullName, file.Length);
            }, token);
            token.ThrowIfCancellationRequested();
            ExportOutputPath = info.FullName; ExportFileSize = $"{info.Length:N0} bytes";
            ExportState = "Completed"; ExportFraction = 1;
            ExportDetails = "Export completed and output validated. Your original media is unchanged.";
            Message = "Completed and validated: " + ExportOutputPath;
            Trace.TraceInformation("Editor export {0}: Completed {1}; bytes={2}", generation, ExportOutputPath, info.Length);
            // A library refresh notification cannot turn an already validated file into a failed export.
            try { Exported?.Invoke(ExportOutputPath); }
            catch (Exception ex) { ExportTechnicalDetails = "Output was saved, but library notification failed: " + ex; Trace.TraceError("{0}", ExportTechnicalDetails); }
        }
        catch (OperationCanceledException)
        {
            ExportState = "Cancelled"; ExportFraction = null;
            ExportDetails = "Export cancelled; no completed output is confirmed. Owned recovery files are retained; no user files were deleted.";
            Trace.TraceInformation("Editor export {0}: Cancelled", generation); throw;
        }
        catch (Exception ex)
        {
            ExportState = "Failed"; ExportFraction = null;
            ExportDetails = ex.Message + " Retry creates a new file after you correct the reported problem.";
            ExportTechnicalDetails = ex.ToString(); Trace.TraceError("Editor export {0}: {1}", generation, ex); throw;
        }
        finally
        {
            StopExportClock(); UpdateExportElapsed(); RefreshExportPresentation();
            // RunAsync clears IsBusy after this callback unwinds.
            _ = exportDispatcher.BeginInvoke(RefreshExportPresentation);
        }
    });

    [RelayCommand(CanExecute = nameof(CanDismissExport))]
    private void DismissExportStatus() { ExportStatusVisible = false; RefreshExportPresentation(); }
    [RelayCommand(CanExecute = nameof(ExportActive))]
    private void CancelExport() { ExportState = "Cancelling"; ExportFraction = null; Cancel(); RefreshExportPresentation(); }
    [RelayCommand(CanExecute = nameof(CanRetryExport))]
    private Task RetryExportAsync() => ExportCommand.ExecuteAsync(null);
    [RelayCommand(CanExecute = nameof(CanOpenExport))]
    private void OpenExportFile() => OpenExportTarget(ExportOutputPath);
    [RelayCommand(CanExecute = nameof(CanOpenExport))]
    private void OpenExportFolder() => OpenExportTarget(Path.GetDirectoryName(ExportOutputPath)!);
    private void OpenExportTarget(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { ExportTechnicalDetails = ex.ToString(); ExportDetails = "Output remains saved. Could not open it: " + ex.Message; }
    }
    private sealed class ExportProgressSink<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
