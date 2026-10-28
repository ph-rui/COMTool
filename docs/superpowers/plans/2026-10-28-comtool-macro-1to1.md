# MobaXterm 宏 1:1 复刻 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 COMTool 的宏功能从"单行命令+可选回车"升级为 MobaXterm 22.1 的完整宏系统：录制、4 种步骤（文本/按键/睡眠/等待字符串）、步骤编辑器、右键菜单、快捷键、Ctrl+Space 弹出菜单、.mxtmacros 导入/导出。

**Architecture:** 纯逻辑（模型/序列化/录制/执行）全部落在 `COMTool.Core.Macros`（可单测），UI 层在 `COMTool.App`：侧栏宏面板重做为 MobaXterm 风格（Record 按钮 + 列表 + 右键菜单），新增 3 个对话框（步骤编辑器/步骤编辑/命名），MainWindow 负责按键捕获（tunnel 级 KeyDown/TextInput）、状态横幅、Ctrl+Space 浮层与全局快捷键。配置保持 `Version=3`，旧 `text+appendEnter` 宏加载时自动迁移为步骤。

**Tech Stack:** .NET 10, Avalonia 12.1.4, CommunityToolkit.Mvvm, xUnit + Avalonia.Headless, 现有 `InMemoryPty`/`TestEnvironment` 测试基建。不引入新 NuGet 依赖。

**Spec:** `docs/superpowers/specs/2026-10-28-macro-1to1-design.md`（实测基准全部来自 2026-10-28 RDP 实机操作 MobaXterm Professional 22.1；导出格式样本 `/tmp/opencode/mxtmacros-sample.txt`）

## Global Constraints

- 版本保持 `0.01`；不 `git push`；发布为 win-x64 单文件 R2R 就地覆盖，不产生 `.bak-*`（`BAK_COUNT=0`）。
- 测试名保持 `ClassName.MethodName` 形式（TestGate 依赖）；`docs/testing/feature-test-matrix.md` 同 commit 更新。
- 中文 UI；但 MobaXterm 实机为英中混合文案的地方按实机原文（如 "Record new macro"、"Please enter a name for this macro"、导出 WARNING 英文原文）。
- 配置 `Version` 保持 3；新字段用可选 + 默认值，旧配置文件必须原样可读（旧版本读新文件靠 `JsonExtensionData` 吸收未知字段）。
- 每个 Task 结束必须：`dotnet build` 0 错 + 该 Task 新增测试 PASS + 全量 gate 无回归（至少 `COMTool.Core.Tests` 与 `COMTool.App.Tests` 相关过滤通过，最终 Task 8 跑全量 gate）。
- 录制/执行不得破坏现有 471 项测试。

## 实测基准（MobaXterm 22.1，RDP 验证 2026-10-28）

- 导出文件（真实样本）：
  ```ini
  [Macros]
  COMTOOL-PROBE=12:0:0:echo probe-macro|258:13:1835009:RETURN|0:0:0:SLEEPEQUAL1200|

  [MacrosHotkeys]
  COMTOOL-PROBE=0
  ```
  步骤编码 `typeId:field1:field2:payload|`：文本=12（field1=按键延迟ms）、按键=258（field1=Windows VK 码，RETURN=13）、睡眠=0（payload `SLEEPEQUAL<ms>`）。等待字符串的 typeId 未实测到（用户宏中无此类型），导出用占位 `4:0:0:WAIT:<pattern>`，导入遇未知 typeId → 警告并跳过该步。
- 录制：连续字符合并为 1 个文本步骤（60ms/字符实测 field1=0，即短间隙不产生延迟值）；0.5s 键间间隙**未**产生 Sleep 步；停止前的大间隙记为 `SLEEP ms=1200`（推断：间隙>阈值才记 Sleep，且封顶 1200ms）。空录制静默丢弃。
- 步骤编辑器 "Macro 编辑"：步骤列表 + 右侧 4 按钮（编辑选定的行 / Insert new line above / 在下方插入新行 / 删除行）+ 快捷键下拉（默认 `<none>`）+ 提示 "or use Ctrl+SPACE in order to display the popup macros menu"。
- 步骤类型对话框 4 radio：文本（文本 + "按键之间的延迟: N ms"）/ 按键 / 睡眠(N ms) / 等待字符串(模式)。
- 右键菜单（原文）：`Rename "NAME"` / `Edit "NAME"` / `Duplicate "NAME"` / `Delete "NAME"` / `Assign hotkey to "NAME"` / `Execute "NAME"` / 创建桌面快捷方式(W) / 导入宏(Y) / 导出宏(Z)。
- 录制中：顶栏 "正在录制新宏: 您在当前终端中键入的所有内容都将被保存。" + 右上"停止"；侧栏行 1 变 "Stop recording macro"。
- 保存命名对话框："Please enter a name for this macro"，默认名=录制首行文本（预填选中）。
- 导出警告（英文原文）："WARNING: your macros will be exported without encryption. This can be dangerous if they contain confidential information. Are you sure you wish to continue?" 是(Y)/否(N)；导出全部宏，默认文件名 `MobaXterm Macros.mxtmacros`，成功后提示 "Your macros have been exported to <path>"。

## Review Focus

- 录制中按键必须**同时**进入终端（pty 收到字节）且被记录，不得吞键；无活动终端时录制按钮禁用。
- 宏执行期间终端被关闭/切换：执行必须可安全终止（取消或目标消失 → 状态复位，不崩溃、不残留 IsExecuting）。
- Wait 步骤必须异步轮询（不冻结 UI）、超时必须**跳过继续**而不是卡死或中止整个宏；轮询间隔 50ms。
- 旧配置（`text`+`appendEnter`）升级后逐字节等价发送（text 原样一个 Text 步骤 + appendEnter→Key RETURN）；`Version` 仍为 3。
- 快捷键不可分配应用保留组合（Ctrl+Space、F11、Ctrl+W/T/N/S 等）；同一热键分配给第二个宏必须被拒绝并提示。
- 导入畸形 .mxtmacros（坏行/未知类型/重名）不崩溃：坏步骤跳过+警告，重名加 " (imported)" 后缀。

---

### Task 1: Core 宏模型 + .mxtmacros 序列化 + 旧格式迁移

**Files:**
- Create: `avalonia/src/COMTool.Core/Macros/MacroStep.cs`（含 `MacroStepKind`、`MacroStep`、`MacroKeys`）
- Create: `avalonia/src/COMTool.Core/Macros/MacroDefinition.cs`
- Create: `avalonia/src/COMTool.Core/Macros/MacroFile.cs`（含 `MacroFileParseResult`）
- Create: `avalonia/src/COMTool.Core/Macros/MacroMigrator.cs`
- Modify: `avalonia/src/COMTool.Core/Config/ComtoolConfig.cs`（`MacroConfig` 加 `Steps`/`Hotkey`，新增 `MacroStepDto`）
- Test: `avalonia/tests/COMTool.Core.Tests/MacroFileTests.cs`、`MacroMigratorTests.cs`、`MacroKeysTests.cs`

**Interfaces:**
- Produces（后续 Task 全部依赖这些签名）：
```csharp
namespace COMTool.Core.Macros;

public enum MacroStepKind { Text, Key, Sleep, WaitString }

public sealed class MacroStep
{
    public MacroStepKind Kind { get; }
    public string Text { get; }            // Text
    public int KeyDelayMs { get; }         // Text：0 = 一次性发送
    public string KeyName { get; }         // Key：RETURN/TAB/...（MacroKeys.All 之一）
    public int Milliseconds { get; }       // Sleep
    public string Pattern { get; }         // WaitString
    public int TimeoutMs { get; }          // WaitString，默认 30000
    public static MacroStep TextStep(string text, int keyDelayMs = 0);
    public static MacroStep KeyStep(string keyName);
    public static MacroStep SleepStep(int ms);
    public static MacroStep WaitStep(string pattern, int timeoutMs = 30_000);
    public string DisplaySummary { get; }  // "…text…" / RETURN / SLEEP ms=1200 / WAIT "p"
    public COMTool.Core.Config.MacroStepDto ToDto();
    public static MacroStep FromDto(COMTool.Core.Config.MacroStepDto dto);
}

public sealed class MacroDefinition
{
    public string Name { get; }
    public string? Hotkey { get; set; }    // "Ctrl+Shift+M" 或 null
    public List<MacroStep> Steps { get; }
    public MacroDefinition(string name, IEnumerable<MacroStep> steps, string? hotkey = null);
}

public static class MacroKeys
{
    public static IReadOnlyList<string> All { get; }  // RETURN,TAB,BACKSPACE,ESC,UP,DOWN,LEFT,RIGHT,HOME,END,PAGEUP,PAGEDOWN,INSERT,DELETE,F1..F12
    public static bool TryGetSequence(string keyName, out string sequence); // RETURN→"\r", TAB→"\t", BACKSPACE→"\x7f", ESC→"\x1b", UP→"\x1b[A", DOWN→"\x1b[B", RIGHT→"\x1b[C", LEFT→"\x1b[D", HOME→"\x1b[H", END→"\x1b[F", PAGEUP→"\x1b[5~", PAGEDOWN→"\x1b[6~", INSERT→"\x1b[2~", DELETE→"\x1b[3~", F1..F4→"\x1bO[OPQRS]", F5..F12→"\x1b[15~"..\x1b[24~"
    public static bool TryGetVkCode(string keyName, out int vk); // RETURN=13, TAB=9, BACKSPACE=8, ESC=27, UP=38, DOWN=40, RIGHT=39, LEFT=37, HOME=36, END=35, PAGEUP=33, PAGEDOWN=34, INSERT=45, DELETE=46, F1..F12=112..123
}

public static class MacroFile
{
    public const string DefaultFileName = "MobaXterm Macros.mxtmacros";
    public const string Filter = "Macros file (*.mxtmacros)|*.mxtmacros";
    public static string Export(IReadOnlyList<MacroDefinition> macros);
    public static MacroFileParseResult Parse(string content);
}
public sealed record MacroFileParseResult(IReadOnlyList<MacroDefinition> Macros, IReadOnlyList<string> Warnings);

public static class MacroMigrator
{
    public static IReadOnlyList<MacroStep> FromLegacy(string? text, bool appendEnter);
}
```
- `MacroConfig`（ComtoolConfig.cs）新增：`[JsonPropertyName("steps")] public List<MacroStepDto> Steps { get; set; } = new();` 与 `[JsonPropertyName("hotkey")] public string? Hotkey { get; set; }`；`MacroStepDto` 字段 `kind`("text"/"key"/"sleep"/"wait")、`text`、`keyDelayMs`、`key`、`ms`、`pattern`、`timeoutMs`(默认30000)。旧字段 `text`/`appendEnter` 保留（迁移用）。
- `MacroFile` 编码：text=`12:{keyDelayMs}:0:{text}`；key=`258:{vk}:0:{KEYNAME}`；sleep=`0:0:0:SLEEPEQUAL{ms}`；wait=`4:0:0:WAIT:{pattern}`（占位，文档注明未实测）。导出结构 `[Macros]`（每宏一行 `Name=step|step|...|`，尾随 `|`）+ 空行 + `[MacrosHotkeys]`（`Name=0`；hotkey 编码未知，一律 0，文档注明局限）。Parse 规则：`[Macros]` 行按第一个 `=` 分 name/value；value 按 `|` 切分（去尾空）；每步按前 3 个 `:` 切（payload 可含 `:`）；typeId 12→Text、258→Key、0→Sleep（payload 前缀 SLEEPEQUAL 或 SLEEP 取数字）、4→Wait；未知 typeId→警告跳过该步；宏有效步骤为 0→警告跳过该宏；hotkey 值 0→null，非 0→null+警告。name 含 `=`/换行 → 跳过+警告。

- [ ] **Step 1: 写失败测试**

`MacroFileTests.cs`：
- `Parse_MobaXtermRealSample_ProducesThreeSteps`：用实测样本原文（COMTOOL-PROBE 3 步）→ 1 宏 3 步：Text("echo probe-macro",0)、Key("RETURN")、Sleep(1200)；Hotkey null；无警告。
- `Export_RoundTripsAllFourStepKinds`：4 种步骤 Export→Parse→逐步断言 Kind/值一致。
- `Parse_UnknownStepType_SkipsWithWarning`、`Parse_MacroWithOnlyUnknownSteps_SkippedWithWarning`、`Parse_NameWithEquals_SkippedWithWarning`、`Parse_EmptyContent_ReturnsEmpty`。
- `Export_MatchesMobaXtermSample_ByteForKnownTypes`：`Export([new MacroDefinition("COMTOOL-PROBE", new[]{ TextStep("echo probe-macro"), KeyStep("RETURN"), SleepStep(1200) })])` 的 `[Macros]` 行 == `COMTOOL-PROBE=12:0:0:echo probe-macro|258:13:0:RETURN|0:0:0:SLEEPEQUAL1200|`，hotkey 行 == `COMTOOL-PROBE=0`。（field3 我们写 0，样本是 1835009 —— 断言到 `258:13:` 前缀 + `:RETURN` 即可，不要求 field3 字节一致。）
- `MacroMigratorTests.cs`：`FromLegacy("ls -la", true)` → [Text("ls -la",0), Key("RETURN")]；`FromLegacy("a\nb", false)` → [Text("a\nb",0)]（保持单步原样，字节等价）；`FromLegacy(null, true)` → 空；`FromLegacy("", false)` → 空。
- `MacroKeysTests.cs`：`TryGetSequence("RETURN")`→"\r"、"UP"→"\u001b[A"、"F5"→"\u001b[15~"、`TryGetVkCode("RETURN")`→13、`TryGetVkCode("F12")`→123、`TryGetSequence("NOTAKEY")`→false；`MacroStep.DisplaySummary`：Text→带引号、Key→名字、Sleep→`SLEEP ms=1200`。

- [ ] **Step 2: 跑测试确认失败**

Run: `cd /tmp/opencode/COMTool/avalonia && DOTNET_ROOT=/tmp/opencode/dotnet PATH=/tmp/opencode/dotnet:$PATH /tmp/opencode/dotnet/dotnet test tests/COMTool.Core.Tests --filter "FullyQualifiedName~Macro" -v minimal`
Expected: 编译失败（类型不存在）。

- [ ] **Step 3: 实现 Task 1 文件**（按上面 Interfaces 逐字实现；Parse 用手工逐行扫描，不用 INI 库）。

- [ ] **Step 4: 测试通过**（同 Step 2 命令，Expected: PASS）。

- [ ] **Step 5: Commit**

```bash
git add avalonia/src/COMTool.Core/Macros avalonia/src/COMTool.Core/Config/ComtoolConfig.cs avalonia/tests/COMTool.Core.Tests
git commit -m "feat(core): macro step model, .mxtmacros serializer, legacy migration"
```

---

### Task 2: MacroRecorder（录制事件流 → 步骤列表）

**Files:**
- Create: `avalonia/src/COMTool.Core/Macros/MacroRecorder.cs`
- Test: `avalonia/tests/COMTool.Core.Tests/MacroRecorderTests.cs`

**Interfaces:**
```csharp
namespace COMTool.Core.Macros;
public sealed class MacroRecorder
{
    public MacroRecorder(int gapThresholdMs = 1000, int maxSleepMs = 1200);
    public bool HasInput { get; }
    public void FeedText(string text, DateTime now);   // 每个字符一个事件，时间戳均为 now
    public void FeedKey(string keyName, DateTime now); // keyName 必须在 MacroKeys.All 内，否则忽略
    public IReadOnlyList<MacroStep> Finish(DateTime stopTime);
}
```
算法（对齐实测）：事件序列 (ts, char|key)；相邻事件间隙 > `gapThresholdMs` → 先发 Sleep(min(gap 四舍五入到 10ms 的倍数, maxSleepMs))；可打印字符连续合并为 1 个 Text 步骤（KeyDelayMs 恒 0）；Key 事件结束当前文本段并产出 Key 步骤；`Finish` 时若末事件到 stopTime 间隙 > 阈值 → 追加尾 Sleep（封顶 maxSleepMs）；无输入返回空列表。

- [ ] **Step 1: 写失败测试**（`MacroRecorderTests.cs`，用固定 `DateTime t0` + `AddMilliseconds`）：
- `MREC01_ConsecutiveChars_SingleTextStepZeroDelay`（"abc" 50ms 间隔 → 1 步 Text "abc" KeyDelayMs=0）
- `MREC02_GapBelowThreshold_NoSleep`（text 后 600ms 按 RETURN → [Text, Key]）
- `MREC03_GapAboveThreshold_InsertsSleep`（1500ms → [Text, Sleep(1500), Key]）
- `MREC04_GapCappedAtMaxSleep`（60_000ms → Sleep(1200)）
- `MREC05_TrailingGap_AppendsTrailingSleep`（末键后 2000ms stop → 尾部 Sleep(1200)）
- `MREC06_TrailingShortGap_NoSleep`（300ms → 无尾部 Sleep）
- `MREC07_KeySplitsTextRuns`（a RETURN b → [Text"a", Key, Text"b"]）
- `MREC08_NoInput_FinishEmpty`（Finish → 0 步，HasInput false）
- `MREC09_UnknownKey_Ignored`（FeedKey("NOTAKEY") 不产生步骤）
- `MREC10_RoundTo10ms`（间隙 1504ms → Sleep(1500)）

- [ ] **Step 2: 跑测试确认失败**（`--filter "FullyQualifiedName~MacroRecorderTests"`）
- [ ] **Step 3: 实现 MacroRecorder**
- [ ] **Step 4: 测试通过**
- [ ] **Step 5: Commit** `feat(core): macro recorder with gap→sleep synthesis`

---

### Task 3: MacroRunner（步骤回放引擎）

**Files:**
- Create: `avalonia/src/COMTool.Core/Macros/ITerminalSink.cs`（含 `MacroRunResult`、`MacroRunner`）
- Test: `avalonia/tests/COMTool.Core.Tests/MacroRunnerTests.cs`

**Interfaces:**
```csharp
namespace COMTool.Core.Macros;
public interface ITerminalSink
{
    Task SendTextAsync(string text, CancellationToken ct);
    string CaptureOutput();
}
public sealed record MacroRunResult(bool Completed, bool Cancelled, string? Message);
public static class MacroRunner
{
    public const int WaitPollIntervalMs = 50;
    public static Task<MacroRunResult> RunAsync(MacroDefinition macro, ITerminalSink sink, CancellationToken ct = default);
}
```
语义：Text：`KeyDelayMs<=0` 一次性 `SendTextAsync(text)`；否则逐字符 send+`Task.Delay(KeyDelayMs)`。Key：`MacroKeys.TryGetSequence`→send；未知键跳过。Sleep：`Task.Delay(Milliseconds, ct)`。WaitString：每 50ms 轮询 `CaptureOutput()`；pattern 含 `*`/`?` 时转正则（其余字符转义，`*`→`.*`、`?`→`.`，OrdinalIgnoreCase），否则 `OrdinalIgnoreCase` 子串匹配；命中→继续；超过 `TimeoutMs` → **跳过该步继续**（不失败）。取消 → `MacroRunResult(false, true, null)`；正常跑完 → `(true, false, null)`。

测试用 `FakeSink`（记录 `List<(string Text, DateTime At)>`、可脚本化 output）：
- [ ] **Step 1: 写失败测试** `MacroRunnerTests.cs`：
  - `MRUN01_TextNoDelay_SentOnce`（"ls -la" → 1 次 send，内容一致）
  - `MRUN02_TextWithDelay_PerCharSends`（KeyDelayMs=1，5 字符 → 5 次 send，顺序一致）
  - `MRUN03_KeyStepSendsSequence`（RETURN→"\r"、UP→"\u001b[A" 按序）
  - `MRUN04_SleepStepDelays`（Sleep(50) → 实测耗时 ≥40ms）
  - `MRUN05_Wait_MatchesThenContinues`（output 第 2 次轮询出现 "password:" → 后续 Text 步骤仍发送）
  - `MRUN06_Wait_TimeoutSkipsAndCompletes`（pattern 永不出现，TimeoutMs=100 → Completed=true 且后续步骤仍发送）
  - `MRUN07_Wait_WildcardMatches`（"log*in" 命中 "Logging in..."）
  - `MRUN08_Cancel_StopsPromptly`（Sleep(5000) 前 cancel → Cancelled=true，返回 <1s）
- [ ] **Step 2: 跑测试确认失败**（`--filter "FullyQualifiedName~MacroRunnerTests"`）
- [ ] **Step 3: 实现**
- [ ] **Step 4: 测试通过**
- [ ] **Step 5: Commit** `feat(core): macro runner (playback engine with wait-for-string)`

---

### Task 4: App 数据层 + 侧栏宏面板 v2 + 录制/执行核心

**Files:**
- Create: `avalonia/src/COMTool.App/Services/MacroRecordingSession.cs`、`avalonia/src/COMTool.App/Services/TerminalMacroSink.cs`
- Create: `avalonia/src/COMTool.App/Services/MacroHotkey.cs`
- Create: `avalonia/src/COMTool.App/ViewModels/MainViewModel.Macros.cs`
- Create: `avalonia/src/COMTool.App/Services/MacroHotkey.cs`
- Create: `avalonia/src/COMTool.App/Views/MacroNameDialog.axaml(.cs)`
- Modify: `avalonia/src/COMTool.App/ViewModels/MainViewModel.Sidebar.cs`（删旧内联编辑器属性，MacroItem 改步骤模型）
- Modify: `avalonia/src/COMTool.App/Views/SessionSidebar.axaml(.cs)`（宏面板重做 + 右键菜单 + 新事件）
- Modify: `avalonia/src/COMTool.App/Views/MainWindow.axaml(.cs)`（LoadSidebarData 迁移、宏事件处理、按键捕获、录制横幅、状态横幅）
- Modify: `avalonia/src/COMTool.App/Styles/AppStyles.axaml`（MacroRecordButton/MacroRow 等样式）
- Test: `avalonia/tests/COMTool.App.Tests/MacroFeatureTests.cs`（新文件；同时**删除** `SidebarRailTests.cs` 里旧的 `MAC01_SaveEditDeleteMacroPersists`、`MAC01_SendWritesTextAndEnterToActiveTerminal`）

**Interfaces:**
```csharp
// MacroRecordingSession.cs
public sealed class MacroRecordingSession
{
    public MacroRecordingSession(Iciclecreek.Terminal.TerminalControl target);
    public Iciclecreek.Terminal.TerminalControl Target { get; }
    public void FeedText(string text, DateTime now);
    public void FeedKey(string keyName, DateTime now);
    public bool HasInput { get; }
    public IReadOnlyList<COMTool.Core.Macros.MacroStep>? Finish(); // null = 空录制
}
// TerminalMacroSink.cs
public sealed class TerminalMacroSink : COMTool.Core.Macros.ITerminalSink
{
    public TerminalMacroSink(Iciclecreek.Terminal.TerminalControl terminal);
    // SendTextAsync → Dispatcher.UIThread.InvokeAsync(() => _terminal.SendInputAsync(text))
    // CaptureOutput → TerminalTranscript.Capture(_terminal)
}
```
- `MacroItem`（MainViewModel.Sidebar.cs 内改造）：`Id`、`[ObservableProperty] Name`、`[ObservableProperty] string? Hotkey`、`IReadOnlyList<MacroStep> Steps`（替换/删除 `Text`/`AppendEnter`）；派生属性供右键菜单：`MenuRenameHeader`=`Rename "Name"`、`MenuEditHeader`=`Edit "Name"`、`MenuDuplicateHeader`、`MenuDeleteHeader`、`MenuHotkeyHeader`=`Assign hotkey to "Name"`、`MenuExecuteHeader`（Name 变更时全部通知）。
- `MainViewModel`（新 partial `MainViewModel.Macros.cs`）：`[ObservableProperty] MacroRecordingSession? _activeRecording`（`IsRecording => ActiveRecording is not null`）、`[ObservableProperty] bool _isExecutingMacro`、`[ObservableProperty] string _executingMacroName = ""`；`CanStartRecording => ActiveRecording is null && !IsExecutingMacro && SelectedTab?.Terminal is not null`；方法：
  - `bool StartRecording()`（无活动终端→false）
  - `IReadOnlyList<MacroStep>? StopRecording()`（空→null）
  - `bool SaveNewMacro(string name, IReadOnlyList<MacroStep> steps, string? hotkey = null)`
  - `Task ExecuteMacroAsync(MacroItem item)`（无终端→状态提示返回；执行中→忽略；用 `TerminalMacroSink` + `MacroRunner.RunAsync`；终态复位 `IsExecutingMacro`）
  - `void CancelExecution()`（CancellationTokenSource 取消）
  - `void DeleteMacro(MacroItem)`、`void DuplicateMacro(MacroItem)`（名称 +" (copy)"，热键清空）、`bool RenameMacro(MacroItem, string newName)`
  - `bool AssignHotkey(MacroItem item, string? hotkey)`（用 `MacroHotkey.IsReserved`/`IsTaken` 校验；非法/冲突返回 false，成功则写回并通知）
  - `string ExportMacrosText()`、`(int imported, IReadOnlyList<string> warnings) ImportMacrosText(string content)`（重名→`" (imported)"` 后缀，循环直至唯一）
- `MacroHotkey`（`Services/MacroHotkey.cs`）：`Options()`→IReadOnlyList&lt;string&gt;（`&lt;none&gt;` + `F5..F12` + `Ctrl+Shift+A..Z` + `Ctrl+Alt+A..Z`）；`TryParse(string, out Key, out KeyModifiers)`；`Format(Key,KeyModifiers)`；`IsReserved(Key,KeyModifiers)`（Ctrl+Space/F11/Ctrl+W/T/N/S 等）；`IsTaken(MainViewModel, string? candidate, string? exceptId)`
  - 旧属性 `IsMacroEditorOpen/MacroEditorName/MacroEditorText/MacroEditorAppendEnter/MacroEditorError/EditingMacroId/OpenMacroEditor/CloseMacroEditor` 删除（Task 5 的新编辑器不依赖它们）。
- MainWindow：`LoadSidebarData` 中 `MacroMigrator` 迁移（Steps 空且 Text 非空 → FromLegacy；迁移后仍空 → 跳过）；保存时写 Steps/Hotkey、清旧 Text。事件处理：`OnMacroRecordToggleClick`（Start/Stop；Stop 有内容 → 打开 `MacroNameDialog`（默认名=首个 Text 步骤文本或首步 DisplaySummary，预填选中）→ SaveNewMacro+持久化）、`OnMacroStopRecordingClick`（横幅停止）、`OnMacroExecuteRequested`、`OnMacroDeleteRequested`（先弹确认窗 `确定删除宏 "NAME" 吗？` 是/否）、`OnMacroRenameRequested`（MacroNameDialog 复用）、`OnMacroDuplicateRequested`；导入/导出菜单项**本 Task 先留占位 handler 弹 "暂不支持（后续轮次）"**（Task 7 实装）。
- 按键捕获（MainWindow ctor 追加，tunnel 级）：`AddHandler(TextInputEvent, OnRecordingTextInput, RoutingStrategies.Tunnel)`；`OnWindowKeyDown` 顶部插入：若 `vm.ActiveRecording is { } rec` 且 `e.Source` 在 `rec.Target` 子树内 → 非修饰键映射 MacroKeys 名（Return→RETURN, Tab→TAB, Back→BACKSPACE, Escape→ESC, Up/Down/Left/Right, Home/End/PageUp/PageDown/Insert/Delete, F1..F12）`rec.FeedKey`（修饰组合键暂不记录，文档注明）；**不** `e.Handled=true`（终端照常收到）。`OnRecordingTextInput` 同理 `rec.FeedText(e.Text, DateTime.Now)`。
- 侧栏 MacrosPanel（`RowDefinitions="Auto,Auto,*"`）：
  - 行 0：`Button Name="MacroRecordButton"` 全宽：红色圆点 `&#xf111;`（`#e03131`，FontAwesome）+ `TextBlock Name="MacroRecordText"`（正常 "Record new macro" / 录制中 "Stop recording macro"，绑 `IsRecording`）；`IsEnabled="{Binding CanStartRecording}"` 录制中恒 true。
  - 行 1：`TextBlock Name="SavedMacrosHeader" Text="已保存的宏"`，`!HasMacros` 时灰（`#9e9e9e`）否则深灰，外框与 MobaXterm 一致（1px 边框、同高）。
  - 行 2：列表 Border（同 Sessions 列表样式）：行模板 `◎`（`&#xf202;`，`#2f7ed8`）+ Name；`DoubleTapped` → 执行；`ContextMenu` 9 项（Header 绑上面派生属性；`创建桌面快捷方式(W)` `IsEnabled="False"` + ToolTip `暂不支持（框架已预留）`；导入/导出 Tag 区分）。
  - 删除 `SidebarAddMacroButton`、内联编辑卡片（MacroNameBox/MacroTextBox/MacroSaveButton/MacroCancelButton）、行内 3 图标按钮及对应事件/旧样式引用（`SidebarIconButton` 样式保留供其他面板使用的话不动，否则随删）。
- 状态横幅：`MainWindow.axaml` TabHost 内 `Grid Grid.Row="1" RowDefinitions="*,6,30"` 改为 `"Auto,*,6,30"`，新 Row 0 `Border Name="MacroStatusBanner"`（高 24，`#fff3f3` 底、红字 11px）：录制中 = "● 正在录制新宏: 您在当前终端中键入的所有内容都将被保存。" + 右 `Button 停止(Name=StopRecordingButton)`；执行中 = "正在执行宏: {name}" + `Button 取消(Name=CancelMacroButton)`；`IsVisible` 绑 `IsRecording || IsExecutingMacro`（用多绑定转换器或 code-behind 更新）。
- `MacroNameDialog`：420x170 固定、`ShowInTaskbar=false`、CenterOwner；标签 "Please enter a name for this macro"、TextBox（预填且全选）、`确定`/`取消`；`internal string? AcceptedName { get; }`。

**Tests（`MacroFeatureTests.cs`，headless，沿用 TestEnvironment/CreateWindow/InMemoryPty 模式）：**
- [ ] **Step 1: 写失败测试**
  - `MAC01_SidebarMacrosPanelMatchesMobaXterm`：宏 tab 显示 `MacroRecordButton`（文本 "Record new macro"）、`SavedMacrosHeader`（空时灰）、无 `MacroNameBox`（旧编辑器已移除）；右键菜单 9 项文本逐一断言（含 `Rename "X"` 引号形式）。
  - `MAC01_LegacyConfigMigratesOnLoad`：SeedConfig 写旧宏 `{name:"重启", text:"reboot now", appendEnter:true}` → 窗口加载后 `vm.Macros[0].Steps` = [Text("reboot now",0), Key(RETURN)]，Hotkey null；再 Save 后读盘 `Steps.Count==2` 且 `Version==3`。
  - `MAC01_ExecuteMacroSendsBytes`：本地 pty 标签页 + 宏 [Text("ls -la"), Key(RETURN)] → `ExecuteMacroAsync` 后 `pty.WriterText=="ls -la\r"`。
  - `MAC04_EmptyRecordingDiscarded`：StartRecording → StopRecording（无输入）→ 返回 null、`Macros.Count` 不变、无对话框。
  - `MAC05_StopRecordingWithName_SavesWithDerivedDefault`：录制后停止有内容 → `MacroNameDialog` 打开且默认名=首行文本（预选中）→ 确定 → 新宏进入 `vm.Macros` 且步骤正确；取消 → 不保存。
  - `MAC13_RecordingCapturesKeysAndForwardsToPty`：录制中对 `rec.Target` 直接 `FeedText/FeedKey`（模拟 tunnel 捕获）+ 断言 `pty.WriterText` 同步增长（转发不吞键）→ Finish 得 [Text, Key(RETURN)]。
  - `MAC13b_ExecutionStopsCleanlyWhenTabClosed`：宏 [Sleep(3000)] 执行中关闭该终端 tab → 执行被取消、`IsExecutingMacro` 复位、无异常。
  - `MAC14_WaitStepTimesOutContinues`：宏 [Wait("zzz-nope", 100), Text("after")] → Completed=true 且 pty 收到 "after"。
  - `MAC15_DeleteAsksConfirmation`：DeleteMacro 前 MainWindow 层确认对话框被触发（注入测试 hook：`internal Func<string,Task<bool>>? ConfirmAsyncForTests`）→ 拒绝时宏仍在、确认时删除+持久化。
- [ ] **Step 2: 跑测试确认失败**（`--filter "FullyQualifiedName~MacroFeatureTests"`；同时确认旧 MAC01 两测已删不冲突）
- [ ] **Step 3: 实现**（顺序：config/VM → sidebar axaml → MainWindow 接线 → 横幅 → 样式）
- [ ] **Step 4: 测试通过 + 全量 App.Tests 过滤跑一遍防回归**（`--filter "FullyQualifiedName~SidebarRailTests|FullyQualifiedName~MacroFeatureTests"`）
- [ ] **Step 5: Commit** `feat(avalonia): macro step model, MobaXterm-style macro panel, record+execute`

---

### Task 5: 宏步骤编辑器对话框

**Files:**
- Create: `avalonia/src/COMTool.App/Views/MacroEditorWindow.axaml(.cs)`
- Create: `avalonia/src/COMTool.App/Views/MacroStepEditorDialog.axaml(.cs)`
- Modify: `MainWindow.axaml.cs`（右键 Edit / Assign hotkey 接线；新增 `OpenMacroEditorWindow(MacroItem?)`）
- Test: `avalonia/tests/COMTool.App.Tests/MacroFeatureTests.cs`（追加）

**Interfaces:**
- `MacroEditorWindow`：标题 "Macro 编辑"，560x400 固定、CenterOwner、非 taskbar。左 `ListBox Name="StepList"`（行 = 类型图标 + `DisplaySummary`）；右 4 竖排按钮 `EditStepButton`（"编辑选定的行"）/`InsertAboveButton`（"Insert new line above"）/`InsertBelowButton`（"在下方插入新行"）/`DeleteStepButton`（"删除行"）；底部 `ComboBox Name="HotkeyCombo"`（首项 `<none>`，项 = Task 4 的 `MacroHotkey.Options()`）+ 提示 TextBlock "or use Ctrl+SPACE in order to display the popup macros menu" + `确定`/`取消`。`internal (string? name, IReadOnlyList<MacroStep>? steps, string? hotkey)? Result { get; }`（取消→null）。
- `MacroStepEditorDialog`：标题 "Macro 编辑"（与实机一致），4 radio（文本/按键/睡眠/等待字符串）+ 对应字段（文本：TextBox + `按键之间的延迟: [NumericUp 0..5000 ms]`；按键：ComboBox=MacroKeys.All；睡眠：NumericUp ms；等待：TextBox pattern）；`确定`/`取消`；`internal MacroStep? Result`。默认选中文本；编辑现有步骤时回填。
- MainWindow：`OnMacroEditRequested`/`OnMacroAssignHotkeyRequested` 打开对应窗；保存后回写 VM + `_config` 持久化。

**Tests：**
- [ ] **Step 1: 写失败测试**
  - `MAC06_EditorOpensWithSteps`：右键 Edit → 窗出现，StepList 行数=宏步骤数，首行文本含 `"echo probe-macro"`（引号）。
  - `MAC07_StepEditorShowsFourTypes`：点"编辑选定的行" → 4 个 radio 存在；切到"睡眠"出现 ms 框；"文本"出现"按键之间的延迟"。
  - `MAC08_EditorInsertDeleteSteps`：选中第 1 行 → Insert new line above（文本 "a"）→ 列表 4 行且 "a" 在第 0 位；DeleteStep 后 3 行；确定后 `vm.Macros[0].Steps` 同步。
  - `MAC08b_EditorCancelDiscards`：改步骤后取消 → 宏不变。
- [ ] **Step 2: 跑测试确认失败**
- [ ] **Step 3: 实现**
- [ ] **Step 4: 测试通过**
- [ ] **Step 5: Commit** `feat(avalonia): macro step editor dialogs (4 step types, hotkey)`

---

### Task 6: 快捷键触发 + Ctrl+Space 弹出菜单 + 顶栏"宏"按钮

**Files:**
- Modify: `MainWindow.axaml(.cs)`（顶栏"宏"flyout 按钮；`MacroPopup` 浮层；OnWindowKeyDown 扩展：热键匹配 / Ctrl+Space / 浮层导航）
- 复用 Task 4 的 `Services/MacroHotkey.cs`（Options/TryParse/Format/IsReserved/IsTaken 均已存在）
- Test: `MacroFeatureTests.cs` 追加

**Interfaces:**
- 消费 Task 4 的 `MacroHotkey`（`Options()`/`TryParse`/`Format`/`IsReserved`/`IsTaken`）。
- 顶栏：`TitleBar` Grid 列定义 `40,104,40,35,*,35,35,35` → `40,104,40,35,35,*,35,35,35`（SessionsButton 后加 `Button Name="MacroMenuButton"` 文字 "宏"，MenuFlyout：`Record new macro`（录制中变 `Stop recording macro`）、分隔线、`已保存的宏`（`IsEnabled={Binding HasMacros}`，点击→`SidebarTab=Macros`））。右侧 min/max/close 列宽不变 → 现有 E2E 坐标不受影响。
- `OnWindowKeyDown`（tunnel，最前）：① 浮层开 → Up/Down 移动选中、Enter 执行、Esc 关闭（`e.Handled=true`）；② `IsRecording` → 交给录制（Task 4 逻辑）；③ 否则遍历 `vm.Macros` 热键匹配（`TryParse` 后比对 `e.Key`+`e.KeyModifiers` 精确相等）→ `ExecuteMacroAsync` + `e.Handled=true`；④ `Key.Space && mods==Control` 且有活动终端且未录制/执行 → 切换 `MacroPopup` 浮层（`e.Handled=true`）；⑤ 原 F11 逻辑不变。
- `MacroPopup`：`ContentArea` 顶层 overlay `Border Name="MacroPopup" IsVisible="False"`（居中、宽 300、`#f5f5f5` 底、1px 边框、阴影）：`ItemsControl` 宏列表（Name + 右对齐灰字热键），选中行高亮；指针点击行=执行并收起；`LostFocus`/点击外部收起。
- 热键分配拒绝逻辑：`IsReserved` 或 `IsTaken`（除自身）→ 不回写并 `ShowErrorAsync("快捷键冲突或保留，未分配")`。

**Tests：**
- [ ] **Step 1: 写失败测试**
  - `MAC11_HotkeyAssignAndTrigger`：VM 设 `Hotkey="Ctrl+Shift+M"` → 对 window `Raise`（KeyDownEvent, Key=M, Shift|Ctrl, tunnel 路由）→ pty 收到宏字节；`IsReserved("Ctrl+Space")` 为 true；重复分配第二个宏被拒（VM 层 `AssignHotkey` 返回 false）。
  - `MAC12_CtrlSpacePopupListsAndExecutes`：Ctrl+Space → `MacroPopup.IsVisible`；ArrowDown+Enter → pty 收到所选宏字节，浮层收起；Esc 收起不执行。
  - `MAC17_ExecutionBannerAndCancel`：宏 [Sleep(3000)] 执行中 → `MacroStatusBanner` 可见且含 "正在执行宏"；`CancelMacroButton` → `IsExecutingMacro` 复位、后续无发送。
  - `MAC16_BannerShowsWhileRecording`：StartRecording → 横幅可见含 "正在录制新宏"；Stop → 隐藏。
- [ ] **Step 2: 跑测试确认失败**
- [ ] **Step 3: 实现**
- [ ] **Step 4: 测试通过**
- [ ] **Step 5: Commit** `feat(avalonia): macro hotkeys, Ctrl+Space popup, title-bar macro menu`

---

### Task 7: 导入/导出（.mxtmacros）+ 明文警告

**Files:**
- Modify: `MainWindow.axaml.cs`（替换 Task 4 占位 handler；新增 `IMacroFilePickers` 抽象 + 脚本化实现）
- Create: `avalonia/src/COMTool.App/Services/MacroFilePickers.cs`
- Modify: `SessionSidebar.axaml.cs`（导入/导出菜单项启用真实 handler）
- Test: `MacroFeatureTests.cs` 追加

**Interfaces:**
```csharp
public interface IMacroFilePickers
{
    Task<string?> PickExportPathAsync(string title, string defaultFileName);
    Task<string?> PickImportPathAsync(string title);
}
// TopLevelMacroFilePickers：StorageProvider.SaveFilePickerAsync/OpenFilePickerAsync（Filter=MacroFile.Filter, 单选）
// ScriptedMacroFilePickers：环境变量 COMTOOL_E2E_MACRO_EXPORT_PATH / COMTOOL_E2E_MACRO_IMPORT_PATH（仿 ScriptedLocalPickers）
public static class MacroFilePickers { public static IMacroFilePickers Create(Control owner); }
```
- 导出：右键 `导出宏(Z)` → 警告窗（标题 "Warning"，正文英文原文 WARNING…，是(Y)/否(N)；否→中止）→ `PickExportPathAsync`（默认名 `MacroFile.DefaultFileName`）→ `File.WriteAllText(path, vm.ExportMacrosText())` → 状态提示 "Your macros have been exported to \"<path>\""。
- 导入：右键 `导入宏(Y)` → `PickImportPathAsync` → `MacroFile.Parse` → `vm.ImportMacrosText` → 持久化 → 状态提示 "导入 N 个宏，跳过 M 个"（有警告时列出前 3 条）。
- `MainWindow` 暴露 `internal IMacroFilePickers? MacroPickersForTests`。

**Tests：**
- [ ] **Step 1: 写失败测试**
  - `MAC08_ImportExportRoundTrip`：建 2 宏（含 Sleep/Key）→ 导出到临时文件（ScriptedMacroFilePickers 经 env）→ 清空 → 导入 → 步骤逐一对比一致、热键 0→null。
  - `MAC09_ExportShowsPlaintextWarning`：测试 hook `Func<string,Task<bool>>? ConfirmAsyncForTests` 记录调用 → 文案含 "exported without encryption"；返回 false → 不产生文件。
  - `MAC10_ImportMalformedFile_SkipsBadEntries`：坏行/未知类型/重名 3 个文件 → 好条目导入、重名带 " (imported)"、无异常。
- [ ] **Step 2: 跑测试确认失败**
- [ ] **Step 3: 实现**
- [ ] **Step 4: 测试通过**
- [ ] **Step 5: Commit** `feat(avalonia): macro import/export with .mxtmacros compatibility`

---

### Task 8: 文档 + 全量 gate + 发布部署 + 冒烟

**Files:**
- Modify: `docs/parity-matrix.md`（MAC-01 行更新为分条：录制/步骤模型/编辑器/右键菜单/热键/Ctrl+Space/导入导出，状态列注明桌面快捷方式 P2 未做）
- Modify: `docs/testing/feature-test-matrix.md`（MAC/MREC/MRUN 全部新测试行）
- Modify: `docs/testing/manual-acceptance.md`（MAC-xx 实机验收项：录制、4 步骤、编辑器、右键 9 项、导出与 MobaXterm 互导、Ctrl+Space、热键、旧配置迁移）

- [ ] **Step 1: 更新三份文档**（测试名与 Task 1-7 实际落地的完全一致）
- [ ] **Step 2: 全量 gate**

Run: `cd /tmp/opencode/COMTool/avalonia && PATH=/tmp/opencode/e2e-deps/usr/bin:$PATH DOTNET_ROOT=/tmp/opencode/dotnet DOTNET=/tmp/opencode/dotnet/dotnet XVFB_BIN=/tmp/opencode/xvfb-deb/root/usr/bin/Xvfb bash pack/verify.sh`
Expected: 全绿（471 旧项中 MAC01 两测被替换，新增 ~30 项）。

- [ ] **Step 3: 发布 + 双份部署 + SSH 冒烟**（deploy_r3.py；产物 `COMTool.App.exe` → `COMTool-Avalonia.exe` 双份覆盖；冒烟：进程起来、config.json 可读写、无崩溃日志）
- [ ] **Step 4: 更新 `avalonia/pack/PLAN-macro-round.md`**（本轮记录，仿 PLAN-servers-round.md）
- [ ] **Step 5: 汇报用户目验**（不 commit——等用户确认；不 push）

## Review Focus（spec 隐含但未显式测试的边界）

| # | 输入/场景 | 期望行为 | 归属 Task + 测试 |
|---|-----------|----------|-----------------|
| 1 | 录制中目标终端断开（serial close / SSH disconnect） | 录制自动停止，已捕获的步骤保留，弹名称对话框（或静默丢弃若为空）；不崩溃 | Task 4：`MAC04b_RecordingStopsWhenTerminalDisconnects` |
| 2 | 宏步骤数 > 1000（极端录制） | 编辑器列表可滚动、不卡 UI；导出/导入不丢数据 | Task 5：`MAC06b_EditorHandlesLargeStepList`（插入 1001 步，断言 ListBox 可滚动、确定后步骤数不变） |
| 3 | 热键分配：用户在编辑器里给宏 A 分配 Ctrl+Shift+A，再给宏 B 也选 Ctrl+Shift+A | 第二个被拒（`IsTaken`），ComboBox 回弹到原值 + 提示 | Task 5：`MAC07b_HotkeyConflictRejected` |
| 4 | 执行中用户切换 tab 或关闭终端 tab | 执行自动取消（CTS 触发），`IsExecutingMacro` 复位，无未处理异常 | Task 4：`MAC13b` 已覆盖关 tab；补 `MAC13c_SwitchTabDoesNotInterrupt`（切 tab 不中断，因为 sink 绑定的是原始 TerminalControl 引用） |
| 5 | 导入文件含 BOM / CRLF / 空 section | Parse 不崩溃，BOM 被跳过，CRLF 当行尾，空 section 无宏 | Task 1：`MacroFileTests.Parse_HandlesBomCrlfAndEmptySections` |

## 已知偏差（写入 spec 附录，非阻塞）

1. 等待字符串步骤的 MobaXterm 导出 typeId 未实测（用户宏中从未出现该类型）：导出用占位 `4`，导入未知 typeId 跳过+警告；两工具互导时 wait 步骤在 MobaXterm 侧可能不识别。
2. 快捷键在 .mxtmacros 中的编码未实测：导出恒 `0`，导入非 0 忽略+警告。
3. 录制间隙→Sleep 的阈值(1000ms)/封顶(1200ms) 由两个实测点推断，未做梯度验证（用户现在占用电脑，无法再上机）；值集中在 `MacroRecorder` 构造参数，后续可调。
4. "等待字符串"超时行为（MobaXterm 未知）实现为：超时跳过继续，默认 30s。
5. 创建桌面快捷方式 = P2 灰显占位；系统级全局热键（MobaXterm 为系统级）实现为应用窗口级（窗口焦点内生效）。
6. 录制中 Ctrl/Alt 组合键不记录（只记录普通文本与 MacroKeys 特殊键）。
