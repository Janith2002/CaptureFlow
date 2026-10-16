using System.Buffers;
using System.Runtime.InteropServices;
using CaptureFlow.Core.Video;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
namespace CaptureFlow.Infrastructure.Video;
public sealed class WindowsGraphicsCaptureSourceFactory : IMonitorCaptureBackend
{
    public Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<MonitorDescriptor>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return NativeMonitors.Enumerate().Select(m => m.Descriptor).ToArray();
    }, cancellationToken);
    public Task<IVideoFrameSource> CreateAsync(VideoSourceOptions options, CancellationToken cancellationToken) => Task.Run<IVideoFrameSource>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.Kind is not (VideoSourceKind.Monitor or VideoSourceKind.Window or VideoSourceKind.Region)) throw new NotSupportedException("Choose a screen, window or region source.");
        options.ValidateScreen();
        if (options.Kind == VideoSourceKind.Window)
        {
            WindowsApplicationWindowDiscovery.Validate(options.Window!);
            try { return new WindowsGraphicsCaptureSource(new IntPtr(options.Window!.Handle), options.CaptureCursor, window: options.Window, effects: options.MouseEffects); }
            catch (COMException ex) { throw new IOException($"Windows Graphics Capture cannot access the selected application window (0x{ex.HResult:X8}). Restore/reselect a supported window. Capture will not switch to the desktop.", ex); }
        }
        var monitor = NativeMonitors.Enumerate().FirstOrDefault(m => m.Descriptor.Id == options.Id) ?? throw new IOException("The selected monitor is disconnected. Refresh monitors.");
        if (options.Region is { } region && !region.Matches(monitor.Descriptor)) throw new IOException("The monitor resolution changed. Select the region again before recording.");
        return new WindowsGraphicsCaptureSource(monitor.Handle, options.CaptureCursor, options.Region, effects: options.MouseEffects);
    }, cancellationToken);
}
internal sealed class WindowsGraphicsCaptureSource : IVideoFrameSource
{
    private readonly object sync = new();
    private readonly GraphicsCaptureItem item;
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly IDirect3DDevice winrtDevice;
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private ID3D11Texture2D? staging;
    private Direct3D11CaptureFrame? latest;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? failure;
    private bool disposed;
    public event EventHandler<Exception>? Failed;
    public VideoSize Size { get; }
    private readonly VideoSize captureSize;
    private readonly ScreenRegion? region;
    private readonly ApplicationWindowDescriptor? window;
    private readonly nint captureHandle;
    private readonly WindowsMouseEffects? mouse;
    public void SetRecordingActivity(bool active) => mouse?.SetActive(active);
    public WindowsGraphicsCaptureSource(nint monitor, bool cursor, ScreenRegion? region = null, ApplicationWindowDescriptor? window = null, MouseEffectsPreferences? effects = null)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows Graphics Capture is not supported in this Windows session.");
        this.region = region; this.window = window; captureHandle = monitor;
        item = CaptureInterop.CreateItem(monitor, window is not null);
        captureSize = new(item.Size.Width, item.Size.Height);
        Size = region is null ? captureSize : new(region.Width, region.Height);
        if (captureSize.Width <= 0 || captureSize.Height <= 0 || (long)captureSize.Width * captureSize.Height > 33_554_432) throw new NotSupportedException("The source dimensions exceed the supported capture buffer size.");
        if (region is not null && (region.MonitorWidth != captureSize.Width || region.MonitorHeight != captureSize.Height)) throw new IOException("Monitor geometry changed. Select the region again.");
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out device!, out context!).CheckError();
        try
        {
            using var dxgi = device.QueryInterface<IDXGIDevice>();
            Marshal.ThrowExceptionForHR(CaptureInterop.CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var pointer));
            try { winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
            finally { Marshal.Release(pointer); }
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            session = pool.CreateCaptureSession(item);
            var hasEffects = effects is { } enabled && (enabled.Highlight || enabled.ClickEffects);
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled")) session.IsCursorCaptureEnabled = cursor && !hasEffects;
            else if (!cursor || hasEffects) throw new NotSupportedException("This Windows session cannot guarantee hiding the native capture cursor.");
            if (hasEffects) mouse = new(effects!, window is null ? 0 : monitor);
            pool.FrameArrived += FrameArrived;
            item.Closed += ItemClosed;
        }
        catch { session?.Dispose(); pool?.Dispose(); winrtDevice?.Dispose(); context.Dispose(); device.Dispose(); throw; }
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (window is not null) WindowsApplicationWindowDiscovery.CheckAlive(window);
        if (mouse is not null) await mouse.StartAsync(cancellationToken).ConfigureAwait(false);
        session.StartCapture();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
    }
    private void ItemClosed(GraphicsCaptureItem sender, object args) => Report(new IOException(window is null
        ? "The monitor capture item closed or the display disconnected."
        : "The selected application window closed or its capture session became unavailable. Intermediate files are retained."));
    private void Report(Exception ex) { failure = ex; ready.TrySetException(ex); Failed?.Invoke(this, ex); }
    private void FrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            lock (sync)
            {
                if (disposed) return;
                var frame = sender.TryGetNextFrame();
                if (frame is null) return;
                if (frame.ContentSize.Width != captureSize.Width || frame.ContentSize.Height != captureSize.Height)
                { var changedSize = new VideoSize(frame.ContentSize.Width, frame.ContentSize.Height); frame.Dispose(); ScreenCaptureLifecycle.EnsureDimensionsUnchanged(captureSize, changedSize, window is not null); }
                latest?.Dispose(); latest = frame; ready.TrySetResult();
            }
        }
        catch (Exception ex) { Report(ex); }
    }
    public ValueTask<VideoFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failure is not null) throw failure;
            if (window is not null) WindowsApplicationWindowDiscovery.CheckAlive(window);
            var frame = latest ?? throw new IOException("No real screen frame is available.");
            using var texture = new ID3D11Texture2D(CaptureInterop.GetTexture(frame.Surface));
            staging ??= device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)captureSize.Width, Height = (uint)captureSize.Height, MipLevels = 1, ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read, BindFlags = BindFlags.None
            });
            context.CopyResource(staging, texture);
            var mapped = context.Map(staging, 0, MapMode.Read);
            PooledPixels? owner = null;
            try
            {
                owner = new PooledPixels(checked(Size.Width * Size.Height * 4));
                BgraCaptureCopy.Copy(mapped.DataPointer, checked((int)mapped.RowPitch), captureSize, region, owner.Buffer);
                if (mouse is not null)
                {
                    var origin = window is null ? NativeMonitors.Origin(captureHandle) : WindowsApplicationWindowDiscovery.CaptureOrigin(captureHandle, captureSize);
                    mouse.Render(owner.Buffer.AsSpan(0, checked(Size.Width * Size.Height * 4)), Size, origin.X, origin.Y, region);
                }
                return ValueTask.FromResult(new VideoFrame(owner, Size, frame.SystemRelativeTime));
            }
            catch { owner?.Dispose(); throw; }
            finally { context.Unmap(staging, 0); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
        }
        pool.FrameArrived -= FrameArrived; item.Closed -= ItemClosed;
        var errors = new List<Exception>();
        void Release(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex); } }
        Release(session.Dispose); Release(pool.Dispose);
        if (mouse is not null) { try { await mouse.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); } }
        lock (sync)
        {
            if (latest is not null) Release(latest.Dispose); latest = null;
            if (staging is not null) Release(staging.Dispose);
            Release(context.Dispose); Release(winrtDevice.Dispose); Release(device.Dispose);
        }
        if (errors.Count > 0) throw new AggregateException("Capture cleanup failed.", errors);
    }
    private sealed class PooledPixels(int length) : IMemoryOwner<byte>
    {
        public byte[] Buffer { get; } = ArrayPool<byte>.Shared.Rent(length);
        private bool disposed;
        public Memory<byte> Memory => Buffer.AsMemory(0, length);
        public void Dispose() { if (!disposed) { disposed = true; ArrayPool<byte>.Shared.Return(Buffer); } }
    }
}
internal static class CaptureInterop
{
    [DllImport("d3d11.dll", ExactSpelling = true)] internal static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint result);
    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, int length, out nint value);
    [DllImport("combase.dll", ExactSpelling = true)] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll", ExactSpelling = true)] private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateMonitor(nint self, nint monitor, in Guid iid, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInterface(nint self, in Guid iid, out nint result);
    internal static GraphicsCaptureItem CreateItem(nint monitor, bool window = false)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var hstring));
        nint factory = 0;
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), out factory));
            var method = Marshal.GetDelegateForFunctionPointer<CreateMonitor>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), (window ? 3 : 4) * IntPtr.Size));
            Marshal.ThrowExceptionForHR(method(factory, monitor, new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"), out var pointer));
            try { return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(pointer); }
            finally { Marshal.Release(pointer); }
        }
        finally { if (factory != 0) Marshal.Release(factory); WindowsDeleteString(hstring); }
    }
    internal static nint GetTexture(IDirect3DSurface surface)
    {
        var inspectable = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        nint access = 0;
        try
        {
            var iid = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(inspectable, in iid, out access));
            var method = Marshal.GetDelegateForFunctionPointer<GetInterface>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(access), 3 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(method(access, typeof(ID3D11Texture2D).GUID, out var texture));
            return texture;
        }
        finally { if (access != 0) Marshal.Release(access); Marshal.Release(inspectable); }
    }
}
internal static class NativeMonitors
{
    internal static MousePoint Origin(nint handle)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
        if (!GetMonitorInfo(handle, ref info)) throw new IOException("The selected monitor disconnected while rendering mouse effects.");
        return new(info.Monitor.Left, info.Monitor.Top);
    }
    internal sealed record Monitor(nint Handle, MonitorDescriptor Descriptor);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public int Size; public Rect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorCallback(nint monitor, nint hdc, nint rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    internal static IReadOnlyList<Monitor> Enumerate()
    {
        var result = new List<Monitor>();
        MonitorCallback callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (GetMonitorInfo(monitor, ref info))
            {
                var width = info.Monitor.Right - info.Monitor.Left; var height = info.Monitor.Bottom - info.Monitor.Top;
                result.Add(new(monitor, new(info.Device, $"{info.Device} · {width} × {height}{((info.Flags & 1) != 0 ? " · Primary" : "")}", width, height, (info.Flags & 1) != 0)));
            }
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }
}
