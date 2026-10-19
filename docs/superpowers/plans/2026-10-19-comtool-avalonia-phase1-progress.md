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
- A failed serial scan preserves remembered sessions; only a successful scan is
  allowed to prune absent ports.
- Serial reads retry `SerialPort` timeouts instead of terminating the terminal
  read loop when the line is idle.
- The tab strip supports multiple tabs with title, selected/unselected styling,
  switching and close buttons. Serial titles use the port name; SSH titles use
  the alias or host.

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

## Still open for Phase 1

- Python-exact layout/spacing for the tab bar and all top-right controls.
- Settings/theme/language/help/about/donation UI and persistence.
- Sidebar context menu (Open/Remove session) and confirmation dialogs.
- Serial port descriptions (Windows SetupAPI/WMI), hotplug UI refresh and a
  Windows serial receive/loopback test.
- SSH disconnect detection and automatic reconnect (currently a stale transport
  is reopened when the session is clicked again).
- Full ZMODEM sender/receiver and `rz`/`sz` transfer tests.
- Windows DPAPI remember-password flow exercised through the UI, not only the
  primitive/smoke tool.
- CI run, DPI checks, 8-hour soak, and the final 1:1 screenshot review.

## Default-version gate

Do not switch the default version. The Python app remains the stable release
until the full Phase 1 parity, ZMODEM transfer, soak/hotplug tests, packaging,
two-week beta and rollback checks are complete.
