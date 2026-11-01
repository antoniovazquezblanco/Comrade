# Security policy

## Reporting a vulnerability

Please report security problems privately, not in a public issue:
[report a vulnerability](https://github.com/antoniovazquezblanco/Comrade/security/advisories/new)
through GitHub's private vulnerability reporting.

Include what you found, the COMrade version, and the steps or files that show it.
Once a fix is released, the advisory is published with credit to you, unless you prefer otherwise.

## Supported versions

Fixes go into the next release. Only the
[latest release](https://github.com/antoniovazquezblanco/Comrade/releases/latest) is supported:
please update before reporting.

## Scope

- The COMrade app, its installer and its release files.
- How COMrade handles data received from serial devices, its settings files and its log files.
- The build and release workflows of this repository.

Problems in the serial drivers or in Windows itself are out of scope.
Please report those to their vendors.

## Checking a download

Every release file comes with a Sigstore signature (`.sigstore.json`) and the release has SLSA build provenance (`.intoto.jsonl`), both made by the release workflow.
To check that a file was built from this repository by its workflow, for example with [slsa-verifier](https://github.com/slsa-framework/slsa-verifier):

```powershell
slsa-verifier verify-artifact COMrade-0.3.0-win-x64.msi `
  --provenance-path COMrade-v0.3.0.intoto.jsonl `
  --source-uri github.com/antoniovazquezblanco/Comrade `
  --source-tag v0.3.0
```
