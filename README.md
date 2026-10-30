<div align="center">

<img src="res/icon/COMrade.svg" alt="COMrade icon" width="128" height="128">

# COMrade

**A native Windows serial port utility.**

[![Build](https://github.com/antoniovazquezblanco/Comrade/actions/workflows/build.yml/badge.svg)](https://github.com/antoniovazquezblanco/Comrade/actions/workflows/build.yml)
[![CodeQL](https://github.com/antoniovazquezblanco/Comrade/actions/workflows/codeql.yml/badge.svg)](https://github.com/antoniovazquezblanco/Comrade/actions/workflows/codeql.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/antoniovazquezblanco/Comrade/badge)](https://scorecard.dev/viewer/?uri=github.com/antoniovazquezblanco/Comrade)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![WinUI](https://img.shields.io/badge/WinUI-3-60CDFF)

</div>


## Building

Requirements:

- Windows 10 version 1809 or later, x64 (the terminal control does not support x86 or ARM64 yet)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Optional: Visual Studio 2026 with the WinUI application development workload

Build and run from the command line:

```powershell
dotnet build COMrade.slnx -c Release -p:Platform=x64
dotnet run --project COMrade
```

Run the unit tests:

```powershell
dotnet test --project COMrade.Core.Tests
```

Or open `COMrade.slnx` in Visual Studio and press <kbd>F5</kbd>.
