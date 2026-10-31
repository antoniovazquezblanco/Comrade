namespace COMrade.Core.Settings;

/// <summary>A device's settings as stored, with when it was last used.</summary>
public sealed record RememberedDevice(DeviceSettings Settings, DateTimeOffset LastUsed);
