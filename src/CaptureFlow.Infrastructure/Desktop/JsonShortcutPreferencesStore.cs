using System.Text.Json;
using CaptureFlow.Core.Desktop;
namespace CaptureFlow.Infrastructure.Desktop;

public sealed class JsonShortcutPreferencesStore(string path) : IShortcutPreferencesStore
{
    public async Task<ShortcutPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        FileStream stream;
        try { stream = File.OpenRead(path); }
        catch (FileNotFoundException) { return ShortcutPreferences.Defaults; }
        catch (DirectoryNotFoundException) { return ShortcutPreferences.Defaults; }
        await using var ownedStream = stream;
        var settings = await JsonSerializer.DeserializeAsync<ShortcutPreferences>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Shortcut preferences are empty.");
        settings.Validate(); return settings;
    }
    public async Task SaveAsync(ShortcutPreferences preferences, CancellationToken cancellationToken = default)
    {
        preferences.Validate();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await JsonSerializer.SerializeAsync(stream, preferences, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
