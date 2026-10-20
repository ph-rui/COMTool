# COMTool MobaXterm 会话类型选择器设计（第一阶段）

- 状态：已确认（2026-10-20 对话中选择“移除插件入口 + 会话类型选择器”）
- 日期：2026-10-20
- 目标：把 Avalonia 版的功能基准从 Python 插件模型逐步转向 MobaXterm 的会话类型模型
- 第一阶段范围：只改造 `+` 新建会话入口，不新增传输协议

## 1. 背景

当前 Avalonia 版的两个 `+` 入口照搬 Python 的插件列表：

- `Send Receive - dbg`
- `protocol - protocol`
- `terminal - terminal`
- `Graph - graph`
- `Load plugin from file`

用户明确不要这些插件功能，并给出 MobaXterm 的 `Session settings` 会话类型列表作为后续方向。MobaXterm 的完整类型包含 SSH、Telnet、Rsh、Xdmcp、RDP、VNC、FTP、SFTP、Serial、File、Shell、Browser、Mosh、AWS S3、WSL，但一次性全部实现不现实，因此按阶段对齐。

## 2. 目标与非目标

### 2.1 目标

- G1：删除两个 `+` 菜单中的未实现插件项，不再显示 `Send Receive`、`protocol`、`Graph`、`Load plugin from file`。
- G2：`+` 改为会话类型选择器，第一阶段提供 `Serial`、`SSH`、`TCP`、`Local terminal`。
- G3：选择 `Serial`/`SSH`/`TCP` 时打开现有连接设置窗口，并预选对应类型页。
- G4：选择 `Local terminal` 时直接创建本地终端标签，不再错误地打开连接设置。
- G5：空状态文案从“add a plugin”改为“add a session”，工具提示改为“New session”。
- G6：为菜单结构、类型预选和本地终端路由补充 headless 测试。

### 2.2 非目标

- N1：不实现 Telnet、Rsh、Xdmcp、RDP、VNC、FTP、SFTP、File、Browser、Mosh、AWS S3、WSL。
- N2：不引入 C# 插件 API，也不加载 Python 插件。
- N3：不改变 Serial/SSH/TCP 的连接行为、配置格式或侧栏结构。
- N4：不把连接设置窗口重构成 MobaXterm 的左右分栏布局；第一阶段只做入口和类型预选。
- N5：不改变 Python 版、启动器、配置目录和 `config.json`。

## 3. 交互设计

### 3.1 `+` 菜单

两个入口使用相同菜单：

1. `Serial`
2. `SSH`
3. `TCP`
4. `Local terminal`

- `Serial`/`SSH`/`TCP` 复用 `ConnectionSettingsWindow`，通过新增的初始类型参数预选 `ConnectionTypeCombo`。
- `Local terminal` 调用现有 `AddLocalTerminal()`。
- 后续 MobaXterm 类型在对应传输真正可用后再加入菜单，不使用长期禁用的占位项。

### 3.2 空状态

- 第一行保持 `Select a session from the left`。
- 第二行改为 `or click + to add a session`。
- 空状态 `+` 的 `ToolTip` 改为 `New session`。
- 有标签时工具栏 `+` 的 `ToolTip` 也改为 `New session`。

### 3.3 连接设置

- 新建会话时窗口标题为 `New session`。
- 现有会话点击设置按钮时，仍按当前标签类型显示并编辑原会话。
- `SessionKind.Local` 不进入连接设置窗口。

## 4. 测试

自动化测试位于 `avalonia/tests/COMTool.App.Tests/NewSessionMenuTests.cs`：

- `UI06_NewSessionMenuReplacesPluginEntries`：断言两个 `+` 菜单都只有四项、顺序和标签正确，且不再包含插件项。
- `UI06_NewSessionSettingsPreselectsRequestedTransport`：断言 Serial/SSH/TCP 初始类型预选到对应设置页，标题为 `New session`。
- `UI06_LocalTerminalEntryAddsLocalTab`：断言本地终端菜单项路由到本地标签，而不是连接设置。
- `UI07_EmptyStatePointsToNewSession`：断言空状态文案和两个入口的工具提示。

## 5. 后续对齐路线（未承诺顺序）

按“先能用、再扩展”的原则，候选阶段如下；每阶段开始前单独确认，不在本设计内实现：

1. 完成本阶段的会话类型选择器。
2. 评估 Telnet（可复用 TCP 基础）。
3. 评估 SFTP/FTP 文件会话。
4. 评估本地 Shell/WSL 会话。
5. 评估 RDP/VNC 等图形会话。
6. 其他 MobaXterm 类型按用户实际需求决定。

Python 版继续作为串口、终端和 ZMODEM 的行为参考；功能范围不再以 Python 插件列表为唯一基准。
