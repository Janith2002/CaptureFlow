using System.Text.Json;
using CaptureFlow.Core.Editing;
namespace CaptureFlow.Infrastructure.Editing;
public sealed class JsonEditorProjectStore : IEditorProjectStore
{
    public async Task SaveAsync(EditorProject project, string path, CancellationToken token)
    {
        project.Validate(); path = Path.GetFullPath(path);
        if (!path.EndsWith(".cfedit", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use the .cfedit project extension.");
        if (project.Media.Any(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase))) throw new IOException("Cannot replace an imported source with a project.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await JsonSerializer.SerializeAsync(file, project, new JsonSerializerOptions { WriteIndented = true }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task<EditorProject> LoadAsync(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Project exceeds the 4 MB safety limit.");
        await using var file = File.OpenRead(path);
        var project = await JsonSerializer.DeserializeAsync<EditorProject>(file, new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }, token).ConfigureAwait(false) ?? throw new IOException("Empty project.");
        project.Validate(); return project;
    }
}

