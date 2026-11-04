using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using COMrade.Core.Mcp;

[assembly: SupportedOSPlatform("windows")]

return await Relay.RunAsync();

/// <summary>
/// COMrade.Mcp.exe, the program an AI assistant's MCP client starts to use COMrade. It passes MCP
/// messages between the client (standard input and output) and the running COMrade (a named pipe
/// only the current user can open), starting COMrade first when it is not running and AI access is
/// on. The tools themselves live in COMrade.
/// </summary>
internal static class Relay
{
    private static readonly TimeSpan QuickConnect = TimeSpan.FromMilliseconds(500);

    // COMrade's first start on a machine takes a few seconds.
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    public static async Task<int> RunAsync()
    {
        if (!Console.IsInputRedirected)
        {
            Console.Error.WriteLine("COMrade.Mcp lets AI assistants use COMrade: their MCP client starts it. See COMrade's Settings, under AI assistants.");
            return 2;
        }

        var pipe = await ConnectAsync();
        if (pipe is null)
            return 1;

        await using (pipe)
        {
            await using var input = Console.OpenStandardInput();
            await using var output = Console.OpenStandardOutput();

            // Whichever side closes first ends the session: the client quitting, or COMrade
            // closing or turning AI access off.
            await Task.WhenAny(CopyAsync(input, pipe), CopyAsync(pipe, output));
        }

        return 0;
    }

    private static async Task<NamedPipeClientStream?> ConnectAsync()
    {
        if (await TryConnectAsync(QuickConnect) is { } pipe)
            return pipe;

        if (IsAccessOff())
            return Fail("AI access is off in COMrade. Turn it on in COMrade's Settings, under AI assistants.");

        if (!IsComradeRunning() && !StartComrade())
            return null;

        return await TryConnectAsync(StartTimeout)
            ?? Fail("COMrade does not accept AI assistants. Check COMrade's Settings, under AI assistants.");
    }

    private static async Task<NamedPipeClientStream?> TryConnectAsync(TimeSpan timeout)
    {
        try
        {
            return await McpPipe.ConnectAsync(McpPipe.Name, timeout, CancellationToken.None);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return Fail("COMrade's pipe belongs to another Windows user.");
        }
    }

    // The settings file of AppSettingsStore.DefaultPath. Without one, AI access was never turned on.
    private static bool IsAccessOff()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "COMrade", "settings.json");
        try
        {
            return McpPipe.IsAccessOff(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsComradeRunning()
    {
        var session = Process.GetCurrentProcess().SessionId;
        return Process.GetProcessesByName("COMrade").Any(process => process.SessionId == session);
    }

    // Started through Explorer, as if the user had started it: COMrade must not belong to this
    // process, or the client could close it together with the relay when it quits.
    private static bool StartComrade()
    {
        var app = Path.Combine(AppContext.BaseDirectory, "COMrade.exe");
        if (!File.Exists(app))
        {
            Fail($"COMrade is not running, and {app} does not exist.");
            return false;
        }

        try
        {
            using var explorer = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{app}\"") { UseShellExecute = false });
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Fail($"COMrade could not be started: {ex.Message}");
            return false;
        }
    }

    private static async Task CopyAsync(Stream from, Stream to)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            int count;
            while ((count = await from.ReadAsync(buffer)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, count));
                await to.FlushAsync();
            }
        }
        catch (IOException)
        {
            // The other side went away.
        }
    }

    private static NamedPipeClientStream? Fail(string message)
    {
        Console.Error.WriteLine($"COMrade.Mcp: {message}");
        return null;
    }
}
