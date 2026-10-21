# COMTool Avalonia Beta Windows package

The Avalonia Beta ships as a ReadyToRun self-contained single-file executable
plus two per-user installer scripts. It is deliberately separate from the
Python COMTool app so the stable app, its launcher and its config are never
modified.

## Build

From the repository root:

```powershell
./avalonia/pack/windows/pack.ps1
```

The script publishes:

```text
avalonia/src/COMTool.App/COMTool.App.csproj
```

with `win-x64`, self-contained, single-file, ReadyToRun and compression
disabled, then produces:

```text
avalonia/publish/package/COMTool-Avalonia-Beta-win-x64-0.01.zip
avalonia/publish/package/SHA256SUMS.txt
```

ReadyToRun removes most JIT work during startup; disabling single-file
compression avoids decompressing the large native payload on every launch.
The trade-off is a larger on-disk executable (about 167 MiB) and a larger
zip (about 71 MiB), while the download remains practical because the payload
compresses well. The release startup comparison is recorded in
`docs/fixtures/phase1/performance-2026-10-20.md`.

The zip layout is:

```text
install.ps1
uninstall.ps1
README.txt
app/COMTool.App.exe
```

Use `-SkipPublish` to package an existing `avalonia/publish/win-x64` output.

## Install

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

Defaults:

| Item | Path |
| --- | --- |
| Payload | `%LOCALAPPDATA%\Programs\COMTool Avalonia` |
| Installer support | `%LOCALAPPDATA%\COMTool Avalonia` |
| Beta config | `%APPDATA%\COMTool-Avalonia` |
| Python config (untouched) | `%APPDATA%\comtool` |

The installer is per-user and does not require administrator rights. It:

- stops only a running `COMTool.App.exe` that lives under the target payload;
- stages the new payload and swaps it in, restoring the old payload on failure;
- creates Start Menu and Desktop shortcuts;
- registers `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\COMToolAvaloniaBeta`;
- launches the app unless `-NoLaunch` is supplied;
- never reads or writes the Python config, Python executable or launcher.

Use `-NoShortcuts` to skip shortcuts and `-InstallDir` / `-SupportDir` for a
sandboxed test. Paths are rejected unless they contain `COMTool Avalonia`.

## Uninstall and rollback

Use Settings > Apps > Installed apps, or:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File "$env:LOCALAPPDATA\COMTool Avalonia\uninstall.ps1"
```

The Beta config is kept unless `-RemoveConfig` is passed. Python COMTool remains
installed and remains the default. Rolling back is therefore: uninstall the
Beta, then continue using the existing Python launcher.

The uninstaller removes the Add/Remove entry, shortcuts, payload and the
scheduled support folder. When launched from a non-interactive remote session,
Windows may keep the small `%LOCALAPPDATA%\COMTool Avalonia` support folder
until it is deleted manually; it contains only the uninstall script and
`install.json`.

## Verification evidence

Checked on `zlgmcu\zhangrui` (Windows 10, PowerShell 5.1) with a temporary
sandbox install:

- installer copied the packaged `COMTool.App.exe` to the requested path;
- `install.json` version handling was verified with the previous package; the
  default is now `0.01`;
- `HKCU` uninstall entry contained `DisplayName`, `DisplayVersion`,
  `InstallLocation` and the quoted `UninstallString`;
- shortcuts were skipped with `-NoShortcuts`;
- uninstall removed the payload and registry entry;
- `%APPDATA%\COMTool-Avalonia` and `%APPDATA%\comtool` both still existed after
  uninstall (no `-RemoveConfig`).

The latest app binary (version `0.01`, borderless chrome, sidebar auto-hide on
the first pointer move into the content area, sidebar reopens when the pointer
moves left of the session-list frame's red edge and retracts only after
crossing its green edge, with both trigger points following sidebar resize,
SSH shell exit/tab close followed by a terminal layout resize no longer
accessing the disposed shell stream, MobaXterm-style stopped SSH sessions with
Enter to close, R to restart in place and S to save the full scrollback, sidebar
expanded on every launch, serial
sessions retained across
unplug/replug with open serial tabs reopened automatically, multiple
independent connections from one saved SSH session, PTY-preserving tab
switching with immediate terminal input, non-focus-stealing chrome controls,
centered tool-strip icons, remembered window size, encoding-dropdown focus
return, automatic terminal focus after opening a session, immediate
single-character echo, double-click terminal maximize/restore, a terminal ANSI
palette that matches the Python reference app (including readable blue and
bright-blue prompt/directory colors on `#212121`), a serial sidebar summary
that keeps the device description and adds `115200 8N1` on a third line, a
serial open path that purges stale driver receive/transmit buffers exactly like
the Python app, and a ZMODEM status-row fix
that keeps the 150px progress bar inside its slot and uses the Fluent progress
text so the percentage stays readable on light and dark themes) was built as a
ReadyToRun release and copied to:

- `C:\Users\zhangrui\COMTool-Avalonia.exe` (working copy)
- `D:\Users\zhangrui\Desktop\COMTool-Avalonia.exe` (desktop copy)

- build commit: `adc338d` (deployed 2026-10-21; the previous working-copy
  build was `4b1f1cf`);
- size: 175,952,304 bytes;
- SHA256: `6D6659E3251697A6D2D22ACB4843F8152FBFBDE0F521A078C2C2AC2399B5B213`;
- both remote copies matched that hash via `Get-FileHash`.

The old `COMTool-Avalonia` process was stopped before the copies were replaced.
No `COMTool-Avalonia.exe.bak-*` files were created or retained. The desktop
copy was launched again after deployment in the interactive RDP session and
remained alive/responding (PID 45516, `Responding=True`). The saved COM99
session opened at `115200 8N1` and showed the clean
`root@imx95evk:~/depth#` prompt with no stale binary payload or replacement
characters; the sidebar and terminal evidence are
`docs/fixtures/phase1/serial-sidebar-summary-windows-115200-8n1.png` and
`docs/fixtures/phase1/serial-clean-prompt-windows-after-buffer-fix.png`.
Opening the saved SSH session and running `ls`
on the Windows desktop showed the prompt and directory names in the Python
palette's readable ANSI blue/bright blue instead of XTerm.NET's near-black
defaults; the screenshot is
`docs/fixtures/phase1/terminal-palette-windows-1280x800.png`. A previous build
also passed a real `sz` receive over SSH with the full `win-zm.bin` label and a
readable `22%`; the status-row crop is
`docs/fixtures/phase1/zmodem-active-windows-1280x800.png`.

The `adc338d` desktop copy was then launched in the interactive RDP session and
checked against a local fake Telnet server. It auto-sent the user name at
`login:`, waited for `Password:`, logged in with the remembered password, and
returned `PONG` for terminal input `PING`. The saved `passwd_enc` was 208
characters and `config.json` contained no plaintext password. After a forced
app restart, clicking the saved Telnet session decrypted the DPAPI password and
logged in without retyping it. Evidence:
`docs/fixtures/phase1/telnet-windows-dpapi-2026-10-21.md`,
`telnet-autologin-windows-1280x800.png`, and
`telnet-remembered-password-restart-windows-1280x800.png`. The Python app and
its config were not touched.

This was a direct executable deployment over the existing working and desktop
copies, not a formal packaged release. No new candidate zip was created or
signed off for this build.

The previously packaged release zip is 74,795,557 bytes with SHA256:

```text
b1a301ceac491a5aa707103416c4861a43b86db22552558ab8a715815aafb436
```

That hash is from the earlier packaging run and does not include the stopped
SSH session, terminal palette, serial sidebar summary, stale serial buffer
purge or ZMODEM status-row changes; it changes whenever the app is rebuilt. Linux warm-start
measurements took the packaged app from 1.54 s to 0.66-0.70 s; Windows startup
still needs confirmation on the target desktop.

## Signing

The executable is currently unsigned. Windows SmartScreen may warn on first
run. Code signing is a release-hardening step and must not change the
application/config identity.
