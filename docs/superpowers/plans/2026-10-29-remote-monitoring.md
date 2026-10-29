# Remote Monitoring (远程监控) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 1:1 replicate MobaXterm 22.1 remote monitoring: a per-SSH-session poller (2 s) that runs one merged exec-channel command against the remote `/proc` + `df`/`ps`/`who`, renders the selected metrics in the main-window status bar, with a "远程监控" toggle in the SFTP pane and a 13-item right-click menu (persisted).

**Architecture:** New `COMTool.Connections/RemoteMonitor/` subsystem (pure parser + snapshot formatter + poll engine + `IExecRunner`). `SshConnection` implements `IExecRunner` via a short-lived exec channel per poll (coexists with the shell channel; SFTP keeps its separate connection). App glue lives in a new `MainViewModel.Monitor.cs` partial; per-tab runtime flag on `TerminalTab` (`MonitorEnabled` + `MonitorSource`); button state on `SftpPaneViewModel` (`IsMonitorOn` + `MonitorToggled` callback); status bar = TabHost row-3 grid extended with a monitor `TextBlock` (col 0) and a 13-item `ContextMenu`; menu option state persisted as `remoteMonitor` in `ComtoolConfig`.

**Tech Stack:** .NET 10 (net10.0), Avalonia 12.1.4, Renci.SshNet (exec channel), xUnit, CommunityToolkit.Mvvm. Test gate: `pack/verify.sh` (build + all unit suites + 26 Xvfb E2E + TestGate).

**Spec:** `docs/superpowers/specs/2026-10-29-remote-monitoring-design.md`

## Global Constraints

- win-x64 single-file R2R publish, version 0.01 unchanged; local commits only (no push); per-task commits.
- Test names: `RM01_<Method>` prefix (engine/parser), `RM02_...` (exec runner), `RM03_...` (config), `RM04_...` (App glue), `RM01_MonitorBarShowsMetricsE2E` (E2E). File-scoped namespaces, `internal` visibility where the test project needs it (project already uses InternalsVisibleTo — verify before adding).
- CJK/UI text must be written with the established codepoint-audit discipline: after any edit that adds CJK to `.axaml`/`.cs`, run the Python codepoint dump on disk and treat build + tests as ground truth (display layer corruption is known). Menu labels and the button label come from the spec table.
- The merged dump script is exactly the spec §3.2 string (single line, `MON_*` markers) — it is the contract between the parser tests and the remote.
- Poll interval 2 s (constant `RemoteMonitor.DefaultInterval`); max 2 consecutive failures before stop; never touch the UI thread from the loop (events marshaled by the App layer).
- `DnsTimeoutMs` etc. from earlier rounds are unrelated — do not modify NetworkScanner in this feature.
- Existing behavior must not regress: the 30px status row keeps its status message + zmodem progress when monitoring is off; the SFTP pane status row keeps retry/progress/cancel.

## Review Focus

1. **Non-Linux remote** (Windows OpenSSH / minimal box): dump script produces garbage or empty `MON_*` values → parser returns `HasAnySection=false`, engine stops after 2 polls with `Failed("unsupported")`; no exception escape, no repeated polls, bar cleared. Test: engine with a fake source returning non-`MON` output.
2. **Dispose race**: tab closed mid-poll while `ExecuteCommand` blocks → `RunCommandAsync` may throw `ObjectDisposedException`/`SshConnectionException`; the engine's catch-all converts any exception into a failure count, and `Stop()`/`Dispose()` are idempotent. Test: engine with a source that throws; also `SshConnection.RunCommandAsync` on a disposed instance throws `ObjectDisposedException` (no hang).
3. **Counter resets / monotonicity**: `/proc/net/dev` bytes can only decrease across a NIC reset; `d < 0 → 0`. CPU total can also reset on boot. Test: parser/engine with decreasing counters → rate 0, not negative, no crash.
4. **Stale config**: `config.json` without the `remoteMonitor` key (all current user configs) → defaults (7 on / 4 off) apply; with the key → stored values win. Test: `RM03` round-trip + missing-key load.
5. **Tab lifecycle while monitoring**: switch tab / stop session (shell exit) / press-R restart / auto-close of a dead tab — exactly one active poller at all times, no orphan loop polling a disposed connection, button state stays consistent after tab re-select. Test: `RM04` glue tests covering all four transitions.

---

### Task 1: Parser + snapshot + formatters (pure, TDD)

**Files:**
- Create: `avalonia/src/COMTool.Connections/RemoteMonitor/RemoteMetricsParser.cs`
- Create: `avalonia/src/COMTool.Connections/RemoteMonitor/MonitorOptions.cs`
- Create: `avalonia/src/COMTool.Connections/RemoteMonitor/MonitorSnapshot.cs`
- Test: `avalonia/tests/COMTool.Connections.Tests/RemoteMonitorTests.cs`

**Interfaces:**
- Consumes: nothing (pure).
- Produces (later tasks rely on these exact names):
  - `MonitorOptions` — POCO, 11 bool props: `ShowHost, ShowCpu, ShowRam, ShowNetUp, ShowNetDown, ShowClients, ShowProcesses, ShowFileDescriptors, ShowUptime, ShowUsers, ShowPartitions` (defaults: the seven true per spec). `public static MonitorOptions Default { get; }`
  - `MonitorSnapshot` — props: `string? Host`, `double? CpuPercent`, `(double UsedGb, double TotalGb)? Ram`, `double? NetUpMbps`, `double? NetDownMbps`, `int? ConnectedClients`, `int? ProcessCount`, `int? FileDescriptorCount`, `TimeSpan? Uptime`, `string[]? Users`, `(string Mount, int UsePct)?[]? Partitions`; method `string Format(MonitorOptions o)`.
  - `RemoteMetricsParser` (static): `Dump(string output) -> DumpData`; `DumpData` with nullable fields `Host, CpuLine (long Idle, long Total)?, Mem (long TotalKb, long UsedKb)?, Net (long Rx, long Tx)?, Uptime (double seconds)?, Clients (int)?, Processes (int)?, Fd (int)?, Users (string[])?, Partitions ((string,int)[])?` and `bool HasAnySection`.
  - `CpuDelta((long Idle, long Total) prev, (long Idle, long Total) cur) -> double?` (percent, null if `dTotal <= 0`)
  - `NetRate((long Rx, long Tx) prev, (long Rx, long Tx) cur, double dtSec) -> (double UpMbps, double DownMbps)` (negative delta → 0; `dtSec <= 0.05` → (0,0))
  - `FormatUptime(TimeSpan) -> string`

- [ ] **Step 1: Write failing tests** in `RemoteMonitorTests.cs` (prefix `RM01_`), using the spec §3.2 marker format in sample strings:
  - `RM01_DumpParsesFullSample`: full multi-line sample (copy a realistic /proc dump with all `MON_` lines) → every field parsed; `HasAnySection=true`.
  - `RM01_DumpToleratesCrlfAndEmptyValues`: `\r\n` line endings; `MON_HOST=` empty → `Host=null`, others still parsed.
  - `RM01_DumpNoSectionsHasNone`: output with no `MON_` lines → `HasAnySection=false`.
  - `RM01_DumpIgnoresMalformedSections`: `MON_NET=abc`, `MON_DF=52%` (no mount) → those fields null, rest parsed.
  - `RM01_CpuDeltaFirstSampleNull`, `RM01_CpuDeltaComputesPercent` (prev idle 100/total 1000, cur idle 400/total 1600 → 100·(600−300)/600 = 50), `RM01_CpuDeltaNonMonotonicNull` (cur < prev → null).
  - `RM01_NetRateExcludesNegativeDelta` (decreasing rx → 0), `RM01_NetRateSmallDtZero` (dt 0.01 → (0,0)).
  - `RM01_FormatUptimeUnits`: 38 d 5 h → "38 days"; 5 h 2 m → "5 hours"; 9 m → "9 min"; 30 s → "30 s".
  - `RM01_FormatMatchesMobaXtermSample`: a snapshot built from the user's screenshot values (`CpuPercent=1.4`, `Ram=(6.05,15.61)`, `NetUpMbps=0.01`, `NetDownMbps=0.0`, `Uptime=38 days`, `Partitions=[("/",49),("/boot/efi",1),("/media/sangfor/vdb",82)]`, `Host=null`) with `MonitorOptions.Default` → exactly `1% 6.05 GB / 15.61 GB ↑0.01Mb/s ↓0.00Mb/s 38 days /: 49% /boot/efi: 1% /media/sangfor/vdb: 82%` (assert with `CultureInfo.InvariantCulture`; the ↑/↓ chars are U+2191/U+2193 — write them in the test via `"\u2191"`/`"\u2193"` escapes to keep the file ASCII).
  - `RM01_FormatOmitsDisabledAndMissing`: with all options on but `Host/Users/Clients/Processes/Fd = null` → those segments absent; `ShowNetUp=true, ShowNetDown=false` → only the `↑` part; `Format` returns "" when no segment survives.
- [ ] **Step 2: Run to verify failure**
  Run: `dotnet test tests/COMTool.Connections.Tests/ -f net10.0 --filter RM01_` (expect compile failure — types don't exist yet)
- [ ] **Step 3: Implement** the three files per the interfaces above. `Format` joins non-null segments with a single space in the fixed order. `FormatRam` uses `"0.00"`, `FormatCpu` rounds to int and clamps 0..100, net uses `"0.00"`.
- [ ] **Step 4: Run to verify pass**
  Run: `dotnet test tests/COMTool.Connections.Tests/ -f net10.0` — all `RM01_` pass, no regressions in the 113 existing.
- [ ] **Step 5: Commit** `git add ... && git commit -m "feat(connections): remote monitoring parser, snapshot formatter and options (RM01)"`

### Task 2: Poll engine `RemoteMonitor` (TDD, fake source)

**Files:**
- Create: `avalonia/src/COMTool.Connections/RemoteMonitor/RemoteMonitor.cs`
- Test: same `RemoteMonitorTests.cs` (append)

**Interfaces:**
- Consumes: Task 1 types.
- Produces: `RemoteMonitor` exactly per spec §3.1 contract; `public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);`

- [ ] **Step 1: Write failing tests** (prefix `RM01_`):
  - `RM01_EngineEmitsSnapshotPerPoll`: source yields two valid full dumps (net counters +100000/+400000 bytes between, cpu counters +1000 total/+400 idle → 60%); interval `TimeSpan.FromMilliseconds(20)`; collect first 2 snapshots: #1 has `CpuPercent=null, NetUpMbps=null`; #2 has `CpuPercent≈60` (±1), net rates consistent with measured dt.
  - `RM01_EngineStopsAfterTwoConsecutiveFailures`: source = [ok, throw, throw] → `Failed` fires exactly once with reason `transport`, `IsRunning` false afterwards, no further snapshots.
  - `RM01_EngineSurvivesSingleTransientFailure`: source = [ok, throw, ok] → no `Failed`, 2 snapshots.
  - `RM01_EngineStopsOnUnsupportedDump`: source always returns `hostname: win\ncmd: not found` (no `MON_`) → `Failed("unsupported")` after 2 polls.
  - `RM01_EngineStopIsIdempotentAndCancels`: start, `Stop()`, `Stop()` again, `Dispose()` — no exceptions, `IsRunning=false`.
  - `RM01_EngineDoesNotFireFailedWhenStoppedWhileBlocked`: source = [ok, Task.Delay(500) then throw]; call `Stop()` during the delay → `Failed` not fired (stop wins).
- [ ] **Step 2: Run to verify failure** (compile failure expected).
- [ ] **Step 3: Implement** `RemoteMonitor`: `Task.Run` loop, `await dumpSource()`, `Parse` + deltas vs. stored previous sample, fire `SnapshotUpdated`, `await Task.Delay(interval, ct)`; `catch (OperationCanceledException) → exit`; `catch (Exception) → failure path`; `HasAnySection=false` counts as failure with reason `unsupported`.
- [ ] **Step 4: Run to verify pass** — full Connections suite green.
- [ ] **Step 5: Commit** `git commit -m "feat(connections): remote monitor poll engine with two-strike failure policy (RM01)"`

### Task 3: `SshConnection.RunCommandAsync` (exec channel)

**Files:**
- Modify: `avalonia/src/COMTool.Connections/SshConnection.cs`
- Create: `avalonia/src/COMTool.Connections/RemoteMonitor/IExecRunner.cs` (or put the interface in `RemoteMonitor.cs` — one file, `namespace COMTool.Connections`)
- Test: append to `tests/COMTool.Connections.Tests/SshIntegrationTests.cs`

**Interfaces:**
- Produces: `public interface IExecRunner { Task<string> RunCommandAsync(string command); }`; `SshConnection` implements it. `RunCommandAsync` = `CreateExecChannel` → `Task.Run(() => channel.ExecuteCommand(command))` → return `result.Output`; `finally { channel.Dispose(); }`; disposed guard throws `ObjectDisposedException`.

- [ ] **Step 1: Write failing test** `RM02_ExecChannelReturnsCommandOutput` in `SshIntegrationTests.cs`, reusing the file's existing local-sshd harness (same keygen/sshd_config setup as `KeyAuthShellOutputResizeAndReconnect`): after `SshConnection.Connect(...)`, `await conn.RunCommandAsync("echo rm02_marker_42")` → output contains `rm02_marker_42`; a second concurrent call also works (channels independent); after `conn.Dispose()`, `RunCommandAsync` throws `ObjectDisposedException` (assert with `Record.ExceptionAsync`). Skip on non-Linux exactly like the existing test's guard.
- [ ] **Step 2: Run to verify failure** (`RM02_` filter; compile error expected).
- [ ] **Step 3: Implement** per interfaces above (no change to existing shell/SFTP behavior).
- [ ] **Step 4: Run to verify pass** — Connections suite green.
- [ ] **Step 5: Commit** `git commit -m "feat(connections): SshConnection exec-channel RunCommandAsync via IExecRunner (RM02)"`

### Task 4: Config `RemoteMonitorConfig` + round-trip test

**Files:**
- Modify: `avalonia/src/COMTool.Core/Config/ComtoolConfig.cs` (append class + property; **no version bump** — new optional key)
- Test: append to `tests/COMTool.Core.Tests/ConfigPhase0Tests.cs` (follow its existing load/save helpers)

**Interfaces:**
- Produces: `RemoteMonitorConfig` with the 11 bools (`[JsonPropertyName]` snake_case: `show_host, show_cpu, show_ram, show_net_up, show_net_down, show_clients, show_processes, show_file_descriptors, show_uptime, show_users, show_partitions`), defaults = spec table, `JsonExtensionData Extra`; `ComtoolConfig.RemoteMonitor` property with `[JsonPropertyName("remoteMonitor")]`.

- [ ] **Step 1: Write failing tests** `RM03_...`:
  - `RM03_LegacyConfigWithoutRemoteMonitorKeepsDefaults`: load a JSON without the key → 7 true / 4 off per spec.
  - `RM03_RoundTripPreservesValues`: set all 11 to distinct values, save, reload → equal; an unknown sibling key is preserved via `Extra`.
- [ ] **Step 2: Run to verify failure** (compile error).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass** — Core suite green.
- [ ] **Step 5: Commit** `git commit -m "feat(core): persist remote monitor menu options as remoteMonitor config (RM03)"`

### Task 5: App glue — VM monitor partial + tab/pane state (TDD)

**Files:**
- Create: `avalonia/src/COMTool.App/ViewModels/MainViewModel.Monitor.cs`
- Modify: `avalonia/src/COMTool.App/ViewModels/MainViewModel.cs` (TerminalTab: add `MonitorEnabled` + `MonitorSource`; SelectedTab setter: call `MonitorOnSelectedTabChanged()` + pane-button sync)
- Modify: `avalonia/src/COMTool.App/ViewModels/SftpPaneViewModel.cs` (`IsMonitorOn`, `MonitorToggled`, `SetMonitorOnSilent`)
- Test: Create: `avalonia/tests/COMTool.App.Tests/RemoteMonitorFeatureTests.cs` (prefix `RM04_`)

**Interfaces:**
- Consumes: `RemoteMonitor`, `MonitorOptions`, `MonitorSnapshot` (Tasks 1–2), `IExecRunner` (Task 3), `RemoteMonitorConfig` (Task 4).
- Produces:
  - `MainViewModel.MonitorMenu` (observable, 11 bools, `SetOption(string tag, bool v)`, `LoadFrom(RemoteMonitorConfig)`, `ToOptions()` — tag strings exactly: `host,cpu,ram,netup,netdown,clients,processes,fds,uptime,users,partitions`)
  - `MainViewModel.MonitorText` (string), `IsMonitorActive` (bool)
  - `MainViewModel.LoadMonitorOptions(RemoteMonitorConfig)`, `RemoteMonitorConfig PersistedMonitorOptions()` (for code-behind save)
  - `MainViewModel.SetMonitorEnabled(TerminalTab tab, bool v)`
  - `MainViewModel.MonitorOnSelectedTabChanged()`, `MonitorOnTransportAttached(TerminalTab)`, `MonitorStopForTab(TerminalTab)`
  - `internal void HandleSnapshot(MonitorSnapshot)` — testable without UI thread
- Test doubles: `FakeExecRunner : IExecRunner` (scripted `Task<string>` outputs, throws on cue); build real `TerminalTab` objects the way `MainViewModel` tests already do (copy the existing tab-construction pattern from `SSH06_*`/`SessionStoppedTests` — check `tests/COMTool.App.Tests/` for the established helper).

- [ ] **Step 1: Write failing tests** (`RM04_`), asserting via public/internal VM API (no XAML):
  - `RM04_ToggleOnSelectedSshTabStartsPollingAndEmitsText`: tab with `FakeExecRunner` (two valid dumps), selected; `SetMonitorEnabled(tab, true)` → after pump, `IsMonitorActive=true`, `MonitorText` contains `GB /`; then `SetMonitorEnabled(tab,false)` → `IsMonitorActive=false`, `MonitorText` empty.
  - `RM04_ToggleOnUnselectedTabDefersUntilSelected`: flag set while unselected → no poll (runner call count 0); select tab → poll starts.
  - `RM04_SwitchTabStopsPreviousPoll`: monitor tab A, select tab B → runner A call growth stops (sample count over a short window stays flat), `IsMonitorActive=false`.
  - `RM04_TransportAttachedResumesWhenSelectedAndEnabled`, `RM04_SessionStopStopsMonitorKeepsFlag` (`ProcessExited` path emulated by calling `MonitorStopForTab`; flag remains; `MonitorOnTransportAttached` resumes).
  - `RM04_NonSshTabToggleIsNoOp`.
  - `RM04_MenuOptionChangeReformatsLineImmediately`: after first snapshot, flip `ShowPartitions` off → `MonitorText` no longer contains `% /`-style partition segment without waiting for a poll; `PersistedMonitorOptions()` reflects the flip.
  - `RM04_SftpPaneMonitorToggledCallbackFires` / `RM04_SetMonitorOnSilentDoesNotFire`.
- [ ] **Step 2: Run to verify failure** (compile error).
- [ ] **Step 3: Implement** the partial + small edits. Loop dispatch: `monitor.SnapshotUpdated += s => Dispatcher.UIThread.Post(() => HandleSnapshot(s));` — but in tests (no Avalonia app) `HandleSnapshot` must be callable directly; wire the dispatcher hop only in production start path (test can call `HandleSnapshot` from a background pump — keep it thread-agnostic, UI-thread-ness comes from the dispatcher hop in production; tests pump the fake runner synchronously).
- [ ] **Step 4: Run to verify pass** — App suite green (no regressions in the 201).
- [ ] **Step 5: Commit** `git commit -m "feat(app): remote monitoring view-model glue, per-tab flag and pane toggle (RM04)"`

### Task 6: UI — SFTP pane button, status bar + context menu, copy/save, wiring, E2E

**Files:**
- Modify: `avalonia/src/COMTool.App/Views/SftpPane.axaml` (+ `.axaml.cs` if a handler is needed)
- Modify: `avalonia/src/COMTool.App/Views/MainWindow.axaml` (row-3 grid)
- Modify: `avalonia/src/COMTool.App/Views/MainWindow.axaml.cs` (wiring + menu handlers + copy/save)
- Modify: `avalonia/src/COMTool.App/App.axaml.cs` or MainWindow ctor (load persisted options into VM at startup)
- Modify: `avalonia/tests/e2e/linux/run-ui-e2e.sh` (+ constants in `lib.sh`)
- Test: E2E `RM01_MonitorBarShowsMetricsE2E` in `run-ui-e2e.sh` (add to the `TESTS` list)

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- UI text (write via codepoint-safe edits, verify by on-disk dump):
  - Button: `远程监控` (U+8FDC U+7A0B U+76D1 U+63A7), ToolTip same.
  - Menu: `复制(O)` / `保存到文件(P)` / separator / `显示主机信息(Q)` / `CPU(C)` / `RAM(R)` / `网络上传(S)` / `网络下载(T)` / `已连接客户端(U)` / `进程数(V)` / `文件描述符(W)` / `正常运行时间(X)` / `连接用户(Y)` / `分区信息(Z)` — codepoints per spec table.

- [ ] **Step 1: SFTP pane button.** Row-6 grid → `ColumnDefinitions="Auto,*,Auto,Auto,Auto"`; new col-0 `ToggleButton` (compact text style, green when checked, gray when not, disabled when the pane has no SSH context — always true here, so plain), bound `IsChecked="{Binding IsMonitorOn, Mode=TwoWay}"`.
- [ ] **Step 2: Status bar.** TabHost row 3: `ColumnDefinitions="Auto,*,Auto"`; col 0 = monitor `TextBlock` (`Name="MonitorText"`, bound `MonitorText`, `IsVisible` bound `IsMonitorActive`, `WindowTextBrush`); existing status `TextBlock` → col 1 with `IsVisible="{Binding !IsMonitorActive}"`; zmodem panel stays col 2. `ContextMenu` on the row-3 `Grid` with the 13 items (checkables: `IsCheckable="True"`, `Tag` per spec, shared `Click="OnMonitorMenuClick"`; actions: `Click="OnMonitorCopyClick"` / `OnMonitorSaveClick"`).
- [ ] **Step 3: Code-behind wiring** in `MainWindow.axaml.cs`:
  - `OnMonitorMenuOpening`: sync each checkable item's `IsChecked` from `viewModel.MonitorMenu`.
  - `OnMonitorMenuClick`: read `Tag` + item's new `IsChecked` → `viewModel.MonitorMenu.SetOption(tag, v)`; `viewModel.ReformatMonitorLine()`; persist via `_config.Config.RemoteMonitor = viewModel.PersistedMonitorOptions(); _config.Save();`
  - `OnMonitorCopyClick`: `TopLevel.GetTopLevel(this)?.Clipboard.SetTextAsync(viewModel.MonitorText)` (no-op when empty).
  - `OnMonitorSaveClick`: `SaveFilePickerAsync` pattern from `SaveStoppedTerminalOutputAsync` (suggested name `remote-monitor-yyyyMMdd-HHmmss.txt`), write `viewModel.MonitorText`.
  - `OpenSshSessionAsync`: wire `pane.MonitorToggled = v => viewModel.SetMonitorEnabled(tab, v);`
  - `AttachSshTransport`: `tab.MonitorSource = connection;` + `viewModel.MonitorOnTransportAttached(tab);`
  - SSH `CloseAction`: prepend `viewModel.MonitorStopForTab(tab);`
  - `ProcessExited` handler: `viewModel.MonitorStopForTab(tab);`
  - restart failure path (where `tab.Connection = null`): `tab.MonitorSource = null;`
  - startup (where `_config` is loaded): `viewModel.LoadMonitorOptions(_config.Config.RemoteMonitor);`
- [ ] **Step 4: E2E** `RM01_MonitorBarShowsMetricsE2E` in `run-ui-e2e.sh` (register in the `TESTS` list near `E2E01`/`SSH11`):
  1. `ensure_first_shell` (opens the SSH session against the local sshd fixture).
  2. `wait_for_ssh_browser_rail` then `click_window $SSH_BROWSER_RAIL_X $SSH_BROWSER_RAIL_Y` → SFTP pane opens (assert `wait_for_sshd_log "subsystem 'sftp'"`).
  3. Click the 远程监控 button: new constants `MONITOR_BUTTON_X/Y` in `lib.sh` (window 1000x650; pane spans the 260px sidebar column minus the 40px rail; button at the pane's bottom-left row — measure from a failure screenshot on first run, same approach the SFTP constants used).
  4. `wait_for_sshd_log "MON_CPU" 30` — the sshd fixture runs with `LogLevel VERBOSE -E`, so the exec command string lands in `sshd.log`; this proves toggle → exec-channel poll end-to-end. Poll a second time (wait ~5 s and confirm a second occurrence count ≥ 2 via `grep -c`).
  5. PASS. (No UI text assertion needed; the sshd-log marker is the deterministic signal.)
- [ ] **Step 5: Build + App tests + codepoint audit** of every CJK-bearing edit (`python3` dump of the edited regions; expect exactly the spec codepoints).
- [ ] **Step 6: Commit** `git commit -m "feat(app): remote monitoring UI - SFTP toggle, status bar, 13-item menu, copy/save, E2E"`

### Task 7: Gate, docs, PLAN round 18, publish, deploy

**Files:**
- Modify: `avalonia/pack/PLAN-macro-round.md` (round 18 entry — Python codepoint write, dry-run → apply → backfill hash, same as rounds 16/17)
- Modify: `docs/parity-matrix.md` (new `SSH-12` row), `docs/testing/feature-test-matrix.md` (`SSH-12` row listing all RM tests + WIN-28), `docs/testing/manual-acceptance.md` (new `WIN-28` row: VDI checklist — toggle on 192.0.2.48 session, expect bar like `… 6.05 GB / 15.61 GB ↑… ↓… 38 days /: 49% …` within ~4 s; right-click menu 13 items with 7 checked; uncheck 分区信息 → segment disappears immediately; copy → clipboard; save → file written; tab switch stops bar; R restart resumes; kill -9 style disconnect stops bar)

- [ ] **Step 1: Docs** (three files, CJK via Python codepoint script with dry-run audit; ASCII test names).
- [ ] **Step 2: Gate** — background `pack/verify.sh` (build + Core/Sftp/Terminal/Connections/Zmodem/TestGate/App unit + 27 E2E incl. new RM01 + TestGate). All PASS required.
- [ ] **Step 3: PLAN round 18** entry (counts from the gate log, commit hash backfilled in a follow-up docs commit).
- [ ] **Step 4: Commit** feature commits are per-task already; final `git add -A && git commit -m "docs: remote monitoring parity/FTM/WIN-28/PLAN round 18"`.
- [ ] **Step 5: Publish + deploy** — `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile -p:IncludeNativeLibrariesForSelfExtract`, then `deploy_r3.py` dual-copy with SHA256 verify.
- [ ] **Step 6: User verification** on VDI against the two reference screenshots (side-by-side with MobaXterm), then backfill the exe hash into PLAN round 18.

---

## Self-review notes (run while writing)

- Spec coverage: script (T1), engine (T2), exec (T3), config (T4), glue (T5), UI (T6), docs/deploy (T7) — every spec §3.x section has a task. ✓
- Review Focus mapping: #1 non-Linux → `RM01_EngineStopsOnUnsupportedDump` (T2); #2 dispose race → `RM02` disposed-guard + `RM01_EngineStopIsIdempotentAndCancels` (T2/T3); #3 counter reset → `RM01_NetRateExcludesNegativeDelta`/`CpuDeltaNonMonotonic` (T1); #4 stale config → `RM03_LegacyConfigWithoutRemoteMonitorKeepsDefaults` (T4); #5 tab lifecycle → `RM04_*` five lifecycle tests (T5). ✓
- Type consistency: `IExecRunner.RunCommandAsync(string)`, `MonitorOptions` 11-prop set identical across spec/T1/T4/T5, tag strings identical T5/T6. ✓
- Proportion: plan is task-level with signatures + test names, no full bodies except where the spec pins exact strings (dump script, sample line, menu labels). ✓
