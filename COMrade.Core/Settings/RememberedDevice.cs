namespace COMrade.Core.Settings;

/// <summary>A device's settings as stored, with when it was last used.</summary>
/// <param name="Description">Device name when it was last used, e.g. "USB-SERIAL CH340".</param>
/// <param name="PortName">COM port when it was last used, e.g. "COM5".</param>
public sealed record RememberedDevice(
    DeviceSettings Settings,
    DateTimeOffset LastUsed,
    string? Description = null,
    string? PortName = null);
