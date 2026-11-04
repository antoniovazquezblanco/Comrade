namespace COMrade.Core.Shortcuts;

/// <summary>What a keyboard shortcut does, in the order the settings page lists them.</summary>
/// <remarks>The settings file keeps them by name: rename none.</remarks>
public enum ShortcutAction
{
    /// <summary>Shows the ports to open one in a new tab.</summary>
    NewTab,

    CloseTab,

    NextTab,

    PreviousTab,

    /// <summary>Opens the tab's port, or closes it while it is open or about to be.</summary>
    OpenOrClosePort,

    SendBreak,

    /// <summary>Copies the text selected in the terminal.</summary>
    Copy,

    /// <summary>Sends the clipboard text to the device, as if it had been typed.</summary>
    Paste,

    ClearTerminal,

    StartOrStopLogging,

    /// <summary>Scrolls the terminal to the oldest output it keeps.</summary>
    ScrollToTop,

    ScrollPageUp,

    ScrollPageDown,

    /// <summary>Scrolls the terminal to the newest output.</summary>
    ScrollToBottom,

    OpenSettings,
}
