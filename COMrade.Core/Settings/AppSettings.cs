using System.Text.Json.Serialization;
using COMrade.Core.Terminal;

namespace COMrade.Core.Settings;

/// <summary>App-wide preferences from the settings page.</summary>
/// <param name="NewDevice">Settings for a device COMrade has not seen before.</param>
/// <param name="TabMode">Whether tabs shrink (the default) or scroll when there are many.</param>
/// <param name="Terminal">Font, colours and cursor of every terminal; null in files from before it existed.</param>
public sealed record AppSettings(DeviceSettings NewDevice, TabMode TabMode = TabMode.Compact, TerminalAppearance? Terminal = null)
{
    public static AppSettings Default { get; } = new(DeviceSettings.Default, Terminal: TerminalAppearance.Default);

    /// <summary>The terminal appearance, with the defaults for settings saved before it existed.</summary>
    [JsonIgnore]
    public TerminalAppearance TerminalOrDefault => Terminal ?? TerminalAppearance.Default;
}
