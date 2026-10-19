# COMTool 串口会话记忆设计

- 状态：待评审
- 日期：2026-10-19
- 目标仓库：`https://github.com/ph-rui/COMTool`（`Neutree/COMTool` 的 fork）
- 当前基线：`d145b05`（`feature/live-port-refresh`）
- 目标平台：Windows 10/11、Linux、macOS
- 许可证：LGPL-3.0（保持不变）

## 1. 背景

COMTool 现在的配置会保存“上次打开的标签”，启动时自动恢复。这个行为适合恢复现场，但不像 MobaXterm 的 `User sessions`：

- `User sessions` 是独立的保存列表，关闭会话不会删除记录；
- 串口会话可以一键打开并自动连接；
- 设备管理器里已经不存在的 COM 口应从会话记忆里自动删除。

目标是把 COMTool 的 terminal 串口使用方式调整为：左侧一个可折叠的串口会话列表，启动时默认不打开任何标签，点击会话后创建 terminal 标签并自动连接。

## 2. 目标与非目标

### 2.1 目标

- G1：主窗口左侧增加可折叠的 `User sessions` 侧栏。
- G2：第一版只保存串口会话（Windows `COMx`，Linux/macOS `/dev/tty*`、`/dev/cu.*`）。
- G3：关闭标签不删除会话；会话独立于标签存在。
- G4：点击会话打开 terminal 标签，套用保存的串口参数并自动连接。
- G5：成功使用过的串口自动保存/更新参数，按端口去重。
- G6：串口扫描成功后，设备列表里不存在的端口自动从会话列表删除。
- G7：启动时不恢复任何标签，只显示侧栏；`+` 菜单仍可添加 terminal、Graph 等插件。
- G8：侧栏宽度、折叠状态和会话列表跨重启保存。

### 2.2 非目标

- N1：不做 SSH/TCP 会话；留到后续版本。
- N2：不做会话重命名、分组、搜索、导入/导出。
- N3：不自动打开所有存在的串口，也不在启动时自动恢复标签。
- N4：不改变 terminal 的 ZMODEM、断线重连、字体、编码等现有行为。
- N5：不删除或迁移非 terminal 插件的历史配置数据；只是启动时不再加载这些标签。
- N6：不引入平台专属设备通知 API；使用跨平台轮询。

## 3. 验收条件

- AC1：启动后没有自动打开的标签；左侧显示保存的串口会话；点击 `+` 仍能添加插件。
- AC2：点击 `COM99` 会话，创建 terminal 标签，串口参数正确，并自动打开端口；成功后标签和侧栏显示已连接。
- AC3：关闭该 terminal 标签，`COM99` 仍在侧栏；再次点击可重新打开。
- AC4：同一条会话被点击时，如果对应端口的 terminal 标签已经存在，则切换到已有标签，不创建第二个占用同一端口的标签。
- AC5：修改波特率等参数并断开/退出后，会话里保存的是最后一次使用的参数。
- AC6：拔掉 CH344 后，下一次成功扫描将对应 COM 会话从侧栏删除，并给出简短提示。
- AC7：扫描函数抛异常时，不删除任何会话。
- AC8：侧栏折叠/展开、宽度在重启后保持。
- AC9：Windows 实机完成上述验证；单元测试全部通过。

## 4. 用户界面

### 4.1 主窗口布局

- 在现有内容区增加水平 `QSplitter`：
  - 左侧：`SessionSidebar`
  - 右侧：现有 `tabWidget` 及设置面板
- 侧栏默认展开，宽度约 200px，可拖动调整；宽度范围约 160–320px。
- 标题栏增加一个始终可见的 `Sessions` 切换按钮；侧栏标题栏也提供折叠按钮。这样即使当前没有任何标签，也能恢复被折叠的侧栏。
- 折叠后侧栏完全隐藏，通过标题栏 `Sessions` 按钮恢复，避免留下无法恢复的零宽度区域。
- 折叠状态和宽度写入配置；重启后恢复。

### 4.2 侧栏内容

- 标题：`User sessions`
- 每个串口会话显示：
  - 状态圆点/图标：已连接绿色，未连接灰色
  - 端口：`COM99` 或 `/dev/ttyUSB0`
  - 端口描述（如果系统提供）：例如 `USB-Enhanced-SERIAL-A CH344`
- 按端口自然排序，`COM2` 在 `COM10` 前。
- 当前标签对应的会话高亮。
- 列表为空时显示 `No saved serial sessions`。
- 右键菜单：`Open`、`Remove session`。删除前确认；删除只影响会话记忆，不关闭已经打开的标签。
- 自动删除失效会话时，通过状态栏或侧栏短暂提示删除了哪些端口，不使用阻塞对话框。

### 4.3 启动与空状态

- 启动时不调用现有的标签恢复逻辑，`items` 不再决定启动时打开哪些标签。
- 主区域为空时显示：
  - `Select a session from the left`
  - `or click + to add a plugin`
  - 一个 `Add plugin` 按钮，使用与 `+` 相同的插件选择逻辑，避免没有标签时找不到添加入口。
- `+` 菜单继续支持 terminal、Send Receive、protocol、Graph 等插件。
- 支持零标签状态：
  - 关闭最后一个标签后进入空状态，而不是禁止关闭。
  - 没有当前标签时，设置面板按钮禁用；键盘事件直接交给窗口处理，不能访问空的 `getCurrentItem()`。
  - 侧栏、标题栏、`Add plugin` 入口在零标签时仍然可见。

### 4.4 点击会话

- 按端口查找已经打开的 terminal 标签：
  - 已存在且已连接：切换到该标签。
  - 已存在但未连接：切换到该标签并尝试打开端口。
  - 不存在：创建 terminal 标签，传入保存的串口参数，切换到新标签并自动连接。
- 自动连接失败（端口被占用、设备刚被拔出）时：
  - 显示现有错误提示；
  - 标签保留；
  - 不删除会话（除非下一次扫描确认端口不存在）。
- 同一端口不允许同时存在两个已打开的 terminal 连接；通过“复用已有标签”保证。

## 5. 数据模型

配置继续使用当前 `config.json`（`version` 仍为 3），新增键：

```json
{
  "serialSessions": [
    {
      "port": "COM99",
      "name": "COM99  USB-Enhanced-SERIAL-A CH344",
      "serial": {
        "port": "COM99",
        "baudrate": 115200,
        "bytesize": 8,
        "parity": "None",
        "stopbits": "1",
        "flowcontrol": "None",
        "rts": false,
        "dtr": false
      },
      "updated": 1760000000
    }
  ],
  "sessionSidebar": {
    "visible": true,
    "width": 200
  },
  "serialSessionsMigrated": true
}
```

规则：

- `port` 是唯一键；比较时使用大小写不敏感归一化，显示时保留系统返回的原始写法。
- `serial` 保存最后一次成功使用的串口参数；`serial.port` 与顶层 `port` 保持一致。
- `name` 保存最后一次扫描到的显示名，供设备暂时不可见时仍能显示；设备可见时用最新描述刷新。
- `updated` 用于排序或后续诊断；第一版列表仍按端口自然排序。
- 不保存易变的连接状态、接收内容或 ZMODEM 临时路径。

## 6. 组件与代码结构

### 6.1 `COMTool/serial_sessions.py`（新增，纯逻辑）

不导入 Qt，便于单元测试：

- `SerialSessionStore`：
  - `load(config)`、`save(config)`
  - `upsert(port, serial_config, name)`：按归一化端口新增或覆盖参数
  - `remove(port)`
  - `prune(available_ports)`：返回被删除的端口列表
  - `get(port)`
  - `sorted_sessions()`
- `session_name(port, description)`：生成 `COM99  USB-Enhanced-SERIAL-A CH344`
- `migrate_terminal_items(items)`：从旧 `config["items"]` 中提取 terminal + serial 配置，返回会话字典
- 复用 `conn/port_utils.py` 的 `natural_key`、`display_label`。

### 6.2 `COMTool/session_sidebar.py`（新增，Qt 组件）

- `SessionSidebar(QWidget)`：
  - 标题、计数、折叠按钮
  - `QListWidget` 会话列表
  - 信号：`sessionActivated(str)`、`removeRequested(str)`、`collapseRequested()`
  - `set_sessions(sessions, connected_ports, active_port)`
  - `set_port_status(port, connected)`
  - 右键菜单
- 不直接访问串口或配置；由 `MainWindow` 注入数据和处理动作。

### 6.3 `COMTool/session_watcher.py`（新增，Qt 组件）

- `SerialPortWatcher(QObject)`：
  - `QTimer` 默认 2000ms
  - 后台线程调用 `collect_ports()`，同一时间只允许一个扫描
  - 信号 `portsScanned(list, bool)`；第二个参数表示扫描是否成功
  - 启动时立即触发一次
  - 退出时停止定时器；线程为 daemon
- 只有 `success=True` 时才允许清理会话。

### 6.4 `COMTool/main2.py`（修改）

- 构建侧栏和水平 splitter；保存/恢复侧栏状态。
- 启动流程改为：
  1. 加载插件信息；
  2. 迁移旧 terminal 会话（只执行一次）；
  3. 初始化会话 store 和 watcher；
  4. 不加载旧 `items`；
  5. 显示空状态。
- 会话点击处理：
  - 查找已有 terminal 标签；
  - 必要时调用 `addItem(terminal, connsConfigs=..., pluginConfig=...)`；
  - 自动连接。
- 连接状态变化时更新侧栏状态。
- 关闭标签时先保存会话，再销毁连接；允许关闭最后一个标签并切换到空状态。
- 退出时采集所有成功连接过的 terminal 标签的最新串口参数，写入 store；只写入最后一次成功扫描确认存在的端口，避免失效设备被已打开的标签重新写回。
- 端口扫描成功时：
  - `store.prune(ports)`；
  - 更新会话描述；
  - 保存配置、刷新侧栏。
- 侧栏删除请求：确认后删除 store 项；不关闭已经打开的标签。被手动删除的端口在本轮运行内不会被退出采集重新写回；用户之后再次成功连接该端口时重新创建会话。
- 零标签时 `toggleSettings`、`keyPressEvent`、`keyReleaseEvent`、`onTabDoubleClicked` 等入口必须处理 `getCurrentItem()` 为 `None` 的情况。

### 6.5 `COMTool/conn/conn_serial.py`（修改）

- 增加显式的 `open()` 入口，供“点击会话自动连接”调用，不依赖按钮文本。
- 打开前确保 `self.com.port` 来自加载的配置；端口为空或设备不存在时走现有错误处理。
- 不改变现有 `openCloseSerial()` 的用户点击行为。

### 6.6 `COMTool/pluginItems.py`（修改）

- 提供一个最小接口，让 `MainWindow` 能：
  - 获取当前连接是否为 Serial；
  - 获取当前 Serial 配置；
  - 请求自动打开；
  - 知道该 terminal 标签是否成功连接过。
- 不把会话逻辑放进插件，避免所有插件承担串口会话概念。

## 7. 会话生命周期

### 7.1 创建/更新

- terminal 第一次成功连接串口（`ConnectionStatus.CONNECTED`）时：
  - 以当前端口为键 `upsert`；
  - 保存当前串口参数和描述。
- 之后修改端口/波特率/数据位/校验/停止位/流控/RTS/DTR：
  - 断开时、关闭标签时、退出程序时重新采集并更新。
- 更新已有会话前检查端口仍在最后一次成功扫描的端口集合中；端口已失效时不写回。
- 连接失败不创建新会话；已经保存的会话保持不变。
- 新建 terminal 标签但从未成功连接，不产生会话。
- 手动删除会话后，如果对应标签仍打开，本轮运行内不把该端口重新写回；用户再次成功连接该端口后解除抑制并重新创建。

### 7.2 删除

- 自动删除：成功扫描后，端口不在 `collect_ports()` 结果中的会话。
- 自动删除会立即保存配置，避免异常退出后残留。
- 手动删除：右键 `Remove session`，确认后删除；如果对应标签仍打开，标签不受影响，但该端口在本轮运行内不会再被退出采集写回。
- 关闭标签不删除会话。

### 7.3 失效设备与已打开标签

- 如果端口在扫描中消失：
  - 会话从侧栏删除；
  - 已经打开的标签保留，由现有断线逻辑处理；
  - 设备重新插入不会从旧 `items` 自动恢复会话；如果仍有标签重新连接成功，则按正常连接规则重新创建会话。

## 8. 迁移与兼容

- 首次运行新版本时，如果 `serialSessionsMigrated` 不存在：
  - 遍历 `config["items"]`；
  - 对 `pluginId == "terminal"` 且配置中有 Serial 端口的项，提取为会话；
  - 保留原 `items` 数据，但启动时不再加载；
  - 设置 `serialSessionsMigrated = true`。
- 迁移只执行一次。之后即使用户手动删除会话，旧 `items` 也不会把它重新加回来。
- 现有非 terminal 标签的配置仍然保留在 `items` 中，但启动时不恢复；用户可通过 `+` 重新添加。
- 不提升配置 `version`；旧版本配置缺少新键时按默认值处理。

## 9. 并发与错误处理

- 串口扫描在后台线程执行；Qt 信号回主线程修改 UI 和配置。
- 扫描抛异常：
  - 记录日志；
  - `success=False`；
  - 不删除会话，不刷新设备描述；
  - 保留侧栏现状。
- 扫描成功但结果为空：
  - 按规则删除所有串口会话；
  - 侧栏显示空状态和删除提示。
- 自动连接与扫描可能同时发生：
  - 会话点击使用保存的端口直接打开，不等待 watcher；
  - watcher 只在成功后清理/刷新，不参与打开流程。
- 侧栏更新只操作主线程对象；后台线程不直接调用 Qt 控件。

## 10. 测试计划

### 10.1 单元测试

- `SerialSessionStore`：
  - 新增、更新同一端口不重复；
  - 端口大小写归一化；
  - `prune` 只删除缺失端口，返回删除列表；
  - 自然排序；
  - 迁移提取 terminal + serial 项，跳过非 terminal/非 serial。
- `session_name`：
  - 有描述、无描述、描述中包含 `(COMx)` 的情况。
- watcher 的成功/失败分支：失败时不调用 `prune`（通过注入扫描函数测试）。

### 10.2 界面测试（offscreen）

- 侧栏渲染会话列表、空状态。
- 点击会话发出 `sessionActivated(port)`。
- 右键删除发出 `removeRequested(port)`。
- 折叠/展开信号和状态。
- 零标签状态：关闭最后一个标签、空状态显示、设置按钮禁用、键盘事件不崩溃。

### 10.3 Windows 实机验收

- 启动无标签，侧栏显示迁移出的 COM99。
- 点击 COM99 自动连接，终端可用。
- 关闭标签后会话仍在，重启后仍在。
- 修改波特率后断开/退出，重新打开会话参数正确。
- 拔掉 CH344，侧栏会话自动消失；重新插入不自动恢复旧会话。
- 扫描异常模拟（临时让 `collect_ports` 抛异常）不删除会话。
- 侧栏折叠、宽度在重启后保持。

## 11. 风险与取舍

- 轮询间隔 2 秒意味着拔出设备后最多约 2 秒才删除会话；这是可接受的延迟，换来了跨平台一致性。
- 按 COM 口而不是 USB 序列号记忆：同一设备换到另一个 COM 口会被视为新会话。当前需求以 COM 口为单位，第一版保持简单。
- 启动不恢复标签会改变现有使用习惯；这是用户明确选择的行为。
- 自动删除“当前没有串口”意味着拔掉 USB 集线器/开发板后会话会被清空；这是需求明确要求的行为，设计不额外做“暂缓删除”机制。

## 12. 暂不实现

- SSH/TCP 会话
- 会话重命名、分组、搜索、导入/导出
- 设备序列号级别的会话跟随
- 启动自动恢复标签或自动打开全部会话
- 系统级设备热插拔事件
