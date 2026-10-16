using System.Text.Json;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;

public sealed class JsonMouseEffectsPreferencesStore(string path) : IMouseEffectsPreferencesStore
{
    public async Task<MouseEffectsPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        FileStream stream;
        try { stream = File.OpenRead(path); }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        await using var owned = stream;
        var preferences = await JsonSerializer.DeserializeAsync<MouseEffectsPreferences>(stream, cancellationToken: cancellationToken) ?? throw new InvalidDataException("Mouse effect preferences are empty.");
        preferences.Validate(); return preferences;
    }
    public async Task SaveAsync(MouseEffectsPreferences preferences, CancellationToken cancellationToken = default)
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

