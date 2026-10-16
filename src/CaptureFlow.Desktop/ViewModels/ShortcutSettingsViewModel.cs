using CaptureFlow.Core.Desktop;
using CaptureFlow.Application.Desktop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace CaptureFlow.Desktop.ViewModels;

public partial class ShortcutSettingsViewModel(IShortcutPreferencesStore store) : ObservableObject
{
    private ShortcutRegistration? registration;
    private ShortcutPreferences applied = ShortcutPreferences.Defaults;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private bool minimizeToTray;
    [ObservableProperty] private string startStop = "Ctrl+Alt+R";
    [ObservableProperty] private string pauseResume = "Ctrl+Alt+P";
    [ObservableProperty] private string toggleMicrophone = "Ctrl+Alt+M";
    [ObservableProperty] private string toggleWindow = "Ctrl+Alt+C";
    [ObservableProperty] private string message = "Shortcut and tray preferences are saved across restarts. Click Save to apply changes.";
    public bool AppliedMinimizeToTray => applied.MinimizeToTray;
    public async Task InitializeAsync(ShortcutRegistration shortcuts)
    {
        registration = shortcuts;
        try { applied = await store.LoadAsync(); LoadFields(applied); shortcuts.Apply(applied); Message = applied.Enabled ? "All four global shortcuts are registered. Preferences are saved across restarts." : "Global shortcuts are disabled. Preferences are saved across restarts."; }
        catch (Exception ex) { Message = "Shortcuts unavailable: " + ex.Message; }
    }
    private void LoadFields(ShortcutPreferences p)
    {
        Enabled = p.Enabled; MinimizeToTray = p.MinimizeToTray;
        StartStop = p.Bindings.Single(b => b.Action == ShortcutAction.StartStop).Display;
        PauseResume = p.Bindings.Single(b => b.Action == ShortcutAction.PauseResume).Display;
        ToggleMicrophone = p.Bindings.Single(b => b.Action == ShortcutAction.ToggleMicrophone).Display;
        ToggleWindow = p.Bindings.Single(b => b.Action == ShortcutAction.ToggleWindow).Display;
    }
    [RelayCommand]
    private async Task SaveAsync()
    {
        var previous = applied;
        try
        {
            var next = new ShortcutPreferences(Enabled, MinimizeToTray, [
                ShortcutBinding.Parse(ShortcutAction.StartStop, StartStop), ShortcutBinding.Parse(ShortcutAction.PauseResume, PauseResume),
                ShortcutBinding.Parse(ShortcutAction.ToggleMicrophone, ToggleMicrophone), ShortcutBinding.Parse(ShortcutAction.ToggleWindow, ToggleWindow)]);
            next.Validate(); registration?.Apply(next);
            await store.SaveAsync(next); applied = next; LoadFields(next);
            Message = next.Enabled ? "Saved. All four global shortcuts are registered." : "Saved. Global shortcuts are disabled.";
        }
        catch (Exception ex)
        {
            Message = "Could not apply preferences: " + ex.Message;
            try { registration?.Apply(previous); }
            catch (Exception restore) { Message += " Previous shortcuts could not be restored: " + restore.Message; }
        }
    }
}
