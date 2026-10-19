# COMTool Avalonia Phase 1 progress

Date: 2026-10-19

Status: **in progress**. The session sidebar and terminal tab host now use the
real Avalonia Beta config and real serial/SSH transports. The Python version is
still the default release.

## Implemented in this slice

- `AppConfigService` loads the independent `COMTool-Avalonia` config, performs
  the one-way Python import when the Beta file is absent, owns the serial/SSH
  session stores and the SSH password cache, and saves changes.
- Startup shows the sidebar only. No tabs are restored, matching the Python
  behavior requested for the Avalonia Beta.
- Session sidebar items now carry a stable session id and kind. Tapping an item
  opens a terminal tab, or selects the already-open tab.
- Serial sessions call `SerialConnection.Open`; SSH sessions use key auth or the
  credential order:
  1. explicit key configured on the session;
  2. in-memory password cache;
  3. Windows DPAPI `passwd_enc`;
  4. password dialog.
- SSH password dialog masks input and offers "Remember password" (default on).
  On Windows a successful remember writes only `passwd_enc` via DPAPI. On
  non-Windows the password remains in the process cache only.
- Closing a tab closes the live transport but leaves the remembered session in
  the sidebar. Clicking it reconnects. Closing/reopening a password session does
  not prompt again in the same process.
- Connected sidebar dots are derived from open tabs and their transport state.
- Right-clicking a sidebar row offers `Open` and `Remove session`. Removal asks
  for confirmation, updates the Beta config and leaves any open tab running,
  matching Python.
- The `skin` field selects the light or dark Avalonia resource set at startup.
  The dark palette follows the Python dark QSS for window, sidebar and tabs.
- A failed serial scan preserves remembered sessions; only a successful scan is
  allowed to prune absent ports.
- Serial reads retry `SerialPort` timeouts instead of terminating the terminal
  read loop when the line is idle.
- The tab strip supports multiple tabs with title, selected/unselected styling,
  switching and close buttons. Serial titles use the port name; SSH titles use
  the alias or host.

- Added the Python-exact main-window slice: 36px tab bar, no-gap 32x32
  add button, 97x31 encoding selector, `arrow-right.png` functional button,
  9px terminal inset, 6px gap and 30px status row.
- Added the non-modal-equivalent connection settings window with serial and SSH
  fields, validated port/baud/parity/stop-bit/flow values, key-file browsing,
  password remember, session upsert and immediate open/reconnect.
- Added SetupAPI friendly-name/device-description discovery for Windows COM
  ports with a parser unit test; Windows `--ports` now prints full descriptions.
- Added in-place sidebar connection-state updates and persisted sidebar
  `visible`/`width` values (`sessionSidebar`, clamped to 160-320px). The
  hide/show toggle and splitter drag were exercised live and saved correctly.
- Added the functional panel with `Load config` and `Share config`. Sharing an
  SSH session copies non-secret fields and never clears the remembered token.
- Added status text/colors for connected, disconnected, ZMODEM progress,
  success and error states, including transfer rate and ETA. The terminal's
  native scrollbar is hidden to match Python while scrollback remains available.
- Added a hidden theme selector and About dialog on the title-bar context menu.
- Added `run.log` with 2 MB rotation in the independent Beta config directory.
- Added MobaXterm-style plain-output keyword highlighting with ANSI
  foreground/background precedence, URL underlining, UTF-8-safe chunking and
  keyword phrases split across stream chunks.
- Ported the full ZMODEM engine: ZDLE subpackets, CRC16/32, offsets, file
  metadata, sender, receiver, resumable offsets/cancel handling, and a stream
  interceptor that automatically detects `rz`/`sz`.
- ZMODEM now routes serial/SSH bytes through `ZmodemStream`, auto-starts
  receive sessions, prompts for outgoing files, saves received files through
  the Avalonia picker, suppresses terminal input during a session, and restores
  the shell prompt/trailing bytes afterward.
- Added simulated end-to-end ZMODEM tests in both directions, subpacket/offset
  round-trips, keyword highlighting tests, and an opt-in real-lrzsz integration
  suite. Real `sz` receive and `rz` send of 64KB binary files now pass through
  the production `PtyStreamConnection`/`ZmodemStream` path.
- Test suite is now 78 passing tests (Core 12, Connections 31, Terminal 8,
  ZMODEM 27) without `lrzsz`; set `LRZSZ_DIR` to enable the two real-transfer
  tests.

## Evidence

Screenshots and descriptions: `docs/fixtures/phase1/`.

Validated live in the UI under Xvfb:

- Imported a Python config into the Beta directory with unknown fields intact.
- Started with an SSH session present and no tabs.
- Opened a key-authenticated SSH session to local OpenSSH.
- Ran `printf 'UI-SSH-OK\n'; stty size`; terminal returned `UI-SSH-OK` and
  `30 100`.
- Opened a password-authenticated SSH session to the Windows test host; the
  Windows shell banner, CJK characters and prompt rendered.
- Closed the Windows tab, clicked the remembered session again, and reconnected
  without a password prompt.
- Opened two tabs and switched between them.
- Closed a tab; the sidebar session remained.

Validated headlessly on the Windows test host over SSH (no RDP used):

- Seeded `%APPDATA%\COMTool-Avalonia\config.json` with a serial session and
  an SSH session whose `passwd_enc` was produced by Windows DPAPI.
- `COMTool.Phase0Smoke --stored-ssh` read the Beta config, decrypted the
  208-character DPAPI token, connected to Windows OpenSSH and verified shell
  output.
- `COMTool.Phase0Smoke --stored-serial` read the Beta config and opened the
  configured `COM97` port.
- The published single-file app started from that config, stayed responsive at
  193 MB working set, and did not alter the stored serial/SSH entries.
- `--ports` returned `COM97 USB-Enhanced-SERIAL-B CH344`, `COM99
  USB-Enhanced-SERIAL-A CH344` and the other expected natural-sorted devices.
- The published executable embeds `Assets/logo.ico`; PowerShell
  `ExtractAssociatedIcon` returns a 32x32 icon.

## Still open for Phase 1

- Terminal byte transcoding for non-UTF-8 encodings; the selector currently
  persists the chosen encoding only.
- Language switching and complete help/donation/update UI; About is present.
- Tab icon/detach behavior and the remaining Python keyboard shortcuts.
- Serial auto-reconnect, SSH disconnect detection/automatic reconnect, and
  translated status/context-menu text.
- Windows serial receive/loopback on hardware.
- Large-file (100MB+) ZMODEM soak and serial-port throughput validation.
- CI run, DPI checks, 8-hour soak, and the final 1:1 screenshot review.

## Default-version gate

Do not switch the default version. The Python app remains the stable release
until the full Phase 1 parity, ZMODEM transfer, soak/hotplug tests, packaging,
two-week beta and rollback checks are complete.
