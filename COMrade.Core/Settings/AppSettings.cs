namespace COMrade.Core.Settings;

/// <summary>App-wide preferences from the settings page.</summary>
/// <param name="NewDevice">Settings for a device COMrade has not seen before.</param>
/// <param name="TabMode">Whether tabs shrink (the default) or scroll when there are many.</param>
public sealed record AppSettings(DeviceSettings NewDevice, TabMode TabMode = TabMode.Compact)
{
    public static AppSettings Default { get; } = new(DeviceSettings.Default);
}
