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
/// only to tell adapter models apart in <see cref="IsSameModelWithoutSerial"/>.
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

    /// <summary>
    /// True when both are the same USB adapter model and neither has a serial number of its own (none
    /// at all, like the CH340, or the same placeholder shared by every unit): they could be one device
    /// that moved to another USB socket.
    /// </summary>
    public bool IsSameModelWithoutSerial(DeviceIdentity other) =>
        IsUsb
        && SerialNumber is null
        && other.SerialNumber is null
        && VendorId == other.VendorId
        && ProductId == other.ProductId
        && string.Equals(PlaceholderSerial, other.PlaceholderSerial, StringComparison.OrdinalIgnoreCase);
}
