using System.IO.Pipes;
using System.Text.Json;
using COMrade.Core.Mcp;
using COMrade.Core.Ports;
using COMrade.Core.Settings;
using COMrade.Core.Tests.Fakes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace COMrade.Core.Tests.Mcp;

public sealed class McpPipeServerTests : IAsyncDisposable
{
    private static readonly string[] ToolNames =
    [
        "clear_terminal", "close_port", "configure_port", "list_ports", "open_port", "port_status", "read", "read_traffic",
        "rename_tab", "send_break", "send_command", "set_control_lines", "set_logging", "show_tab", "wait_for", "write",
    ];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"comrade-tests-{Guid.NewGuid():N}");
    private readonly string _pipeName = $"COMrade.Tests.{Guid.NewGuid():N}";
    private readonly PortList _ports = new();
    private readonly AppSettingsStore _settings;
    private readonly DeviceSettingsStore _devices;
    private readonly FakeAssistantHost _host;
    private readonly McpPipeServer _server;
    private readonly List<IAsyncDisposable> _clients = [];

    public McpPipeServerTests()
    {
        _ports.AddOrUpdate(new PortDevice(@"USB\VID_1A86&PID_7523\5&2F3C1B2&0&3", "COM5", "USB-SERIAL CH340 (COM5)", LocationPath: "USB(3)"));
        _settings = new AppSettingsStore(Path.Combine(_folder, "settings.json"));
        _devices = new DeviceSettingsStore(Path.Combine(_folder, "devices.json"));
        _host = new FakeAssistantHost(_ports, new FakeSerialDriver(), _devices, _settings);
        _server = new McpPipeServer(_host.Host, _ports, _devices, _settings, "1.2.3", _pipeName);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync();
        await _server.DisposeAsync();
        await _host.DisposeAsync();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task Nothing_listens_while_ai_access_is_off()
    {
        _server.Start();

        Assert.Equal(McpServerState.Off, _server.State);
        await Assert.ThrowsAsync<TimeoutException>(() => ConnectPipeAsync(timeoutMs: 200));
    }

    [Fact]
    public async Task An_assistant_gets_the_tools_and_the_instructions()
    {
        SetAccess(AiAccess.AllPorts);
        _server.Start();

        var client = await ConnectAsync();
        var tools = await client.ListToolsAsync(cancellationToken: Cancellation);

        Assert.Equal(McpServerState.Listening, _server.State);
        Assert.Equal(ToolNames, tools.Select(tool => tool.Name).Order());
        Assert.Equal(("COMrade", "1.2.3"), (client.ServerInfo?.Name, client.ServerInfo?.Version));
        Assert.Contains("untrusted data", client.ServerInstructions);
        await WaitForAsync(() => _server.Clients == 1);
    }

    [Fact]
    public async Task Reading_tools_say_so_and_the_others_do_not()
    {
        SetAccess(AiAccess.AllPorts);
        _server.Start();

        var tools = await (await ConnectAsync()).ListToolsAsync(cancellationToken: Cancellation);

        var readOnly = tools.Where(tool => tool.ProtocolTool.Annotations is { ReadOnlyHint: true }).Select(tool => tool.Name).Order();
        Assert.Equal(["list_ports", "port_status", "read", "read_traffic", "wait_for"], readOnly);
    }

    [Fact]
    public async Task Results_come_as_json_and_failures_as_tool_errors()
    {
        SetAccess(AiAccess.AllPorts);
        _server.Start();
        var client = await ConnectAsync();

        var list = await client.CallToolAsync("list_ports", cancellationToken: Cancellation);
        var missing = await client.CallToolAsync("port_status", new Dictionary<string, object?> { ["port"] = "COM42" }, cancellationToken: Cancellation);

        using var json = JsonDocument.Parse(Text(list));
        Assert.NotEqual(true, list.IsError);
        Assert.Equal("AllPorts", json.RootElement.GetProperty("access").GetString());
        Assert.Equal("COM5", json.RootElement.GetProperty("ports")[0].GetProperty("port").GetString());
        Assert.False(json.RootElement.GetProperty("ports")[0].TryGetProperty("state", out _));
        Assert.True(missing.IsError);
        Assert.Contains("no port or tab named \"COM42\"", Text(missing));
    }

    [Fact]
    public async Task Turning_ai_access_off_disconnects_assistants()
    {
        SetAccess(AiAccess.PerTab);
        _server.Start();
        await ConnectAsync();
        await WaitForAsync(() => _server.Clients == 1);

        SetAccess(AiAccess.Off);

        Assert.Equal(McpServerState.Off, _server.State);
        await WaitForAsync(() => _server.Clients == 0);
        await Assert.ThrowsAsync<TimeoutException>(() => ConnectPipeAsync(timeoutMs: 200));
    }

    [Fact]
    public async Task Switching_between_per_tab_and_all_ports_keeps_assistants_connected()
    {
        SetAccess(AiAccess.PerTab);
        _server.Start();
        var client = await ConnectAsync();

        SetAccess(AiAccess.AllPorts);
        var list = await client.CallToolAsync("list_ports", cancellationToken: Cancellation);

        Assert.Contains("\"access\":\"AllPorts\"", Text(list));
    }

    [Fact]
    public async Task Clients_from_the_network_are_refused()
    {
        SetAccess(AiAccess.AllPorts);
        _server.Start();
        await WaitForAsync(() => _server.State == McpServerState.Listening);

        // Through the SMB server, as a client on another machine would come; the same user is
        // let in locally (see An_assistant_gets_the_tools_and_the_instructions).
        await using var remote = new NamedPipeClientStream("localhost", _pipeName, PipeDirection.InOut);
        var error = await Record.ExceptionAsync(() => remote.ConnectAsync(5000, Cancellation));

        Assert.NotNull(error);
        Assert.SkipWhen(error is not UnauthorizedAccessException, $"No SMB server to come through ({error.GetType().Name}: {error.Message})");
    }

    [Fact]
    public async Task Another_server_on_the_same_pipe_stays_unavailable()
    {
        SetAccess(AiAccess.AllPorts);
        _server.Start();
        await using var second = new McpPipeServer(_host.Host, _ports, _devices, _settings, "1.2.3", _pipeName);

        second.Start();

        await WaitForAsync(() => second.State == McpServerState.Unavailable);
        Assert.Equal(McpServerState.Listening, _server.State);
    }

    [Fact]
    public async Task Several_assistants_can_connect_at_once()
    {
        SetAccess(AiAccess.AllPorts);
        _server.Start();

        var first = await ConnectAsync();
        var second = await ConnectAsync();
        await first.ListToolsAsync(cancellationToken: Cancellation);
        await second.ListToolsAsync(cancellationToken: Cancellation);

        await WaitForAsync(() => _server.Clients == 2);
    }

    private void SetAccess(AiAccess access) => _settings.Update(settings => settings with { AiAccess = access });

    // As the relay connects.
    private Task<NamedPipeClientStream> ConnectPipeAsync(int timeoutMs = 5000) =>
        McpPipe.ConnectAsync(_pipeName, TimeSpan.FromMilliseconds(timeoutMs), Cancellation);

    private async Task<McpClient> ConnectAsync()
    {
        var pipe = await ConnectPipeAsync();
        _clients.Add(pipe);
        var client = await McpClient.CreateAsync(new StreamClientTransport(pipe, pipe), cancellationToken: Cancellation);
        _clients.Insert(0, client);
        return client;
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

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
