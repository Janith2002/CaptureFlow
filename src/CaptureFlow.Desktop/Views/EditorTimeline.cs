using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CaptureFlow.Application.Editing;
using CaptureFlow.Core.Editing;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Desktop.Views;

/// <summary>A bounded drawing surface: three tracks, at most 64 clips and a single draggable playhead.</summary>
public sealed class EditorTimeline : FrameworkElement
{
    public static readonly DependencyProperty TrackHeightProperty = DependencyProperty.Register(nameof(TrackHeight), typeof(double), typeof(EditorTimeline),
        new FrameworkPropertyMetadata(56d, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((EditorTimeline)d).Height = 25 + 3 * (double)e.NewValue),
        value => value is double height && double.IsFinite(height) && height >= 56 && height <= 200);
    public double TrackHeight { get => (double)GetValue(TrackHeightProperty); set => SetValue(TrackHeightProperty, value); }
    private EditorViewModel? vm; private Point anchor; private EditorClip? dragged; private bool seeking; private EditorClip? proposed; private TimelineGesture gesture; private bool validGesture; private int dropTrack = -1;
    private bool? fadeEdge; private double fadeDuration;
    public event Action<double>? SeekRequested;
    public EditorTimeline()
    {
        Height = 193; Focusable = true; AllowDrop = true; System.Windows.Automation.AutomationProperties.SetName(this, "Interactive editing timeline");
        DataContextChanged += (_, _) => { if (vm is not null) vm.Changed -= Refresh; vm = DataContext as EditorViewModel; if (vm is not null) vm.Changed += Refresh; Refresh(); };
        Loaded += (_, _) => { if (vm is not null) { vm.Changed -= Refresh; vm.Changed += Refresh; } Refresh(); };
        Unloaded += (_, _) => { if (vm is not null) vm.Changed -= Refresh; };
    }
    private void Refresh() { Width = vm is null ? 1000 : Math.Max(400, vm.Project.Duration * vm.Zoom + 100); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (vm is null) return;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(21, 26, 38)), null, new(0, 0, ActualWidth, Height));
        var step = new[] { 1d / 30, .1, .5, 1, 5, 10, 30, 60, 300, 900, 3600, 10800, 21600, 86400 }.First(s => s * vm.Zoom >= 80);
        // Draw no more than 400 ruler marks, even for a long project at high zoom.
        var effective = Math.Max(step, Math.Ceiling((vm.Project.Duration + 5) / 400 / step) * step);
        for (var mark = 0; mark <= 400; mark++)
        { var t = Math.Round(mark * effective, 9); if (t > vm.Project.Duration + 5) break; Label(dc, EditorSession.Timecode(t), t * vm.Zoom + 4, 4, 10, Brushes.LightSlateGray); }
        foreach (var track in vm.Project.Tracks)
        {
            var y = 25 + track.Id * TrackHeight;
            if (vm.SelectedTrack == track.Id) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 40, 58)), null, new Rect(0, y, ActualWidth, TrackHeight));
            dc.DrawLine(new Pen(Brushes.SlateGray, .5), new(0, y), new(ActualWidth, y));
            foreach (var clip in vm.Project.Clips.Where(c => c.TrackId == track.Id))
            {
                var display = dragged?.Id == clip.Id && proposed is not null ? proposed : clip; var start = display.Start; var x = start * vm.Zoom; var w = Math.Max(2, display.Duration * vm.Zoom); var clipY = 25 + display.TrackId * TrackHeight;
                var rect = new Rect(x, clipY + 2, w, TrackHeight - 4); var selected = vm.SelectedClip?.Id == clip.Id;
                dc.DrawRoundedRectangle(new SolidColorBrush(track.Video ? Color.FromRgb(65, 42, 103) : track.Id == 2 ? Color.FromRgb(24, 75, 61) : Color.FromRgb(19, 68, 81)), new Pen(dragged?.Id == clip.Id && !validGesture ? Brushes.OrangeRed : selected ? Brushes.Cyan : Brushes.SlateGray, selected ? 2 : 1), rect, 5, 5);
                dc.PushClip(new RectangleGeometry(rect));
                var item = vm.Media.FirstOrDefault(m => m.Media.Id == clip.MediaId);
                if (track.Video && item?.Thumbnail is { } thumbnail)
                {
                    var count = Math.Min(32, (int)Math.Ceiling(w / 90)); var tileWidth = w / count;
                    for (var i = 0; i < count; i++)
                    {
                        var sample = display.In + (display.Out - display.In) * i / count;
                        var image = item.Filmstrip.Length == 0 ? thumbnail : item.Filmstrip[Math.Clamp((int)(sample / item.Media.Duration * item.Filmstrip.Length), 0, item.Filmstrip.Length - 1)];
                        dc.DrawImage(image, new(x + i * tileWidth, clipY + 3, tileWidth, 17));
                    }
                }
                else if (item?.Waveform is { Length: > 0 } waveform)
                {
                    var media = item.Media;
                    for (var i = 0; i < 256; i++)
                    {
                        var source = display.In + (display.Out - display.In) * i / 255; var bucket = Math.Clamp((int)(source / media.Duration * waveform.Length), 0, waveform.Length - 1);
                        var amplitude = waveform[bucket] * 10; var px = x + w * i / 256;
                        dc.DrawLine(new Pen(Brushes.Turquoise, Math.Max(1, w / 256 * .6)), new(px, clipY + 13 - amplitude), new(px, clipY + 13 + amplitude));
                    }
                }
                Label(dc, item is null ? "Missing media" : System.IO.Path.GetFileName(item.Media.Path), x + 6, clipY + 20, 11, Brushes.White);
                foreach (var transition in display.Transitions ?? [])
                {
                    var tx = transition.Placement == TransitionPlacement.Start ? x : x + w - transition.Duration * vm.Zoom;
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 189, 164, 255)), selected && vm.TransitionPlacement == transition.Placement ? new Pen(Brushes.Yellow, 1) : null, new(tx, clipY + 31, transition.Type == TransitionType.CrossDissolve ? Math.Min(w, 75) : transition.Duration * vm.Zoom, 9));
                    Label(dc, transition.Type == TransitionType.CrossDissolve ? "Dissolve ↤" : transition.Type.ToString(), tx + 2, clipY + 30, 8, Brushes.White);
                }
                foreach (var key in display.Keyframes ?? [])
                {
                    var kx = x + key.Time * vm.Zoom; var brush = vm.SelectedKeyframe?.Id == key.Id ? Brushes.Yellow : Brushes.Cyan;
                    dc.DrawRectangle(brush, null, new(kx - 3, clipY + 44, 6, 6));
                }
                if (item?.Media.HasAudio == true)
                {
                    var fi = fadeEdge == true && dragged?.Id == clip.Id ? fadeDuration : display.FadeIn;
                    var fo = fadeEdge == false && dragged?.Id == clip.Id ? fadeDuration : display.FadeOut;
                    dc.DrawLine(new Pen(Brushes.Gold, 1), new(x, clipY + 16), new(x + fi * vm.Zoom, clipY + 6));
                    dc.DrawLine(new Pen(Brushes.Gold, 1), new(x + w - fo * vm.Zoom, clipY + 6), new(x + w, clipY + 16));
                    dc.DrawRectangle(Brushes.Gold, null, new(x + fi * vm.Zoom - 3, clipY + 3, 6, 6));
                    dc.DrawRectangle(Brushes.Gold, null, new(x + w - fo * vm.Zoom - 3, clipY + 3, 6, 6));
                }
                if (selected) { dc.DrawRectangle(Brushes.Cyan, null, new Rect(x, clipY + 4, 4, 46)); dc.DrawRectangle(Brushes.Cyan, null, new Rect(x + Math.Max(0, w - 4), clipY + 4, 4, 46)); } dc.Pop();
                foreach (var transition in (display.Transitions ?? []).Where(t => t.Type == TransitionType.CrossDissolve))
                    dc.DrawRectangle(null, new Pen(selected && vm.TransitionPlacement == TransitionPlacement.Start ? Brushes.Yellow : Brushes.MediumPurple, 2), new(Math.Max(0, x - transition.Duration * vm.Zoom), clipY + 31, transition.Duration * vm.Zoom, 9));
            }
        }
        if (dropTrack >= 0) dc.DrawRectangle(null, new Pen(Brushes.Cyan, 2), new Rect(0, 25 + dropTrack * TrackHeight, ActualWidth, TrackHeight));
        var position = vm.Playhead * vm.Zoom; dc.DrawLine(new Pen(Brushes.Cyan, 1.5), new(position, 0), new(position, Height));
    }
    private void Label(DrawingContext dc, string text, double x, double y, double size, Brush brush) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); if (vm is null || !vm.CanEdit) return;
        Focus(); if (BeginInteraction(e.GetPosition(this))) CaptureMouse(); e.Handled = true;
    }
    // Logical-coordinate routing is shared by native input and deterministic STA/WPF interaction checks.
    internal bool BeginInteraction(Point point)
    {
        if (vm is null || !vm.CanEdit) return false;
        anchor = point; var track = TrackAt(anchor.Y); if (track >= 0) vm.SelectedTrack = track;
        if (track == 0 && anchor.Y - 25 is >= 30 and <= 41)
        {
            var incoming = vm.Project.Clips.FirstOrDefault(c => c.TrackId == 0 && (c.Transitions ?? []).Any(t => t.Type == TransitionType.CrossDissolve && anchor.X / vm.Zoom >= c.Start - t.Duration && anchor.X / vm.Zoom <= c.Start));
            if (incoming is not null) { vm.SelectedClip = incoming; vm.SelectTransition(TransitionPlacement.Start); return false; }
        }
        dragged = track >= 0 ? vm.Project.Clips.LastOrDefault(c => c.TrackId == track && anchor.X / vm.Zoom >= c.Start && anchor.X / vm.Zoom <= c.End) : null;
        seeking = dragged is null;
        if (dragged is not null)
        {
            vm.SelectedClip = dragged; proposed = dragged; validGesture = !vm.Project.Tracks.Single(t => t.Id == track).Locked;
            var row = anchor.Y - (25 + track * TrackHeight);
            if (row >= 42)
            {
                var key = (dragged.Keyframes ?? []).OrderBy(k => Math.Abs((dragged.Start + k.Time) * vm.Zoom - anchor.X)).FirstOrDefault();
                if (key is not null && Math.Abs((dragged.Start + key.Time) * vm.Zoom - anchor.X) <= 7)
                { vm.SelectedKeyframe = key; dragged = proposed = null; return false; }
            }
            if (row is >= 30 and <= 41)
            {
                var transition = (dragged.Transitions ?? []).FirstOrDefault(t => t.Placement == TransitionPlacement.Start ? anchor.X <= (dragged.Start + t.Duration) * vm.Zoom : anchor.X >= (dragged.End - t.Duration) * vm.Zoom);
                if (transition is not null) { vm.SelectTransition(transition.Placement); dragged = proposed = null; return false; }
            }
            if (row is >= 2 and <= 11 && validGesture && vm.Project.Media.Single(m => m.Id == dragged.MediaId).HasAudio)
            {
                if (Math.Abs(anchor.X - (dragged.Start + dragged.FadeIn) * vm.Zoom) <= 7) fadeEdge = true;
                else if (Math.Abs(anchor.X - (dragged.End - dragged.FadeOut) * vm.Zoom) <= 7) fadeEdge = false;
                if (fadeEdge is not null) { fadeDuration = fadeEdge.Value ? dragged.FadeIn : dragged.FadeOut; return true; }
            }
            var left = Math.Abs(anchor.X - dragged.Start * vm.Zoom); var right = Math.Abs(anchor.X - dragged.End * vm.Zoom);
            gesture = left <= 7 || vm.ActiveTimelineTool == "Trim" && left < right ? TimelineGesture.TrimLeft : right <= 7 || vm.ActiveTimelineTool == "Trim" ? TimelineGesture.TrimRight : TimelineGesture.Move;
        }
        else SetPlayhead(anchor.X);
        return true;
    }
    private int TrackAt(double y) => y >= 25 && y < Height ? (int)((y - 25) / TrackHeight) : -1;
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (vm is null) return;
        ToolTip = "Gold top handles: clip fades (audio and video, unless a video transition owns that edge). Cyan bottom markers: select keyframes. Purple bands: select transitions. Body: move; edges: trim.";
        if (!IsMouseCaptured) return;
        UpdateInteraction(e.GetPosition(this));
    }
    internal void UpdateInteraction(Point point)
    {
        if (vm is null || !vm.CanEdit) return;
        if (fadeEdge is not null && dragged is not null)
        {
            fadeDuration = Math.Clamp(fadeEdge.Value ? point.X / vm.Zoom - dragged.Start : dragged.End - point.X / vm.Zoom, 0, dragged.Duration - (fadeEdge.Value ? dragged.FadeOut : dragged.FadeIn));
            InvalidateVisual(); return;
        }
        if (seeking) SetPlayhead(point.X);
        else if (dragged is not null)
        {
            try { proposed = vm.ProposeGesture(dragged, gesture, (point.X - anchor.X) / vm.Zoom, gesture == TimelineGesture.Move ? TrackAt(point.Y) : dragged.TrackId); validGesture = true; Cursor = Cursors.SizeWE; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { validGesture = false; vm.Message = ex.Message; Cursor = Cursors.No; }
            InvalidateVisual();
        }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e); if (!IsMouseCaptured || vm is null) return;
        CompleteInteraction(); e.Handled = true;
    }
    internal void CompleteInteraction()
    {
        if (vm is null) return;
        if (dragged is not null) vm.SelectedClip = vm.Project.Clips.FirstOrDefault(c => c.Id == dragged.Id);
        if (fadeEdge is not null && dragged is not null) vm.SetAudioFade(fadeEdge.Value, fadeDuration);
        else if (dragged is not null && proposed is not null && proposed != dragged && validGesture) vm.CompleteGesture(proposed);
        CancelInteraction();
    }
    internal void CancelInteraction() { dragged = proposed = null; fadeEdge = null; seeking = false; if (IsMouseCaptured) ReleaseMouseCapture(); Cursor = Cursors.Arrow; InvalidateVisual(); }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); dragged = proposed = null; fadeEdge = null; seeking = false; Cursor = Cursors.Arrow; InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CancelInteraction(); e.Handled = true; }
        else if (vm is not null && Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Left or Key.Right) { vm.SelectAdjacentClip(e.Key == Key.Left ? -1 : 1); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (IsMouseCaptured) { e.Handled = true; return; }
        DependencyObject? parent = VisualTreeHelper.GetParent(this); while (parent is not null && parent is not System.Windows.Controls.ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
        var scroll = parent as System.Windows.Controls.ScrollViewer;
        if (vm is not null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var x = e.GetPosition(this).X; var time = x / vm.Zoom; var viewportX = x - (scroll?.HorizontalOffset ?? 0);
            if (e.Delta > 0) vm.ZoomInCommand.Execute(null); else vm.ZoomOutCommand.Execute(null);
            UpdateLayout(); scroll?.ScrollToHorizontalOffset(Math.Max(0, time * vm.Zoom - viewportX)); e.Handled = true;
        }
        else if (scroll is not null && scroll.ScrollableWidth > 0) { scroll.ScrollToHorizontalOffset(Math.Max(0, scroll.HorizontalOffset - e.Delta)); e.Handled = true; }
        base.OnMouseWheel(e);
    }
    public const string MediaDragFormat = "CaptureFlow.EditorMedia";
    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e); dropTrack = -1; e.Effects = DragDropEffects.None;
        if (vm?.CanEdit == true && e.Data.GetData(MediaDragFormat) is Guid id)
        {
            var point = e.GetPosition(this); var track = TrackAt(point.Y); var media = vm.Project.Media.FirstOrDefault(m => m.Id == id);
            if (track >= 0 && media is not null && !vm.Project.Tracks.Single(t => t.Id == track).Locked)
            {
                try { vm.ProposeDrop(id, track, point.X / vm.Zoom); dropTrack = track; e.Effects = DragDropEffects.Copy; } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
            }
        }
        e.Handled = true; InvalidateVisual();
    }
    protected override void OnDragLeave(DragEventArgs e) { dropTrack = -1; InvalidateVisual(); base.OnDragLeave(e); }
    protected override void OnDrop(DragEventArgs e)
    {
        OnDragOver(e);
        if (vm is not null && dropTrack >= 0 && e.Data.GetData(MediaDragFormat) is Guid id) vm.DropMedia(id, dropTrack, e.GetPosition(this).X / vm.Zoom);
        dropTrack = -1; e.Handled = true; InvalidateVisual();
    }
    private void SetPlayhead(double x) { if (vm is null) return; vm.Playhead = Math.Clamp(TimelineGeometry.Quantize(x / vm.Zoom), 0, vm.Project.Duration); SeekRequested?.Invoke(vm.Playhead); }
}
