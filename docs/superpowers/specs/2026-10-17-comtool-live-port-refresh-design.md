# COMTool 串口列表热插拔实时刷新改造设计

- 状态：待评审
- 日期：2026-10-17
- 目标仓库：`https://github.com/ph-rui/COMTool`（`Neutree/COMTool` 的 fork）
- 上游基线：`8dbf97d`（master）
- 目标平台：Windows 10/11、Linux、macOS
- 许可证：LGPL-3.0（保持不变）

## 1. 背景

当前使用 MobaXterm 的 Serial 会话连接 Linux 开发板。MobaXterm 在“Session settings”对话框打开时只枚举一次 COM 口，热插拔后必须：

1. 关闭 Session settings；
2. 重新打开 Session settings；
3. 重新选择串口。

用户希望基于开源工具定制，让串口列表随设备插拔实时更新，同时保留多设备、多标签同时查看的能力。

选型结论：fork COMTool，做定向改造。理由：

- 原生支持 Windows / Linux / macOS / 树莓派，技术栈为 Python + PyQt5；
- 已有 `QTabWidget` 多标签，每个标签页是独立的连接实例，已支持断线重连；
- 串口下拉框在鼠标按下时已经会触发一次重新枚举，距离目标只差“弹窗期间持续刷新”；
- LGPL-3.0，允许修改和再分发；上游仍在维护。

## 2. 现状与问题

关键代码位置（上游 `8dbf97d`）：

- `COMTool/Combobox.py:15-21`：`mouseReleaseEvent()` 打开弹窗；`showPopup()` 被重写为空实现，真正的打开逻辑在 `_showPopup()`。
- `COMTool/conn/conn_serial.py:183`：`serialPortCombobox.clicked` 连接到 `detectSerialPort()`。
- `COMTool/conn/conn_serial.py:343-370`：`detectSerialPort()` / `detectSerialPortProcess()`，在后台线程中枚举串口。
- `COMTool/conn/conn_serial.py:373-388`：`showCombobox()`，清空并重建列表，按文本匹配当前端口。
- `COMTool/conn/conn_serial.py:408-410`：`findSerialPort()`，直接调用 `serial.tools.list_ports.comports()`。

已确认的问题：

| 编号 | 问题 | 影响 |
|---|---|---|
| P1 | 每次点击只扫描一次，弹窗打开后不再刷新 | 弹窗开着时插入设备看不到，必须收起再点 |
| P2 | 无串口时 `while 1` 永久循环；`isDetectSerialPort` 一直为 `True` | 没有设备时点击下拉框不弹列表，后续点击也失效；后台线程常驻 |
| P3 | 用 `currentText().split(" ")[0]` 反推设备名 | 设备名/描述含空格时解析错误；占位项会被误当成端口 |
| P4 | macOS 上同一个 USB 串口同时出现 `/dev/cu.*` 和 `/dev/tty.*` | 列表重复 |
| P5 | 按字符串排序 | `COM10` 排在 `COM2` 前面 |
| P6 | 自动更新指向上游 `Neutree/COMTool` 的 release | 定制版会被提示升级到上游版本，丢失改造 |

## 3. 目标与非目标

### 3.1 目标

- G1：串口下拉框展开期间，设备集合变化后 1 秒内更新列表。
- G2：没有串口时也能正常弹窗，显示占位提示；插入后自动替换为真实端口。
- G3：修掉 P2 的死循环和状态卡死。
- G4：列表显示设备路径、厂商/描述、VID:PID；`COM2` 排在 `COM10` 前。
- G5：刷新过程不改变用户已选端口、不干扰已打开的连接、不影响多标签。
- G6：Windows / Linux / macOS 行为一致，不引入平台专属热插拔 API。

### 3.2 非目标

- N1：不做“插入即自动连接”或“按 VID/PID 自动打开”。
- N2：不修改日志、HEX 收发、定时发送、文件传输、SSH、TCP/UDP、图表插件。
- N3：不重做 UI、主题或图标。
- N4：不改变断线重连策略（保留上游 `receiveDataProcess()` 中的重连逻辑）。
- N5：不新增安装器；打包继续使用上游 `pack.py`。
- N6：不向上游提交 PR（除非后续单独决定）。

## 4. 成功标准（验收条件）

- AC1：下拉框展开时插入 CH340/CP210x/FTDI 设备，系统枚举完成后 ≤1s 出现在列表。
- AC2：下拉框展开时拔出设备，≤1s 从列表消失；已选端口被拔出时连接状态由上游重连逻辑处理，列表不崩溃。
- AC3：启动时无任何串口，点击下拉框能正常弹出并显示“未检测到串口”占位项；插入设备后占位项被真实端口替换。
- AC4：连续快速点击下拉框 10 次，不产生线程堆积、不卡 UI、不出现重复项。
- AC5：两个标签页分别打开两个串口，插拔其中一个不影响另一个的列表和连接。
- AC6：已选 `COM5` 时刷新列表，选中项仍为 `COM5`；不会因为刷新自动跳到第一个端口。
- AC7：Windows 10/11、Ubuntu、macOS 至少各完成一次手工验证；单元测试全部通过。

## 5. 总体方案

采用**轮询**而不是平台热插拔事件：

- 点击下拉框时立即扫描一次（保留上游行为）；
- 弹窗可见期间，每 500ms 扫描一次；
- 扫描在后台线程执行，结果通过 Qt signal 回到 UI 线程；
- 只有设备集合发生变化时才重建列表，避免无谓的 UI 抖动。

不采用 `WM_DEVICECHANGE` / `udev` / `IOKit` 的原因：需要三套平台代码，收益仅是更低的延迟；500ms 轮询在弹窗打开时才运行，成本可忽略，且能覆盖所有平台。

新增状态（每个 `Serial` 实例独立）：

```python
_port_scan_in_flight: bool   # 单飞标志，防止并发扫描
_last_port_keys: Optional[Tuple]  # 上次渲染的设备集合
_popup_visible: bool         # 下拉框是否展开
_initial_scan_done: bool     # 是否完成过启动扫描
_port_refresh_timer: QTimer  # 500ms，parent 为当前标签页 widget
```

## 6. 组件设计

### 6.1 新增 `COMTool/conn/port_utils.py`

把与 Qt 无关的纯逻辑独立出来，便于测试。该模块只依赖标准库和 `pyserial` 的端口对象，**不导入 PyQt5**，因此单元测试不需要图形环境。

```python
@dataclass(frozen=True)
class PortInfo:
    device: str
    description: str = ""
    name: str = ""
    manufacturer: str = ""
    vid: Optional[int] = None
    pid: Optional[int] = None
    serial_number: Optional[str] = None
    hwid: str = ""

def from_pyserial(port) -> PortInfo
def normalize_ports(ports: List[PortInfo]) -> List[PortInfo]
def collect_ports(comports=None) -> List[PortInfo]
def display_text(port: PortInfo) -> str
def natural_key(text: str) -> Tuple
def port_keys(ports: List[PortInfo]) -> Tuple
def choose_index(ports: List[PortInfo], preferred_device: Optional[str]) -> int
```

规则：

- `from_pyserial()` 读取 `device/description/name/manufacturer/vid/pid/serial_number/hwid`，缺失字段用空值。
- `normalize_ports()`：
  - 过滤 `/dev/cu.Bluetooth-Incoming-Port` 和 `/dev/tty.Bluetooth-Incoming-Port`；
  - macOS 去重：若 `/dev/cu.X` 存在，则丢弃 `/dev/tty.X`；
  - 按 `natural_key(device)`、再按 `natural_key(label)` 排序。
- `display_text()` 输出：
  - 有 VID/PID：`COM5  CH344 USB-SERIAL  [1A86:55D4]`
  - 无 VID/PID：`/dev/ttyUSB0  CP2102 USB to UART`
  - label 优先取 `manufacturer`，否则 `description`，否则 `name`；
  - 去掉 Windows 描述里重复的 `(COM5)` 后缀，压缩连续空白；
  - 不截断，tooltip 显示完整 `hwid`、序列号、原始描述。
- `natural_key()` 用 `re.split(r"(\d+)", text)`，数字段转 `int`，实现 `COM2 < COM10`。
- `port_keys()` 返回 `(device, vid, pid, serial_number, description)` 的有序元组，用于判断集合是否变化。
- `choose_index()` 按 `device` 精确匹配；找不到返回 `-1`，不按文本模糊匹配。

### 6.2 `COMTool/Combobox.py`

新增两个信号，并跟踪真实可见状态：

```python
popupShown = pyqtSignal()
popupHidden = pyqtSignal()

def _showPopup(self):
    ...
    super(ComboBox, self).showPopup()
    self._popup_visible = True
    self.popupShown.emit()

def hidePopup(self):
    super(ComboBox, self).hidePopup()
    if self._popup_visible:
        self._popup_visible = False
        self.popupHidden.emit()
```

`showPopup()` 仍然保持空实现，避免扫描结果把弹窗强行打开。

### 6.3 `COMTool/conn/conn_serial.py`

替换 `detectSerialPort()` / `detectSerialPortProcess()` / `showCombobox()`：

```text
clicked ──────────────► requestPortScan()
popupShown ───────────► _popup_visible=True; requestPortScan(); timer.start()
popupHidden ──────────► _popup_visible=False; timer.stop()
timer.timeout ────────► requestPortScan()
requestPortScan() ────► 若 in_flight 则返回；否则起 daemon 线程
worker ───────────────► collect_ports(); emit(ports)
_onPortsScanned() ────► in_flight=False; 比较 keys; 需要时 _applyPorts()
```

模块级常量：

```python
PORT_REFRESH_INTERVAL_MS = 500
```

实现要点：

- `requestPortScan()` 不直接调用 `list_ports`，只负责单飞调度。
- worker 用 `try/except/finally`，任何异常都 `emit([])`，确保 UI 线程收到结果。
- `_onPortsScanned()` 第一行复位 `_port_scan_in_flight`，再做比较和渲染。
- `_last_port_keys` 初始为 `None`，强制第一次渲染；之后集合相同则直接返回。
- `_applyPorts()`：
  1. `blockSignals(True)`；
  2. `clear()`；
  3. 空列表：加入禁用占位项 `_("No serial port found")`，`currentIndex = -1`；
  4. 非空：逐项 `addItem(display_text(p))`，同时 `setItemData(index, p.device, Qt.UserRole)`，tooltip 写入完整信息；
  5. 用 `choose_index()` 恢复选中项；
  6. `blockSignals(False)`；
  7. 若弹窗仍可见但视图被 Qt 关闭，调用 `showItems()` 重开（平台 fallback）；
  8. 更新弹窗最小宽度，避免新增长描述被截断。
- 启动扫描（`onUiInitDone()`）：若配置端口存在则选中；若不存在且列表非空，按上游行为选中第一个并写入配置；若列表为空，只显示占位项。
- 实时扫描：**绝不**自动改选端口。当前配置端口消失时保持 `config["port"]` 不变，列表 `currentIndex = -1`；设备重新出现后自动恢复选中。
- `onSerialConfigChanged()` 的端口分支改用 `obj.currentData(Qt.UserRole)`，不再解析文本；刷新期间信号已阻塞，因此移除对 `isDetectSerialPort` 的依赖。
- `onDel()` 停止定时器；定时器 parent 为 `self.widget`，标签页关闭或拖出窗口时随 widget 销毁。
- `findSerialPort()` 保留，供断线重连的 `portExits()` 使用；不改变重连逻辑。

### 6.4 多标签

- 每个标签页创建独立的 `Serial` 实例，各自持有 `_port_refresh_timer`、`_last_port_keys`、`_port_scan_in_flight`。
- 不使用模块级全局状态。
- 标签页双击拖出为独立窗口时，widget 及其子对象（含定时器）一起迁移，刷新继续工作。
- 关闭标签页调用 `onDel()`，定时器停止，后台线程为 daemon，不会阻止退出。

## 7. 错误处理与边界

| 场景 | 行为 |
|---|---|
| `list_ports.comports()` 抛异常 | 记录日志，按空列表处理，UI 显示占位项；不冻结 |
| 扫描未完成时弹窗关闭 | 结果仍应用到 model，但不重新打开弹窗 |
| 扫描未完成时再次点击 | 单飞标志拦截，不重复起线程 |
| 设备在扫描过程中被拔出 | 下一次扫描结果会移除；`choose_index()` 找不到时 `-1` |
| 用户正在用键盘浏览列表时设备变化 | 仅在集合变化时重建；重建后按 `device` 恢复高亮；极端情况下弹窗关闭，由 fallback 重开 |
| 已连接时刷新列表 | 只更新 model，不调用 `com.open()/close()`，不改 `config["port"]` |
| widget 已销毁但结果到达 | Qt 自动断开 signal，槽不会执行；daemon 线程自行结束 |

新增日志使用项目现有的 `parameters.log`，不引入新日志框架。

## 8. 平台差异

- **Windows**：`comports()` 返回 `COMx`；自然排序处理 `COM10`；描述可能含 `(COMx)`，显示时去重；`COM10` 及以上由 pyserial 以 `\\.\COM10` 处理，无需额外代码。
- **Linux**：常见 `/dev/ttyUSB*`、`/dev/ttyACM*`；不做额外过滤。无权限时列表仍可见，打开失败沿用上游提示。
- **macOS**：优先 `/dev/cu.*`，去重 `/dev/tty.*`；过滤蓝牙伪串口；设备名可能较长，tooltip 保留完整信息。
- 三平台统一按 500ms 轮询，不写平台专属分支。

## 9. 自动更新与版本

`COMTool/autoUpdate.py` 当前指向：

```python
updateUrl = "https://github.com/Neutree/COMTool/releases"
releaseApiUrl = "https://api.github.com/repos/Neutree/COMTool/releases"
releaseApiUrl2 = "https://neucrack.com/comtool_update"
```

改造：

- 将 `updateUrl`、`releaseApiUrl` 指向 `ph-rui/COMTool`；
- `detectNewVersion()` 跳过 `checkUpdate_neucrack()`，只查 fork 的 releases；
- 版本号保持上游基线 `3.4.1`，不伪装成新上游版本；
- 在 fork 发布自己的 release 前，检查结果为空，不会弹升级提示。

## 10. 测试计划

### 10.1 单元测试（新增 `tests/test_port_utils.py`）

只依赖标准库和 `port_utils`，不需要 GUI；测试文件通过 `sys.path` 把仓库根目录加入导入路径：

| 用例 | 断言 |
|---|---|
| 无 VID/PID | 显示 `device + label`，不出现空方括号 |
| 有 VID/PID | 显示 `[1A86:55D4]` |
| Windows 描述 `USB-SERIAL CH340 (COM5)` | 不重复显示 `(COM5)` |
| `COM2` / `COM10` | `COM2` 在前 |
| macOS `cu.usbserial-1` + `tty.usbserial-1` | 只保留 `cu` |
| 蓝牙伪串口 | 被过滤 |
| 空列表 | `port_keys([]) == ()`，`choose_index([], None) == -1` |
| 首选端口不存在 | 返回 `-1` |
| 设备集合变化 | `port_keys` 不相等；仅描述变化也能检测 |

运行方式：

```bash
python -m pytest tests -q
```

新增 `.github/workflows/test.yml`：在 Ubuntu 上安装 `pytest` 后运行单元测试（纯逻辑测试不需要 PyQt5）。

### 10.2 手工集成测试

| 平台 | 设备 | 步骤 |
|---|---|---|
| Windows 10/11 | CH340、CP210x、FTDI | 弹窗打开时插拔；无设备时点击；多标签各接一个 |
| Ubuntu 22.04/24.04 | `/dev/ttyUSB0`、`/dev/ttyACM0` | 插拔、权限不足、断线重连 |
| macOS (arm64/x64) | `cu.usbserial-*` | 插拔、cu/tty 去重、蓝牙端口过滤 |

Linux/macOS 可用虚拟串口辅助测试：

```bash
socat -d -d pty,raw,echo=0 pty,raw,echo=0
```

重点回归：

- 上游原有的打开/关闭、波特率、RTS/DTR、断线重连；
- 两个标签页互不影响；
- 关闭标签页后进程能正常退出。

## 11. 打包与分发

- 依赖不变，继续使用 `requirements.txt`。
- 使用上游 `pack.py`（PyInstaller）：
  - Windows：`python pack.py` 生成 `comtool_windows_v3.4.1.7z`；
  - Linux：生成 `comtool_ubuntu_v3.4.1.tar.xz`；
  - macOS：生成 `.app` / `.dmg`（未签名，首次打开需右键打开）。
- 打包产物不提交到 git；通过 fork 的 GitHub Releases 分发。
- 源码运行：
  ```bash
  python -m pip install -r requirements.txt
  python COMTool/Main.py
  ```

## 12. 许可证、署名与上游同步

- 保留 `LICENSE`（LGPL-3.0）和上游版权声明，不改变许可证。
- 新增 `NOTICE`：
  - 说明基于 `Neutree/COMTool`；
  - 列出本 fork 的修改点；
  - 保留上游作者与仓库链接。
- fork 仓库公开，满足 LGPL 对对应源码的要求；分发二进制时在 Release 中附源码链接。
- 保留 `upstream` remote，后续可 `git fetch upstream && git rebase upstream/master` 同步。
- 修改集中在独立文件和小范围函数，降低上游合并冲突。

## 13. 风险与缓解

| 风险 | 缓解 |
|---|---|
| Qt 在弹窗打开时更新 model 可能自动关闭弹窗 | 上游现有代码已在弹窗打开时 `clear()/addItem()`；若个别平台仍关闭，检测视图不可见并调用 `showItems()` 重开 |
| 500ms 轮询带来 CPU 开销 | 仅在弹窗可见时运行；`list_ports` 是轻量枚举；可调整常量 `PORT_REFRESH_INTERVAL_MS` |
| 扫描线程与重连线程同时枚举 | 两者只读枚举，无共享可变状态；单飞标志限制本组件的扫描 |
| 刷新重置用户选择 | 重建时阻塞信号，按 `device` 精确恢复；实时刷新不自动改选 |
| macOS 打包/签名问题 | 沿用上游 CI 的 macOS 构建方式；自用可右键打开 |
| 自动更新覆盖定制版 | 已改为指向 fork，且无 release 时不提示 |
| 新增占位文案缺少翻译 | 使用 `_()`，同步更新 `messages.pot` 与 `zh_CN`/`zh_TW` 的 `.po`；`.mo` 由项目启动时自动生成 |

## 14. 交付物

1. 分支 `feature/live-port-refresh`，推送到 `origin`（`ph-rui/COMTool`）。
2. 代码：
   - `COMTool/Combobox.py`
   - `COMTool/conn/conn_serial.py`
   - `COMTool/conn/port_utils.py`（新增）
   - `COMTool/autoUpdate.py`
   - `COMTool/locales/messages.pot`、`zh_CN`/`zh_TW` 的 `.po`
3. 测试：`tests/test_port_utils.py`、`.github/workflows/test.yml`。
4. 文档：本设计文档、`NOTICE`、README 增加 fork 说明与运行/打包命令。
5. 提交记录按“文档 → 实现 → 测试”拆分，便于评审。

## 15. 实施顺序

1. 本设计文档提交并推送。
2. 新增 `port_utils.py` 与单元测试，先跑通纯逻辑。
3. 修改 `Combobox.py` 增加弹窗信号。
4. 重写 `conn_serial.py` 的枚举与刷新逻辑。
5. 调整自动更新地址与占位文案翻译。
6. 手工矩阵验证，修正平台差异。
7. 推送实现分支，附验证记录。

## 16. 明确不做

- 自动连接/自动重连开串口（仅保留上游断线重连）。
- 串口数据日志格式、HEX、定时发送、快捷指令的改动。
- SSH/SFTP、TCP/UDP、插件系统、图表插件的改动。
- UI 主题、布局、图标重设计。
- 安装器、自动更新下载器、代码签名。
