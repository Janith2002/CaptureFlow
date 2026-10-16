using System.Text.Json;
using CaptureFlow.Core.Desktop;
namespace CaptureFlow.Infrastructure.Desktop;
public sealed class JsonRecordingExperienceStore(string path) : IRecordingExperienceStore
{
    public async Task<RecordingExperiencePreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        FileStream stream;
        try { stream = File.OpenRead(path); }
        catch (FileNotFoundException) { return new(); } catch (DirectoryNotFoundException) { return new(); }
        await using var owned = stream;
        var preferences = await JsonSerializer.DeserializeAsync<RecordingExperiencePreferences>(stream, cancellationToken: cancellationToken) ?? throw new InvalidDataException("Recording experience preferences are empty.");
        preferences.Validate(); return preferences;
    }
    public async Task SaveAsync(RecordingExperiencePreferences preferences, CancellationToken cancellationToken = default)
    {
        preferences.Validate(); var destination = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
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
