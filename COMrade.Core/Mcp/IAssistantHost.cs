using COMrade.Core.Ports;
using COMrade.Core.Serial;
using COMrade.Core.Sessions;
using COMrade.Core.Settings;

namespace COMrade.Core.Mcp;

/// <summary>COMrade's windows as AI assistants use them: the device tabs, and opening new ones.</summary>
/// <remarks>Implemented by the app. Called from any thread.</remarks>
public interface IAssistantHost
{
    /// <summary>The device tabs of every window.</summary>
    Task<IReadOnlyList<IAssistantTab>> GetTabsAsync();

    /// <summary>
    /// The device's tab; a device without one gets a new tab, selected, in the window used last.
    /// No window is brought to the front.
    /// </summary>
    Task<IAssistantTab> OpenTabAsync(SerialPortInfo port);

    /// <summary>Selects the tab and brings its window to the front.</summary>
    Task ShowTabAsync(IAssistantTab tab);
}

/// <summary>A device tab as AI assistants use it: what they do shows in the tab, as if typed.</summary>
/// <remarks>Implemented by the app. Called from any thread.</remarks>
public interface IAssistantTab
{
    PortSession Session { get; }

    /// <summary>The tab's settings, saved for the device as they change.</summary>
    DeviceSettings Settings { get; }

    /// <summary>The file the tab is logging to, or null.</summary>
    string? LogPath { get; }

    /// <summary>Opens the port, as the tab's Open port button does.</summary>
    Task OpenAsync();

    /// <summary>
    /// Sends bytes as typing does; false when the port is not open or failed. With local echo on,
    /// the terminal shows <paramref name="typed"/>: the text as typed, Enter as CR (none for raw bytes).
    /// </summary>
    Task<bool> SendAsync(byte[] data, string? typed);

    /// <summary>Applies and saves settings, as the port settings do; false when the driver rejects them.</summary>
    Task<bool> ChangeSettingsAsync(DeviceSettings settings);

    /// <summary>Sets DTR and RTS, as the toolbar's toggles do; false when the open port failed to.</summary>
    Task<bool> SetControlLinesAsync(ControlLines lines);

    /// <summary>Clears the terminal, as the toolbar's Clear does.</summary>
    Task ClearAsync();

    /// <summary>Starts logging into the log folder and returns the file; throws <see cref="IOException"/> when it cannot.</summary>
    Task<string> StartLogAsync();

    Task StopLogAsync();

    /// <summary>Shows which assistants use the tab and what they did last. Must return at once.</summary>
    void ShowAssistantStatus(AssistantStatus status);
}

/// <summary>AI assistants using a tab.</summary>
/// <param name="Clients">Assistants connected that have used the tab.</param>
/// <param name="LastAction">What one of them did last, e.g. "Sent \"printenv\"".</param>
/// <param name="LastActionAt">When.</param>
public sealed record AssistantStatus(int Clients, string? LastAction, DateTimeOffset? LastActionAt)
{
    public static AssistantStatus None { get; } = new(0, null, null);
}
