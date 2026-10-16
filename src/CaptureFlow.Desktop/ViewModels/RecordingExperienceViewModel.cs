using CaptureFlow.Core.Desktop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace CaptureFlow.Desktop.ViewModels;
public partial class RecordingExperienceViewModel(IRecordingExperienceStore? store = null) : ObservableObject
{
    private readonly SemaphoreSlim saveGate = new(1);
    [ObservableProperty] private string selectedCountdown = "3 seconds";
    [ObservableProperty] private bool showToolbar = true;
    [ObservableProperty] private bool toolbarTopmost = true;
    [ObservableProperty] private string message = "Countdown and toolbar preferences can be saved across restarts. Toolbar position saves when moved.";
    public string[] CountdownOptions { get; } = ["Off", "3 seconds", "5 seconds", "10 seconds"];
    public int CountdownSeconds => SelectedCountdown switch { "Off" => 0, "3 seconds" => 3, "5 seconds" => 5, "10 seconds" => 10, _ => throw new ArgumentException("Choose a supported countdown.") };
    public ToolbarAnchor? Anchor { get; set; }
    public async Task LoadAsync()
    {
        try { if (store is null) return; var p = await store.LoadAsync(); SelectedCountdown = p.CountdownSeconds == 0 ? "Off" : $"{p.CountdownSeconds} seconds"; ShowToolbar = p.ShowToolbar; ToolbarTopmost = p.ToolbarTopmost; Anchor = p.Anchor; }
        catch (Exception ex) { Message = "Could not load countdown/toolbar preferences: " + ex.Message; }
    }
    [RelayCommand] public async Task SaveAsync()
    {
        await saveGate.WaitAsync();
        try { var p = new RecordingExperiencePreferences(CountdownSeconds, ShowToolbar, ToolbarTopmost, Anchor); p.Validate(); if (store is not null) await store.SaveAsync(p); Message = "Countdown and toolbar preferences saved."; }
        catch (Exception ex) { Message = "Could not save countdown/toolbar preferences: " + ex.Message; }
        finally { saveGate.Release(); }
    }
}
