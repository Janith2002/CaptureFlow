using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Qa;
internal static class EditorPointerQa
{
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    public static async Task RunAsync(MainWindow window, EditorTimeline timeline, EditorViewModel vm)
    {
        window.Activate(); await Task.Delay(100);
        if (GetForegroundWindow() != new WindowInteropHelper(window).Handle || !GetCursorPos(out var old))
        { Console.WriteLine("BLOCKED real pointer gesture check: owned QA window lacks foreground input access."); return; }
        var originalZoom = vm.Zoom; vm.Zoom = 200; window.UpdateLayout();
        var clip = vm.Project.Clips.OrderBy(c => c.Start).First(); var original = clip;
        var oldPlayhead = vm.Playhead; vm.Playhead = clip.Start + 2;
        try
        {
            await DragAsync(timeline, new Point((clip.Start + clip.Duration / 2) * vm.Zoom, 45), new Point((clip.Start + clip.Duration / 2 + 2.03) * vm.Zoom, 45));
            Check(Math.Abs(vm.Project.Clips.Single(c => c.Id == clip.Id).Start - (clip.Start + 2)) < .01, "Real pointer clip move did not commit.");
            vm.UndoCommand.Execute(null); Check(vm.Project.Clips.Single(c => c.Id == clip.Id) == original, "Pointer move was not one undoable action.");
            vm.RedoCommand.Execute(null); Check(Math.Abs(vm.Project.Clips.Single(c => c.Id == clip.Id).Start - (clip.Start + 2)) < .01, "Pointer move redo failed."); vm.UndoCommand.Execute(null); window.UpdateLayout();
            await DragAsync(timeline, new Point(clip.End * vm.Zoom - 2, 45), new Point(clip.End * vm.Zoom - 22, 45));
            Check(vm.Project.Clips.Single(c => c.Id == clip.Id).Out < clip.Out, "Real right-edge trim did not shorten the source range.");
            vm.UndoCommand.Execute(null); Check(vm.Project.Clips.Single(c => c.Id == clip.Id) == original, "Pointer edge trim was not one undoable action.");
            Console.WriteLine("PASS actual Windows pointer body drag/playhead snapping/right-edge trim and one-action undo/redo, using generated clips in the owned editor only.");
        }
        finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); SetCursorPos(old.X, old.Y); vm.Zoom = originalZoom; vm.Playhead = oldPlayhead; }
    }
    private static async Task DragAsync(EditorTimeline timeline, Point from, Point to)
    {
        var start = timeline.PointToScreen(from); var end = timeline.PointToScreen(to);
        if (!SetCursorPos((int)Math.Round(start.X), (int)Math.Round(start.Y))) throw new InvalidOperationException("Pointer positioning is unavailable.");
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(60);
        for (var step = 1; step <= 5; step++) { if (!SetCursorPos((int)Math.Round(start.X + (end.X - start.X) * step / 5), (int)Math.Round(start.Y + (end.Y - start.Y) * step / 5))) throw new InvalidOperationException("Pointer movement is unavailable."); await Task.Delay(30); }
        mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
