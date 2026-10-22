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

A later direct deployment on 2026-10-21 replaced both copies with commit
`7d2f34e`, which adds the square deep-blue title-bar/window icon, the
MobaXterm-style new-session type icons, and broader Telnet login-prompt
matching (`User name:`, `Login ID:`, `Account name:`, `Please enter ...`,
`>>`-prefixed and Chinese prompts):

- size: 174,596,528 bytes;
- SHA256: `A19A0BEEA9CB1DA61679E6AA9857A55FA9A357E8D2EFA4BB31861E0946252EC7`;
- both remote copies matched that hash via `Get-FileHash`;
- no `COMTool-Avalonia.exe.bak-*` files were created, and the old process was
  not running when the copies were replaced;
- a Windows process smoke test started the deployed working copy from the SSH
  session, observed it `Running` and `Responding=True` for six seconds, then
  stopped it; this does not replace the interactive GUI check;
- automated Linux Xvfb and headless checks pass, while the Windows GUI visual
  check and a real-device Telnet prompt check for this build are still pending.

A follow-up direct deployment on 2026-10-21 replaced both copies with commit
`6cd4366`, which recognizes Telnet login prompts with a host-name prefix such
as `SonnePower_070_Ax login:`, adds an `Edit session` sidebar action that
populates saved host/port/user settings, removes the bright icon rim, and
reduces the title-bar icon to 28px:

- size: 174,571,952 bytes;
- SHA256: `804938A568D45B6D8812A872B42273FF099EE3D2F23232F61C41B1F3BA73722C`;
- both remote copies matched that hash via `Get-FileHash`;
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it; this does not replace the
  interactive GUI check;
- the `SonnePower_070_Ax login:` prompt is covered by the automated
  `TELNET03_CommonLoginPromptVariantsTriggerAutoLogin` test, but a retest
  against the user's real Telnet device is still required;
- Windows visual confirmation of the 28px rim-free title-bar icon and the
  `Edit session` menu remains pending.

A later direct deployment on 2026-10-21 replaced both copies with commit
`a7f7229`, which moves the new-session `+` button from the top toolbar into the
`User sessions` sidebar header and removes the Functional arrow button and its
`Load config` / `Share config` panel:

- size: 174,543,280 bytes;
- SHA256: `D7C4CB201BBE1CFBA5DC248CBA7148F766536CEBA47428843710885C2443CB56`;
- both remote copies matched that hash via `Get-FileHash`;
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it; this does not replace the
  interactive GUI check;
- the automated suite passes 234 tests, including the sidebar-entry and
  Functional-panel-removal checks, and Linux Xvfb E2E passes 10/10;
- the Linux visual fixture is
  `docs/fixtures/phase1/sidebar-add-session-850x500.png`; interactive Windows
  visual confirmation of the sidebar `+` and removed Functional panel remains
  pending.

A follow-up direct deployment on 2026-10-21 replaced both copies with commit
`c7b4bc4`, which keeps the sidebar open while the pointer is over its new-session
`+` or the open new-session menu, and moves the connection-settings gear into
the selected tab so it is clear which tab it edits:

- size: 174,551,472 bytes;
- SHA256: `9C0C6D6930F02171282096A3DEA8B17183E48BB2A82ED1AA58B7944EE4D9484B`;
- both remote copies matched that hash via `Get-FileHash`;
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it; this does not replace the
  interactive GUI check;
- the automated suite passes 236 tests and Linux Xvfb E2E passes 10/10;
- the Linux visual fixtures are
  `docs/fixtures/phase1/sidebar-add-session-menu-stays-open-850x500.png` and
  `docs/fixtures/phase1/tab-settings-gear-850x500.png`; interactive Windows
  visual confirmation of the hover/menu behavior and per-tab gear remains
  pending.

A follow-up direct deployment on 2026-10-21 replaced both copies with commit
`9c37848`, which keeps the selected tab uncluttered at rest and reveals its
connection-settings gear only while the pointer is over that selected tab;
unselected tabs never show the gear:

- size: 174,555,568 bytes;
- SHA256: `0A2F1E2CCD022407F5F063155E47DFD1C7191BB7FD6FC5F9D3273FE2F02FEF77`;
- both remote copies matched that hash via `Get-FileHash`;
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it; this does not replace the
  interactive GUI check;
- the automated suite passes 236 tests and Linux Xvfb E2E passes 10/10;
- the Linux visual fixtures are
  `docs/fixtures/phase1/tab-settings-gear-at-rest-850x500.png` and
  `docs/fixtures/phase1/tab-settings-gear-850x500.png`; interactive Windows
  confirmation of the hover-only gear remains pending.

A follow-up direct deployment on 2026-10-21 replaced both copies with commit
`f51ebad`, which gives immediate sidebar feedback when opening a session:
the clicked row shows a blue `Connecting...` state, repeated clicks while the
attempt is pending are ignored, and SSH connection setup runs off the UI
thread so an unreachable host no longer freezes the window:

- size: 174,567,856 bytes;
- SHA256: `DA36A898E2B8C122D1EAD45018F6BBD6324FBB1D0D0077BB8D0EDBCCCDEF9DA8`;
- both remote copies matched that hash via `Get-FileHash`;
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it; this does not replace the
  interactive GUI check;
- the automated suite passes 237 tests and Linux Xvfb E2E passes 10/10;
- the Linux visual fixture is
  `docs/fixtures/phase1/session-connecting-feedback-1000x650.png`; interactive
  Windows confirmation against the unreachable `192.168.1.12` Telnet session
  remains pending.

This was a direct executable deployment over the existing working and desktop
copies, not a formal packaged release. No new candidate zip was created or
signed off for this build.

A fourth direct deployment on 2026-10-21 replaced both copies with commit
`7ee78b7`, which widens the gap between the hovered connection-settings gear
and the tab close button from 8px to 16px so the two icons no longer look
glued together:

- size: 174,584,240 bytes;
- SHA256: `1E31AAD8C4F437B048DFE1AE7F763F10D73B56BAA8FCB86D5DE34B883A21634B`;
- both remote copies matched that hash via `Get-FileHash`;
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it; this does not replace the
  interactive GUI check;
- the automated suite passes 336 tests and Linux Xvfb E2E passes 12/12;
- the headless layout regression
  `SessionEditTests.UI08_TabSettingsGearKeepsClearanceFromCloseButton` measures
  the real button gap, and the cropped screenshot
  `docs/fixtures/phase1/tab-gear-close-spacing.png` shows the result; the
  interactive Windows check of the tab strip remains pending.

This was a direct executable deployment over the existing working and desktop
copies, not a formal packaged release. No new candidate zip was created or
signed off for this build.

A third direct deployment on 2026-10-21 replaced both copies with commit
`8261fff`, which gives Telnet sessions the same MobaXterm-style stopped
screen as SSH: a dropped connection shows `Session stopped` with the
`<return>`/`R`/`S` prompt, `R` reconnects inside the same tab with the saved
host, port, user and password, and a failed restart keeps the stopped screen
so `R` can be pressed again:

- size: 174,584,240 bytes;
- SHA256: `D76F57045E764E48F1CE77C94D504C3519086EE9D07864D46A1415FD221FB07D`;
- both remote copies matched that hash via `Get-FileHash`;
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it; this does not replace the
  interactive GUI check;
- the automated suite passes 241 tests and Linux Xvfb E2E passes 11/11
  (including the new `TELNET02_RestartStoppedSessionE2E`);
- the Linux visual fixtures are
  `docs/fixtures/phase1/telnet-stopped-screen-1000x650.png` and
  `docs/fixtures/phase1/telnet-restarted-after-r-1000x650.png`; the
  interactive Windows check of the stopped screen and the `R` reconnect
  against a real Telnet device remains pending.

This was a direct executable deployment over the existing working and desktop
copies, not a formal packaged release. No new candidate zip was created or
signed off for this build.

A later direct deployment on 2026-10-22 replaced both copies with commit
`f2c8d75`, which adds the embedded SFTP drawer to SSH session tabs (dual
remote/local panes, transfer queue, conflict prompt, per-session collapsed
state and height) and disables the upload/download buttons while a transfer is
running:

- size: 175,129,420 bytes;
- SHA256: `DFC7ED2E861D047CB65DD2DE90F53C74483692854AB74CA3A858AB10D33C8218`;
- deployed over SSH (`scp` to `C:\Users\zhangrui\comtool-win-build\COMTool.App-sftp.exe`
  plus a PowerShell deploy script); both remote copies matched the source hash
  via `Get-FileHash` (`WORK_SHA256` = `DESKTOP_SHA256` = source hash);
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it, and left no running
  instances; this does not replace the interactive GUI check;
- the automated suite passes 336 tests (App 104, including the SFTP drawer,
  transfer-engine and transfer-guard tests) and Linux Xvfb E2E passes 12/12
  (including `SFTP01_BrowserTransferE2E`);
- interactive Windows confirmation of the SFTP drawer against a real SSH server
  (`WIN-23` in `docs/testing/manual-acceptance.md`) remains pending.

A follow-up direct deployment on 2026-10-22 replaced both copies with commit
`4e2b6cc`, which keeps the SFTP browser in a folded left panel instead of a
bottom drawer: the panel starts folded away, the folder button of the tab
strip opens it on the left of the terminal, the remote list sits above the
local list, the width (default 420, draggable 320..640) is kept per session,
and the sftp subsystem is requested the first time the panel is opened:

- size: 175,137,612 bytes;
- SHA256: `04E8471F3E229B0E357503A62626AAE918B0406D3C74AB5126148FE6A90F5117`;
- both remote copies matched that hash via `Get-FileHash`
  (`WORK_SHA256` = `DESKTOP_SHA256` = source hash);
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it;
- the automated suite passes 341 tests (App 109) and Linux Xvfb E2E passes
  12/12, including `SFTP01_BrowserTransferE2E` with the new left-panel
  coordinates and the button click that opens the panel;
- the Linux fixture `docs/fixtures/phase1/sftp-panel-1000x650.png` replaces
  the old drawer screenshot; interactive Windows confirmation of the folded
  default and the drag handle (`WIN-23`) remains pending.

This was a direct executable deployment over the existing working and desktop
copies, not a formal packaged release. No new candidate zip was created or
signed off for this build.

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
