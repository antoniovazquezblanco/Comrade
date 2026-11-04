namespace COMrade.Core.Settings;

/// <summary>What AI assistants may use through COMrade's MCP server.</summary>
public enum AiAccess
{
    /// <summary>Nothing: COMrade does not listen for assistants.</summary>
    Off,

    /// <summary>Only devices whose tab has MCP turned on.</summary>
    PerTab,

    /// <summary>Every port.</summary>
    AllPorts,
}
