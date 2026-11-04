using System.Text.Json;
using COMrade.Core.Mcp;

namespace COMrade.Core.Tests.Mcp;

public sealed class ClientSetupTests
{
    private const string Home = @"C:\Users\José";
    private const string Relay = Home + @"\AppData\Local\Programs\COMrade\COMrade.Mcp.exe";

    private static readonly IReadOnlyList<ClientSetup> Clients = ClientSetup.All(Relay, Home);

    [Fact]
    public void Each_client_is_listed_once_with_any_other_last()
    {
        Assert.Equal(Clients.Count, Clients.DistinctBy(client => client.Name).Count());
        Assert.Equal("Other clients", Clients[^1].Name);
        Assert.All(Clients, client => Assert.DoesNotContain(new[] { client.Steps, client.Snippet, client.Note }, string.IsNullOrWhiteSpace));
    }

    [Theory]
    [InlineData("Claude Code", "claude mcp add --scope user comrade -- \"" + Relay + "\"")]
    [InlineData("Codex", "codex mcp add comrade -- \"" + Relay + "\"")]
    [InlineData("Gemini CLI", "gemini mcp add --scope user comrade \"" + Relay + "\"")]
    public void Clients_with_a_command_get_it_with_the_relay_quoted(string name, string command)
    {
        Assert.Equal(command, Find(name).Snippet);
    }

    [Fact]
    public void A_path_that_powershell_would_expand_goes_in_single_quotes()
    {
        var clients = ClientSetup.All(@"C:\Users\it's$me\COMrade.Mcp.exe", @"C:\Users\it's$me");

        Assert.EndsWith(@" -- 'C:\Users\it''s$me\COMrade.Mcp.exe'", clients[0].Snippet);
    }

    [Theory]
    [InlineData("Claude Desktop", "mcpServers")]
    [InlineData("VS Code", "servers")]
    [InlineData("Visual Studio", "servers")]
    [InlineData("Cursor", "mcpServers")]
    [InlineData("Other clients", "mcpServers")]
    public void Clients_with_a_configuration_file_get_json_that_starts_the_relay(string name, string servers)
    {
        using var json = JsonDocument.Parse(Find(name).Snippet);

        Assert.Equal(Relay, json.RootElement.GetProperty(servers).GetProperty("comrade").GetProperty("command").GetString());
    }

    [Fact]
    public void Paths_in_json_keep_their_accents()
    {
        Assert.Contains(@"""C:\\Users\\José\\AppData", Find("Cursor").Snippet);
    }

    [Fact]
    public void Configuration_files_are_named_in_the_users_profile()
    {
        Assert.Contains(@"C:\Users\José\.mcp.json", Find("Visual Studio").Steps);
        Assert.Contains(@"C:\Users\José\.cursor\mcp.json", Find("Cursor").Steps);
        Assert.Contains(@"C:\Users\José\.codex\config.toml", Find("Codex").Note);
    }

    private static ClientSetup Find(string name) => Clients.Single(client => client.Name == name);
}
