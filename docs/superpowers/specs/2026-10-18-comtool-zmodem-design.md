# COMTool 串口 ZMODEM 文件传输设计

- 状态：待评审
- 日期：2026-10-18
- 目标仓库：`https://github.com/ph-rui/COMTool`（`Neutree/COMTool` 的 fork）
- 当前基线：`b6d1520`（`feature/live-port-refresh`）
- 目标平台：Windows 10/11、Linux、macOS
- 许可证：LGPL-3.0（保持不变）
- 运行时依赖：仅 Python 标准库和项目现有依赖，不引入 `lrzsz` 或其他外部程序

## 1. 背景与目标

COMTool 已作为串口控制台使用，terminal 标签页支持按键直通和 ANSI 终端渲染。现场 Linux 设备上通常使用 `sz` 把文件发到电脑、使用 `rz` 从电脑接收文件，两者都基于 ZMODEM 协议。

目标是在 terminal 标签页内实现自动文件传输：

1. Linux 执行 `sz file` 时，COMTool 自动识别并接收文件。
2. Linux 执行 `rz` 时，COMTool 自动识别并弹出文件选择框，把文件发送给 Linux。
3. 传输期间二进制数据不能进入终端屏幕渲染，不能破坏当前终端内容。
4. 传输结束或取消后，终端和串口连接恢复正常。
5. 支持 Windows、Linux、macOS 主机；串口仍是唯一传输通道。

## 2. 范围

### 2.1 包含

- ZMODEM 协议收发，兼容 Linux `lrzsz` 的 `rz` / `sz`。
- 自动识别远端 `sz`（ZRQINIT）和远端 `rz`（ZRINIT）。
- 单文件和一次多文件传输。
- 16 位和 32 位 CRC、ZDLE 转义、ZCAN 中止、ZRPOS 重传。
- 接收文件先落临时目录，会话结束后逐个“另存为”。
- 发送时一次选择多个文件。
- 终端状态栏显示进度和取消按钮。
- 传输期间锁定键盘输入，结束后恢复。
- 超时、串口断开、写盘失败、标签页关闭时的清理。

### 2.2 不包含

- XMODEM / YMODEM。
- 跨会话断点续传、压缩、加密。
- SSH/SFTP、日志、HEX 发送、定时发送等额外功能。
- 修改 Linux 端或要求 Linux 端安装额外程序。
- 在终端中显示二进制传输内容。

## 3. 用户流程

### 3.1 接收文件（Linux 执行 `sz`）

1. 用户在 Linux 终端输入 `sz file.bin`。
2. Linux 的 `sz` 输出 ZMODEM ZRQINIT。
3. COMTool 的 detector 校验头部 CRC，确认是 ZRQINIT。
4. COMTool 立即启动纯 Python 接收引擎，把后续串口数据分流到引擎，不再喂给 `pyte`。
5. 引擎逐个接收文件，写入私有临时目录，并更新状态栏进度。
6. ZMODEM 会话结束后，COMTool 按接收顺序为每个文件弹出“另存为”。
7. 用户选择路径后文件从临时目录移动过去；取消某个文件只丢弃该文件。
8. 所有文件处理完后清理临时目录，终端恢复输入。

### 3.2 发送文件（Linux 执行 `rz`）

1. 用户在 Linux 终端输入 `rz`。
2. Linux 的 `rz` 输出 ZMODEM ZRINIT。
3. COMTool 的 detector 校验头部 CRC，确认是 ZRINIT。
4. COMTool 在界面线程弹出多文件选择框，同时进入 pending-send 状态。
5. 用户选择文件后启动发送引擎；选择框打开期间只保留最新的重复 ZRINIT，避免重复握手。
6. 引擎按顺序发送文件，状态栏显示当前文件和进度。
7. 完成后发送 ZFIN/结束序列，终端恢复输入。
8. 用户取消选择框时发送 ZCAN，让远端 `rz` 退出。

### 3.3 取消与异常

- 用户点“取消”：发送 ZCAN 中止序列，终止引擎线程，删除临时文件，恢复终端。
- 远端发送 ZCAN 或按 Ctrl+C：引擎识别中止，清理并恢复终端。
- 串口断开、标签页关闭、应用退出：终止引擎，清理临时文件。
- 协议超时、CRC 错误、帧错误、写盘失败：显示错误并执行中止流程。
- 普通终端输出中出现不完整或 CRC 不匹配的 `**\x18` 字节：detector 回退，内容原样交给终端。

## 4. 架构

### 4.1 模块划分

新增 `COMTool/zmodem/` 包，保持与 Qt 解耦，便于单元测试和集成测试：

- `crc.py`：CRC16 和 ZMODEM CRC32 查表实现。
- `codec.py`：ZMODEM 头、ZDLE 转义、数据子包、常量。
- `detector.py`：终端字节流中的 ZMODEM 头识别。
- `io.py`：线程安全的字节队列和 `ZmodemIO` 接口。
- `filename.py`：文件名解码、清洗、唯一临时名生成。
- `receiver.py`：接收状态机。
- `sender.py`：发送状态机。
- `session.py`：线程、超时、取消、临时目录和进度回调的编排。
- `__init__.py`：对外导出。

`COMTool/plugins/terminal.py` 负责 Qt 集成：detector、分流、对话框、状态栏、输入锁定和生命周期。

### 4.2 关键接口

`ZmodemIO`：

```python
class ZmodemIO:
    def feed(self, data: bytes) -> None: ...
    def read(self, size: int, timeout: float) -> bytes: ...
    def write(self, data: bytes) -> None: ...
    def cancel(self) -> None: ...
```

- `feed` 由串口接收线程调用，写入线程安全队列。
- `read` 在引擎线程阻塞读取，必须读满 `size` 字节；超时或取消时抛出 `ZmodemTimeout`/`ZmodemAbort`。
- `write` 调用 terminal 插件已有的 `send`，不直接操作串口。
- `cancel` 唤醒阻塞读取并让引擎退出。

detector 输出：

```python
@dataclass
class Detection:
    direction: Literal["receive", "send"]
    header: bytes
    remainder: bytes

@dataclass
class DetectionResult:
    terminal_data: bytes
    detection: Detection | None
```

- `terminal_data` 是确认不属于 ZMODEM 的字节，按原顺序交给 `pyte`。
- `detection.direction` 以 COMTool 为主语：ZRQINIT 表示远端要发送，方向为 `receive`；ZRINIT 表示远端要接收，方向为 `send`。
- detector 内部保留最多一个最大 ZMODEM 头长度的尾缓冲，处理跨 chunk 的头。
- 只在头部 CRC 校验通过且帧类型为 ZRQINIT/ZRINIT 时触发；否则回退。

session 回调：

```python
on_file_start(name: str, size: int | None, temp_path: str)
on_progress(name: str, transferred: int, total: int | None)
on_file_complete(name: str, temp_path: str)
on_error(message: str)
on_finished(received_files: list[ReceivedFile])
```

### 4.3 线程与数据流

1. 串口接收线程调用 `Plugin.onReceived(data)`。
2. 空闲时数据先进入 detector：
   - 普通字节交给 `Scroll_Terminal.onReceived`，继续由 `pyte` 渲染。
   - 检测到 ZMODEM 头时切换到传输状态。
3. 传输状态下，所有串口字节只进入 `ZmodemIO.feed`，不再进入 `pyte`。
4. ZMODEM 引擎在独立 daemon 线程运行，通过 `ZmodemIO.write` 把协议数据交给现有发送队列。
5. 引擎通过 Qt signal 把进度、文件完成、错误发给界面线程；界面线程负责所有对话框和控件更新。
6. 传输结束后引擎标记 inactive；之后到达的串口字节重新交给 terminal。

发送选择框使用 pending-send 状态：detector 已消费第一个 ZRINIT，界面线程弹出选择框，串口接收线程继续缓存输入但不喂终端；选择完成后把最新 ZRINIT 交给发送引擎，开始握手。

### 4.4 协议实现范围

接收和发送都实现以下 ZMODEM 子集，目标是和 `lrzsz 0.12.20/0.12.21` 互通：

- 头类型：ZRQINIT、ZRINIT、ZFILE、ZSKIP、ZNAK、ZABORT、ZFIN、ZRPOS、ZDATA、ZEOF、ZCRC、ZCAN，以及必要的 ZACK。
- 头编码：ZHEX、ZBIN、ZBIN32；接受 `CR LF`、`CR LF XON` 以及 lrzsz 常见的 `0x8a/0x8d` 变体。
- 数据子包：ZCRCG、ZCRCQ、ZCRCE、ZCRCW；单包最大 8192 字节。
- CRC：16 位和 32 位；接收方在 ZRINIT 中声明支持 32 位 CRC。
- ZDLE 转义：按协议转义控制字符和 ZDLE 自身；处理 ZRUB0/ZRUB1。
- 多文件：发送方逐个发送 ZFILE、ZDATA、ZEOF；接收方逐个响应 ZRPOS/ZRINIT。
- 重传：收到 ZRPOS 时从指定偏移重发；收到 ZNAK 时重发当前帧；支持 ZSKIP。
- 结束：ZFIN 后完成 `OO` 交换。
- 中止：发送 8 个 CAN 加 8 个 BS；识别连续 CAN。
- 超时：等待头 30 秒，数据不活动 30 秒；超时执行中止。
- 接收文件流式写入磁盘，不把整个文件放在内存。

不实现跨会话恢复、文件压缩、ZMODEM 的 ZCHALLENGE/ZCOMPL 扩展。

## 5. 界面与交互

### 5.1 状态栏

复用 `Plugin_Base` 已创建的 `statusBar`，在布局右侧增加：

- 传输状态标签：文件名或“第 i/n 个文件”。
- `QProgressBar`：总大小未知时使用不确定进度；已知时显示百分比。
- “取消”按钮：仅传输期间可见。

空闲时全部隐藏，不改变现有终端布局；连接状态文本继续使用原有标签。

### 5.2 输入锁定

- `Terminal_Frontend` 增加 `setTransferActive(bool)`。
- 传输期间 `keyPressEvent`、粘贴和输入法事件直接忽略；鼠标选中、复制、滚动仍可用。
- 传输结束、取消或错误后恢复输入并把焦点还给终端。

### 5.3 接收保存

- 临时目录使用 `tempfile.mkdtemp(prefix="comtool-zmodem-")`。
- 会话结束后按接收顺序逐个调用 `QFileDialog.getSaveFileName`。
- 默认目录使用插件配置中的 `zmodem_last_dir`，首次为空时使用系统默认目录。
- 默认文件名使用远端文件名（已清洗）；同名文件由对话框确认覆盖，不静默覆盖。
- 用户取消单个文件：删除该临时文件，继续下一个。
- 所有文件处理完后删除临时目录。
- 移动失败时保留临时文件，并在错误提示中显示临时路径。

### 5.4 发送选择

- 检测到 ZRINIT 后调用 `QFileDialog.getOpenFileNames`。
- 默认目录同样使用 `zmodem_last_dir`，选择后更新配置。
- 用户取消：发送 ZCAN，恢复终端。
- 选择多个文件时按选择顺序发送。

## 6. 文件处理与安全

- ZFILE 元数据格式：`filename\0size mtime mode serial`；缺少 size 时按未知总大小处理。
- 文件名按 UTF-8 解码，失败使用 `surrogateescape`；只保留 basename。
- 过滤 `/`、`\`、Windows 非法字符、控制字符、`.` 和 `..`；结果为空时使用 `download`。
- 所有接收文件只能写入临时目录；最终移动使用用户选择的目标路径。
- 临时文件使用唯一名称，避免多文件同名互相覆盖。
- 文件大小、mtime 仅用于显示和保存，不执行远端给出的路径或权限。

## 7. 异常处理与超时

- 头部等待超时：发送 ZCAN，清理，提示“ZMODEM 握手超时”。
- 数据不活动超时：发送 ZCAN，清理，提示“传输超时”。
- CRC/帧错误：按协议请求重传；同一位置连续 5 次失败后中止。
- 远端 ZCAN：立即中止，不再发送数据。
- 串口断开：取消引擎，清理临时文件，终端回到断开状态。
- 标签页关闭或应用退出：终止线程，清理临时目录，不弹对话框。
- 保存对话框期间不占用协议超时，因为协议会话已经结束。
- 引擎线程 join 设置上限；超时后强制标记结束并恢复终端。

## 8. 测试

### 8.1 单元测试

- CRC16/CRC32 已知向量。
- ZHEX/ZBIN/ZBIN32 头编解码和 CRC 校验。
- ZDLE 转义/反转义，包括 ZRUB0/ZRUB1。
- 数据子包编码、解析、边界长度。
- detector：完整头、跨 chunk 头、无效 CRC、普通文本、多个候选、回退顺序。
- 文件名清洗、UTF-8 失败、路径穿越、非法字符。
- ZFILE 元数据解析，包括缺少 size。

### 8.2 协议集成测试

使用测试环境本地编译的 `lrzsz`（不进入运行时依赖）作为对端，通过管道把双方连接：

- Python 接收 vs `sz`：单文件、多文件、随机二进制、1 MB 文件、带空格和 UTF-8 文件名。
- Python 发送 vs `rz`：单文件、多文件、随机二进制、1 MB 文件。
- 延迟启动：模拟接收保存/发送选择造成的延迟。
- ZCAN 取消、超时、远端中止。
- 校验接收文件 SHA256 和发送文件 SHA256。

集成测试在找不到 `sz`/`rz` 时跳过；测试文档说明如何在本地构建 `lrzsz`。Windows 上可用便携版 `lrzsz` 作为测试对端，但不提交到仓库。

### 8.3 无头 GUI 测试

- 假连接下验证普通文本继续进入终端。
- 注入合法 ZRQINIT/ZRINIT，验证自动进入传输状态、输入锁定、状态栏显示。
- 注入不完整/无效头，验证回退到终端。
- 模拟接收完成，验证逐个保存、取消单个文件、取消全部。
- 模拟发送选择取消，验证发送 ZCAN 和终端恢复。
- 模拟连接断开、引擎异常，验证清理和恢复。

### 8.4 真实串口验收

在用户确认的维护窗口内，用 Windows 正式实例连接 Linux 板卡：

1. Linux `sz` 单文件和多文件 → COMTool 接收，SHA 一致，另存为正常。
2. Linux `rz` → COMTool 多选发送，Linux 端 SHA 一致。
3. 传输中取消、远端 Ctrl+C、拔插/断开，终端均能恢复。
4. 传输前后普通终端输出、按键、中文显示正常。
5. 不在用户正在使用的 COM99 上执行破坏性测试，除非用户明确同意。

## 9. 风险与备选方案

### 9.1 主要风险

纯 Python ZMODEM 状态机是主要风险，尤其是 lrzsz 的重传、ZCRCQ/ZCRCW、ZFIN/OO 边界行为。控制措施：

- 先实现并验证协议引擎，再接 UI。
- 所有互通用真实 lrzsz 做双向集成测试，不以单元测试代替。
- 保留详细日志开关，便于现场定位。
- 如果互通无法在合理范围内稳定，停下来向用户报告，不擅自改成外部二进制方案。

### 9.2 已考虑的备选方案

- 内置 Windows `lrzsz` 二进制：协议成熟，但用户明确选择纯 Python，且涉及 GPL 助手和仓库体积。
- 所有平台依赖系统 `rz`/`sz`：仓库干净，但 Windows 无法开箱即用。
- 纯 Python 方案：无外部依赖、跨平台一致，但开发和测试成本最高；本设计选择此方案。

## 10. 验收标准

- Linux `sz` 单文件和多文件均能自动触发接收，保存后 SHA256 一致。
- Linux `rz` 能自动触发多文件选择并发送，Linux 端 SHA256 一致。
- 传输期间终端不显示二进制乱码，输入被锁定；结束后恢复。
- 取消、远端中止、串口断开、协议超时均能在合理时间内恢复，不残留临时文件。
- 普通终端输出中的疑似 ZMODEM 字节不会被误吞。
- Linux 无头测试通过；Windows 正式实例通过真实串口验收。
- 不新增运行时依赖；不改变 SSH/SFTP、日志等非目标功能。

## 11. 预计改动文件

- 新增 `COMTool/zmodem/`：`__init__.py`、`crc.py`、`codec.py`、`detector.py`、`io.py`、`filename.py`、`receiver.py`、`sender.py`、`session.py`
- 修改 `COMTool/plugins/terminal.py`：detector 分流、状态栏、输入锁定、保存/选择对话框、线程生命周期
- 新增 `tests/test_zmodem_crc.py`、`tests/test_zmodem_codec.py`、`tests/test_zmodem_detector.py`、`tests/test_zmodem_filename.py`、`tests/test_zmodem_integration.py`、`tests/test_terminal_zmodem.py`
- 可选新增测试辅助脚本 `tools/build_lrzsz_for_tests.sh`，仅用于本地/CI 集成测试，不参与打包
- 更新 `docs/superpowers/plans/` 下的实现计划（下一步）
