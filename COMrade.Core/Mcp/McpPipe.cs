using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;

namespace COMrade.Core.Mcp;

/// <summary>
/// Where COMrade and its MCP relay (COMrade.Mcp.exe) meet. Also compiled into the relay, which
/// does not reference COMrade.Core, so this file uses nothing else from it.
/// </summary>
[SupportedOSPlatform("windows")]
public static class McpPipe
{
    /// <summary>Overrides the pipe name, for tests that run their own server.</summary>
    public const string NameVariable = "COMRADE_MCP_PIPE";

    /// <summary>
    /// One pipe per Windows user, so two users of the same machine each reach their own COMrade.
    /// Both ends also open it for the current user only.
    /// </summary>
    public static string Name =>
        Environment.GetEnvironmentVariable(NameVariable) is { Length: > 0 } name
            ? name
            : $"COMrade.Mcp.{WindowsIdentity.GetCurrent().User?.Value}";

    /// <summary>
    /// Connects to the pipe <paramref name="name"/> and checks that the current user owns it, so that
    /// another user of the machine cannot pose as COMrade by creating the pipe first.
    /// </summary>
    /// <exception cref="TimeoutException">Nothing listened on the pipe within <paramref name="timeout"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The pipe belongs to another user.</exception>
    /// <remarks>
    /// Not .NET's <see cref="PipeOptions.CurrentUserOnly"/>: it compares the owner with the token's
    /// default owner, which in an elevated process is the Administrators group, so an elevated
    /// assistant (or a build agent) would be refused. COMrade makes the user the owner either way.
    /// </remarks>
    public static async Task<NamedPipeClientStream> ConnectAsync(string name, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)timeout.TotalMilliseconds, cancellationToken);
            using var user = WindowsIdentity.GetCurrent();
            if (pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) != user.User)
                throw new UnauthorizedAccessException($"The pipe {name} belongs to another user.");
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// True when the settings file (settings.json) leaves AI access off, also when it does not say:
    /// the relay must not start COMrade then. Reads the property of <c>AppSettings.AiAccess</c>.
    /// </summary>
    public static bool IsAccessOff(string settingsJson)
    {
        try
        {
            using var settings = JsonDocument.Parse(settingsJson);
            return !(settings.RootElement.ValueKind is JsonValueKind.Object
                && settings.RootElement.TryGetProperty("aiAccess", out var access)
                && access.ValueKind is JsonValueKind.String
                && access.GetString() is "PerTab" or "AllPorts");
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
