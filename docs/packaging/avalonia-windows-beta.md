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
The trade-off is a larger on-disk executable (about 168 MiB) and a larger
zip (about 69 MiB), while the download remains practical because the payload
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

The latest app binary (version `0.01`, borderless chrome, auto-hiding sidebar,
non-focus-stealing chrome controls, centered tool-strip icons, remembered
window size, encoding-dropdown focus return, and the ReadyToRun release build)
was published over SSH and copied to:

- `C:\Users\zhangrui\COMTool-Avalonia.exe` (working copy)
- `D:\Users\zhangrui\Desktop\COMTool-Avalonia.exe` (desktop copy)

- size: 175,550,896 bytes;
- SHA256: `25a03a62a3b14d8b23f08b9a395980682eb58976ab45076b051f6c48a19b9cea`;
- both remote copies matched that hash via `certutil`.

The release zip is 72,018,013 bytes and is validated with SHA256:

```text
5e86c8887fd9d935367a8987cac87ee6f69388da371b39b27551fdb8cb32366f
```

That hash is from the local packaging run and changes whenever the app is
rebuilt. Linux warm-start measurements took the packaged app from 1.54 s to
0.69 s; Windows startup still needs confirmation on the target desktop.

## Signing

The executable is currently unsigned. Windows SmartScreen may warn on first
run. Code signing is a release-hardening step and must not change the
application/config identity.
