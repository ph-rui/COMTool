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
feature surface. `tools/COMTool.Phase0Smoke` exercises DPAPI, config import,
terminal throughput, serial enumeration, SSH shell output and PTY resize:

```bash
dotnet run --project tools/COMTool.Phase0Smoke -- --dpapi --config --terminal --ports
```

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
- Terminal rendering with ANSI, 256 colors, CJK/wide characters, selection,
  copy/paste, resize and 1MB/10MB throughput checks.
- Serial and SSH `IPtyConnection` adapters (`SerialConnection`, `SshConnection`),
  Windows COM enumeration and Linux socat PTY read/write tests.
- ZMODEM CRC/header codec and detector, including a real `lrzsz` ZRQINIT
  fixture. The full sender/receiver is still to port.
- One-way Python config import and Windows DPAPI round-trip via
  `tools/COMTool.Phase0Smoke`.
- 45 passing tests across Core, Connections, Terminal and ZMODEM.

Not implemented yet:

- Full tab bar/plugin selector parity and the complete settings/theme/language
  UI.
- Wiring the real serial/SSH backends and credential resolver into the UI.
- Full ZMODEM sender/receiver and `rz`/`sz` transfer tests.
- CI execution, 8-hour soak, hotplug/reconnect and Windows serial receive.

## Data isolation

The Avalonia Beta must never write the Python `config.json`. `ConfigPaths`
uses:

- Python: `%APPDATA%\COMTool\config.json`
- Avalonia Beta: `%APPDATA%\COMTool-Avalonia\config.json`

On Linux the equivalent directories are `~/.config/comtool` and
`~/.config/comtool-avalonia`. Python config import will be one-way during beta.
