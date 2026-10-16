using System.Security.Cryptography;
using System.Text.Json;
using CaptureFlow.Core.Recovery;
namespace CaptureFlow.Infrastructure.Recovery;

public sealed partial class JsonRecoveryJournal
{
    private static bool Finished(string state) => state is "Completed" or "Recovered" or "EditorSaved";
    /// <summary>Rejects traversal, junctions and symlinks, including ancestors. Cleanup never follows links.</summary>
    public static void ValidateManagedPath(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); path = Path.GetFullPath(path);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Recovery operation escapes the managed recovery directory.");
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Recovery cleanup does not traverse symlinks or junctions.");
    }
    private string OwningRoot(string path, Guid id)
    {
        foreach (var root in Roots)
        {
            var active = Path.Combine(root, id.ToString("N"), "session.json"); var archived = Path.Combine(root, "archive", id.ToString("N"), "session.json");
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(active), StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFullPath(path), Path.GetFullPath(archived), StringComparison.OrdinalIgnoreCase))
            { ValidateManagedPath(root, path); return root; }
        }
        throw new IOException("Session metadata is outside the managed recovery directory.");
    }
    private async Task ArchiveAsync(RecoveryRecord record, string path, CancellationToken token)
    {
        if (!Finished(record.State)) return;
        var root = OwningRoot(path, record.Id); var destination = Path.Combine(root, "archive", record.Id.ToString("N"), "session.json");
        ValidateManagedPath(root, destination);
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) return;
        // Move only the known journal. Media, autosaves, jobs, unknown files and lock leases stay intact.
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); token.ThrowIfCancellationRequested();
        File.Move(path, destination, false); lock (locations) locations[record.Id] = destination;
        await Task.CompletedTask;
    }
    private async Task ArchiveExistingFinishedAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            foreach (var root in Roots.Where(Directory.Exists))
            foreach (var folder in Directory.EnumerateDirectories(root).Where(f => File.Exists(Path.Combine(f, "session.json"))).Take(1000).ToArray())
            {
                token.ThrowIfCancellationRequested(); var path = Path.Combine(folder, "session.json");
                try { var record = await ReadAsync<RecoveryRecord>(path, 65536, token); await ArchiveAsync(record, path, token); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { System.Diagnostics.Trace.TraceError("Recovery archive retained original data: {0}", ex.Message); }
            }
        }
        finally { gate.Release(); }
    }
    private IEnumerable<string> MetadataPaths()
    {
        foreach (var root in Roots.Where(Directory.Exists))
        foreach (var parent in new[] { root, Path.Combine(root, "archive") }.Where(Directory.Exists))
        foreach (var folder in Directory.EnumerateDirectories(parent).Take(10000))
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) continue;
            var file = Path.Combine(folder, "session.json"); if (File.Exists(file)) yield return file;
        }
    }
    public Task<RecoveryInventory> InventoryAsync(CancellationToken token = default) => Task.Run(() => InventoryCoreAsync(token), token);
    private async Task<RecoveryInventory> InventoryCoreAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            List<RecoveryInventoryItem> entries = []; var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long externalBytes = 0;
            var warning = "Read-only inventory is bounded to 10,000 folders per location and 100,000 files. Linked paths are skipped. All media is retained; cleanup targets metadata only.";
            foreach (var path in MetadataPaths())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var record = await ReadAsync<RecoveryRecord>(path, 65536, token); var root = OwningRoot(path, record.Id);
                    var live = false;
                    if (!Finished(record.State) && record.State is not ("Error" or "Cancelled" or "Dismissed" or "LibraryPending") && record.OwnerProcessId > 0)
                        try { using var owner = System.Diagnostics.Process.GetProcessById(record.OwnerProcessId); live = owner.StartTime.ToUniversalTime().Ticks == record.OwnerStartedTicks; }
                        catch (ArgumentException) { } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { live = true; }
                    var classification = live ? "Active — protected" : record.State switch
                    { "Completed" => "Completed recording", "Recovered" => "Successfully recovered", "EditorSaved" => "Saved editor autosave", _ => "Incomplete / potentially recoverable — protected" };
                    var folder = Path.GetDirectoryName(path)!;
                    entries.Add(new(folder, classification, TreeBytes(folder, token), $"{record.Mode}; {record.Updated:g}; final file: {record.RecoveredFile ?? "unknown"}"));
                    // Read-only size accounting for legacy capture intermediates. Never delete these paths.
                    if (!string.IsNullOrEmpty(record.OutputDirectory) && !string.IsNullOrEmpty(record.WorkDirectory) && referenced.Add(record.WorkDirectory))
                    {
                        var legacy = Path.Combine(Path.GetFullPath(record.OutputDirectory), ".captureflow-work") + Path.DirectorySeparatorChar;
                        if (Path.GetFullPath(record.WorkDirectory).StartsWith(legacy, StringComparison.OrdinalIgnoreCase)) externalBytes += TreeBytes(record.WorkDirectory, token);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                { entries.Add(new(Path.GetDirectoryName(path)!, "Unreadable — protected", 0, ex.Message)); }
            }
            foreach (var root in Roots.Where(Directory.Exists))
            foreach (var folder in Directory.EnumerateDirectories(root).Take(10000))
                if (Guid.TryParseExact(Path.GetFileName(folder), "N", out _) && !File.Exists(Path.Combine(folder, "session.json")))
                    entries.Add(new(folder, "Orphaned / archived-session leftovers — protected", TreeBytes(folder, token), "No active journal here. Media and unknown files are never automatically deleted."));
            return new(entries.ToArray(), Roots.Sum(r => TreeBytes(r, token)), externalBytes, warning);
        }
        finally { gate.Release(); }
    }
    private static long TreeBytes(string folder, CancellationToken token)
    {
        if (!Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return 0;
        long bytes = 0; var count = 0; var queue = new Stack<string>(); queue.Push(folder);
        while (queue.Count > 0 && count < 100000)
        {
            token.ThrowIfCancellationRequested(); var next = queue.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(next))
            {
                if (++count > 100000) break;
                var attributes = File.GetAttributes(path); if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0) queue.Push(path); else bytes += new FileInfo(path).Length;
            }
        }
        return bytes;
    }
    public Task<RecoveryCleanupResult> CleanupAsync(bool confirmed, CancellationToken token = default) => confirmed
        ? Task.Run(() => CleanupCoreAsync(token), token) : Task.FromResult(new RecoveryCleanupResult(0, 0, 0, "Cleanup was not confirmed; nothing changed."));
    private async Task<RecoveryCleanupResult> CleanupCoreAsync(CancellationToken token)
    {
        await gate.WaitAsync(token); int removed = 0, protectedCount = 0; long bytes = 0;
        try
        {
            foreach (var path in MetadataPaths().ToArray())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var record = await ReadAsync<RecoveryRecord>(path, 65536, token); var root = OwningRoot(path, record.Id);
                    if (!Finished(record.State)) { protectedCount++; continue; }
                    // Archiving frees index slots without deleting any media, including when auto-archive is off.
                    await ArchiveAsync(record, path, token); var current = Find(record.Id);
                    if (DateTimeOffset.UtcNow - record.Updated < TimeSpan.FromDays(Preferences.TerminalRetentionDays) || record.RecoveredFile is null || record.OutputHash is null)
                    { protectedCount++; continue; }
                    var final = Path.GetFullPath(record.RecoveredFile);
                    var expected = record.State == "EditorSaved" ? ".cfedit" : "." + record.Format.ToLowerInvariant();
                    if (!string.Equals(Path.GetExtension(final), expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(final)) { protectedCount++; continue; }
                    await using (var input = File.OpenRead(final))
                        if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(input, token)), record.OutputHash, StringComparison.Ordinal)) { protectedCount++; continue; }
                    using var lease = await AcquireRecoveryAsync(record.Id, token);
                    // Another process may have resumed an editor while we checked the final file.
                    // Retain everything unless the journal is still exactly the version inspected.
                    var latest = await ReadAsync<RecoveryRecord>(current, 65536, token);
                    if (latest != record) { protectedCount++; continue; }
                    if (record.State == "EditorSaved")
                    {
                        // Delete a saved autosave only at its exact managed session path. Explicit projects are never targeted.
                        var snapshot = Path.Combine(record.WorkDirectory, "editor.json");
                        var originalFolder = Path.Combine(root, record.Id.ToString("N")); var archiveFolder = Path.Combine(root, "archive", record.Id.ToString("N"));
                        if (string.Equals(Path.GetFullPath(record.WorkDirectory), Path.GetFullPath(originalFolder), StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFullPath(record.WorkDirectory), Path.GetFullPath(archiveFolder), StringComparison.OrdinalIgnoreCase))
                        {
                            ValidateManagedPath(root, snapshot);
                            if (File.Exists(snapshot)) { bytes += new FileInfo(snapshot).Length; File.Delete(snapshot); removed++; }
                        }
                        else { protectedCount++; continue; }
                    }
                    ValidateManagedPath(root, current); bytes += new FileInfo(current).Length; File.Delete(current); removed++;
                    lock (locations) locations.Remove(record.Id);
                    // Deliberately no recursive deletion. Unknown files, media, jobs, leases and orphans remain untouched.
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                { protectedCount++; System.Diagnostics.Trace.TraceError("Protected recovery cleanup: {0}: {1}", ex.GetType().Name, ex.Message); }
            }
            return new(removed, bytes, protectedCount, "Cleanup removed only eligible terminal journals / saved autosaves. All recordings, intermediate media, jobs, incomplete and orphaned data were retained.");
        }
        finally { gate.Release(); }
    }
}
