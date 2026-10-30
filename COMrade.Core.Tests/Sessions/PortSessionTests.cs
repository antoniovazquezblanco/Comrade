using COMrade.Core.Ports;
using COMrade.Core.Serial;
using COMrade.Core.Sessions;
using COMrade.Core.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace COMrade.Core.Tests.Sessions;

public class PortSessionTests : IAsyncDisposable
{
    private const string Ch340Id = @"USB\VID_1A86&PID_7523\5&2F3C1B2&0&3";
    private const string Cp210xId = @"USB\VID_10C4&PID_EA60\0001";

    private readonly PortList _ports = new();
    private readonly FakeSerialDriver _driver = new();
    private readonly FakeTimeProvider _time = new();
    private readonly PortSession _session;

    public PortSessionTests()
    {
        _ports.AddOrUpdate(Ch340("COM5"));
        _session = CreateSession(Ch340Id);
    }

    public async ValueTask DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public void New_session_starts_closed_with_the_device_present()
    {
        Assert.Equal(PortSessionState.Closed, _session.State);
        Assert.True(_session.IsDevicePresent);
        Assert.Empty(_driver.Connections);
    }

    [Fact]
    public async Task Open_opens_the_selected_port_with_the_session_settings()
    {
        await _session.OpenAsync();

        Assert.Equal(PortSessionState.Open, _session.State);
        Assert.Equal("COM5", _driver.Current.PortName);
        Assert.Equal(SerialSettings.Default, _driver.Current.Settings);
        Assert.Equal(_time.GetUtcNow(), _session.OpenedAt);
    }

    [Fact]
    public async Task Opening_an_open_session_again_does_nothing()
    {
        await _session.OpenAsync();
        await _session.OpenAsync();

        Assert.Single(_driver.Connections);
    }

    [Fact]
    public async Task Received_bytes_are_reported_and_counted()
    {
        var received = new List<byte>();
        _session.DataReceived += (_, data) => { lock (received) received.AddRange(data); };
        await _session.OpenAsync();

        _driver.Current.Receive(0x55, 0x2D);
        _driver.Current.Receive(0x42);
        await WaitForAsync(() => _session.BytesReceived == 3);

        lock (received)
            Assert.Equal([0x55, 0x2D, 0x42], received);
    }

    [Fact]
    public async Task Sent_bytes_reach_the_port_and_are_counted()
    {
        await _session.OpenAsync();

        Assert.True(await _session.SendAsync("ls\r"u8.ToArray()));

        Assert.Equal("ls\r"u8.ToArray(), _driver.Current.Written);
        Assert.Equal(3, _session.BytesSent);
    }

    [Fact]
    public async Task Sending_while_closed_does_nothing()
    {
        Assert.False(await _session.SendAsync("ls\r"u8.ToArray()));
        Assert.Equal(0, _session.BytesSent);
    }

    [Fact]
    public async Task Close_closes_the_port()
    {
        await _session.OpenAsync();

        await _session.CloseAsync();

        Assert.Equal(PortSessionState.Closed, _session.State);
        Assert.True(_driver.Current.IsDisposed);
        Assert.Null(_session.OpenedAt);
    }

    [Fact]
    public async Task Port_in_use_is_an_error_and_try_again_succeeds()
    {
        _driver.FailNextOpen(SerialPortError.InUse);

        await _session.OpenAsync();
        Assert.Equal(PortSessionState.Error, _session.State);
        Assert.Equal(SerialPortError.InUse, _session.LastError);

        await _session.OpenAsync();
        Assert.Equal(PortSessionState.Open, _session.State);
        Assert.Null(_session.LastError);
    }

    [Fact]
    public async Task Unplug_while_open_waits_and_replug_reopens()
    {
        await _session.OpenAsync();
        var first = _driver.Current;

        await UnplugAsync(Ch340Id);
        Assert.Equal(PortSessionState.Waiting, _session.State);
        Assert.Equal(SerialPortError.DeviceRemoved, _session.LastError);
        Assert.False(_session.IsDevicePresent);
        Assert.True(first.IsDisposed);

        await PlugAsync(Ch340("COM5"));
        Assert.Equal(PortSessionState.Open, _session.State);
        Assert.Equal(2, _driver.Connections.Count);

        _driver.Current.Receive(0x41);
        await WaitForAsync(() => _session.BytesReceived == 1);
    }

    [Fact]
    public async Task Read_failure_from_device_removal_waits_for_the_device()
    {
        await _session.OpenAsync();

        _driver.Current.Fail(SerialPortError.DeviceRemoved);

        await WaitForAsync(() => _session.State == PortSessionState.Waiting);
        Assert.Equal(SerialPortError.DeviceRemoved, _session.LastError);
    }

    [Fact]
    public async Task Io_failure_is_an_error_until_the_device_turns_out_to_be_unplugged()
    {
        await _session.OpenAsync();

        _driver.Current.Fail(SerialPortError.IoFailure);
        await WaitForAsync(() => _session.State == PortSessionState.Error);
        Assert.Equal(SerialPortError.IoFailure, _session.LastError);

        await UnplugAsync(Ch340Id);
        Assert.Equal(PortSessionState.Waiting, _session.State);

        await PlugAsync(Ch340("COM5"));
        Assert.Equal(PortSessionState.Open, _session.State);
    }

    [Fact]
    public async Task Failed_write_is_handled_like_a_lost_device()
    {
        await _session.OpenAsync();
        _driver.Current.FailWrites = true;

        Assert.False(await _session.SendAsync("x"u8.ToArray()));

        Assert.Equal(PortSessionState.Waiting, _session.State);
    }

    [Fact]
    public async Task Without_automatic_reopen_unplug_is_an_error_and_replug_does_not_reopen()
    {
        _session.ReopenAutomatically = false;
        await _session.OpenAsync();

        await UnplugAsync(Ch340Id);
        Assert.Equal(PortSessionState.Error, _session.State);
        Assert.Equal(SerialPortError.DeviceRemoved, _session.LastError);

        await PlugAsync(Ch340("COM5"));
        Assert.Equal(PortSessionState.Error, _session.State);
        Assert.Single(_driver.Connections);
    }

    [Fact]
    public async Task Device_returning_under_a_new_com_number_is_reopened_there()
    {
        var portChanged = 0;
        _session.PortChanged += (_, _) => portChanged++;
        await _session.OpenAsync();

        await UnplugAsync(Ch340Id);
        await PlugAsync(Ch340("COM6"));

        Assert.Equal(PortSessionState.Open, _session.State);
        Assert.Equal("COM6", _session.Port.PortName);
        Assert.Equal("COM6", _driver.Current.PortName);
        Assert.Equal(1, portChanged);
    }

    [Fact]
    public async Task Device_that_never_returns_keeps_waiting()
    {
        await _session.OpenAsync();
        await UnplugAsync(Ch340Id);

        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(PortSessionState.Waiting, _session.State);
        Assert.Single(_driver.Connections);
    }

    [Fact]
    public async Task Close_while_waiting_stops_reopening()
    {
        await _session.OpenAsync();
        await UnplugAsync(Ch340Id);

        await _session.CloseAsync();
        await PlugAsync(Ch340("COM5"));

        Assert.Equal(PortSessionState.Closed, _session.State);
        Assert.Single(_driver.Connections);
    }

    [Fact]
    public async Task Unplug_while_opening_waits_for_the_device()
    {
        _driver.FailNextOpen(SerialPortError.DeviceRemoved);

        await _session.OpenAsync();

        Assert.Equal(PortSessionState.Waiting, _session.State);
    }

    [Fact]
    public async Task Opening_a_missing_device_without_automatic_reopen_is_an_error()
    {
        _session.ReopenAutomatically = false;
        _driver.FailNextOpen(SerialPortError.NotFound);

        await _session.OpenAsync();

        Assert.Equal(PortSessionState.Error, _session.State);
        Assert.Equal(SerialPortError.NotFound, _session.LastError);
    }

    [Fact]
    public async Task Reconnect_now_while_the_device_is_still_missing_keeps_waiting()
    {
        await _session.OpenAsync();
        await UnplugAsync(Ch340Id);
        _driver.FailNextOpen(SerialPortError.NotFound);

        await _session.OpenAsync();

        Assert.Equal(PortSessionState.Waiting, _session.State);
    }

    [Fact]
    public async Task Reopen_gives_the_driver_a_moment_before_the_first_attempt()
    {
        await _session.OpenAsync();
        await UnplugAsync(Ch340Id);

        _ports.AddOrUpdate(Ch340("COM5"));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(PortSessionState.Waiting, _session.State);
        Assert.Single(_driver.Connections);

        _time.Advance(PortSession.ReopenDelays[0]);
        await WaitForAsync(() => _session.State == PortSessionState.Open);
    }

    [Fact]
    public async Task Reopen_retries_while_the_driver_is_still_starting()
    {
        await _session.OpenAsync();
        await UnplugAsync(Ch340Id);
        _driver.FailNextOpen(SerialPortError.AccessDenied, times: 2);

        _ports.AddOrUpdate(Ch340("COM5"));
        await AdvanceTimeUntilAsync(() => _session.State == PortSessionState.Open);

        Assert.Equal(2, _driver.Connections.Count);
    }

    [Fact]
    public async Task Reopen_gives_up_after_the_last_attempt()
    {
        await _session.OpenAsync();
        await UnplugAsync(Ch340Id);
        _driver.FailNextOpen(SerialPortError.InUse, times: PortSession.ReopenDelays.Length);

        _ports.AddOrUpdate(Ch340("COM5"));
        await AdvanceTimeUntilAsync(() => _session.State == PortSessionState.Error);

        Assert.Equal(SerialPortError.InUse, _session.LastError);
        Assert.Single(_driver.Connections);
    }

    [Fact]
    public async Task Settings_change_applies_to_the_open_port()
    {
        await _session.OpenAsync();
        var faster = SerialSettings.Default with { BaudRate = 921600 };

        Assert.True(await _session.ApplySettingsAsync(faster));

        Assert.Equal(faster, _session.Settings);
        Assert.Equal(faster, _driver.Current.Settings);
        Assert.Single(_driver.Connections);
    }

    [Fact]
    public async Task Rejected_settings_change_keeps_the_previous_settings()
    {
        await _session.OpenAsync();
        _driver.Current.RejectSettings = true;

        Assert.False(await _session.ApplySettingsAsync(SerialSettings.Default with { BaudRate = 12345 }));

        Assert.Equal(SerialSettings.Default, _session.Settings);
        Assert.Equal(SerialPortError.InvalidSettings, _session.LastError);
        Assert.Equal(PortSessionState.Open, _session.State);
    }

    [Fact]
    public async Task Settings_changed_while_closed_are_used_when_opening()
    {
        var settings = SerialSettings.Default with { BaudRate = 9600, Parity = Parity.Even };

        await _session.ApplySettingsAsync(settings);
        await _session.OpenAsync();

        Assert.Equal(settings, _driver.Current.Settings);
    }

    [Fact]
    public async Task Close_gives_up_on_a_driver_that_never_finishes_closing()
    {
        _driver.HangOnClose = true;
        await _session.OpenAsync();

        var close = _session.CloseAsync();
        Assert.False(close.IsCompleted);

        _time.Advance(PortSession.CloseTimeout);
        await close.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionState.Closed, _session.State);
    }

    [Fact]
    public async Task Other_devices_coming_and_going_are_ignored()
    {
        await _session.OpenAsync();
        const string ftdiId = @"FTDIBUS\VID_0403+PID_6001+A50285BIA\0000";

        await PlugAsync(new PortDevice(ftdiId, "COM3", "USB Serial Port (COM3)"));
        await UnplugAsync(ftdiId);

        Assert.Equal(PortSessionState.Open, _session.State);
        Assert.Single(_driver.Connections);
    }

    [Fact]
    public async Task Placeholder_serial_adapter_moved_to_another_socket_is_reopened()
    {
        _ports.AddOrUpdate(Cp210x(Cp210xId, "COM19", "USB(1)"));
        await using var session = CreateSession(Cp210xId);
        await session.OpenAsync();

        await UnplugAsync(Cp210xId, session);
        await PlugAsync(Cp210x(Cp210xId, "COM19", "USB(4)"), session);

        Assert.Equal(PortSessionState.Open, session.State);
        Assert.Equal("USB(4)", session.Port.Identity.Location);
    }

    [Fact]
    public async Task Placeholder_fallback_does_not_guess_between_two_adapters()
    {
        _ports.AddOrUpdate(Cp210x(Cp210xId, "COM19", "USB(1)"));
        _ports.AddOrUpdate(new PortDevice(
            @"USB\VID_10C4&PID_EA60&MI_00\7&1A2B3C&0&0000",
            "COM22",
            "Silicon Labs CP210x USB to UART Bridge (COM22)",
            ParentDeviceId: Cp210xId,
            LocationPath: "USB(5)"));
        await using var session = CreateSession(Cp210xId);
        await session.OpenAsync();

        await UnplugAsync(Cp210xId, session);
        await PlugAsync(Cp210x(Cp210xId, "COM19", "USB(4)"), session);

        Assert.Equal(PortSessionState.Waiting, session.State);
    }

    private PortSession CreateSession(string deviceId) =>
        new(_ports.Ports.Single(port => port.DeviceId == deviceId), SerialSettings.Default, _ports, _driver.OpenAsync, _time);

    private async Task UnplugAsync(string deviceId, PortSession? session = null)
    {
        _ports.Remove(deviceId);
        await (session ?? _session).DeviceEventsHandled;
    }

    // Reopening waits on the fake clock, so time moves forward until the arrival is handled.
    private async Task PlugAsync(PortDevice device, PortSession? session = null)
    {
        _ports.AddOrUpdate(device);
        var handled = (session ?? _session).DeviceEventsHandled;
        await AdvanceTimeUntilAsync(() => handled.IsCompleted);
    }

    private static PortDevice Ch340(string portName) =>
        new(Ch340Id, portName, $"USB-SERIAL CH340 ({portName})", LocationPath: "USB(3)");

    private static PortDevice Cp210x(string deviceId, string portName, string location) =>
        new(deviceId, portName, $"Silicon Labs CP210x USB to UART Bridge ({portName})", LocationPath: location);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met within 5 seconds.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    // Reopen retries wait on the fake clock from a background task: keep moving time forward
    // until the session gets where the test expects.
    private async Task AdvanceTimeUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met within 5 seconds.");
            _time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
