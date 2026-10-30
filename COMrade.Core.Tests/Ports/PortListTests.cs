using COMrade.Core.Ports;

namespace COMrade.Core.Tests.Ports;

public class PortListTests
{
    private const string Ch340Id = @"USB\VID_1A86&PID_7523\5&2F3C1B2&0&3";
    private const string FtdiId = @"FTDIBUS\VID_0403+PID_6001+A50285BIA\0000";

    private readonly PortList _list = new();
    private readonly List<string> _events = [];

    public PortListTests()
    {
        _list.PortArrived += (_, port) => _events.Add($"+{port.PortName}");
        _list.PortRemoved += (_, port) => _events.Add($"-{port.PortName}");
        _list.EnumerationCompleted += (_, _) => _events.Add("enumerated");
    }

    [Fact]
    public void New_device_is_listed_and_announced()
    {
        _list.AddOrUpdate(Ch340("COM5"));

        Assert.Equal(["+COM5"], _events);
        var port = Assert.Single(_list.Ports);
        Assert.Equal("USB-SERIAL CH340", port.Description);
    }

    [Fact]
    public void Repeated_report_of_an_unchanged_device_is_ignored()
    {
        _list.AddOrUpdate(Ch340("COM5"));
        _list.AddOrUpdate(Ch340("COM5"));

        Assert.Equal(["+COM5"], _events);
        Assert.Single(_list.Ports);
    }

    [Fact]
    public void Removed_device_is_unlisted_and_announced()
    {
        _list.AddOrUpdate(Ch340("COM5"));
        _list.Remove(Ch340Id);

        Assert.Equal(["+COM5", "-COM5"], _events);
        Assert.Empty(_list.Ports);
    }

    [Fact]
    public void Removal_of_an_unknown_device_is_ignored()
    {
        _list.Remove(Ch340Id);

        Assert.Empty(_events);
    }

    [Fact]
    public void Com_number_change_is_reported_as_removal_then_arrival()
    {
        _list.AddOrUpdate(Ch340("COM5"));
        _list.AddOrUpdate(Ch340("COM6"));

        Assert.Equal(["+COM5", "-COM5", "+COM6"], _events);
        Assert.Equal("COM6", Assert.Single(_list.Ports).PortName);
    }

    [Fact]
    public void Device_that_stops_being_a_com_port_is_removed()
    {
        _list.AddOrUpdate(Ch340("COM5"));
        _list.AddOrUpdate(new PortDevice(Ch340Id, null, "USB-SERIAL CH340"));

        Assert.Equal(["+COM5", "-COM5"], _events);
        Assert.Empty(_list.Ports);
    }

    [Fact]
    public void Parallel_ports_are_not_listed()
    {
        _list.AddOrUpdate(new PortDevice(@"ACPI\PNP0401\1", "LPT1", "Printer Port (LPT1)"));

        Assert.Empty(_events);
        Assert.Empty(_list.Ports);
    }

    [Fact]
    public void Ports_are_sorted_by_com_number()
    {
        _list.AddOrUpdate(new PortDevice(@"ACPI\PNP0501\10", "COM10", "Communications Port (COM10)"));
        _list.AddOrUpdate(new PortDevice(@"ACPI\PNP0501\2", "COM2", "Communications Port (COM2)"));
        _list.AddOrUpdate(Ch340("COM3"));

        Assert.Equal(["COM2", "COM3", "COM10"], _list.Ports.Select(port => port.PortName));
    }

    [Fact]
    public void Rapid_unplug_and_replug_ends_with_the_device_listed_and_events_in_order()
    {
        _list.AddOrUpdate(Ch340("COM5"));
        _list.Remove(Ch340Id);
        _list.AddOrUpdate(Ch340("COM5"));
        _list.Remove(Ch340Id);
        _list.AddOrUpdate(Ch340("COM6"));

        Assert.Equal(["+COM5", "-COM5", "+COM5", "-COM5", "+COM6"], _events);
        Assert.Equal("COM6", Assert.Single(_list.Ports).PortName);
    }

    [Fact]
    public void Two_devices_are_tracked_independently()
    {
        _list.AddOrUpdate(Ch340("COM5"));
        _list.AddOrUpdate(new PortDevice(FtdiId, "COM3", "USB Serial Port (COM3)"));
        _list.Remove(Ch340Id);

        Assert.Equal("COM3", Assert.Single(_list.Ports).PortName);
    }

    [Fact]
    public async Task Concurrent_reports_leave_a_consistent_list()
    {
        var arrivals = 0;
        _list.PortArrived += (_, _) => Interlocked.Increment(ref arrivals);

        await Task.WhenAll(Enumerable.Range(1, 200).Select(n => Task.Run(() =>
            _list.AddOrUpdate(new PortDevice($@"ACPI\PNP0501\{n}", $"COM{n}", $"Communications Port (COM{n})")))));

        Assert.Equal(200, _list.Ports.Count);
        Assert.Equal(200, arrivals);
        Assert.Equal(Enumerable.Range(1, 200), _list.Ports.Select(port => port.PortNumber));
    }

    [Fact]
    public void Enumeration_completion_is_reported_once()
    {
        Assert.False(_list.IsEnumerationComplete);

        _list.CompleteEnumeration();
        _list.CompleteEnumeration();

        Assert.True(_list.IsEnumerationComplete);
        Assert.Equal(["enumerated"], _events);
    }

    private static PortDevice Ch340(string portName) =>
        new(Ch340Id, portName, $"USB-SERIAL CH340 ({portName})", LocationPath: "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)");
}
