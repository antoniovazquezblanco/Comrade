using COMrade.Core.Serial;
using COMrade.Core.Terminal;

namespace COMrade.Core.Settings;

/// <summary>Everything remembered for one device.</summary>
/// <param name="Name">Name the user gave the device's tab, e.g. "Router console"; null for the default title.</param>
public sealed record DeviceSettings(SerialSettings Serial, TerminalSettings Terminal, bool ReopenAutomatically = true, string? Name = null)
{
    /// <summary>Settings for a device COMrade has not seen before.</summary>
    public static DeviceSettings Default { get; } = new(SerialSettings.Default, TerminalSettings.Default);
}
