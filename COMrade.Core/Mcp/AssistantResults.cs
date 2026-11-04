using COMrade.Core.Sessions;
using COMrade.Core.Settings;

namespace COMrade.Core.Mcp;

// What the tools return to assistants, as JSON: camelCase names, enums as text, nulls left out.
// Device output only ever appears inside string fields, so it cannot pass for COMrade's own words.

/// <summary>The result of list_ports.</summary>
internal sealed record PortsResult(AiAccess Access, IReadOnlyList<PortEntry> Ports);

/// <summary>A port of the computer, or a device tab whose device may be unplugged.</summary>
/// <param name="Port">COM name, e.g. COM5.</param>
/// <param name="Name">The tab's name, e.g. "Router console".</param>
/// <param name="Description">The device, e.g. "USB-SERIAL CH340".</param>
/// <param name="Kind">For example "USB 1A86:7523", "Bluetooth", "built-in" or "virtual".</param>
/// <param name="Connected">The device is plugged in.</param>
/// <param name="HasTab">The device has a tab in COMrade.</param>
/// <param name="State">The tab's port state; null without a tab.</param>
/// <param name="Settings">For example "115200 8N1, Enter sends CR".</param>
/// <param name="Allowed">You may use it.</param>
/// <param name="Note">Why not, or what is special about it.</param>
internal sealed record PortEntry(
    string Port,
    string? Name,
    string Description,
    string Kind,
    bool Connected,
    bool HasTab,
    PortSessionState? State,
    string Settings,
    bool Allowed,
    string? Note);

/// <summary>The result of port_status and open_port.</summary>
internal sealed record PortStatus(
    string Port,
    string? Name,
    string Description,
    PortSessionState State,
    string? Error,
    PortSettings Settings,
    LineStatus Lines,
    long BytesReceived,
    long BytesSent,
    DateTimeOffset? OpenedAt,
    string? LogFile,
    long UnreadBytes);

/// <summary>A device's settings, in the words configure_port takes.</summary>
internal sealed record PortSettings(
    int BaudRate,
    int DataBits,
    string Parity,
    string StopBits,
    string FlowControl,
    string SendLineEnding,
    bool LocalEcho,
    bool NewLineOnLf,
    bool NewLineOnCr,
    bool ReopenAutomatically,
    bool McpEnabled);

/// <summary>DTR and RTS as set; CTS, DSR and CD as the device drives them (all off while the port is not open).</summary>
internal sealed record LineStatus(bool Dtr, bool Rts, bool Cts, bool Dsr, bool CarrierDetect);

/// <param name="Output">Text without colour codes, or hex.</param>
/// <param name="Bytes">Bytes returned.</param>
/// <param name="UnreadBytes">Bytes still unread after this.</param>
/// <param name="MissedBytes">Bytes dropped before you read them: more than 1 MB arrived meanwhile.</param>
/// <param name="SkippedBytes">Unread bytes skipped on purpose.</param>
internal sealed record ReadResult(
    string Port,
    PortSessionState State,
    string Output,
    int Bytes,
    long UnreadBytes,
    long? MissedBytes,
    long? SkippedBytes);

/// <summary>The result of wait_for and send_command.</summary>
/// <param name="Matched">The pattern was found; null when there was none to find.</param>
/// <param name="Match">The text that matched.</param>
/// <param name="Output">Everything read, up to and including the part where the pattern matched.</param>
/// <param name="TimedOut">The time ran out first.</param>
/// <param name="TruncatedChars">Characters left out from the start of a long output.</param>
internal sealed record OutputResult(
    string Port,
    PortSessionState State,
    bool? Matched,
    string? Match,
    string Output,
    bool TimedOut,
    long UnreadBytes,
    long? MissedBytes,
    long? SkippedBytes,
    int? TruncatedChars);

/// <summary>The result of the tools that only do something.</summary>
internal sealed record ActionResult(string Port, string Result);
