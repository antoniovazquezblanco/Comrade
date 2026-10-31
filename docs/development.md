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
