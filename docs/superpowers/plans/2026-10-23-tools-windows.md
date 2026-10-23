# Tools Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 19-entry tool menu with three standalone MobaXterm-style windows (network servers management, network scanner, port scanner) behind a small tool catalog, with working HTTP and TFTP servers.

**Architecture:** Each tool is a self-contained `Window` with its own view model and lifecycle; a static `ToolCatalog` maps tool ids to window factories and drives the sidebar menu. Server engines live in `COMTool.Connections.Servers` behind an `IServerHost` interface; windows own cancellation, timers and logs.

**Tech Stack:** .NET 8, Avalonia 11, CommunityToolkit.Mvvm, xUnit + Avalonia.Headless, raw `TcpListener`/`UdpClient` sockets.

**Spec:** `docs/superpowers/specs/2026-10-23-tools-windows-design.md`

## Global Constraints

- Version stays `0.01`; Windows artifact remains `COMTool-Avalonia.exe` (single-file, R2R, win-x64).
- Only the existing DPAPI `passwd_enc` mechanism may persist secrets; this feature stores no secrets.
- Deployment is in-place overwrite; no `.bak-*` files; `BAK_COUNT=0` after deploy.
- Every feature needs automated tests; test names stay `ClassName.MethodName` (TestGate).
- Chinese UI labels and tooltips; menu icons are FontAwesome 6 solid glyphs already bundled.
- No administrator privileges: the HTTP server must not use `HttpListener`.
- Do not push or publish without explicit confirmation.
- Never commit or log plaintext passwords.

## Review Focus

- HTTP requests with percent-encoded or `..` paths must never escape the root directory (403/404, never file contents).
- A busy port (HTTP 80 / TFTP 69 are likely taken) must leave the server stopped, keep the dialog usable, and show the error in the server output log.
- Closing the servers dialog or the scan windows while running must stop listeners and cancel scans; no background thread may keep the process alive.
- TFTP transfers of files larger than one 512-byte block must survive one lost ACK (retransmit) and complete with a zero-length block.
- Clicking a tool menu item twice must focus the existing window, not open a duplicate.

---

### Task 1: Network scan window

**Files:**
- Create: `avalonia/src/COMTool.App/ViewModels/Tools/NetworkScanWindowViewModel.cs`
- Create: `avalonia/src/COMTool.App/Tools/UiProgress.cs`
- Create: `avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml`
- Create: `avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml.cs`
- Modify: `avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs:119-152` (defaults test) and `:615-759` (NET05 tests)

**Interfaces:**
- Consumes: `INetworkScanner`, `NetworkServices.AllProbePorts`, `NetworkScanTargets.TryParseSubnet`.
- Produces:
  - `NetworkScanWindowViewModel` : `ObservableObject` with `Octet1="192"`, `Octet2="168"`, `Octet3="1"`, `OctetFrom="1"`, `OctetTo="254"`, `Status`, `Error`, `IsScanning`, `ObservableCollection<ScanGridRow> Rows`, plus `ScanGridRow`/`PortCell` moved from `MainViewModel.Sidebar.cs`.
  - `NetworkScanWindow` : `Window` with `internal NetworkScanWindowViewModel ViewModel`, `internal INetworkScanner? ScannerForTests { get; set; }`, `internal Task StartScanForTestsAsync()` (used by tests; the button calls the same method).
  - `internal sealed class UiProgress<T>(Action<T> action) : IProgress<T>` posting to the UI thread.

- [ ] **Step 1: Write the failing tests**

In `SidebarRailTests.cs`, replace the bodies of `NET05_ScanInvalidInputShowsError`, `NET05_ScanShowsServiceGridResults`, `NET05_NetScanCloseCancelsScan` and `UI16_NetScanDefaultsMatchMobaXterm` so they instantiate the window directly (no `MainWindow`):

```csharp
var window = new NetworkScanWindow { ScannerForTests = new FakeScanner(hosts) { DelayMs = 1 } };
window.Show();
Dispatcher.UIThread.RunJobs();
var vm = window.ViewModel;
```

Assertions to keep verbatim from the current tests: invalid octets set `vm.Error` and leave `Rows` empty; two hosts produce 2 rows with 7 cells, SSH/HTTP open, `OtherPorts == "443"`, final `Status == "完成：发现 2 台在线设备"`; closing the window cancels (a 5000 ms fake scanner) and `Status` starts with `已停止`; defaults are 192/168/1/1/254 expanding to 254 targets.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `PATH=/tmp/opencode/e2e-deps/usr/bin:$PATH DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests --filter "FullyQualifiedName~SidebarRailTests.NET05|FullyQualifiedName~UI16_NetScanDefaults"`
Expected: compile error, `NetworkScanWindow` not found.

- [ ] **Step 3: Implement the view model**

Create `NetworkScanWindowViewModel.cs` with the moved `ScanGridRow`/`PortCell` types (same members as the current definitions in `MainViewModel.Sidebar.cs`) and the eight observable properties above. `Status`/`Error`/`IsScanning` notify `HasRows` as needed.

- [ ] **Step 4: Implement the window and XAML**

`NetworkScanWindow.axaml`: `Title="网络扫描" Width="880" Height="540" MinWidth="700" MinHeight="420" CanResize="True" ShowInTaskbar="False" WindowStartupLocation="CenterOwner" Background="{DynamicResource WindowBackgroundBrush}" FontSize="12"`.

Layout: top `Border Classes="SidebarCard"` with `IP 地址范围` label, five `TextBox`es (Width 46) named `ScanOctet1Box`…`ScanOctetToBox`, dot labels, `⟶`, and a `▶ Start scan` button named `ScanStartButton`; middle `Border` with a 10-column header grid (IP 地址 120, 名称 90, SSH/RDP/VNC/FTP/Telnet/Rlogin/HTTP 60 each, 其他端口 140) whose service headers carry colored glyphs (SSH `\uf120`, RDP `\uf108`, VNC `\uf2d0`, FTP `\uf07b`, Telnet `\uf120`, Rlogin `\uf0c0`, HTTP `\uf0ac`), an `ItemsControl` of `Rows` reusing the same column widths, and a status line; bottom `✗ 关闭` button named `CloseButton` (`\uf00d` white + 关闭) centered.

Code-behind: `DataContext = new NetworkScanWindowViewModel()`; `ScannerForTests` backing field; `StartScanForTestsAsync()` validates via `NetworkScanTargets.TryParseSubnet`, clears rows, sets `Status="扫描中… 0/N"`, creates a CTS, calls `Scanner.ScanAsync(targets, NetworkServices.AllProbePorts, progress, token)` with a `UiProgress<NetworkScanProgress>` that appends `ToScanGridRow(host)` and updates `Status`, and on completion/cancel/exception writes `完成：发现 N 台在线设备` / `已停止（已发现 N 台）` / `Error = ex.Message`. `OnClosed` cancels and disposes the CTS.

- [ ] **Step 5: Run the tests and make them pass**

Run: `... dotnet test avalonia/tests/COMTool.App.Tests --filter "FullyQualifiedName~SidebarRailTests.NET05|FullyQualifiedName~UI16_NetScanDefaults"`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add avalonia/src/COMTool.App/ViewModels/Tools/NetworkScanWindowViewModel.cs avalonia/src/COMTool.App/Tools/UiProgress.cs avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml.cs avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs
git commit -m "feat: add standalone network scan window"
```

---

### Task 2: Port scan window

**Files:**
- Create: `avalonia/src/COMTool.App/ViewModels/Tools/PortScanWindowViewModel.cs`
- Create: `avalonia/src/COMTool.App/Views/PortScanWindow.axaml`
- Create: `avalonia/src/COMTool.App/Views/PortScanWindow.axaml.cs`
- Modify: `avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs:761-923` (PORT01 tests)

**Interfaces:**
- Consumes: `INetworkScanner.ScanHostPortsAsync` (both overloads), `NetworkServices.KnownPortNumbers`, `NetworkScanTargets.TryParseIp`, `UiProgress<T>`.
- Produces: `PortScanWindowViewModel` with `Ip`, `RangeMode` (default false), `From="1"`, `To="65535"`, `Status`, `Error`, `IsScanning`, `Result`; `PortScanWindow` with `internal PortScanWindowViewModel ViewModel`, `internal INetworkScanner? ScannerForTests`, `internal Task StartScanForTestsAsync()`, `internal void StopScan()`.

- [ ] **Step 1: Write the failing tests**

Rewrite `PORT01_PortScanKnownModeFormatsListeningLines`, `PORT01_PortScanRangeModeScansDeep`, `PORT01_PortScanStopAndInvalidInput` to create `new PortScanWindow { ScannerForTests = ... }`, `Show()`, then set `vm.Ip`, toggle `vm.RangeMode`, and call/click start. Keep the exact expectations: known mode with open ports {22,80} formats `"- Port #22 (ssh): listening\n- Port #80 (http): listening"`; range mode uses the deep overload; stop yields `"扫描已停止"`; `"not-an-ip"`, and from>to set `Error` with `IsScanning == false`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `... dotnet test avalonia/tests/COMTool.App.Tests --filter "FullyQualifiedName~SidebarRailTests.PORT01"`
Expected: compile error, `PortScanWindow` not found.

- [ ] **Step 3: Implement the view model**

Create `PortScanWindowViewModel` with the properties above; `RangeMode` toggles `IsKnownMode => !RangeMode`.

- [ ] **Step 4: Implement the window and XAML**

`PortScanWindow.axaml`: `Title="网络扫描" Width="820" Height="480" MinWidth="640" MinHeight="380" CanResize="True" ShowInTaskbar="False" WindowStartupLocation="CenterOwner"`.

Top `Border` with a 1px border: `IP 地址:` + `TextBox Name="PortScanIpBox"` (Width 240); `RadioButton Name="PortScanKnownRadio" Content="仅扫描已知端口"` and `RadioButton Name="PortScanRangeRadio" Content="扫描端口范围"`, a horizontal range row `[TextBox Name="PortScanFromBox" Width=60] --> [TextBox Name="PortScanToBox" Width=60]` enabled only in range mode, `▶ Start scan` (`PortScanStartButton`) and `停止` (`PortScanStopButton`, enabled only while scanning). Middle: read-only `TextBox Name="PortScanResultBox"` (`AcceptsReturn="True"`, monospaced, vertical scrollbar auto) bound to `Result`, plus status/error text. Bottom: `✗ 关闭` (`PortScanCloseButton`) centered.

Code-behind: same lifecycle as Task 1, using `ScanHostPortsAsync(host, from, to, ...)` in range mode and `ScanHostPortsAsync(host, NetworkServices.KnownPortNumbers, ...)` otherwise; `FormatPortResults` moved here from `MainWindow` (0 ports → `未发现开放端口`). Closing cancels.

- [ ] **Step 5: Run the tests and make them pass**

Run: `... dotnet test avalonia/tests/COMTool.App.Tests --filter "FullyQualifiedName~SidebarRailTests.PORT01"`
Expected: 3 passed.

- [ ] **Step 6: Commit**

```bash
git add avalonia/src/COMTool.App/ViewModels/Tools/PortScanWindowViewModel.cs avalonia/src/COMTool.App/Views/PortScanWindow.axaml avalonia/src/COMTool.App/Views/PortScanWindow.axaml.cs avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs
git commit -m "feat: add standalone port scan window"
```

---

### Task 3: HTTP file server engine

**Files:**
- Create: `avalonia/src/COMTool.Connections/Servers/IServerHost.cs`
- Create: `avalonia/src/COMTool.Connections/Servers/HttpFileServer.cs`
- Test: `avalonia/tests/COMTool.Connections.Tests/HttpFileServerTests.cs`

**Interfaces:**
- Produces: `IServerHost` exactly as in the spec; `HttpFileServer(string rootDirectory, int port, bool allowDirectoryListing)` with `Name => "HTTP server"`, `IsRunning`, `Port`, `event Action<string>? LogMessage`, `StartAsync`, `StopAsync`, `DisposeAsync`.

- [ ] **Step 1: Write the failing tests**

```csharp
[AvaloniaFact] // or [Fact]; no UI needed
public async Task ServesFileWithContentType()
{
    using var dir = new TempDir();
    File.WriteAllText(Path.Combine(dir.Path, "hello.txt"), "hello");
    await using var server = new HttpFileServer(dir.Path, 0, allowDirectoryListing: true);
    await server.StartAsync();
    var text = await new HttpClient().GetStringAsync($"http://127.0.0.1:{server.Port}/hello.txt");
    Assert.Equal("hello", text);
}
```

Also: `ServesDirectoryListingOnlyWhenEnabled` (listing true → body contains `hello.txt`; false → 403), `RejectsTraversal` (`/../secret.txt` outside root → 403), `MissingFileReturns404`, `StopClosesListener` (after stop, a `TcpClient` connect to the port fails), `BusyPortThrows` (bind the port with another listener first, then `StartAsync` throws).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `... dotnet test avalonia/tests/COMTool.Connections.Tests --filter "FullyQualifiedName~HttpFileServerTests"`
Expected: compile error.

- [ ] **Step 3: Implement the engine**

Implement as specified: `TcpListener(IPAddress.Any, port)`, `port == 0` allowed (read `((IPEndPoint)listener.LocalEndpoint).Port` after `Start`), accept loop on a background task, request parse capped at 8 KB, `GET`/`HEAD` only, percent-decode + normalize with `Path.GetFullPath` and a root-prefix check (case-insensitive on Windows, ordinal on Linux), file or directory response, MIME map, `Connection: close`, and one log line per request (`GET /path 200`). `StartAsync` logs `Starting HTTP server, please wait...` then `HTTP server started`; `StopAsync` logs `Stopping HTTP server, please wait...` then `HTTP server stopped`. Exceptions on bind propagate to the caller.

- [ ] **Step 4: Run the tests and make them pass**

Run: the Step 2 command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add avalonia/src/COMTool.Connections/Servers avalonia/tests/COMTool.Connections.Tests/HttpFileServerTests.cs
git commit -m "feat: add HTTP file server engine"
```

---

### Task 4: TFTP server engine

**Files:**
- Create: `avalonia/src/COMTool.Connections/Servers/TftpServer.cs`
- Test: `avalonia/tests/COMTool.Connections.Tests/TftpServerTests.cs`

**Interfaces:**
- Produces: `TftpServer(string rootDirectory, int port, bool showDownloadInfo, bool showUploadInfo)` implementing `IServerHost` (`Name => "TFTP server"`); `Port` reflects the bound port (0 → assigned).

- [ ] **Step 1: Write the failing tests**

A minimal in-test TFTP client helper: send RRQ (`opcode 1`, `filename\0octet\0`) to `127.0.0.1:server.Port`, then loop: receive DATA, write payload, send ACK(block), until payload < 512. Tests:

- `ReadRequestSendsFileInBlocks`: 700-byte file arrives intact, blocks 1 and 2, final DATA length < 512.
- `ReadRequestMissingFileReturnsError`: ERROR opcode 5 with code 1.
- `WriteRequestStoresFile`: WRQ `upload.bin`, receive ACK(0), send DATA(1, 600 bytes), ACK(2, 88 bytes), then the file on disk equals the sent bytes.
- `TraversalFileNameIsRejected`: RRQ `../evil.txt` → ERROR code 2.

Use a 2 s receive timeout so failures do not hang the suite.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `... dotnet test avalonia/tests/COMTool.Connections.Tests --filter "FullyQualifiedName~TftpServerTests"`
Expected: compile error.

- [ ] **Step 3: Implement the engine**

Implement RRQ/WRQ as specified: `UdpClient` bound to `IPAddress.Any:port`, receiver loop with `ReceiveAsync`, per-transfer ephemeral `UdpClient` for the TID, 512-byte DATA blocks, ACK wait with 1 s timeout and 3 retransmissions, zero-length terminal block, ERROR frames (1 file not found, 2 access violation, 4 illegal operation), root-prefix traversal check, octet mode only, and the four lifecycle log messages. Transfers log only when the matching flag is enabled.

- [ ] **Step 4: Run the tests and make them pass**

Run: the Step 2 command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add avalonia/src/COMTool.Connections/Servers/TftpServer.cs avalonia/tests/COMTool.Connections.Tests/TftpServerTests.cs
git commit -m "feat: add TFTP server engine"
```

---

### Task 5: Server settings persistence

**Files:**
- Create: `avalonia/src/COMTool.Core/Config/ServerSettings.cs`
- Modify: `avalonia/src/COMTool.Core/Config/ComtoolConfig.cs` (add the `servers` property)
- Test: `avalonia/tests/COMTool.Core.Tests/ServerSettingsTests.cs`

**Interfaces:**
- Produces: `ServerSettings`, `HttpServerSettings`, `TftpServerSettings` classes with the exact defaults from the spec; `ComtoolConfig.Servers` typed `ServerSettings`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void ServerSettingsRoundTrip()
{
    var config = new ComtoolConfig();
    config.Servers.Http.Port = 8080;
    config.Servers.Http.RootDirectory = @"C:\share";
    config.Servers.Tftp.Port = 1069;
    config.Servers.Tftp.ShowUploadInfo = false;
    var json = JsonSerializer.Serialize(config);
    var back = JsonSerializer.Deserialize<ComtoolConfig>(json)!;
    Assert.Equal(8080, back.Servers.Http.Port);
    Assert.Equal(@"C:\share", back.Servers.Http.RootDirectory);
    Assert.Equal(1069, back.Servers.Tftp.Port);
    Assert.False(back.Servers.Tftp.ShowUploadInfo);
    Assert.True(back.Servers.Tftp.EnableRuntimeLimit);
    Assert.Equal(3600, back.Servers.Tftp.RuntimeSeconds);
}
```

Also assert a config JSON without `servers` deserializes to the defaults.

- [ ] **Step 2: Run the test to verify it fails**

Run: `... dotnet test avalonia/tests/COMTool.Core.Tests --filter "FullyQualifiedName~ServerSettingsTests"`
Expected: compile error.

- [ ] **Step 3: Implement the settings classes**

Implement the three classes with `[JsonPropertyName]` attributes and the spec defaults; add `Servers` to `ComtoolConfig` with the `servers` JSON name.

- [ ] **Step 4: Run the test and make it pass**

Run: the Step 2 command. Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add avalonia/src/COMTool.Core/Config avalonia/tests/COMTool.Core.Tests/ServerSettingsTests.cs
git commit -m "feat: persist server settings in the config file"
```

---

### Task 6: Servers management window

**Files:**
- Create: `avalonia/src/COMTool.App/ViewModels/Tools/ServersManagementViewModel.cs`
- Create: `avalonia/src/COMTool.App/Views/ServersManagementWindow.axaml`
- Create: `avalonia/src/COMTool.App/Views/ServersManagementWindow.axaml.cs`
- Test: `avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs` (append SRV01 tests)

**Interfaces:**
- Consumes: `IServerHost`, `HttpFileServer`, `TftpServer`, `ServerSettings`, `AppConfigService` (optional).
- Produces:
  - `ServersManagementWindow(AppConfigService? config = null)` with `internal ServersManagementViewModel ViewModel`, `internal Func<string, IServerHost>? ServerFactoryForTests` (id → host; overrides the real engines).
  - `ServersManagementViewModel`: `ObservableCollection<ServerRowViewModel> Rows` (9, fixed order), `ServerRowViewModel? Selected`, `bool IsWelcome`, `bool IsHttpSelected`, `bool IsTftpSelected`, `HttpServerSettings Http`, `TftpServerSettings Tftp`.

- [ ] **Step 1: Write the failing tests**

Append three `[AvaloniaFact]` methods:

- `SRV01_ServersManagementShowsNineRows`: window shown; `Rows.Count == 9`; names exactly `TFTP 服务器, FTP 服务器, HTTP 服务器, SSH/SFTP 服务器, Telnet 服务器, NFS 服务器, VNC 服务器, Cron 服务器, Iperf 服务器`; HTTP/TFTP `IsImplemented` true and the other seven false; `IsWelcome` true initially; `确定` button exists.
- `SRV01_HttpConfigStartsWithStopAndLog`: select HTTP (⚙), assert `IsHttpSelected`; inject a fake host; click start → fake `StartAsync` called, log contains `HTTP server started`, `IsRunning` true, row shows a countdown when enabled; click stop → log contains `HTTP server stopped`, `IsRunning` false.
- `SRV01_SelectingRowShowsMatchingConfig`: select TFTP → `IsTftpSelected` and the panel shows `监听端口` default 69; select HTTP → port default 80 and `允许用户列出文件夹内容` checked.
- Also assert failure handling and shutdown: a factory whose `StartAsync` throws leaves `IsRunning == false` and appends the exception message to the HTTP log; closing the window calls `StopAsync` on every running fake host exactly once.

Use a `FakeServerHost : IServerHost` test double with `RaiseLog` and call counters.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `... dotnet test avalonia/tests/COMTool.App.Tests --filter "FullyQualifiedName~SidebarRailTests.SRV01"`
Expected: compile error.

- [ ] **Step 3: Implement the view model**

`ServerRowViewModel`: `Id`, `Name`, `IsImplemented`, `IsRunning`, `RemainingSeconds`, `ConfigKind`; the **view model** (not the row) owns one `IServerHost` instance per implemented server. Rows call `ServersManagementViewModel.StartAsync(row)` / `StopAsync(row)`, which create the host via the injected factory (or `new HttpFileServer(...)` / `new TftpServer(...)`), subscribe `LogMessage` (prefixing `[HH:mm:ss] `), append to the row's `Log` collection, and start a 1-second `DispatcherTimer` when `EnableRuntimeLimit` is set; at zero it stops the server and logs `N秒运行时间到，服务器已停止`. Any exception from `StartAsync`/`StopAsync` is caught there, appended to the log, and leaves `IsRunning` false.

- [ ] **Step 4: Implement the window and XAML**

`ServersManagementWindow.axaml`: `Title="Servers management" Width="734" Height="445" CanResize="False" ShowInTaskbar="False" WindowStartupLocation="CenterOwner"`; `Grid ColumnDefinitions="300,*" RowDefinitions="*,56"`. Left: `ItemsControl` of `Rows` with a `Grid` per row (label at left margin 22, three 23x20 buttons `Classes="ServerButton"`, countdown `(N)`); button `IsEnabled` bound to `IsImplemented` and a `ToolTip.Tip` of `暂不支持（框架已预留）` when disabled. Right: a `Border` (Background `#FFFFFF` in light, `{DynamicResource WindowBackgroundBrush}` in dark via theme resource, BorderBrush `#A0A0A0`, BorderThickness 1) with three stacked panels toggled by `IsWelcome`/`IsHttpSelected`/`IsTftpSelected`: welcome pane (red `网络服务器`, centered `欢迎使用` / `MobaXterm 网络服务器`, and a `Canvas` molecule: center `#2b3f9e` r=13, six `#22c3e6` r=10 circles at 60° on r=35, six `#d9d9d9` 4px lines); HTTP panel (title, 根目录 + `浏览…`, listing checkbox, port `NumericUpDown`, runtime checkbox + seconds, log `ListBox`); TFTP panel (title, 根目录, two info checkboxes, port, runtime, log). Bottom: centered `✓ 确定` button.

Code-behind: construct the view model with the config service, save on start and on close, stop all hosts on `Closing`/`Closed`, and show a folder picker via `StorageProvider.OpenFolderPickerAsync` for the two `浏览…` buttons, writing the selected path into the corresponding `RootDirectory`.

- [ ] **Step 5: Run the tests and make them pass**

Run: `... dotnet test avalonia/tests/COMTool.App.Tests --filter "FullyQualifiedName~SidebarRailTests.SRV01"`
Expected: 3 passed.

- [ ] **Step 6: Commit**

```bash
git add avalonia/src/COMTool.App/ViewModels/Tools/ServersManagementViewModel.cs avalonia/src/COMTool.App/Views/ServersManagementWindow.axaml avalonia/src/COMTool.App/Views/ServersManagementWindow.axaml.cs avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs
git commit -m "feat: add servers management window with HTTP and TFTP"
```

---

### Task 7: Tool catalog, three-item menu, and removal of the old tool UI

**Files:**
- Create: `avalonia/src/COMTool.App/Tools/ToolDescriptor.cs`
- Create: `avalonia/src/COMTool.App/Tools/ToolCatalog.cs`
- Modify: `avalonia/src/COMTool.App/ViewModels/MainViewModel.Sidebar.cs` (menu + deletions)
- Modify: `avalonia/src/COMTool.App/Views/SessionSidebar.axaml:236-625` (menu only; delete both embedded views and the back button)
- Modify: `avalonia/src/COMTool.App/Views/SessionSidebar.axaml.cs` (one `ToolRequested` event)
- Modify: `avalonia/src/COMTool.App/Views/MainWindow.axaml.cs:42-44, 93-96, 3208-3425` (window registry; delete scan code)
- Modify: `avalonia/src/COMTool.App/Styles/AppStyles.axaml:186-188` (drop the disabled opacity rule)
- Modify: `avalonia/src/COMTool.App/Styles/ThemeDark.axaml`, `ThemeLight.axaml` (remove the now-unused `SidebarGroupHeaderBrush`)
- Modify: `avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs:154-245` (UI16 menu test)
- Modify: `docs/testing/feature-test-matrix.md`, `docs/parity-matrix.md`, `docs/testing/manual-acceptance.md`

**Interfaces:**
- Consumes: all three windows from Tasks 1, 2, 6.
- Produces: `ToolDescriptor(Id, Name, Glyph, GlyphColor, IsEnabled, Func<MainWindow, Window> CreateWindow)`; `ToolCatalog.All` (net-server, scanner, port-scanner in that order) and `ToolCatalog.Find(id)`; `SessionSidebar.ToolRequested` (`EventHandler<string>`).

- [ ] **Step 1: Write the failing menu test**

Rewrite `UI16_ToolsMenuMatchesMobaXtermDropdown`:

```csharp
vm.SidebarTab = SidebarTab.Tools;
RunJobs(window);
Assert.Equal(3, vm.ToolMenu.Count);
Assert.Equal(new[] { "网络服务器", "网络扫描仪", "端口扫描仪" }, vm.ToolMenu.Select(t => t.Name));
Assert.Equal(new[] { "\uf013", "\uf7c0", "\uf1eb" }, vm.ToolMenu.Select(t => t.Glyph));
Assert.All(vm.ToolMenu, t => Assert.True(t.IsEnabled));
Click(window, MenuButton(window, "scanner"));
Assert.NotNull(window.FindToolWindowForTests("scanner"));   // Window instance
Click(window, MenuButton(window, "scanner"));               // activates, does not duplicate
Assert.Single(window.ToolWindowsForTests);
Click(window, MenuButton(window, "port-scanner"));
Click(window, MenuButton(window, "net-server"));
Assert.Equal(3, window.ToolWindowsForTests.Count);
```

`MainWindow` exposes `internal IReadOnlyCollection<Window> ToolWindowsForTests => _toolWindows.Values;` and `internal Window? FindToolWindowForTests(string id) => _toolWindows.GetValueOrDefault(id);`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `... dotnet test avalonia/tests/COMTool.App.Tests --filter "FullyQualifiedName~UI16_ToolsMenu"`
Expected: FAIL (`ToolMenu.Count` is 22).

- [ ] **Step 3: Implement the catalog and menu**

`ToolCatalog.All` returns the three descriptors in the order above, each `IsEnabled = true` and each factory constructing the window:

```csharp
new("net-server", "网络服务器", "\uf013", "#00897b", true, owner => new ServersManagementWindow(owner.ConfigService)),
new("scanner", "网络扫描仪", "\uf7c0", "#5e35b1", true, _ => new NetworkScanWindow()),
new("port-scanner", "端口扫描仪", "\uf1eb", "#2196f3", true, _ => new PortScanWindow()),
```

`MainWindow` gains `internal AppConfigService? ConfigService => _config;`, the `_toolWindows` dictionary, `OnToolRequested` (open or activate; remove on `Closed`; `RestoreTerminalFocus()`), and drops the four old subscriptions.

`MainViewModel.InitToolMenu()` builds `ToolMenu` from `ToolCatalog.All` (`ToolTip` = `Name`), and the sidebar's `OnToolMenuClick` raises `ToolRequested` with the tag.

- [ ] **Step 4: Delete the old tool UI and state**

Remove from `SessionSidebar.axaml` the `NetworkScannerView`/`PortScannerView` grids and the back button; remove from `MainViewModel.Sidebar.cs` the `ToolsView` enum, all scan properties/collections and their partial-changed handlers; remove from `MainWindow.axaml.cs` the scan fields/handlers/`UiProgress`/`ToScanGridRow`/`FormatPortResults`; drop the `ToolMenuItemButton:disabled` opacity style and the group-header resources.

- [ ] **Step 5: Run the full gate**

Run: `cd /tmp/opencode/COMTool && PATH=/tmp/opencode/e2e-deps/usr/bin:$PATH XVFB_BIN=/tmp/opencode/rdp-tools/root/usr/bin/Xvfb WM_BIN=/usr/bin/mutter DOTNET_ROOT=/tmp/opencode/dotnet DOTNET=/tmp/opencode/dotnet/dotnet bash avalonia/pack/verify.sh`
Expected: `summary.txt` reports all suites passed (previously 388 tests, now 388 + the new ones) and `TestGate PASS`.

- [ ] **Step 6: Update docs and commit**

Update `feature-test-matrix.md` with the same `ClassName.MethodName` rows and the correct expected counts; update `parity-matrix.md` (tool page = 3 windows, servers management with HTTP/TFTP) and `manual-acceptance.md` (new steps for opening each window, starting/stopping HTTP/TFTP).

```bash
git add -A
git commit -m "feat: reduce tools menu to three window-based tools"
```

---

### Task 8: Publish and deploy

**Files:**
- Modify: `/tmp/opencode/deploy-singlepane.ps1` (`$expected` hash)
- Modify: `docs/packaging/avalonia-windows-beta.md`

- [ ] **Step 1: Publish** with the established command (self-contained, single file, R2R, win-x64) and copy the artifact to `/tmp/opencode/publish-singlepane/COMTool.App.exe`.
- [ ] **Step 2: Verify the artifact** — size and `sha256sum`; update `$expected` in `deploy-singlepane.ps1` and re-read it to confirm all 64 hex characters.
- [ ] **Step 3: Deploy in place** to `C:\Users\zhangrui\COMTool-Avalonia.exe` and the desktop copy, verify both hashes match, `BAK_COUNT=0`, and the smoke run succeeds.
- [ ] **Step 4: Record** the version/hash/size in `docs/packaging/avalonia-windows-beta.md` and commit.
- [ ] **Step 5: Report** the result; do not push or publish externally without confirmation.
