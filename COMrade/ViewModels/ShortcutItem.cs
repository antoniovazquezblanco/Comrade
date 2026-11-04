using COMrade.Core.Shortcuts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMrade.ViewModels;

/// <summary>A keyboard shortcut in the settings page.</summary>
public sealed partial class ShortcutItem : ObservableObject
{
    public ShortcutItem(ShortcutAction action, Keymap keymap)
    {
        Action = action;
        Name = NameOf(action);
        ResetToolTip = Keymap.DefaultFor(action) is { } keys ? $"Back to {keys}" : "Back to no shortcut";
        Update(keymap, isRecording: false);
    }

    public ShortcutAction Action { get; }

    /// <summary>For example "Close the tab".</summary>
    public string Name { get; }

    /// <summary>For example "Ctrl+Shift+W" or "None"; a prompt while the new keys are awaited.</summary>
    [ObservableProperty]
    public partial string Keys { get; private set; }

    [ObservableProperty]
    public partial bool HasKeys { get; private set; }

    /// <summary>Whether the keys are not the default ones.</summary>
    [ObservableProperty]
    public partial bool IsChanged { get; private set; }

    /// <summary>For example "Back to Ctrl+Shift+W".</summary>
    public string ResetToolTip { get; }

    public static string NameOf(ShortcutAction action) => action switch
    {
        ShortcutAction.NewTab => "Open a port in a new tab",
        ShortcutAction.CloseTab => "Close the tab",
        ShortcutAction.NextTab => "Next tab",
        ShortcutAction.PreviousTab => "Previous tab",
        ShortcutAction.OpenOrClosePort => "Open or close the port",
        ShortcutAction.SendBreak => "Send a break",
        ShortcutAction.Copy => "Copy",
        ShortcutAction.Paste => "Paste",
        ShortcutAction.Find => "Find in the output",
        ShortcutAction.ClearTerminal => "Clear the terminal",
        ShortcutAction.StartOrStopLogging => "Start or stop logging",
        ShortcutAction.ScrollToTop => "Scroll to the top",
        ShortcutAction.ScrollPageUp => "Scroll up a page",
        ShortcutAction.ScrollPageDown => "Scroll down a page",
        ShortcutAction.ScrollToBottom => "Scroll to the bottom",
        ShortcutAction.OpenSettings => "Settings",
        _ => action.ToString(),
    };

    internal void Update(Keymap keymap, bool isRecording)
    {
        var keys = keymap.KeysFor(Action);
        Keys = isRecording ? "Press the new keys..." : keys?.ToString() ?? "None";
        HasKeys = keys is not null;
        IsChanged = keymap.IsChanged(Action);
    }
}
