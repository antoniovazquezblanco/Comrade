using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace COMrade.Core.Mcp;

/// <summary>
/// How to add COMrade to one MCP client, as the settings page shows it: what to do, the command or
/// configuration to copy, and what comes after. Checked against each client's documentation in
/// November 2026; clients change how they take servers, so check again when one reports a problem.
/// </summary>
public sealed record ClientSetup(string Name, string Steps, string Snippet, string Note)
{
    /// <summary>COMrade's name in the clients' configuration.</summary>
    public const string ServerName = "comrade";

    // Paths as written, accents included; only what JSON requires is escaped.
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The clients COMrade knows, then one for any other.</summary>
    /// <param name="relay">COMrade.Mcp.exe, where it is installed.</param>
    /// <param name="home">The user's profile folder, where several clients keep their configuration.</param>
    public static IReadOnlyList<ClientSetup> All(string relay, string home) =>
    [
        new(
            "Claude Code",
            "In a terminal, run:",
            $"claude mcp add --scope user {ServerName} -- {Quote(relay)}",
            "Then start a new Claude Code session: /mcp shows whether COMrade is connected."),
        new(
            "Claude Desktop",
            "In Claude Desktop, open Settings, Developer, Edit Config, and add COMrade to the \"mcpServers\" of the file it opens, keeping any servers already there:",
            Json("mcpServers", new JsonObject { ["command"] = relay }),
            "Then quit Claude Desktop completely and start it again."),
        new(
            "VS Code",
            "In VS Code, run MCP: Open User Configuration from the Command Palette (Ctrl+Shift+P) and add COMrade to its \"servers\", keeping any servers already there:",
            Json("servers", new JsonObject { ["type"] = "stdio", ["command"] = relay }),
            "GitHub Copilot uses it in Agent mode, in every workspace."),
        new(
            "Visual Studio",
            $"Create or open {Path.Combine(home, ".mcp.json")} and add COMrade to its \"servers\", keeping any servers already there:",
            Json("servers", new JsonObject { ["type"] = "stdio", ["command"] = relay }),
            "Visual Studio 2022 17.14 or later loads it for every solution. GitHub Copilot uses it in Agent mode, once COMrade's tools are turned on in the chat's tool picker."),
        new(
            "Cursor",
            $"Create or open {Path.Combine(home, ".cursor", "mcp.json")} and add COMrade to its \"mcpServers\", keeping any servers already there:",
            Json("mcpServers", new JsonObject { ["type"] = "stdio", ["command"] = relay }),
            "Cursor uses it in every project. Its settings, under MCP, show whether COMrade is connected."),
        new(
            "Codex",
            "In a terminal, run:",
            $"codex mcp add {ServerName} -- {Quote(relay)}",
            $"The Codex CLI and its IDE extension share this configuration, kept in {Path.Combine(home, ".codex", "config.toml")}."),
        new(
            "Gemini CLI",
            "In a terminal, run:",
            $"gemini mcp add --scope user {ServerName} {Quote(relay)}",
            "Then start Gemini CLI again: /mcp shows whether COMrade is connected."),
        new(
            "Other clients",
            "Other MCP clients run COMrade as a local (stdio) server: a program started with no arguments. Most take it in a JSON file, under \"mcpServers\":",
            Json("mcpServers", new JsonObject { ["command"] = relay }),
            "Then ask the assistant to list the serial ports, to check that it reaches COMrade."),
    ];

    // A path for a terminal command. Double quotes suit PowerShell, cmd and Git Bash alike, but
    // PowerShell would expand $ and ` inside them: such a path goes in PowerShell's single quotes.
    private static string Quote(string path) =>
        path.Contains('$') || path.Contains('`')
            ? $"'{path.Replace("'", "''")}'"
            : $"\"{path}\"";

    private static string Json(string servers, JsonObject server) =>
        new JsonObject { [servers] = new JsonObject { [ServerName] = server } }.ToJsonString(Indented);
}
