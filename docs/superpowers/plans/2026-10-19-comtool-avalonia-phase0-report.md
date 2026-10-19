# COMTool Avalonia Phase 0 report (v0)

Date: 2026-10-19

Branch: `avalonia`

Status: **Continue Phase 0**. The shell and terminal spike are viable; SSH,
serial, ZMODEM and performance gates are still open. No go decision yet.

## Environment

| Item | Result |
| --- | --- |
| SDK | .NET SDK `10.0.402`, installed locally at `/tmp/opencode/dotnet` |
| UI framework | Avalonia `12.1.4` |
| Terminal | `Iciclecreek.Avalonia.Terminal` `4.1.0` / XTerm.NET `3.0.0` |
| Serial | `System.IO.Ports` `10.0.12` (referenced, not yet exercised) |
| SSH | SSH.NET `2026.0.0` (referenced, not yet exercised) |
| Config crypto | `System.Security.Cryptography.ProtectedData` `10.0.12` (referenced) |
| Tests | xUnit, 10 passing on Linux |

## Build and test result

```text
dotnet build COMTool.Avalonia.sln -c Debug  -> 0 warnings, 0 errors
dotnet test  COMTool.Avalonia.sln -c Debug  -> 10 passed, 0 failed
```

The config tests cover:

- Python-compatible JSON round-trip for skin, locale, encoding, serial and SSH
  sessions, including `passwd_enc`.
- Version-mismatch backup before returning defaults.
- Preservation of unknown JSON fields through load/save.
- Separate Python/Avalonia config directories.
- Serial session deduplication, normalized port keys, pruning and upsert.
- SSH key normalization and password retention when metadata changes.

## UI shell

The Phase 0 shell reproduces the current Python window structure:

- 850x500 client area, 35px dark title bar, 6px gap, content margins
  `16,0,10,10`.
- Custom title bar: logo, title, pin, session toggle, minimize/maximize/close.
- 220px `User sessions` sidebar, 3px splitter, list border and empty state.
- Always-visible 10x48 blue left-edge session handle, centered vertically.
- Empty state proportions and 28x28 add button aligned with the Python layout.
- Demo serial/SSH rows with green connected icons and two-line labels.
- Terminal tab shell with settings, close, add, encoding and functional
  controls.

Fixtures:

- `docs/fixtures/ui/python-empty-850x500.png`
- `docs/fixtures/ui/avalonia-empty-850x500.png`
- `docs/fixtures/ui/avalonia-terminal-850x500.png`

The client-area crops are visually aligned at 100% scaling. Remaining shell
differences are font rasterization, the provisional tab bar, and the
placeholder right-side controls.

## Terminal spike

`--terminal` creates an `Iciclecreek.Avalonia.Terminal.TerminalControl` and
launches the local shell through a PTY. On Linux/X11 it:

- Displays a live shell prompt and ANSI-colored prompt segments.
- Uses the Python terminal defaults: background `#212121`, white foreground,
  cursor `#4caf50`, 15px Consolas-first font stack.

Not yet measured or validated:

- 1MB/s and 10MB burst throughput with input responsiveness.
- Keyboard mapping, 256-color palette accuracy and CJK cell widths against
  pyte.
- Window resize and PTY dimension propagation.
- Selection, copy/paste and right-click behavior parity.
- ZMODEM raw-byte interception.

## Packaging

Self-contained single-file publish succeeds:

| Target | Executable | Notes |
| --- | --- | --- |
| `win-x64` | ~53 MB | Launched on Windows 10 host; process remained responsive after 8s |
| `linux-x64` | ~52 MB | Launched under Xvfb and rendered the terminal spike |

PDB/native symbol files are removed from the package (NuGet still copies native
PDBs into the output directory unless they are deleted after publish). The
Windows smoke test was run through a non-interactive SSH session, so no desktop
screenshot or interactive terminal test was possible there. The process itself
started and stayed responsive.

## Compatibility findings

- Avalonia 12 removed `ExtendClientAreaChromeHints`. The shell now uses
  `WindowDecorations="BorderOnly"` and handles dragging/double-click itself.
- Avalonia 12 uses `ZIndex` directly; `Panel.ZIndex` is rejected.
- Material Design Icons PUA glyphs rendered incorrectly for the maximize
  button in this setup. Window controls now use Font Awesome 6 solid glyphs.
- The UI font is platform-selected: Microsoft YaHei on Windows, DejaVu Sans on
  Linux. Inter was not used because the inherited fallback rendered as a serif
  face in this environment.
- The local SDK is not on `PATH`; all commands use the absolute path.

## Open gates

1. Terminal throughput, selection/clipboard, 256-color and CJK validation.
2. ZMODEM byte-stream integration design.
3. Windows serial enumeration and real send/receive.
4. SSH.NET shell, PTY resize, password/key auth and reconnect timing.
5. Python config one-way import and DPAPI token migration.
6. Real tab bar, full settings/theme/language/help UI and dark theme.
7. CI execution on GitHub Actions.
8. Go/no-go review against the P0 parity matrix.

## Recommendation

Continue Phase 0. The terminal and packaging paths are viable enough to justify
the remaining feasibility work, but the project is not ready to start full UI
replication or to switch the default application.
