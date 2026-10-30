using COMrade.Core.Ports;
using COMrade.Core.Serial;
using COMrade.Core.Sessions;
using COMrade.Core.Tests.Fakes;

namespace COMrade.Core.Tests.Sessions;

public class PortSessionsTests
{
    private readonly PortList _ports = new();
    private readonly FakeSerialDriver _driver = new();
    private readonly PortSessions _sessions;
    private readonly SerialPortInfo _ch340;
    private readonly SerialPortInfo _ftdi;

    public PortSessionsTests()
    {
        _ports.AddOrUpdate(new PortDevice(@"USB\VID_1A86&PID_7523\5&2F3C1B2&0&3", "COM5", "USB-SERIAL CH340 (COM5)", LocationPath: "USB(3)"));
        _ports.AddOrUpdate(new PortDevice(@"FTDIBUS\VID_0403+PID_6001+A50285BIA\0000", "COM3", "USB Serial Port (COM3)"));
        _ftdi = _ports.Ports[0];
        _ch340 = _ports.Ports[1];
        _sessions = new PortSessions(_ports, _driver.OpenAsync);
    }

    [Fact]
    public void Selecting_a_device_that_already_has_a_tab_returns_that_tab()
    {
        var first = _sessions.GetOrCreate(_ch340, SerialSettings.Default);
        var second = _sessions.GetOrCreate(_ch340, SerialSettings.Default with { BaudRate = 9600 });

        Assert.Same(first, second);
        Assert.Single(_sessions.Sessions);
    }

    [Fact]
    public void Each_device_gets_its_own_tab()
    {
        var ch340 = _sessions.GetOrCreate(_ch340, SerialSettings.Default);
        var ftdi = _sessions.GetOrCreate(_ftdi, SerialSettings.Default);

        Assert.NotSame(ch340, ftdi);
        Assert.Equal(2, _sessions.Sessions.Count);
    }

    [Fact]
    public async Task Closing_a_tab_closes_its_port_and_forgets_it()
    {
        var session = _sessions.GetOrCreate(_ch340, SerialSettings.Default);
        await session.OpenAsync();

        await _sessions.CloseAsync(session);

        Assert.Empty(_sessions.Sessions);
        Assert.True(_driver.Current.IsDisposed);
        Assert.Equal(PortSessionState.Closed, session.State);
    }
}
