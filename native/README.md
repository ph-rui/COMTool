# COMTool Native

A small, genuinely native serial terminal with built-in ZMODEM, written in
C++17 and Qt 5 Widgets.  It lives beside the original Python COMTool and does
not share its configuration or runtime.

## Status

v1 is feature-complete and has been exercised on Linux and Windows:

- serial terminal with ANSI/256-colour rendering, UTF-8 and wide characters;
- hot-plug port list, editable port name, manual PTY paths;
- multiple tabs, per-tab serial settings, collapsible settings panel;
- bidirectional ZMODEM with automatic direction detection, multi-file
  transfers, progress, cancellation, and one save dialog per received file;
- light and dark palettes, Chinese/English UI, persisted window/session
  configuration (ports are restored closed).

The old Python application and its tests are unchanged.

## Build (Linux)

```bash
cmake -S native -B build-native -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_PREFIX_PATH=/path/to/Qt/5.15.2/gcc_64
cmake --build build-native
./build-native/comtool-native
```

Qt 5.15 Widgets and SerialPort are required.  libvterm 0.3.3 is vendored in
`native/third_party/libvterm` under its MIT licence.

## Build (Windows)

Use a Qt 5.15 MinGW/MSVC prompt (the tested build uses MinGW 8.1 and Windows
Qt 5.15.2):

```bat
cmake -S native -B build-native -G Ninja ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_PREFIX_PATH=C:\Qt\5.15.2\mingw81_64
cmake --build build-native
```

The Windows target is a GUI-subsystem executable, so no console window is
left behind.  Deploy Qt's DLLs, `platforms/qwindows.dll`, the MinGW runtime,
and optionally `translations/qt_zh_CN.qm` next to `comtool-native.exe`; a
small `qt.conf` with `Prefix=.` is enough for a relocatable folder.

## Configuration

- Linux: `~/.config/comtool-native/config.ini`
- Windows: `%APPDATA%\comtool-native\config.ini`

Sessions are restored on startup but serial ports are never opened
automatically.  The default theme is light; the View menu switches to dark.

## Tests

```bash
cmake --build build-native --target zmodem_tests
ctest --test-dir build-native --output-on-failure
```

`zmodem_tests` covers codec/detector/filename handling, empty and binary
transfers, multi-file metadata, and 256 KiB in-memory transfers.  On POSIX,
`zmodem_peer` is a small CLI endpoint for interoperability tests against
`lrzsz` over a PTY pair.  GUI acceptance used `lrz`/`lsz` over `socat` PTYs
and a real i.MX95 EVK console (`console.sh` -> `ttyLP0`, 115200 8N1):
a 4 MiB file was sent and received with matching SHA256.  The Python suite
remains at `128 passed`.

## Measured startup

`--benchmark-startup` reports the time from process entry to the first
event-loop turn.

| System | Result |
| --- | --- |
| Linux, Qt 5.15.2, Xvfb | 0.12–0.21 s |
| Windows 10 Pro 1909 VM over RDP, Qt 5.15.2 | 0.60–0.68 s |

The Windows VM spends ~0.34 s in Qt's first font-database initialisation and
~0.20 s showing the first text window; a bare Qt window containing only one
label measures ~0.51 s on the same VM.  The native application is therefore
close to the Qt floor there, while remaining roughly twice as fast as the
old Python build (1.4–1.7 s).  On a normal local desktop the font-database
cost is expected to be substantially lower.
