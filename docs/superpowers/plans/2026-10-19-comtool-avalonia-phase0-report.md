# COMTool Avalonia Phase 0 report (v1)

Date: 2026-10-19

Branch: `avalonia`

Status: **Phase 0 gate passed for the terminal/connection/config architecture.
Go for Phase 1 UI replication. No-go for switching the default version.**

Phase 0 answered the architecture questions. The remaining work is parity
work, not a known architectural blocker. Full ZMODEM transfer and the complete
UI are still Phase 1 gates.

## 1. Summary

| Area | Result | Evidence |
| --- | --- | --- |
| Terminal | Pass | ANSI, 256 colors, CJK/wide characters, selection, copy/paste, resize, 1MB/10MB throughput |
| SSH | Pass | SSH.NET key and password auth, shell output, PTY resize, reconnect |
| Serial | Pass with caveat | Windows COM enumeration/open; Linux PTY read/write; Windows receive needs a hardware loopback |
| ZMODEM | Integration point pass; transfer engine open | Ported CRC/header codec/detector; real `sz` ZRQINIT detected; sender/receiver still to port |
| Config | Pass | One-way Python import, no-overwrite, backup, DPAPI round-trip on Windows |
| Packaging | Pass | Windows self-contained single-file app starts and stays responsive |
| CI | Not executed | Workflow exists; no GitHub Actions run yet |

## 2. Environment

| Item | Result |
| --- | --- |
| SDK | .NET SDK `10.0.402`, installed locally at `/tmp/opencode/dotnet` |
| UI framework | Avalonia `12.1.4` |
| Terminal | `Iciclecreek.Avalonia.Terminal` `4.1.0` / XTerm.NET `3.0.0` |
| Serial | `System.IO.Ports` `10.0.12` |
| SSH | SSH.NET `2026.0.0` |
| Config crypto | `System.Security.Cryptography.ProtectedData` `10.0.12` |
| Test framework | xUnit, 45 passing tests on Linux |

## 3. Verification results

### 3.1 Terminal

The terminal uses `xterm-256color`, `#212121` background, white foreground and
`#4caf50` cursor, matching the Python terminal baseline.

- ANSI SGR colors and 256-color foreground/background were rendered and checked
  both by cell attributes and in the live UI.
  Palette checks: index `196` = `#ff0000`, `21` = `#0000ff`, `15` = `#ffffff`.
- CJK and wide characters occupy two cells; the continuation cell has width 0.
- Keyboard input, command echo and CJK output were exercised in the running
  Avalonia window.
- Left-drag selects, right-click copies the selection and clears it, and
  right-click without a selection pastes. `Ctrl+V`/`Ctrl+Shift+V` paste.
  This matches the Python terminal's clipboard behavior.
- Window resize propagates to the PTY. In the live UI, `stty size` changed from
  `22 41` to `27 50` after resizing the window.
- Scrollback and xterm bracketed-paste state are covered by tests.
- Throughput on Linux: 1 MB in about 70-112 ms; 10 MB in about 232-935 ms
  depending on test parallelism. On Windows: 1 MB in 69.8 ms and 10 MB in
  273.8 ms. The parser remains well above 1 MB/s.

Evidence screenshots: `docs/fixtures/phase0/`.

### 3.2 SSH

- Key authentication was tested against a local OpenSSH server on port 2222.
  First connect took 187 ms; reconnect took 33 ms.
- Password authentication was tested with SSH.NET against the Windows OpenSSH
  server. Connect took 446 ms and shell output was returned.
- Shell output, command execution and PTY resize (`stty size` = `40 100`) were
  validated on Linux.
- Reconnect was exercised by opening a second `SshConnection` and repeating
  shell output and resize checks.
- The implementation uses one `IPtyConnection` adapter around `ShellStream`;
  the terminal control is not specialized for SSH.

### 3.3 Serial

- Windows `SerialPort.GetPortNames()` returned:
  `COM2, COM3, COM4, COM5, COM6, COM23, COM97, COM98, COM99, COM100`.
  The list is naturally sorted (`COM23` after `COM6`, `COM100` last).
- `COM99`, `COM97` and `COM23` opened successfully on the Windows test host.
- Linux `System.IO.Ports` read/write was validated with a `socat` PTY pair:
  bytes written on one side were read on the other. This uses the same
  `PtyStreamConnection` and `TerminalControl.AttachConnection` path planned for
  the real UI.
- A failed scan is represented separately from a successful empty scan, so
  remembered sessions are preserved when enumeration fails.
- Caveat: the Windows host has no internal serial loopback pair. Windows
  open/write and Linux read/write are proven; a Windows receive test needs a
  hardware loopback or `com0com` pair.

### 3.4 ZMODEM

- Ported from the Python implementation:
  - CRC16 (XMODEM) and CRC32 (IEEE) routines.
  - Hex and binary ZMODEM header encoding/parsing.
  - ZDLE decoding and header prefix/CRC validation.
  - `ZmodemDetector` with split-chunk, invalid-CRC, leading/trailing text and
    multiple-star cases.
- A real 24-byte `sz` ZRQINIT capture from `lrzsz 0.12.21` is stored in
  `avalonia/tests/COMTool.Zmodem.Tests/lsz-zrqinit.bin`; the detector returns
  `rz\r` as terminal data and a `receive` detection.
- Remaining work: port `sender.py`, `receiver.py`, `session.py` and `io.py`
  (roughly 900 lines of Python), then run send/receive against `lrzsz`.
  There is no architectural blocker: the source, tests and an `lrzsz` fixture
  are available.
- Until that port lands, ZMODEM is an integration-point pass, not a transfer
  pass.

### 3.5 Config and DPAPI

- `PythonConfigImporter` copies the Python `config.json` to the separate
  Avalonia Beta path only when the Beta file is absent; it never edits the
  source and preserves unknown fields byte-for-byte.
- Existing Beta config is not overwritten by default. Explicit overwrite first
  creates a timestamped backup.
- Missing, malformed and unsupported-version sources are rejected without
  touching the Beta config.
- On Windows, DPAPI protected a test secret to a 228-character token and
  decrypted it back exactly. On non-Windows, protect/unprotect return empty,
  matching the Python `secure_store` behavior.
- The per-process SSH password cache exists and is tested; wiring the full
  auth order into the settings UI remains Phase 1.

### 3.6 Packaging and CI

- Windows `win-x64` self-contained single-file publish: 53 MB.
- The published app started on the Windows test host, stayed responsive, and
  used about 258 MB working set in a non-interactive session 0 process.
  No interactive desktop screenshot was possible from that session.
- The Phase 0 smoke executable is 48 MB self-contained and passed DPAPI,
  config import, terminal benchmark and serial enumeration on Windows.
- Linux publish from the earlier Phase 0 run remains runnable under Xvfb.
- `.github/workflows/avalonia.yml` exists with path filtering and Windows
  artifact publishing, but the workflow has not been executed on GitHub.

## 4. Automated tests

```text
dotnet build COMTool.Avalonia.sln -c Release  -> 0 warnings, 0 errors
dotnet test  COMTool.Avalonia.sln -c Release  -> 45 passed, 0 failed
```

Breakdown:

- Core: 10
- Connections: 18
- Terminal: 8
- ZMODEM: 9

The connection suite includes a real Linux `socat` PTY read/write test and is
skipped only when `/usr/bin/socat` is unavailable. The ZMODEM suite includes a
real `lrzsz` header fixture. The Windows DPAPI check is run through
`tools/COMTool.Phase0Smoke`.

## 5. Open gates before a default switch

These do not block starting Phase 1, but they do block switching defaults:

1. Port the full ZMODEM sender/receiver and pass send/receive with `lrzsz`.
2. Test Windows serial receive through a hardware loopback or `com0com`.
3. Add Windows port descriptions (SetupAPI/WMI) and verify natural ordering in
   the sidebar.
4. Complete 1:1 UI parity: settings, theme, language, help, tab behavior,
   sidebar menus, status messages and all key/mouse mappings.
5. Wire first-run Python config import, password cache, DPAPI and auth order
   into the real UI.
6. Execute the GitHub Actions workflow and publish artifacts from CI.
7. Run hotplug, reconnect, ZMODEM, 8-hour soak, memory and performance tests.
8. Keep the Python stable version available for rollback for at least two
   weeks of beta.

## 6. Decision

- **GO** for Phase 1: one-to-one Avalonia UI replication and parity work.
- **NO-GO** for changing the default version or removing the Python fallback.
- ZMODEM transfer is the highest-risk remaining Phase 0 item; schedule the
  sender/receiver port before UI parity is declared complete.

## 7. Next steps

1. Replace the provisional terminal tab host with the real tab bar and session
   lifecycle.
2. Implement settings/theme/language/help UI against the Python baseline.
3. Port ZMODEM sender/receiver/session and run `sz`/`rz` against `lrzsz`.
4. Wire serial/SSH sessions to the sidebar and `TerminalControl` using the
   `IPtyConnection` adapters.
5. Add the first-run Python config import and credential resolver.
6. Run CI and update this report with the CI run and the full transfer result.
