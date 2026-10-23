# COMTool Avalonia Tools Page Design (3 tools + servers management)

## Goal

Replace the 19-entry MobaXterm tool menu with a three-tool, window-based
tool set that mirrors MobaXterm Professional 22.1, and leave an extension
point for future tools:

- 网络服务器 (network servers management)
- 网络扫描仪 (network scanner)
- 端口扫描仪 (port scanner)

The other 16 MobaXterm tool entries are removed. The three tools open as
independent windows, matching MobaXterm, instead of embedded sidebar panels.

## Scope

Included:

- Tool catalog framework: adding a tool later means one `ToolDescriptor`
  row plus one window class; no menu, sidebar, or `MainWindow` changes.
- Tools menu reduced to the three entries above (flat list, no group
  headers), with the MobaXterm icon glyph/color for each.
- `NetworkScanWindow` and `PortScanWindow`: the existing scan engines and
  MobaXterm layouts moved out of `MainWindow`/`SessionSidebar` into
  standalone windows that own their scan lifecycle.
- `ServersManagementWindow`: a 1:1 replica of MobaXterm's "Servers
  management" dialog. HTTP and TFTP servers are functional; the other
  seven rows are present but disabled ("暂不支持（框架已预留）").
- HTTP file server and TFTP server engines in `COMTool.Connections`, with
  unit tests.
- Server settings persistence (port, root directory, options, runtime
  limit) in the Avalonia config file.
- Removal of the embedded scanner views, the `ToolsView` enum, all scanner
  state on `MainViewModel`/`MainWindow`, and the four sidebar scan events.

Excluded from this phase:

- FTP, SSH/SFTP, Telnet, NFS, VNC, Cron and Iperf server implementations.
- Authentication, TLS, WebDAV or upload endpoints for the HTTP server.
- Windows URL ACL registration: the HTTP engine deliberately uses a raw
  `TcpListener` HTTP/1.1 implementation so it needs no administrator
  rights (unlike `HttpListener`).
- Any change to the port in the running server without a restart.
- TFTP netascii mode.

## Measured Baseline

All measurements were taken on the real MobaXterm Professional 22.1
(`D:\Win App\MobaXterm_22.1_Green`) over RDP at 1280x800. Reference
captures are kept at:

- `/tmp/opencode/rdp-toolsmenu2.png` (full Tools menu)
- `/tmp/opencode/rdp-servers1.png` (Servers management, initial state)
- `/tmp/opencode/rdp-tftp-on.png` (TFTP config panel, running)
- `/tmp/opencode/rdp-http-on.png` (HTTP config panel, running)
- `/tmp/opencode/rdp-http-stop.png` (HTTP stopped, log lines)

### Tools menu

The MobaXterm menu is a flat Win32 dropdown: three group headers
(系统 / 文本 / 网络) and 19 entries. We keep only the three implemented
entries, flat, with these icons (verified present in the bundled
`fontawesome6-solid.ttf` cmap):

| Id | Name | Glyph | Color |
|----|------|-------|-------|
| net-server | 网络服务器 | `\uf013` gear | `#00897b` |
| scanner | 网络扫描仪 | `\uf7c0` satellite-dish | `#5e35b1` |
| port-scanner | 端口扫描仪 | `\uf1eb` wifi | `#2196f3` |

### Servers management dialog

- Window title `Servers management`, about 734x445 client (736x475 outer),
  fixed size, system title bar with close button, centered on the owner,
  not in the taskbar.
- Left column, nine rows, row height about 40px, label font 13px:

  TFTP 服务器 / FTP 服务器 / HTTP 服务器 / SSH/SFTP 服务器 / Telnet 服务器 /
  NFS 服务器 / VNC 服务器 / Cron 服务器 / Iperf 服务器

  Each row has three 23x20 square buttons with a 1px gray border and a
  light gray background:
  - play (`\uf144`, blue `#1976d2`) start
  - stop (`\uf28d`, teal-gray `#607d8b`) stop
  - gears (`\uf085`, blue `#1976d2`) show this server's configuration
  When a server is running with a runtime limit, the remaining seconds are
  shown as `(N)` to the right of the row buttons.
- Right panel: white background, 1px gray border, about 410x301, inset
  from the top and left; it shows either the welcome pane or the selected
  server's configuration.
- Welcome pane (no selection): red bold `网络服务器` at the top left,
  centered two-line text `欢迎使用` / `MobaXterm 网络服务器`, and a molecule
  logo on the right (dark blue center circle, six cyan satellites, gray
  links). The wording and logo match the user-supplied reference and the
  live capture.
- Bottom: a centered `✓ 确定` button (green `\uf058`, 72x26) closes the
  dialog; closing stops every running server.

### HTTP 服务器配置 panel

- Centered bold title `HTTP 服务器配置`.
- `根目录:` path text box plus a yellow folder browse button.
- Checkbox `允许用户列出文件夹内容`.
- `监听端口:` numeric field, default 80.
- Checkbox `服务器运行时间` plus a seconds field, default 3600.
- `服务器输出:` read-only log box.
- Log lines use MobaXterm's `[HH:mm:ss] message` format, including
  `Starting HTTP server, please wait...`, `HTTP server started`,
  `Stopping HTTP server, please wait...`, `HTTP server stopped`.

### TFTP 服务器配置 panel

Same shape with TFTP-specific fields:

- `根目录:` plus browse.
- `下载成功后显示信息` checkbox.
- `上传成功后显示信息` checkbox.
- `监听端口:` default 69.
- `服务器运行时间` checkbox plus seconds, default 3600.
- `服务器输出:` log box with the same lifecycle lines
  (`Starting TFTP server, please wait...`, `TFTP server started`,
  `TFTP server stopped`).

### Interaction

- ▶ starts the server with the current (possibly edited) configuration.
  A failure (for example a busy port) is appended to the log; the server
  stays stopped.
- ■ stops the server.
- ⚙ and clicking the row select it, showing its configuration panel on the
  right.
- A running server with the runtime limit enabled is stopped
  automatically when the countdown reaches zero, with a log line.
- The seven unimplemented rows keep their buttons visible but disabled
  (tooltip `暂不支持（框架已预留）`); they never show a configuration panel.

## Architecture

### Tool framework

New files in `src/COMTool.App/Tools/`:

```csharp
public sealed record ToolDescriptor(
    string Id,
    string Name,
    string Glyph,
    string GlyphColor,
    bool IsEnabled,
    Func<MainWindow, Window> CreateWindow);

public static class ToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> All { get; }
    public static ToolDescriptor? Find(string id);
}
```

`ToolCatalog.All` is the single source of truth for the menu and the open
action. `MainWindow` keeps `Dictionary<string, Window>` keyed by tool id:
clicking an already-open tool activates it, otherwise it calls
`CreateWindow(this)` and `Show(this)`, and removes the entry on `Closed`
(restoring terminal focus, like `ConnectionSettingsWindow`).

`MainViewModel.InitToolMenu()` builds `ToolMenu` from `ToolCatalog.All`.
`ToolMenuItem` keeps `Id`, `Name`, `IsEnabled`, `ToolTip`, `Glyph`,
`GlyphColor`; the group-header flag and template are deleted.

### Windows

- `NetworkScanWindow` (title `网络扫描`, 880x540, resizable): header form
  `IP 地址范围:` five octet boxes (`192 . 168 . 1 . [1] --> [254]`) and a
  `▶ Start scan` button; a 10-column bordered table (IP 地址, 名称, SSH,
  RDP, VNC, FTP, Telnet, Rlogin, HTTP, 其他端口) with small colored header
  icons and ✓/✗ cells; a status line; `✗ 关闭` at the bottom. Closing
  cancels an active scan.
- `PortScanWindow` (title `网络扫描`, per MobaXterm, 820x480): bordered
  form with `IP 地址:`, radio `仅扫描已知端口` (default) and
  `扫描端口范围 [1] --> [65535]`, `▶ Start scan` and the previously
  approved `停止` control; a large read-only, monospaced results box
  (`- Port #22 (ssh): listening` lines); `✗ 关闭` at the bottom. Closing
  cancels an active scan.
- `ServersManagementWindow` (title `Servers management`, 734x445 fixed):
  as measured above.

Each scan window owns a `CancellationTokenSource`, its progress state and
its results; it exposes `internal INetworkScanner? ScannerForTests` for
headless tests (default `new NetworkScanner()`).

### Server engines

New namespace `COMTool.Connections.Servers`:

```csharp
public interface IServerHost : IAsyncDisposable
{
    string Name { get; }                 // "HTTP server" / "TFTP server"
    bool IsRunning { get; }
    int Port { get; }
    event Action<string>? LogMessage;    // raw message; the UI adds [HH:mm:ss]
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
```

`HttpFileServer(string rootDirectory, int port, bool allowDirectoryListing)`
implements a minimal HTTP/1.1 server over `TcpListener` bound to
`IPAddress.Any`:

- `GET` and `HEAD` only; other methods return 405.
- Percent-decoding, query stripping, and path normalization; any resolved
  path outside the root returns 403 (traversal protection).
- Files are served with a Content-Type from an extension map
  (text/html, text/css, application/javascript, application/json, images,
  text/plain, zip, octet-stream fallback) and a Content-Length.
- Directories: with listing enabled, an HTML index of the directory;
  otherwise 403. A missing path returns 404.
- Each request is logged as `METHOD /path status`.
- `StartAsync` binds the socket and starts the accept loop; binding
  failures propagate (the UI logs them). `StopAsync` closes the listener
  and logs the stop sequence.

`TftpServer(string rootDirectory, int port, bool showDownloadInfo,
bool showUploadInfo)` implements TFTP over `UdpClient`:

- RRQ (download): read the file under the root, send DATA blocks of 512
  bytes from an ephemeral TID, wait for the matching ACK with a 1-second
  timeout and up to 3 retransmissions, then the final zero-length block.
  Missing/invalid paths return ERROR code 1.
- WRQ (upload): ACK block 0, receive DATA blocks, write under the root,
  ACK each block, finish on a short block.
- Transfers log `Download: <file> (<bytes> bytes)` / `Upload: ...` only
  when the corresponding checkbox is enabled.
- Only octet mode; requests with a mode other than `octet` (or `netascii`,
  treated as octet) are accepted as octet.

Both engines never require administrator rights.

### Persistence

`ComtoolConfig` gains:

```csharp
[JsonPropertyName("servers")]
public ServerSettings Servers { get; set; } = new();

public sealed class ServerSettings
{
    [JsonPropertyName("http")] public HttpServerSettings Http { get; set; } = new();
    [JsonPropertyName("tftp")] public TftpServerSettings Tftp { get; set; } = new();
}
```

`HttpServerSettings`: `Port` (80), `RootDirectory` (empty means the user's
Documents folder), `AllowDirectoryListing` (true),
`EnableRuntimeLimit` (true), `RuntimeSeconds` (3600).
`TftpServerSettings`: `Port` (69), `RootDirectory`,
`ShowDownloadInfo` (true), `ShowUploadInfo` (true),
`EnableRuntimeLimit` (true), `RuntimeSeconds` (3600).

`MainWindow` passes its `AppConfigService` to `ServersManagementWindow`
through the catalog factory; the window writes the settings back and saves
when a server starts and when the window closes. No secrets are stored
(no passwords exist in this tool).

## Removals

- `SessionSidebar.axaml`: the tool group-header template, the embedded
  `NetworkScannerView` and `PortScannerView` grids, and the `← 返回`
  sub-view button (all tool pages are windows now).
- `SessionSidebar.axaml.cs`: `NetScanStartRequested`,
  `PortScanStartRequested`, `PortScanStopRequested`, `ToolCloseRequested`
  and their handlers; replace with `ToolRequested(string id)`.
- `MainViewModel.Sidebar.cs`: the `ToolsView` enum, `ScanOctet*`,
  `ScanStatus`, `ScanError`, `IsScanning`, `ScanRows`, `PortScan*`,
  `ScanGridRow`, `PortCell`, `IsBusy`, `HasScanRows`, and the related
  property notifications. `PortCell`/`ScanGridRow` move to
  `NetworkScanWindowViewModel`.
- `MainWindow.axaml.cs`: `_scanCts`, `_portScanCts`, `_scanner`,
  `NetworkScannerForTests`, `ActiveScanner`, the four scan handlers,
  `FormatPortResults`, `ToScanGridRow`, and the `UiProgress<T>` helper
  (moved to `Tools/UiProgress.cs` as `internal`).
- `AppStyles.axaml`: the `ToolMenuItemButton:disabled` opacity rule
  (there are no disabled menu items now; future disabled tools use gray
  text, keeping icons colored).

## Testing

New/updated automated tests:

- `SidebarRailTests.UI16_ToolsMenuMatchesMobaXtermDropdown`: exactly three
  rows with the names/glyphs/colors above, all enabled, no headers;
  clicking each opens the matching window and a second click activates the
  existing instance.
- `SidebarRailTests.UI16_NetScanDefaultsMatchMobaXterm`: defaults live on
  `NetworkScanWindowViewModel` (192/168/1/1/254).
- `SidebarRailTests.NET05_ScanInvalidInputShowsError`,
  `NET05_ScanShowsServiceGridResults`, `NET05_NetScanCloseCancelsScan`:
  drive `NetworkScanWindow` headlessly with a fake scanner.
- `SidebarRailTests.PORT01_PortScanKnownModeFormatsListeningLines`,
  `PORT01_PortScanRangeModeScansDeep`, `PORT01_PortScanStopAndInvalidInput`:
  drive `PortScanWindow` headlessly with a fake scanner.
- `SidebarRailTests.SRV01_ServersManagementShowsNineRows`,
  `SRV01_HttpConfigStartsWithStopAndLog`,
  `SRV01_SelectingRowShowsMatchingConfig`: the dialog structure, welcome
  pane, row selection, and the HTTP/TFTP panels, with an injected fake
  server host.
- `COMTool.Connections.Tests.HttpFileServerTests`: serve a file, listing
  on/off, 404, traversal rejection, port binding error, stop closes the
  listener.
- `COMTool.Connections.Tests.TftpServerTests`: RRQ multi-block download,
  missing file ERROR, WRQ upload.
- Config round-trip test for `ServerSettings` in `COMTool.Core.Tests`.

Test names stay in `ClassName.MethodName` form so `COMTool.TestGate`
continues to resolve them; `docs/testing/feature-test-matrix.md` is
updated in the same commit.

## Acceptance

- Tools menu shows exactly 网络服务器, 网络扫描仪, 端口扫描仪; each opens its
  own window; a second click focuses the open window.
- The two scan windows reproduce the previously approved layouts and
  behavior; closing cancels scanning; no scan state remains in
  `MainViewModel`.
- `Servers management` matches the measured layout (nine rows, three
  buttons per row, welcome pane, 确定) and HTTP/TFTP start, serve, log,
  honor the runtime limit and stop cleanly.
- The seven unimplemented rows are visible, disabled, and explain
  "暂不支持（框架已预留）".
- All existing tests keep passing; the new tests above pass; the full
  gate stays green and the Windows single-file build deploys in place with
  no `.bak-*` files.
