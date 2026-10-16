using CaptureFlow.Core.Library;

namespace CaptureFlow.Application.Library;

public sealed class RecordingLibraryService(IRecordingRepository repository, IMediaMetadataReader metadata, IRecordingFileActions files) : IRecordingLibraryService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<IReadOnlyList<RecordingEntry>> DiscoverAsync(string folder, CancellationToken token)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await RecoverRenamesAsync(token).ConfigureAwait(false);
            var known = (await repository.GetFolderAsync(folder, token).ConfigureAwait(false)).ToDictionary(e => e.FullPath, StringComparer.OrdinalIgnoreCase);
            // Enumeration must succeed before missing entries are inferred; access failure isn't absence.
            var paths = await Task.Run(() =>
            {
                try { return Directory.GetFiles(folder); }
                catch (DirectoryNotFoundException) { return []; }
            }, token).ConfigureAwait(false);
            var result = new List<RecordingEntry>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension is not (".mp4" or ".mp3" or ".wav")) continue;
                seen.Add(path); known.TryGetValue(path, out var existing);
                try
                {
                    var info = new FileInfo(path);
                    var changed = existing is null || existing.IsMissing || existing.Bytes != info.Length || existing.Modified.UtcDateTime != info.LastWriteTimeUtc;
                    var duration = changed ? await metadata.ReadDurationAsync(path, token).ConfigureAwait(false) : existing!.Duration;
                    result.Add(new(existing?.Id ?? Guid.NewGuid(), path, folder, existing?.Type ?? RecordingLibraryQuery.InferType(path), extension[1..].ToUpperInvariant(),
                        duration, info.Length, new(info.CreationTimeUtc), new(info.LastWriteTimeUtc), Warning: duration is null ? "Duration unavailable; playback will report unsupported or damaged media." : null));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    var missing = ex is FileNotFoundException or DirectoryNotFoundException;
                    if (existing is not null) result.Add(existing with { Warning = "File unavailable: " + ex.Message, IsMissing = missing });
                    else result.Add(new(Guid.NewGuid(), path, folder, RecordingLibraryQuery.InferType(path), extension[1..].ToUpperInvariant(), null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, missing, ex.Message));
                }
            }
            result.AddRange(known.Values.Where(e => !seen.Contains(e.FullPath)).Select(e => e with { IsMissing = true, Warning = "File missing or moved externally. Refresh after restoring it." }));
            await repository.SaveAsync(result, token).ConfigureAwait(false);
            return result;
        }
        finally { gate.Release(); }
    }
    public async Task<RecordingEntry> RenameAsync(RecordingEntry entry, string name, CancellationToken token)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' ')) throw new ArgumentException("Enter a valid filename without a folder path.");
        if (!Path.GetExtension(name).Equals("." + entry.Format, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Keep the original file extension.");
        var stem = Path.GetFileNameWithoutExtension(name).Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9') throw new ArgumentException("This filename is reserved by Windows.");
        var target = Path.Combine(entry.Folder, name);
        if (string.Equals(entry.FullPath, target, StringComparison.OrdinalIgnoreCase)) return entry;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            RequireUnchanged(entry);
            if (File.Exists(target)) throw new IOException("A file with that name already exists.");
            await repository.BeginRenameAsync(new(entry.Id, entry.FullPath, target), token).ConfigureAwait(false);
            var moved = false;
            try
            {
                token.ThrowIfCancellationRequested(); files.Rename(entry.FullPath, target); moved = true;
                var renamed = entry with { FullPath = target };
                // Once moved, complete or compensate even if the caller cancels.
                await repository.CompleteRenameAsync(renamed, CancellationToken.None).ConfigureAwait(false);
                return renamed;
            }
            catch (Exception original)
            {
                try
                {
                    if (moved) files.Rename(target, entry.FullPath);
                    await repository.CancelRenameAsync(entry.Id, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollback) { throw new AggregateException("Rename needs recovery. Files were not overwritten; refresh to reconcile the saved rename journal.", original, rollback); }
                throw;
            }
        }
        finally { gate.Release(); }
    }
    public async Task<bool> DeleteAsync(RecordingEntry entry, bool confirmed, CancellationToken token)
    {
        if (!confirmed) return false;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (FileExistsOrThrow(entry.FullPath))
            {
                RequireUnchanged(entry);
                await files.RecycleAsync(entry.FullPath, token).ConfigureAwait(false);
                if (File.Exists(entry.FullPath)) throw new IOException("The file was not recycled. Its library entry is retained.");
            }
            // A database failure leaves a missing entry; refresh reconciles without deleting media.
            await repository.RemoveAsync(entry.Id, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        finally { gate.Release(); }
    }
    private async Task RecoverRenamesAsync(CancellationToken token)
    {
        foreach (var pending in await repository.GetRenamesAsync(token).ConfigureAwait(false))
        {
            var oldExists = FileExistsOrThrow(pending.OldPath); var newExists = FileExistsOrThrow(pending.NewPath);
            if (oldExists && newExists) throw new IOException("A pending rename has files at both locations. Resolve the conflict without overwriting either file: " + pending.NewPath);
            if (newExists)
            {
                var entry = (await repository.GetFolderAsync(Path.GetDirectoryName(pending.OldPath)!, token).ConfigureAwait(false)).FirstOrDefault(e => e.Id == pending.Id);
                if (entry is not null) await repository.CompleteRenameAsync(entry with { FullPath = pending.NewPath }, token).ConfigureAwait(false);
                else await repository.CancelRenameAsync(pending.Id, token).ConfigureAwait(false);
            }
            else await repository.CancelRenameAsync(pending.Id, token).ConfigureAwait(false);
        }
    }
    private static void RequireUnchanged(RecordingEntry entry)
    {
        _ = File.GetAttributes(entry.FullPath); // Preserve access errors rather than misreporting absence.
        var info = new FileInfo(entry.FullPath);
        if (!info.Exists) throw new FileNotFoundException("Recording is missing or moved. Refresh the library.", entry.FullPath);
        if (info.Length != entry.Bytes || info.LastWriteTimeUtc != entry.Modified.UtcDateTime) throw new IOException("The recording changed outside CaptureFlow. Refresh before changing it.");
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Manage linked recordings in File Explorer; library file actions require a regular file.");
    }
    private static bool FileExistsOrThrow(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}
