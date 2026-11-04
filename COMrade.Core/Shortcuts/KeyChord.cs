using Windows.System;

namespace COMrade.Core.Shortcuts;

/// <summary>The keys of a shortcut: Ctrl, maybe Shift, and one more key, such as Ctrl+Shift+T.</summary>
/// <remarks>
/// Shortcuts always take Ctrl and never Alt: keys pressed without Ctrl are typed into the device,
/// and Ctrl with Alt is AltGr, which types characters such as @ on many keyboards. The last key is
/// one that is the same on every keyboard layout.
/// </remarks>
public readonly record struct KeyChord(VirtualKey Key, bool Shift = false)
{
    public const VirtualKey Plus = (VirtualKey)0xBB;
    public const VirtualKey Comma = (VirtualKey)0xBC;
    public const VirtualKey Minus = (VirtualKey)0xBD;
    public const VirtualKey Period = (VirtualKey)0xBE;

    private const string CtrlPrefix = "Ctrl+";
    private const string ShiftPrefix = "Shift+";

    private static readonly Dictionary<VirtualKey, string> Names = CreateNames();
    private static readonly Dictionary<string, VirtualKey> KeysByName =
        Names.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The keys, or null when <paramref name="key"/> cannot be part of a shortcut.</summary>
    public static KeyChord? From(VirtualKey key, bool shift) => Names.ContainsKey(key) ? new(key, shift) : null;

    /// <summary>Reads keys written as <see cref="ToString"/> does; null when they are no shortcut.</summary>
    public static KeyChord? Parse(string? text)
    {
        var rest = text?.Trim() ?? string.Empty;
        if (!rest.StartsWith(CtrlPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        rest = rest[CtrlPrefix.Length..];

        var shift = rest.StartsWith(ShiftPrefix, StringComparison.OrdinalIgnoreCase);
        if (shift)
            rest = rest[ShiftPrefix.Length..];

        return KeysByName.TryGetValue(rest, out var key) ? new KeyChord(key, shift) : null;
    }

    /// <summary>For example "Ctrl+Shift+T", "Ctrl+Tab" or "Ctrl+,".</summary>
    public override string ToString() =>
        CtrlPrefix + (Shift ? ShiftPrefix : string.Empty) + Names.GetValueOrDefault(Key, Key.ToString());

    private static Dictionary<VirtualKey, string> CreateNames()
    {
        var names = new Dictionary<VirtualKey, string>
        {
            [VirtualKey.Tab] = "Tab",
            [VirtualKey.Enter] = "Enter",
            [VirtualKey.Space] = "Space",
            [VirtualKey.Back] = "Backspace",
            [VirtualKey.Insert] = "Insert",
            [VirtualKey.Delete] = "Delete",
            [VirtualKey.Home] = "Home",
            [VirtualKey.End] = "End",
            [VirtualKey.PageUp] = "PageUp",
            [VirtualKey.PageDown] = "PageDown",
            [VirtualKey.Up] = "Up",
            [VirtualKey.Down] = "Down",
            [VirtualKey.Left] = "Left",
            [VirtualKey.Right] = "Right",
            [Plus] = "+",
            [Comma] = ",",
            [Minus] = "-",
            [Period] = ".",
        };
        for (var key = VirtualKey.A; key <= VirtualKey.Z; key++)
            names[key] = key.ToString();
        for (var key = VirtualKey.Number0; key <= VirtualKey.Number9; key++)
            names[key] = ((char)key).ToString();
        for (var key = VirtualKey.F1; key <= VirtualKey.F24; key++)
            names[key] = key.ToString();
        return names;
    }
}
