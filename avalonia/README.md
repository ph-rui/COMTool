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
# Windows headless check of the real Beta config, DPAPI token and serial session:
dotnet run --project tools/COMTool.Phase0Smoke -- --stored-ssh --stored-serial
```

## Publish

Windows self-contained, single-file:

```bash
/tmp/opencode/dotnet/dotnet publish src/COMTool.App/COMTool.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:PublishReadyToRun=true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o publish/win-x64
# r51 (2026-11-05): PublishReadyToRun 为启动性能关键项（VM 实测进程内就绪
# 434ms→107-169ms，约 2.5x；恢复 2026-10-19「以包体积换启动」的原始策略，
# 此前逐轮 publish 命令漂移把 R2R/压缩弄丢了）。勿删。
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
  -p:PublishReadyToRun=true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o publish/linux-x64
```

## Publish — Native AOT (r52, 2026-11-05)

Native AOT removes the JIT and the single-file self-extraction: fastest startup,
smallest memory. The build is **slower** and needs a C++ toolchain:

- Windows: VS Build Tools with the C++ workload (MSVC cl/link + Windows SDK)
  installed at a custom path is fine (dotnet auto-discovers it via vswhere), or
  clang+lld. AOT cannot be cross-compiled across OSes — build win-x64 on Windows.
- Linux: clang + lld on PATH (e.g. conda-forge `clangdev`/`llvmdev` + `lld`).

```bash
# win-x64 (run on a Windows machine with the C++ toolchain)
/tmp/opencode/dotnet/dotnet publish src/COMTool.App/COMTool.App.csproj \
  -c Release -r win-x64 -p:PublishAot=true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o publish/win-x64-aot
find publish/win-x64-aot -name '*.pdb' -delete

# linux-x64 (clang/lld on PATH)
PATH=/tmp/opencode/miniforge/bin:$PATH /tmp/opencode/dotnet/dotnet publish \
  src/COMTool.App/COMTool.App.csproj -c Release -r linux-x64 -p:PublishAot=true \
  -p:DebugType=None -p:DebugSymbols=false -o publish/linux-x64-aot
```

The output is a native executable plus a few native DLLs/.so
(libSkiaSharp, libHarfBuzzSharp, av_libglesv2, conpty, ...) — deploy the whole
folder. r52 VM measurement (cold start): wall 0.58-0.76s vs 1.51-2.53s for the
R2R single file; in-process ready 22-58ms vs 107-169ms.

AOT rules this repo follows (do not break them):

- Every App .axaml root carries `x:CompileBindings="True"`.
- XAML that is loaded at runtime (not just compiled in) must have an `x:Class`
  and be instantiated in C# — `new ResourceInclude(uri)` / `new StyleInclude(uri)`
  fail under AOT with "No precompiled XAML found". See ThemeLight/ThemeDark
  (ThemeManager) and the terminal library 4.1.5 (TerminalGenericStyles).
- JSON goes through `ConfigJsonContext` (source generation), never reflective
  `JsonSerializer`.
- All projects set `IsAotCompatible=true` (Directory.Build.props); keep new
  code free of runtime reflection, or annotate with DynamicDependency.

## Current implementation (Phase 0 + Phase 1)

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
- Config-backed session sidebar with serial/SSH session activation, dynamic
  multi-tab terminal host, SSH password prompt/cache and tab close/reconnect.
- Sidebar context menu with confirmed session removal and a dark theme resource
  set selected from the stored `skin` value.
- 45 passing tests across Core, Connections, Terminal and ZMODEM.

Not implemented yet:

- Full tab bar/plugin selector parity and the complete settings/theme/language
  UI.
- Serial-port descriptions, Windows serial receive and SSH disconnect
  auto-reconnect.
- Full ZMODEM sender/receiver and `rz`/`sz` transfer tests.
- CI execution, 8-hour soak, hotplug/reconnect and Windows serial receive.

## Data isolation

The Avalonia Beta must never write the Python `config.json`. `ConfigPaths`
uses:

- Python: `%APPDATA%\COMTool\config.json`
- Avalonia Beta: `%APPDATA%\COMTool-Avalonia\config.json`

On Linux the equivalent directories are `~/.config/comtool` and
`~/.config/comtool-avalonia`. Python config import is one-way during beta.
