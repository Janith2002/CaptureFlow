using System.Diagnostics;
using System.Text.Json;
using CaptureFlow.Core.Recovery;
namespace CaptureFlow.Infrastructure.Recovery;

/// <summary>Atomic, bounded metadata. Dismissal never removes media or autosaves.</summary>
public sealed partial class JsonRecoveryJournal : IRecoveryJournal, IRecoveryMaintenance
{
    private readonly string preferencesPath;
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<Guid, string> locations = [];
    public RecoveryPreferences Preferences { get; private set; }
    public bool RecordingEnabled => Preferences.RecordingEnabled;
    public JsonRecoveryJournal(string root)
    {
        root = Path.GetFullPath(root);
        preferencesPath = Path.Combine(root, "preferences.json");
        Preferences = new(StorageLocation: Path.Combine(root, "sessions"));
    }
    public async Task LoadPreferencesAsync(CancellationToken token = default)
    {
        if (File.Exists(preferencesPath))
        { var value = await ReadAsync<RecoveryPreferences>(preferencesPath, 65536, token); value.Validate(); Preferences = value; }
        if (Preferences.AutoArchiveFinished) await ArchiveExistingFinishedAsync(token);
    }
    public async Task SavePreferencesAsync(RecoveryPreferences preferences, CancellationToken token = default)
    {
        preferences.Validate();
        var root = Path.GetFullPath(preferences.StorageLocation);
        var installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        if (string.Equals(Path.TrimEndingDirectorySeparator(root), installation, StringComparison.OrdinalIgnoreCase) || root.StartsWith(installation + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Recovery storage must be outside the application installation directory.");
        Directory.CreateDirectory(root);
        var probe = Path.Combine(root, Guid.NewGuid() + ".probe");
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write)) { }
        File.Delete(probe);
        var previous = (Preferences.PreviousLocations ?? []).Append(Preferences.StorageLocation)
            .Where(p => !string.Equals(p, root, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        preferences = preferences with { StorageLocation = root, PreviousLocations = previous };
        preferences.Validate();
        await AtomicAsync(preferencesPath, preferences, 65536, token); Preferences = preferences;
    }
    public async Task<RecoveryRecord?> BeginAsync(string mode, string output, string work, string format, bool audio, CancellationToken token = default)
    {
        if (!RecordingEnabled) return null;
        using var process = Process.GetCurrentProcess(); var now = DateTimeOffset.UtcNow;
        var record = new RecoveryRecord(Guid.NewGuid(), mode, Path.GetFullPath(output), Path.GetFullPath(work), format,
            now, now, "Starting", ExpectedAudio: audio, OwnerProcessId: process.Id, OwnerStartedTicks: process.StartTime.ToUniversalTime().Ticks);
        await SaveRecordAsync(record, token); return record;
    }
    private IEnumerable<string> Roots => new[] { Preferences.StorageLocation }.Concat(Preferences.PreviousLocations ?? []).Distinct(StringComparer.OrdinalIgnoreCase);
    private string Find(Guid id)
    {
        lock (locations)
        {
        if (locations.TryGetValue(id, out var path) && File.Exists(path)) return path;
        foreach (var root in Roots) foreach (var folder in new[] { Path.Combine(root, id.ToString("N")), Path.Combine(root, "archive", id.ToString("N")) })
        { path = Path.Combine(folder, "session.json"); if (File.Exists(path)) return locations[id] = path; }
        // Cache persisted identities only: failed/unknown lookups must not grow the cache.
        return Path.Combine(Preferences.StorageLocation, id.ToString("N"), "session.json");
        }
    }
    public async Task SaveRecordAsync(RecoveryRecord record, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var path = Find(record.Id);
            if (!File.Exists(path) && Roots.Sum(r => Directory.Exists(r) ? Directory.EnumerateDirectories(r).Where(f => File.Exists(Path.Combine(f, "session.json"))).Take(1001).Count() : 0) >= 1000)
                throw new IOException("Recovery metadata limit reached (1000 sessions). Review retained sessions before starting another protected recording.");
            await AtomicAsync(path, record, 65536, token);
            if (Preferences.AutoArchiveFinished) await ArchiveAsync(record, path, token);
        }
        finally { gate.Release(); }
    }
    public async Task UpdateAsync(Guid id, string state, double duration, string? finalFile = null, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var path = Find(id); var record = await ReadAsync<RecoveryRecord>(path, 65536, token);
            record = record with { State = state, DurationSeconds = duration, Updated = DateTimeOffset.UtcNow, RecoveredFile = finalFile ?? record.RecoveredFile };
            await AtomicAsync(path, record, 65536, token);
            if (Preferences.AutoArchiveFinished) await ArchiveAsync(record, path, token);
        }
        finally { gate.Release(); }
    }
    public Task<IDisposable> AcquireRecoveryAsync(Guid id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var folder = Path.GetDirectoryName(Find(id))!;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Recovery metadata folder is missing.");
        return Task.FromResult<IDisposable>(new FileStream(Path.Combine(folder, "recovery.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
    }
    public async Task DismissAsync(Guid id, CancellationToken token = default)
    {
        using var lease = await AcquireRecoveryAsync(id, token);
        await UpdateAsync(id, "Dismissed", 0, token: token);
    }
    public async Task PrepareOutputAsync(Guid id, string finalFile, string hash, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var path = Find(id); var record = await ReadAsync<RecoveryRecord>(path, 65536, token);
            await AtomicAsync(path, record with { State = "Publishing", RecoveredFile = finalFile, OutputHash = hash, Updated = DateTimeOffset.UtcNow }, 65536, token);
        }
        finally { gate.Release(); }
    }
    public Task<IReadOnlyList<RecoveryCandidate>> ScanAsync(CancellationToken token = default) => Task.Run(() => ScanCoreAsync(token), token);
    private async Task<IReadOnlyList<RecoveryCandidate>> ScanCoreAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            List<RecoveryCandidate> result = [];
            foreach (var root in Roots.Where(Directory.Exists))
            foreach (var folder in Directory.EnumerateDirectories(root).Where(f => File.Exists(Path.Combine(f, "session.json"))).Take(1000))
            {
                token.ThrowIfCancellationRequested(); var path = Path.Combine(folder, "session.json");
                if (!File.Exists(path)) continue;
                try
                {
                var record = await ReadAsync<RecoveryRecord>(path, 65536, token);
                if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out var folderId) || record.Id == Guid.Empty || record.Id != folderId ||
                    string.IsNullOrWhiteSpace(record.Mode) || string.IsNullOrWhiteSpace(record.State) ||
                    record.Format is not ("MP4" or "MP3" or "WAV" or "CFEDIT") || !Path.IsPathFullyQualified(record.WorkDirectory ?? "") ||
                    record.Mode != "Editor" && !Path.IsPathFullyQualified(record.OutputDirectory ?? "") ||
                    !double.IsFinite(record.DurationSeconds) || record.DurationSeconds < 0)
                    throw new IOException("Recovery metadata has invalid session identity, paths, format or duration.");
                lock (locations) locations[record.Id] = path;
                if (record.State is "Completed" or "Recovered" or "Dismissed" or "EditorSaved") continue;
                var live = false;
                if (record.OwnerProcessId > 0 && record.State is not ("Error" or "Cancelled" or "LibraryPending"))
                    try { using var owner = Process.GetProcessById(record.OwnerProcessId); live = owner.StartTime.ToUniversalTime().Ticks == record.OwnerStartedTicks; }
                    catch (ArgumentException) { } catch (InvalidOperationException) { }
                string[] files = Directory.Exists(record.WorkDirectory) ? Directory.EnumerateFiles(record.WorkDirectory).Take(128).ToArray() : [];
                result.Add(new(record, files, files.Sum(f => { var info = new FileInfo(f); return info.Exists ? info.Length : 0; }), live, files.Length == 0 ? "Temporary files are missing." : null));
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out var id)) throw;
                    System.Diagnostics.Trace.TraceError("Recovery journal {0} is unreadable: {1}: {2}", id, ex.GetType().Name, ex.Message);
                    var unreadable = new RecoveryRecord(id, "Unrecognized session", "", folder, "UNKNOWN", DateTimeOffset.MinValue, DateTimeOffset.MinValue, "Unreadable");
                    result.Add(new(unreadable, [path], 0, false, "Recovery metadata or media inspection failed. Originals were retained. Open Folder to inspect/restore the journal. " + ex.Message));
                }
            }
            return result.OrderByDescending(c => c.Record.Updated).ToArray();
        }
        finally { gate.Release(); }
    }
    public async Task SaveEditorAsync(Guid id, EditorAutosave autosave, CancellationToken token = default)
    {
        autosave.Project.Validate(); autosave.Saved.Validate();
        await gate.WaitAsync(token);
        try
        {
            var existing = Find(id);
            if (File.Exists(existing))
            {
                var root = OwningRoot(existing, id); var active = Path.Combine(root, id.ToString("N"), "session.json");
                if (!string.Equals(Path.GetFullPath(active), Path.GetFullPath(existing), StringComparison.OrdinalIgnoreCase))
                {
                    ValidateManagedPath(root, active); Directory.CreateDirectory(Path.GetDirectoryName(active)!);
                    File.Move(existing, active, false); lock (locations) locations[id] = active;
                }
            }
        }
        finally { gate.Release(); }
        var folder = Path.GetDirectoryName(Find(id))!;
        using var owner = Process.GetCurrentProcess();
        await SaveRecordAsync(new(id, "Editor", "", folder, "CFEDIT", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Editor", OwnerProcessId: owner.Id, OwnerStartedTicks: owner.StartTime.ToUniversalTime().Ticks), token);
        await AtomicAsync(Path.Combine(folder, "editor.json"), autosave, 8 * 1024 * 1024, token);
    }
    public async Task<EditorAutosave> LoadEditorAsync(Guid id, CancellationToken token = default)
    {
        var record = await ReadAsync<RecoveryRecord>(Find(id), 65536, token);
        return await ReadAsync<EditorAutosave>(Path.Combine(record.WorkDirectory, "editor.json"), 8 * 1024 * 1024, token);
    }
    private static async Task<T> ReadAsync<T>(string path, int limit, CancellationToken token)
    {
        if (new FileInfo(path).Length > limit) throw new IOException("Recovery metadata exceeds its safety limit.");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: token) ?? throw new IOException("Invalid recovery metadata.");
    }
    private static async Task AtomicAsync<T>(string path, T value, int limit, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > limit) throw new IOException("Recovery metadata exceeds its safety limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            { await file.WriteAsync(bytes, token); file.Flush(true); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
