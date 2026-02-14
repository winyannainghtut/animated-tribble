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

## Creating a Windows `.exe`

Build a standalone Windows executable locally with:

```bash
dotnet publish CodexBar.Cli/CodexBar.Cli.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o publish
```

The generated executable will be at:

- `publish/CodexBar.Cli.exe`

## GitHub Release Workflow

This repository includes a GitHub Actions workflow at `.github/workflows/release-cli.yml` that:

1. Runs on pushes to tags matching `v*` (for example `v0.1.0`) or via manual dispatch.
2. Restores and publishes the CLI for `win-x64` as a self-contained single file.
3. Uploads the `.exe` and a `.zip` bundle as workflow artifacts.
4. Creates a GitHub Release and attaches those files when the run was triggered by a version tag.

### Triggering a release

```bash
# Example: create and push a version tag
git tag v0.1.0
git push origin v0.1.0
```

After the workflow completes, download `CodexBar.Cli.exe` from the GitHub Release assets.

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
