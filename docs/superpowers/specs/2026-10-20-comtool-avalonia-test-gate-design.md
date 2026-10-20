# COMTool Avalonia 全功能测试门禁设计

日期：2026-10-20
状态：已确认，待实现
范围：Avalonia Beta 的测试体系、候选包/正式包流程、CI 与人工签收

## 1. 目标

建立一套可重复执行、能阻止回归的测试门禁，使 COMTool Avalonia 在生成候选包之前必须先通过全部自动化测试，并在正式发布之前完成 Windows 实机与硬件人工签收。

具体目标：

- `docs/parity-matrix.md` 中每个“完成”功能都必须有对应的自动化测试或可执行脚本。
- 每个“部分”功能必须有自动化测试覆盖已完成部分，或有人工验收项覆盖确实无法自动化的部分，并在矩阵中写明原因。
- 标签切换、输入焦点、PTY 生命周期、侧栏展开、双击最大化、SSH 多连接等 UI 行为必须有自动化回归，不能只依赖截图和手工点击。
- 修复 bug 必须先增加能复现问题的失败测试，再修改实现。
- 自动化测试任何一项失败时，不生成候选包，不覆盖正式包。
- 正式包必须来自通过人工签收的候选包，签收文件必须绑定候选包 SHA256。
- 不修改 Python 应用、启动器、Python 3.10、ser2net 或 Python 配置。

## 2. 非目标

- 不把 Python 测试套件纳入 Avalonia 每次打包门禁。只有修改 Python 代码时才单独执行 Python 测试。
- 不引入 Windows FlaUI/WinAppDriver 全 UI 自动化。Windows 真实桌面和硬件验证使用候选包人工签收。
- 不自动验证物理串口热插拔、真实 USB 设备、Windows 白条、DPI 视觉和原生保存对话框。
- 不改变版本号、产品名、现有产物命名规则、远程部署名、R2R 打包策略或现有 UI 行为。
- 不把 2 周 Beta 观察压缩成每次打包步骤。它仍是切换默认版本前的发布门槛。
- 不做逐像素截图比对。自动化验证行为结果，视觉一致性由候选包人工验收。

## 3. 现状与问题

当前测试结构：

- `COMTool.Core.Tests`：配置、会话存储。
- `COMTool.Connections.Tests`：串口、SSH、PTY、ZMODEM 传输。
- `COMTool.Terminal.Tests`：终端仿真和 golden 输出。
- `COMTool.Zmodem.Tests`：ZMODEM 协议、检测、关键字高亮。
- 合计 87 个测试，全部通过。

缺口：

- 没有 `COMTool.App.Tests`，窗口、视图模型和 UI 生命周期没有测试。
- “切换标签后输入失效”这类问题只能通过 Xvfb 手工操作发现。
- `pack.ps1` 和 `pack.sh` 不执行测试，测试与打包之间没有强制关系。
- CI 的 build/test 与 publish job 没有共享同一套本地门禁命令。
- `docs/parity-matrix.md` 的“完成”状态没有测试证据字段，无法判断某个功能是否真的被覆盖。
- 没有候选包与正式包的区分，也没有可机器校验的人工签收记录。

根因不是测试数量不足，而是缺少“功能状态、测试证据、打包动作”三者的强制关联。

## 4. 总体方案

采用四层测试和两阶段产物：

1. 领域单元/集成测试。
2. Avalonia Headless UI 测试。
3. Linux Xvfb 端到端 UI 测试。
4. Windows 候选包人工验收。

产物流程：

```text
源代码
  -> verify（restore/build/全部自动化测试/矩阵检查/Linux E2E）
  -> 候选包（publish/candidate，仅自动化全绿后生成）
  -> Windows 实机与硬件人工签收
  -> 正式包（publish/package，仅签收通过后生成）
```

正式包不是候选包的简单改名，而是由提升脚本在验证签收文件和 SHA256 后复制生成。

## 5. 组件设计

### 5.1 功能测试矩阵

新增 `docs/testing/feature-test-matrix.md`，以 `docs/parity-matrix.md` 的功能 ID 为主键。

列定义：

| 列 | 含义 |
|---|---|
| `Feature` | parity ID，例如 `UI-05` |
| `Status` | `完成`、`部分`、`未开始`、`不适用` |
| `Automated` | 自动化测试 ID，多个用逗号分隔；没有则写 `-` |
| `Phase` | `verify`、`package` 或 `manual` |
| `Platform` | `all`、`linux`、`windows` 或 `macos` |
| `Command` | 运行该测试的项目或脚本 |
| `Manual` | 人工验收项 ID；没有则写 `-` |
| `Evidence` | 截图、日志或测试报告路径 |

规则：

- `完成` 必须有至少一个 `Automated`，且门禁实际执行通过。
- `Phase` 决定该条目在哪个门禁阶段校验；`manual` 只在人工签收阶段校验。
- `部分` 至少要有 `Automated` 或 `Manual` 之一：
  - 有自动化部分时，必须写 `Automated`，并用 `Manual` 覆盖剩余平台/硬件部分。
  - 纯硬件、纯视觉或必须依赖真实桌面的功能可以只有 `Manual`，但必须在 `Evidence` 中写明不能自动化的原因。
- `未开始` 和 `不适用` 不要求测试，但必须显式出现在矩阵中。
- 矩阵中引用的测试 ID 必须能在 `dotnet test --list-tests` 或端到端脚本输出中找到。
- 只有人工验收、没有自动化测试的功能不能标为 `完成`；如果确实无法自动化，状态必须保持 `部分`，直到人工签收完成。
- 新增功能时，未更新矩阵视为门禁失败。

新测试统一使用功能 ID 前缀：

- `UI05_TabSwitchKeepsBothTerminalsLive`
- `TERM15_DoubleClickUsesMaximizePath`
- `SSH06_SecondClickOpensIndependentConnection`
- `SER07_SerialLoopbackReceivesBytes`

已有测试可以保留原名称，在矩阵中填写完整测试名（例如 `COMTool.Core.Tests.ConfigTests...`）。新测试使用前缀，便于矩阵、测试报告和失败日志直接关联。

### 5.2 矩阵完整性检查器

新增跨平台检查器 `avalonia/tools/COMTool.TestGate`，作为 .NET 控制台项目，避免 shell/PowerShell 两套解析逻辑。

职责：

1. 解析 `docs/parity-matrix.md` 中的所有功能行。
2. 解析 `docs/testing/feature-test-matrix.md`。
3. 检查每个 `完成` 和 `部分` 功能都有矩阵条目。
4. 检查 `完成` 功能至少有一个自动化测试；`部分` 功能至少有一个自动化测试或人工验收项。
5. 检查每个矩阵条目引用的测试 ID 存在：
   - 单元/集成/Headless 测试通过 `dotnet test --list-tests` 收集；
   - 端到端测试通过脚本的 `PASS <TestId>` 输出收集；
   - 候选包烟测通过 `pack` 的 `PASS <TestId>` 输出收集。
   - `Phase=verify` 的条目在 `--phase verify` 检查，`Phase=package` 的条目在 `--phase package` 检查。
6. 检查人工验收 ID 存在于 `docs/testing/manual-acceptance.md`。
7. 对只有人工项、没有自动化项的 `部分` 功能，检查矩阵中写明了不能自动化的原因。
8. 输出缺失清单并以非 0 退出码失败。

检查器提供两个阶段：

- `--phase verify`：在候选包生成前检查单元、集成、Headless、E2E 和人工项映射。
- `--phase package`：在候选包烟测后检查包相关测试和最终映射。

两个阶段都失败时退出码非 0。检查器不执行测试，只验证覆盖关系；测试执行由 `verify` 和 `pack` 脚本负责。

### 5.3 Avalonia Headless UI 测试

新增 `avalonia/tests/COMTool.App.Tests`：

- 引用 `COMTool.App`、`COMTool.Core`、`COMTool.Connections`、`COMTool.Terminal`。
- 使用 `Avalonia.Headless.XUnit 12.1.4`、`xunit`、`Microsoft.NET.Test.Sdk`。
- 在 Windows、Linux、macOS 的 CI 上运行。

首批测试：

| 测试 ID | 覆盖功能 | 验证内容 |
|---|---|---|
| `UI02_SidebarExpandsOnLaunch` | UI-02 | 即使配置为折叠，窗口打开后侧栏仍展开，宽度恢复 |
| `UI05_TabSwitchReparentOrder` | UI-05 | 旧终端先 `BeginReparent`，新终端附加后 `EndReparent` |
| `UI05_TabSwitchKeepsBothTerminalsLive` | UI-05 | 两个终端使用内存 PTY，切换后都能继续接收输入 |
| `UI05_TabSwitchFocusesIncomingTerminal` | UI-05 | 切换后键盘输入进入新选中的终端 |
| `UI05_CloseSelectedTabSelectsNeighbor` | UI-05 | 关闭当前标签后的选中规则与 Python 一致 |
| `TERM15_DoubleClickUsesMaximizePath` | TERM-15 | 双击终端调用与标题栏最大化相同的路径 |
| `UI08_SettingsReplacesEditedTabOnly` | UI-08 | 设置保存只替换被编辑的标签，不影响同会话的其他并行标签 |
| `SSH06_ActivationPolicy` | SSH-06 | 有存活连接时新增连接；无存活连接时清理死标签后重连 |

实现要点：

- 使用真实 `TerminalControl` 加内存 PTY 连接，不启动外部 shell。
- 通过 Avalonia Headless 的键盘注入验证焦点，不只调用 `Focus()`。
- 测试使用临时配置目录，不读写真实用户配置。
- 无法在 Headless 中可靠复现的真实 PTY/窗口管理器行为，由 Xvfb 端到端测试补齐，不以 Headless 结果替代。

### 5.4 测试配置隔离

在 `COMTool.Core.Config.ConfigPaths` 增加两个可选环境变量：

- `COMTOOL_AVALONIA_CONFIG_DIR`：覆盖 Avalonia 配置目录。
- `COMTOOL_PYTHON_CONFIG_DIR`：覆盖 Python 配置查找目录，仅用于测试隔离。

规则：

- 只有环境变量非空时才覆盖默认路径。
- 生产启动不设置这两个变量，行为不变。
- 测试和端到端脚本总是设置到临时目录。
- Avalonia 与 Python 配置目录不能相同。
- 测试成功后在进程退出时删除临时目录；失败时保留路径用于排查，但不得写入真实密码或真实用户配置。
- 该覆盖能力写入 `docs/dev.md`，明确标注为测试/便携用途。

### 5.5 Linux Xvfb 端到端测试

新增 `avalonia/tests/e2e/linux/run-ui-e2e.sh`。

环境：

- Xvfb 独立 display。
- xdotool 注入鼠标和键盘。
- 本地 OpenSSH 服务，独立端口和临时 host key。
- 临时 `HOME`、Avalonia 配置目录和 SSH 密钥。
- 测试结束通过 trap 清理进程、临时目录和 display。

首批用例：

| 测试 ID | 覆盖功能 | 验证内容 |
|---|---|---|
| `UI02_SidebarExpandsOnLaunchE2E` | UI-02 | 启动后侧栏可见，会话行可点击 |
| `UI05_TabSwitchTypingBothDirections` | UI-05 | 两个 SSH 标签来回切换后直接输入，命令在正确 shell 执行 |
| `SSH06_MultipleConnectionsE2E` | SSH-06 | 同一 SSH 会话点击两次得到两个独立 shell |
| `TERM15_DoubleClickMaximizeE2E` | TERM-15 | 双击终端后窗口尺寸变为最大化，再双击恢复 |

输入正确性验证方式：

- 每个 SSH shell 先进入独立临时目录。
- 切换标签后输入 `touch <标记文件>`，不通过截图判断。
- 断言标记文件出现在该标签对应的目录，而不是另一个标签的目录。
- 两个方向都执行一次，分别验证切到第二个标签和切回第一个标签。

脚本输出固定格式：

```text
PASS UI05_TabSwitchTypingBothDirections
FAIL SSH06_MultipleConnectionsE2E: <原因>
```

门禁检查器从输出中收集测试 ID。

稳定性措施：

- 每个用例等待 shell 提示符或标记文件，不使用无限固定 sleep。
- 标记文件轮询有超时，超时输出当前窗口截图和日志。
- 使用独立端口，避免与用户现有 sshd、ser2net 冲突。
- 失败时保留临时目录路径，成功后清理。
- 依赖缺失时在 Linux 上直接失败，不静默跳过。

### 5.6 统一验证与打包命令

新增：

- `avalonia/pack/verify.sh`
- `avalonia/pack/verify.ps1`

步骤：

1. `dotnet restore avalonia/COMTool.Avalonia.sln`
2. `dotnet build -c Release --no-restore`
3. `dotnet test -c Release --no-build`，启用 TRX 日志
4. Linux 上执行 `run-ui-e2e.sh`
5. 执行 `COMTool.TestGate` 矩阵检查
6. 汇总结果，任一步失败立即退出非 0

打包入口：

- `avalonia/pack/windows/pack.ps1`
- `avalonia/pack/linux/pack.sh`

行为：

1. 先调用 `verify`。
2. 发布 R2R 单文件应用到临时 staging 目录。
3. 对 staging 可执行文件执行启动烟测，输出 `PASS PKG01_CandidateSmoke`。
4. 烟测通过后才生成候选包到 `avalonia/publish/candidate/`。
5. 计算并写入候选包 SHA256 清单和 `candidate.json`。
6. 调用 `COMTool.TestGate --phase package` 检查包阶段覆盖关系。
7. 包阶段检查失败时删除本次候选包并退出非 0，不覆盖已有候选包或正式包，也不留下可发布的半成品。

测试结果目录：

```text
avalonia/publish/test-results/
  <timestamp>/
    unit-tests.trx
    app-tests.trx
    e2e.log
    screenshots/
    summary.txt
```

### 5.7 候选包与正式包

目录：

```text
avalonia/publish/candidate/
  COMTool-Avalonia-Beta-win-x64-0.01.zip
  COMTool-Avalonia-Beta-win-x64-0.01/
  SHA256SUMS.txt
  candidate.json

avalonia/publish/package/
  COMTool-Avalonia-Beta-win-x64-0.01.zip
  SHA256SUMS.txt
  release.json
```

`candidate.json` 包含：

- 版本。
- 目标平台。
- 候选包 SHA256。
- 构建时间。
- 测试报告目录。
- 源代码提交号。
- 自动化测试汇总（通过/失败/跳过）。

`release.json` 包含：

- 候选包 SHA256。
- 正式包 SHA256。
- 签收文件路径。
- 提升时间。

### 5.8 人工验收与签收

新增：

- `docs/testing/manual-acceptance.md`：人工验收项清单。
- `docs/testing/signoffs/windows-0.01-<sha12>.json`：每个候选包一份签收记录。

人工验收项至少包括：

| ID | 覆盖功能 | 内容 |
|---|---|---|
| `WIN-01` | UI-01 | Windows 无原生白条，标题栏按钮、拖拽、缩放正常 |
| `WIN-02` | UI-14 | Windows 150%/200% DPI 无裁切 |
| `WIN-03` | SER-03 | 串口热插拔后列表刷新 |
| `WIN-04` | SER-06 | 占用端口显示正确错误 |
| `WIN-05` | SER-07 | 串口回环收发 |
| `WIN-06` | SSH-04 | Windows DPAPI 记住密码并重连 |
| `WIN-07` | ZM-02/ZM-03 | 原生保存/选择文件对话框完成 ZMODEM 收发 |
| `WIN-08` | PKG-04 | 安装、升级、卸载保留 Beta 配置 |
| `WIN-09` | TST-06 | 候选包 8 小时稳定性运行 |

签收文件格式：

```json
{
  "product": "COMTool-Avalonia",
  "version": "0.01",
  "platform": "windows-x64",
  "candidateSha256": "完整的 64 位 SHA256",
  "tester": "姓名",
  "date": "2026-10-20",
  "windowsVersion": "Windows 11 23H2",
  "hardware": ["COM97 CH344", "COM99 CH344"],
  "items": [
    {
      "id": "WIN-01",
      "result": "PASS",
      "evidence": "docs/testing/evidence/..."
    }
  ],
  "soak": {
    "hours": 8,
    "result": "PASS",
    "evidence": "docs/testing/evidence/..."
  }
}
```

提升脚本 `avalonia/pack/promote.ps1`：

1. 读取 `candidate.json`。
2. 查找对应 SHA256 的签收文件。
3. 校验 SHA256 完全一致。
4. 校验所有必需人工项存在且为 `PASS`。
5. 校验 8 小时稳定性为 `PASS`。
6. 校验没有 `FAIL` 或空证据。
7. 通过后复制候选包到 `publish/package/`，写入 `release.json`。

任何一项不满足都不生成正式包。

## 6. 数据流

### 6.1 自动化门禁

```text
verify
  -> parity-matrix + feature-test-matrix
  -> dotnet restore/build
  -> 单元/集成/Headless 测试
  -> Linux E2E
  -> TestGate 覆盖检查
  -> 退出码 0/非 0
```

### 6.2 候选包

```text
pack
  -> verify 成功
  -> publish R2R 到 staging
  -> staging 启动烟测
  -> 生成 candidate 包
  -> 写 candidate.json + SHA256
  -> TestGate --phase package
```

### 6.3 正式包

```text
promote
  -> 读取 candidate.json
  -> 匹配 signoff JSON
  -> 校验 SHA256/人工项/8 小时稳定性
  -> 复制到 package
  -> 写 release.json
```

## 7. 错误处理

- 测试失败：停止门禁，保留 TRX、E2E 日志和截图，不生成候选包。
- 矩阵缺项：列出缺失功能 ID 和缺失测试 ID，停止门禁。
- 测试 ID 不存在：视为矩阵过期，停止门禁。
- 候选包烟测失败：不生成候选包，不写 `candidate.json`，保留失败日志。
- 签收文件缺失或 SHA256 不匹配：拒绝提升正式包。
- 人工项有 FAIL 或证据为空：拒绝提升正式包。
- 8 小时稳定性未完成：拒绝提升正式包。
- E2E 依赖缺失：Linux 上直接失败并提示安装命令；不静默跳过。
- 临时目录清理失败：不影响测试结论，但打印路径供排查。

## 8. CI 设计

`.github/workflows/avalonia.yml` 调整为：

1. `verify` job：
   - Ubuntu、Windows、macOS 矩阵。
   - 调用对应平台的 `verify` 脚本。
   - Linux 额外安装 Xvfb、xdotool、openssh-server、lrzsz 并运行 E2E。
   - 上传 TRX、E2E 日志和截图。
2. `publish-windows`：
   - `needs: verify`。
   - 调用 `pack.ps1`，上传候选包和 `candidate.json`。
3. `publish-linux`：
   - `needs: verify`。
   - 调用 `pack.sh`，上传候选包和 `candidate.json`。
4. 正式包不在 CI 自动生成；人工签收后由 `promote` 生成。

CI 与本地使用相同脚本，避免两套标准。

## 9. 测试策略

### 9.1 单元/集成测试

保留现有 87 个测试，并补充：

- SSH 会话点击策略的纯逻辑测试。
- 标签选择/关闭规则测试。
- 配置目录覆盖测试。
- 双击最大化路径测试（不启动窗口的部分）。

### 9.2 Headless UI 测试

新增 `COMTool.App.Tests`，覆盖第 5.3 节列出的行为。所有测试使用临时配置和内存 PTY。

### 9.3 端到端测试

新增 Linux Xvfb 脚本，覆盖第 5.5 节列出的用例。E2E 是 UI-05 和 SSH-06 的最终自动化证据。

### 9.4 人工验收

按第 5.8 节执行。人工验收只针对无法自动化的平台、硬件和视觉项，不重复自动化已经覆盖的逻辑。

### 9.5 回归规则

- 修复 bug 的提交必须先包含一个在旧代码上失败的测试。
- 测试名称包含 parity ID。
- 同一提交更新 `feature-test-matrix.md`。
- 没有回归测试的 bug 修复不能进入候选包。

## 10. 实施顺序

1. 增加 `COMTool.TestGate`、矩阵模板和矩阵解析。
2. 增加配置目录覆盖和 `COMTool.App.Tests` Headless 测试。
3. 增加 Linux Xvfb E2E 脚本和本地 sshd fixture。
4. 增加 `verify`、候选包/正式包目录和 `promote` 脚本。
5. 修改 Windows/Linux 打包脚本，移除跳过测试的正式包路径。
6. 修改 CI 使用统一门禁。
7. 完成 87 个现有测试到功能矩阵的映射。
8. 对缺失测试的功能补测试；无法自动化的转为人工验收。
9. 运行完整门禁，生成候选包。
10. 完成 Windows/硬件人工签收。
11. 提升正式包；2 周 Beta 和默认版本切换仍按原发布门槛执行。

## 11. 风险与缓解

| 风险 | 缓解 |
|---|---|
| Headless 无法完全模拟真实焦点/PTY | UI-05/SSH-06 同时保留 Xvfb E2E；Headless 只覆盖窗口状态和 reparent 顺序 |
| Xvfb 测试时序不稳定 | 使用标记文件轮询、独立端口、独立 HOME、失败截图和超时 |
| 矩阵与测试脱节 | 测试 ID 前缀 + `--list-tests` + E2E 输出解析 + 门禁强制 |
| 打包脚本绕过测试 | 正式包只能由 `promote` 生成；`pack` 无跳过测试参数 |
| 测试污染用户配置 | `COMTOOL_AVALONIA_CONFIG_DIR`/`COMTOOL_PYTHON_CONFIG_DIR` 临时覆盖 |
| CI Windows 无法跑 E2E | Windows 自动化只跑 Headless；真实桌面由候选包人工签收 |
| 门禁耗时过长 | 测试项目并行、NuGet 缓存、E2E 只跑一次、长稳定性测试只在发布候选包执行 |
| 人工签收流于形式 | 签收文件绑定 SHA256，提升脚本校验必填项和证据路径 |

## 12. 完成标准

以下条件全部满足才算本设计实现完成：

- `docs/testing/feature-test-matrix.md` 覆盖所有 `完成` 和 `部分` 功能。
- `COMTool.TestGate` 能在缺项、引用不存在测试时失败。
- `COMTool.App.Tests` 在 Windows/Linux/macOS Headless 环境通过。
- Linux Xvfb E2E 在 CI 和本地通过，包含两个方向的标签切换输入验证。
- `verify` 在干净环境可重复通过。
- `pack` 只能在 `verify` 通过后生成候选包。
- `promote` 只能提升有匹配 SHA256 且全部人工项 PASS 的候选包。
- CI 使用相同门禁，测试报告可下载。
- 不修改 Python 应用、Python 配置、启动器、ser2net 或 Python 3.10。
- 版本保持 `0.01`，现有打包命名和远程部署名保持不变。

## 13. 待实现后确认的事项

以下不是设计空白，而是需要在实际候选包上执行后才能填写的验收结果：

- Windows 实机人工项的具体证据路径。
- 8 小时稳定性运行的资源曲线。
- 2 周 Beta 观察记录。
- 默认版本切换和回滚演练结果。

这些结果写入签收文件和发布记录，不在本设计阶段预先填写。
