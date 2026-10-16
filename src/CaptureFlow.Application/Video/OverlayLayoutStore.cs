using CaptureFlow.Core.Video;
namespace CaptureFlow.Application.Video;
public sealed class OverlayLayoutStore : IOverlayLayoutStore
{
    private OverlayLayout current = new();
    public OverlayLayout Current
    {
        get => Volatile.Read(ref current);
        set { value.Validate(); Interlocked.Exchange(ref current, value); }
    }
}
