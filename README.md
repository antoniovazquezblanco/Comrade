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

<p align="center">
  <img src="docs/images/port-list.png" alt="COMrade listing the serial ports of the machine" width="800">
</p>

COMrade opens serial ports in tabs with a fast, modern terminal, and keeps working when a device is
unplugged, resets itself or comes back under a different COM number.

### Keyboard shortcuts

| Keys | Action |
|---|---|
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>T</kbd> | Open a port in a new tab |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>W</kbd> | Close the tab |
| <kbd>Ctrl</kbd>+<kbd>Tab</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Tab</kbd> | Next / previous tab |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>C</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> | Copy / paste |
| <kbd>Ctrl</kbd>+<kbd>,</kbd> | Settings |

<kbd>Ctrl</kbd>+<kbd>C</kbd> copies when text is selected and otherwise reaches the device, like the
other control keys devices use.

## Download

From the [latest release](https://github.com/antoniovazquezblanco/Comrade/releases/latest), get either:

- `COMrade-<version>-win-x64.msi`: Installs COMrade with a Start menu entry, either just for you
  (no administrator rights needed) or for every user of the computer. A newer version installs over
  it and keeps your settings.
- `COMrade-<version>-win-x64.zip`: A portable copy. Unzip it anywhere and run `COMrade.exe`.

Both include .NET and the Windows App SDK. Each file has a Sigstore signature (`.sigstore.json`) made
by the release workflow, and each release has SLSA build provenance (`.intoto.jsonl`); see
[SECURITY.md](SECURITY.md#checking-a-download) for how to check a download.

Requirements: Windows 10 version 1809 or later, x64.

## Building

See [docs/development.md](docs/development.md) for building, testing and releasing COMrade.
