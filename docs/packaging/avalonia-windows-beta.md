# COMTool Avalonia Beta Windows package

The Avalonia Beta ships as a compressed self-contained single-file executable
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

with `win-x64`, self-contained, single-file and compression enabled, then
produces:

```text
avalonia/publish/package/COMTool-Avalonia-Beta-win-x64-3.4.1-beta.1.zip
avalonia/publish/package/SHA256SUMS.txt
```

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

- installer copied `COMTool.App.exe` (55,947,056 bytes) to the requested path;
- `install.json` recorded `3.4.1-beta.1`;
- `HKCU` uninstall entry contained `DisplayName`, `DisplayVersion`,
  `InstallLocation` and the quoted `UninstallString`;
- shortcuts were skipped with `-NoShortcuts`;
- uninstall removed the payload and registry entry;
- `%APPDATA%\COMTool-Avalonia` and `%APPDATA%\comtool` both still existed after
  uninstall (no `-RemoveConfig`).

The release zip itself is validated with SHA256:

```text
84ca426659f483ae52dae7a7cd5938f6116433ff432974cd115a6fec861ab6d1
```

That hash is from the local packaging run and changes whenever the app is
rebuilt.

## Signing

The executable is currently unsigned. Windows SmartScreen may warn on first
run. Code signing is a release-hardening step and must not change the
application/config identity.
