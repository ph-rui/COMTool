# COMTool 纯 Python ZMODEM 文件传输实现计划

对应设计：`docs/superpowers/specs/2026-10-18-comtool-zmodem-design.md`

分支：`feature/live-port-refresh`

目标：在不引入运行时外部程序的前提下，实现与 Linux `rz`/`sz` 互通的 ZMODEM 文件传输，并接入 terminal 标签页。

## 执行原则

- 每个任务先写失败测试，再实现到通过。
- 协议引擎先与真实 `lrzsz` 互通，再接入 Qt UI。
- 所有代码兼容 Python 3.8（项目 venv 为 3.8.10）。
- 每个任务单独提交；提交前跑对应测试。
- 不使用外部运行时依赖；测试用 `lrzsz` 只作为对端，不进入打包。
- 不在用户正在使用的 COM99 上做破坏性测试。

## 阶段 0：基线与测试辅助

- [ ] 0.1 确认工作区干净，记录基线测试：
  ```bash
  cd /tmp/opencode/COMTool
  venv/bin/python -m pytest tests -q
  ```
  预期当前为 `26 passed`。
- [ ] 0.2 新增测试辅助 `tests/zmodem_peer.py`：
  - 查找 `COMTOOL_LRZSZ_DIR` 下的 `sz`/`rz`（Windows 为 `.exe`），或 `shutil.which`。
  - 提供 `LrzszPeer`：启动 `sz`/`rz` 子进程，用线程把子进程 stdout 接入 `ZmodemIO.feed`，把 `ZmodemIO.write` 写到子进程 stdin。
  - 找不到时让集成测试 skip，而不是失败。
- [ ] 0.3 可选新增 `tools/build_lrzsz_for_tests.sh`，用于 Linux 本地构建 `lrzsz 0.12.20`；不参与打包。
- [ ] 0.4 提交：`test: add zmodem integration test harness`

## 阶段 1：协议基础

### 任务 1：CRC

- [ ] 1.1 新增 `tests/test_zmodem_crc.py`：
  - CRC16/XMODEM 已知向量：空数据、`b"123456789"`、单字节。
  - CRC32 与 `zlib.crc32` 对照。
  - 大小端打包/解包。
- [ ] 1.2 新增 `COMTool/zmodem/crc.py`：
  - `crc16(data) -> int`，多项式 `0x1021`。
  - `crc32(data) -> int`，标准 IEEE CRC32。
  - `pack_crc16` / `unpack_crc16`、`pack_crc32` / `unpack_crc32`。
- [ ] 1.3 运行：
  ```bash
  venv/bin/python -m pytest tests/test_zmodem_crc.py -q
  ```
- [ ] 1.4 提交：`feat(zmodem): add crc16 and crc32`

### 任务 2：头、ZDLE、数据子包

- [ ] 2.1 新增 `tests/test_zmodem_codec.py`：
  - 常量帧类型编号正确。
  - ZHEX 头编码包含 `**\x18B`、14 个十六进制字符、CRC16、`\r\n\x11`。
  - ZBIN/ZBIN32 头编码/解析；CRC 校验失败时拒绝。
  - 带前导垃圾的解析；不完整头返回 `None`；跨 chunk 解析。
  - ZDLE 转义/反转义，控制字符、ZDLE、0x0d/0x0a、XON/XOFF。
  - 数据子包 ZCRCG/ZCRCQ/ZCRCE/ZCRCW 编解码，CRC16/CRC32，空 payload。
  - 子包解析在数据不完整时返回 `None`，不消耗输入。
- [ ] 2.2 新增 `COMTool/zmodem/codec.py`：
  - 常量：`ZPAD`、`ZDLE`、`ZBIN`、`ZHEX`、`ZBIN32`、帧类型、子包类型、标志位。
  - `ZHeader` dataclass：`frame_type`、`data`（4 字节）、`raw`、`crc_len`。
  - `encode_hex_header(frame_type, data=b"\0\0\0\0")`。
  - `encode_binary_header(frame_type, data, crc_len=16)`。
  - `parse_header(buffer) -> Optional[Tuple[ZHeader, int]]`，支持前导垃圾和三种编码。
  - `zdle_encode(data, escape_ctrl=True)`、`zdle_decode(data)`。
  - `encode_subpacket(payload, frame_end, crc_len)`。
  - `parse_subpacket(buffer, crc_len) -> Optional[Tuple[Subpacket, int]]`，支持 ZDLE 解码和 CRC 校验。
  - 提供 `strip_ignored_bytes`（XON/XOFF）。
- [ ] 2.3 运行：
  ```bash
  venv/bin/python -m pytest tests/test_zmodem_codec.py -q
  ```
- [ ] 2.4 提交：`feat(zmodem): add header, zdle and subpacket codec`

### 任务 3：ZMODEM 自动识别

- [ ] 3.1 新增 `tests/test_zmodem_detector.py`：
  - 普通文本全部透传。
  - 完整 ZRQINIT → `direction="receive"`；完整 ZRINIT → `direction="send"`。
  - 头跨多个 chunk 时延迟输出前缀，最终识别。
  - 无效 CRC → 回退为终端字节，不误触发。
  - 文本中出现 `**\x18` 片段 → 不吞后续内容。
  - 识别头后的 `remainder` 正确。
  - 多个候选头时只触发第一个合法头。
- [ ] 3.2 新增 `COMTool/zmodem/detector.py`：
  - `Detection` / `DetectionResult` dataclass。
  - `ZmodemDetector.feed(data: bytes) -> DetectionResult`。
  - 内部维护 pending 缓冲；只保留最大头长度 + 1 的尾部。
  - 使用 codec 的 `parse_header` 校验 CRC 和帧类型。
  - 识别后清空 pending，不重复触发。
- [ ] 3.3 运行：
  ```bash
  venv/bin/python -m pytest tests/test_zmodem_detector.py -q
  ```
- [ ] 3.4 提交：`feat(zmodem): detect zmodem headers in terminal stream`

### 任务 4：文件名与 ZFILE 元数据

- [ ] 4.1 新增 `tests/test_zmodem_filename.py`：
  - UTF-8、带空格、中文、非法字节文件名。
  - 路径穿越 `../../etc/passwd` → basename。
  - Windows 非法字符、控制字符、`.`/`..`、空名 → `download`。
  - 唯一临时文件名生成，避免多文件重名。
  - ZFILE 元数据解析：`name\0size mtime mode serial`；缺少 size；负值/非数字。
- [ ] 4.2 新增 `COMTool/zmodem/filename.py`：
  - `decode_zmodem_name(raw: bytes) -> str`。
  - `sanitize_basename(name: str) -> str`。
  - `unique_temp_path(dir, name) -> str`。
  - `parse_zfile_metadata(payload: bytes) -> FileMeta`。
- [ ] 4.3 运行：
  ```bash
  venv/bin/python -m pytest tests/test_zmodem_filename.py -q
  ```
- [ ] 4.4 提交：`feat(zmodem): parse and sanitize received file names`

### 任务 5：线程安全 IO

- [ ] 5.1 新增 `tests/test_zmodem_io.py`：
  - `feed` 后 `read` 能读满指定字节。
  - 跨多次 `feed` 读取。
  - 超时抛出 `ZmodemTimeout`。
  - `cancel` 唤醒阻塞读取并抛出 `ZmodemAbort`。
  - `write` 回调收到完整数据。
  - 多线程生产者/消费者无丢失、无重复。
- [ ] 5.2 新增 `COMTool/zmodem/io.py`：
  - `ZmodemTimeout`、`ZmodemAbort`。
  - `ZmodemIO(feed, write_callback)`，内部 `bytearray` + `Condition`。
  - `read(size, timeout)` 必须读满；`write` 直接调用回调；`cancel` 设置标志并通知。
- [ ] 5.3 运行：
  ```bash
  venv/bin/python -m pytest tests/test_zmodem_io.py -q
  ```
- [ ] 5.4 提交：`feat(zmodem): add thread-safe byte io`

## 阶段 2：接收引擎与真实互通

### 任务 6：接收状态机

- [ ] 6.1 新增 `tests/test_zmodem_receiver_unit.py`：
  - 用 fake peer 脚本构造 ZRINIT/ZFILE/ZDATA/ZEOF/ZFIN 序列，验证发送的 ZRINIT/ZRPOS/ZACK/ZFIN。
  - 多文件开始/完成回调顺序。
  - 文件名/大小解析、进度回调。
  - ZRPOS 重传请求、ZNAK、ZEOF offset 不匹配。
  - ZCAN/ZABORT 中止。
  - 超过 5 次重试后失败。
- [ ] 6.2 新增 `COMTool/zmodem/receiver.py`：
  - `ReceivedFile` dataclass。
  - `ZmodemReceiver(io, output_dir, callbacks)`。
  - `run()`：发送 ZRINIT；处理 ZRQINIT/ZFILE/ZDATA/ZEOF/ZFIN/ZSKIP/ZNAK/ZABORT/ZCAN。
  - 支持 16/32 位 CRC；ZDATA 子包流式写文件。
  - ZFILE 后发送 ZRPOS(0)；数据帧结束时按需 ZACK。
  - ZEOF 后发送 ZRINIT 等待下一个文件；ZFIN 后发送 ZFIN 并完成 `OO` 收尾。
  - 文件写入临时目录，使用 `filename.py` 清洗。
  - 进度按已写字节回调；总大小未知时 `total=None`。
- [ ] 6.3 运行单元测试：
  ```bash
  venv/bin/python -m pytest tests/test_zmodem_receiver_unit.py -q
  ```
- [ ] 6.4 新增 `tests/test_zmodem_receiver_integration.py`，用真实 `lrzsz` 的 `sz` 作为对端：
  - 单文件、随机 256KB。
  - 多文件。
  - 1MB 文件。
  - 带空格/中文文件名。
  - 延迟启动（先启动 `sz`，2 秒后启动接收引擎）。
  - 校验 SHA256。
- [ ] 6.5 运行集成测试（先设置测试二进制目录）：
  ```bash
  COMTOOL_LRZSZ_DIR=/tmp/opencode/lrzsz-install/bin \
    venv/bin/python -m pytest tests/test_zmodem_receiver_integration.py -q
  ```
- [ ] 6.6 根据真实抓包修正 ZRINIT 标志、ZACK/ZRPOS 时机、ZFIN/OO 顺序。
- [ ] 6.7 提交：`feat(zmodem): implement zmodem receiver`

## 阶段 3：发送引擎与真实互通

### 任务 7：发送状态机

- [ ] 7.1 新增 `tests/test_zmodem_sender_unit.py`：
  - fake peer 构造 ZRINIT，验证 ZFILE/ZDATA/ZEOF/ZFIN 顺序。
  - 多文件顺序。
  - ZRINIT 不支持 CANFC32 时使用 16 位 CRC。
  - ZRPOS 重传从指定 offset 继续。
  - ZSKIP 跳过文件。
  - ZNAK 重发当前帧；连续 5 次失败中止。
  - ZCAN/ZABORT 中止。
  - 文件不存在/读取失败。
- [ ] 7.2 新增 `COMTool/zmodem/sender.py`：
  - `ZmodemSender(io, files, initial_zrinit=None, callbacks)`。
  - `run()`：若有 initial_zrinit 直接使用；否则发送 ZRQINIT 并等待 ZRINIT。
  - 根据 ZRINIT flags 选择 16/32 位子包 CRC、是否强制 ZDLE 控制字符转义。
  - 每个文件发送 ZFILE（二进制头）+ 元数据子包，等待 ZRPOS(0)。
  - 按 8192 字节分块发送 ZDATA + ZCRCG，最后 ZCRCE；发送 ZEOF。
  - 等待下一个 ZRINIT；全部文件后发送 ZFIN，等待 ZFIN，发送 `OO`。
  - 进度按已发送文件字节回调。
- [ ] 7.3 运行单元测试：
  ```bash
  venv/bin/python -m pytest tests/test_zmodem_sender_unit.py -q
  ```
- [ ] 7.4 新增 `tests/test_zmodem_sender_integration.py`，用真实 `lrzsz` 的 `rz` 作为对端：
  - 单文件、多文件、1MB 文件、随机二进制。
  - 延迟启动（先启动 `rz`，2 秒后启动发送引擎）。
  - 取消。
  - 校验对端文件 SHA256。
- [ ] 7.5 运行集成测试：
  ```bash
  COMTOOL_LRZSZ_DIR=/tmp/opencode/lrzsz-install/bin \
    venv/bin/python -m pytest tests/test_zmodem_sender_integration.py -q
  ```
- [ ] 7.6 根据抓包修正 ZFILE 头编码、ZCRCG/ZCRCE、ZEOF/ZFIN 和 `OO` 顺序。
- [ ] 7.7 提交：`feat(zmodem): implement zmodem sender`

## 阶段 4：会话编排

### 任务 8：Session、取消、超时

- [ ] 8.1 新增 `tests/test_zmodem_session.py`：
  - 接收/发送线程启动和结束。
  - `cancel()` 发送 ZCAN 并唤醒读取。
  - 超时回调错误。
  - 临时目录创建/清理。
  - 回调顺序：start → progress → complete → finished。
  - 重复 cancel 幂等。
- [ ] 8.2 新增 `COMTool/zmodem/session.py`：
  - `ZmodemSession(direction, io, files_or_output, callbacks)`。
  - daemon 线程运行 receiver/sender。
  - `cancel()` 设置取消、发送中止序列、唤醒 IO。
  - 统一超时（头 30s，数据不活动 30s）。
  - 接收完成后保留临时目录，交给 UI 保存；`cleanup()` 删除。
- [ ] 8.3 运行：
  ```bash
  venv/bin/python -m pytest tests/test_zmodem_session.py -q
  ```
- [ ] 8.4 提交：`feat(zmodem): add session orchestration and cancellation`

## 阶段 5：terminal 集成

### 任务 9：数据分流与自动识别

- [ ] 9.1 新增 `tests/test_terminal_zmodem_routing.py`（无头 Qt）：
  - 普通数据仍进入 `Scroll_Terminal.onReceived`。
  - 合法 ZRQINIT 后进入接收状态，后续数据不再进终端。
  - 合法 ZRINIT 后进入 pending-send，触发文件选择信号。
  - 无效头/普通文本透传。
  - 传输结束/取消后数据重新进入终端。
  - 连接断开时取消。
- [ ] 9.2 修改 `COMTool/plugins/terminal.py`：
  - 导入 `ZmodemDetector`、`ZmodemSession`、`ZmodemIO`。
  - `Plugin.onInit` 增加 `zmodem_last_dir` 默认值。
  - `Plugin.onReceived` 按设计分流，使用 `threading.RLock` 保护 session 状态。
  - 新增 Qt signal：进度、完成、错误、发送文件选择请求。
  - `onDel` / `onConnChanged` 取消并清理。
  - `Terminal_Frontend` 增加 `setTransferActive`，在 `keyPressEvent` 顶部拦截输入。
- [ ] 9.3 运行：
  ```bash
  QT_QPA_PLATFORM=offscreen venv/bin/python -m pytest tests/test_terminal_zmodem_routing.py -q
  ```
- [ ] 9.4 提交：`feat(terminal): route zmodem transfers from serial stream`

### 任务 10：状态栏、对话框和生命周期

- [ ] 10.1 新增 `tests/test_terminal_zmodem_ui.py`（无头 Qt，mock 对话框）：
  - 状态栏控件空闲隐藏，传输时显示。
  - 进度更新、取消按钮触发 cancel。
  - 接收完成后逐个保存，取消单个文件只删该文件。
  - 发送选择取消时发送 ZCAN。
  - 保存目录写入 `zmodem_last_dir`。
  - 错误/断开/关闭标签清理。
- [ ] 10.2 修改 `COMTool/plugins/terminal.py`：
  - 在 `onUiInitDone` 中给 `self.statusBar` 增加传输标签、`QProgressBar`、取消按钮。
  - 连接 Qt signal 到 UI 槽。
  - pending-send 保存最新 ZRINIT；文件选择后启动 sender。
  - 接收完成逐个 `QFileDialog.getSaveFileName`，`shutil.move` 到目标；取消单个文件删除临时文件。
  - 保存/选择目录写入配置。
  - 传输开始/结束切换 `setTransferActive`。
  - 错误显示在状态栏，必要时 `hintSignal`。
- [ ] 10.3 运行：
  ```bash
  QT_QPA_PLATFORM=offscreen venv/bin/python -m pytest tests/test_terminal_zmodem_ui.py -q
  ```
- [ ] 10.4 提交：`feat(terminal): add zmodem status bar and dialogs`

## 阶段 6：收尾与验证

### 任务 11：错误路径、文档和翻译

- [ ] 11.1 补测试：超时、远端 ZCAN、串口断开、标签关闭、写盘失败、临时文件清理。
- [ ] 11.2 更新 `messages.pot`、`zh_CN`、`zh_TW` 中新增的界面字符串（传输中、取消、保存、错误提示）。
- [ ] 11.3 README 增加：
  - Linux 端 `sz`/`rz` 用法。
  - 接收后另存为的流程。
  - 纯 Python 实现、无外部运行时依赖。
  - 本地集成测试构建 `lrzsz` 的说明。
- [ ] 11.4 运行完整测试：
  ```bash
  venv/bin/python -m pytest tests -q
  QT_QPA_PLATFORM=offscreen venv/bin/python -m pytest tests -q
  venv/bin/python -m py_compile COMTool/zmodem/*.py COMTool/plugins/terminal.py
  ```
- [ ] 11.5 提交：`docs: document zmodem file transfer`

### 任务 12：Windows 与真实串口验收

- [x] 12.1 将分支同步到 Windows 测试仓库（必要时用本地 bundle）。
  - 仓库路径：`C:\Users\zhangrui\COMTool-hotplug`
  - 通过本地 bundle + HTTP 传输，`git pull <bundle> feature/live-port-refresh --ff-only` 成功。
- [x] 12.2 Windows 运行：
  ```bat
  python -m pytest tests -q
  ```
- [x] 12.3 Windows 无头/离屏 GUI 测试通过。
- [x] 12.4 在用户确认的维护窗口连接 Linux 板卡：
  - Linux `sz` 单/多文件 → COMTool 接收，SHA256 一致。
  - Linux `rz` → COMTool 多选发送，Linux SHA256 一致。
  - 取消、远端 Ctrl+C、断开串口恢复。
  - 终端普通输出和按键正常。
  - 备注：取消大文件后，PC 驱动和板端 tty 中已排队的 ZMODEM 数据会在远端
    shell 回显为一段二进制乱码；远端 `rz` 已删除半成品并退出，按一次
    Ctrl+C 即可得到干净提示符，不影响后续传输。
- [x] 12.5 记录验证结果到计划文档。
- [x] 12.6 推送 `origin/feature/live-port-refresh`。

## 验证记录

### 2026-10-18 Linux 开发机

- 无外部 `lrzsz`：`102 passed, 13 skipped`。
- 带本地编译的 `lrzsz 0.12.20`（`COMTOOL_LRZSZ_DIR=/tmp/opencode/lrzsz-install/bin`）：`115 passed`。
- 覆盖：单/多文件、1MB、延迟启动、16 位 CRC、中文/空格文件名、空文件、SHA256、CRC 重试、ZCAN、ZNAK、ZRPOS、ZSKIP、UI 路由/状态栏/保存对话框、插件级真实 lrzsz 端到端。
- `python -m py_compile COMTool/zmodem/*.py COMTool/plugins/terminal.py` 通过。
- `zh_CN`/`zh_TW` 翻译文件可正常加载。

### 2026-10-18 Windows 测试机 172.16.66.163

- 仓库：`C:\Users\zhangrui\COMTool-hotplug`，分支 `feature/live-port-refresh`，
  HEAD `e21b381`（与 Linux `e1dc519` 内容相同），工作区干净。
- Python 3.10.11 + PyQt5（`QT_QPA_PLATFORM=offscreen`）。
- 无外部 `lrzsz`：`102 passed, 13 skipped in 2.33s`。
- 使用 win32 `lrzsz 0.12.21rc`
  （`C:\Users\zhangrui\lrzsz-bin\lrzsz_0.12.21rc_windows_x86_64`）：
  - 集成/E2E：`13 passed in 17.76s`。
  - 全量：`116 passed, 9 warnings in 10.91s`（修复后复跑）。
- GUI 冒烟：`python -m COMTool.Main`（临时 APPDATA，未自动连接串口）启动正常；
  terminal 标签页正常，左侧设置面板悬停自动收起，蓝色 chevron 手柄显示正常。

### 2026-10-18 Windows 真实串口 COM99（Linux 开发板）

- 环境：COM99，115200 8N1 无流控；板端 `root@imx95evk`
  （kernel 6.12.49 aarch64，BusyBox 1.37.0，lrzsz 0.12.20）。
- 修复：`e1dc519` / `e21b381`。
  - 串口接收改为 `timeout=0` + `in_waiting` 轮询，并在线路空闲或缓冲达到
    4 KiB 时立即交付；修复“最后一个 ZRINIT 被扣在缓冲区、发送卡在 100%”。
  - ZMODEM 写入等待 `sendData` 队列回调形成背压；进度条按实际写入推进，
    取消不再被整文件队列拖住。
- 发送（板端 `rz -O -y -b`）：
  - `sd.bin` 32 KiB → SHA256
    `aa3203fdef2a8af45354bc599f179f163445351a9171ee917ff78ce3e64bee6a`
    一致，状态栏 “Files sent”。
  - `big.bin` 1 MiB → SHA256
    `43cac66fa1323973901a37f58e88cbf1d92b59f4099f0a991662516b6cef2454`
    一致；进度条约 10% → 50% → 100% 逐步推进，耗时约 90 s。
  - 多选 `sd.bin` + `s3.bin` → 两个 SHA256 均一致
    （`s3.bin` 为 `fdc6acc8777ccb482e36adf750a5ada25fcd22b6516c05368a1f8399c435e`）。
- 接收（板端 `sz -q -b b2.dat`）：弹“Save received file”，保存到
  `C:\Users\zhangrui\b2.dat`，SHA256
  `9124c39a5ddfe31b06f308d75e9dcfdabd9ff39d6b05d783b99c97adde1641c`
  与板端一致，状态栏 “Received 1 file(s)”。
  多文件接收（3 个文件含中文名）已在此前同一设备上验证通过。
- 取消：发送 1 MiB 中途点击 Cancel，状态栏 “File transfer cancelled”，
  板端 `rz: big.bin removed.` 并回到提示符；UI 未卡死，按 Ctrl+C 后终端恢复。
- 未再验证：真实 USB 热插拔、串口断开后的自动重连（受限于现场操作）。

### 待验收（需要用户确认维护窗口）

- 真实 USB 热插拔与串口断开/重连恢复。
- 视需要运行 `python pack.py` 打包 `dist\comtool\comtool.exe`。

## 风险与回退点

- 任务 6/7 是最大风险点；如果真实 `lrzsz` 互通在合理时间内不稳定，停止并向用户报告，不擅自切换到外部二进制方案。
- 若 Qt 集成出现线程/信号死锁，先把 session 与 UI 解耦，用 fake IO 复现，再修。
- 若 Windows 串口写入队列导致 ZMODEM 超时，检查 `PluginItem.sendDataProcess` 的发送节奏，必要时为 ZMODEM 数据增加专用发送队列或回调，但不改普通终端发送语义。
- 若接收保存对话框影响终端恢复，先恢复终端输入，再弹保存框（协议已完成，不需要锁输入）。

## 验收命令汇总

```bash
cd /tmp/opencode/COMTool

# 纯单元测试
venv/bin/python -m pytest tests/test_zmodem_crc.py \
  tests/test_zmodem_codec.py \
  tests/test_zmodem_detector.py \
  tests/test_zmodem_filename.py \
  tests/test_zmodem_io.py -q

# 真实 lrzsz 互通
COMTOOL_LRZSZ_DIR=/tmp/opencode/lrzsz-install/bin \
  venv/bin/python -m pytest tests/test_zmodem_receiver_integration.py \
  tests/test_zmodem_sender_integration.py -q

# 无头 GUI
QT_QPA_PLATFORM=offscreen venv/bin/python -m pytest tests/test_terminal_zmodem_routing.py \
  tests/test_terminal_zmodem_ui.py -q

# 全量
venv/bin/python -m pytest tests -q
```
