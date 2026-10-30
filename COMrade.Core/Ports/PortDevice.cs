namespace COMrade.Core.Ports;

/// <summary>A device in Windows' "Ports (COM &amp; LPT)" class, as reported by the operating system.</summary>
/// <param name="DeviceId">Device instance ID, e.g. <c>USB\VID_1A86&amp;PID_7523\5&amp;2F3C1B2&amp;0&amp;3</c>.</param>
/// <param name="PortName">Port name assigned by Windows, e.g. <c>COM5</c> or <c>LPT1</c>; null when unknown.</param>
/// <param name="FriendlyName">Name shown in Device Manager, e.g. <c>USB-SERIAL CH340 (COM5)</c>.</param>
/// <param name="ParentDeviceId">Instance ID of the parent device, if any.</param>
/// <param name="LocationPath">Physical location path (USB hub and socket), if known.</param>
public sealed record PortDevice(
    string DeviceId,
    string? PortName,
    string FriendlyName,
    string? ParentDeviceId = null,
    string? LocationPath = null);
