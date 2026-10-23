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

A follow-up direct deployment on 2026-10-22 replaced both copies with commit
`f2a5119`, which redesigned the SFTP panel to match MobaXterm: a single remote
list (no local pane), an icon-only toolbar with Chinese hover tooltips,
Chinese sortable column headers (名称 / 大小 (KB) / 最后修改 / 所有权限,
directories always first), an inline prompt row for new-folder/rename, and
picker-driven transfers (system file dialog for upload, folder dialog for
download):

- size: 175,141,708 bytes;
- SHA256: `8E413C1A94A791FDE68CE83E14BB494EE5C82943927551B82E71090A019C7CE0`;
- both remote copies matched that hash via `Get-FileHash`
  (`WORK_SHA256` = `DESKTOP_SHA256` = source hash);
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it;
- the automated suite passes 349 tests (App 117) and Linux Xvfb E2E passes
  12/12; `SFTP01_BrowserTransferE2E` now drives the upload and download
  through the scripted pickers (`COMTOOL_E2E_PICK_FILES` /
  `COMTOOL_E2E_PICK_FOLDER`) and waits for the path box's focus ring before
  typing, so the keystrokes can no longer leak into the terminal;
- the Linux fixture `docs/fixtures/phase1/sftp-pane-single.png` supersedes
  `sftp-panel-1000x650.png` (the dual-pane layout); interactive Windows
  confirmation of the icon toolbar and Chinese tooltips (`WIN-23`) remains
  pending.

This was a direct executable deployment over the existing working and desktop
copies, not a formal packaged release. No new candidate zip was created or
signed off for this build.

A follow-up direct deployment on 2026-10-22 replaced both copies with commit
`4bb8ac5`, addressing the user's review of the single-pane panel on Windows:
the inline prompt row (new folder / rename) is now a loud full-width blue bar
with a bold label and 确定/取消 buttons instead of a subtle strip, and the
tab-strip toggle is now a self-explaining "SFTP" chip (folder icon + label,
matching the encoding combo's height) with Chinese tooltips (展开/收起 SFTP 面板):

- size: 175,141,708 bytes;
- SHA256: `C8B9BFE09268E59D3F5DB28EE7F41E9C6F02B3A7DCA2145B8C21E7A3254A4CF9`;
- both remote copies matched that hash via `Get-FileHash`
  (`WORK_SHA256` = `DESKTOP_SHA256` = source hash);
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it;
- the automated suite passes 349 tests (App 117) and Linux Xvfb E2E passes
  13/13; the new `SFTP02_FileManagementE2E` drives + (new folder) / rename /
  two-step delete through the real UI against the fixture sshd;
- the toggle chip moved left in the tab strip (e2e coordinate 873 -> 858);
  interactive Windows confirmation of the chip and the prompt row
  (`WIN-23`) remains pending.

This was a direct executable deployment over the existing working and desktop
copies, not a formal packaged release. No new candidate zip was created or
signed off for this build.

A follow-up direct deployment on 2026-10-22 replaced both copies with commit
`5230b76`, addressing two user requests from annotated screenshots:
(1) the per-tab connection-settings gear is gone (each tab keeps only its
status dot, title and close button; connection settings stay reachable via
the sidebar right-click `Edit session`), and (2) pressing R on a stopped
session now shows an indeterminate "Restarting..." progress bar in the status
row while reconnecting; when the attempt still fails, a "重连失败" dialog asks
继续重启 (retry) / 不重连 (give up) — retrying re-enters the restart flow,
giving up (or closing the dialog) keeps the stopped screen; wired identically
for SSH and Telnet:

- size: 175,137,612 bytes;
- SHA256: `9A2F32822A28272EDA9341B1A6687DDB9AAB44B82C07CC42D83232FB8F356CF6`;
- both remote copies matched that hash via `Get-FileHash`
  (`WORK_SHA256` = `DESKTOP_SHA256` = source hash);
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it;
- the automated suite passes 351 tests (App 119) and Linux Xvfb E2E passes
  13/13; new headless coverage: `RestartProgressTests` (the restart
  progress state machine and both dialog branches) and the extended
  `TELNET06_FailedRestartKeepsStoppedSessionAndAllowsRetry`, which drives a
  real failed Telnet restart through the dialog (不重连, then R again,
  继续重启, which fails and asks a third time, then 不重连);
- interactive Windows confirmation of the removed gear, the restart progress
  bar and the 继续重启/不重连 dialog (`WIN-23`) remains pending.

This was a direct executable deployment over the existing working and desktop
copies, not a formal packaged release. No new candidate zip was created or
signed off for this build.

A follow-up direct deployment on 2026-10-23 replaced both copies with commit
`20dc26d`, fixing the user-reported uneven text in mixed Chinese/English
terminal output: the terminal font is now the bundled Noto Sans Mono CJK SC
(Regular + Bold, OFL-licensed avares assets), so latin and CJK are shaped
from one face grid and a mixed-language line keeps a single baseline — no
more system-font fallback with different line metrics (the old per-run CJK
fallback sat ~5px below the latin glyphs on Windows):

- size: 208,491,340 bytes (+33.4 MB for the two font faces);
- SHA256: `CE9A88C0582B287FC7FCAE573C98389339AA5166F9CCAB2C4C771C7CCC7203E5`;
- both remote copies matched that hash via `Get-FileHash`
  (`WORK_SHA256` = `DESKTOP_SHA256` = source hash);
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it;
- the automated suite passes 354 tests (App 122) and Linux Xvfb E2E passes
  13/13; new headless coverage: `CjkTerminalFontTests` (bundled font source
  and weight resolution, CJK glyph coverage in the primary face map so no
  fallback is possible, identical line metrics for latin/CJK/mixed runs);
- interactive Windows confirmation of the mixed-language baseline
  (WIN-20) remains pending.

This was a direct executable deployment over the existing working and desktop
copies, not a formal packaged release. No new candidate zip was created or
signed off for this build.

A second deployment on 2026-10-23 replaced both copies with commit `06c18f4`,
which makes the sessions panel a fixed MobaXterm-style dock (user request:
"不要什么自动侧边栏了"): the left-edge hover strip, the floating arrow handle,
the 250ms auto-hide timer and the red/green-edge reveal logic are gone; only
the title-bar sessions button toggles the dock, the splitter still resizes it,
and the visible state and width are restored on launch:

- size: 208,483,148 bytes;
- SHA256: `6D40457341F096DB365D55797A27B11517383722AC5B4B7EB9D9BDD94A34F1FB`;
- both remote copies matched that hash via `Get-FileHash`;
- no `COMTool-Avalonia.exe.bak-*` files were created (`BAK_COUNT=0`);
- a Windows process smoke test started the deployed working copy, observed it
  `Running` and `Responding=True`, then stopped it;
- the automated suite passes 358 tests (App 126) and Linux Xvfb E2E passes
  13/13, including the new `SidebarDockTests` (pointer movement never moves
  the dock, manual toggle writes the config, collapsed state and width survive
  a restart, the new-session menu keeps the dock open) and the repurposed E2E
  pair `UI02_SidebarDockedOnLaunchE2E` / `UI02_SidebarStaysDockedE2E`;

## 2026-10-23 — sidebar rail, network scanner, macros, history

- feature: MobaXterm-style 4-tab icon rail in the sidebar (Sessions /
  Tools / Macros / History, Chinese tooltips, active cell white), the 16px
  left gutter removed; Tools tab hosts the v1 network scanner (target
  single IP / CIDR / range capped at 1024, ports default `22,23`, 32-way
  concurrency, best-effort ICMP + 500ms TCP probe + 1s reverse DNS, live
  progress, stop button); Macros tab = global named commands sent to the
  active tab's terminal via `SendInputAsync` with optional trailing `\r`
  (`macros: [{id,name,text,appendEnter}]` in config, persisted on change);
  History tab = last-20 MRU of opened saved sessions
  (`recentSessions: [{kind,id,at}]`), click to reopen, deleted sessions
  pruned, 清空 button; both new config fields optional, version stays 3;
- test evidence: `SidebarRailTests` (13 headless tests: rail switching/
  active state, macro CRUD + persisted bytes with `\r` asserted on an
  in-memory PTY, send disabled without a terminal, history MRU/cap/prune/
  click-reopen/clear, scanner UI with an injectable fake scanner and
  stop/cancel), `NetworkScannerTests` (20: target parser single/CIDR/
  range/invalid/cap, loopback open-port discovery, closed port never
  reported open, cancellation), `ConfigTests` macro/history round-trip;
  the automated suite passes 381 tests (App 139) and Linux Xvfb E2E passes
  13/13 with the rail in place;
- interactive Windows confirmation is WIN-26 (rail tabs, real-LAN scan,
  macro send, history reopen across restart);
- deployment 2026-10-23 (in-place overwrite, no `.bak-*`):
  - exe `COMTool.App-singlepane.exe` published from `16258e3`;
  - size: 208,741,196 bytes;
  - SHA256: `7E085D17F0EB663038BE4AC736CB1CAA87792FB1731E3C5A5F43ECF3DD63B1E6`;
  - WORK and DESKTOP copies both matched that hash via `Get-FileHash`;
  - `BAK_COUNT=0`;
  - smoke test: process started, `Responding=True`, then stopped.

## 2026-10-23 — visual fixes (MobaXterm look)

- user feedback on the deployed rail build: icons and CJK labels rendered
  soft, 16px white band left of the rail, rail icons unclear;
- fixes:
  - `app.manifest` gained `PerMonitorV2` DPI awareness — without it Windows
    bitmap-scales the whole window on scaled desktops, blurring every glyph
    and icon (root cause of 发虚);
  - `UiFontFamily` now `Microsoft YaHei UI, Microsoft YaHei, DejaVu Sans`
    (native crisp CJK on Windows, DejaVu fallback on Linux);
  - `BodyGrid` left margin 16→0 so the sidebar rail is flush on the window
    edge (white band gone); content column margin compensates (+16) so tab
    strip and terminal positions are unchanged — E2E coordinates untouched;
  - session rows compressed MobaXterm-style: line 1 = `Name (Detail)`, line
    2 = serial params only (MinHeight 40→28);
  - rail icons 14→16 px; rail tooltips now descriptive Chinese
    (会话/工具（网络扫描仪）/宏（命令一键发送）/历史（最近打开的会话）);
- test evidence: full gate PASS — 381 tests (Core 47, SFTP 40, Connections
  63, Terminal 11, ZMODEM 32, TestGate 49, App 139), Linux Xvfb E2E 13/13
  (the new rail and row layout did not move any E2E hit points);
- deployment 2026-10-23 (in-place overwrite, no `.bak-*`):
  - exe published from `be28a12`;
  - size: 208,741,196 bytes;
  - SHA256: `97E350617D547E52F66C56A425385A6DD1077A07CFFC86660244905FC39149DF`;
  - WORK and DESKTOP copies both matched that hash via `Get-FileHash`;
  - `BAK_COUNT=0`;
  - smoke test: process started, `Responding=True`, then stopped;
- interactive Windows confirmation is WIN-26 (includes checking icons/labels
  render crisp on the real desktop).

### 2026-10-23 — 工具页 1:1 对标 MobaXterm（剪刀图标 / 8 项菜单 / 扫描仪对话框 / 深度扫描）

- commit: `a79001b` (feat(avalonia): 工具页 1:1 对标 MobaXterm：剪刀图标、
  8 项工具菜单、网络扫描仪对话框（起始/结束 IP + 主机×端口表格 + 深度扫描）);
- test evidence: full gate PASS — 389 tests (Core 47, SFTP 40, Connections
  67, Terminal 11, ZMODEM 32, TestGate 49, App 143), Linux Xvfb E2E 13/13;
- deployment 2026-10-23 (in-place overwrite, no `.bak-*`):
  - exe published from `a79001b`;
  - size: 208,847,692 bytes;
  - SHA256: `2FEBADCCA5FA3B6900CA5BAE4DBDF37DC744ADC9A79B7B6B29811BBAB509F133`;
  - WORK and DESKTOP copies both matched that hash via `Get-FileHash`;
  - `BAK_COUNT=0`;
  - smoke test: process started, `Responding=True`, then stopped;
- interactive Windows confirmation is WIN-26 (工具菜单文案、扫描仪对话框
  布局与深度扫描行为对照实机 MobaXterm 22.1 核对，桌面解锁后补拍)。

### 2026-10-23 — 网络扫描/端口扫描仪按实跑 MobaXterm v22.1 截图 1:1 重做

- commits: `fb74d89` (feat) + `fe0f977` (docs)；
- 依据 172.16.66.163 上实跑 MobaXterm Professional v22.1 的对话框截图
  （网络扫描/端口扫描仪）推翻原"起始/结束 IP + 自由端口列表 + 延迟列"设计：
  - 网络扫描仪 = 五段"IP 地址范围"（前三段固定+末段范围，默认 192.168.1.1→254，
    移除本机网段自动探测）+ 固定 10 列表格（IP 地址/名称/SSH/RDP/VNC/FTP/
    Telnet/Rlogin/HTTP/其他端口；服务端口 22/3389/5900/21/23/513/80，
    其他端口 443/445/135/139/1433/3306/5432/8080/8443/6000）
    + ▶ Start scan（英文标签同实测）+ ✗ 关闭（取消扫描并返回工具菜单）；
  - 端口扫描仪（原第 7 项"暂不支持"，深度扫描功能归入此工具）= IP 地址严格
    点分 IPv4 单输入框 + 单选"仅扫描已知端口"（默认，35 项 IANA 风格表）/
    "扫描端口范围 [1]→[65535]"（即深度扫描，进度 N/total + 停止）；
    结果行格式 1:1 "- Port #22 (ssh): listening"（未知端口不带服务名）；
  - 布局修复：窄侧栏（~158px 内容区）中横排按钮/输入行溢出卡片，
    停止按钮被挤出可点区域（headless 测试 hit-test 抓到）——按钮改竖排、
    八段框收窄至 24px、范围框单列一行；
- 两处 1:1 假设（截图未覆盖的渲染细节）：
  1. 网络扫描"有结果"的填充格子未实测拍到，✓/✗ 沿用 FA f00c/f00d +
     绿 #43a047/灰 #9e9e9e；
  2. 端口扫描仪"仅扫描已知端口"完整表未实测，取 35 项 IANA 风格近似集
     （覆盖实测出现的全部端口 22/123/139/445/3389/993/995/6000）；
- test evidence: full gate PASS — 388 tests (Core 47, SFTP 40, Connections
  66, Terminal 11, ZMODEM 32, TestGate 49, App 143), Linux Xvfb E2E 13/13
  （测试 389→388：NetworkScannerTests 12→11，SidebarRailTests 保持 17，
  5 个旧扫描/深扫测试重写为服务网格+端口扫描仪已知/范围/停止+非法输入）;
- deployment 2026-10-23 (in-place overwrite, no `.bak-*`):
  - exe `COMTool.App-singlepane.exe` published from `fe0f977`;
  - size: 208,888,652 bytes;
  - SHA256: `DEE698A7D252FBE32D00D7F4978C03DEBDAA8B1F5838DFCCEE486233D73DF8F1`;
  - WORK and DESKTOP copies both matched that hash via `Get-FileHash`;
  - `BAK_COUNT=0`;
  - smoke test: process started, `Responding=True`, then stopped;
- interactive Windows confirmation is WIN-26 (五段 IP 范围表单、10 列服务
  网格、端口扫描仪两种模式与深度扫描行为对照实机 MobaXterm 22.1 核对)。

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
