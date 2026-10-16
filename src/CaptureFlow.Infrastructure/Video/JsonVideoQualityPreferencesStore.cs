using System.Text.Json;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;

public sealed class JsonVideoQualityPreferencesStore(string path) : IVideoQualityPreferencesStore
{
    public async Task<VideoQualityPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        FileStream stream;
        try { stream = File.OpenRead(path); }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        await using var owned = stream;
        var preferences = await JsonSerializer.DeserializeAsync<VideoQualityPreferences>(stream, cancellationToken: cancellationToken) ?? throw new InvalidDataException("Video quality preferences are empty.");
        preferences.Validate(); return preferences;
    }
    public async Task SaveAsync(VideoQualityPreferences preferences, CancellationToken cancellationToken = default)
    {
        preferences.Validate(); var destination = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await JsonSerializer.SerializeAsync(stream, preferences, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
