using CaptureFlow.Core.Video;
using CommunityToolkit.Mvvm.ComponentModel;
namespace CaptureFlow.Desktop.ViewModels;

public partial class ScreenRecorderViewModel
{
    private readonly IMouseEffectsPreferencesStore? mouseStore;
    private readonly SemaphoreSlim mouseSaveGate = new(1, 1);
    private bool loadingMouse;
    private Task mouseSave = Task.CompletedTask;
    [ObservableProperty] private bool highlightCursor;
    [ObservableProperty] private string highlightColor = "#60A5FA";
    [ObservableProperty] private double highlightRadius = 24;
    [ObservableProperty] private double highlightOpacity = .3;
    [ObservableProperty] private bool showLeftClicks = true;
    [ObservableProperty] private bool showRightClicks = true;
    [ObservableProperty] private string leftClickColor = "#60A5FA";
    [ObservableProperty] private string rightClickColor = "#C084FC";
    [ObservableProperty] private double clickDurationMilliseconds = 500;
    [ObservableProperty] private string mouseEffectsMessage = "Effects are rendered into video before the webcam overlay. Size is in source pixels; no desktop overlay is shown.";
    public string[] MouseColorOptions { get; } = ["#60A5FA", "#C084FC", "#FBBF24", "#34D399", "#F87171", "#FFFFFF"];
    public MouseEffectsPreferences MousePreferences => new(ShowMouseCursor, HighlightCursor, HighlightColor, HighlightRadius, HighlightOpacity,
        HighlightMouseClicks, ShowLeftClicks, ShowRightClicks, LeftClickColor, RightClickColor, ClickDurationMilliseconds);
    private bool ValidMousePreferences { get { try { MousePreferences.Validate(); return true; } catch (ArgumentException) { return false; } } }
    private void MousePreferencesChanged()
    {
        if (loadingMouse) return;
        SourceSelectionChanged();
        if (!ValidMousePreferences) { MouseEffectsMessage = "Choose valid mouse effect colors, size, opacity and duration before recording."; return; }
        mouseSave = SaveMouseAsync(MousePreferences);
    }
    private async Task SaveMouseAsync(MouseEffectsPreferences preferences)
    {
        if (mouseStore is null) return;
        await mouseSaveGate.WaitAsync();
        try { await mouseStore.SaveAsync(preferences); MouseEffectsMessage = "Mouse preferences saved. Effects are encoded before the webcam overlay; size is in source pixels."; }
        catch (Exception ex) { MouseEffectsMessage = "Could not save mouse preferences: " + ex.Message; }
        finally { mouseSaveGate.Release(); }
    }
    public async Task LoadMousePreferencesAsync()
    {
        if (mouseStore is null) return;
        loadingMouse = true;
        try
        {
            var p = await mouseStore.LoadAsync();
            ShowMouseCursor = p.ShowCursor; HighlightCursor = p.Highlight; HighlightColor = p.HighlightColor; HighlightRadius = p.Radius; HighlightOpacity = p.Opacity;
            HighlightMouseClicks = p.ClickEffects; ShowLeftClicks = p.LeftClicks; ShowRightClicks = p.RightClicks; LeftClickColor = p.LeftColor; RightClickColor = p.RightColor; ClickDurationMilliseconds = p.ClickDurationMilliseconds;
        }
        catch (Exception ex) { MouseEffectsMessage = "Could not load mouse preferences: " + ex.Message; }
        finally { loadingMouse = false; SourceSelectionChanged(); }
    }
    partial void OnHighlightCursorChanged(bool value) => MousePreferencesChanged();
    partial void OnHighlightColorChanged(string value) => MousePreferencesChanged();
    partial void OnHighlightRadiusChanged(double value) => MousePreferencesChanged();
    partial void OnHighlightOpacityChanged(double value) => MousePreferencesChanged();
    partial void OnHighlightMouseClicksChanged(bool value) => MousePreferencesChanged();
    partial void OnShowLeftClicksChanged(bool value) => MousePreferencesChanged();
    partial void OnShowRightClicksChanged(bool value) => MousePreferencesChanged();
    partial void OnLeftClickColorChanged(string value) => MousePreferencesChanged();
    partial void OnRightClickColorChanged(string value) => MousePreferencesChanged();
    partial void OnClickDurationMillisecondsChanged(double value) => MousePreferencesChanged();
}
