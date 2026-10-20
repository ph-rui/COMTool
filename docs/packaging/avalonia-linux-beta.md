# COMTool Avalonia Beta Linux package

The Linux Beta is a portable, self-contained `linux-x64` build plus per-user
install and uninstall scripts. It is deliberately separate from the Python
COMTool app: it never writes `~/.config/comtool` or replaces the stable
launcher.

## Build

From the repository root:

```bash
./avalonia/pack/linux/pack.sh
```

Set `DOTNET=/path/to/dotnet` if `dotnet` is not on `PATH`. The script publishes

```text
avalonia/src/COMTool.App/COMTool.App.csproj
```

as `linux-x64`, self-contained, single-file, ReadyToRun and uncompressed, then
creates:

```text
avalonia/publish/package/COMTool-Avalonia-linux-x64-0.01.tar.gz
avalonia/publish/package/SHA256SUMS-linux.txt
```

The archive layout is:

```text
COMTool-Avalonia-linux-x64-0.01/
  app/COMTool.App
  app/comtool-avalonia.png
  install.sh
  uninstall.sh
  README.txt
```

Use `SKIP_PUBLISH=1` to reuse an existing `avalonia/publish/linux-x64` output.

## Install

```bash
tar -xzf COMTool-Avalonia-linux-x64-0.01.tar.gz
cd COMTool-Avalonia-linux-x64-0.01
./install.sh
```

Defaults:

| Item | Path |
| --- | --- |
| Application | `~/.local/opt/comtool-avalonia` |
| Launcher | `~/.local/bin/comtool-avalonia` |
| Desktop entry | `~/.local/share/applications/comtool-avalonia.desktop` |
| Icon | `~/.local/share/icons/hicolor/256x256/apps/comtool-avalonia.png` |
| Config | `~/.config/comtool-avalonia` |

No sudo is required. A custom prefix can be selected with
`PREFIX=/opt/my-prefix ./install.sh`.

## Uninstall

```bash
./uninstall.sh
```

This removes the application, launcher, icon and desktop entry but preserves
`~/.config/comtool-avalonia`, matching the Windows Beta rollback behavior. The
Python app and `~/.config/comtool` are untouched.

## Verification

Validated on Linux with Xvfb `:99` (1280x800, 100% scaling):

- package built from the current Avalonia app sources with ReadyToRun enabled
  and single-file compression disabled;
- tarball size 69,563,572 bytes, SHA256
  `984e74666533d402be05b25ae9a452b570492762e7e458cf819f01f392cac483`;
- packaged executable size 168,878,903 bytes, SHA256
  `a53c26810e9d36d9805868355940dde98555e5bd1d7f1546d353a7153cc3f0a3`;
- warm startup to the first main window measured 0.66-0.70 s (median 0.66 s
  in the latest run) on Xvfb `:99`, down from 1.54 s for the previous
  compressed build; see `docs/fixtures/phase1/performance-2026-10-20.md`;
- version `0.01`, the borderless window, auto-hiding sidebar expanded on every
  launch, multiple independent connections from one saved SSH session, Material
  Design title-bar icons, centered settings/plugin/functional icons, remembered
  window size, non-focus-stealing chrome controls, encoding-drop-down focus
  return, immediate single-character SSH echo, terminal-pane double-click
  maximize/restore, and PTY-preserving tab switching were exercised on Xvfb
  before and after packaging;
- the packaged app switched between two live SSH tabs in both directions and
  accepted `echo PACKAGED_SWITCH_TWO_OK` / `echo PACKAGED_SWITCH_ONE_OK`
  immediately after each tab click, without clicking the terminal area first;
  see `docs/fixtures/phase1/tab-switch-input-second-1000x650.png` and
  `tab-switch-input-first-1000x650.png`;
- `bash -n` passed for all three scripts;
- install created an executable app, absolute launcher symlink, desktop entry
  and icon;
- the packaged app started from a separate `HOME`, created only
  `~/.config/comtool-avalonia`, and rendered the empty sidebar in an 850x500
  borderless window;
- a second install (upgrade path) succeeded;
- uninstall removed the app, symlink, desktop entry and icon while preserving
  the Beta config;
- `~/.local`, `~/.config/comtool`, the Python app and the Windows install were
  not touched.

Wayland-only sessions and an AppImage wrapper have not been validated yet.
