using COMrade.Core.Serial;
using COMrade.Core.Terminal;

namespace COMrade.Core.Settings;

/// <summary>Everything remembered for one device.</summary>
public sealed record DeviceSettings(SerialSettings Serial, TerminalSettings Terminal, bool ReopenAutomatically = true)
{
    /// <summary>Settings for a device COMrade has not seen before.</summary>
    public static DeviceSettings Default { get; } = new(SerialSettings.Default, TerminalSettings.Default);
}
