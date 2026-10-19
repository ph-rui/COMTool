# COMTool Avalonia Phase 1 progress

Date: 2026-10-19

Status: **Phase 1 features and packaging implemented; release hardening remains**.
The session sidebar, terminal tab host, serial/SSH transports, ZMODEM, tab
detach and Beta packages are working. Python remains the default release until
the hardware, soak, CI and two-week Beta gates are complete.

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
- Removed the native `BorderOnly` window decoration that left a white strip
  above the custom title bar. The window now uses `WindowDecorations=None`,
  custom 5px edge/corner resize hit areas, and Python-style resize cursors.
- Removed the leftover 6px spacer below the title bar so the body starts
  immediately at the title bar, matching Python.
- Ported the sidebar's 10px left hover strip, auto-show on hover, 250ms
  dx-based auto-hide timer, 350ms hover-open click guard, and non-persisting
  auto-hide. Manual title-bar/handle toggles still persist `sessionSidebar`.
- Replaced the Font Awesome window-button glyphs with the bundled Material
  Design Icons at 13px, including the restore glyph after maximizing.
- F11 now collapses the title-bar grid row as well as hiding the bar, so the
  full-screen body starts at the top edge. `RootGrid` restores the 35px row on
  exit.
- Rebuilt the Beta packages and re-ran the full suite: 84/84 tests pass
  (Core 12, Connections 31, Terminal 11, ZMODEM 30), 0 warnings/0 errors.
- Restored terminal focus when the encoding ComboBox closes. The
  `DropDownClosed` handler posts `FocusSelectedTerminal` at input priority so
  the popup's own focus restoration cannot win. Verified with mouse selection,
  keyboard selection and Escape, followed immediately by
  `echo FOCUS-OK` / `echo FOCUS-MOUSE`.
- Set the Avalonia app and package version to `0.01`. Made title-bar,
  settings, plugin-menu, tab-close, functional and sidebar-handle controls
  non-focusable, and return focus to the selected terminal after
  maximize/restore, settings close, tab close and encoding selection. Verified
  maximize followed by Enter, settings close, plugin-menu close, tab click and
  encoding selection with `echo MAX-ENTER`, `SETTINGS-OK`, `PLUS-OK`,
  `TAB-OK`, `ENC-OK` and `UTF8-RESTORED`.
- Fixed the actual tool-strip display defect: the settings gear, plugin plus
  and functional button inherited top-left content alignment, so their icons
  stuck to the upper-left corner of the blue control. Setting
  `HorizontalContentAlignment`/`VerticalContentAlignment` to `Center` matches
  the Python reference; `echo ALIGN-OK` still works after the change.
- Added user-requested window-size persistence. The Avalonia config now stores
  `window.width`/`window.height`; the saved size is applied before the window is
  shown so it still opens centered, and the last normal size is saved on close.
  Closing while maximized saves the pre-maximize size. Verified 1000x650 was
  restored after close/reopen and after maximizing, closing and reopening;
  config round-trip test added.
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
- Test suite is now 80 passing tests (Core 12, Connections 31, Terminal 8,
  ZMODEM 29) without `lrzsz`; set `LRZSZ_DIR` to enable the two real-transfer
  tests.
- Added the Python-style ZMODEM status controls: transfer label, 150px
  progress bar with percentage, and a 90px Cancel button. Cancel writes ZCAN,
  cleans temporary files, restores terminal input/prompt, and reports
  `File transfer cancelled`. Verified with a 200MB `sz` receive over the local
  SSH test server.
- Verified terminal input mappings with `cat -v`: arrows, Home/End,
  Insert/Delete and F1-F12 emit the xterm sequences expected by the shell.
- Added a pyte golden fixture generated with pyte 0.8.2 at 60x20/LNM and a
  matching XTerm.NET screen test. The same byte stream now checks text,
  wrapping, tabs, erase sequences, cursor addressing, wide characters and
  final cursor position against the Python backend. Unit tests are 84 total
  (Core 12, Connections 31, Terminal 11, ZMODEM 30).
- Made transport EOF a first-class liveness signal (`ZmodemStream.IsInputCompleted`, surfaced through `PtyStreamConnection`) and enabled SSH keepalives. Killing the SSH child now changes the live tab to `Connection lose` on the next poll; clicking the remembered session reconnects with its cached key and no password dialog.
- Added a Windows per-user installer/uninstaller and release packaging under
  `avalonia/pack/windows/`. It installs a separate `COMTool Avalonia Beta`
  identity to LocalAppData, creates shortcuts and an HKCU uninstall entry,
  performs a staged atomic upgrade, and keeps `%APPDATA%\COMTool-Avalonia`.
  Verified install, repeated upgrade, registry values and uninstall on the
  Windows host; `%APPDATA%\comtool` and the Python app were untouched.
- Added a CI packaging step that validates the PowerShell scripts and uploads
  the zip plus SHA256 checksum.

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
- Double-clicked a tab to detach it; ran `echo DETACH-OK` in the detached
  window, then closed the window and reattached the live tab without a second
  SSH connection or duplicated terminal buffer.
- Detached the local `--terminal` tab repeatedly: only one bash child existed,
  the startup hint appeared once, and both pre-detach and post-detach commands
  remained in the scrollback.
- Detached two tabs at once; the second window cascaded 24px instead of hiding
  the first, closing one reattached only its tab, and closing the main window
  removed the remaining detached window and shell.
- Sent 5MB over the local SSH session at 1MB/s (`PERF-DONE`): output completed
  within the 6s screenshot, RSS rose 202MB -> 232MB and then stayed flat, CPU
  returned to ~8% idle, and `echo INPUT-OK` was accepted immediately.
- Fixed the duplicate buffer at its source: `TerminalControl` defaults
  `Process` to `bash`/`cmd.exe` and launches it on load. Every external
  serial/SSH tab now sets `Process = string.Empty`, so a local shell can no
  longer print its MOTD/prompt into the stream-backed terminal when reparented.
- Verified the switched-on local terminal path separately with `--terminal`:
  the app still launches a local `bash` and `echo LOCAL-OK` works.
- Replaced the comma-separated Qt font family with a platform monospace family;
  resize and detach now keep correct column metrics (`CharWidth` ~9px at the
  850x500 window) and clean wrapping.
- Killed the sshd child while a tab was detached; `ShellStream.Closed` marked
  the connection dead, the sidebar dot changed to disconnected, and clicking
  the session closed the dead detached window and reconnected.
- Closed the main window while a tab was detached; both windows and the
  dotnet process exited with no orphan process.
- Pressed F11: the custom title bar and its 35px grid row hid, the body started
  at the top edge, the window covered the 1280x800 display, and a second F11
  restored the 850x500 window with the title bar.
- Verified the borderless window on Xvfb light and dark themes: no native white
  strip, custom bottom-right resize changed 850x500 to 950x550, title-bar drag
  still moved the window, and maximize/restore changed the icon between the MDI
  maximize and restore glyphs.
- Moved the pointer into the sidebar, then into the content area: it collapsed
  after the next 250ms timer tick and the left handle remained. Hovering the
  10px left strip re-opened it; moving out through the left edge kept it open.
  Auto-hide did not change `sessionSidebar.visible`; manual toggles did.
- Opened the About dialog from the title-bar menu; it shows the version,
  config path, LGPL license and GitHub/Releases/Issues/Donate links.
- Stopped the local sshd and clicked `local-ssh`: the UI showed an `Error`
  dialog with `Connect Failed` and `Connection refused`, then reconnected
  normally after the server was restarted.
- Set `Xft.dpi` to 144 (150% scaling) and relaunched: the sidebar, tabs,
  encoding selector, terminal prompt and status line stayed unclipped and
  readable; no source change was needed.
- Repeated the check at 192 DPI (200%) on a 2560x1600 Xvfb screen: the client
  area scaled to 1700x1000 with no clipped title bar, tab strip or terminal
  text.

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
- Republished the latest app after the detach/font/F11 fixes, copied it to
  `C:\Users\zhangrui\comtool-phase1-app.exe` and the Desktop over SSH, verified
  both SHA256 hashes, and started/stopped it without touching the Python
  config.
- Rebuilt the Windows Beta zip from that binary; `app/COMTool.App.exe` inside
  the zip matches the published hash and the package hash is recorded in
  `docs/packaging/avalonia-windows-beta.md`.
- Added a portable `linux-x64` package with per-user install/uninstall scripts.
  The installed app started from the test prefix, created only its own
  `~/.config/comtool-avalonia`, survived a repeated install, and uninstall kept
  the Beta config while leaving the Python app/config untouched.

## Still open for Phase 1

- Terminal byte transcoding for non-UTF-8 encodings; the selector currently
  persists the chosen encoding only.
- Language switching, translated status/context-menu text and plugin
  help/update UI; About with donation links is present.
- DSR/DTR hardware flow control; `System.IO.Ports` has no equivalent mode, so
  the port is left neutral instead of silently enabling RTS/CTS + XON/XOFF.
- Windows serial receive/loopback, hotplug and occupied-port tests on hardware;
  the UI scan/prune logic and Linux PTY path are implemented.
- Serial-port throughput validation (the 1MB/s test used SSH).
- Received-file save dialog on a real desktop; the headless Linux picker hangs
  under Xvfb.
- CI run, 200%/Windows DPI checks, 8-hour soak and the final 1:1 screenshot
  review.
- Two-week small-scope Beta use before any default-version switch.

## Default-version gate

Do not switch the default version. The Python app remains the stable release
until the full Phase 1 parity, ZMODEM transfer, soak/hotplug tests, packaging,
two-week beta and rollback checks are complete.
