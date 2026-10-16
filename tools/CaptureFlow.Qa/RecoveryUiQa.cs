using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using CaptureFlow.Core.Recovery;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Infrastructure.Recovery;
namespace CaptureFlow.Qa;

internal static class RecoveryUiQa
{
    public static void Prepare()
    {
        var root = Environment.GetEnvironmentVariable("CAPTUREFLOW_RECOVERY_ROOT")!;
        var output = Path.Combine(root, "fixture-output"); Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("CAPTUREFLOW_LIBRARY_INITIAL_FOLDER", output);
        var work = Path.Combine(output, ".captureflow-work", "audio-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        using (var writer = new BinaryWriter(File.Create(Path.Combine(work, "synthetic.wav"))))
        {
            writer.Write("RIFF"u8); writer.Write(96036); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(96000); writer.Write(new byte[96000]);
        }
        var journal = new JsonRecoveryJournal(root); var record = journal.BeginAsync("MicrophoneOnly", output, work, "WAV", true).GetAwaiter().GetResult()!;
        journal.UpdateAsync(record.Id, "Error", 1).GetAwaiter().GetResult();
    }
    public static async Task VerifyAsync(MainWindow window, DashboardViewModel dashboard)
    {
        var recovery = dashboard.Recovery ?? throw new InvalidOperationException("Recovery dependency missing.");
        if (window.Icon is null) throw new InvalidOperationException("Branded window icon missing.");
        var tools = dashboard.ExternalTools ?? throw new InvalidOperationException("External tools settings missing.");
        await tools.SaveCommand.ExecuteAsync(null);
        if (!tools.Message.Contains("verified and saved")) throw new InvalidOperationException(tools.Message);
        var savedPath = dashboard.Screen!.FfmpegPath;
        tools.Executable = System.IO.Path.Combine(Environment.GetEnvironmentVariable("CAPTUREFLOW_RECOVERY_ROOT")!, "missing", "ffmpeg.exe");
        await tools.SaveCommand.ExecuteAsync(null);
        if (!tools.Message.StartsWith("External tools were not changed:") || dashboard.Screen.FfmpegPath != savedPath)
            throw new InvalidOperationException("Invalid external path replaced the current configuration.");
        tools.Executable = savedPath;
        await recovery.InspectStorageCommand.ExecuteAsync(null);
        if (recovery.Inventory.Count == 0) throw new InvalidOperationException("Recovery storage inventory missing.");
        Console.WriteLine("PASS branded WPF window, shared external-tool settings persistence, missing-tool error without replacing configuration, read-only recovery inventory. No capture started.");
        if (!recovery.Available || recovery.Sessions.Count != 1) throw new InvalidOperationException("Expected one isolated recovery notification.");
        dashboard.ShowRecoveryCommand.Execute(null); window.UpdateLayout();
        if (!Children(window).OfType<CaptureFlow.Desktop.Views.RecoverySettings>().Any(v => v.IsVisible)) throw new InvalidOperationException("Recovery settings view is not visible.");
        if (!Children(window).OfType<Button>().Any(b => b.IsVisible && b.Command == recovery.RecoverCommand)) throw new InvalidOperationException("Recovery command is not bound.");
        if (!recovery.RecordingEnabled || !recovery.EditorAutosaveEnabled || recovery.AutosaveSeconds != 60) throw new InvalidOperationException("Invalid protection defaults.");
        recovery.Selected = recovery.Sessions.Single(); await recovery.RecoverCommand.ExecuteAsync(null);
        if (recovery.Available || !recovery.Message.StartsWith("Recovered:")) throw new InvalidOperationException(recovery.Message);
        await dashboard.RefreshRecordingsCommand.ExecuteAsync(null);
        if (!dashboard.Recordings.Any(r => r.Format == "WAV" && r.Name.StartsWith("Recovered_"))) throw new InvalidOperationException("Recovered fixture was not added to SQLite library.");
        Console.WriteLine("PASS recovery banner, Settings UI, defaults, command routing, synthetic WAV validation, SQLite refresh and duplicate notification removal. No capture started.");
        var editor = dashboard.Editor!; await editor.ImportFileAsync(dashboard.Recordings.Single(r => r.Name.StartsWith("Recovered_")).FullPath, true);
        var journal = new JsonRecoveryJournal(Environment.GetEnvironmentVariable("CAPTUREFLOW_RECOVERY_ROOT")!);
        async Task<RecoveryCandidate> WaitForSnapshotAsync(double expected)
        {
            var until = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < until)
            {
                var candidate = (await journal.ScanAsync()).FirstOrDefault(c => c.Record.Mode == "Editor" && c.Files.Any(f => Path.GetFileName(f) == "editor.json"));
                if (candidate is not null && Math.Abs((await journal.LoadEditorAsync(candidate.Record.Id)).Project.Duration - expected) < .01) return candidate;
                await Task.Delay(100);
            }
            throw new TimeoutException("Editor autosave did not persist the generated fixture project.");
        }
        var autosave = await WaitForSnapshotAsync(1);
        editor.TrimIn = .1; editor.TrimOut = .8; editor.ApplyClipCommand.Execute(null);
        autosave = await WaitForSnapshotAsync(.7);
        var snapshot = await journal.LoadEditorAsync(autosave.Record.Id);
        if (snapshot.Undo.Length == 0 || snapshot.Project.Clips.Length != 1) throw new InvalidOperationException("Editor autosave history was not retained.");
        editor.Session.MarkSaved(); await journal.UpdateAsync(autosave.Record.Id, "EditorSaved", .7);
        Console.WriteLine("PASS real WPF editor debounce autosave after import and trim, isolated snapshots and undo history. Generated media only.");
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject item)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) { var child = VisualTreeHelper.GetChild(item, i); yield return child; foreach (var next in Children(child)) yield return next; }
    }
}
