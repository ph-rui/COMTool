#!/usr/bin/env bash
# Builds the portable COMTool Avalonia Beta Linux x64 package.
#
# Environment:
#   VERSION=0.01   package version
#   SKIP_PUBLISH=1         reuse avalonia/publish/linux-x64
#   DOTNET=/path/to/dotnet dotnet executable (default: dotnet)
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
project="$repo_root/avalonia/src/COMTool.App/COMTool.App.csproj"
source_dir="$repo_root/avalonia/publish/linux-x64"
output_dir="$repo_root/avalonia/publish/package"
version="${VERSION:-0.01}"
name="COMTool-Avalonia-linux-x64-$version"
dotnet="${DOTNET:-dotnet}"

if [[ "${SKIP_PUBLISH:-0}" != "1" ]]; then
    # ReadyToRun removes most startup JIT; leaving single-file compression off
    # avoids decompressing the native payload on every launch. The package is
    # larger, but the startup comparison is in docs/fixtures/phase1.
    echo "Publishing self-contained linux-x64 app..."
    "$dotnet" publish "$project" \
        -c Release -r linux-x64 --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=false \
        -p:PublishReadyToRun=true \
        -p:DebugType=None -p:DebugSymbols=false \
        -o "$source_dir"
fi

if [[ ! -f "$source_dir/COMTool.App" ]]; then
    echo "error: $source_dir/COMTool.App not found" >&2
    exit 1
fi

rm -rf "${output_dir:?}/${name:?}"
mkdir -p "$output_dir/$name/app"
install -m 0644 "$source_dir/COMTool.App" "$output_dir/$name/app/COMTool.App"
chmod 0755 "$output_dir/$name/app/COMTool.App"
install -m 0644 "$repo_root/avalonia/src/COMTool.App/Assets/logo.png" \
    "$output_dir/$name/app/comtool-avalonia.png"
install -m 0755 "$script_dir/install.sh" "$output_dir/$name/install.sh"
install -m 0755 "$script_dir/uninstall.sh" "$output_dir/$name/uninstall.sh"
install -m 0644 "$script_dir/README.txt" "$output_dir/$name/README.txt"

(
    cd "$output_dir"
    tar -czf "$name.tar.gz" "$name"
    sha256sum "$name.tar.gz" > SHA256SUMS-linux.txt
)

echo "Created:"
echo "  $output_dir/$name.tar.gz"
echo "  $output_dir/SHA256SUMS-linux.txt"
