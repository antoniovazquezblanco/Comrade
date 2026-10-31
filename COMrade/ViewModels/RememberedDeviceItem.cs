namespace COMrade.ViewModels;

/// <summary>A device with saved settings, in the settings page.</summary>
/// <param name="Key">Identity key in the device store.</param>
/// <param name="Title">For example "USB-SERIAL CH340 · last on COM5".</param>
/// <param name="Detail">For example "Used 31/10/2026 · 115200 8N1 · CR".</param>
public sealed record RememberedDeviceItem(string Key, string Title, string Detail);
