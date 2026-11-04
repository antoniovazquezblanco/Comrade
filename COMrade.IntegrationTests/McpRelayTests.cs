using COMrade.Core.Mcp;
using COMrade.Core.Ports;
using COMrade.Core.Settings;
using ModelContextProtocol.Client;

namespace COMrade.IntegrationTests;

/// <summary>
/// The relay (COMrade.Mcp.exe) started as an MCP client starts it, talking to COMrade's MCP server
/// on a pipe of the test's own. No serial port needed.
/// </summary>
public sealed class McpRelayTests : IAsyncDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"comrade-tests-{Guid.NewGuid():N}");
    private readonly string _pipeName = $"COMrade.Tests.{Guid.NewGuid():N}";
    private readonly McpPipeServer _server;

    public McpRelayTests()
    {
        var settings = new AppSettingsStore(Path.Combine(_folder, "settings.json"));
        settings.Save(AppSettings.Default with { AiAccess = AiAccess.AllPorts });
        var devices = new DeviceSettingsStore(Path.Combine(_folder, "devices.json"));
        _server = new McpPipeServer(new NoWindows(), new PortList(), devices, settings, "1.2.3", _pipeName);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task An_assistant_reaches_comrade_through_the_relay()
    {
        _server.Start();

        await using var client = await StartRelayAsync();
        var tools = await client.ListToolsAsync(cancellationToken: Cancellation);
        var ports = await client.CallToolAsync("list_ports", cancellationToken: Cancellation);

        Assert.Equal("COMrade", client.ServerInfo?.Name);
        Assert.Contains(tools, tool => tool.Name == "send_command");
        Assert.NotEqual(true, ports.IsError);
        Assert.Equal(1, _server.Clients);
    }

    [Fact]
    public async Task The_relay_ends_the_session_when_comrade_stops_serving()
    {
        _server.Start();
        var client = await StartRelayAsync();
        await client.ListToolsAsync(cancellationToken: Cancellation);

        await _server.DisposeAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => client.ListToolsAsync(cancellationToken: Cancellation).AsTask());
        await client.DisposeAsync();
    }

    private Task<McpClient> StartRelayAsync() => McpClient.CreateAsync(
        new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "COMrade",
            Command = Path.Combine(AppContext.BaseDirectory, "COMrade.Mcp.exe"),
            EnvironmentVariables = new Dictionary<string, string?> { [McpPipe.NameVariable] = _pipeName },
        }),
        cancellationToken: Cancellation);

    // COMrade without windows: enough to list the tools and the ports.
    private sealed class NoWindows : IAssistantHost
    {
        public Task<IReadOnlyList<IAssistantTab>> GetTabsAsync() => Task.FromResult<IReadOnlyList<IAssistantTab>>([]);

        public Task<IAssistantTab> OpenTabAsync(SerialPortInfo port) => throw new InvalidOperationException("No windows.");

        public Task ShowTabAsync(IAssistantTab tab) => Task.CompletedTask;
    }
}
