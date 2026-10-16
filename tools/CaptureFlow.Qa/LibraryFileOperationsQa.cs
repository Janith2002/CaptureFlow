using System.IO;
using System.Runtime.InteropServices;
using CaptureFlow.Application.Library;
using CaptureFlow.Core.Library;
using CaptureFlow.Infrastructure.Library;
using NAudio.Wave;

internal static class LibraryFileOperationsQa
{
    public static async Task<int> RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts/qa/library-fileops", Guid.NewGuid().ToString("N")));
        var folder = Path.Combine(root, "generated-media"); Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "generated-qa.wav");
        using (var writer = new WaveFileWriter(source, new WaveFormat(48000, 16, 1))) writer.Write(new byte[9600]);
        try
        {
            var repository = new SqliteRecordingRepository(Path.Combine(root, "catalog.db"));
            var service = new RecordingLibraryService(repository, new Metadata(), new WindowsRecordingFileActions());
            var original = (await service.DiscoverAsync(folder, default)).Single();
            var renamedName = "qa-recycled-" + Guid.NewGuid().ToString("N") + ".wav";
            var renamed = await service.RenameAsync(original, renamedName, default);
            if (File.Exists(source) || !File.Exists(renamed.FullPath) || (await repository.GetFolderAsync(folder, default)).Single().FullPath != renamed.FullPath) throw new IOException("Filesystem/SQLite rename mismatch.");
            Console.WriteLine("PASS real Windows rename + SQLite update on new QA WAV: " + renamed.FullPath);
            if (await service.DeleteAsync(renamed, false, default) || !File.Exists(renamed.FullPath)) throw new IOException("Deletion without confirmation.");
            Console.WriteLine("PASS declined confirmation leaves QA file intact");
            await service.DeleteAsync(renamed, true, default);
            if (File.Exists(renamed.FullPath) || (await repository.GetFolderAsync(folder, default)).Count != 0) throw new IOException("Recycle/catalog removal failed.");
            if (!FindInRecycleBin(renamedName)) throw new IOException("File disappeared but Recycle Bin membership was not confirmed.");
            Console.WriteLine("PASS Windows Recycle Bin membership confirmed for newly generated QA WAV; no user files touched or recycle entries cleared.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL isolated library file operations: " + ex); return 1; }
    }
    private static bool FindInRecycleBin(string name)
    {
        object? shell = null, folder = null, items = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder = ((dynamic)shell!).NameSpace(10); items = ((dynamic)folder!).Items();
            foreach (object item in (System.Collections.IEnumerable)items!)
            {
                try
                {
                    string display = ((dynamic)item).Name;
                    if (display == name || display == Path.GetFileNameWithoutExtension(name)) return true;
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            return false;
        }
        finally { if (items is not null) Marshal.ReleaseComObject(items); if (folder is not null) Marshal.ReleaseComObject(folder); if (shell is not null) Marshal.ReleaseComObject(shell); }
    }
    private sealed class Metadata : IMediaMetadataReader
    {
        public Task<TimeSpan?> ReadDurationAsync(string path, CancellationToken token) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(100));
    }
}
