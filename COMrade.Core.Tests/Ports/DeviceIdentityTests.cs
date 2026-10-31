using COMrade.Core.Ports;

namespace COMrade.Core.Tests.Ports;

public class DeviceIdentityTests
{
    [Fact]
    public void Same_serial_number_under_a_different_com_number_is_the_same_device()
    {
        var before = new DeviceIdentity("COM5", 0x0403, 0x6001, "A50285BIA");
        var after = new DeviceIdentity("COM6", 0x0403, 0x6001, "a50285bia");

        Assert.True(before.IsSameDeviceAs(after));
    }

    [Fact]
    public void Adapter_without_serial_number_in_the_same_usb_socket_is_the_same_device()
    {
        var before = new DeviceIdentity("COM5", 0x1A86, 0x7523, Location: "USB(3)");
        var after = new DeviceIdentity("COM6", 0x1A86, 0x7523, Location: "USB(3)");

        Assert.True(before.IsSameDeviceAs(after));
    }

    [Fact]
    public void Identical_adapters_without_serial_number_in_different_sockets_are_different_devices()
    {
        var first = new DeviceIdentity("COM5", 0x1A86, 0x7523, Location: "USB(3)");
        var second = new DeviceIdentity("COM6", 0x1A86, 0x7523, Location: "USB(4)");

        Assert.False(first.IsSameDeviceAs(second));
    }

    [Fact]
    public void Same_serial_number_from_a_different_vendor_is_a_different_device()
    {
        var first = new DeviceIdentity("COM5", 0x10C4, 0xEA60, "0001");
        var second = new DeviceIdentity("COM5", 0x067B, 0x2303, "0001");

        Assert.False(first.IsSameDeviceAs(second));
    }

    [Fact]
    public void Non_usb_ports_match_by_port_name()
    {
        Assert.True(new DeviceIdentity("COM1").IsSameDeviceAs(new DeviceIdentity("com1")));
        Assert.False(new DeviceIdentity("COM1").IsSameDeviceAs(new DeviceIdentity("COM2")));
    }

    [Fact]
    public void Same_model_with_the_same_placeholder_serial_could_be_one_device()
    {
        var before = new DeviceIdentity("COM19", 0x10C4, 0xEA60, Location: "USB(1)", PlaceholderSerial: "0001");
        var after = new DeviceIdentity("COM19", 0x10C4, 0xEA60, Location: "USB(4)", PlaceholderSerial: "0001");

        Assert.False(before.IsSameDeviceAs(after));
        Assert.True(before.IsSameModelWithoutSerial(after));
    }

    [Fact]
    public void Same_model_without_any_serial_could_be_one_device()
    {
        var before = new DeviceIdentity("COM24", 0x1A86, 0x7523, Location: "USB(6)");
        var after = new DeviceIdentity("COM13", 0x1A86, 0x7523, Location: "USB(2)");

        Assert.False(before.IsSameDeviceAs(after));
        Assert.True(before.IsSameModelWithoutSerial(after));
    }

    [Fact]
    public void Adapters_with_their_own_serial_are_never_taken_for_another()
    {
        var ftdi = new DeviceIdentity("COM3", 0x0403, 0x6001, "A50285BIA", "USB(1)");

        Assert.False(ftdi.IsSameModelWithoutSerial(ftdi with { SerialNumber = "A50285BJB", Location = "USB(2)" }));
        Assert.False(ftdi.IsSameModelWithoutSerial(ftdi with { SerialNumber = null }));
        Assert.False(new DeviceIdentity("COM1").IsSameModelWithoutSerial(new DeviceIdentity("COM2")));
    }

    [Fact]
    public void Moved_socket_match_needs_the_same_model_and_placeholder()
    {
        var cp210x = new DeviceIdentity("COM19", 0x10C4, 0xEA60, Location: "USB(1)", PlaceholderSerial: "0001");

        Assert.False(cp210x.IsSameModelWithoutSerial(cp210x with { ProductId = 0xEA70 }));
        Assert.False(cp210x.IsSameModelWithoutSerial(cp210x with { PlaceholderSerial = "0000" }));
        Assert.False(cp210x.IsSameModelWithoutSerial(cp210x with { PlaceholderSerial = null }));
    }

    [Fact]
    public void Usb_device_never_matches_a_non_usb_port_with_the_same_name()
    {
        var usb = new DeviceIdentity("COM5", 0x1A86, 0x7523, Location: "USB(3)");

        Assert.False(usb.IsSameDeviceAs(new DeviceIdentity("COM5")));
    }
}
