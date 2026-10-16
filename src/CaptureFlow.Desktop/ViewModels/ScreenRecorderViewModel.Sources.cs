using System.Collections.ObjectModel;
using CaptureFlow.Core.Video;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace CaptureFlow.Desktop.ViewModels;
public partial class ScreenRecorderViewModel
{
    private readonly IApplicationWindowDiscovery? windowDiscovery;
    private readonly IRegionSelection? regionSelection;
    private readonly IRegionPreferencesStore? regionStore;
    private readonly CancellationTokenSource sourceSelectionCancellation = new();
    private Task sourceChange = Task.CompletedTask;
    private ScreenRegion? savedRegion;
    [ObservableProperty] private string selectedScreenSource = "Entire Screen";
    [ObservableProperty] private ApplicationWindowDescriptor? selectedApplicationWindow;
    [ObservableProperty] private ScreenRegion? selectedRegion;
    [ObservableProperty] private string sourceSelectionMessage = "Select Entire Screen, an application window, or a custom region. Window resize/minimize stops capture safely; no desktop fallback is used.";
    public string[] ScreenSourceOptions { get; } = ["Entire Screen", "Application Window", "Custom Region"];
    public ObservableCollection<ApplicationWindowDescriptor> ApplicationWindows { get; } = [];
    public bool IsWindowSource => IsScreen && SelectedScreenSource == "Application Window";
    public bool IsRegionSource => IsScreen && SelectedScreenSource == "Custom Region";
    public bool UsesMonitor => IsScreen && !IsWindowSource;
    public VideoSourceOptions? SelectedScreenOptions => SelectedScreenSource switch
    {
        "Entire Screen" when SelectedMonitor is { } monitor => new(VideoSourceKind.Monitor, monitor.Id, ShowMouseCursor, MouseEffects: MousePreferences),
        "Application Window" when SelectedApplicationWindow is { } window => new(VideoSourceKind.Window, window.Id, ShowMouseCursor, Window: window, MouseEffects: MousePreferences),
        "Custom Region" when SelectedMonitor is { } monitor && SelectedRegion is { } region && region.Matches(monitor) => new(VideoSourceKind.Region, monitor.Id, ShowMouseCursor, Region: region, MouseEffects: MousePreferences),
        _ => null
    };
    public VideoSize? SelectedScreenSize => SelectedScreenOptions is { } source ? source.Kind switch
    { VideoSourceKind.Window => new(source.Window!.Width, source.Window.Height), VideoSourceKind.Region => new(source.Region!.Width, source.Region.Height), _ => new(SelectedMonitor!.Width, SelectedMonitor.Height) } : null;
    public string RegionDescription => SelectedRegion is { } r ? $"{r.Width} × {r.Height} px at ({r.X}, {r.Y}) within {r.MonitorId}. Display-size changes require reselection." : "No region selected. Select at least 16 × 16 pixels on the chosen monitor.";
    private void SourceSelectionChanged()
    {
        OnPropertyChanged(nameof(IsWindowSource)); OnPropertyChanged(nameof(IsRegionSource)); OnPropertyChanged(nameof(UsesMonitor)); OnPropertyChanged(nameof(RegionDescription));
        OnPropertyChanged(nameof(CombinedSourceOptions)); OnPropertyChanged(nameof(PreviewPixelWidth)); OnPropertyChanged(nameof(PreviewPixelHeight));
        RefreshCommands(); NotifyOverlayBounds();
        if (!disposed && IsCombined && combined.IsPreviewing && !combined.IsRecording) sourceChange = InvalidateCombinedPreviewAsync();
        if (!disposed && !IsCameraMode && screenPreview?.ActiveOptions is { } active && active != SelectedScreenOptions && !screenPreview.IsRecording) sourceChange = InvalidateScreenPreviewAsync();
    }
    private async Task InvalidateScreenPreviewAsync()
    {
        IsCameraBusy = true; ResetPreview();
        try { await screenPreview!.StopAsync(); StudioMessage = "Screen source changed. Start preview to apply the new source."; }
        catch (Exception ex) { StudioMessage = ex.Message; }
        finally { IsCameraBusy = false; }
    }
    private async Task InvalidateCombinedPreviewAsync()
    {
        IsCameraBusy = true; ResetPreview();
        try { await combined.StopPreviewAsync(); CameraMessage = "Screen source changed. Start preview to apply the selected window or region before recording."; }
        catch (Exception ex) { CameraMessage = ex.Message; }
        finally { IsCameraBusy = false; }
    }
    partial void OnSelectedScreenSourceChanged(string value) => SourceSelectionChanged();
    partial void OnSelectedApplicationWindowChanged(ApplicationWindowDescriptor? value) => SourceSelectionChanged();
    partial void OnSelectedRegionChanged(ScreenRegion? value) => SourceSelectionChanged();
    [RelayCommand(CanExecute = nameof(CanEditPreferences))]
    private async Task RefreshWindowsAsync()
    {
        IsBusy = true;
        try
        {
            var previousId = SelectedApplicationWindow?.Id;
            var windows = windowDiscovery is null ? [] : await windowDiscovery.GetWindowsAsync(sourceSelectionCancellation.Token);
            ApplicationWindows.Clear(); foreach (var window in windows) ApplicationWindows.Add(window);
            SelectedApplicationWindow = ApplicationWindows.FirstOrDefault(w => w.Id == previousId);
            SourceSelectionMessage = windows.Count == 0 ? "No eligible application windows. Restore a visible application window and refresh. CaptureFlow and protected/tool windows are excluded." : $"{windows.Count} eligible windows. Select a window; closure, resize or minimization interrupts recording with retained intermediates.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SourceSelectionMessage = "Could not enumerate application windows: " + ex.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanSelectRegion))]
    private async Task SelectRegionAsync()
    {
        if (SelectedMonitor is not { } monitor || regionSelection is null) return;
        IsBusy = true;
        try
        {
            await sourceChange;
            if (IsCombined && combined.IsPreviewing) { ResetPreview(); await combined.StopPreviewAsync(); }
            var selected = await regionSelection.SelectAsync(monitor, SelectedRegion, sourceSelectionCancellation.Token);
            if (selected is not null)
            {
                selected.Validate(); if (!selected.Matches(monitor)) throw new InvalidOperationException("The selected monitor geometry changed.");
                SelectedRegion = savedRegion = selected;
                if (regionStore is not null) await regionStore.SaveAsync(selected, sourceSelectionCancellation.Token);
                SourceSelectionMessage = "Region confirmed. Only its pixels are passed to the encoder/compositor. Start preview again for Screen + Webcam.";
            }
            else SourceSelectionMessage = "Region selection cancelled; the previous confirmed region is retained.";
        }
        catch (OperationCanceledException) { SourceSelectionMessage = "Region selection cancelled."; }
        catch (Exception ex) { SourceSelectionMessage = "Region selection failed: " + ex.Message; }
        finally { IsBusy = false; }
    }
    private bool CanSelectRegion() => CanEditPreferences && IsRegionSource && SelectedMonitor is not null;
    public async Task LoadRegionPreferencesAsync()
    {
        try { if (regionStore is not null) savedRegion = await regionStore.LoadAsync(sourceSelectionCancellation.Token); RestoreRegionForMonitor(); }
        catch (Exception ex) { SourceSelectionMessage = "Could not load region preferences: " + ex.Message; }
    }
    private void RestoreRegionForMonitor()
    { SelectedRegion = SelectedMonitor is { } monitor && savedRegion?.Matches(monitor) == true ? savedRegion : null; }
    public async Task StopScreenSelectionOperationsAsync()
    {
        sourceSelectionCancellation.Cancel();
        foreach (var command in new[] { SelectRegionCommand, RefreshWindowsCommand }) if (command.IsRunning && command.ExecutionTask is { } task) await task;
        await sourceChange;
        await mouseSave;
    }
}
