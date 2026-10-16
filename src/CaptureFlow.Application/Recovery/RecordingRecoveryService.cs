using System.Security.Cryptography;
using CaptureFlow.Core.Recovery;
using CaptureFlow.Core.Library;
namespace CaptureFlow.Application.Recovery;

public sealed class RecordingRecoveryService(IRecoveryJournal journal, IRecoveryMediaBackend backend, IRecordingRepository repository)
{
    private readonly SemaphoreSlim gate = new(1);
    public async Task<RecoveredMedia> RecoverAsync(RecoveryCandidate candidate, string ffmpeg, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            using var lease = await journal.AcquireRecoveryAsync(candidate.Record.Id, token);
            var current = (await journal.ScanAsync(token)).SingleOrDefault(c => c.Record.Id == candidate.Record.Id)
                ?? throw new InvalidOperationException("This session was already recovered or dismissed.");
            if (current.IsLive || current.Record.Mode == "Editor") throw new InvalidOperationException("An active recording or editor project cannot be encoded as recovered media.");
            if (current.Record.State == "Unreadable") throw new IOException("The recovery journal is unreadable. Restore its metadata before recovering; original files have not been changed.");
            var record = current.Record; RecoveredMedia media;
            if (record.RecoveredFile is not null && record.OutputHash is not null && File.Exists(record.RecoveredFile))
            {
                if (await HashAsync(record.RecoveredFile, token) != record.OutputHash) throw new IOException("The pending recovered output changed externally. It will not be overwritten.");
                media = await backend.ValidateAsync(record.RecoveredFile, ffmpeg, token);
            }
            else
            {
                var job = Path.Combine(journal.Preferences.StorageLocation, record.Id.ToString("N"), "jobs", Guid.NewGuid().ToString("N"));
                var pending = Path.Combine(job, "recovered." + record.Format.ToLowerInvariant());
                media = await backend.RecoverAsync(current, pending, ffmpeg, token);
                Directory.CreateDirectory(record.OutputDirectory);
                var final = Path.Combine(record.OutputDirectory, "Recovered_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N") + "." + record.Format.ToLowerInvariant());
                record = record with { RecoveredFile = final, OutputHash = await HashAsync(pending, token), State = "LibraryPending", Message = media.Warning };
                await journal.SaveRecordAsync(record, token);
                // Copy handles a custom recovery root on another volume. Source remains available.
                var publishFolder = Path.Combine(record.OutputDirectory, ".captureflow-work", "recovery-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(publishFolder);
                var publish = Path.Combine(publishFolder, "validated." + record.Format.ToLowerInvariant());
                await using (var input = File.OpenRead(pending))
                await using (var output = new FileStream(publish, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                { await input.CopyToAsync(output, token); output.Flush(true); }
                media = await backend.ValidateAsync(publish, ffmpeg, token);
                token.ThrowIfCancellationRequested();
                File.Move(publish, final, false); media = media with { Path = final };
            }
            var type = record.Format != "MP4" ? RecordingType.Audio : record.Mode == "Webcam" ? RecordingType.Webcam : record.Mode == "ScreenAndWebcam" ? RecordingType.ScreenAndWebcam : RecordingType.Screen;
            var info = new FileInfo(media.Path);
            await repository.SaveAsync([new(record.Id, media.Path, record.OutputDirectory, type, record.Format, TimeSpan.FromSeconds(media.Duration), info.Length, record.Started, DateTimeOffset.UtcNow, Warning: record.Message)], token);
            await journal.SaveRecordAsync(record with { State = "Recovered", Updated = DateTimeOffset.UtcNow }, token);
            return media with { Warning = record.Message ?? media.Warning };
        }
        finally { gate.Release(); }
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    { await using var file = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(file, token)); }
}
