# Contributing to CaptureFlow

CaptureFlow's original code and documentation are available under the [MIT License](../LICENSE). Third-party materials retain their own terms. Contributions should be yours to submit under MIT; preserve attribution and discuss large changes first. No additional CLA or DCO is imposed by this guide.

## Reports and changes

Use [GitHub Issues](https://github.com/Janith2002/CaptureFlow/issues) for non-sensitive bugs, documentation corrections and workflow suggestions. Include build/Windows version, mode, reproduction steps, expected result and observed result. Use synthetic media and redact private paths/identifiers. Never attach credentials, personal recordings or projects. No private vulnerability-reporting channel is approved; avoid sensitive exploit details in public issues.

Keep changes focused and verify them on Windows with .NET 10 SDK:

```powershell
dotnet build CaptureFlow.slnx
dotnet test CaptureFlow.slnx
```

Preserve user originals, keep automated and human/device evidence separate, and use deterministic fakes/generated media in tests. Do not initiate physical capture automatically. Do not commit bin/obj, caches, recordings, projects, databases, logs, private diagnostics or signing credentials. Use your GitHub noreply identity when privacy is important.

See [architecture](ARCHITECTURE.md), [installation](INSTALLATION.md), [privacy](PRIVACY.md) and [license scope](SOURCE-CODE-POLICY.md). Installer/binary publication requires separate approval.
