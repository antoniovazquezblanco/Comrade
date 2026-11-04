# Developing COMrade

## Requirements

- Windows 10 version 1809 or later, x64 (the terminal control does not support x86 or ARM64 yet)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Optional: Visual Studio 2026 with the WinUI application development workload

## Build and run

```powershell
dotnet build COMrade.slnx -c Release -p:Platform=x64
dotnet run --project COMrade
```

Or open `COMrade.slnx` in Visual Studio and press <kbd>F5</kbd>.

A self-contained build, like the one in the releases:

```powershell
dotnet publish COMrade/COMrade.csproj -c Release -p:Platform=x64 -p:SelfContained=true -o publish/COMrade
```

The installer packs that build into an MSI (WiX 5) that installs either just for the current user
or, with administrator rights, for every user. It is not built with the solution, as it needs the
published app, and takes its version from that build's `COMrade.exe` (0.3.1 for a build after
v0.3.0: an MSI version cannot say pre-release). The MSI lands in
`COMrade.Installer/bin/Release/en-US/`:

```powershell
dotnet build COMrade.Installer -c Release -p:AppFolder="$PWD/publish/COMrade"
```

Unattended, `msiexec /i COMrade.msi /qn` installs for the current user and adds `ALLUSERS=1` to
install for every user (from an administrator prompt).

Visual Studio opens the installer project with FireGiant's free HeatWave extension (Extensions >
Manage Extensions, search for HeatWave). Without it the project shows as incompatible, and the rest
of the solution works as usual.

## Tests

Unit tests, no hardware needed:

```powershell
dotnet test --project COMrade.Core.Tests
```

Integration tests use real serial ports and skip themselves unless you name the ports:

| Variable | Port |
|---|---|
| `COMRADE_TEST_PORT` | Any real serial port, e.g. `COM5` |
| `COMRADE_LOOPBACK_PORT` | A port with TX wired to RX |

```powershell
$env:COMRADE_LOOPBACK_PORT = "COM16"
dotnet test --project COMrade.IntegrationTests
```

The integration tests also start the MCP relay (`COMrade.Mcp.exe`) against a server of their own;
those need no serial port.

## AI access (MCP)

COMrade serves AI assistants over MCP while AI access is on in its settings. The MCP client starts
`COMrade.Mcp.exe`, which relays MCP between the client's standard input and output and a named pipe
of the running COMrade (`COMrade.Mcp.<user SID>`). The pipe is created for the current user only and
refuses clients from other machines. The tools live in `COMrade.Core/Mcp`; the app gives them its tabs
through `IAssistantHost` and `IAssistantTab`.

To try a development build, turn AI access on in its settings and add the relay built next to it.
The settings page shows how for each client, with the debug build's path; for Claude Code:

```powershell
claude mcp add comrade -- "$PWD\COMrade\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\COMrade.Mcp.exe"
```

`COMRADE_MCP_PIPE` gives the relay another pipe name, for tests that run their own server.

The setup steps for each client live in `COMrade.Core/Mcp/ClientSetup.cs`. Clients change how they
take servers: check the steps against their documentation when one reports a problem.

## Project layout

| Project | Contents |
|---|---|
| `COMrade` | WinUI 3 app: windows, views, view models, the terminal control host |
| `COMrade.Core` | No UI: port discovery and device identity, serial connection, port sessions with automatic reopen, settings stores, terminal line endings, the MCP server for AI assistants |
| `COMrade.Mcp` | The MCP relay AI clients start (`COMrade.Mcp.exe`), built and published next to `COMrade.exe` |
| `COMrade.Core.Tests` | xUnit unit tests with fakes and a fake clock |
| `COMrade.IntegrationTests` | Tests against real serial ports |
| `COMrade.Installer` | Per-user MSI installer of the self-contained build |
