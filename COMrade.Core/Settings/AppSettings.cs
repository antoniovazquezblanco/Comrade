namespace COMrade.Core.Settings;

/// <summary>App-wide preferences from the settings page.</summary>
/// <param name="NewDevice">Settings for a device COMrade has not seen before.</param>
public sealed record AppSettings(DeviceSettings NewDevice)
{
    public static AppSettings Default { get; } = new(DeviceSettings.Default);
}
