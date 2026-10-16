using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CaptureFlow.Application.Library;

namespace CaptureFlow.Desktop.Views;

public partial class LibraryMediaPlayer : UserControl
{
    private readonly PlaybackSession session = new();
    private readonly DispatcherTimer timer;
    private bool updatingSeek, draggingSeek, isVideo;
    private Window? fullscreen;
    private long loadStarted;
    private bool playOnOpen = true;
    private TimeSpan? initialPosition;
    public PlaybackSession Session => session;
    public event EventHandler? PlaybackStateChanged;
    private PlaybackState lastNotifiedState;
    public bool IsFullscreen => fullscreen is not null;
    public double PlaybackVolume { get => Volume.Value; set => Volume.Value = Math.Clamp(value, 0, 1); }
    // Editor owns its transport bar; Library keeps the full playback controls.
    public bool MonitorOnly
    {
        get => ((StackPanel)Seek.Parent).Visibility == Visibility.Collapsed;
        set
        {
            ((StackPanel)Seek.Parent).Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            StatusText.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }
    public bool Compact
    {
        get => FileTitle.Visibility == Visibility.Collapsed;
        set
        {
            if (!value) return;
            VideoSurface.MinHeight = 90; FileTitle.Visibility = Visibility.Collapsed;
            ((DockPanel)CurrentTime.Parent).Visibility = Visibility.Collapsed;
            ((StackPanel)Seek.Parent).Margin = new Thickness(0, 4, 0, 0); Seek.Margin = new Thickness(0);
            var buttons = (WrapPanel)PlayButton.Parent; buttons.Margin = new Thickness(0, 4, 0, 0);
            foreach (var button in buttons.Children.OfType<Button>()) { button.MinHeight = 26; button.Padding = new Thickness(8, 4, 8, 4); button.Margin = new Thickness(0, 0, 6, 2); }
            Volume.Margin = new Thickness(0); Volume.Width = 60; StatusText.Margin = new Thickness(0); StatusText.FontSize = 11;
        }
    }
    public LibraryMediaPlayer()
    {
        InitializeComponent();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            if (session.State == PlaybackState.Loading && Stopwatch.GetElapsedTime(loadStarted) > TimeSpan.FromSeconds(15)) { Fail("Media could not be opened within 15 seconds. Check permissions, format or file corruption."); return; }
            if (!draggingSeek && session.State is PlaybackState.Playing or PlaybackState.Paused) session.Seek(Media.Position);
            UpdateControls();
        };
        Unloaded += (_, _) => CloseMedia();
        UpdateControls();
    }
    public void Open(string path) => Open(path, true);
    public void Open(string path, bool autoPlay, TimeSpan? position = null)
    {
        CloseMedia();
        playOnOpen = autoPlay;
        initialPosition = position;
        if (!File.Exists(path)) { Fail("Recording is missing, moved or inaccessible. Refresh the library."); return; }
        isVideo = Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        session.Load(); loadStarted = Stopwatch.GetTimestamp(); FileTitle.Text = Path.GetFileName(path);
        PlaceholderText.Text = isVideo ? "Opening video…" : "Audio recording";
        Placeholder.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
        try { Media.Source = new Uri(Path.GetFullPath(path)); Media.Volume = session.Muted || !autoPlay ? 0 : session.Volume; Media.Play(); timer.Start(); UpdateControls(); }
        catch (Exception ex) { Fail("Unable to open this recording: " + ex.Message); }
    }
    public void CloseMedia()
    {
        fullscreen?.Close(); timer?.Stop(); Media.Stop(); Media.Close(); Media.Source = null;
        session.Close(); if (StatusText is not null) { Placeholder.Visibility = Visibility.Visible; PlaceholderText.Text = "Select a recording and choose Open"; UpdateControls(); }
    }
    public void Play() { session.Play(); if (session.State == PlaybackState.Playing) { Media.Position = session.Position; Media.Play(); timer.Start(); } UpdateControls(); }
    public void Pause() { Media.Pause(); session.Seek(Media.Position); session.Pause(); UpdateControls(); }
    public void Stop() { session.Stop(); Media.Stop(); timer.Stop(); UpdateControls(); }
    public void SeekTo(TimeSpan position) { session.Seek(position); Media.Position = session.Position; UpdateControls(); }
    private void MediaOpened(object sender, RoutedEventArgs e) { if (session.State != PlaybackState.Loading) return; session.Opened(Media.NaturalDuration.HasTimeSpan ? Media.NaturalDuration.TimeSpan : TimeSpan.Zero); if (initialPosition is { } position) SeekTo(position); if (playOnOpen) Play(); else { Media.Pause(); Media.Volume = session.Muted ? 0 : session.Volume; UpdateControls(); } }
    private void MediaFailed(object sender, ExceptionRoutedEventArgs e) { if (session.State is not (PlaybackState.Empty or PlaybackState.Error)) Fail("Unsupported or damaged media, or access denied. " + e.ErrorException.Message); }
    private void MediaEnded(object sender, RoutedEventArgs e) { session.End(); timer.Stop(); UpdateControls(); }
    private void Fail(string message) { Media.Stop(); Media.Close(); timer?.Stop(); session.Fail(message); UpdateControls(); }
    private void PlayClicked(object sender, RoutedEventArgs e) => Play();
    private void PauseClicked(object sender, RoutedEventArgs e) => Pause();
    private void StopClicked(object sender, RoutedEventArgs e) => Stop();
    private void SeekBegin(object sender, MouseButtonEventArgs e) => draggingSeek = true;
    private void SeekEnd(object sender, MouseButtonEventArgs e) { draggingSeek = false; SeekTo(TimeSpan.FromSeconds(Seek.Value)); }
    private void SeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!updatingSeek && !draggingSeek && Media is not null && session.Duration > TimeSpan.Zero) SeekTo(TimeSpan.FromSeconds(e.NewValue)); }
    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { session.SetVolume(e.NewValue); if (Media is not null) Media.Volume = session.Muted ? 0 : session.Volume; }
    private void MuteClicked(object sender, RoutedEventArgs e) => ToggleMute();
    public void ToggleMute() { session.Muted = !session.Muted; Media.Volume = session.Muted ? 0 : session.Volume; MuteButton.Content = session.Muted ? "Unmute" : "Mute"; }
    private void UpdateControls()
    {
        if (StatusText is null) return;
        StatusText.Text = session.Error ?? session.State.ToString();
        CurrentTime.Text = Display(session.Position); TotalTime.Text = Display(session.Duration);
        updatingSeek = true; Seek.Maximum = Math.Max(1, session.Duration.TotalSeconds); if (!draggingSeek) Seek.Value = session.Position.TotalSeconds; updatingSeek = false;
        PlayButton.IsEnabled = session.State is PlaybackState.Stopped or PlaybackState.Paused or PlaybackState.Ended;
        PauseButton.IsEnabled = session.State == PlaybackState.Playing;
        StopButton.IsEnabled = session.State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Ended;
        Seek.IsEnabled = session.Duration > TimeSpan.Zero && session.State is not (PlaybackState.Error or PlaybackState.Loading);
        FullscreenButton.IsEnabled = isVideo && session.State is not (PlaybackState.Empty or PlaybackState.Error or PlaybackState.Loading);
        if (lastNotifiedState != session.State) { lastNotifiedState = session.State; PlaybackStateChanged?.Invoke(this, EventArgs.Empty); }
    }
    private static string Display(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    private void FullscreenClicked(object sender, RoutedEventArgs e)
        => ToggleFullscreen();
    public void ToggleFullscreen()
    {
        if (fullscreen is not null) { fullscreen.Close(); return; }
        if (!isVideo) return;
        Content = null;
        fullscreen = new Window { Title = "CaptureFlow playback · Escape to exit", Content = PlayerRoot, WindowStyle = WindowStyle.None, WindowState = WindowState.Maximized, Background = System.Windows.Media.Brushes.Black, Owner = Window.GetWindow(this) };
        fullscreen.KeyDown += (_, args) => { if (args.Key == Key.Escape) fullscreen?.Close(); };
        fullscreen.Closed += (_, _) => { var old = fullscreen; fullscreen = null; if (old is not null) old.Content = null; Content = PlayerRoot; FullscreenButton.Content = "Fullscreen"; };
        FullscreenButton.Content = "Exit fullscreen";
        fullscreen.Show();
    }
}
