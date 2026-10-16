using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CaptureFlow.Application.Desktop;
using CaptureFlow.Core.Desktop;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Desktop.Services;

public sealed class WindowDesktopIntegration : IDisposable
{
    private readonly MainWindow window;
    private readonly DashboardViewModel dashboard;
    private readonly RecordingCommandRouter router;
    private readonly WindowsGlobalShortcuts hotkeys;
    private readonly ShortcutRegistration registration;
    private readonly WindowsTrayIcon? tray;
    private readonly IRecordingCountdown countdown;
    private readonly FloatingToolbarViewModel toolbarModel;
    private readonly FloatingToolbarController toolbar;
    private bool shuttingDown;
    private readonly MenuItem start, pause, stop;
    private bool disposed;
    private string previousStatus = "";
    private string? trayNotice;
    public bool TrayAvailable => tray?.Available == true;
    public bool Active => router.Active;
    public bool Busy => dashboard.Audio.StartCommand.IsRunning || dashboard.Audio.StopCommand.IsRunning ||
        dashboard.Screen.StartCommand.IsRunning || dashboard.Screen.StopCommand.IsRunning || router.Active && router.Busy;
    public WindowDesktopIntegration(MainWindow window, DashboardViewModel dashboard, HwndSource source, IRecordingCountdown countdown)
    {
        this.window = window; this.dashboard = dashboard;
        this.countdown = countdown;
        router = new(new ViewModelRecordingTarget(dashboard.Screen), new ViewModelRecordingTarget(dashboard.Audio));
        toolbarModel = new(RouteAsync, countdown.Cancel, Open);
        toolbar = new(dashboard.Experience, toolbarModel); countdown.Changed += CountdownChanged; dashboard.Experience.PropertyChanged += Changed;
        hotkeys = new(source); registration = new(hotkeys); hotkeys.Invoked += OnHotkey;
        var menu = new ContextMenu();
        menu.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenu");
        menu.Items.Add(Item("Open CaptureFlow", Open));
        start = Item("Start Recording", () => Route(RecordingAction.Start)); menu.Items.Add(start);
        pause = Item("Pause/Resume Recording", () => Route(RecordingAction.PauseResume)); menu.Items.Add(pause);
        stop = Item("Stop Recording", () => Route(RecordingAction.Stop)); menu.Items.Add(stop);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open Recordings", () => { dashboard.NavigateCommand.Execute("Recordings"); Open(); }));
        menu.Items.Add(Item("Exit", window.RequestExit));
        menu.Opened += (_, _) => Update();
        try { tray = new(source, menu, Open); if (!tray.Available) throw new InvalidOperationException("Windows notification area is unavailable."); }
        catch (Exception ex) { trayNotice = "Tray unavailable: " + ex.Message; dashboard.Shortcuts.Message = trayNotice; StartupDiagnostics.Record("tray-unavailable", exception: ex); }
        dashboard.PropertyChanged += Changed; dashboard.Audio.PropertyChanged += Changed; dashboard.Screen.PropertyChanged += Changed;
        window.StateChanged += WindowStateChanged;
        Update();
    }
    public async Task InitializeAsync()
    {
        await dashboard.Shortcuts.InitializeAsync(registration);
        if (dashboard.Shortcuts.Message.StartsWith("Shortcuts unavailable:"))
            StartupDiagnostics.Record("shortcuts-unavailable", new { message = dashboard.Shortcuts.Message });
        if (trayNotice is not null) dashboard.Shortcuts.Message += " " + trayNotice;
    }
    private static MenuItem Item(string text, Action action) { var item = new MenuItem { Header = text }; item.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuItem"); item.Click += (_, _) => action(); return item; }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed) return;
        if (sender == dashboard && e.PropertyName == nameof(dashboard.SelectedPage))
        {
            if (dashboard.IsAudioRecorder) router.PreferAudio = true;
            else if (dashboard.IsScreenRecorder) router.PreferAudio = false;
        }
        Update();
    }
    private void WindowStateChanged(object? sender, EventArgs e)
    { if (!disposed && window.WindowState == WindowState.Minimized && dashboard.Shortcuts.AppliedMinimizeToTray && TrayAvailable) window.Hide(); }
    public void Open() { if (disposed) return; window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); }
    private void OnHotkey(ShortcutAction action)
    {
        if (disposed) return;
        if (action == ShortcutAction.ToggleWindow) { if (window.IsVisible && window.WindowState != WindowState.Minimized) window.Hide(); else Open(); }
        else Route(action switch { ShortcutAction.StartStop => RecordingAction.StartStop, ShortcutAction.PauseResume => RecordingAction.PauseResume, _ => RecordingAction.ToggleMicrophone });
    }
    private void CountdownChanged(object? sender, EventArgs e) => Update();
    private async void Route(RecordingAction action)
    {
        await RouteAsync(action);
    }
    private async Task RouteAsync(RecordingAction action)
    {
        if (shuttingDown) return;
        if (countdown.IsRunning && action is RecordingAction.StartStop or RecordingAction.Stop) { countdown.Cancel(); return; }
        try { await router.RouteAsync(action); }
        catch (Exception ex) { Open(); MessageBox.Show(window, ex.Message, "Recording action", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { Update(); }
    }
    private void Update()
    {
        if (disposed) return;
        var audio = dashboard.Audio; var video = dashboard.Screen;
        var useAudio = audio.IsCountingDown || audio.StartCommand.IsRunning ? true : video.IsCountingDown || video.StartCommand.IsRunning ? false : router.AudioTarget;
        var state = countdown.IsRunning && countdown.Remaining > 0 ? $"Countdown · {countdown.Remaining}" : useAudio ? audio.StartCommand.IsRunning && !router.Active ? "Starting" : audio.Status.ToString() :
            video.StartCommand.IsRunning && !router.Active ? "Starting" : video.Status.ToString();
        if (useAudio ? audio.StopCommand.IsRunning : video.StopCommand.IsRunning) state = useAudio && audio.Status == Core.Audio.AudioRecordingState.Exporting ? "Exporting" : "Stopping";
        toolbarModel.Status = state; toolbarModel.Elapsed = useAudio ? audio.ElapsedText : video.ElapsedText;
        toolbarModel.IsCountdown = countdown.IsRunning && countdown.Remaining > 0;
        toolbarModel.CanStop = !shuttingDown && (toolbarModel.IsCountdown || !router.Busy && router.Active);
        toolbarModel.CanPause = !shuttingDown && !router.Busy && (useAudio ? audio.PauseCommand.CanExecute(null) || audio.ResumeCommand.CanExecute(null) : video.PauseCommand.CanExecute(null) || video.ResumeCommand.CanExecute(null));
        toolbarModel.PauseText = useAudio ? audio.Status == Core.Audio.AudioRecordingState.Paused ? "Resume" : "Pause" : video.Status == Core.Video.VideoRecordingState.Paused ? "Resume" : "Pause";
        toolbarModel.CanMute = !shuttingDown && router.Active && !router.Busy && (useAudio ? audio.MicrophoneEnabled : video.MicrophoneEnabled);
        toolbarModel.MicrophoneMuted = useAudio ? audio.MicrophoneMuted : video.MicrophoneMuted;
        toolbar.Update(countdown.IsRunning || router.Active || audio.StartCommand.IsRunning || video.StartCommand.IsRunning || audio.StopCommand.IsRunning || video.StopCommand.IsRunning ||
            audio.Status is Core.Audio.AudioRecordingState.Saving or Core.Audio.AudioRecordingState.Exporting || video.Status is Core.Video.VideoRecordingState.Starting or Core.Video.VideoRecordingState.Saving ||
            (useAudio ? audio.Status == Core.Audio.AudioRecordingState.Completed : video.Status == Core.Video.VideoRecordingState.Completed));
        var status = useAudio ? $"{audio.SelectedMode} · {state} · {audio.ElapsedText}" : $"{video.SelectedMode} · {state} · {video.ElapsedText}";
        if (status != previousStatus) { tray?.Update(status, router.Active); previousStatus = status; }
        var targetStart = useAudio ? audio.StartCommand.CanExecute(null) : video.StartCommand.CanExecute(null);
        var targetPause = useAudio ? audio.PauseCommand.CanExecute(null) || audio.ResumeCommand.CanExecute(null) : video.PauseCommand.CanExecute(null) || video.ResumeCommand.CanExecute(null);
        start.IsEnabled = !router.Busy && !router.Active && targetStart;
        pause.IsEnabled = !router.Busy && targetPause;
        stop.Header = toolbarModel.IsCountdown ? "Cancel Countdown" : "Stop Recording";
        stop.IsEnabled = toolbarModel.CanStop;
    }
    public async Task FinalizeAsync()
    {
        shuttingDown = true; countdown.Cancel(); Update();
        registration.Dispose();
        try
        {
            await countdown.CancelAndWaitAsync();
            if (dashboard.Screen.StartCommand.IsRunning && dashboard.Screen.CancelCommand.CanExecute(null))
                await dashboard.Screen.CancelCommand.ExecuteAsync(null);
            await router.FinalizeAsync();
        }
        finally { await toolbar.DisposeAsync(); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        window.StateChanged -= WindowStateChanged; dashboard.PropertyChanged -= Changed;
        dashboard.Audio.PropertyChanged -= Changed; dashboard.Screen.PropertyChanged -= Changed;
        hotkeys.Invoked -= OnHotkey; registration.Dispose(); tray?.Dispose();
        countdown.Changed -= CountdownChanged; dashboard.Experience.PropertyChanged -= Changed;
    }
}
