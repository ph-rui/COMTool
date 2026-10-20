# Phase 1 session/tab evidence

Early images were captured on Xvfb `:99` at 100% scaling. The current automated
E2E harness selects a free display at `100+` and never uses `:99`. The early
images use an 850x500 client area; the 1176x552 images are the Python reference
geometry. These are functional evidence images, not final pixel baselines.

| File | What it proves |
| --- | --- |
| `ui-chrome-fixed-850x500.png` | Borderless custom window: no native white strip above the title bar, Material Design minimize/maximize/close icons, and no 6px gap below the title bar. |
| `sidebar-auto-hide-850x500.png` | The sidebar auto-collapsed after the pointer moved into the content area; the left hover strip and manual handle remain available. |
| `encoding-focus-850x500.png` | Selecting UTF-8 from the encoding drop-down returns keyboard focus to the terminal; `echo FOCUS-MOUSE` is accepted immediately. |
| `version-focus-0.01-1280x800.png` | The title bar reports `COMTool v0.01`; after maximize, settings close, plugin-menu close, tab click and encoding selection, the terminal still accepts `echo ...` and Enter. |
| `chrome-icons-centered-850x500.png` | The settings gear, plugin plus and functional arrow icons are centered inside their blue/transparent controls instead of sticking to the top-left corner; `echo ALIGN-OK` still works. |
| `window-size-restored-1000x650.png` | The window was resized to 1000x650, closed, and reopened at 1000x650; closing it while maximized still saved the previous normal size instead of the full-screen size. |
| `about-version-0.01-1280x800.png` | The About dialog reports `v0.01` with the Beta config path and existing project links. |
| `sessions-850x500.png` | Config-backed sidebar shows remembered SSH sessions and no tabs after restart. |
| `two-tabs-850x500.png` | Multiple SSH tabs are open; `win-ssh` is selected, `local-ssh` is not. Sidebar status dots reflect open sessions. |
| `switch-tab-850x500.png` | Clicking the `local-ssh` tab switches the terminal content back without reconnecting. |
| `tab-switch-input-second-1000x650.png` | In the packaged build, switching from the first SSH tab to the second and typing `echo PACKAGED_SWITCH_TWO_OK` works immediately; the tab keeps its own live shell and buffer. |
| `tab-switch-input-first-1000x650.png` | Switching back to the first tab and typing `echo PACKAGED_SWITCH_ONE_OK` also works immediately, with the first tab's earlier history intact. This covers both switch directions without clicking the terminal area. |
| `password-dialog-850x500.png` | A password-only SSH session prompts on first open; the password field is masked and "Remember password" defaults on. |
| `windows-ssh-850x500.png` | Password-authenticated SSH to the Windows test host renders the shell output, including CJK wide characters. |
| `password-cache-850x500.png` | Closing `win-ssh` and clicking the remembered sidebar session reopens it without a password prompt (in-memory cache). |
| `context-menu-850x500.png` | Right-clicking a session shows `Open` / `Remove session`. |
| `remove-confirm-850x500.png` | Removing an SSH session asks for confirmation and names the exact session. |
| `session-removed-850x500.png` | Confirming removal updates the sidebar and persists only the remaining session. |
| `dark-theme-850x500.png` | `skin: "dark"` in the Beta config selects the dark resource set on startup. |
| `main-light-1176x552.png` | Main window at the Python reference geometry: 36px tab bar, 9px terminal inset, 30px status bar, hidden native scrollbar. |
| `connection-settings-serial-1176x552.png` | Serial settings panel with Python-matched field widths and editable baud rate. |
| `connection-settings-ssh-1176x552.png` | SSH settings panel with key path, password and remember-password controls. |
| `functional-panel-1176x552.png` | `Load config` / `Share config` functional panel opened from `arrow-right.png`. |
| `titlebar-context-menu-1176x552.png` | Theme selector and About entry on the title-bar context menu. |
| `dark-theme-menu-1176x552.png` | Live dark-theme switch; `skin` persists in the independent Beta config. |
| `moba-keyword-highlighting-1176x552.png` | Error red, success green, warning yellow, IP magenta, and URL underline on a live SSH session; ANSI colors take precedence. |
| `sidebar-hidden-1176x552.png` | Sessions sidebar hidden with its floating reopen handle; `sessionSidebar.visible=false` is persisted. |
| `zmodem-active-850x500.png` | Real `sz` receive over SSH shows the Python-style transfer label, 150px progress bar and 90px Cancel button in the status row. |
| `zmodem-cancelled-850x500.png` | Cancel writes ZCAN, restores the panel and reports `File transfer cancelled`. |
| `zmodem-cancel-input-850x500.png` | Terminal input and the remote shell prompt work again after cancelling a ZMODEM transfer. |
| `terminal-key-mappings-850x500.png` | `cat -v` shows the expected sequences for arrows, Home/End, Insert/Delete and F1-F12, including both SS3 and CSI forms. |
| `terminal-autofocus-ssh-1000x650.png` | Clicking an SSH session immediately focuses the new terminal; `echo PACKAGE_FOCUS` is accepted and executed without clicking the terminal area first. |
| `terminal-single-char-echo-1000x650.png` | Typing a bare `i` over SSH echoes immediately even though `i` is a prefix of the highlight keywords `info`/`invalid`/`important`; the highlighter's pending prefix is flushed after 15 ms instead of waiting for Enter. |
| `session-sidebar-expanded-on-launch-1000x650.png` | The sessions sidebar is expanded on startup even though the test config had `sessionSidebar.visible=false`; the saved width is still applied and auto-hide remains available. |
| `ssh-multiple-connections-1000x650.png` | Clicking the same saved SSH session twice opens two independent `perf-local` tabs; both sidebar and tabs show live connections. |
| `terminal-double-click-maximize-1280x800.png` | Double-clicking the black terminal pane (not the title bar) maximizes the window to 1280x800, and `echo FINAL_PACKAGE_OK` is accepted without refocusing the terminal. |
| `terminal-double-click-restore-1000x650.png` | A second double-click on the terminal pane restores the previous 1000x650 window size and keeps the live shell. |
| `connection-lose-850x500.png` | Killing the SSH child leaves the TCP socket silently closed; the status polling detects EOF and shows `Connection lose` with the sidebar dot disconnected. |
| `ssh-reconnected-850x500.png` | Clicking the remembered session closes the dead tab and reconnects with the cached key, without a password prompt. |
| `tab-detached-850x500.png` | Double-clicking a tab detaches the live terminal into an 850x500 `local-ssh` window; the shell still accepts `echo DETACH-OK` without reconnecting. |
| `tab-reattached-850x500.png` | Closing the detached window returns the same live tab and buffer to the main window; there is no duplicated MOTD, prompt, or second shell. |
| `detached-loss-850x500.png` | The SSH child was killed while its tab was detached; the main window remains responsive and the dead detached session can be reopened from the sidebar. |
| `f11-fullscreen-1280x800.png` | F11 toggles the Python-style full-screen mode: the title-bar row collapses, the custom title bar is hidden and the window covers the 1280x800 display; F11 restores the 850x500 window with the title bar. |
| `about-dialog-850x500.png` | About dialog shows version, description, config path, LGPL license and working GitHub/Releases/Issues/Donate links. |
| `connect-error-850x500.png` | Clicking a remembered SSH session while the server is down shows the Python-style `Error` dialog with `Connect Failed` and the underlying `Connection refused` detail. |
| `dpi-150-1280x800.png` | At 150% X11 scaling (`Xft.dpi=144`) the sidebar, tabs, encoding selector, terminal prompt and status line remain unclipped and readable. |
| `dpi-200-1736x1036.png` | At 200% scaling on a 2560x1600 Xvfb display the 850x500 logical window is 1700x1000 client pixels; title bar, tabs, terminal text and prompt are all crisp and unclipped. |
| `performance-2026-10-20.md` | Startup, terminal parsing, SSH 10 MB throughput, R2R packaging and Native AOT experiment measurements. |

## Automated test-gate coverage (2026-10-20)

`docs/testing/feature-test-matrix.md` now maps all 89 parity rows. The gate
passes in `verify` and `package` phases on Linux, and the structure also passes
for Windows/macOS. The suite is 197 tests:

- Core 41, Connections 37, Terminal 11, ZMODEM 32, TestGate 49, App 27.
- Linux Xvfb E2E: `APP01_SmokeLaunchE2E`,
  `UI02_SidebarExpandsOnLaunchE2E`, `E2E01_OpenSshSession`,
  `SSH06_MultipleConnectionsE2E`, `UI05_TabSwitchTypingBothDirections`,
  `TERM15_DoubleClickMaximizeE2E`.
- Candidate package smoke: `PKG01_CandidateSmoke`.

The images below remain the manual evidence for `WIN-*` items such as the
title-bar/white-strip check (`WIN-01`), DPI (`WIN-02`), ZMODEM dialogs
(`WIN-07`), install/upgrade/uninstall (`WIN-08`), and the final pixel review
(`WIN-18`). Automated tests do not replace those real-desktop checks.

The tab title rule matches Python: serial tabs use the port name, SSH tabs use
the alias when present and the host otherwise. Closing a tab only closes the
live terminal; the sidebar session remains. Clicking a serial session reuses
its one tab, while clicking an SSH session that already has a live connection
opens another independent connection. With no live SSH connection, dead tabs
are discarded and the saved session reconnects.
