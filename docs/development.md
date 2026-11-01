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
published app; the version must be plain numbers. The MSI lands in
`COMrade.Installer/bin/Release/en-US/`:

```powershell
dotnet build COMrade.Installer -c Release -p:AppFolder="$PWD/publish/COMrade" -p:ProductVersion=0.3.0
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

## Project layout

| Project | Contents |
|---|---|
| `COMrade` | WinUI 3 app: windows, views, view models, the terminal control host |
| `COMrade.Core` | No UI: port discovery and device identity, serial connection, port sessions with automatic reopen, settings stores, terminal line endings |
| `COMrade.Core.Tests` | xUnit unit tests with fakes and a fake clock |
| `COMrade.IntegrationTests` | Tests against real serial ports |
| `COMrade.Installer` | Per-user MSI installer of the self-contained build |
