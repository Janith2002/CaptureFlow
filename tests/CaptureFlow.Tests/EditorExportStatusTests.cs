using System.Windows.Threading;
using CaptureFlow.Core.Editing;
using CaptureFlow.Desktop.ViewModels;

namespace CaptureFlow.Tests;

public class EditorExportStatusTests
{
    [Fact] public Task ActualExportProgressBindingCanAttachToReadOnlyViewModelProperty() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "CaptureFlow.slnx"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var doc = System.Xml.Linq.XDocument.Load(Path.Combine(folder.FullName, "src", "CaptureFlow.Desktop", "Views", "EditorPage.xaml"));
        var source = Assert.Single(doc.Descendants(), e => e.Name.LocalName == "ProgressBar" && ((string?)e.Attribute("Value"))?.Contains("ExportProgressValue") == true);
        // Use the production Value binding on a real WPF ProgressBar; RangeBase.Value
        // defaults to TwoWay, which throws when DataContext has a getter-only source.
        var markup = new System.Xml.Linq.XElement(source.Name, new System.Xml.Linq.XAttribute("Value", source.Attribute("Value")!.Value));
        var bar = (System.Windows.Controls.ProgressBar)System.Windows.Markup.XamlReader.Parse(markup.ToString());
        await using var f = await Fixture.Create();
        bar.DataContext = f.Vm;
        var binding = System.Windows.Data.BindingOperations.GetBinding(bar, System.Windows.Controls.Primitives.RangeBase.ValueProperty);
        Assert.NotNull(binding); Assert.Equal(System.Windows.Data.BindingMode.OneWay, binding.Mode); Assert.Equal(0, bar.Value);
    });
    [Theory]
    [InlineData(5_000_000L, 10d, .5)] [InlineData(20_000_000L, 10d, 1d)]
    public void TimestampProgressUsesValidDuration(long micros, double duration, double expected) => Assert.Equal(expected, EditorExportProgress.FractionFor(micros, duration));
    [Theory] [InlineData(0d)] [InlineData(-1d)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void UnknownOrInvalidDurationDoesNotInventPercentage(double duration) => Assert.Null(EditorExportProgress.FractionFor(1000, duration));

    [Fact] public Task StartAndWorkerProgressRemainResponsiveAndCannotBeDismissed() => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = await Fixture.Create(); var run = f.Vm.ExportCommand.ExecuteAsync(null); await f.Backend.Entered.Task; await Flush();
        Assert.True(f.Vm.ExportActive); Assert.True(f.Vm.ExportStatusVisible); Assert.False(f.Vm.DismissExportStatusCommand.CanExecute(null));
        Assert.True(f.Vm.ExportIndeterminate); Assert.Empty(f.Vm.ExportProgressText);
        var uiThread = Environment.CurrentManagedThreadId; var updateThread = 0;
        f.Vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(f.Vm.ExportFraction)) updateThread = Environment.CurrentManagedThreadId; };
        await Task.Run(() => f.Backend.Progress!.Report(new("Exporting", Fraction: .5))); await Flush();
        Assert.Equal(.5, f.Vm.ExportProgressValue); Assert.Equal("50%", f.Vm.ExportProgressText); Assert.Equal(uiThread, updateThread);
        var ticked = false; await Dispatcher.CurrentDispatcher.InvokeAsync(() => ticked = true); Assert.True(ticked);
        f.Backend.Release.TrySetResult(); await run;
    });

    [Theory] [InlineData("MP4")] [InlineData("MP3")] [InlineData("WAV")]
    public Task ValidatedBackendCompletionPersistsLocationSizeAndActions(string format) => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = await Fixture.Create(); f.Vm.ExportFormat = format; var registered = 0; f.Vm.Exported += _ => registered++;
        var run = f.Vm.ExportCommand.ExecuteAsync(null); await f.Backend.Entered.Task;
        f.Backend.Progress!.Report(new("Finalizing", f.Backend.Path)); await Flush();
        Assert.Equal("Finalizing", f.Vm.ExportState); Assert.True(f.Vm.ExportIndeterminate); Assert.Equal(0, registered);
        f.Backend.Release.TrySetResult(); await run;
        Assert.Equal("Completed", f.Vm.ExportState); Assert.Equal("100%", f.Vm.ExportProgressText); Assert.True(f.Vm.ExportStatusVisible);
        Assert.Equal(f.Backend.Path, f.Vm.ExportOutputPath); Assert.Contains("bytes", f.Vm.ExportFileSize); Assert.Equal(format, f.Vm.ExportJobFormat);
        Assert.True(f.Vm.OpenExportFileCommand.CanExecute(null)); Assert.True(f.Vm.OpenExportFolderCommand.CanExecute(null)); Assert.Equal(1, registered);
        f.Vm.Message = "An unrelated editor operation"; Assert.Equal("Export completed", f.Vm.ExportHeading);
        f.Backend.Progress.Report(new("Exporting", Fraction: .1)); await Flush(); Assert.Equal("Completed", f.Vm.ExportState);
        f.Vm.DismissExportStatusCommand.Execute(null); Assert.False(f.Vm.ExportStatusVisible);
    });

    [Theory] [InlineData(true)] [InlineData(false)]
    public Task MissingOutputOrNonzeroExitNeverSignalsCompletion(bool missing) => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = await Fixture.Create(); f.Backend.Missing = missing;
        if (!missing) f.Backend.Error = new IOException("FFmpeg exited with code 1. fixture stderr");
        var registered = false; f.Vm.Exported += _ => registered = true;
        var run = f.Vm.ExportCommand.ExecuteAsync(null); await f.Backend.Entered.Task; f.Backend.Release.TrySetResult(); await run; await Flush();
        Assert.Equal("Failed", f.Vm.ExportState); Assert.False(registered); Assert.True(f.Vm.ExportStatusVisible); Assert.False(f.Vm.OpenExportFileCommand.CanExecute(null));
        Assert.Contains(missing ? "nonempty output" : "code 1", f.Vm.ExportDetails); Assert.NotEmpty(f.Vm.ExportTechnicalDetails); Assert.True(f.Vm.RetryExportCommand.CanExecute(null));
    });

    [Fact] public Task CancellationStopsOwnedJobAndRetainsUnrelatedFiles() => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = await Fixture.Create(); var existing = System.IO.Path.Combine(f.Folder, "unrelated.wav"); await File.WriteAllTextAsync(existing, "preserve");
        var run = f.Vm.ExportCommand.ExecuteAsync(null); await f.Backend.Entered.Task; f.Vm.CancelExportCommand.Execute(null); await run;
        Assert.Equal("Cancelled", f.Vm.ExportState); Assert.False(f.Vm.ExportActive); Assert.False(File.Exists(f.Backend.Path)); Assert.Equal("preserve", await File.ReadAllTextAsync(existing));
        Assert.Equal(1, f.Backend.Cleanups); Assert.True(f.Vm.ExportStatusVisible); Assert.False(f.Vm.OpenExportFileCommand.CanExecute(null));
    });

    [Fact] public Task EmptyOutputFolderShowsPersistentActionableFailureBeforeRender() => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = await Fixture.Create(); f.Vm.OutputFolder = ""; await f.Vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal("Failed", f.Vm.ExportState); Assert.Contains("Choose an output folder", f.Vm.ExportDetails); Assert.Equal(0, f.Backend.Starts);
    });

    [Fact] public Task RapidRepeatedClicksCreateOneExportAndNeverOverwriteExistingOutput() => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = await Fixture.Create();
        var run = f.Vm.ExportCommand.ExecuteAsync(null); await f.Backend.Entered.Task;
        Assert.False(f.Vm.ExportCommand.CanExecute(null)); await f.Vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal(1, f.Backend.Starts); await File.WriteAllTextAsync(f.Backend.Path!, "existing export");
        f.Backend.Release.TrySetResult(); await run;
        Assert.Equal("Failed", f.Vm.ExportState); Assert.Equal("existing export", await File.ReadAllTextAsync(f.Backend.Path!));
    });

    [Fact] public Task InvalidProgressAndFinalizingNeverShowPrematureOneHundredPercent() => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = await Fixture.Create(); var run = f.Vm.ExportCommand.ExecuteAsync(null); await f.Backend.Entered.Task;
        f.Backend.Progress!.Report(new("Exporting", Fraction: double.NaN)); await Flush(); Assert.True(f.Vm.ExportIndeterminate);
        f.Backend.Progress.Report(new("Exporting", Fraction: 1)); await Flush(); Assert.Equal("99%", f.Vm.ExportProgressText);
        f.Backend.Progress.Report(new("Finalizing", f.Backend.Path)); await Flush(); Assert.True(f.Vm.ExportIndeterminate); Assert.Empty(f.Vm.ExportProgressText);
        f.Backend.Release.TrySetResult(); await run;
    });

    [Fact] public Task RetryAllocatesNewOutputAndIgnoresPreviousJobProgress() => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = await Fixture.Create(); f.Backend.Error = new IOException("fixture failure");
        var first = f.Vm.ExportCommand.ExecuteAsync(null); await f.Backend.Entered.Task;
        var oldProgress = f.Backend.Progress!; var oldPath = f.Backend.Path;
        f.Backend.Release.TrySetResult(); await first; await Flush();
        f.Backend.Error = null; f.Backend.Release = new(TaskCreationOptions.RunContinuationsAsynchronously); f.Backend.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = f.Vm.RetryExportCommand.ExecuteAsync(null); await f.Backend.Entered.Task; await Flush();
        Assert.NotEqual(oldPath, f.Backend.Path);
        oldProgress.Report(new("Finalizing", oldPath)); await Flush(); Assert.Equal("Exporting", f.Vm.ExportState);
        f.Backend.Release.TrySetResult(); await retry; Assert.Equal("Completed", f.Vm.ExportState);
    });

    private static async Task Flush() => await Dispatcher.Yield(DispatcherPriority.Background);
    private sealed class Fixture : IAsyncDisposable
    {
        public string Folder { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CaptureFlow-export-status-tests", Guid.NewGuid().ToString("N"));
        public Backend Backend { get; } = new(); public EditorViewModel Vm { get; }
        private Fixture() { Directory.CreateDirectory(Folder); Vm = new(Backend, new Store()) { OutputFolder = Folder }; }
        public static async Task<Fixture> Create() { var f = new Fixture(); await f.Vm.ImportFileAsync(f.Backend.Media.Path, true); return f; }
        public async ValueTask DisposeAsync() { Backend.Release.TrySetResult(); await Vm.DisposeAsync(); Directory.Delete(Folder, true); }
    }
    private sealed class Backend : IEditorMediaBackend, IEditorExportBackend
    {
        public EditorMedia Media { get; } = EditorTimelineTests.Media();
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IProgress<EditorExportProgress>? Progress; public string? Path; public int Starts, Cleanups; public bool Missing; public Exception? Error;
        public Task<EditorMedia> ProbeAsync(string path, string ffmpeg, CancellationToken token) => Task.FromResult(Media);
        public Task<EditorAnalysis> AnalyzeAsync(EditorMedia media, string ffmpeg, CancellationToken token) => Task.FromResult(new EditorAnalysis(null, []));
        public Task<EditorRange[]> DetectSilenceAsync(EditorMedia media, double db, double minimum, string ffmpeg, CancellationToken token) => Task.FromResult<EditorRange[]>([]);
        public Task<string> RenderAsync(EditorProject project, EditorExportSettings settings, string folder, string ffmpeg, IProgress<double>? progress, CancellationToken token) => throw new NotSupportedException();
        public async Task<string> RenderExportAsync(EditorProject project, EditorExportSettings settings, string folder, string ffmpeg, IProgress<EditorExportProgress> progress, CancellationToken token)
        {
            Starts++; Progress = progress; Path = System.IO.Path.Combine(folder, Guid.NewGuid().ToString("N") + "." + settings.Format.ToLowerInvariant());
            try
            {
                progress.Report(new("Preparing", Path)); progress.Report(new("Exporting", Path)); Entered.TrySetResult();
                await Release.Task.WaitAsync(token); if (Error is not null) throw Error;
                if (!Missing) { await using var file = new FileStream(Path, FileMode.CreateNew); await file.WriteAsync(new byte[] { 1, 2, 3 }, token); }
                return Path; // Fake backend contract: media validation is simulated, not perceptual playback.
            }
            finally { Cleanups++; }
        }
    }
    private sealed class Store : IEditorProjectStore
    {
        public Task SaveAsync(EditorProject project, string path, CancellationToken token) => Task.CompletedTask;
        public Task<EditorProject> LoadAsync(string path, CancellationToken token) => Task.FromResult(EditorProject.Empty);
    }
}
