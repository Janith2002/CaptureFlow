namespace CaptureFlow.Application.Library;
public enum PlaybackState { Empty, Loading, Playing, Paused, Stopped, Ended, Error }
public sealed class PlaybackSession
{
    public PlaybackState State { get; private set; }
    public TimeSpan Duration { get; private set; }
    public TimeSpan Position { get; private set; }
    public string? Error { get; private set; }
    public double Volume { get; private set; } = .7;
    public bool Muted { get; set; }
    public void Load() { State = PlaybackState.Loading; Position = Duration = TimeSpan.Zero; Error = null; }
    public void Opened(TimeSpan duration) { if (State != PlaybackState.Loading) return; Duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero; State = PlaybackState.Stopped; }
    public void Play() { if (State is PlaybackState.Stopped or PlaybackState.Paused or PlaybackState.Ended) { if (State == PlaybackState.Ended) Position = TimeSpan.Zero; State = PlaybackState.Playing; } }
    public void Pause() { if (State == PlaybackState.Playing) State = PlaybackState.Paused; }
    public void Stop() { if (State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Ended) { State = PlaybackState.Stopped; Position = TimeSpan.Zero; } }
    public void Seek(TimeSpan position) { if (State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Stopped or PlaybackState.Ended) Position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, Duration.Ticks)); }
    public void SetVolume(double volume) { if (!double.IsFinite(volume)) throw new ArgumentException("Invalid playback volume."); Volume = Math.Clamp(volume, 0, 1); }
    public void End() { if (State != PlaybackState.Playing) return; Position = Duration; State = PlaybackState.Ended; }
    public void Fail(string error) { Error = error; State = PlaybackState.Error; }
    public void Close() { State = PlaybackState.Empty; Duration = Position = TimeSpan.Zero; Error = null; }
}
