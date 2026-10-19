#!/usr/bin/env bash
# Removes the per-user COMTool Avalonia Beta installation. User configuration is
# intentionally preserved.
#
# Environment:
#   PREFIX=/custom/prefix   install root (default: $HOME/.local)
set -euo pipefail

prefix="${PREFIX:-${HOME:?HOME is not set}/.local}"
app_dir="$prefix/opt/comtool-avalonia"
bin_dir="$prefix/bin"
desktop_file="$prefix/share/applications/comtool-avalonia.desktop"
icon_file="$prefix/share/icons/hicolor/256x256/apps/comtool-avalonia.png"

rm -rf "$app_dir"
rm -f "$bin_dir/comtool-avalonia" "$desktop_file" "$icon_file"

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$prefix/share/applications" >/dev/null 2>&1 || true
fi

echo "Removed COMTool Avalonia Beta from:"
echo "  $prefix"
echo "Configuration was preserved in:"
echo "  ${XDG_CONFIG_HOME:-$HOME/.config}/comtool-avalonia"
