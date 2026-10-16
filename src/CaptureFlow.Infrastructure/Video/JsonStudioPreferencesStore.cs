using System.Text.Json;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;

public sealed class JsonStudioPreferencesStore(string path) : IStudioPreferencesStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<StudioPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            FileStream stream;
            try { stream = File.OpenRead(path); }
            catch (FileNotFoundException) { return new(); }
            catch (DirectoryNotFoundException) { return new(); }
            await using var owned = stream;
            if (stream.Length > 1_048_576) throw new InvalidDataException("Studio preferences exceed the safe size limit.");
            var value = await JsonSerializer.DeserializeAsync<StudioPreferences>(stream, cancellationToken: cancellationToken) ?? throw new InvalidDataException("Empty studio preferences.");
            value.Validate(); return value;
        }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(StudioPreferences preferences, CancellationToken cancellationToken = default)
    {
        preferences.Validate(); await gate.WaitAsync(cancellationToken);
        var destination = Path.GetFullPath(path);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await JsonSerializer.SerializeAsync(stream, preferences, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); gate.Release(); }
    }
}
