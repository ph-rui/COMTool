# COMTool Avalonia

This directory is the parallel Avalonia port of the Python COMTool. The Python
application at the repository root remains the stable release during Phase 0.

## Requirements

- .NET SDK `10.0.402` (see `global.json`)
- Windows for the target packaging and DPAPI checks; Linux/macOS are used for
  build and UI smoke tests.

The local development SDK is installed outside the repository at
`/tmp/opencode/dotnet`. Replace that path with your own `dotnet` location when
building on another machine.

## Build and test

```bash
cd avalonia
/tmp/opencode/dotnet/dotnet restore COMTool.Avalonia.sln
/tmp/opencode/dotnet/dotnet build COMTool.Avalonia.sln -c Release
/tmp/opencode/dotnet/dotnet test COMTool.Avalonia.sln -c Release
```

## Run

```bash
/tmp/opencode/dotnet/dotnet run --project src/COMTool.App
```

Phase 0 development flags:

- `--demo` shows fake serial and SSH sessions in the sidebar.
- `--terminal` opens the terminal spike and launches the system shell.

The flags are temporary scaffolding and must not be part of the Phase 1
feature surface.

## Publish

Windows self-contained, single-file:

```bash
/tmp/opencode/dotnet/dotnet publish src/COMTool.App/COMTool.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o publish/win-x64
# Native library PDBs are still copied by NuGet; remove them from the package.
find publish/win-x64 -name '*.pdb' -delete
```

Linux self-contained, single-file:

```bash
/tmp/opencode/dotnet/dotnet publish src/COMTool.App/COMTool.App.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o publish/linux-x64
```

## Current Phase 0 scope

Implemented:

- Custom title bar, window controls, pin and sidebar toggle.
- `User sessions` sidebar with serial/SSH groups, empty state and demo rows.
- Left-edge sidebar handle matching the Python UI.
- Light-theme tokens for the main shell.
- Config models, session normalization/pruning and independent Beta config path.
- Terminal spike based on `Iciclecreek.Avalonia.Terminal` / XTerm.NET.
- Unit tests for config round-trip, unknown-field preservation, session
  deduplication/pruning and SSH key/password retention.

Not implemented yet:

- Full tab bar and plugin selector parity.
- Dark theme and the complete settings/language/help UI.
- Real serial/SSH connection backends.
- ZMODEM integration.
- Python config import and DPAPI token migration.
- Terminal performance and 256-color/CJK validation.

## Data isolation

The Avalonia Beta must never write the Python `config.json`. `ConfigPaths`
uses:

- Python: `%APPDATA%\COMTool\config.json`
- Avalonia Beta: `%APPDATA%\COMTool-Avalonia\config.json`

On Linux the equivalent directories are `~/.config/comtool` and
`~/.config/comtool-avalonia`. Python config import will be one-way during beta.
