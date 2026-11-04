using Windows.System;

namespace COMrade.Core.Shortcuts;

/// <summary>The keys of each shortcut: the defaults, with the changes made in the settings.</summary>
/// <remarks>
/// Only the changes are saved, so later versions can add shortcuts with keys of their own. Keys
/// set for one shortcut leave any other shortcut without them: the keys a change gives one win over
/// the default keys of another. Changes are kept as text, and a file from another version that
/// names an unknown shortcut or keys loses only those.
/// </remarks>
public sealed class Keymap
{
    private readonly Dictionary<ShortcutAction, KeyChord?> _changes;
    private readonly Dictionary<ShortcutAction, KeyChord> _keys = [];
    private readonly Dictionary<KeyChord, ShortcutAction> _actions = [];

    /// <param name="changes">
    /// The changes, as <see cref="Changes"/> gives them: each shortcut's keys, or null for none.
    /// </param>
    public Keymap(IReadOnlyDictionary<string, string?>? changes = null)
        : this(Read(changes))
    {
    }

    private Keymap(Dictionary<ShortcutAction, KeyChord?> changes)
    {
        _changes = changes;
        foreach (var action in Actions)
        {
            if (_changes.TryGetValue(action, out var keys) && keys is not null)
                Add(action, keys.Value);
        }
        foreach (var action in Actions)
        {
            if (!_changes.ContainsKey(action) && DefaultFor(action) is { } keys)
                Add(action, keys);
        }
    }

    public static IReadOnlyList<ShortcutAction> Actions { get; } = Enum.GetValues<ShortcutAction>();

    /// <summary>
    /// Windows Terminal's keys for the actions it has too. Ctrl+Shift, because Ctrl with a letter
    /// is a control character that devices use (Ctrl+T, Ctrl+W, Ctrl+C...), so it must reach the
    /// terminal.
    /// </summary>
    public static IReadOnlyDictionary<ShortcutAction, KeyChord> Defaults { get; } = new Dictionary<ShortcutAction, KeyChord>
    {
        [ShortcutAction.NewTab] = new(VirtualKey.T, Shift: true),
        [ShortcutAction.CloseTab] = new(VirtualKey.W, Shift: true),
        [ShortcutAction.NextTab] = new(VirtualKey.Tab),
        [ShortcutAction.PreviousTab] = new(VirtualKey.Tab, Shift: true),
        [ShortcutAction.OpenOrClosePort] = new(VirtualKey.O, Shift: true),
        [ShortcutAction.SendBreak] = new(VirtualKey.B, Shift: true),
        [ShortcutAction.Copy] = new(VirtualKey.C, Shift: true),
        [ShortcutAction.Paste] = new(VirtualKey.V, Shift: true),
        [ShortcutAction.ClearTerminal] = new(VirtualKey.K, Shift: true),
        [ShortcutAction.StartOrStopLogging] = new(VirtualKey.L, Shift: true),
        [ShortcutAction.ScrollToTop] = new(VirtualKey.Home, Shift: true),
        [ShortcutAction.ScrollPageUp] = new(VirtualKey.PageUp, Shift: true),
        [ShortcutAction.ScrollPageDown] = new(VirtualKey.PageDown, Shift: true),
        [ShortcutAction.ScrollToBottom] = new(VirtualKey.End, Shift: true),
        [ShortcutAction.OpenSettings] = new(KeyChord.Comma),
    };

    public static Keymap Default { get; } = new();

    /// <summary>Whether any shortcut has other keys than its default ones.</summary>
    public bool HasChanges => Actions.Any(IsChanged);

    /// <summary>
    /// The changes to keep in the settings: each changed shortcut's keys as text, such as
    /// "Ctrl+Shift+T", or null for none; null when nothing is changed.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Changes =>
        _changes.Count == 0
            ? null
            : Actions.Where(_changes.ContainsKey).ToDictionary(action => action.ToString(), action => _changes[action]?.ToString());

    public static KeyChord? DefaultFor(ShortcutAction action) => Defaults.TryGetValue(action, out var keys) ? keys : null;

    /// <summary>The shortcut's keys; null when it has none.</summary>
    public KeyChord? KeysFor(ShortcutAction action) => _keys.TryGetValue(action, out var keys) ? keys : null;

    /// <summary>What the keys do; null when they are no shortcut, and reach the device or the focused control.</summary>
    public ShortcutAction? ActionFor(KeyChord keys) => _actions.TryGetValue(keys, out var action) ? action : null;

    public bool IsChanged(ShortcutAction action) => KeysFor(action) != DefaultFor(action);

    /// <summary>A copy where <paramref name="keys"/> are the shortcut's, taken from any shortcut that had them.</summary>
    public Keymap With(ShortcutAction action, KeyChord? keys)
    {
        var changes = new Dictionary<ShortcutAction, KeyChord?>(_changes);
        if (keys is { } taken && ActionFor(taken) is { } previous && previous != action)
            changes[previous] = null;

        if (keys == DefaultFor(action))
            changes.Remove(action);
        else
            changes[action] = keys;
        return new Keymap(changes);
    }

    /// <summary>A copy where the shortcut has its default keys again, taken from any shortcut that had them.</summary>
    public Keymap Reset(ShortcutAction action) => With(action, DefaultFor(action));

    private void Add(ShortcutAction action, KeyChord keys)
    {
        if (_actions.TryAdd(keys, action))
            _keys[action] = keys;
    }

    private static Dictionary<ShortcutAction, KeyChord?> Read(IReadOnlyDictionary<string, string?>? changes)
    {
        var read = new Dictionary<ShortcutAction, KeyChord?>();
        foreach (var (name, text) in changes ?? new Dictionary<string, string?>())
        {
            if (!Enum.TryParse<ShortcutAction>(name, ignoreCase: true, out var action) || !Enum.IsDefined(action))
                continue;
            if (text is null)
                read[action] = null;
            else if (KeyChord.Parse(text) is { } keys)
                read[action] = keys;
        }
        return read;
    }
}
