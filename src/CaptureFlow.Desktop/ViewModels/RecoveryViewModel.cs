using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CaptureFlow.Core.Recovery;
using CaptureFlow.Application.Recovery;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace CaptureFlow.Desktop.ViewModels;

public partial class RecoveryViewModel(IRecoveryJournal journal, RecordingRecoveryService service, EditorViewModel editor) : ObservableObject, IAsyncDisposable
{
    private CancellationTokenSource? operation;
    private Task running = Task.CompletedTask;
    private bool disposed;
    public Func<string>? FfmpegPath { get; set; }
    public event Action? Recovered;
    public ObservableCollection<RecoveryCandidate> Sessions { get; } = [];
    public ObservableCollection<RecoveryInventoryItem> Inventory { get; } = [];
    [ObservableProperty] private bool autoArchiveFinished = true;
    [ObservableProperty] private int terminalRetentionDays = 30;
    [ObservableProperty] private RecoveryCandidate? selected;
    [ObservableProperty] private bool recordingEnabled = true;
    [ObservableProperty] private bool editorAutosaveEnabled = true;
    [ObservableProperty] private int autosaveSeconds = 60;
    [ObservableProperty] private string storageLocation = "";
    [ObservableProperty] private string message = "Recovery keeps original intermediates. Dismiss hides a session; it does not delete files.";
    [ObservableProperty] private string usage = "";
    [ObservableProperty] private bool busy;
    public bool Available => Sessions.Any(s => !s.IsLive);
    public async Task LoadAsync()
    {
        try
        {
            await journal.LoadPreferencesAsync(); var p = journal.Preferences;
            RecordingEnabled = p.RecordingEnabled; EditorAutosaveEnabled = p.EditorAutosaveEnabled; AutosaveSeconds = p.AutosaveSeconds; StorageLocation = p.StorageLocation;
            AutoArchiveFinished = p.AutoArchiveFinished; TerminalRetentionDays = p.TerminalRetentionDays;
            await RefreshAsync();
        }
        catch (Exception ex) { Trace.TraceError("Recovery settings: {0}: {1}", ex.GetType().Name, ex.Message); Message = "Recovery settings could not be loaded: " + ex.Message; }
    }
    [RelayCommand] private async Task SaveAsync()
    {
        try { await journal.SavePreferencesAsync(new(RecordingEnabled, EditorAutosaveEnabled, AutosaveSeconds, StorageLocation, AutoArchiveFinished: AutoArchiveFinished, TerminalRetentionDays: TerminalRetentionDays)); Message = "Recovery preferences saved. Retained sessions in previous locations remain discoverable."; }
        catch (Exception ex) { Message = ex.Message; }
    }
    [RelayCommand] public async Task RefreshAsync()
    {
        try
        {
            var values = await journal.ScanAsync(); Sessions.Clear(); foreach (var value in values) Sessions.Add(value);
            Usage = $"Retained media in incomplete sessions: {values.Sum(v => v.Bytes) / 1048576d:0.0} MB";
            OnPropertyChanged(nameof(Available));
            if (Available) Message = "Recovery Available — choose a session below. Recovery may be partial; originals are retained.";
        }
        catch (Exception ex) { Trace.TraceError("Recovery scan: {0}: {1}", ex.GetType().Name, ex.Message); Message = "Recovery scan failed: " + ex.Message; }
    }
    [RelayCommand] private async Task RecoverAsync()
    {
        if (Busy || Selected is null || Selected.IsLive || disposed) return;
        if (Selected.Record.Mode == "Editor" && !editor.CanEdit) { Message = "Wait for the editor operation to finish."; return; }
        Busy = true; using var cancellation = new CancellationTokenSource(); operation = cancellation;
        var selected = Selected;
        async Task WorkAsync()
        {
            try
            {
                if (selected.Record.Mode == "Editor")
                {
                    if (!editor.ConfirmDiscard()) return;
                    using var lease = await journal.AcquireRecoveryAsync(selected.Record.Id, cancellation.Token);
                    editor.RestoreAutosave(await journal.LoadEditorAsync(selected.Record.Id, cancellation.Token), selected.Record.Id);
                    await journal.SaveEditorAsync(selected.Record.Id, editor.Session.Snapshot(), cancellation.Token);
                    Message = editor.Message;
                    var result = Message; await RefreshAsync(); Message = result;
                }
                else
                {
                    Message = "Recovering and decoding for validation…";
                    var media = await service.RecoverAsync(selected, FfmpegPath?.Invoke() ?? "ffmpeg.exe", cancellation.Token);
                    Message = "Recovered: " + media.Path + ". " + media.Warning; Recovered?.Invoke();
                    var result = Message; await RefreshAsync(); Message = result;
                }
            }
            catch (OperationCanceledException) { Message = "Recovery cancelled. Original files and recovery jobs are retained."; }
            catch (Exception ex) { Trace.TraceError("Recovery session {0}: {1}: {2}", selected.Record.Id, ex.GetType().Name, ex.Message); Message = "Recovery failed: " + ex.Message + " Original files are retained."; }
            finally { Busy = false; operation = null; }
        }
        running = WorkAsync(); await running;
    }
    [RelayCommand] private void Cancel() => operation?.Cancel();
    [RelayCommand] private async Task InspectStorageAsync()
    {
        if (Busy || journal is not IRecoveryMaintenance maintenance) return;
        try
        {
            var values = await maintenance.InventoryAsync(); Inventory.Clear(); foreach (var value in values.Items) Inventory.Add(value);
            Usage = $"Managed recovery storage: {values.ManagedBytes / 1048576d:0.00} MiB. Referenced capture intermediates outside it: {values.ReferencedMediaBytes / 1048576d:0.00} MiB (retained).";
            Message = values.Warning;
        }
        catch (Exception ex) { Message = "Storage inventory failed: " + ex.Message; }
    }
    [RelayCommand] private async Task CleanupAsync()
    {
        if (Busy || journal is not IRecoveryMaintenance maintenance) return;
        if (MessageBox.Show("Archive finished journals and remove eligible terminal metadata / saved autosaves older than the saved retention preference? Completed output fingerprints must match. No recordings, intermediate media, incomplete sessions or orphaned media will be deleted.", "Confirm recovery metadata cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Busy = true;
        try { var result = await maintenance.CleanupAsync(true); Message = $"Removed {result.FilesRemoved} metadata files ({result.BytesRemoved / 1048576d:0.00} MiB); {result.ProtectedSessions} sessions protected. {result.Message}"; }
        catch (Exception ex) { Message = "Cleanup failed; media was retained: " + ex.Message; }
        finally { Busy = false; }
    }
    [RelayCommand] private void OpenFolder()
    {
        if (Selected is null) return;
        try { if (!Directory.Exists(Selected.Record.WorkDirectory)) throw new DirectoryNotFoundException("Recovery folder is missing."); Process.Start(new ProcessStartInfo(Selected.Record.WorkDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { Message = ex.Message; }
    }
    [RelayCommand] private async Task DismissAsync()
    {
        if (Busy || Selected is null || Selected.IsLive) return;
        if (MessageBox.Show("Hide this recovery notification? Its media and autosave files will be retained.", "Dismiss recovery", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { await journal.DismissAsync(Selected.Record.Id); await RefreshAsync(); }
        catch (Exception ex) { Message = ex.Message; }
    }
    public async ValueTask DisposeAsync()
    { disposed = true; operation?.Cancel(); try { await running.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception ex) { Trace.TraceError("Recovery shutdown: {0}", ex.Message); } Recovered = null; }
}
