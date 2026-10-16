using CaptureFlow.Core.Editing;
namespace CaptureFlow.Core.Recovery;

public sealed record RecoveryPreferences(bool RecordingEnabled = true, bool EditorAutosaveEnabled = true, int AutosaveSeconds = 60,
    string StorageLocation = "", string[]? PreviousLocations = null, bool AutoArchiveFinished = true, int TerminalRetentionDays = 30)
{
    public void Validate()
    {
        if (TerminalRetentionDays is < 0 or > 3650 || AutosaveSeconds is < 15 or > 300 || !Path.IsPathFullyQualified(StorageLocation) || (PreviousLocations?.Length ?? 0) > 16 || (PreviousLocations?.Any(p => !Path.IsPathFullyQualified(p)) ?? false))
            throw new ArgumentException("Choose an absolute recovery folder and an autosave interval of 15–300 seconds.");
    }
}
public sealed record RecoveryRecord(Guid Id, string Mode, string OutputDirectory, string WorkDirectory, string Format,
    DateTimeOffset Started, DateTimeOffset Updated, string State, double DurationSeconds = 0, bool ExpectedAudio = false,
    int OwnerProcessId = 0, long OwnerStartedTicks = 0, string? RecoveredFile = null, string? OutputHash = null, string? Message = null);
public sealed record RecoveryCandidate(RecoveryRecord Record, string[] Files, long Bytes, bool IsLive, string? Warning = null)
{
    public string ModeLabel => Record.Mode switch
    {
        "Monitor" => "Screen Only · Entire Screen", "Window" => "Screen Only · Application Window", "Region" => "Screen Only · Custom Region",
        "Webcam" => "Webcam Only", "ScreenAndWebcam" => "Screen + Webcam", "MicrophoneOnly" => "Microphone Only",
        "SystemAudioOnly" => "System Audio Only", "MicrophoneAndSystemAudio" => "Microphone + System Audio", _ => Record.Mode
    };
}
public sealed record EditorAutosave(EditorProject Project, EditorProject Saved, EditorProject[] Undo, EditorProject[] Redo);
public interface IRecoveryJournal
{
    bool RecordingEnabled { get; }
    RecoveryPreferences Preferences { get; }
    Task LoadPreferencesAsync(CancellationToken token = default);
    Task SavePreferencesAsync(RecoveryPreferences preferences, CancellationToken token = default);
    Task<RecoveryRecord?> BeginAsync(string mode, string output, string work, string format, bool audio, CancellationToken token = default);
    Task UpdateAsync(Guid id, string state, double duration, string? finalFile = null, CancellationToken token = default);
    Task PrepareOutputAsync(Guid id, string finalFile, string hash, CancellationToken token = default);
    Task<IDisposable> AcquireRecoveryAsync(Guid id, CancellationToken token = default);
    Task<IReadOnlyList<RecoveryCandidate>> ScanAsync(CancellationToken token = default);
    Task SaveRecordAsync(RecoveryRecord record, CancellationToken token = default);
    Task DismissAsync(Guid id, CancellationToken token = default);
    Task SaveEditorAsync(Guid id, EditorAutosave autosave, CancellationToken token = default);
    Task<EditorAutosave> LoadEditorAsync(Guid id, CancellationToken token = default);
}
public sealed record RecoveredMedia(string Path, double Duration, bool HasVideo, bool HasAudio, string Warning);
public interface IRecoveryMediaBackend
{
    Task<RecoveredMedia> RecoverAsync(RecoveryCandidate candidate, string pendingPath, string ffmpeg, CancellationToken token);
    Task<RecoveredMedia> ValidateAsync(string path, string ffmpeg, CancellationToken token);
}
public sealed record RecoveryInventoryItem(string Folder, string Classification, long Bytes, string Detail);
public sealed record RecoveryInventory(RecoveryInventoryItem[] Items, long ManagedBytes, long ReferencedMediaBytes, string Warning);
public sealed record RecoveryCleanupResult(int FilesRemoved, long BytesRemoved, int ProtectedSessions, string Message);
public interface IRecoveryMaintenance
{
    Task<RecoveryInventory> InventoryAsync(CancellationToken token = default);
    Task<RecoveryCleanupResult> CleanupAsync(bool confirmed, CancellationToken token = default);
}
