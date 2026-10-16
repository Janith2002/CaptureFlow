using System.Runtime.InteropServices;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;

public class WindowsMouseEffectsTests
{
    [Fact] public async Task NativeMouseListenerStartsPausesResumesAndStopsWithoutCaptureOrInjectedInput()
    {
        var settings = new MouseEffectsPreferences(ClickEffects: true);
        await using var mouse = new WindowsMouseEffects(settings);
        await mouse.StartAsync(default); mouse.SetActive(false); mouse.SetActive(true);
        await mouse.DisposeAsync(); await mouse.DisposeAsync();
        await using var unstarted = new WindowsMouseEffects(settings); await unstarted.DisposeAsync();
    }
    [Fact] public async Task PreCancelledListenerDoesNotStartAndDisposesWithoutLeakingResources()
    {
        await using var mouse = new WindowsMouseEffects(new(ClickEffects: true)); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mouse.StartAsync(cancel.Token));
        await mouse.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Theory] [InlineData(0, 0)] [InlineData(60, 60)] [InlineData(127, 95)]
    public void NativeCursorShapesRenderAboveOpaqueHighlightAndClipToFrame(int x, int y)
    {
        var size = new VideoSize(128, 96); var pixels = new byte[128 * 96 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 255; pixels[i + 3] = 255; }
        var original = pixels.ToArray();
        using var renderer = new WindowsCursorRenderer(); var arrow = LoadCursor(0, new nint(32512)); Assert.NotEqual(0, arrow);
        renderer.Draw(pixels, size, new(x, y), arrow); Assert.False(original.SequenceEqual(pixels));
        Assert.All(pixels.Where((_, i) => i % 4 == 3), alpha => Assert.Equal(255, alpha));
        Array.Copy(original, pixels, original.Length); renderer.Draw(pixels, size, new(60, 60), LoadCursor(0, new nint(32513))); Assert.False(original.SequenceEqual(pixels));
        renderer.Dispose(); Assert.Throws<ObjectDisposedException>(() => renderer.Draw(pixels, size, new(60, 60), arrow));
    }
    [Fact] public void OutsideCursorDoesNotPaintAndRendererDisposesSafelyBeforeFirstDraw()
    {
        var pixels = new byte[16 * 16 * 4]; using var renderer = new WindowsCursorRenderer();
        renderer.Draw(pixels, new(16, 16), new(-1, 1), LoadCursor(0, new nint(32512))); Assert.All(pixels, b => Assert.Equal(0, b));
        renderer.Dispose(); renderer.Dispose();
    }
    [DllImport("user32.dll", EntryPoint = "LoadCursorW")] private static extern nint LoadCursor(nint instance, nint name);
}
