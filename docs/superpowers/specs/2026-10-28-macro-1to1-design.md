# COMTool 宏功能 1:1 复刻 MobaXterm 设计

## 目标

将 COMTool 的宏功能从"单行命令 + 可选回车"升级为与 MobaXterm Professional 22.1 完全对齐的多步骤宏系统：支持录制、手动编辑、4 种步骤类型（文本/按键/睡眠/等待字符串）、快捷键绑定、Ctrl+Space 弹出菜单、导入/导出（.mxtmacros 格式兼容）。

## 实测基准（MobaXterm 22.1 实机 RDP 验证 2026-10-28）

### 宏数据模型

```
Macro = {
  name: string          // 唯一标识，也用于显示
  hotkey: string|null   // 快捷键字符串，null = 无
  steps: Step[]         // 有序步骤列表
}

Step =
  | { type: "text",  text: string, keyDelayMs: int }     // 文本 + 按键间延迟
  | { type: "key",   key: string }                        // 特殊键 (RETURN/TAB/ESC/UP/DOWN/LEFT/RIGHT/...)
  | { type: "sleep", ms: int }                            // 纯延时
  | { type: "wait",  pattern: string, timeoutMs: int }    // 等待终端输出匹配
```

### 录制行为（实机验证）

- 入口：侧栏"宏"tab 第 1 行 `● Record new macro` 按钮（红色圆点图标）；或顶栏菜单 `宏 → Record new macro`
- 录制中 UI：
  - 顶栏出现红点 + 文字 "正在录制新宏: 您在当前终端中键入的所有内容都将被保存。" + 右上角"停止"按钮
  - 侧栏行 1 变为 "Stop recording macro"
  - 录制目标 = 当前活动标签页终端（录制开始时的活动 tab）
- 按键捕获规则（推断 + 实测）：
  - 连续可打印字符 → 合并为 1 个 Text 步骤（keyDelay = 按键间隔中位数，上限约 100ms）
  - 特殊键（Enter/Tab/Esc/箭头/F键等）→ 独立 Key 步骤
  - 两键之间间隙 > 阈值（实测 ~500ms 不记，≥1s 记为 SLEEP）→ 插入 Sleep 步骤
  - 录制结束时的尾部间隙也记为 Sleep（实测 1200ms）
- 停止录制：
  - 无任何按键 → 静默丢弃，无对话框
  - 有内容 → 弹出 "Please enter a name for this macro" 对话框，默认名 = 录制首行文本（预填+选中），OK/Cancel

### 步骤编辑器（"Macro 编辑" 对话框）

- 模态对话框，标题 "Macro 编辑"
- 左侧：步骤列表（ListBox），每行 = 图标 + 摘要文本
  - 文本步骤显示：`"echo probe-macro"`
  - 按键步骤显示：`RETURN`
  - 睡眠步骤显示：`SLEEP ms=1200`
  - 等待步骤显示：`WAIT "pattern"`
- 右侧 4 按钮（纵向）：
  - `编辑选定的行` — 弹出步骤类型编辑对话框
  - `Insert new line above` — 在选中行上方插入新步骤
  - `在下方插入新行` — 在选中行下方插入新步骤
  - `删除行` — 删除选中步骤
- 底部：
  - 快捷键下拉框（默认 `<none>`，可选 Ctrl+字母/组合键）
  - 提示文字："or use Ctrl+SPACE in order to display the popup macros menu"
  - 确定 / 取消 按钮

**步骤编辑对话框**（4 种 radio 切换）：
- ○ 文本 (Text)：文本输入框 + "按键之间的延迟: [N] ms"
- ○ 按键 (Key)：下拉选择（RETURN, TAB, ESC, UP, DOWN, LEFT, RIGHT, HOME, END, PAGEUP, PAGEDOWN, INSERT, DELETE, F1-F12, ...）
- ○ 睡眠 (Sleep)：数值输入 "N ms"
- ○ 等待字符串 (Wait for string)：模式输入框（支持通配符 * ?）

### 右键菜单（宏列表行上）

| 项目 | 说明 |
|------|------|
| Rename | 重命名（弹窗输入） |
| Edit | 打开步骤编辑器 |
| Duplicate | 复制（名称加 " (copy)"） |
| Delete | 删除（确认对话框） |
| Assign hotkey | 快捷键分配对话框 |
| Execute | 立即执行 |
| 创建桌面快捷方式(W) | Windows shell 快捷方式（P2，暂不实现） |
| 导入宏(Y) | 打开文件对话框 → 解析 .mxtmacros → 合并 |
| 导出宏(Z) | 明文警告 → 保存对话框 → 写 .mxtmacros |

### 导出/导入格式（.mxtmacros，INI 文本）

```ini
[Macros]
MacroName1=<step>|<step>|...|
MacroName2=...

[MacrosHotkeys]
MacroName1=<hotkeyInt or 0>
MacroName2=0
```

步骤编码：`typeId:field1:field2:payload|`
- 文本步骤：`12:<keyDelayMs>:0:<text>`
- 按键步骤：`258:<vkCode>:<flags?>:<KEYNAME>`
- 睡眠步骤：`0:0:0:SLEEPEQUAL<ms>`
- 等待字符串：`<typeId 待确认>:0:0:WAIT<pattern>`（待下次实机补全 typeId）

导出前弹确认："WARNING: your macros will be exported without encryption..."（是Y/否N）
导出范围：**全部宏**（非单个），文件名默认 `MobaXterm Macros.mxtmacros`。

### 执行（回放）行为

- 触发方式：
  1. 侧栏列表双击（或右键 Execute）
  2. 绑定的快捷键
  3. Ctrl+Space 弹出菜单选择
- 执行目标：当前活动终端 tab（无活动终端时禁用/提示）
- 步骤执行顺序：
  - Text：逐字符 `SendInputAsync(char)` 间隔 `keyDelayMs`；默认 0ms = 一次性发送
  - Key：发送对应转义序列/控制字符（RETURN→\r, TAB→\t, ESC→\x1b, 箭头→\x1b[A 等）
  - Sleep：`Task.Delay(ms)`
  - Wait：轮询终端缓冲区（每 50ms）直到 pattern 出现在可见文本中或超时（默认 30s）；超时后跳过继续下一步
- 执行中不可重入（同一宏执行中不可再次触发）
- 执行中顶栏显示 "正在执行宏: <name>" 状态条 + 取消按钮

### 侧栏面板布局（宏 tab）

```
┌─────────────────────────────┐
│  ● Record new macro  [全宽] │  ← 红点图标按钮
├─────────────────────────────┤
│  已保存的宏    (灰/黑)      │  ← 分组标题，空时灰色
├─────────────────────────────┤
│  ◎ MacroName1               │
│  ◎ MacroName2               │
│  ◎ MacroName3               │
│  ...（可滚动）              │
└─────────────────────────────┘
```

- 当前 COMTool 的内联编辑卡片（MacroNameBox/MacroTextBox/MacroSaveButton）移除，替换为 MobaXterm 风格的 Record 按钮 + 列表
- 列表行：◎ 图标 + 宏名称（单行，无副标题）
- 右键弹出上下文菜单（9 项中的 8 项，桌面快捷方式 P2 跳过）

### 快捷键系统

- 每个宏可绑定一个全局快捷键（存储为字符串如 "Ctrl+Shift+M"）
- 快捷键触发时：若有活动终端 → 执行宏；若无 → 忽略
- 快捷键分配通过右键 "Assign hotkey" 或编辑器底部下拉
- 可用键位：Ctrl+字母、Ctrl+Shift+字母、Ctrl+Alt+字母、F1-F12（排除与现有冲突：Ctrl+W/T/N/S/F11 等）
- 冲突检测：分配时检查是否与已有宏/系统快捷键冲突，冲突则提示

### Ctrl+Space 弹出菜单

- 当终端有焦点且无宏正在录制/执行时，Ctrl+Space 弹出浮层
- 浮层 = 宏列表（名称 + 快捷键提示），键盘可上下选择 + 回车执行，鼠标可点击
- Esc 关闭

## 范围

### 包含

1. **数据模型重构**：`MacroConfig` 扩展为 `steps[]` + `hotkey`；旧格式自动迁移（text→TextStep, appendEnter→追加 KeyStep(RETURN)）
2. **录制引擎**：`MacroRecorder` 服务，hook 窗口级 PreviewKeyDown/TextInput，时间戳采集，停止时合成步骤列表
3. **步骤执行引擎**：`MacroRunner` 服务，异步逐步执行（Text 逐字/Key/Sleep/Wait），支持取消
4. **宏编辑器对话框**：独立 Window（步骤列表 + 4 按钮 + 快捷键下拉 + 确定/取消）
5. **步骤编辑对话框**：4 种 radio 类型 + 对应输入控件
6. **侧栏宏 tab 重做**：Record 按钮 + 列表行（◎ 图标 + 名称）+ 右键菜单
7. **名称对话框**：录制完成后弹出（默认名 = 首行文本）
8. **删除确认**：是/否
9. **导入/导出**：.mxtmacros INI 格式读写（3 种已知类型完全兼容，等待步骤 typeId 用 3 占位待验证）
10. **导出警告**：明文警告对话框
11. **快捷键系统**：分配/冲突检测/全局监听
12. **Ctrl+Space 弹出菜单**：终端焦点下弹出宏选择浮层
13. **执行状态指示**：顶栏 "正在执行宏: X" + 取消按钮
14. **顶栏"宏"菜单**：Record new macro / 已保存的宏（跳转侧栏）

### 不包含（P2/后续）

- 创建桌面快捷方式（Windows Shell 集成）
- 等待字符串的超时精确值调优（默认 30s，后续可调）
- 宏的密码字段加密（MobaXterm 本身也是明文）
- 录制时的"暂停/继续"（MobaXterm 无此功能）

## 架构

### 新增文件

| 文件 | 职责 |
|------|------|
| `src/COMTool.Core/Macros/MacroStep.cs` | 步骤模型（discriminated union via `StepType` enum） |
| `src/COMTool.Core/Macros/MacroDefinition.cs` | 宏定义（name, hotkey, steps） |
| `src/COMTool.Core/Macros/MacroSerializer.cs` | .mxtmacros INI 格式 读写 |
| `src/COMTool.Core/Macros/MacroMigrator.cs` | 旧 config (text+appendEnter) → 新 steps 格式迁移 |
| `src/COMTool.App/Services/MacroRecorder.cs` | 录制引擎：hook 按键事件 → 时间戳流 → 步骤列表 |
| `src/COMTool.App/Services/MacroRunner.cs` | 执行引擎：逐步回放 + 等待 + 取消 |
| `src/COMTool.App/ViewModels/MacrosViewModel.cs` | 宏 tab 的 ViewModel（列表、录制状态、执行状态） |
| `src/COMTool.App/Views/MacroEditorWindow.axaml(.cs)` | 宏步骤编辑器对话框 |
| `src/COMTool.App/Views/MacroStepEditorDialog.axaml(.cs)` | 单步骤编辑对话框（4 种类型） |
| `src/COMTool.App/Views/MacroNameDialog.axaml(.cs)` | 录制完成后命名对话框 |
| `src/COMTool.App/Views/MacroPopup.axaml(.cs)` | Ctrl+Space 弹出宏菜单 |

### 修改文件

| 文件 | 改动 |
|------|------|
| `MainViewModel.Sidebar.cs` | MacroItem → 持有 steps[]; 新增 IsRecording/IsExecuting 状态; 新增 RecordMacro/StopRecording/ExecuteMacro/AssignHotkey/ImportMacros/ExportMacros 命令 |
| `MainViewModel.cs` | 注册全局快捷键监听; Ctrl+Space 拦截 |
| `MainWindow.axaml.cs` | 录制 hook（PreviewKeyDown/TextInput → MacroRecorder）; 执行状态栏; 右键菜单; 弹窗管理 |
| `SessionSidebar.axaml(.cs)` | 宏面板重做（Record 按钮 + 列表 + 右键 ContextMenu） |
| `ComtoolConfig.cs` | MacroConfig 扩展 steps/hotkey; 旧字段保留做迁移 |
| `AppStyles.axaml` | 宏相关样式（Record 按钮红点、执行状态条、弹出菜单） |
| `MainWindow.axaml` | 顶栏"宏"菜单项; 执行状态条 |

### 关键设计决策

1. **录制实现**：在 MainWindow 的 tunnel-level `PreviewKeyDown` + `PreviewTextInput` 中拦截（已有 `OnWindowKeyDown` 先例）。录制中记录 `(timestamp, key/text)` 事件流。停止时 `MacroRecorder.Finalize()` 将事件流合并为步骤列表（连续文本合并为 1 个 TextStep，间隙 > 阈值插入 SleepStep）。阈值默认 500ms，可配置。

2. **等待字符串实现**：`MacroRunner` 在 WaitStep 时订阅 `TerminalControl.OutputReceived` 事件（或轮询 `TerminalTranscript.Capture()`），每次输出到达时检查 pattern（支持 * ? 通配符 → 转 regex），匹配则继续，超时（30s）则跳过。

3. **快捷键注册**：使用 Avalonia `InputBindings` 在 MainWindow 级别注册动态快捷键。宏的 hotkey 变更时重新注册。

4. **配置兼容**：`MacroConfig` 新增 `steps: List<MacroStepConfig>?` 和 `hotkey: string?`。Load 时若 steps 为空但 text 非空 → 自动迁移。Save 时写新格式（不再写 text/appendEnter）。`Version` 保持 3（向后兼容：旧版本读新文件时忽略未知字段 via JsonExtensionData）。

5. **导出/导入**：
   - 导出：`MacroSerializer.ExportAll(macs) → string`，格式完全兼容 MobaXterm .mxtmacros
   - 导入：`MacroSerializer.Import(string) → List<MacroDefinition>`，解析 [Macros] + [MacrosHotkeys]
   - 未知 typeId 的步骤 → 跳过 + 日志警告
   - 导入时同名宏 → 追加 " (imported)" 后缀

## 测试

### COMTool.Core.Tests（纯逻辑，无 UI）

- `MacroStepTests`：步骤序列化/反序列化、各类型 round-trip
- `MacroSerializerTests`：.mxtmacros 格式 写→读→写 幂等性；与 MobaXterm 真实导出样本对比
- `MacroMigratorTests`：旧格式 (text="ls", appendEnter=true) → [Text("ls"), Key(RETURN)]
- `MacroRecorderTests`：给定按键事件流 → 断言步骤列表（连续字符合并、间隙→Sleep、特殊键→Key）
- `MacroRunnerTests`：fake terminal（实现 ITerminalSink）→ 执行各步骤类型 → 断言发送顺序/延时

### COMTool.App.Tests（headless Avalonia）

- `MAC01_SidebarShowsRecordButtonAndList`：宏 tab 显示 Record 按钮 + 空态"已保存的宏"灰
- `MAC02_RecordFlow`：模拟按键事件 → 停止 → 名称对话框出现 → 确认 → 列表有宏
- `MAC03_RecorderMergesConsecutiveChars`：连续输入 "hello" → 1 个 TextStep
- `MAC04_RecorderInsertsSleepForGap`：输入间 1s 停顿 → 出现 SleepStep
- `MAC05_EmptyRecordingDiscarded`：无任何按键停止 → 无对话框、无新宏
- `MAC06_EditorOpensWithSteps`：右键 Edit → 编辑器打开，步骤列表正确
- `MAC07_StepEditDialogs`：4 种类型 radio 切换、字段验证
- `MAC08_ImportExportRoundTrip`：导出 → 导入 → 步骤一致
- `MAC09_ExportWarningDialog`：导出前弹出明文警告
- `MAC10_DeleteConfirmation`：删除 → 确认对话框 → 确认后消失
- `MAC11_HotkeyAssignAndTrigger`：分配 Ctrl+Shift+M → 按键触发执行
- `MAC12_CtrlSpacePopup`：Ctrl+Space → 弹出菜单 → 选择 → 执行
- `MAC13_ExecuteSendsToTerminal`：执行宏 → 活动终端收到预期字节
- `MAC14_WaitStepTimesOut`：wait 步骤超时 → 跳过继续
- `MAC15_OldFormatMigration`：旧 config (text+appendEnter) → 加载后等价 steps
- `MAC16_RecordingBarVisibility`：录制中顶栏红条出现/消失
- `MAC17_ExecutionStatusAndCancel`：执行中状态条显示 + 取消中断

## 验收

- 录制：在本地终端输入命令 → 停止 → 自动命名 → 宏出现在列表
- 编辑器：打开宏 → 看到步骤列表 → 增/删/改步骤 → 保存
- 4 种步骤类型均可手动添加和编辑
- 执行：双击/右键 Execute/快捷键 → 步骤按序发送到活动终端
- 等待字符串：执行含 wait 步骤的宏，终端输出匹配后继续
- 导入 .mxtmacros 文件（含 MobaXterm 导出的真实文件）→ 宏出现
- 导出 → 文件内容与 MobaXterm 格式一致（可用 MobaXterm 导入验证）
- 右键菜单 8 项全部可用（桌面快捷方式灰显"暂不支持"）
- Ctrl+Space 在终端焦点时弹出宏列表
- 快捷键分配后全局生效（终端焦点下也能触发）
- 旧配置（text+appendEnter 格式）升级后自动迁移，宏可正常执行
- 全量 gate PASS（现有 471 + 新增 ~17 测试）
- 用户实机目验通过

## 约束

- 测试名 `ClassName.MethodName` 格式
- 版本号保持 `0.01`
- 不推送远端（等用户确认）
- Windows 产物 win-x64 单文件 R2R，就地覆盖
- 不引入新 NuGet 依赖（录制/执行/快捷键均用 Avalonia 内置能力）
- .mxtmacros 导出格式必须与 MobaXterm 兼容（用户可在两个工具间互导）
- 录制仅捕获当前活动终端的输入，不影响其他 tab
