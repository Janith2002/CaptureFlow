namespace CaptureFlow.Core.Desktop;

public enum ShortcutAction { StartStop, PauseResume, ToggleMicrophone, ToggleWindow }
[Flags] public enum ShortcutModifiers { Alt = 1, Control = 2, Shift = 4, Windows = 8 }
public sealed record ShortcutBinding(ShortcutAction Action, ShortcutModifiers Modifiers, int VirtualKey)
{
    public string Display => string.Join("+", new[] {
        Modifiers.HasFlag(ShortcutModifiers.Control) ? "Ctrl" : null,
        Modifiers.HasFlag(ShortcutModifiers.Alt) ? "Alt" : null,
        Modifiers.HasFlag(ShortcutModifiers.Shift) ? "Shift" : null,
        Modifiers.HasFlag(ShortcutModifiers.Windows) ? "Win" : null,
        VirtualKey is >= 112 and <= 135 ? $"F{VirtualKey - 111}" : ((char)VirtualKey).ToString()
    }.Where(s => s is not null));
    public static ShortcutBinding Parse(ShortcutAction action, string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var modifiers = (ShortcutModifiers)0;
        foreach (var part in parts.SkipLast(1))
            modifiers |= part.ToUpperInvariant() switch { "CTRL" => ShortcutModifiers.Control, "ALT" => ShortcutModifiers.Alt, "SHIFT" => ShortcutModifiers.Shift, "WIN" => ShortcutModifiers.Windows, _ => throw new ArgumentException("Use Ctrl, Alt, Shift or Win as modifiers.") };
        var key = parts.LastOrDefault()?.ToUpperInvariant() ?? "";
        var code = key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) ? key[0] :
            key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 24 ? 111 + f : 0;
        var result = new ShortcutBinding(action, modifiers, code);
        result.Validate(); return result;
    }
    public void Validate()
    {
        if (!Enum.IsDefined(Action) || Modifiers == 0 || ((int)Modifiers & ~15) != 0 ||
            !(VirtualKey is >= 48 and <= 57 or >= 65 and <= 90 or >= 112 and <= 135))
            throw new ArgumentException("Use a modifier and a letter, digit or F1–F24 key.");
    }
}
public sealed record ShortcutPreferences(bool Enabled, bool MinimizeToTray, ShortcutBinding[] Bindings)
{
    public static ShortcutPreferences Defaults => new(true, false,
        Enum.GetValues<ShortcutAction>().Zip(new[] { 'R', 'P', 'M', 'C' }, (a, k) => new ShortcutBinding(a, ShortcutModifiers.Control | ShortcutModifiers.Alt, k)).ToArray());
    public void Validate()
    {
        if (Bindings is null || Bindings.Length != 4 || Bindings.Any(b => b is null) || Bindings.Select(b => b.Action).Distinct().Count() != 4)
            throw new ArgumentException("Assign each of the four actions exactly once.");
        foreach (var binding in Bindings) binding.Validate();
        if (Bindings.Select(b => (b.Modifiers, b.VirtualKey)).Distinct().Count() != Bindings.Length)
            throw new ArgumentException("Each shortcut must have a different key combination.");
    }
}
public interface IShortcutPreferencesStore
{
    Task<ShortcutPreferences> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ShortcutPreferences preferences, CancellationToken cancellationToken = default);
}
public interface IGlobalShortcutRegistrar : IDisposable
{
    void Register(ShortcutBinding binding);
    void Unregister(ShortcutAction action);
}
