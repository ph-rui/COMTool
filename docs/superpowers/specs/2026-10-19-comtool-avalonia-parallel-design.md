# COMTool Avalonia 并行重构设计

- 状态：Phase 0 执行中
- 日期：2026-10-19
- 目标仓库：`https://github.com/ph-rui/COMTool`
- 当前 Python 基线：`7ae55a5`（`feature/live-port-refresh`）
- 目标平台：Windows 10/11、Linux、macOS
- 许可证：LGPL-3.0（保持不变）
- 对应计划：`docs/superpowers/plans/2026-10-19-comtool-avalonia-phase0-plan.md`

## 1. 背景与决策

现有 COMTool 使用 Python + PyQt5，已经具备完整的串口、SSH、TCP/UDP、终端、ZMODEM、插件和会话侧栏能力。近期已经优化了终端解析和侧栏刷新，但 Python + pyte 在持续大流量输出下仍有性能上限。

经过评估，决定采用：

- Python/PyQt5 版本继续作为稳定版；
- 使用 Avalonia + C# 全量实现新版本；
- 两个版本并行开发；
- Avalonia 版本达到稳定标准后，再切换为默认版本；
- Python 版本在切换后保留一个过渡周期，作为回滚方案。

### 1.1 初期原则：一比一复刻

初期阶段不是重新设计产品，而是 **用 Avalonia 一比一复刻当前 Python 版本的 UI 和功能**。

“一比一”指：

- 相同的窗口结构、控件布局、间距、层级和默认尺寸；
- 相同的文字、翻译 key、图标、颜色、字体和主题；
- 相同的菜单、按钮、下拉框、对话框、提示和状态栏；
- 相同的快捷键、鼠标行为、右键菜单、选择复制、拖拽和焦点行为；
- 相同的连接参数、默认值、配置字段、会话生命周期和错误提示；
- 相同的串口、SSH、TCP/UDP、终端、ZMODEM、插件和协议功能。

允许的差异仅限：

- Qt 与 Avalonia 字体栅格化造成的像素级差异；
- 平台原生窗口装饰、文件选择器、输入法候选框；
- 内部实现语言、线程模型和依赖库；
- 经明确批准的、无法在 Avalonia 中保持一致的控件替换。

任何功能删减、交互变更或视觉重设计都必须单独提出并获批，不能以“Avalonia 更现代”为理由自行改变。

## 2. 目标与非目标

### 2.1 目标

- G1：建立可长期维护的 `avalonia/` C# 解决方案，与现有 Python 代码并行存在。
- G2：Avalonia 版本的 UI 和功能与 Python 版本一比一复刻。
- G3：保持 `config.json` 数据模型、会话语义和 `.po` 翻译内容兼容。
- G4：Avalonia Beta 使用独立配置目录、安装标识和更新通道，避免影响稳定版。
- G5：建立功能对齐清单、UI 基准截图和自动化测试。
- G6：在 Windows、Linux、macOS 上可构建和发布。
- G7：达到稳定标准后再切换默认版本。

### 2.2 非目标

- N1：不在第一阶段重新设计 UI、增加新功能或改变交互。
- N2：不要求 Python 插件二进制直接加载到 C# 进程；插件兼容范围后续单独决策。
- N3：不让两个版本同时写同一份 `config.json`。
- N4：不在切换前删除 Python 版本或迁移其安装目录。
- N5：不承诺 Avalonia 与 Qt 在字体渲染上逐像素完全一致。

## 3. 仓库、分支与发布

### 3.1 目录结构

```text
COMTool/                     # 现有 Python 稳定版
avalonia/
  COMTool.Avalonia.sln
  global.json
  Directory.Build.props
  src/
    COMTool.App/             # Avalonia UI、MVVM、主题、窗口
    COMTool.Core/            # 配置、会话、模型、通用服务
    COMTool.Connections/     # Serial / SSH / TCP / UDP
    COMTool.Terminal/        # 终端控件封装、高亮、选择
    COMTool.Zmodem/          # ZMODEM 移植
  tests/
    COMTool.Core.Tests/
    COMTool.Terminal.Tests/
    COMTool.Connections.Tests/
docs/
  parity-matrix.md
  fixtures/
  superpowers/specs/
  superpowers/plans/
```

Python 文件在过渡期保持在仓库根目录，不移动，避免破坏现有打包、脚本和用户习惯。切换后再决定是否把 Python 版本移入 `legacy/`。

### 3.2 分支策略

- `main`：Python 稳定版，保持可发布。
- `avalonia`：Avalonia 长期集成分支。
- `avalonia/feature-*`：Avalonia 功能分支。
- Python 侧只接受关键 bug、安全、兼容性修复。
- 新功能默认只进入 Avalonia；如果必须同时进入 Python，先记录到 `docs/parity-matrix.md`。

### 3.3 版本与发布

- Python：`py-vX.Y.Z`
- Avalonia Beta：`av-vX.Y.Z-beta.N`
- 两个版本使用独立的安装标识、AppUserModelID、配置目录和日志目录。
- Avalonia 更新通道不能覆盖 Python 稳定版安装。
- 切换前，Avalonia 始终标记为 `Beta` / `Next`，避免用户误认。

## 4. 配置、会话与兼容

### 4.1 配置隔离

- Python 继续使用 `%APPDATA%/COMTool/config.json` 等现有路径。
- Avalonia Beta 使用独立目录，例如 `%APPDATA%/COMTool-Avalonia/config.json`。
- Avalonia 提供“从 COMTool 导入配置和会话”，默认只复制支持迁移的字段。
- 导入后两个版本各写各的文件，禁止共享可写配置。
- 同一 COM 口不能被两个版本同时打开；Avalonia 需要检测并给出与 Python 版本一致的错误提示。

### 4.2 数据模型

- 保持现有 `config.json` 的 `version`、`items`、`serialSessions`、`sshSessions`、`sessionSidebar` 等字段语义。
- 为 Avalonia 定义对应的 C# 模型和 JSON schema 测试。
- SSH 密码继续使用 Windows DPAPI 加密，只保存 `passwd_enc`，不保存明文。
- 非 Windows 平台保持现有的“不保存密码，重新提示”策略。
- 会话 key、端口归一化、排序规则必须与 Python 版本一致。

## 5. UI 一比一策略

### 5.1 UI 清单

以现有 Python 源码和 QSS 为准，至少覆盖：

- 自定义标题栏、Logo、窗口按钮；
- 左侧 `User sessions` 侧栏；
- 串口/SSH 分组、连接状态、描述、右键菜单；
- 标签页、`+` 插件选择器、标签标题/图标；
- terminal 主界面、滚动条、选择、复制、粘贴、右键菜单；
- 连接设置、串口参数、SSH 参数、记住密码；
- 设置面板、主题切换、语言切换；
- 功能栏、发送/接收、ZMODEM 进度和文件选择；
- Graph、protocol、debug 等插件界面；
- 帮助、关于、捐赠、更新提示；
- 状态栏、错误提示、确认对话框；
- 浅色/深色主题及 QSS 中的颜色、边距、圆角、字号。

### 5.2 视觉基准

Phase 0 开始建立 `docs/fixtures/ui/`：

- Windows 100% 和 150% DPI；
- 浅色/深色；
- 主窗口空状态；
- 侧栏展开/折叠；
- 串口连接中/已连接；
- SSH 已连接；
- 终端大段输出；
- 连接设置；
- Graph、protocol、debug 各一页；
- 对话框和右键菜单。

每张基准图记录：窗口尺寸、DPI、主题、语言、数据。

### 5.3 复刻方式

- 从 `assets/qss/style-light.qss`、`style-dark.qss` 提取颜色、字体、边距、圆角等 token，映射到 Avalonia `Styles` / `ResourceDictionary`。
- 图标直接复用 `COMTool/assets/` 下的 PNG/ICO/ICNS，或使用相同的 qtawesome 图标来源。
- 文案从现有翻译文件迁移，保持英文/简体中文/繁体中文/日文。
- 使用 Avalonia `Grid`/`DockPanel`/`SplitView` 复刻 Qt 的布局比例，不随意改成新式卡片布局。
- 先用静态 UI 对齐，再接真实数据和交互。

### 5.4 UI 验收

- 默认窗口尺寸下，控件位置和层级与基准图一致；
- 标签、按钮、提示文字一致；
- 颜色、图标、间距、圆角、字号一致；
- 主题、DPI、语言切换后仍一致；
- 所有交互路径与 Python 版本一致；
- Qt 字体栅格化差异需在验收记录中说明，不能作为布局偏差的理由。

## 6. 功能对齐

功能清单见 `docs/parity-matrix.md`。

优先级：

- P0：没有就不能切换；
- P1：切换前必须完成；
- P2：切换后短期补齐，但最终仍要达到一比一。

每次 Python 侧修复 bug，都要在 parity matrix 中确认 Avalonia 是否需要同步修复。

## 7. 阶段计划

### Phase 0：环境、骨架、UI 基准、可行性验证（1–2 周）

- 安装并固定 .NET SDK；
- 创建 `avalonia/` 解决方案和 CI；
- 建立 UI 基准截图、配置 fixtures、parity matrix；
- 复刻主窗口静态壳：标题栏、侧栏、标签区、空状态；
- 验证 Avalonia.Terminal、SSH.NET、System.IO.Ports；
- 验证 Windows/Linux 自包含发布；
- 输出 go/no-go 报告。

### Phase 1：一比一 UI 与基础连接（4–6 周）

- 完成主窗口、终端、串口、SSH、会话侧栏；
- 配置导入、会话持久化、标签页、设置；
- 与 Python 版本逐页对照；
- Windows 安装包。

### Phase 2：功能对齐（6–10 周）

- ZMODEM；
- TCP/UDP；
- 插件/协议/图形/调试；
- 国际化、分离窗口、自动重连、DPAPI 细节；
- Linux/macOS 打包。

### Phase 3：稳定化与切换（4–6 周）

- UI 截图对比、功能回归、硬件测试；
- 压力测试、长时间运行；
- Beta 试用；
- 切换默认版本和回滚演练。

按 1–2 人投入，预计 4–7 个月达到可切换状态；全功能 parity 的保守工作量为 8–14 人月。

## 8. 稳定标准

1. P0/P1 对齐项完成，无阻塞级 bug；
2. UI 基准图逐页通过，无未说明的布局/文案/交互差异；
3. 1MB/s 持续输出时 UI 不冻结，输入回显可接受；
4. 8 小时运行、反复重连、热插拔、ZMODEM 大文件无崩溃或明显泄漏；
5. Windows COM、Linux `/dev/ttyUSB*`、SSH 密码/密钥、lrzsz 验证通过；
6. Windows 自包含安装包、配置、日志、升级路径可用；
7. 至少 2 周小范围实际使用，无 P0/P1 回归；
8. Python 版本仍可下载、安装并读取旧配置。

## 9. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 双版本维护成本 | Python 功能冻结；parity matrix；固定切换窗口 |
| UI 复刻偏差 | 先做静态壳和基准图；逐页 review |
| 终端控件能力不足 | Phase 0 验证选择、滚动、颜色、ZMODEM 字节流 |
| ZMODEM 移植 | 优先移植现有实现和测试，不依赖未验证第三方库 |
| 插件不兼容 | 外部 Python 插件兼容性单独决策；先按不要求二进制兼容设计 |
| 配置冲突 | 独立目录；单向导入；禁止同时写同一文件 |
| 字体/DPI 差异 | 记录基准环境；使用相同字体和字号；按 DPI 验收 |
| 用户混淆 | Beta 命名、独立图标/安装标识、独立更新通道 |

## 10. 未决问题

- 外部 Python 插件是否必须兼容，还是只要求内置功能对齐。
- .NET 目标版本在 Phase 1 固定为当前 LTS，并在发布前确认生命周期。
- macOS 签名、公证和自动更新方案。
- 是否保留 Qt 版本的自动更新源作为回滚通道。

## 11. 参考

- Python 主窗口：`COMTool/main2.py`
- Python 控件：`COMTool/widgets.py`
- 终端：`COMTool/plugins/terminal.py`
- 主题：`COMTool/assets/qss/style-light.qss`、`style-dark.qss`
- 图标：`COMTool/assets/`
- 配置：`COMTool/parameters.py`、`COMTool/serial_sessions.py`、`COMTool/ssh_sessions.py`
- 功能对齐：`docs/parity-matrix.md`
