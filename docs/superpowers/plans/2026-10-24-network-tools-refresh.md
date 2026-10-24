# Network Tools Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the network scanner on seven fixed service ports, make scans stoppable, align numeric controls in the scanner and network server, and detach tool windows from the main window.

**Architecture:** Remove the extra-probe data path from `NetworkServices`/`ScanGridRow` and use `ServicePorts` only in the scanner. Add a stop action and harden cancellation in `NetworkScanner`. Apply centering through XAML styles and change the shared tool-window opener from owned `Show(this)` to unowned `Show()`, with explicit lifetime cleanup.

**Tech Stack:** .NET 10, Avalonia 12.1.4, CommunityToolkit.Mvvm, xUnit + Avalonia.Headless.

**Spec:** `docs/superpowers/specs/2026-10-24-network-tools-refresh-design.md`

## Global Constraints

- Version stays `0.01`; Windows artifact is win-x64 single-file R2R.
- Deploy in place; no `.bak-*`; `BAK_COUNT=0`.
- Test names are `ClassName.MethodName`; every behavior needs a test.
- Do not push or publish without explicit confirmation.
- Chinese UI labels and tooltips.
- Never log plaintext passwords or secrets.

## Review Focus

- Stop pressed while probes are in flight must cancel promptly and must not be overwritten by a late “完成” progress callback.
- Closing the main window while a scan or HTTP/TFTP server is running must close/cancel everything; no background work may keep the process alive.
- A second click on a tool menu item must activate the existing window, never create a duplicate; a detached window must be able to sit behind the main window.
- Numeric controls must remain readable and aligned at the minimum window width; removing the “其他端口” column must not break the scanner grid header/row alignment.
- The port scanner’s known-port table must remain unchanged even though the network scanner’s extra probes are removed.

---

### Task 1: Limit network scan to seven service ports

**Files:**
- Modify: `avalonia/src/COMTool.Connections/NetworkScan/NetworkServices.cs`
- Modify: `avalonia/src/COMTool.App/ViewModels/Tools/NetworkScanWindowViewModel.cs`
- Modify: `avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml.cs`
- Modify: `avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml`
- Test: `avalonia/tests/COMTool.Connections.Tests/NetworkScannerTests.cs`
- Test: `avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs`

**Interfaces:**
- Consumes: `NetworkServices.ServiceColumns`, `NetworkServices.ServicePorts`, `NetworkScanHost.OpenPorts`.
- Produces: `ScanGridRow` with `Address`, `HostName`, `Cells` only; scanner probes exactly `NetworkServices.ServicePorts`.

- [x] **Step 1: Write the failing assertions**

In `NetworkScannerTests.NET05_ServiceColumnsMatchMobaXterm`, replace the `AllProbePorts` assertion with:

```csharp
Assert.Equal(
    new[] { 22, 3389, 5900, 21, 23, 513, 80 },
    NetworkServices.ServicePorts);
Assert.DoesNotContain(443, NetworkServices.ServicePorts);
```

In `SidebarRailTests.NET05_ScanShowsServiceGridResults`, remove the `OtherPorts` assertions and add:

```csharp
var header = window.FindControl<Grid>("ScanHeaderGrid")!;
Assert.Equal(9, header.ColumnDefinitions.Count);
Assert.DoesNotContain(
    window.GetVisualDescendants().OfType<TextBlock>(),
    text => text.Text == "其他端口");
```

The test host still has port 443 open, proving it is no longer surfaced.

- [x] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
DOTNET_ROOT=/tmp/opencode/dotnet PATH=/tmp/opencode/dotnet:$PATH \
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Connections.Tests \
  --filter "FullyQualifiedName~NetworkScannerTests.NET05_ServiceColumnsMatchMobaXterm"
DOTNET_ROOT=/tmp/opencode/dotnet PATH=/tmp/opencode/dotnet:$PATH \
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests \
  --filter "FullyQualifiedName~SidebarRailTests.NET05_ScanShowsServiceGridResults"
```

Expected: the app test fails because `ScanHeaderGrid`/nine columns/absent “其他端口” are not implemented; the scanner test still compiles but no longer asserts the old extra set.

- [x] **Step 3: Remove the extra-port data path**

In `NetworkServices.cs`, delete `OtherPorts` and `AllProbePorts`; update the class comment to say the scanner uses the fixed service table only. Keep `KnownPorts` and `KnownPortNumbers` untouched.

In `NetworkScanWindowViewModel.cs`, remove `OtherPorts` and `HasOtherPorts` from `ScanGridRow`.

In `NetworkScanWindow.axaml.cs`, use `NetworkServices.ServicePorts` instead of `AllProbePorts`, and remove the `OtherPorts` calculation from `ToScanGridRow`.

In `NetworkScanWindow.axaml`, name the header grid `x:Name="ScanHeaderGrid"`, use nine fixed columns (`120,90,60,60,60,60,60,60,60`) for both header and rows, and delete the “其他端口” header/cell. Update the layout comment.

- [x] **Step 4: Run the focused tests to verify they pass**

Run the two Step 2 commands. Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add avalonia/src/COMTool.Connections/NetworkScan/NetworkServices.cs \
  avalonia/src/COMTool.App/ViewModels/Tools/NetworkScanWindowViewModel.cs \
  avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml.cs \
  avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml \
  avalonia/tests/COMTool.Connections.Tests/NetworkScannerTests.cs \
  avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs
git commit -m "feat: limit network scan to fixed service ports"
```

---

### Task 2: Add stop and propagate scan cancellation

**Files:**
- Modify: `avalonia/src/COMTool.Connections/NetworkScan/NetworkScanner.cs`
- Modify: `avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml`
- Modify: `avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml.cs`
- Test: `avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs`
- Test: `avalonia/tests/COMTool.Connections.Tests/NetworkScannerTests.cs`

**Interfaces:**
- Consumes: `INetworkScanner.ScanAsync`, `CancellationTokenSource`.
- Produces: `NetworkScanWindow.StopScan()`; `NetScanStopButton`; progress callbacks ignore stale scans.

- [x] **Step 1: Write the failing stop test**

Add `SidebarRailTests.NET05_StopButtonCancelsScan`:

```csharp
var window = new NetworkScanWindow
{
    ScannerForTests = new FakeScanner(new List<NetworkScanHost>
    {
        new(new IPAddress(new byte[] { 127, 0, 0, 1 }), "loopback", new[] { 80 }, 1),
    }) { DelayMs = 50 },
};
window.Show();
Dispatcher.UIThread.RunJobs();
var vm = window.ViewModel;

Click(window, window.FindControl<Button>("ScanStartButton")!);
WaitUntil(() => vm.Rows.Count == 1, "first scan row");
var stop = window.FindControl<Button>("NetScanStopButton")!;
Assert.True(stop.IsEnabled);
Click(window, stop);
WaitUntil(() => !vm.IsScanning, "scan to stop");

Assert.Single(vm.Rows);
Assert.StartsWith("已停止", vm.Status);
Assert.False(stop.IsEnabled);
```

- [x] **Step 2: Run it to verify it fails**

```bash
DOTNET_ROOT=/tmp/opencode/dotnet PATH=/tmp/opencode/dotnet:$PATH \
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests \
  --filter "FullyQualifiedName~SidebarRailTests.NET05_StopButtonCancelsScan"
```

Expected: FAIL because `NetScanStopButton` is not found.

- [x] **Step 3: Implement stop and cancellation propagation**

Add a `NetScanStopButton` next to `ScanStartButton`, enabled with `{Binding IsScanning}`, using the same `SidebarActionButton` style and a “停止” label. Add `OnStopScanClick` and `internal void StopScan() => _cts?.Cancel();`.

In `StartScanAsync`, guard the progress callback with `if (!ReferenceEquals(_cts, cts)) return;` so late progress cannot overwrite the stopped/completed state.

In `NetworkScanner.cs`:
- `PingAsync`: await `SendPingAsync(ip, PingTimeoutMs).WaitAsync(ct)` and rethrow `OperationCanceledException` when `ct.IsCancellationRequested`.
- `ConnectAsync`: rethrow `OperationCanceledException` when the external `ct` is cancelled; keep timeout cancellation returning `false`.
- `ResolveAsync`: same cancellation filter around `WaitAsync(cts.Token)`.
- `ProbeAsync`: call `ct.ThrowIfCancellationRequested()` after `Task.WhenAll`.

- [x] **Step 4: Run stop, close, and scanner cancellation tests**

```bash
DOTNET_ROOT=/tmp/opencode/dotnet PATH=/tmp/opencode/dotnet:$PATH \
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests \
  --filter "FullyQualifiedName~SidebarRailTests.NET05"
DOTNET_ROOT=/tmp/opencode/dotnet PATH=/tmp/opencode/dotnet:$PATH \
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Connections.Tests \
  --filter "FullyQualifiedName~NetworkScannerTests.NET05_CancellationStopsScan|FullyQualifiedName~NetworkScannerTests.NET05_DeepScanWithCancelledToken"
```

Expected: PASS, including the existing close-cancels test.

- [x] **Step 5: Commit**

```bash
git add avalonia/src/COMTool.Connections/NetworkScan/NetworkScanner.cs \
  avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml \
  avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml.cs \
  avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs \
  avalonia/tests/COMTool.Connections.Tests/NetworkScannerTests.cs
git commit -m "feat: stop network scans and propagate cancellation"
```

---

### Task 3: Align numeric controls and detach tool windows

**Files:**
- Modify: `avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml`
- Modify: `avalonia/src/COMTool.App/Views/ServersManagementWindow.axaml`
- Modify: `avalonia/src/COMTool.App/Views/PortScanWindow.axaml`
- Modify: `avalonia/src/COMTool.App/Views/MainWindow.axaml.cs`
- Test: `avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs`

**Interfaces:**
- Consumes: `Window.Show()`, `Window.Activate()`, `WindowState`, `TextAlignment`.
- Produces: unowned tool windows with `CenterScreen`; centered numeric inputs; main-window close cleanup.

- [x] **Step 1: Write the failing alignment and ownership tests**

Add `using Avalonia.Media;` to `SidebarRailTests.cs`. Add `NET05_ScanNumericInputsAreCenteredAndEqualWidth`:

```csharp
var names = new[]
{
    "ScanOctet1Box", "ScanOctet2Box", "ScanOctet3Box",
    "ScanOctetFromBox", "ScanOctetToBox",
};
var boxes = names.Select(name => window.FindControl<TextBox>(name)!).ToArray();
Assert.All(boxes, box => Assert.Equal(TextAlignment.Center, box.TextAlignment));
Assert.All(boxes, box => Assert.Equal(52, box.Width));
```

Add `SRV01_NumericInputsAreCenteredAndShareWidth`:

```csharp
var names = new[] { "HttpPortBox", "HttpSecondsBox", "TftpPortBox", "TftpSecondsBox" };
var boxes = names.Select(name => window.FindControl<NumericUpDown>(name)!).ToArray();
Assert.All(boxes, box => Assert.Equal(TextAlignment.Center, box.TextAlignment));
Assert.All(boxes, box => Assert.Equal(120, box.Width));
```

In `UI16_ToolsMenuMatchesMobaXtermDropdown`, after creating each window, assert `Assert.Null(servers.Owner)`, `Assert.Null(scanner.Owner)`, and `Assert.Null(portScanner.Owner)`.

Add `UI16_MainWindowClosesDetachedTools` that opens the scanner from the tools menu, closes the main window, runs jobs, and asserts the scanner is no longer visible and the tool registry is empty.

- [x] **Step 2: Run the focused tests to verify they fail**

```bash
DOTNET_ROOT=/tmp/opencode/dotnet PATH=/tmp/opencode/dotnet:$PATH \
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests \
  --filter "FullyQualifiedName~SidebarRailTests.NET05_ScanNumericInputsAreCenteredAndEqualWidth|FullyQualifiedName~SidebarRailTests.SRV01_NumericInputsAreCenteredAndShareWidth|FullyQualifiedName~SidebarRailTests.UI16_ToolsMenuMatchesMobaXtermDropdown|FullyQualifiedName~SidebarRailTests.UI16_MainWindowClosesDetachedTools"
```

Expected: alignment tests fail on default left alignment / 132px second fields; ownership assertions fail because the windows are owned.

- [x] **Step 3: Apply alignment and window changes**

`NetworkScanWindow.axaml`: add a style for the five octet boxes with `Width=52`, `TextAlignment=Center`, `VerticalContentAlignment=Center`; set `WindowStartupLocation="CenterScreen"`. Keep the nine fixed grid columns from Task 1.

`ServersManagementWindow.axaml`: add `TextAlignment=Center` and `VerticalContentAlignment=Center` to the local `NumericUpDown` style; set all four port/seconds boxes to `Width=120`; set `WindowStartupLocation="CenterScreen"`.

`PortScanWindow.axaml`: set `WindowStartupLocation="CenterScreen"` (the only change from the shared window behavior).

`MainWindow.axaml.cs`: replace `window.Show(this)` with `window.Show()`; before activating an existing window, restore `WindowState.Normal` if it is minimized; in `OnClosed`, close every window in `_toolWindows.Values.ToList()` and clear the dictionary.

- [x] **Step 4: Run the focused tests to verify they pass**

Run the Step 2 command. Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add avalonia/src/COMTool.App/Views/NetworkScanWindow.axaml \
  avalonia/src/COMTool.App/Views/ServersManagementWindow.axaml \
  avalonia/src/COMTool.App/Views/PortScanWindow.axaml \
  avalonia/src/COMTool.App/Views/MainWindow.axaml.cs \
  avalonia/tests/COMTool.App.Tests/SidebarRailTests.cs
git commit -m "feat: align tool numbers and detach tool windows"
```

---

### Task 4: Docs, full gate, and Windows deployment evidence

**Files:**
- Modify: `docs/testing/feature-test-matrix.md`
- Modify: `docs/testing/manual-acceptance.md`
- Modify: `docs/packaging/avalonia-windows-beta.md` if evidence/hash needs updating
- Modify: `/tmp/opencode/deploy-singlepane.ps1` expected SHA256 before deployment
- Evidence: `docs/fixtures/phase1/tools-menu-260-850x500.png` and new tool screenshots

**Interfaces:**
- Consumes: all test IDs from Tasks 1–3, `avalonia/pack/verify.sh`, Windows `pack.ps1`.
- Produces: updated matrix/manual wording and verified Windows artifact.

- [x] **Step 1: Update the test documentation**

Update NET-05 to say seven fixed service ports, no “其他端口” probe/column, Stop button, preserved rows, and cancellation propagation. Update SRV-01 to mention centered/equal-width port and seconds inputs. Update UI-16 to mention unowned `Show()` tool windows, CenterScreen, and main-window close cleanup. Update WIN-26’s manual 10-column description to 9 columns and add the Stop/independent-window checks.

- [x] **Step 2: Run the full verification gate**

```bash
PATH=/tmp/opencode/e2e-deps/usr/bin:$PATH \
DOTNET_ROOT=/tmp/opencode/dotnet \
DOTNET=/tmp/opencode/dotnet/dotnet \
avalonia/pack/verify.sh
```

Expected: restore/build PASS, every test project PASS, App tests PASS, Linux E2E PASS, TestGate PASS. Record the timestamp/results directory.

- [x] **Step 3: Build/publish the Windows single-file R2R artifact**

Use the existing `avalonia/pack/windows/pack.ps1` flow and copy the resulting `COMTool.App-tools.exe` to the deployment source path. Confirm the file is single-file, win-x64, self-contained R2R, version `0.01`.

- [x] **Step 4: Deploy in place and verify**

Set `$expected` in `/tmp/opencode/deploy-singlepane.ps1` to the new SHA256, run it, and require:

```text
SOURCE_SIZE=<recorded>
SOURCE_SHA256=<recorded>
WORK_SHA256=<same>
DESKTOP_SHA256=<same>
BAK_COUNT=0
SMOKE... Responding=True
DEPLOY_DONE
```

Open the tools once on Windows and capture the refreshed scanner/server screenshots (9-column grid, Stop button, aligned numeric fields, independent windows).

- [x] **Step 5: Commit docs and evidence**

```bash
git add docs/testing/feature-test-matrix.md docs/testing/manual-acceptance.md \
  docs/packaging/avalonia-windows-beta.md docs/fixtures/phase1
git commit -m "docs: record network tools refresh and Windows verification"
```

Do not push.
