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

as `linux-x64`, self-contained, single-file, compressed, then creates:

```text
avalonia/publish/package/COMTool-Avalonia-linux-x64-3.4.1-beta.1.tar.gz
avalonia/publish/package/SHA256SUMS-linux.txt
```

The archive layout is:

```text
COMTool-Avalonia-linux-x64-3.4.1-beta.1/
  app/COMTool.App
  app/comtool-avalonia.png
  install.sh
  uninstall.sh
  README.txt
```

Use `SKIP_PUBLISH=1` to reuse an existing `avalonia/publish/linux-x64` output.

## Install

```bash
tar -xzf COMTool-Avalonia-linux-x64-3.4.1-beta.1.tar.gz
cd COMTool-Avalonia-linux-x64-3.4.1-beta.1
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

- package built from the current Avalonia app sources;
- tarball size 46,812,750 bytes, SHA256
  `6a14114e4cf8846953d00fdf8bdbf115781dd315f9395f9606d635f652b3d60d`;
- the borderless window, auto-hiding sidebar, Material Design title-bar icons
  and encoding-drop-down focus return were exercised on Xvfb before packaging;
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
