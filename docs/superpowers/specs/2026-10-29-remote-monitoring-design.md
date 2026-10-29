# Remote Monitoring (远程监控) — Design Spec

Date: 2026-10-29. Target: MobaXterm 22.1 parity for SSH sessions.
User-approved decisions (2026-10-29): Linux remotes only; all 11 menu options
implemented (7 default-on per user screenshot); toggle button at the bottom-left
of the SFTP browser pane; 2 s poll interval.

## 1. Ground truth (user screenshots, 2026-10-29)

Status bar of the active SSH tab while monitoring is on:

```
1%   6.05 GB / 15.61 GB   ↑0.01Mb/s ↓0.00Mb/s   38 days   /: 49% /boot/efi: 1% /media/sangfor/vdb: 82%
```

i.e. `CPU%`, `RAM used / total GB`, `net ↑up ↓down Mb/s`, `uptime (dominant unit)`,
`partitions "mount: use%"`, single-space separated, no labels.

Toggle: a green "远程监控" text button at the bottom-left of the SFTP browser
panel (chars U+8FDC U+7A0B U+76D1 U+63A7).

Status bar right-click menu, 13 items in this order (checked = default on):

| # | Label (codepoints) | Type | Default | Tag |
|---|--------------------|------|---------|-----|
| 1 | 复制(O)  U+590D U+5236 | action (copy line to clipboard) | - | `copy` |
| 2 | 保存到文件(P)  U+4FDD U+5B58 U+5230 U+6587 U+4EF6 | action (save line to .txt) | - | `save` |
| - | separator | | | |
| 3 | 显示主机信息(Q)  U+663E U+793A U+4E3B U+673A U+4FE1 U+606F | toggle | ON | `host` |
| 4 | CPU(C) | toggle | ON | `cpu` |
| 5 | RAM(R) | toggle | ON | `ram` |
| 6 | 网络上传(S)  U+7F51 U+7EDC U+4E0A U+4F20 | toggle | ON | `netup` |
| 7 | 网络下载(T)  U+7F51 U+7EDC U+4E0B U+8F7D | toggle | ON | `netdown` |
| 8 | 已连接客户端(U)  U+5DF2 U+8FDE U+63A5 U+5BA2 U+6237 U+7AEF | toggle | off | `clients` |
| 9 | 进程数(V)  U+8FDB U+7A0B U+6570 | toggle | off | `processes` |
| 10 | 文件描述符(W)  U+6587 U+4EF6 U+63CF U+8FF0 U+7B26 | toggle | off | `fds` |
| 11 | 正常运行时间(X)  U+6B63 U+5E38 U+8FD0 U+884C U+65F6 U+95F4 | toggle | ON | `uptime` |
| 12 | 连接用户(Y)  U+8FDE U+63A5 U+7528 U+6237 | toggle | off | `users` |
| 13 | 分区信息(Z)  U+5206 U+533A U+4FE1 U+606F | toggle | ON | `partitions` |

## 2. Scope

- Linux remotes only: every metric reads `/proc/*`, `df`, `ps`, `who`. On a
  non-Linux remote the first dumps yield no parseable sections; the monitor
  stops silently after 2 attempts (AppLog only). No Windows-remote variants.
- Only the **selected** SSH tab is polled. Switching away stops the poll;
  switching back (same tab still enabled + transport open) resumes. Background
  tabs are never polled.
- Poll interval fixed at **2 s** (MobaXterm feel), not user-configurable.
- Metrics come from a **short-lived exec channel per poll** on the existing
  `SshClient` (coexists with the interactive shell channel; SFTP stays on its
  separate connection as today).
- Menu selections persist **globally** in config.json (`remoteMonitor`).
  The per-tab button state is runtime-only (MobaXterm behavior) and survives a
  restart of the same session (R) because it lives on the tab.

## 3. Architecture

### 3.1 COMTool.Connections — new folder `RemoteMonitor/`

```
IExecRunner            interface: Task<string> RunCommandAsync(string command)
SshConnection          : implements IExecRunner (new method; exec channel,
                         blocking ExecuteCommand on threadpool, channel
                         disposed in finally; throws on transport death)
MonitorOptions         POCO, 11 bool props, defaults per table above
DumpData               parsed raw sections (nullable fields), HasAnySection
MonitorSnapshot        per-poll computed values + string Format(MonitorOptions)
RemoteMetricsParser    static pure functions: ParseDump + per-section parsers
RemoteMonitor          poll engine (below)
```

Engine contract:

```csharp
public sealed class RemoteMonitor : IDisposable
{
    public RemoteMonitor(Func<Task<string>> dumpSource, TimeSpan interval);
    public event Action<MonitorSnapshot>? SnapshotUpdated;  // fires on loop thread
    public event Action<string>? Failed;                    // reason: "transport"|"unsupported"
    public bool IsRunning { get; }
    public void Start();   // idempotent
    public void Stop();    // idempotent; loop exits at current poll boundary
    public void Dispose();
}
```

Loop: sequential (never overlapping polls). Each iteration: `dump = await
dumpSource()`; parse; compute deltas vs. the previous sample; fire
`SnapshotUpdated`. Failure = exception from `dumpSource()` **or** a dump with
no parseable section; 2 consecutive failures stop the loop and fire `Failed`
(a single transient exec hiccup does not kill the monitor). Stop() while
blocked in a dump: the loop notices cancellation after the dump returns and
exits without firing `Failed`.

### 3.2 Dump script (one exec, single line, POSIX sh)

```sh
echo "MON_HOST=$(hostname 2>/dev/null)"; echo "MON_CPU=$(head -n1 /proc/stat 2>/dev/null)"; echo "MON_MEM=$(grep -E '^(MemTotal|MemAvailable):' /proc/meminfo 2>/dev/null | tr '\n' '|')"; echo "MON_UP=$(cat /proc/uptime 2>/dev/null)"; echo "MON_CLIENTS=$(who 2>/dev/null | wc -l)"; echo "MON_PROC=$(ps -e --no-headers 2>/dev/null | wc -l)"; echo "MON_FD=$(cut -d' ' -f1 /proc/sys/fs/file-nr 2>/dev/null)"; echo "MON_WHO=$(who 2>/dev/null | awk '{print $1}' | paste -sd, -)"; echo "MON_NET=$(awk 'NR>2 && $1!="lo:" {rx+=$2; tx+=$10} END{print rx+0" "tx+0}' /proc/net/dev 2>/dev/null)"; echo "MON_DF=$(df -P -x tmpfs -x devtmpfs -x squashfs -x overlay 2>/dev/null | awk 'NR>1 {print $5" "$6}' | paste -sd'|' -)"; echo MON_END
```

Each metric is exactly one `KEY=value` line (multi-value sections are joined
with `|` or `,` inside the substitution), so parsing is line-by-line.

### 3.3 Parsing rules (all in `RemoteMetricsParser`, pure + unit-tested)

- Line split on `\n` (tolerate `\r`). Section = line prefix `MON_XXX=`; value
  may be empty. `HasAnySection` = at least one field parsed non-null.
- `MON_CPU`: first line of /proc/stat, `cpu  user nice system idle iowait ...`
  → `Idle = idle+iowait`, `Total = sum(all fields)`. < 5 fields → null.
- `MON_MEM`: tokens `MemTotal: N kB` / `MemAvailable: M kB` →
  `Ram = (TotalKb: N, UsedKb: N-M)`. Missing either → null.
- `MON_NET`: `rxBytes txBytes` (longs). Shell already excludes `lo` and sums
  the rest; parser re-verifies two whitespace-separated non-negative ints.
- `MON_UP`: first whitespace token, double seconds → `TimeSpan`.
- `MON_CLIENTS` / `MON_PROC` / `MON_FD`: non-negative int.
- `MON_WHO`: comma-joined user names (empty → null, not zero-length list).
- `MON_DF`: `|`-joined `PCT MOUNT` tokens (e.g. `52% /|72% /boot/efi`);
  skip tokens without a `%`; mount = single word (limitation: mounts with
  spaces show the first word only).
- `DumpData` fields are nullable; a section that is present but malformed
  parses as null (never throws).

### 3.4 Snapshot + formatting (exact strings)

Display order (menu order): host, cpu, ram, net, clients, processes, fds,
uptime, users, partitions. Segments joined with a single space; null segment
omitted. `InvariantCulture` for all numbers.

| Metric | Format | Notes |
|--------|--------|-------|
| host | bare hostname, first segment | assumption: MobaXterm shows it first; verify on VDI |
| cpu | `{0:0}%` | rounded, clamped 0..100; null on first poll (no baseline) |
| ram | `{0:0.00} GB / {1:0.00} GB` | used = MemTotal−MemAvailable; GB = kB/1048576 |
| net | `↑{0:0.00}Mb/s` / `↓{1:0.00}Mb/s` (U+2191 / U+2193) | Mbps = Δbytes·8/10⁶/Δt, Δt = measured between polls; each direction shown only if its option is on; Δ<0 (counter reset) → 0; null on first poll |
| clients | bare count | e.g. `2` |
| processes | bare count | e.g. `245` |
| fds | bare count | |
| uptime | `{N} day(s)` ≥1 d, else `{N} hour(s)` ≥1 h, else `{N} min` ≥1 m, else `{N} s` | dominant unit only (screenshot: `38 days`) |
| users | comma-joined names | e.g. `devuser,root` |
| partitions | `{mount}: {pct}%` joined by space | e.g. `/: 49% /boot/efi: 1%` |

Assumption (documented for VDI check): the four optional metrics render as
bare values, matching MobaXterm's compact no-label style; one-line fix if
MobaXterm actually labels them.

### 3.5 App glue

`TerminalTab` (MainViewModel.cs):
- `[ObservableProperty] bool MonitorEnabled` (runtime per-tab flag)
- `IExecRunner? MonitorSource { get; set; }` (set on attach, cleared on
  restart failure)

`SftpPaneViewModel`:
- `[ObservableProperty] bool IsMonitorOn` → setter fires `MonitorToggled?.Invoke(v)`
- `Action? MonitorToggled`
- `internal void SetMonitorOnSilent(bool v)` (no callback; used for tab-switch
  sync so re-showing a pane does not re-trigger start/stop)

`MainViewModel` new partial `MainViewModel.Monitor.cs`:
- `MonitorMenu` — new `ObservableObject` (same file) with 11
  `[ObservableProperty] bool`s + `ToOptions()` (→ POCO) + `LoadFrom(RemoteMonitorConfig)`
- `MonitorText` (string), `IsMonitorActive` (bool) — observable
- `SetMonitorEnabled(TerminalTab tab, bool v)`
- `MonitorOnSelectedTabChanged()` — called from the `SelectedTab` setter:
  stop poll of previous tab, start poll if new tab is SSH + enabled + transport open;
  sync `pane.SetMonitorOnSilent(tab.MonitorEnabled)`
- `MonitorOnTransportAttached(TerminalTab tab)` — called from
  `AttachSshTransport`: start poll if tab is selected + enabled
- `MonitorStopForTab(TerminalTab tab)` — called from SSH `CloseAction` (before
  `connection.Dispose()`) and from the `ProcessExited` handler; no-op if not
  the monitored tab
- `HandleSnapshot(MonitorSnapshot)` — internal, sets MonitorText/
  IsMonitorActive/_lastSnapshot (no dispatcher; tests call it directly)
- `ReformatMonitorLine()` — re-runs `_lastSnapshot.Format(MonitorMenu.ToOptions())`
  (menu toggle updates the line immediately, no wait for next poll)

Poll wiring (production): `monitor.SnapshotUpdated += s =>
Dispatcher.UIThread.Post(() => HandleSnapshot(s))`; `monitor.Failed += reason
=> Dispatcher.UIThread.Post(() => { MonitorStopForTab(_monitoredTab); AppLog... })`.

`MainWindow.axaml.cs`:
- `OpenSshSessionAsync`: after `new SftpPaneViewModel(...)`,
  `pane.MonitorToggled = v => viewModel.SetMonitorEnabled(tab, v);`
  and initial `pane.SetMonitorOnSilent(tab.MonitorEnabled)`
- `AttachSshTransport`: `tab.MonitorSource = connection;`
  `viewModel.MonitorOnTransportAttached(tab);`
- SSH `CloseAction`: first line `viewModel.MonitorStopForTab(tab);`
- `ProcessExited` handler: `viewModel.MonitorStopForTab(tab);`
- restart failure path (where `tab.Connection = null` is set):
  `tab.MonitorSource = null;`
- status bar ContextMenu: `Opening` handler syncs every checkable item's
  `IsChecked` from `viewModel.MonitorMenu` (Tag→property map); one shared
  `Click` handler reads `Tag` + new `IsChecked` →
  `viewModel.MonitorMenu.SetOption(tag, v)` + `viewModel.ReformatMonitorLine()`
  + persist (`_config.Config.RemoteMonitor = ...; _config.Save();`);
  `copy` → `TopLevel.GetTopLevel(this)?.Clipboard.SetTextAsync(viewModel.MonitorText)`;
  `save` → `StorageProvider.SaveFilePickerAsync` (same pattern as
  `SaveStoppedTerminalOutputAsync`, suggested name `remote-monitor-<yyyyMMdd-HHmmss>.txt`)

`ComtoolConfig`: new `RemoteMonitorConfig` class (11 bools, same defaults,
`[JsonExtensionData] Extra`) + property `[JsonPropertyName("remoteMonitor")]`;
old configs without the key load the defaults (no version bump).

### 3.6 UI

`SftpPane.axaml` row 6 (status row): `ColumnDefinitions` becomes
`Auto,*,Auto,Auto,Auto`; new col 0:
`ToggleButton` classes `MonitorToggle`, content TextBlock `远程监控`,
`IsChecked="{Binding IsMonitorOn, Mode=TwoWay}"`, `ToolTip.Tip="远程监控"`,
compact (no default button chrome): FontSize 11, Padding 6,2, Foreground
`#2e7d32` when checked / `#666666` when unchecked (local style).

`MainWindow.axaml` TabHost row 3 grid: `ColumnDefinitions="*,Auto"` →
`"Auto,*,Auto"`:
- col 0: monitor `TextBlock` (`Text="{Binding MonitorText}"`,
  `Foreground="{DynamicResource WindowTextBrush}"`, FontSize 12,
  `IsVisible="{Binding IsMonitorActive}"`, `TextTrimming=CharacterEllipsis`,
  Margin 4,0,8,0, VerticalAlignment Center)
- col 1: existing status message `TextBlock` — gains
  `IsVisible="{Binding !IsMonitorActive}"`
- col 2: zmodem progress unchanged
- `ContextMenu` on the row-3 grid: the 13 items of §1; checkable items
  `IsCheckable="True" Tag="..."`, click handlers in code-behind (no
  in-flyout bindings — matches the codebase's existing Tag+Click style)

## 4. Lifecycle (event → action)

| Event | Action |
|-------|--------|
| toggle on, tab selected + transport open | start poll |
| toggle on, tab not selected | flag only; starts on select |
| toggle off | stop poll if this tab is monitored; clear bar |
| select SSH tab (enabled + transport open) | start poll |
| select other tab / non-SSH tab | stop poll, clear bar |
| `AttachSshTransport` (connect or restart success) | `MonitorSource = conn`; start poll if selected + enabled |
| `ProcessExited` (session stopped) | stop poll, clear bar; **flag stays on** → auto-resume on R restart |
| tab close (incl. auto-close of dead tab) | `MonitorStopForTab` in `CloseAction` before `connection.Dispose()` |
| exec failure ×2 consecutive | engine stops, `Failed` → clear bar + AppLog |
| dump without sections ×2 (non-Linux) | same, reason `unsupported` |

## 5. Test strategy

- **Connections** (new `RemoteMonitorTests.cs`, prefix `RM01_`):
  parser (real sample data + edges: CRLF, empty values, `lo` excluded,
  counter reset → 0, meminfo missing → null, df malformed token, cpu
  <5 fields, no sections → `HasAnySection=false`), formatter (each option
  combination, rounding/clamping, culture-invariant), engine (fake
  `Func<Task<string>>`: cpu/net null on 1st snapshot then present; 2
  consecutive failures → `Failed` + auto-stop; one transient failure →
  survives; Stop() idempotent; cancel while blocked doesn't fire `Failed`),
  plus one `SshIntegrationTests` case: `RunCommandAsync` against the local
  sshd harness (echo + a real `/proc` read on Linux; skip on Windows).
- **Core**: config round-trip — defaults when key missing, saved values
  reload unchanged, unknown-extra preserved.
- **App** (new tests in `COMTool.App.Tests`, prefix `RM01_`): VM glue with a
  fake `IExecRunner` + real `TerminalTab` objects: toggle on selected tab
  starts polling (snapshots arrive via `HandleSnapshot`), deselect stops,
  reselect resumes, `MonitorOnTransportAttached` on non-selected tab does
  not start, `MonitorStopForTab` clears text, `SetMonitorOption` reformats
  immediately, non-SSH tab toggle is a no-op, `SftpPaneViewModel`
  `IsMonitorOn` fires `MonitorToggled` once, `SetMonitorOnSilent` does not.
- **E2E** (Xvfb, `RM01_MonitorBarShowsMetricsE2E`): open SSH session (local
  sshd as in `SSH11_RestartStoppedSessionE2E`), open SFTP browser rail,
  click 远程监控, assert the status bar gains text matching `GB /` within
  ~6 s.

## 6. Out of scope

Windows-remote metrics, per-host option memory, configurable interval,
graphs/history, host info beyond `hostname`, menu accelerators as real key
bindings (Q/C/R/… are display-only, like the current screenshot).

## 7. Docs & process (per project conventions)

- `docs/parity-matrix.md`: new `SSH-12` row (远程监控)
- `docs/testing/feature-test-matrix.md`: `SSH-12` row listing the new tests
- `docs/testing/manual-acceptance.md`: `WIN-28` row (real-machine checklist)
- `avalonia/pack/PLAN-macro-round.md`: round 18 entry (codepoint-safe write)
- win-x64 single-file R2R, version 0.01; per-task local commits; full gate
  (build + all unit suites + E2E + TestGate); deploy dual copies with SHA256
  verify; user verifies on VDI against MobaXterm side by side.
