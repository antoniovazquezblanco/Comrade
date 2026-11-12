using COMrade.Core.Serial;
using COMrade.Core.Terminal;

namespace COMrade.Core.Settings;

/// <summary>Everything remembered for one device.</summary>
/// <param name="Name">Name the user gave the device's tab, e.g. "Router console"; null for the default title.</param>
/// <param name="McpEnabled">AI assistants may use the device while AI access is set to per tab.</param>
/// <param name="View">What the tab shows: the terminal, or the analysis of what went over the port.</param>
public sealed record DeviceSettings(
    SerialSettings Serial,
    TerminalSettings Terminal,
    bool ReopenAutomatically = true,
    string? Name = null,
    bool McpEnabled = false,
    OutputView View = OutputView.Terminal)
{
    /// <summary>Settings for a device COMrade has not seen before.</summary>
    public static DeviceSettings Default { get; } = new(SerialSettings.Default, TerminalSettings.Default);
}

/// <summary>What a device's tab shows.</summary>
public enum OutputView
{
    /// <summary>The terminal, as the device means its output to look.</summary>
    Terminal,

    /// <summary>What went over the port both ways, by time and sender, see <see cref="TrafficView.TrafficRows"/>.</summary>
    Analysis,
}
