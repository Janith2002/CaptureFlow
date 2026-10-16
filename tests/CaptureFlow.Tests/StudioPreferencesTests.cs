using CaptureFlow.Core.Video;
using CaptureFlow.Core.Audio;
using CaptureFlow.Infrastructure.Video;
using System.Text.Json;
namespace CaptureFlow.Tests;
public class StudioPreferencesTests
{
    [Theory] [InlineData(1)] [InlineData(1.25)] [InlineData(1.5)]
    public void DockGeometryRetainsPanelMinimumsAcrossFractionalDpi(double dpi)
    {
        var converter = new CaptureFlow.Desktop.Converters.StudioWidthConverter();
        foreach (var width in new[] { 760d, 900d, 1320d, 1920d })
        {
            var studio = (double)converter.Convert(width, typeof(double), null!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(Math.Floor(studio * dpi) >= Math.Ceiling(220 * dpi) + Math.Ceiling(300 * dpi) + Math.Ceiling(220 * dpi) + Math.Ceiling(12 * dpi));
        }
    }
    private static StudioPreset Preset(string name = "Desktop") => new(Guid.NewGuid(), name, "Screen Only", "Entire Screen", "monitor", null, null, null, null, false, null, false, null, new(), "3 seconds");
    [Fact] public async Task WorkspaceAndPresetsRoundTripWithoutTouchingMedia()
    {
        var root = Path.Combine(Path.GetTempPath(), "CaptureFlowStudioTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var media = Path.Combine(root, "keep.mp4"); await File.WriteAllTextAsync(media, "owned preservation fixture");
        var store = new JsonStudioPreferencesStore(Path.Combine(root, "studio.json")); Assert.Equal(new StudioPreferences(), await store.LoadAsync());
        var expected = new StudioPreferences(240, 320, 220, false, false, [Preset()]); await store.SaveAsync(expected);
        var actual = await store.LoadAsync(); Assert.Equal(expected.SourcesWidth, actual.SourcesWidth); Assert.False(actual.ShowMixer); Assert.False(actual.ShowProperties); Assert.Equal(expected.Presets![0], actual.Presets![0]);
        Assert.Equal("owned preservation fixture", await File.ReadAllTextAsync(media)); Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }
    [Fact] public void DuplicateNamesAndIdentitiesAreRejected()
    {
        var one = Preset(); Assert.Throws<ArgumentException>(() => new StudioPreferences(Presets: [one, Preset(" desktop ")]).Validate());
        Assert.Throws<ArgumentException>(() => new StudioPreferences(Presets: [one, one with { Name = "Other" }]).Validate());
    }
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(0)] [InlineData(701)]
    public void UnsafePanelSizesAreRejected(double width) => Assert.Throws<ArgumentException>(() => new StudioPreferences(PropertiesWidth: width).Validate());
    [Theory] [InlineData("Scenes")] [InlineData("Streaming")]
    public void UnsupportedModesAreRejected(string mode) => Assert.Throws<ArgumentException>(() => (Preset() with { Mode = mode }).Validate());
    [Fact] public async Task CancellationPreservesLastGoodPreferences()
    {
        var path = Path.Combine(Path.GetTempPath(), "CaptureFlowStudioTests", Guid.NewGuid().ToString("N"), "studio.json"); var store = new JsonStudioPreferencesStore(path);
        await store.SaveAsync(new()); var original = await File.ReadAllBytesAsync(path); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(ShowMixer: false), cancellation.Token)); Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }
    [Fact] public async Task CorruptPreferencesAreReportedRatherThanOverwritten()
    {
        var path = Path.Combine(Path.GetTempPath(), "CaptureFlowStudioTests", Guid.NewGuid().ToString("N"), "studio.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "broken");
        await Assert.ThrowsAsync<JsonException>(() => new JsonStudioPreferencesStore(path).LoadAsync()); Assert.Equal("broken", await File.ReadAllTextAsync(path));
    }
    [Theory] [InlineData(false, 1, 1, 0)] [InlineData(false, .5, .5, -6.0206)] [InlineData(true, 1, 0, -60)]
    public void MeterUsesRealSamplesGainMuteAndLogScale(bool muted, float gain, double peak, double db)
    {
        var levels = AudioLevels.Measure(true, [1, -1, 0, 0], gain, muted, 123); Assert.Equal(peak, levels.Peak, 5); Assert.Equal(peak / Math.Sqrt(2), levels.Rms, 5); Assert.Equal(db, AudioLevels.Decibels(levels.Peak), 3); Assert.Equal(123, levels.Timestamp);
    }
    [Fact] public void MeterHandlesSilenceAndNonFiniteSamples()
    { Assert.Equal(0, AudioLevels.Measure(false, [float.NaN, float.PositiveInfinity, 0], 1, false, 1).Peak); Assert.Equal(0, AudioLevels.Measure(false, [], 1, false, 1).Rms); }
    [Fact] public void RecoverySerializationDoesNotPersistCallback()
    { var json = JsonSerializer.Serialize(new AudioRecordingOptions("mic", "owned", LevelObserver: _ => throw new Exception())); Assert.DoesNotContain("LevelObserver", json); Assert.Null(JsonSerializer.Deserialize<AudioRecordingOptions>(json)!.LevelObserver); }
}
