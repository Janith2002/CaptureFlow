using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CaptureFlow.Core.Video;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
namespace CaptureFlow.Infrastructure.Video;

/// <summary>Windows camera frame sources, kept separate from encoding and WPF presentation.</summary>
public sealed class WindowsCameraCaptureBackend : ICameraCaptureBackend
{
    // MediaCapture initialization may display permission UI; retain the application's UI context.
    private readonly SynchronizationContext? initializationContext;
    public WindowsCameraCaptureBackend(SynchronizationContext? initializationContext = null) =>
        this.initializationContext = initializationContext ?? SynchronizationContext.Current;
    public async Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Trace.TraceInformation($"Camera metadata enumeration: api=MediaFrameSourceGroup.FindAllAsync; thread={Environment.CurrentManagedThreadId}; apartment={Thread.CurrentThread.GetApartmentState()}; no camera initialization.");
        try
        {
        var groups = await MediaFrameSourceGroup.FindAllAsync().AsTask(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return groups.Where(g => g.SourceInfos.Any(IsColorVideo))
            .Select(g => new CameraDevice(g.Id, g.DisplayName)).OrderBy(g => g.Name).ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Trace.TraceError($"Camera metadata enumeration failed: api=MediaFrameSourceGroup.FindAllAsync; type={ex.GetType().Name}; HRESULT=0x{ex.HResult:X8}; stack={new StackTrace(ex, false)}");
            throw CameraErrors.Describe(ex, enumerating: true);
        }
    }
    private static bool IsColorVideo(MediaFrameSourceInfo info) => info.SourceKind == MediaFrameSourceKind.Color &&
        info.MediaStreamType is Windows.Media.Capture.MediaStreamType.VideoRecord or Windows.Media.Capture.MediaStreamType.VideoPreview;

    public Task<ICameraCaptureSession> OpenAsync(string deviceId, CancellationToken cancellationToken)
    {
        if (initializationContext is null) throw new InvalidOperationException("Open the camera using a running STA application context. Camera discovery does not require camera initialization.");
        var owner = new CameraOwnerContext(initializationContext);
        return owner.InvokeAsync(async () => (ICameraCaptureSession)new CameraSessionLifetime(await OpenOnContextAsync(deviceId, cancellationToken), owner));
    }
    private static async Task<ICameraCaptureSession> OpenOnContextAsync(string deviceId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        MediaCapture? capture = null;
        try
        {
            IReadOnlyList<MediaFrameSourceGroup> groups;
            try { groups = await MediaFrameSourceGroup.FindAllAsync().AsTask(token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { throw CameraErrors.Describe(ex, enumerating: true); }
            var group = groups.FirstOrDefault(g => g.Id == deviceId) ?? throw new CameraOperationException(
                CameraFailureKind.Disconnected, "The selected camera is disconnected. Refresh cameras.", new IOException("Camera source group is no longer available."));
            capture = new MediaCapture();
            Trace.TraceInformation($"Camera initializing: thread={Environment.CurrentManagedThreadId}; apartment={Thread.CurrentThread.GetApartmentState()}");
            // Do not cancel the await and then close an in-flight native InitializeAsync.
            // Observe native completion before checking cancellation and disposing on this owner context.
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group, StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl, MemoryPreference = MediaCaptureMemoryPreference.Cpu
            }).AsTask();
            var source = capture.FrameSources.Values.Where(s => IsColorVideo(s.Info))
                .OrderBy(s => s.Info.MediaStreamType == Windows.Media.Capture.MediaStreamType.VideoRecord ? 0 : 1).FirstOrDefault()
                ?? throw new NotSupportedException("This camera has no color video frame source.");
            token.ThrowIfCancellationRequested();
            return new WindowsCameraSession(capture, source);
        }
        catch (OperationCanceledException) { capture?.Dispose(); throw; }
        catch (Exception ex)
        {
            capture?.Dispose();
            throw CameraErrors.Describe(ex, enumerating: false);
        }
    }
}

internal sealed class WindowsCameraSession : ICameraCaptureSession
{
    private readonly object sync = new();
    private readonly MediaCapture capture;
    private readonly MediaFrameSource source;
    private readonly Dictionary<CameraFormat, MediaFrameFormat> formats = [];
    private MediaFrameReader? reader;
    private VideoFrame? latest;
    private TaskCompletionSource ready = NewReady();
    private Exception? failure;
    private long lastArrival;
    private volatile bool disposed;
    private readonly object disposeSync = new();
    private bool readerStarted;
    private Task? disposal;
    public event EventHandler<Exception>? Failed;
    public VideoSize Size { get; private set; } = new(0, 0);
    public IReadOnlyList<CameraFormat> SupportedFormats { get; }
    public WindowsCameraSession(MediaCapture capture, MediaFrameSource source)
    {
        this.capture = capture; this.source = source;
        var index = 0;
        foreach (var native in source.SupportedFormats)
        {
            if (native.VideoFormat is null) continue;
            var format = new CameraFormat($"{source.Info.Id}:{index++}", checked((int)native.VideoFormat.Width), checked((int)native.VideoFormat.Height),
                native.FrameRate.Numerator, native.FrameRate.Denominator, native.Subtype);
            try { format.Validate(); formats.Add(format, native); } catch (ArgumentException) { }
        }
        SupportedFormats = formats.Keys.OrderByDescending(f => (long)f.Width * f.Height).ThenBy(f => f.FramesPerSecond).ToArray();
        capture.Failed += CaptureFailed;
    }
    private static TaskCompletionSource NewReady() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task ConfigureAsync(CameraFormat format, CancellationToken cancellationToken)
    {
        Trace.TraceInformation($"Camera native configure: thread={Environment.CurrentManagedThreadId}; apartment={Thread.CurrentThread.GetApartmentState()}");
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!formats.TryGetValue(format, out var native)) throw new NotSupportedException("Camera format is not supported by the selected device.");
        cancellationToken.ThrowIfCancellationRequested();
        await StopReaderAsync();
        lock (sync) { latest?.Dispose(); latest = null; ready = NewReady(); failure = null; Size = new(format.Width, format.Height); }
        await source.SetFormatAsync(native).AsTask(); cancellationToken.ThrowIfCancellationRequested();
        // Windows converts supported native formats into CPU BGRA frames for both preview and encoder.
        try { reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Argb32).AsTask(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Some drivers only expose native CPU bitmaps. Convert those explicitly in FrameArrived.
            Trace.TraceWarning($"Camera BGRA reader unavailable; trying native conversion. HRESULT=0x{ex.HResult:X8}");
            cancellationToken.ThrowIfCancellationRequested();
            reader = await capture.CreateFrameReaderAsync(source).AsTask();
        }
        reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
        reader.FrameArrived += FrameArrived;
        cancellationToken.ThrowIfCancellationRequested();
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Trace.TraceInformation($"Camera native start: thread={Environment.CurrentManagedThreadId}; apartment={Thread.CurrentThread.GetApartmentState()}");
        var current = reader ?? throw new InvalidOperationException("Configure a camera format first.");
        cancellationToken.ThrowIfCancellationRequested();
        var status = await current.StartAsync().AsTask();
        readerStarted = status == MediaFrameReaderStartStatus.Success;
        cancellationToken.ThrowIfCancellationRequested();
        if (status != MediaFrameReaderStartStatus.Success) throw new IOException("Camera preview could not start: " + status + ". Try another reported format or close other camera apps.");
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }
    private void CaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs args)
    {
        lock (sync) { if (disposed || !ReferenceEquals(sender, capture)) return; Report(new IOException($"Camera capture failed (0x{args.Code:X8}): {args.Message}")); }
    }
    private void Report(Exception error)
    {
        lock (sync) { if (disposed || failure is not null) return; failure = error; ready.TrySetException(error); }
        Failed?.Invoke(this, error);
    }
    private void FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        try
        {
            lock (sync)
            {
                if (disposed || !ReferenceEquals(sender, reader)) return;
                using var frame = sender.TryAcquireLatestFrame();
                if (frame is null) return;
                using var bitmap = frame.VideoMediaFrame?.SoftwareBitmap;
                if (bitmap is null) throw new IOException("Camera did not supply a CPU color frame.");
                if (bitmap.PixelWidth != Size.Width || bitmap.PixelHeight != Size.Height) throw new IOException("Camera changed its video dimensions. Reload camera formats.");
                using var converted = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? null : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                var pixels = CopyPixels(converted ?? bitmap);
                var replacement = new VideoFrame(pixels, Size, frame.SystemRelativeTime ?? TimeSpan.FromTicks((long)RecordingTimeline.NowQpc));
                latest?.Dispose(); latest = replacement; lastArrival = Stopwatch.GetTimestamp(); ready.TrySetResult();
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
            if (latest is null) throw new IOException("No live camera frame is available.");
            if (Stopwatch.GetElapsedTime(lastArrival) > TimeSpan.FromSeconds(5))
                throw new IOException("Camera stopped delivering frames. Check disconnection, camera privacy settings, or another app using the camera.");
            return ValueTask.FromResult(latest.Lease());
        }
    }
    private async Task StopReaderAsync()
    {
        var current = reader; reader = null;
        if (current is null) return;
        // Closing marks disposed under sync first, draining any in-flight frame copy and rejecting later callbacks.
        try { if (readerStarted) await current.StopAsync().AsTask(); }
        finally { readerStarted = false; current.FrameArrived -= FrameArrived; current.Dispose(); }
    }
    public ValueTask DisposeAsync()
    {
        lock (disposeSync) { disposed = true; disposal ??= DisposeCoreAsync(); return new(disposal); }
    }
    private async Task DisposeCoreAsync()
    {
        await Task.Yield(); // Release the callback lock before native event removal/close on the owner context.
        await Task.Run(() => { lock (sync) { } }); // Drain an in-flight copy without blocking the STA dispatcher.
        var errors = new List<Exception>();
        try { await StopReaderAsync(); } catch (Exception ex) { errors.Add(ex); }
        try { capture.Failed -= CaptureFailed; } catch (Exception ex) { errors.Add(ex); }
        lock (sync) { latest?.Dispose(); latest = null; }
        try { capture.Dispose(); } catch (Exception ex) { errors.Add(ex); }
        Trace.TraceInformation($"Camera native close: thread={Environment.CurrentManagedThreadId}; apartment={Thread.CurrentThread.GetApartmentState()}; errors={errors.Count}");
        if (errors.Count > 0) throw new AggregateException("Camera native cleanup failed.", errors);
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBuffer(nint self, out nint buffer, out uint capacity);
    private static PooledPixels CopyPixels(SoftwareBitmap bitmap)
    {
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        var owner = new PooledPixels(checked(bitmap.PixelWidth * bitmap.PixelHeight * 4));
        nint inspectable = 0, access = 0;
        try
        {
            inspectable = WinRT.MarshalInterface<Windows.Foundation.IMemoryBufferReference>.FromManaged(reference);
            var iid = new Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(inspectable, in iid, out access));
            var method = Marshal.GetDelegateForFunctionPointer<GetBuffer>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(access), 3 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(method(access, out var pointer, out var capacity));
            var plane = buffer.GetPlaneDescription(0); var rowBytes = checked(bitmap.PixelWidth * 4);
            for (var row = 0; row < bitmap.PixelHeight; row++)
            {
                var offset = checked(plane.StartIndex + row * plane.Stride);
                if (offset < 0 || offset + (long)rowBytes > capacity) throw new IOException("Camera returned an invalid pixel buffer layout.");
                Marshal.Copy(pointer + offset, owner.Buffer, row * rowBytes, rowBytes);
            }
            return owner;
        }
        catch { owner.Dispose(); throw; }
        finally { if (access != 0) Marshal.Release(access); if (inspectable != 0) Marshal.Release(inspectable); }
    }
    private sealed class PooledPixels(int length) : IMemoryOwner<byte>
    {
        public byte[] Buffer { get; } = ArrayPool<byte>.Shared.Rent(length);
        public Memory<byte> Memory => Buffer.AsMemory(0, length);
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) ArrayPool<byte>.Shared.Return(Buffer); }
    }
}
