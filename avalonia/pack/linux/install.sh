#!/usr/bin/env bash
# Per-user installer for the COMTool Avalonia Beta Linux package.
#
# Environment:
#   PREFIX=/custom/prefix   install root (default: $HOME/.local)
#
# The app is installed under $PREFIX/opt/comtool-avalonia. The Python COMTool
# app and $HOME/.config/comtool are never touched.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
prefix="${PREFIX:-${HOME:?HOME is not set}/.local}"
app_dir="$prefix/opt/comtool-avalonia"
bin_dir="$prefix/bin"
desktop_dir="$prefix/share/applications"
icon_dir="$prefix/share/icons/hicolor/256x256/apps"
desktop_file="$desktop_dir/comtool-avalonia.desktop"
icon_file="$icon_dir/comtool-avalonia.png"

if [[ ! -f "$script_dir/app/COMTool.App" ]]; then
    echo "error: $script_dir/app/COMTool.App not found" >&2
    exit 1
fi

mkdir -p "$prefix/opt" "$bin_dir" "$desktop_dir" "$icon_dir"
stage="$(mktemp -d "$prefix/opt/.comtool-avalonia.XXXXXX")"
old=""
cleanup() {
    rm -rf "$stage"
    if [[ -n "$old" && -d "$old" ]]; then
        rm -rf "$old"
    fi
}
trap cleanup EXIT

cp -R "$script_dir/app/." "$stage/"
chmod 0755 "$stage/COMTool.App"

# Stage first, then swap the directory so an interrupted upgrade never leaves a
# partially written application directory.
if [[ -e "$app_dir" ]]; then
    old="$app_dir.old.$$"
    mv "$app_dir" "$old"
fi
mv "$stage" "$app_dir"

ln -sfn "$app_dir/COMTool.App" "$bin_dir/comtool-avalonia"
install -m 0644 "$app_dir/comtool-avalonia.png" "$icon_file"
cat > "$desktop_file" <<EOF
[Desktop Entry]
Type=Application
Name=COMTool Avalonia Beta
Comment=Serial port and SSH terminal
Exec="$app_dir/COMTool.App" %U
Icon=$icon_file
Terminal=false
Categories=Development;Utility;
StartupWMClass=COMTool.App
EOF

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$desktop_dir" >/dev/null 2>&1 || true
fi

echo "Installed COMTool Avalonia Beta:"
echo "  app:     $app_dir/COMTool.App"
echo "  command: $bin_dir/comtool-avalonia"
echo "  config:  ${XDG_CONFIG_HOME:-$HOME/.config}/comtool-avalonia (kept separate)"
