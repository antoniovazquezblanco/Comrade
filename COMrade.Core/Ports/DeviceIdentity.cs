namespace COMrade.Core.Ports;

/// <summary>
/// What makes a physical serial device recognisable after it is unplugged and plugged back in,
/// even when Windows gives it a different COM number.
/// </summary>
/// <remarks>
/// Preference order: USB vendor/product ID with serial number, then USB vendor/product ID with
/// the USB socket location (for adapters without a unique serial number), then the COM port name.
/// </remarks>
/// <param name="PlaceholderSerial">
/// Serial number shared by every unit of the adapter (e.g. CP210x "0001"); not used in <see cref="Key"/>,
/// only to recognise the adapter after it moves to another USB socket.
/// </param>
public sealed record DeviceIdentity(
    string PortName,
    ushort? VendorId = null,
    ushort? ProductId = null,
    string? SerialNumber = null,
    string? Location = null,
    string? PlaceholderSerial = null)
{
    public bool IsUsb => VendorId is not null && ProductId is not null;

    /// <summary>Stable text key; two identities with the same key belong to the same device.</summary>
    public string Key
    {
        get
        {
            if (!IsUsb)
                return $"port:{PortName}";

            var usb = $"usb:{VendorId!.Value:X4}:{ProductId!.Value:X4}";
            if (SerialNumber is not null)
                return $"{usb}:sn:{SerialNumber}";
            if (Location is not null)
                return $"{usb}:loc:{Location}";
            return $"{usb}:port:{PortName}";
        }
    }

    public bool IsSameDeviceAs(DeviceIdentity other) =>
        string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when both are the same adapter model reporting the same placeholder serial number.</summary>
    public bool IsPlaceholderMatch(DeviceIdentity other) =>
        PlaceholderSerial is not null
        && VendorId == other.VendorId
        && ProductId == other.ProductId
        && string.Equals(PlaceholderSerial, other.PlaceholderSerial, StringComparison.OrdinalIgnoreCase);
}
