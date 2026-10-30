using COMrade.Core.Ports;

namespace COMrade.Core.Tests.Ports;

public class DeviceIdentityParserTests
{
    private const string UsbLocation = "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)";

    [Fact]
    public void Ch340_without_serial_number_is_identified_by_usb_location()
    {
        var identity = DeviceIdentityParser.Parse("COM5", @"USB\VID_1A86&PID_7523\5&2F3C1B2&0&3", locationPath: UsbLocation);

        Assert.Equal((ushort)0x1A86, identity.VendorId);
        Assert.Equal((ushort)0x7523, identity.ProductId);
        Assert.Null(identity.SerialNumber);
        Assert.Equal($"usb:1A86:7523:loc:{UsbLocation}", identity.Key);
    }

    [Fact]
    public void Cp210x_reports_its_serial_number()
    {
        var identity = DeviceIdentityParser.Parse("COM7", @"USB\VID_10C4&PID_EA60\D6E0B5F8A1C2EB11", locationPath: UsbLocation);

        Assert.Equal("usb:10C4:EA60:sn:D6E0B5F8A1C2EB11", identity.Key);
    }

    [Theory]
    [InlineData(@"USB\VID_10C4&PID_EA60\0001")]
    [InlineData(@"USB\VID_10C4&PID_EA60\0000")]
    [InlineData(@"USB\VID_1A86&PID_55D4\00000000")]
    public void Placeholder_serial_numbers_fall_back_to_usb_location(string deviceId)
    {
        var identity = DeviceIdentityParser.Parse("COM19", deviceId, locationPath: UsbLocation);

        Assert.Null(identity.SerialNumber);
        Assert.Equal(deviceId.Split('\\')[2], identity.PlaceholderSerial);
        Assert.EndsWith($":loc:{UsbLocation}", identity.Key);
    }

    [Fact]
    public void Unique_serial_numbers_are_not_placeholders()
    {
        var identity = DeviceIdentityParser.Parse("COM7", @"USB\VID_10C4&PID_EA60\D6E0B5F8A1C2EB11");

        Assert.Null(identity.PlaceholderSerial);
    }

    [Fact]
    public void Ftdi_bus_ids_carry_vendor_product_and_serial_number()
    {
        var identity = DeviceIdentityParser.Parse("COM3", @"FTDIBUS\VID_0403+PID_6001+A50285BIA\0000");

        Assert.Equal((ushort)0x0403, identity.VendorId);
        Assert.Equal((ushort)0x6001, identity.ProductId);
        Assert.Equal("A50285BIA", identity.SerialNumber);
    }

    [Fact]
    public void Interface_of_a_composite_device_takes_the_serial_number_of_its_parent()
    {
        var identity = DeviceIdentityParser.Parse(
            "COM8",
            @"USB\VID_2E8A&PID_000A&MI_00\6&3A1C2B3&0&0000",
            parentDeviceId: @"USB\VID_2E8A&PID_000A\E66138935F4B2C28",
            locationPath: UsbLocation);

        Assert.Equal("usb:2E8A:000A:sn:E66138935F4B2C28", identity.Key);
    }

    [Fact]
    public void Interface_of_a_composite_device_without_known_parent_falls_back_to_location()
    {
        var identity = DeviceIdentityParser.Parse("COM8", @"USB\VID_2E8A&PID_000A&MI_00\6&3A1C2B3&0&0000", locationPath: UsbLocation);

        Assert.Null(identity.SerialNumber);
        Assert.Equal($"usb:2E8A:000A:loc:{UsbLocation}", identity.Key);
    }

    [Fact]
    public void Cdc_device_reports_its_serial_number()
    {
        var identity = DeviceIdentityParser.Parse("COM4", @"USB\VID_2341&PID_0043\85736323838351F0B0A1");

        Assert.Equal("usb:2341:0043:sn:85736323838351F0B0A1", identity.Key);
    }

    [Fact]
    public void Lowercase_ids_are_parsed()
    {
        var identity = DeviceIdentityParser.Parse("COM5", @"usb\vid_1a86&pid_7523\5&2f3c1b2&0&3");

        Assert.Equal((ushort)0x1A86, identity.VendorId);
        Assert.Equal((ushort)0x7523, identity.ProductId);
    }

    [Theory]
    [InlineData("COM9", @"BTHENUM\{00001101-0000-1000-8000-00805F9B34FB}_LOCALMFG&0002\7&1A2B3C&0&000000000000_00000000")]
    [InlineData("COM1", @"ACPI\PNP0501\1")]
    [InlineData("COM10", @"COM0COM\PORT\CNCA0")]
    public void Non_usb_ports_are_identified_by_port_name(string portName, string deviceId)
    {
        var identity = DeviceIdentityParser.Parse(portName, deviceId);

        Assert.False(identity.IsUsb);
        Assert.Equal($"port:{portName}", identity.Key);
    }

    [Fact]
    public void Usb_device_without_serial_number_or_location_falls_back_to_port_name()
    {
        var identity = DeviceIdentityParser.Parse("COM5", @"USB\VID_1A86&PID_7523\5&2F3C1B2&0&3");

        Assert.Equal("usb:1A86:7523:port:COM5", identity.Key);
    }
}
