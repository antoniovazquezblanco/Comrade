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
