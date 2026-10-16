using CaptureFlow.Application.Library;
using CommunityToolkit.Mvvm.Input;
namespace CaptureFlow.Desktop.ViewModels;

public partial class EditorViewModel
{
    public PlaybackState PreviewPlaybackState { get; private set; } = PlaybackState.Empty;
    private string? playbackErrorMessage;
    public event Action? PlayPreviewRequested;
    public event Action? PausePreviewRequested;
    public event Action? StopPreviewRequested;
    public bool CanPlayPreview => CanEdit && Project.Clips.Length > 0 && PreviewPlaybackState is not (PlaybackState.Loading or PlaybackState.Playing);
    public bool CanPausePreview => !PreviewStale && PreviewPlaybackState == PlaybackState.Playing;
    public bool CanStopPreview => !PreviewStale && PreviewPlaybackState is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Ended;
    public string PreviewPlaybackHint => Project.Clips.Length == 0 ? "Add a clip to the timeline" : IsBusy ? "Processing preview or project…" :
        PreviewPlaybackState == PlaybackState.Loading ? "Loading preview…" : PreviewStale ? "Play builds the edited preview" :
        PreviewPlaybackState == PlaybackState.Error ? "Preview failed; Play to retry" : "Preview " + PreviewPlaybackState.ToString().ToLowerInvariant();

    public void UpdatePreviewPlaybackState(PlaybackState state, string? error = null)
    {
        if (PreviewPlaybackState == state) return;
        PreviewPlaybackState = state;
        OnPropertyChanged(nameof(PreviewPlaybackState)); NotifyPlayback();
        if (state == PlaybackState.Error) Message = playbackErrorMessage = "Preview playback failed: " + error + " Press Play to retry, or update the preview.";
        else if (state is PlaybackState.Playing or PlaybackState.Stopped && playbackErrorMessage is not null)
        {
            if (Message == playbackErrorMessage) Message = "Edited preview ready.";
            playbackErrorMessage = null;
        }
    }
    private void NotifyPlayback()
    {
        OnPropertyChanged(nameof(CanPlayPreview)); OnPropertyChanged(nameof(CanPausePreview)); OnPropertyChanged(nameof(CanStopPreview));
        OnPropertyChanged(nameof(PreviewPlaybackHint));
        PlayPreviewCommand.NotifyCanExecuteChanged(); PausePreviewCommand.NotifyCanExecuteChanged(); StopPreviewCommand.NotifyCanExecuteChanged();
    }
    partial void OnPreviewStaleChanged(bool value) => NotifyPlayback();

    private void ReleasePlayback()
    {
        PreviewReleaseRequested?.Invoke();
        UpdatePreviewPlaybackState(PlaybackState.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanPlayPreview))]
    private async Task PlayPreviewAsync()
    {
        if (!CanPlayPreview) return;
        if (PreviewStale || PreviewPath is null || !System.IO.File.Exists(PreviewPath))
            await PreviewCommand.ExecuteAsync(null); // Same production renderer; rendering and native load keep Play disabled.
        else if (PreviewPlaybackState is PlaybackState.Empty or PlaybackState.Error)
            PreviewRequested?.Invoke(PreviewPath); // Retry/open the cached edited preview through the real view owner.
        else PlayPreviewRequested?.Invoke();
    }
    [RelayCommand(CanExecute = nameof(CanPausePreview))]
    private void PausePreview() { if (CanPausePreview) PausePreviewRequested?.Invoke(); }
    [RelayCommand(CanExecute = nameof(CanStopPreview))]
    private void StopPreview() { if (CanStopPreview) { StopPreviewRequested?.Invoke(); Playhead = 0; } }
}
