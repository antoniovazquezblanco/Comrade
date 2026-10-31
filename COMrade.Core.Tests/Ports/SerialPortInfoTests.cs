using COMrade.Core.Ports;

namespace COMrade.Core.Tests.Ports;

public class SerialPortInfoTests
{
    [Fact]
    public void Description_drops_the_port_name_windows_appends_to_the_friendly_name()
    {
        var port = SerialPortInfo.FromDevice(new PortDevice(@"USB\VID_1A86&PID_7523\5&2F3C1B2&0&3", "COM5", "USB-SERIAL CH340 (COM5)"));

        Assert.NotNull(port);
        Assert.Equal("COM5", port.PortName);
        Assert.Equal("USB-SERIAL CH340", port.Description);
        Assert.Equal(5, port.PortNumber);
    }

    [Fact]
    public void Description_is_kept_when_the_friendly_name_has_no_port_suffix()
    {
        var port = SerialPortInfo.FromDevice(new PortDevice(@"ACPI\PNP0501\1", "COM1", "Communications Port"));

        Assert.Equal("Communications Port", port?.Description);
    }

    [Fact]
    public void Port_name_is_used_when_the_device_has_no_name()
    {
        var port = SerialPortInfo.FromDevice(new PortDevice(@"COM0COM\PORT\CNCA0", "com10", ""));

        Assert.Equal("COM10", port?.PortName);
        Assert.Equal("COM10", port?.Description);
    }

    [Theory]
    [InlineData("LPT1")]
    [InlineData("COM")]
    [InlineData("COMX")]
    [InlineData(null)]
    public void Devices_that_are_not_com_ports_are_ignored(string? portName)
    {
        Assert.Null(SerialPortInfo.FromDevice(new PortDevice(@"ACPI\PNP0401\1", portName, "Printer Port")));
    }

    [Theory]
    [InlineData(@"USB\VID_1A86&PID_7523\5&2F3C1B2&0&3", PortKind.Usb)]
    [InlineData(@"FTDIBUS\VID_0403+PID_6001+A50285BIA\0000", PortKind.Usb)]
    [InlineData(@"BTHENUM\{00001101-0000-1000-8000-00805F9B34FB}_LOCALMFG&0002\7&2EDB73B5&0&24B231F4C893_C00000000", PortKind.Bluetooth)]
    [InlineData(@"ACPI\PNP0501\1", PortKind.BuiltIn)]
    [InlineData(@"COM0COM\PORT\CNCA0", PortKind.Virtual)]
    [InlineData(@"ROOT\PORTS\0000", PortKind.Virtual)]
    public void Kind_is_recognised_from_the_device(string deviceId, PortKind expected)
    {
        Assert.Equal(expected, SerialPortInfo.FromDevice(new PortDevice(deviceId, "COM7", "Port (COM7)"))?.Kind);
    }

    [Fact]
    public void Composite_usb_boards_are_usb()
    {
        // ESP32-C3 native USB: the port is one interface of a composite device.
        var port = SerialPortInfo.FromDevice(new PortDevice(
            @"USB\VID_303A&PID_1001&MI_00\7&2DE0FCAF&0&0000", "COM10", "USB Serial Device (COM10)",
            ParentDeviceId: @"USB\VID_303A&PID_1001\58:CF:79:A3:5E:1C"));

        Assert.Equal(PortKind.Usb, port?.Kind);
    }

    [Theory]
    [InlineData("PL2303HXA PHASED OUT SINCE 2012. PLEASE CONTACT YOUR SUPPLIER. (COM25)")]
    [InlineData("THIS IS NOT PROLIFIC PL2303. PLEASE CONTACT YOUR SUPPLIER. (COM25)")]
    [InlineData("PL2303TA DO NOT SUPPORT WINDOWS 11 OR LATER, PLEASE CONTACT YOUR SUPPLIER. (COM25)")]
    public void Prolific_adapters_renamed_by_their_driver_are_refused(string friendlyName)
    {
        var port = SerialPortInfo.FromDevice(new PortDevice(@"USB\VID_067B&PID_2303\7&3B613358&0&1", "COM25", friendlyName));

        Assert.True(port?.IsRefusedByDriver);
    }

    [Fact]
    public void Working_prolific_adapters_and_other_devices_are_not_refused()
    {
        Assert.False(SerialPortInfo.FromDevice(new PortDevice(@"USB\VID_067B&PID_2303\7&3B613358&0&1", "COM26", "Prolific USB-to-Serial Comm Port (COM26)"))?.IsRefusedByDriver);
        Assert.False(SerialPortInfo.FromDevice(new PortDevice(@"USB\VID_1A86&PID_7523\5&2F3C1B2&0&3", "COM5", "PLEASE CONTACT YOUR SUPPLIER (COM5)"))?.IsRefusedByDriver);
    }
}
