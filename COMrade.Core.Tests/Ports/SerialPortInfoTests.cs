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
}
