using System.Text.RegularExpressions;

namespace COMrade.Core.Ports;

/// <summary>Builds a <see cref="DeviceIdentity"/> from the IDs Windows reports for a port device.</summary>
public static partial class DeviceIdentityParser
{
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;

    public static DeviceIdentity Parse(
        string portName,
        string deviceId,
        string? parentDeviceId = null,
        string? locationPath = null)
    {
        // FTDI's own bus driver: FTDIBUS\VID_0403+PID_6001+A50285BIA\0000
        if (deviceId.StartsWith(@"FTDIBUS\", IgnoreCase))
        {
            var hardware = Segment(deviceId, 1);
            var parts = hardware.Split('+', 3);
            if (parts.Length == 3 && TryParseVidPid(hardware, out var ftdiVid, out var ftdiPid))
                return new DeviceIdentity(portName, ftdiVid, ftdiPid, UniqueSerialOrNull(NullIfEmpty(parts[2])), locationPath);
        }

        // Each interface of a composite USB device (MI_xx) has a generated ID;
        // the serial number belongs to the parent USB device.
        var usbId = IsCompositeInterface(deviceId) && parentDeviceId is not null && IsUsbDevice(parentDeviceId)
            ? parentDeviceId
            : deviceId;

        if (!IsUsbDevice(usbId) || !TryParseVidPid(Segment(usbId, 1), out var vid, out var pid))
            return new DeviceIdentity(portName);

        // Windows generates an instance ID containing '&' when the device has no serial number.
        var instance = Segment(usbId, 2);
        var serial = instance.Length == 0 || instance.Contains('&') ? null : instance;

        return new DeviceIdentity(portName, vid, pid, UniqueSerialOrNull(serial), locationPath);
    }

    // Some adapters ship with a placeholder serial number shared by every unit (Silicon Labs
    // CP210x default "0001", all-zero serials); those cannot tell two adapters apart.
    private static string? UniqueSerialOrNull(string? serial) =>
        serial is null || serial == "0001" || serial.AsSpan().IndexOfAnyExcept('0') < 0 ? null : serial;

    private static bool IsUsbDevice(string deviceId) => deviceId.StartsWith(@"USB\", IgnoreCase);

    private static bool IsCompositeInterface(string deviceId) => Segment(deviceId, 1).Contains("&MI_", IgnoreCase);

    private static bool TryParseVidPid(string hardwareId, out ushort vendorId, out ushort productId)
    {
        var match = VidPidPattern().Match(hardwareId);
        if (!match.Success)
        {
            vendorId = productId = 0;
            return false;
        }

        vendorId = Convert.ToUInt16(match.Groups["vid"].Value, 16);
        productId = Convert.ToUInt16(match.Groups["pid"].Value, 16);
        return true;
    }

    private static string Segment(string deviceId, int index)
    {
        var segments = deviceId.Split('\\');
        return index < segments.Length ? segments[index] : string.Empty;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    [GeneratedRegex(@"VID_(?<vid>[0-9A-F]{4})[&+]PID_(?<pid>[0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPidPattern();
}
