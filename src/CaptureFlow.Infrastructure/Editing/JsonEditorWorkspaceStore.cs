using System.Text.Json;
using CaptureFlow.Core.Editing;
namespace CaptureFlow.Infrastructure.Editing;
public sealed class JsonEditorWorkspaceStore(string path) : IEditorWorkspaceStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<EditorWorkspace> LoadAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            FileStream stream; try { stream = File.OpenRead(path); } catch (FileNotFoundException) { return new(); } catch (DirectoryNotFoundException) { return new(); }
            await using var owned = stream; if (stream.Length > 4096) throw new InvalidDataException("Workspace settings exceed size limit.");
            var value = await JsonSerializer.DeserializeAsync<EditorWorkspace>(stream, cancellationToken: token) ?? throw new InvalidDataException("Empty workspace settings."); value.Validate(); return value;
        }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(EditorWorkspace settings, CancellationToken token = default)
    {
        settings.Validate(); await gate.WaitAsync(token); var destination = Path.GetFullPath(path); var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true)) await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: token);
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); gate.Release(); }
    }
}
