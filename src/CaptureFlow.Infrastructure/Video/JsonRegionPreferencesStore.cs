using System.Text.Json;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;
public sealed class JsonRegionPreferencesStore(string path) : IRegionPreferencesStore
{
    public async Task<ScreenRegion?> LoadAsync(CancellationToken cancellationToken = default)
    {
        FileStream stream;
        try { stream = File.OpenRead(path); } catch (FileNotFoundException) { return null; } catch (DirectoryNotFoundException) { return null; }
        await using var owned = stream; var region = await JsonSerializer.DeserializeAsync<ScreenRegion>(stream, cancellationToken: cancellationToken);
        region?.Validate(); return region;
    }
    public async Task SaveAsync(ScreenRegion region, CancellationToken cancellationToken = default)
    {
        region.Validate(); var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true)) await JsonSerializer.SerializeAsync(stream, region, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
