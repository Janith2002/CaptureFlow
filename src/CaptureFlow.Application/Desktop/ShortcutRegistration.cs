using CaptureFlow.Core.Desktop;
namespace CaptureFlow.Application.Desktop;

/// <summary>Atomic registration: a conflict releases the entire attempted set.</summary>
public sealed class ShortcutRegistration(IGlobalShortcutRegistrar registrar) : IDisposable
{
    private bool disposed;
    public void Apply(ShortcutPreferences preferences)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        preferences.Validate(); Clear();
        if (!preferences.Enabled) return;
        try { foreach (var binding in preferences.Bindings) registrar.Register(binding); }
        catch { Clear(); throw; }
    }
    private void Clear() { foreach (var action in Enum.GetValues<ShortcutAction>()) registrar.Unregister(action); }
    public void Dispose() { if (disposed) return; disposed = true; try { Clear(); } finally { registrar.Dispose(); } }
}
