# Phase 1 session/tab evidence

Captured on Xvfb `:99` at 100% scaling. The early images use an 850x500
client area; the 1176x552 images are the Python reference geometry. These are
functional evidence images, not final pixel baselines.

| File | What it proves |
| --- | --- |
| `sessions-850x500.png` | Config-backed sidebar shows remembered SSH sessions and no tabs after restart. |
| `two-tabs-850x500.png` | Multiple SSH tabs are open; `win-ssh` is selected, `local-ssh` is not. Sidebar status dots reflect open sessions. |
| `switch-tab-850x500.png` | Clicking the `local-ssh` tab switches the terminal content back without reconnecting. |
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

The tab title rule matches Python: serial tabs use the port name, SSH tabs use
the alias when present and the host otherwise. Closing a tab only closes the
live terminal; the sidebar session remains and clicking it reconnects.
