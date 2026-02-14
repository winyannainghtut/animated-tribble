# CodexBar for Windows - Agent Implementation Guide

## Overview

CodexBar is a menu bar / system tray application that displays real-time usage statistics for AI coding assistants (OpenAI Codex, Claude Code, Cursor, Gemini, etc.). It shows session limits, weekly quotas, and reset timers without requiring users to log into web dashboards.

**Key value proposition:** One-glance visibility into AI usage limits from the system tray, preventing unexpected quota exhaustion.

---

## Core Features

### 1. Multi-Provider Support
- **15+ AI providers:** Codex, Claude, Cursor, Gemini, Antigravity, Droid, Copilot, z.ai, Kiro, Vertex AI, Augment, Amp, JetBrains AI, etc.
- Each provider has its own:
  - Usage window (5-hour session, weekly, monthly)
  - Quota tracking
  - Reset time display
  - Authentication method

### 2. Visual Display
- **System tray icon** with dynamic two-bar meter:
  - Top bar: Session/5-hour window usage (thicker if credits available)
  - Bottom bar: Weekly/monthly window (hairline)
- **Merge Icons mode:** Combine all providers into one status item with a switcher
- **Dimmed icons** when data is stale or errors occur
- **Status overlays** for provider incidents/outages

### 3. Menu/Popup Window
- Session + weekly usage with reset countdowns
- Credits display (where applicable)
- "Refresh now" manual trigger
- Provider-specific actions (e.g., "Buy Credits" for Codex)
- Settings/preferences access

### 4. Refresh Mechanism
- Configurable intervals: Manual, 1m, 2m, 5m (default), 15m, 30m
- Background refresh off-main thread
- Status polling for provider incidents
- Stale/error state detection

### 5. CLI Integration
- Bundled CLI (`codexbar`) for scripts and CI
- `codexbar usage -p <provider>` - Check usage
- `codexbar cost -p <provider>` - Check local cost usage from logs

---

## Architecture

### Module Structure

```
├── Core/
│   ├── Providers/
│   │   ├── <ProviderID>/
│   │   │   ├── Descriptor.swift    # Provider metadata + fetch pipeline
│   │   │   ├── Strategies.swift    # Fetch implementations
│   │   │   ├── Models.swift        # Data models
│   │   │   └── Parser.swift        # Parsing logic (if needed)
│   ├── UsageFetcher.swift           # Main fetch orchestration
│   ├── ProviderRegistry.swift        # Provider registration
│   └── HostAPIs/                    # Shared capabilities
│       ├── KeychainAPI              # Secure token storage
│       ├── BrowserCookieAPI         # Cookie import
│       ├── PTYAPI                   # CLI interaction
│       ├── HTTPAPI                  # HTTP requests
│       └── StatusAPI                # Status polling
├── UI/
│   ├── SystemTrayController.swift    # Tray icon + menu
│   ├── MainWindow.xaml               # Preferences window
│   ├── IconRenderer.swift            # Two-bar meter rendering
│   └── MenuBuilder.swift            # Dynamic menu construction
├── CLI/
│   └── codexbar.exe                 # Command-line interface
└── Config/
    └── SettingsStore.swift           # Persistence + preferences
```

### Data Flow

```
Background Refresh Loop
         ↓
UsageFetcher.selectProvider()
         ↓
ProviderDescriptor.resolveStrategy()
         ↓
Strategy.fetch() → ProviderFetchResult
         ↓
UsageStore.update(usage, credits, status)
         ↓
UI refresh (icon + menu)
         ↓
CLI state update (if running)
```

### Concurrency Model
- Swift 6 strict concurrency (Sendable state, explicit MainActor hops)
- All fetch operations run off-main thread
- State updates serialized through UsageStore
- UI updates dispatched to main thread

---

## Provider System

### Provider Descriptor (Source of Truth)

Each provider defines:

```swift
struct ProviderDescriptor {
    // Identity
    id: ProviderID
    displayName: String
    sessionLabel: String        // e.g., "5-hour limit"
    weeklyLabel: String        // e.g., "Weekly limit"

    // Capabilities
    supportsCredits: Bool
    supportsTokenCost: Bool
    supportsStatusPolling: Bool
    supportsLogin: Bool

    // Branding
    iconResourceName: String
    primaryColor: Color

    // Authentication methods
    fetchPlan: ProviderFetchPlan

    // UI labels
    toggleTitle: String         // e.g., "Show Codex usage"
    defaultEnabled: Bool
}
```

### Fetch Strategy Pipeline

Each provider supports multiple authentication methods, in priority order:

```swift
struct ProviderFetchStrategy {
    let id: String
    let kind: FetchKind         // .cli, .oauth, .cookies, .apiToken, .localProbe

    // Check if this method is available
    func isAvailable(ctx: ProviderFetchContext) async -> Bool

    // Fetch usage data
    func fetch(ctx: ProviderFetchContext) async throws -> ProviderFetchResult

    // Should we try the next strategy on failure?
    func shouldFallback(on: Error, ctx: ProviderFetchContext) -> Bool
}
```

**Example: Codex provider fallback order**
1. OAuth API (reads `~/.codex/auth.json`)
2. CLI RPC (`codex -s read-only -a untrusted app-server`)
3. CLI PTY (`codex /status`)
4. OpenAI web dashboard (optional, requires cookies)

### Host APIs (Shared Capabilities)

**KeychainAPI** - Secure credential storage
- Read-only access to allowlisted service/account pairs
- On Windows: Use Windows Credential Manager (CredRead/CredWrite)

**BrowserCookieAPI** - Cookie extraction
- Safari (macOS only): `~/Library/Cookies/Cookies.binarycookies`
- Chrome/Edge/Brave: `AppData/Local/Google/Chrome/User Data/*/Cookies`
- Firefox: `AppData/Roaming/Mozilla/Firefox/Profiles/*/cookies.sqlite`
- **Windows DPAPI decryption required** for Chrome/Edge

**PTYAPI** - Interactive CLI sessions
- Run CLI with timeouts and "send on substring" logic
- For Windows: Use `ConPTY` (Console Pseudoconsole)
- Example: Launch `codex`, wait for prompt, send `/status`, parse output

**HTTPAPI** - REST API calls
- URLSession wrapper with domain allowlist
- Standard headers (User-Agent, Authorization)
- Request/response logging for debugging

**StatusAPI** - Incident polling
- Fetch status pages (Statuspage.io, Workspace)
- Cache results to avoid rate limiting
- Badge incidents in UI

---

## Implementation Guide for Windows

### Technology Stack Recommendation

**Option 1: Rust + egui** (as used by Win-CodexBar)
- Pros: Native performance, no runtime dependencies, cross-platform
- Cons: Steeper learning curve for Swift developers

**Option 2: C# + WPF/WinUI 3**
- Pros: Native Windows UI, easy system tray integration, familiar .NET ecosystem
- Cons: Windows-only

**Option 3: Swift + Windows Support**
- Pros: Reuse existing Swift codebase
- Cons: Swift on Windows is still maturing, limited UI frameworks

**Recommendation:** Start with **C# + WinUI 3** or **Rust + egui** for best Windows experience.

### Critical Windows Challenges

#### 1. Browser Cookie Extraction
**Challenge:** Chrome/Edge/Brave cookies are encrypted with DPAPI

**Solution:**
```csharp
// Use CryptUnprotectData (DPAPI)
[DllImport("crypt32.dll", CharSet = CharSet.Auto)]
private static extern bool CryptUnprotectData(
    ref DATA_BLOB pDataIn,
    IntPtr szDataDescr,
    ref DATA_BLOB pOptionalEntropy,
    IntPtr pvReserved,
    IntPtr pPromptStruct,
    uint dwFlags,
    ref DATA_BLOB pDataOut
);

// Chrome cookie path
string chromeCookiePath = Environment.GetFolderPath(
    Environment.SpecialFolder.LocalApplicationData)
    + @"\Google\Chrome\User Data\Default\Cookies";
```

#### 2. System Tray Icon
**Challenge:** Windows requires a specific WPF/WinUI pattern for tray icons

**Solution:**
```csharp
var trayIcon = new NotifyIcon {
    Icon = GenerateIcon(usagePercent),
    Visible = true,
    Text = $"Codex: {usagePercent}%"
};

trayIcon.Click += (s, e) => ShowMenu();
trayIcon.MouseMove += (s, e) => UpdateIcon();
```

#### 3. PTY / Console Pseudoconsole
**Challenge:** Running interactive CLI sessions on Windows

**Solution:** Use `ConPTY` (Windows 10 1809+)
```csharp
[DllImport("kernel32.dll")]
private static extern bool CreatePseudoConsole(
    uint sizeX, uint sizeY,
    IntPtr hInput, IntPtr hOutput,
    uint dwFlags,
    out IntPtr phPC
);

// Or use third-party library: Console.Pty
var pty = new Pty("codex", size: (80, 24));
await pty.Start();
await pty.Write("/status\n");
var output = await pty.ReadUntil(">");
```

#### 4. Keychain Equivalent
**Challenge:** Secure credential storage on Windows

**Solution:** Use Windows Credential Manager
```csharp
[DllImport("advapi32.dll", CharSet = CharSet.Auto)]
private static extern bool CredWrite(
    [In] ref CREDENTIAL credential,
    [In] uint flags
);

// Store token
CredWrite(new CREDENTIAL {
    TargetName = "CodexBar:z.ai",
    UserName = "api-token",
    CredentialBlob = tokenBytes,
    Type = CredentialType.Generic
}, 0);

// Read token
CredRead("CodexBar:z.ai", CredentialType.Generic, 0, out var cred);
```

#### 5. Background Refresh Loop
**Challenge:** Timer-based refresh while app is minimized to tray

**Solution:**
```csharp
var refreshTimer = new DispatcherTimer {
    Interval = TimeSpan.FromMinutes(5)
};
refreshTimer.Tick += async (s, e) => {
    await RefreshAllProviders();
    UpdateTrayIcon();
};
refreshTimer.Start();
```

---

## Provider-Specific Implementation Details

### Codex Provider

**Data sources (priority order):**
1. **OAuth API** (`~/.codex/auth.json` or `%USERPROFILE%\.codex\auth.json`)
   - Endpoint: `https://chatgpt.com/backend-api/wham/usage`
   - Headers: `Authorization: Bearer <token>`
   - Refreshes tokens older than 8 days

2. **CLI RPC** (`codex -s read-only -a untrusted app-server`)
   - JSON-RPC over stdin/stdout:
     ```
     {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientName":"CodexBar"}}
     {"jsonrpc":"2.0","id":2,"method":"account/read"}
     {"jsonrpc":"2.0","id":3,"method":"account/rateLimits/read"}
     ```

3. **CLI PTY Fallback** (`codex /status`)
   - Parse output for:
     - `Credits: <amount>`
     - `5h limit: <percent>% (resets in <time>)`
     - `Weekly limit: <percent>% (resets in <time>)`

**Local cost usage scanning:**
- Parse `~/.codex/sessions/YYYY/MM/DD/*.jsonl` files
- Compute token costs per model
- Cache results (60s minimum refresh)

### Claude Provider

**Data sources (priority order):**
1. **OAuth API** (`claude auth token`)
2. **Browser cookies** (`claude.ai` domain)
3. **CLI PTY** (`claude status`)

### Cursor Provider

**Authentication:** Browser cookies only
- Cookie domain: `cursor.sh`
- Extracts: Plan, usage, billing reset time

### Gemini Provider

**Authentication:** OAuth via gcloud
- Uses `gcloud auth print-access-token`
- Quota endpoint: Google Cloud API

---

## UI Design

### Icon Rendering (Two-Bar Meter)

```
┌──────────────┐
│  ████░░░░░  │  Top bar: 5h session (40% used)
│  ░████░░░░   │  Bottom bar: Weekly (40% used)
└──────────────┘
```

**Implementation:**
- 18x18 template image
- Fill represents percent remaining (configurable to show used instead)
- Dimmed when last refresh failed
- Status overlay (⚠️) on provider incidents

### Menu/Popup Window

```
┌────────────────────────────┐
│ 🟢 Codex                   │
├────────────────────────────┤
│ 5h limit: 40% (2h 15m)    │
│ Weekly limit: 65% (3d 2h) │
│ Credits: $12.50            │
│                           │
│ [Refresh now]              │
│ [Settings...]              │
└────────────────────────────┘
```

**Features:**
- Per-provider toggles in Settings
- Merge Icons mode combines all providers
- "Show usage as used" option
- Account switcher for token-based providers

---

## Testing Strategy

### Unit Tests
- Provider descriptor parsing
- Token cost calculations
- HTML/JSON parsing for web scrapers

### Integration Tests
- CLI RPC/PTY probes
- Cookie import from all browsers
- HTTP API calls (with mocks)

### Manual Testing Checklist
- [ ] All providers fetch data correctly
- [ ] Cookie import works on Chrome/Edge/Firefox
- [ ] Icon updates on refresh
- [ ] Stale data dims icon
- [ ] Manual refresh works
- [ ] Settings persist across restarts
- [ ] CLI commands return correct output

---

## Security Considerations

### Privacy
- **Default to on-device parsing** - no cloud relay
- Browser cookies are opt-in
- No password storage (reuse existing sessions)
- Credential tokens stored securely (Keychain/Windows Credential Manager)

### Cookie Security
- Cookies are read, not written
- Cached cookies have expiration
- No cross-provider credential sharing

### Network Security
- Allowlist domains per provider
- No user-agent spoofing (use honest client identification)
- Timeout all network requests

---

## CLI Reference

```bash
# Check usage for all providers
codexbar usage -p all

# Check specific provider
codexbar usage -p codex
codexbar usage -p claude

# Check local cost usage
codexbar cost -p codex --days 30

# Verbose output (show fetch attempts)
codexbar usage -p codex --verbose
```

---

## Build & Distribution

### Windows Build
```bash
# Rust + egui
cd rust
cargo build --release
# Binary: target/release/codexbar.exe

# Create installer (NSIS or WiX)
makensis installer.nsi
```

### Release Checklist
- [ ] Update version number
- [ ] Test on Windows 10/11
- [ ] Verify all providers work
- [ ] Create signed installer
- [ ] Update GitHub releases
- [ ] Update documentation

---

## Success Criteria

✅ **MVP (Minimum Viable Product):**
- [ ] System tray icon with two-bar meter
- [ ] Support for top 3 providers (Codex, Claude, Cursor)
- [ ] Manual refresh
- [ ] Settings window to enable/disable providers
- [ ] CLI commands (`codexbar usage`)

✅ **Full Feature Parity:**
- [ ] All 15+ providers
- [ ] Automatic refresh (configurable intervals)
- [ ] Browser cookie import
- [ ] Local cost usage scanning
- [ ] Status polling + incident badges
- [ ] Merge Icons mode

---

## Common Pitfalls

1. **Forgetting to handle DPAPI decryption** for Chrome cookies on Windows
2. **Not respecting user timezone** when displaying reset times
3. **Blocking the UI thread** during fetch operations (use async)
4. **Not caching browser cookies** causes excessive disk I/O
5. **Ignoring rate limits** on provider APIs (leads to bans)
6. **Hardcoding paths** - use environment variables (%USERPROFILE%, %APPDATA%)
7. **Not handling CLI updates** - detect when provider CLIs need updates

---

## Next Steps for AI Agent

1. **Choose technology stack** (Rust+egui or C#+WinUI 3)
2. **Implement Host APIs** first (Keychain, Cookies, PTY, HTTP)
3. **Create Provider Registry** and add one provider (Codex) as proof-of-concept
4. **Build system tray integration** with icon rendering
5. **Add remaining providers** by copying the pattern from the first
6. **Implement CLI** for scripting support
7. **Add browser cookie extraction** (hardest part on Windows)
8. **Test thoroughly** across Windows 10/11 and all supported browsers

---

## References

- **Original CodexBar:** https://github.com/steipete/CodexBar
- **Win-CodexBar (Rust port):** https://github.com/Finesssee/Win-CodexBar
- **Provider authoring guide:** https://github.com/steipete/CodexBar/blob/main/docs/provider.md
- **Architecture overview:** https://github.com/steipete/CodexBar/blob/main/docs/architecture.md
