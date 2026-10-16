using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;

/// <summary>Transient mouse-only input for a capture source. No keyboard hooks or persisted event history.</summary>
internal sealed class WindowsMouseEffects : IAsyncDisposable
{
    private readonly MouseEffectsPreferences settings;
    private readonly MouseClickBuffer clicks = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly object sync = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Hook callback;
    private readonly nint targetWindow;
    private uint threadId;
    private bool disposed;
    private bool started;
    private readonly WindowsCursorRenderer cursorRenderer = new();
    public WindowsMouseEffects(MouseEffectsPreferences settings, nint targetWindow = 0)
    {
        settings.Validate(); this.settings = settings; this.targetWindow = targetWindow; clicks.SetActive(true); callback = OnMouse;
    }
    public async Task StartAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!settings.ClickEffects) return;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!started) { started = true; var thread = new Thread(Listen) { IsBackground = true, Name = "CaptureFlow mouse effects" }; thread.Start(); }
        }
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
    }
    private void Listen()
    {
        nint hook = 0;
        try
        {
            threadId = GetCurrentThreadId(); PeekMessage(out _, 0, 0, 0, 0); // Create the queue before publishing readiness.
            hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
            if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot listen for mouse clicks. Disable click effects or check Windows permissions.");
            ready.TrySetResult();
            int result;
            while ((result = GetMessage(out var message, 0, 0, 0)) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
            if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (Exception ex) { ready.TrySetException(ex); ended.TrySetException(ex); }
        finally
        {
            if (hook != 0 && !UnhookWindowsHookEx(hook)) ended.TrySetException(new Win32Exception(Marshal.GetLastWin32Error(), "Could not release mouse listener."));
            ended.TrySetResult();
        }
    }
    private nint OnMouse(int code, nint message, nint data)
    {
        // Tiny callback: only down position/button/timestamp. Always forward input to Windows.
        if (code >= 0 && (message == 0x201 || message == 0x204))
        {
            var point = Marshal.PtrToStructure<Point>(data);
            if (IsTarget(point)) lock (sync) { if (!disposed && clock.IsRunning) clicks.Add(new(new(point.X, point.Y), message == 0x201 ? MouseClickButton.Left : MouseClickButton.Right, clock.Elapsed)); }
        }
        return CallNextHookEx(0, code, message, data);
    }
    public void SetActive(bool value)
    {
        lock (sync) { if (disposed) return; clicks.SetActive(value); if (value) clock.Start(); else clock.Stop(); }
    }
    public void Render(Span<byte> pixels, VideoSize size, double left, double top, ScreenRegion? region)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ended.Task.IsFaulted) ended.Task.GetAwaiter().GetResult();
        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        MousePoint? point = null;
        if (GetCursorInfo(ref cursor) && (cursor.Flags & 1) != 0 && GetPhysicalCursorPos(out var physical) && IsTarget(physical)) point = MouseCoordinates.ToFrame(new(physical.X, physical.Y), left, top, region);
        TimeSpan now; MouseClick[] events;
        lock (sync) { now = clock.Elapsed; events = clicks.Snapshot(now, TimeSpan.FromMilliseconds(settings.ClickDurationMilliseconds)); }
        for (var i = 0; i < events.Length; i++) events[i] = events[i] with { Position = MouseCoordinates.ToFrame(events[i].Position, left, top, region) };
        MouseEffectsRenderer.Render(pixels, size, settings, point, events, now);
        if (settings.ShowCursor && point is { } visible) cursorRenderer.Draw(pixels, size, visible, cursor.Cursor);
    }
    private bool IsTarget(Point point) => targetWindow == 0 || GetAncestor(WindowFromPoint(point), 2) == targetWindow;
    public async ValueTask DisposeAsync()
    {
        lock (sync) { if (disposed) return; disposed = true; clicks.SetActive(false); clock.Stop(); }
        cursorRenderer.Dispose();
        if (!settings.ClickEffects || !started) return;
        // Startup cancellation may race with native hook creation. Await readiness before posting quit.
        try { await ready.Task.ConfigureAwait(false); }
        catch { await ended.Task.ConfigureAwait(false); return; }
        if (!ended.Task.IsCompleted && !PostThreadMessage(threadId, 0x12, 0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot stop mouse listener.");
        await ended.Task.ConfigureAwait(false);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size, Flags; public nint Cursor; public Point Position; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Point Position; public uint Private; }
    private delegate nint Hook(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, Hook callback, nint module, uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint flags);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostThreadMessage(uint thread, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo cursor);
    [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
