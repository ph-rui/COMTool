# COMTool Avalonia Telnet Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add a minimal usable Telnet session type with IAC negotiation, optional auto-login, saved sessions, sidebar/settings UI, and automated coverage.

**Architecture:** A `TelnetConnection` mirrors the existing TCP client shape: socket -> Telnet codec stream -> receive loop -> `ByteQueueStream` -> `PtyStreamConnection`. Core config adds a separate `TelnetSessionStore`; the app reuses the existing session/sidebar/tab patterns. Auto-login is a receive-side prompt observer that writes through the same Telnet stream.

**Tech Stack:** .NET 10, Avalonia 12, xUnit, existing `COMTool.Connections`, `COMTool.Core`, `COMTool.App` projects.

**Spec:** `docs/superpowers/specs/2026-10-21-comtool-telnet-session-design.md`

## Global Constraints

- Keep the Avalonia and Python config files separate; never read or write the
  Python config for Telnet.
- Persist no plaintext password. Only DPAPI-encrypted `passwd_enc` may be
  written; non-Windows keeps passwords in memory only.
- Keep config version `3` and preserve unknown JSON fields.
- Do not add automatic reconnect, TLS, proxy, or arbitrary login macros.
- Every production change needs an automated test; do not validate UI only by
  screenshots or manual clicking.
- Use display `100+` for Linux E2E; never touch `127.0.0.1:2222`.
- Keep the existing version, executable name, and R2R packaging unchanged.
- Do not push or publish without explicit confirmation.

## Review Focus

- Prompt text split across socket reads: auto-login must still recognize it.
- Username/password prompts containing ANSI color escapes: matching must strip
  escapes before deciding.
- Repeated or echoed prompts: credentials must be sent at most once.
- Servers that never ask for a user name: no credential may be sent.
- Incoming/outgoing byte `0xFF`: it must be escaped/unescaped as Telnet IAC,
  not passed through as terminal data.
- Remote close while the terminal is reading: the reader must reach EOF rather
  than spin.

---

### Task 1: Telnet protocol core and loopback tests

**Files:**
- Create: `avalonia/src/COMTool.Connections/TelnetStream.cs`
- Create: `avalonia/src/COMTool.Connections/TelnetAutoLogin.cs`
- Create: `avalonia/src/COMTool.Connections/TelnetConnection.cs`
- Create: `avalonia/tests/COMTool.Connections.Tests/TelnetConnectionTests.cs`

**Interfaces:**
- `public sealed record TelnetConnectionOptions(string Host, int Port = 23, string? User = null, string? Password = null, int Columns = 100, int Rows = 30, TimeSpan? Timeout = null, string TerminalType = "XTERM-256COLOR")`
- `public sealed class TelnetConnection : IDisposable`
  - `public PtyStreamConnection Pty { get; }`
  - `public bool IsConnected { get; }`
  - `public static Task<TelnetConnection> ConnectAsync(TelnetConnectionOptions options, CancellationToken cancellationToken = default)`
  - `public void Resize(int columns, int rows)`
- `internal sealed class TelnetStream : Stream`
  - `TelnetStream(Stream inner, string terminalType, int columns, int rows)`
  - `void Start()`
  - `void Resize(int columns, int rows)`
  - `void WriteData(ReadOnlySpan<byte> data)`
- `internal sealed class TelnetAutoLogin`
  - `TelnetAutoLogin(string user, string? password, Action<string> send)`
  - `void Observe(ReadOnlySpan<byte> data)`

- [x] **Step 1: Write failing loopback tests**

Add `TelnetConnectionTests` with a `TcpListener` fake server. Cover:
`TELNET01_HandshakeRelaysDataAndTerminalType`,
`TELNET02_NawsTracksResize`, `TELNET03_AutoLoginSendsCredentialsOnce`,
`TELNET04_NoPromptDoesNotSendCredentials`, `TELNET05_CloseEndsTerminalRead`,
and an escaped-IAC assertion.

Use exact protocol bytes in assertions:
`IAC=255, WILL=251, WONT=252, DO=253, DONT=254, SB=250, SE=240`,
`TTYPE=24, NAWS=31, ECHO=1, SGA=3`.

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test avalonia/tests/COMTool.Connections.Tests/COMTool.Connections.Tests.csproj -c Release`
Expected: compile failure because `TelnetConnection` and options do not exist.

- [x] **Step 3: Implement the Telnet codec and connection**

Implement the parser state machine, negotiation responses, TTYPE/NAWS
subnegotiation, IAC escaping, NVT newline normalization, prompt observer, and
receive loop described in the spec. The receive loop must call `Pty.Close()` on
EOF so terminal reads complete.

- [x] **Step 4: Run the connections tests**

Run: `dotnet test avalonia/tests/COMTool.Connections.Tests/COMTool.Connections.Tests.csproj -c Release`
Expected: all tests pass, including the existing TCP/SSH tests.

- [x] **Step 5: Commit**

```bash
git add avalonia/src/COMTool.Connections/TelnetStream.cs \
  avalonia/src/COMTool.Connections/TelnetAutoLogin.cs \
  avalonia/src/COMTool.Connections/TelnetConnection.cs \
  avalonia/tests/COMTool.Connections.Tests/TelnetConnectionTests.cs
git commit -m "feat(avalonia): add telnet protocol connection"
```

### Task 2: Telnet session config and security

**Files:**
- Create: `avalonia/src/COMTool.Core/Config/TelnetSessionStore.cs`
- Modify: `avalonia/src/COMTool.Core/Config/ComtoolConfig.cs`
- Modify: `avalonia/src/COMTool.App/Services/AppConfigService.cs`
- Modify: `avalonia/src/COMTool.Core/Config/SshPasswordCache.cs` (doc comment only)
- Test: `avalonia/tests/COMTool.Core.Tests/SessionStoreTests.cs`
- Test: `avalonia/tests/COMTool.Core.Tests/ConfigTests.cs`

**Interfaces:**
- `TelnetSessionRules.DefaultPort = 23`
- `TelnetSessionRules.Key(string host, int port, string user) -> string`
- `TelnetSessionRules.DisplayName(TelnetSession) -> string`
- `TelnetSessionRules.Detail(TelnetSession) -> string`
- `TelnetSessionRules.ForShare(TelnetSession) -> TelnetSession` (password empty)
- `TelnetSessionStore.Load(ComtoolConfig)`, `Save(ComtoolConfig)`, `Get(string)`,
  `Upsert(string host, int port, string user, string alias = "", string? encryptedPassword = null)`,
  `Remove(string)`, `SortedSessions()`
- `AppConfigService.TelnetSessions`, `RememberTelnetPassword(TelnetSession, string, bool)`

- [x] **Step 1: Write failing config/store tests**

Cover normalized key, invalid port fallback, deduplication, password
preservation on metadata update, `ForShare` stripping the DPAPI token, and
`ComtoolConfig` round-trip of `telnetSessions`.

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test avalonia/tests/COMTool.Core.Tests/COMTool.Core.Tests.csproj -c Release`
Expected: compile failure for missing Telnet types.

- [x] **Step 3: Implement the model, store, and service wiring**

Add `telnetSessions` with `[JsonExtensionData]` support; keep version 3. Wire
the store into `AppConfigService.Load/Save` and add the DPAPI remember method.

- [x] **Step 4: Run the core tests**

Run: `dotnet test avalonia/tests/COMTool.Core.Tests/COMTool.Core.Tests.csproj -c Release`
Expected: all pass.

- [x] **Step 5: Commit**

```bash
git add avalonia/src/COMTool.Core/Config/TelnetSessionStore.cs \
  avalonia/src/COMTool.Core/Config/ComtoolConfig.cs \
  avalonia/src/COMTool.App/Services/AppConfigService.cs \
  avalonia/src/COMTool.Core/Config/SshPasswordCache.cs \
  avalonia/tests/COMTool.Core.Tests/SessionStoreTests.cs \
  avalonia/tests/COMTool.Core.Tests/ConfigTests.cs
git commit -m "feat(avalonia): persist telnet sessions"
```

### Task 3: App routing, sidebar, and settings UI

**Files:**
- Modify: `avalonia/src/COMTool.App/ViewModels/MainViewModel.cs`
- Modify: `avalonia/src/COMTool.App/Views/MainWindow.axaml`
- Modify: `avalonia/src/COMTool.App/Views/MainWindow.axaml.cs`
- Modify: `avalonia/src/COMTool.App/Views/ConnectionSettingsWindow.axaml`
- Modify: `avalonia/src/COMTool.App/Views/ConnectionSettingsWindow.axaml.cs`
- Test: `avalonia/tests/COMTool.App.Tests/NewSessionMenuTests.cs`
- Test: `avalonia/tests/COMTool.App.Tests/TcpSessionAppTests.cs`
- Create: `avalonia/tests/COMTool.App.Tests/TelnetSessionAppTests.cs`

**Interfaces:**
- `SessionKind.Telnet`
- `MainViewModel.LoadSessions(..., IReadOnlySet<string>? connectedTelnet = null)`
- `MainViewModel.UpdateConnectionStates(..., IReadOnlySet<string>? connectedTelnet = null)`
- `ConnectionSettingsWindow` constructor gains
  `Func<string, TerminalTab?, Task> openTelnet` before `showError`.
- Named controls: `TelnetPanel`, `TelnetHostBox`, `TelnetPortBox`,
  `TelnetUserBox`, `TelnetAliasBox`, `TelnetPasswordBox`,
  `TelnetRememberCheck`, `TelnetOpenButton`.

- [x] **Step 1: Write failing headless tests**

Update menu expectations to `Serial, SSH, Telnet, TCP, Local terminal`, add
Telnet to the preselection cases, update TCP index to 3, and add tests for
sidebar loading/connected state, settings save/routing, and credential cache.

- [x] **Step 2: Run the app tests to verify they fail**

Run: `dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release`
Expected: failing menu/preselection assertions and missing Telnet controls.

- [x] **Step 3: Implement the UI and routing**

Add the menu item, settings panel, sidebar section, independent connection
opening, disconnect status, remove handling, and share/load sanitization.

- [x] **Step 4: Run the app tests**

Run: `dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release`
Expected: all pass.

- [x] **Step 5: Commit**

```bash
git add avalonia/src/COMTool.App/ViewModels/MainViewModel.cs \
  avalonia/src/COMTool.App/Views/MainWindow.axaml \
  avalonia/src/COMTool.App/Views/MainWindow.axaml.cs \
  avalonia/src/COMTool.App/Views/ConnectionSettingsWindow.axaml \
  avalonia/src/COMTool.App/Views/ConnectionSettingsWindow.axaml.cs \
  avalonia/tests/COMTool.App.Tests/NewSessionMenuTests.cs \
  avalonia/tests/COMTool.App.Tests/TcpSessionAppTests.cs \
  avalonia/tests/COMTool.App.Tests/TelnetSessionAppTests.cs
git commit -m "feat(avalonia): add telnet session UI"
```

### Task 4: Linux E2E, docs, and full gate

**Files:**
- Modify: `avalonia/tests/e2e/linux/lib.sh`
- Modify: `avalonia/tests/e2e/linux/run-ui-e2e.sh`
- Modify: `docs/parity-matrix.md`
- Modify: `docs/testing/feature-test-matrix.md`
- Maybe modify: `docs/superpowers/specs/2026-10-20-comtool-mobaxterm-session-selector-design.md`

**Interfaces:**
- E2E test id `TELNET01_AutoLoginE2E`
- Seeded config key `telnet:e2e@127.0.0.1:<port>`
- Marker file written by the fake server after receiving the auto-login user.

- [x] **Step 1: Add the fake server and E2E flow**

Use a local Python 3 socket server on `127.0.0.1` with a free high port. It
sends `login:`, reads one line, writes the marker, then keeps the connection
open. Seed only a Telnet session with user `e2e` (no plaintext password).

- [x] **Step 2: Run the focused E2E test**

Run: `bash avalonia/tests/e2e/linux/run-ui-e2e.sh --app <built-app> --filter '^TELNET01_AutoLoginE2E$'`
Expected: `PASS TELNET01_AutoLoginE2E`.

- [x] **Step 3: Run the full gate**

Run: `bash avalonia/pack/verify.sh --results avalonia/publish/test-results/telnet-final`
Expected: all unit/app tests pass, E2E passes, TestGate `PASS`.

- [x] **Step 4: Update docs and commit**

Record the Telnet status/test IDs and commit the E2E and docs.

```bash
git add avalonia/tests/e2e/linux/lib.sh \
  avalonia/tests/e2e/linux/run-ui-e2e.sh \
  docs/parity-matrix.md docs/testing/feature-test-matrix.md \
  docs/superpowers/specs/2026-10-20-comtool-mobaxterm-session-selector-design.md
git commit -m "test(avalonia): cover telnet session end to end"
```

## Post-gate Windows verification (2026-10-21)

The Linux gate cannot exercise Windows DPAPI. After the gate, the `adc338d`
build was deployed to the Windows 10 test host and checked over RDP against a
local fake Telnet server:

- The saved session sent the user name at `login:` and the password only after
  `Password:`; `PING` returned `PONG`.
- **Remember password** wrote a 208-character `passwd_enc`; `config.json`
  contained no plaintext password.
- After restarting the app, clicking the saved session decrypted the DPAPI
  password and logged in without retyping it.

Evidence: `docs/fixtures/phase1/telnet-windows-dpapi-2026-10-21.md`,
`telnet-autologin-windows-1280x800.png`, and
`telnet-remembered-password-restart-windows-1280x800.png`. `TELNET-04` is now
complete; no Python files or Python config were modified.
