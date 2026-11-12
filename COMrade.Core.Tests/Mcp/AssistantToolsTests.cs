using System.Text;
using COMrade.Core.Mcp;
using COMrade.Core.Ports;
using COMrade.Core.Serial;
using COMrade.Core.Sessions;
using COMrade.Core.Settings;
using COMrade.Core.Tests.Fakes;
using ModelContextProtocol;

namespace COMrade.Core.Tests.Mcp;

public sealed class AssistantToolsTests : IAsyncDisposable
{
    private const string Ch340Id = @"USB\VID_1A86&PID_7523\5&2F3C1B2&0&3";
    private const string FtdiId = @"FTDIBUS\VID_0403+PID_6001+A50285BIA\0000";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"comrade-tests-{Guid.NewGuid():N}");
    private readonly PortList _ports = new();
    private readonly FakeSerialDriver _driver = new();
    private readonly AppSettingsStore _settings;
    private readonly DeviceSettingsStore _devices;
    private readonly FakeAssistantHost _host;
    private readonly AssistantActivity _activity = new(TimeProvider.System);
    private readonly AssistantTools _tools;

    public AssistantToolsTests()
    {
        _ports.AddOrUpdate(new PortDevice(Ch340Id, "COM5", "USB-SERIAL CH340 (COM5)", LocationPath: "USB(3)"));
        _ports.AddOrUpdate(new PortDevice(FtdiId, "COM3", "USB Serial Port (COM3)"));
        _settings = new AppSettingsStore(Path.Combine(_folder, "settings.json"));
        _devices = new DeviceSettingsStore(Path.Combine(_folder, "devices.json"));
        SetAccess(AiAccess.AllPorts);
        _host = new FakeAssistantHost(_ports, _driver, _devices, _settings);
        _tools = new AssistantTools(_host.Host, _ports, _devices, _settings, _activity, TimeProvider.System);
    }

    private SerialPortInfo Ch340 => _ports.Ports.Single(port => port.PortName == "COM5");

    private SerialPortInfo Ftdi => _ports.Ports.Single(port => port.PortName == "COM3");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    // Access.

    [Fact]
    public async Task Nothing_is_allowed_while_ai_access_is_off()
    {
        SetAccess(AiAccess.Off);
        await OpenTabAsync(Ch340);

        var list = await _tools.ListPortsAsync();
        var error = await Assert.ThrowsAsync<McpException>(() => _tools.ReadAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation));

        Assert.Equal(AiAccess.Off, list.Access);
        Assert.All(list.Ports, port => Assert.False(port.Allowed));
        Assert.Contains("AI access is off", error.Message);
    }

    [Fact]
    public async Task Per_tab_only_devices_with_mcp_on_can_be_used()
    {
        SetAccess(AiAccess.PerTab);
        await OpenTabAsync(Ch340, WithMcp());
        await OpenTabAsync(Ftdi);

        var list = await _tools.ListPortsAsync();
        var status = await _tools.PortStatusAsync("COM5");
        var error = await Assert.ThrowsAsync<McpException>(() => _tools.PortStatusAsync("COM3"));

        Assert.True(list.Ports.Single(port => port.Port == "COM5").Allowed);
        Assert.False(list.Ports.Single(port => port.Port == "COM3").Allowed);
        Assert.Equal(PortSessionState.Open, status.State);
        Assert.Contains("turn on MCP in its tab's toolbar", error.Message);
    }

    [Fact]
    public async Task Per_tab_a_device_given_mcp_before_can_be_opened()
    {
        SetAccess(AiAccess.PerTab);
        _devices.Save(Ch340.Identity, WithMcp(), DateTimeOffset.UtcNow);

        var status = await _tools.OpenPortAsync("COM5");

        Assert.Equal(PortSessionState.Open, status.State);
        Assert.Single(_host.Tabs);
    }

    [Fact]
    public async Task Per_tab_a_device_never_given_mcp_cannot_be_opened()
    {
        SetAccess(AiAccess.PerTab);

        var error = await Assert.ThrowsAsync<McpException>(() => _tools.OpenPortAsync("COM5"));

        Assert.Contains("open it in COMrade and turn on MCP", error.Message);
        Assert.Empty(_host.Tabs);
    }

    [Fact]
    public async Task Turning_access_off_stops_an_assistant_at_its_next_call()
    {
        await OpenTabAsync(Ch340);
        await _tools.PortStatusAsync("COM5");

        SetAccess(AiAccess.Off);

        await Assert.ThrowsAsync<McpException>(() => _tools.PortStatusAsync("COM5"));
    }

    // Finding ports.

    [Fact]
    public async Task List_ports_shows_the_tabs_and_the_ports_without_one()
    {
        await OpenTabAsync(Ch340, DeviceSettings.Default with { Name = "Router console" });

        var ports = (await _tools.ListPortsAsync()).Ports;

        var router = ports.Single(port => port.Port == "COM5");
        var ftdi = ports.Single(port => port.Port == "COM3");
        Assert.Equal(2, ports.Count);
        Assert.Equal(("Router console", "USB-SERIAL CH340", "USB 1A86:7523"), (router.Name, router.Description, router.Kind));
        Assert.True(router.HasTab);
        Assert.Equal(PortSessionState.Open, router.State);
        Assert.Equal("115200 8N1, Enter sends CR", router.Settings);
        Assert.False(ftdi.HasTab);
        Assert.Null(ftdi.State);
        Assert.True(ftdi.Allowed);
    }

    [Fact]
    public async Task A_tab_is_found_by_its_name_whatever_the_case()
    {
        await OpenTabAsync(Ch340, DeviceSettings.Default with { Name = "Router console" });

        var status = await _tools.PortStatusAsync(" router CONSOLE ");

        Assert.Equal("COM5", status.Port);
    }

    [Fact]
    public async Task An_unknown_port_is_reported()
    {
        var error = await Assert.ThrowsAsync<McpException>(() => _tools.PortStatusAsync("COM42"));

        Assert.Contains("no port or tab named \"COM42\"", error.Message);
    }

    [Fact]
    public async Task A_port_without_a_tab_must_be_opened_first()
    {
        var error = await Assert.ThrowsAsync<McpException>(() => _tools.ReadAsync("COM3", timeoutMs: 0, cancellationToken: Cancellation));

        Assert.Contains("Call open_port first", error.Message);
    }

    // Opening and closing.

    [Fact]
    public async Task Open_port_opens_a_tab_with_the_settings_remembered_for_the_device()
    {
        var remembered = DeviceSettings.Default with { Serial = SerialSettings.Default with { BaudRate = 9600 } };
        _devices.Save(Ch340.Identity, remembered, DateTimeOffset.UtcNow);

        var status = await _tools.OpenPortAsync("COM5");

        Assert.Equal(PortSessionState.Open, status.State);
        Assert.Equal(9600, status.Settings.BaudRate);
        Assert.Equal(9600, _driver.Current.Settings.BaudRate);
        Assert.Equal("Opened the port", _host.Tabs.Single().AssistantStatus.LastAction);
    }

    [Fact]
    public async Task Open_port_with_a_speed_uses_and_keeps_it()
    {
        await _tools.OpenPortAsync("COM5", baudRate: 57600);

        Assert.Equal(57600, _driver.Current.Settings.BaudRate);
        Assert.Equal(57600, _host.Tabs.Single().Settings.Serial.BaudRate);
    }

    [Fact]
    public async Task Open_port_says_why_the_port_could_not_open()
    {
        _driver.FailNextOpen(SerialPortError.InUse);

        var error = await Assert.ThrowsAsync<McpException>(() => _tools.OpenPortAsync("COM5"));

        Assert.Contains("another program is using the port", error.Message);
    }

    [Fact]
    public async Task Close_port_closes_the_port_and_keeps_the_tab()
    {
        var tab = await OpenTabAsync(Ch340);

        await _tools.ClosePortAsync("COM5");

        Assert.Equal(PortSessionState.Closed, tab.Session.State);
        Assert.Single(_host.Tabs);
    }

    // Reading.

    [Fact]
    public async Task The_first_read_returns_the_recent_output_then_only_new_output()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, new string('x', 5000) + "END");

        var first = await _tools.ReadAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);
        var second = await _tools.ReadAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);
        await ReceiveAsync(tab, "more");
        var third = await _tools.ReadAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);

        Assert.Equal(AssistantTools.Backlog, first.Bytes);
        Assert.EndsWith("xxxEND", first.Output);
        Assert.Equal(string.Empty, second.Output);
        Assert.Equal("more", third.Output);
    }

    [Fact]
    public async Task Read_waits_for_output_when_there_is_none()
    {
        var tab = await OpenTabAsync(Ch340);

        var reading = _tools.ReadAsync("COM5", timeoutMs: 5000, cancellationToken: Cancellation);
        await Task.Delay(50, Cancellation);
        _driver.Current.Receive("hi"u8.ToArray());
        var result = await reading;

        Assert.Equal("hi", result.Output);
        Assert.Equal(PortSessionState.Open, result.State);
    }

    [Fact]
    public async Task Read_gives_up_after_its_timeout()
    {
        await OpenTabAsync(Ch340);

        var result = await _tools.ReadAsync("COM5", timeoutMs: 50, cancellationToken: Cancellation);

        Assert.Equal((string.Empty, 0), (result.Output, result.Bytes));
    }

    [Fact]
    public async Task What_does_not_fit_stays_unread()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, new string('a', 100));

        var result = await _tools.ReadAsync("COM5", timeoutMs: 0, maxBytes: 30, cancellationToken: Cancellation);

        Assert.Equal((30, 70L), (result.Bytes, result.UnreadBytes));
    }

    [Fact]
    public async Task Read_latest_skips_older_unread_output()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, "old output new");

        var result = await _tools.ReadAsync("COM5", timeoutMs: 0, maxBytes: 3, latest: true, cancellationToken: Cancellation);

        Assert.Equal("new", result.Output);
        Assert.Equal(11, result.SkippedBytes);
    }

    [Fact]
    public async Task Read_can_return_hex()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, [0x00, 0x7E, 0xFF]);

        var result = await _tools.ReadAsync("COM5", timeoutMs: 0, hex: true, cancellationToken: Cancellation);

        Assert.Equal("00 7E FF", result.Output);
    }

    [Fact]
    public async Task Each_assistant_has_its_own_read_position()
    {
        var tab = await OpenTabAsync(Ch340);
        var other = new AssistantTools(_host.Host, _ports, _devices, _settings, _activity, TimeProvider.System);
        await ReceiveAsync(tab, "boot");

        var first = await _tools.ReadAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);
        var second = await other.ReadAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);

        Assert.Equal(("boot", "boot"), (first.Output, second.Output));
    }

    // Waiting for text.

    [Fact]
    public async Task Wait_for_returns_once_the_text_arrives_even_split_between_reads()
    {
        var tab = await OpenTabAsync(Ch340);

        var waiting = _tools.WaitForAsync("COM5", "Hit any key", timeoutMs: 5000, cancellationToken: Cancellation);
        await ReceiveAsync(tab, "U-Boot 2026.10\r\nHit an");
        await ReceiveAsync(tab, "y key to stop autoboot:  3");
        var result = await waiting;

        Assert.True(result.Matched);
        Assert.Equal("Hit any key", result.Match);
        Assert.Equal("U-Boot 2026.10\r\nHit any key to stop autoboot:  3", result.Output);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Wait_for_times_out_with_what_arrived()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, "abc");

        var result = await _tools.WaitForAsync("COM5", "xyz", timeoutMs: 50, cancellationToken: Cancellation);

        Assert.False(result.Matched);
        Assert.True(result.TimedOut);
        Assert.Equal("abc", result.Output);
    }

    [Fact]
    public async Task Wait_for_can_skip_unread_output_so_only_new_output_matches()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, "login: ");

        var result = await _tools.WaitForAsync("COM5", "login: ", timeoutMs: 50, skipUnread: true, cancellationToken: Cancellation);

        Assert.False(result.Matched);
        Assert.Equal(7, result.SkippedBytes);
    }

    [Fact]
    public async Task Patterns_are_plain_text_unless_regex_is_asked_for()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, "user@host:~$ ");

        var text = await _tools.WaitForAsync("COM5", "$ ", timeoutMs: 0, cancellationToken: Cancellation);
        await ReceiveAsync(tab, "Read 512 bytes");
        var regex = await _tools.WaitForAsync("COM5", @"\d+ BYTES", timeoutMs: 0, regex: true, ignoreCase: true, cancellationToken: Cancellation);

        Assert.Equal("$ ", text.Match);
        Assert.Equal("512 bytes", regex.Match);
    }

    [Fact]
    public async Task An_invalid_regular_expression_is_reported()
    {
        await OpenTabAsync(Ch340);

        var error = await Assert.ThrowsAsync<McpException>(() => _tools.WaitForAsync("COM5", "(", regex: true, cancellationToken: Cancellation));

        Assert.Contains("Not a valid regular expression", error.Message);
    }

    // Commands.

    [Fact]
    public async Task Send_command_sends_the_line_with_the_devices_enter_and_returns_the_reply()
    {
        var tab = await OpenTabAsync(Ch340, DeviceSettings.Default with { Serial = SerialSettings.Default with { SendLineEnding = LineEnding.CrLf } });
        await ReceiveAsync(tab, "earlier noise");

        var command = _tools.SendCommandAsync("COM5", "version", until: "=> ", timeoutMs: 5000, cancellationToken: Cancellation);
        await WaitForAsync(() => _driver.Current.Written.Length > 0);
        await ReceiveAsync(tab, "version\r\nU-Boot 2026.10\r\n=> ");
        var result = await command;

        Assert.Equal("version\r\n"u8.ToArray(), _driver.Current.Written);
        Assert.Equal("version\r\nU-Boot 2026.10\r\n=> ", result.Output);
        Assert.True(result.Matched);
        Assert.Equal(13, result.SkippedBytes);
        Assert.Equal("Sent \"version\"", tab.AssistantStatus.LastAction);
    }

    [Fact]
    public async Task Send_command_without_until_returns_after_a_pause_in_the_output()
    {
        var tab = await OpenTabAsync(Ch340);

        var command = _tools.SendCommandAsync("COM5", "help", timeoutMs: 10_000, quietMs: 100, cancellationToken: Cancellation);
        await WaitForAsync(() => _driver.Current.Written.Length > 0);
        await ReceiveAsync(tab, "commands: ...");
        var result = await command;

        Assert.Equal("commands: ...", result.Output);
        Assert.Null(result.Matched);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Send_command_on_a_port_that_is_not_open_says_so()
    {
        _host.AddTab(Ch340);

        var error = await Assert.ThrowsAsync<McpException>(() => _tools.SendCommandAsync("COM5", "ls", cancellationToken: Cancellation));

        Assert.Contains("COM5 is not open", error.Message);
    }

    [Fact]
    public async Task Local_echo_shows_what_the_assistant_sends()
    {
        var tab = await OpenTabAsync(Ch340, DeviceSettings.Default with { Terminal = DeviceSettings.Default.Terminal with { LocalEcho = true } });

        await _tools.SendCommandAsync("COM5", "ls", timeoutMs: 0, cancellationToken: Cancellation);

        Assert.Equal(["ls\r"], tab.Echoed);
    }

    // Writing.

    [Fact]
    public async Task Write_sends_text_exactly_as_given()
    {
        var tab = await OpenTabAsync(Ch340);

        await _tools.WriteAsync("COM5", text: "\u0003");

        Assert.Equal([0x03], _driver.Current.Written);
        Assert.Equal("Sent \"\\x03\"", tab.AssistantStatus.LastAction);
    }

    [Fact]
    public async Task Write_with_enter_adds_the_devices_line_ending()
    {
        await OpenTabAsync(Ch340, DeviceSettings.Default with { Serial = SerialSettings.Default with { SendLineEnding = LineEnding.Lf } });

        await _tools.WriteAsync("COM5", text: "ls", enter: true);

        Assert.Equal("ls\n"u8.ToArray(), _driver.Current.Written);
    }

    [Fact]
    public async Task Write_sends_bytes_given_in_hex()
    {
        await OpenTabAsync(Ch340);

        await _tools.WriteAsync("COM5", hex: "7E 01, 0x0a7e A");

        Assert.Equal([0x7E, 0x01, 0x0A, 0x7E, 0x0A], _driver.Current.Written);
    }

    [Theory]
    [InlineData("a", "7E", "not both")]
    [InlineData(null, "7G", "\"7G\" is not hex")]
    [InlineData(null, null, "Nothing to send")]
    public async Task Write_rejects_what_it_cannot_send(string? text, string? hex, string message)
    {
        await OpenTabAsync(Ch340);

        var error = await Assert.ThrowsAsync<McpException>(() => _tools.WriteAsync("COM5", text, hex));

        Assert.Contains(message, error.Message);
        Assert.Empty(_driver.Current.Written);
    }

    // Lines.

    [Fact]
    public async Task Set_control_lines_changes_only_the_lines_given()
    {
        await OpenTabAsync(Ch340);

        var status = await _tools.SetControlLinesAsync("COM5", rts: true);

        Assert.Equal(new ControlLines(Dtr: true, Rts: true), _driver.Current.Lines);
        Assert.True(status.Lines.Rts);
    }

    [Fact]
    public async Task Send_break_holds_the_line_for_the_duration_up_to_ten_seconds()
    {
        await OpenTabAsync(Ch340);

        await _tools.SendBreakAsync("COM5", durationMs: 100);
        await _tools.SendBreakAsync("COM5", durationMs: 600_000);

        Assert.Equal([TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10)], _driver.Current.Breaks);
    }

    // Settings and the tab.

    [Fact]
    public async Task Configure_port_changes_only_the_settings_given()
    {
        var tab = await OpenTabAsync(Ch340);

        var status = await _tools.ConfigurePortAsync("COM5", parity: "even", stopBits: "2", flowControl: "rts/cts", sendLineEnding: "cr+lf", localEcho: true);

        Assert.Equal(SerialSettings.Default with { Parity = Parity.Even, StopBits = StopBits.Two, FlowControl = FlowControl.RtsCts, SendLineEnding = LineEnding.CrLf }, tab.Settings.Serial);
        Assert.True(tab.Settings.Terminal.LocalEcho);
        Assert.Equal(Parity.Even, _driver.Current.Settings.Parity);
        Assert.Equal(("even", "2", "rts/cts", "CR+LF"), (status.Settings.Parity, status.Settings.StopBits, status.Settings.FlowControl, status.Settings.SendLineEnding));
    }

    [Fact]
    public async Task Configure_port_explains_values_it_does_not_take()
    {
        await OpenTabAsync(Ch340);

        var error = await Assert.ThrowsAsync<McpException>(() => _tools.ConfigurePortAsync("COM5", parity: "maybe"));

        Assert.Equal("parity cannot be \"maybe\": use none, odd, even, mark or space.", error.Message);
    }

    [Fact]
    public async Task Configure_port_reports_settings_the_driver_rejects()
    {
        var tab = await OpenTabAsync(Ch340);
        _driver.Current.RejectSettings = true;

        var error = await Assert.ThrowsAsync<McpException>(() => _tools.ConfigurePortAsync("COM5", baudRate: 3));

        Assert.Contains("rejected these settings", error.Message);
        Assert.Equal(SerialSettings.Default, tab.Settings.Serial);
    }

    [Fact]
    public async Task The_tab_can_be_renamed_shown_cleared_and_logged()
    {
        var tab = await OpenTabAsync(Ch340);

        await _tools.RenameTabAsync("COM5", "  Router console ");
        await _tools.ShowTabAsync("Router console");
        await _tools.ClearTerminalAsync("Router console");
        var started = await _tools.SetLoggingAsync("COM5", enabled: true);
        var stopped = await _tools.SetLoggingAsync("COM5", enabled: false);
        await _tools.RenameTabAsync("COM5", "");

        Assert.Null(tab.Settings.Name);
        Assert.Equal((1, 1), (tab.Shown, tab.Cleared));
        Assert.Equal(@"Logging to C:\Logs\COM5.log.", started.Result);
        Assert.Equal(@"Stopped logging to C:\Logs\COM5.log.", stopped.Result);
        Assert.Null(tab.LogPath);
    }

    [Fact]
    public async Task A_log_that_cannot_start_is_reported()
    {
        var tab = await OpenTabAsync(Ch340);
        tab.LogFailure = new IOException("The disk is full.");

        var error = await Assert.ThrowsAsync<McpException>(() => _tools.SetLoggingAsync("COM5", enabled: true));

        Assert.Equal("Logging could not start: The disk is full.", error.Message);
    }

    // What the tabs show.

    [Fact]
    public async Task Tabs_show_the_assistants_using_them_until_they_disconnect()
    {
        var tab = await OpenTabAsync(Ch340);

        await _tools.WriteAsync("COM5", text: "x");
        var connected = tab.AssistantStatus;
        _activity.Release(_tools);

        Assert.Equal(1, connected.Clients);
        Assert.Equal("Sent \"x\"", connected.LastAction);
        Assert.NotNull(connected.LastActionAt);
        Assert.Equal(0, tab.AssistantStatus.Clients);
    }

    private void SetAccess(AiAccess access) => _settings.Update(settings => settings with { AiAccess = access });

    private static DeviceSettings WithMcp() => DeviceSettings.Default with { McpEnabled = true };

    // Both ways.

    [Fact]
    public async Task Read_traffic_tells_what_the_device_sent_from_what_was_sent_to_it_and_by_whom()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, "login: ");
        await _tools.WriteAsync("COM5", text: "root\r");
        await tab.Session.SendAsync("ls\r"u8.ToArray(), TrafficSender.Keyboard);
        await tab.Session.SendAsync("AT\r"u8.ToArray(), TrafficSender.Comrade);
        await ReceiveAsync(tab, "# ");

        var result = await _tools.ReadTrafficAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);

        Assert.Equal(
            [("rx", "device", "login: "), ("tx", "assistant", "root\r"), ("tx", "user", "ls\r"), ("tx", "comrade", "AT\r"), ("rx", "device", "# ")],
            result.Traffic.Select(piece => (piece.Direction, piece.From, piece.Text)));
        Assert.Equal(20, result.Bytes);
        Assert.Equal(0, result.UnreadBytes);
        Assert.All(result.Traffic, piece => Assert.Null(piece.Hex));
    }

    [Fact]
    public async Task Read_traffic_decodes_each_side_on_its_own()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, [(byte)'c', (byte)'a', (byte)'f', 0xC3]);
        await tab.Session.SendAsync("x"u8.ToArray(), TrafficSender.Keyboard);
        await ReceiveAsync(tab, [0xA9, (byte)'\e', (byte)'[', (byte)'0', (byte)'m']);

        var result = await _tools.ReadTrafficAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);

        Assert.Equal(["caf", "x", "\xE9"], result.Traffic.Select(piece => piece.Text));
    }

    [Fact]
    public async Task Read_traffic_gives_hex_when_asked()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, [0x7E, 0x01]);
        await tab.Session.SendAsync(new byte[] { 0x0D }, TrafficSender.Comrade);

        var result = await _tools.ReadTrafficAsync("COM5", timeoutMs: 0, hex: true, cancellationToken: Cancellation);

        Assert.Equal([("7E 01", (string?)null), ("0D", null)], result.Traffic.Select(piece => (piece.Hex, piece.Text)));
    }

    [Fact]
    public async Task Read_traffic_and_read_keep_their_own_places()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, "boot\r\n");

        var traffic = await _tools.ReadTrafficAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);
        var read = await _tools.ReadAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);
        var again = await _tools.ReadTrafficAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);

        Assert.Equal("boot\r\n", traffic.Traffic.Single().Text);
        Assert.Equal("boot\r\n", read.Output);
        Assert.Empty(again.Traffic);
    }

    [Fact]
    public async Task Read_traffic_waits_for_traffic_either_way()
    {
        var tab = await OpenTabAsync(Ch340);
        await _tools.ReadTrafficAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);

        var read = _tools.ReadTrafficAsync("COM5", timeoutMs: 5000, cancellationToken: Cancellation);
        await tab.Session.SendAsync("reset\r"u8.ToArray(), TrafficSender.Comrade);

        var piece = Assert.Single((await read).Traffic);
        Assert.Equal(("tx", "comrade", "reset\r"), (piece.Direction, piece.From, piece.Text));
    }

    [Fact]
    public async Task Read_traffic_keeps_every_control_character_of_what_was_sent()
    {
        var tab = await OpenTabAsync(Ch340);
        await _tools.WriteAsync("COM5", text: "\x03");
        await ReceiveAsync(tab, "^C\r\n\x1B[32m# \x1B[0m");
        await tab.Session.SendAsync("\x1B[A\r"u8.ToArray(), TrafficSender.Keyboard);

        var result = await _tools.ReadTrafficAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);

        Assert.Equal(["\x03", "^C\r\n# ", "\x1B[A\r"], result.Traffic.Select(piece => piece.Text));
    }

    [Fact]
    public async Task Read_traffic_returns_at_most_512_pieces_at_a_time()
    {
        var tab = await OpenTabAsync(Ch340);
        _ = await _tools.ReadTrafficAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);
        // The device and another sender taking turns a byte at a time.
        for (var i = 0; i < 300; i++)
        {
            tab.Session.Traffic.Append(TrafficSender.Device, "a"u8, DateTimeOffset.UtcNow);
            tab.Session.Traffic.Append(TrafficSender.Comrade, "b"u8, DateTimeOffset.UtcNow);
        }

        var first = await _tools.ReadTrafficAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);
        var rest = await _tools.ReadTrafficAsync("COM5", timeoutMs: 0, cancellationToken: Cancellation);

        Assert.Equal(512, first.Traffic.Count);
        Assert.Equal(88, first.UnreadBytes);
        Assert.Equal(88, rest.Traffic.Count);
        Assert.Equal("a", first.Traffic[0].Text);
        Assert.Equal("b", rest.Traffic[^1].Text);
    }

    [Fact]
    public async Task Read_traffic_can_skip_to_the_latest()
    {
        var tab = await OpenTabAsync(Ch340);
        await ReceiveAsync(tab, "old output\r\n");
        await tab.Session.SendAsync("new"u8.ToArray(), TrafficSender.Keyboard);

        var result = await _tools.ReadTrafficAsync("COM5", timeoutMs: 0, maxBytes: 5, latest: true, cancellationToken: Cancellation);

        Assert.Equal([("device", "\r\n"), ("user", "new")], result.Traffic.Select(piece => (piece.From, piece.Text)));
        Assert.Equal(10, result.SkippedBytes);
    }

    private async Task<FakeAssistantTab> OpenTabAsync(SerialPortInfo port, DeviceSettings? settings = null)
    {
        var tab = _host.AddTab(port, settings);
        await tab.OpenAsync();
        return tab;
    }

    private Task ReceiveAsync(FakeAssistantTab tab, string text) => ReceiveAsync(tab, Encoding.UTF8.GetBytes(text));

    // The session reads on a background thread, 4 KB at a time: wait until the bytes are in its buffer.
    private async Task ReceiveAsync(FakeAssistantTab tab, byte[] data)
    {
        var end = tab.Session.Received.End + data.Length;
        var connection = _driver.Connections.Last(connection => connection.PortName == tab.Session.Port.PortName);
        foreach (var chunk in data.Chunk(4096))
            connection.Receive(chunk);
        await WaitForAsync(() => tab.Session.Received.End >= end);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met within 5 seconds.");
            await Task.Delay(10, Cancellation);
        }
    }
}
