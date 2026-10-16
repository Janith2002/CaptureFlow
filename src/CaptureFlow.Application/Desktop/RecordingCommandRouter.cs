namespace CaptureFlow.Application.Desktop;

public enum RecordingAction { StartStop, Start, Stop, PauseResume, ToggleMicrophone }
public interface IRecordingCommandTarget
{
    bool Active { get; }
    bool Busy { get; }
    Task ExecuteAsync(RecordingAction action);
    Task FinalizeAsync();
}
/// <summary>Routes to existing UI commands; owns no capture or encoding state.</summary>
public sealed class RecordingCommandRouter(IRecordingCommandTarget video, IRecordingCommandTarget audio)
{
    private readonly SemaphoreSlim gate = new(1);
    private bool exiting;
    public bool PreferAudio { get; set; }
    public bool Active => video.Active || audio.Active;
    public bool Busy => video.Busy || audio.Busy;
    public bool AudioTarget => audio.Active != video.Active ? audio.Active : PreferAudio;
    private IRecordingCommandTarget Target => AudioTarget ? audio : video;
    public async Task<bool> RouteAsync(RecordingAction action)
    {
        if (exiting || !await gate.WaitAsync(0)) return false;
        try
        {
            if (exiting || Busy) return false;
            await Target.ExecuteAsync(action); return true;
        }
        finally { gate.Release(); }
    }
    public async Task FinalizeAsync()
    {
        exiting = true;
        await gate.WaitAsync();
        try
        {
            var errors = new List<Exception>();
            foreach (var target in new[] { video, audio })
                try { await target.FinalizeAsync(); } catch (Exception ex) { errors.Add(ex); }
            if (errors.Count > 0) throw new AggregateException("Recording finalization failed.", errors);
        }
        finally { gate.Release(); }
    }
}
public static class WindowClosePolicy
{
    public static bool HideInsteadOfClose(bool minimizeToTray, bool explicitExit, bool trayAvailable) => minimizeToTray && !explicitExit && trayAvailable;
    public static bool NeedsConfirmation(bool recordingActive, bool pendingOperation) => recordingActive || pendingOperation;
}
