using System.Globalization;

namespace COMrade.Core.Ports;

/// <summary>A COM port available on this machine.</summary>
/// <param name="DeviceId">Windows device instance ID; stable while the device stays plugged in.</param>
/// <param name="PortName">COM port name, e.g. <c>COM5</c>.</param>
/// <param name="Description">Device name without the port suffix, e.g. <c>USB-SERIAL CH340</c>.</param>
/// <param name="Identity">How the physical device is recognised across replugs.</param>
public sealed record SerialPortInfo(
    string DeviceId,
    string PortName,
    string Description,
    DeviceIdentity Identity)
{
    private const string ComPrefix = "COM";
    private const ushort ProlificVendorId = 0x067B;

    // Prolific's driver renames chips it refuses: "PL2303HXA PHASED OUT SINCE 2012. PLEASE CONTACT
    // YOUR SUPPLIER.", "THIS IS NOT PROLIFIC PL2303. PLEASE CONTACT YOUR SUPPLIER.", and so on.
    private const string RefusalText = "CONTACT YOUR SUPPLIER";

    /// <summary>USB, built-in, virtual or Bluetooth; decides where the port is listed.</summary>
    public PortKind Kind
    {
        get
        {
            if (Identity.IsUsb)
                return PortKind.Usb;
            if (DeviceId.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase))
                return PortKind.Bluetooth;
            if (DeviceId.StartsWith(@"ACPI\", StringComparison.OrdinalIgnoreCase))
                return PortKind.BuiltIn;
            return PortKind.Virtual;
        }
    }

    /// <summary>
    /// True for an old or counterfeit Prolific PL2303 adapter that Prolific's current driver refuses:
    /// Windows lists it as a working port, but it cannot be used until an older driver is installed.
    /// </summary>
    public bool IsRefusedByDriver =>
        Identity.VendorId == ProlificVendorId && Description.Contains(RefusalText, StringComparison.OrdinalIgnoreCase);

    /// <summary>Number of the COM port (5 for COM5), used to sort ports naturally.</summary>
    public int PortNumber => int.Parse(PortName.AsSpan(ComPrefix.Length), CultureInfo.InvariantCulture);

    /// <summary>Creates the port for a device, or null when the device is not a COM port (e.g. LPT1).</summary>
    public static SerialPortInfo? FromDevice(PortDevice device)
    {
        if (!IsComPortName(device.PortName))
            return null;

        var portName = device.PortName.ToUpperInvariant();
        var identity = DeviceIdentityParser.Parse(portName, device.DeviceId, device.ParentDeviceId, device.LocationPath);
        return new SerialPortInfo(device.DeviceId, portName, Describe(device.FriendlyName, portName), identity);
    }

    private static bool IsComPortName([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? name) =>
        name is not null
        && name.Length > ComPrefix.Length
        && name.StartsWith(ComPrefix, StringComparison.OrdinalIgnoreCase)
        && name.AsSpan(ComPrefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;

    private static string Describe(string friendlyName, string portName)
    {
        var suffix = $" ({portName})";
        var description = friendlyName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? friendlyName[..^suffix.Length]
            : friendlyName;
        return string.IsNullOrWhiteSpace(description) ? portName : description.Trim();
    }
}
