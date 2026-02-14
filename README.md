# CodexBar for Windows

A system tray application that displays real-time AI coding assistant usage statistics.

## Prerequisites

- .NET 8.0 SDK or later
- Windows 10/11
- OpenAI Codex CLI (for Codex provider)

## Building

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Build release
dotnet build -c Release
```

## Running

### CLI

```bash
# Check usage
dotnet run --project CodexBar.Cli

# Build and run
cd CodexBar.Cli
dotnet run
```

### Windows App (TODO)

The WinUI 3 system tray application is not yet implemented. This is a minimal scaffold with:
- Core models and provider interfaces
- Codex provider stub (returns sample data)
- CLI entry point for testing

## Project Structure

```
CodexBar.sln
├── CodexBar.Core/
│   ├── CodexBar.Core.csproj
│   ├── Models/
│   │   └── ProviderModels.cs
│   ├── Providers/
│   │   ├── IProviderDescriptor.cs
│   │   └── CodexProvider.cs
│   └── HostAPIs/ (TODO)
└── CodexBar.Cli/
    ├── CodexBar.Cli.csproj
    └── Program.cs
```

## Next Steps

To complete the MVP:

1. Implement actual Codex fetching strategies:
   - OAuth API (read `~/.codex/auth.json`)
   - CLI RPC (`codex -s read-only -a untrusted app-server`)
   - CLI PTY fallback (`codex /status`)

2. Add Host API implementations:
   - Windows Credential Manager for token storage
   - HTTP client with allowlist
   - ConPTY wrapper for CLI interaction

3. Implement WinUI 3 app:
   - System tray icon with two-bar meter
   - Dynamic icon rendering based on usage
   - Settings window for provider toggles

4. Add more providers:
   - Claude
   - Cursor
   - Gemini

## References

- [Original CodexBar](https://github.com/steipete/CodexBar)
- [Win-CodexBar](https://github.com/Finesssee/Win-CodexBar)
