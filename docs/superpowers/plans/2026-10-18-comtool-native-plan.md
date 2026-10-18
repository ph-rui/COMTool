# COMTool Native v1 实施计划

依据：`docs/superpowers/specs/2026-10-18-comtool-native-design.md`

状态：v1 已实现并在 Linux / Windows 验收。旧 Python 应用保持独立，仍使用原配置与原有功能。

## 1. 工程与工具链

- [x] 建 `native/` CMake 工程，Qt5 Widgets/SerialPort，C++17。
- [x] vendor libvterm 0.3.3 源码与 LICENSE，建静态库目标。
- [x] 空 MainWindow 启动，`--benchmark-startup`。
- [x] Linux 本地 Qt 5.15.2 构建通过。
- [x] Windows MinGW 8.1 + Qt 5.15.2 构建通过，GUI 子系统无控制台窗口。

## 2. 协议基础

- [x] CRC16/CRC32、ZDLE、header、subpacket。
- [x] ZmodemReader 与 ZmodemDetector。
- [x] 文件名清洗与临时路径。
- [x] `zmodem_tests` 覆盖 codec/detector/filename。
- [x] 可选的 `zmodem_peer` CLI。

## 3. 终端

- [x] TerminalView 集成 libvterm、damage 合并、滚动历史。
- [x] ANSI/256 色/宽字符/属性/光标。
- [x] 键盘、IME、粘贴、选择、复制、滚轮、字体缩放。
- [x] 终端清屏与主题切换。

## 4. 串口与 UI

- [x] SerialWorker 线程、打开/关闭、错误、热插拔。
- [x] SerialPanel 参数与可编辑端口。
- [x] 多标签、折叠面板、窗口配置恢复。
- [x] 浅色/深色、中英文字符串表。
- [x] 纯 QPalette 主题；关闭按钮/折叠箭头使用当前文字色自绘，深色下可见。
- [x] 初始窗口激活后聚焦终端，无控制台窗口，窗口标题不重复应用名。

## 5. ZMODEM 集成

- [x] SerialZmodemIO（阻塞读、背压写、cancel、drain）。
- [x] 自动识别双向、remainder 注入。
- [x] sender/receiver 回调、多文件、1 KiB 窗口。
- [x] 发送选文件暂停/恢复；接收临时目录/逐个另存为。
- [x] 进度条、取消、错误处理、尾流限长。
- [x] ZFILE mtime/mode 使用八进制；发送中源文件尺寸变化中止。

## 6. 验收

- [x] `zmodem_tests` 全部通过（Linux 与 Wine 下 Windows 构建）。
- [x] PTY + lrzsz：双向单文件、多文件、空文件、二进制 SHA256。
- [x] 262144 字节双向 SHA256；元数据时间戳保留。
- [x] Xvfb GUI：终端收发、ANSI/256 色、IME/宽字符、切换标签、手工 PTY、复制粘贴、滚动、折叠面板。
- [x] GUI ZMODEM 发送/接收 65536 字节 SHA256 一致；多文件逐项另存为；发送空文件；取消传输。
- [x] Linux 启动计时：`startup_ms=117–209`（Qt 5.15.2 Release，Xvfb）。
- [x] Windows 构建与启动计时：RDP VM 上 604–674 ms；同机仅一个 QLabel 的裸 Qt 窗口约 509 ms，字体库初始化约 340 ms，为目标平台下限。
- [x] 原生 4 MiB GUI 双向 PTY 测试（与 lrzsz）：4194304 字节，收/发 SHA256 `3e7a90c4d60cf213aafa59e00a5fd59399d92a7c7230a5b469addc82b1125194` 一致；接收逐个另存为、发送进度/完成状态正常。
- [x] 原生 4 MiB 双向实板测试：`console.sh` 暴露 i.MX95 EVK 的 `ttyLP0`（COM99，115200 8N1），板端 lrzsz 0.12.20；GUI 发送/接收均为 4194304 字节，SHA256 `3e7a90c4d60cf213aafa59e00a5fd59399d92a7c7230a5b469addc82b1125194` 与主机一致；发送约 8–9 分钟、接收约 6 分钟，接收逐个另存为、进度/完成状态正常。
- [x] 更新 native/README 与本计划状态。

## 7. 仓库

- [x] 设计/计划提交。
- [x] native 实现提交（与旧 Python 应用隔离）。
- [x] 推送 `da6e9b0` 及 native 提交到 `origin/feature/live-port-refresh`。

## 8. 已知限制

- Windows RDP 测试机没有 Qt/CMake/MinGW，只能在 Wine 下交叉构建后把部署包传到 Windows 运行；无法在目标机本机重编译。
- Windows 启动目标 ≤0.5 s 在该 RDP VM 上受 Qt 字体库初始化限制，实测 0.60–0.68 s；普通本地桌面预期更低，但未在更快的 Windows 机器上实测。
- 真实 USB 串口热插拔/断开自动恢复仍需用实际设备验证；PTY 与手工端口路径已验收。
