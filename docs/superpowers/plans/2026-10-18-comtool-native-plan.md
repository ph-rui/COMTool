# COMTool Native v1 实施计划

依据：`docs/superpowers/specs/2026-10-18-comtool-native-design.md`

## 1. 工程与工具链

- [ ] 建 `native/` CMake 工程，Qt5 Widgets/SerialPort，C++17。
- [ ] vendor libvterm 0.3.3 源码与 LICENSE，建静态库目标。
- [ ] 空 MainWindow 启动，`--benchmark-startup`。
- [ ] Linux 本地 Qt 5.15.2 构建通过。

## 2. 协议基础

- [ ] CRC16/CRC32、ZDLE、header、subpacket。
- [ ] ZmodemReader 与 ZmodemDetector。
- [ ] 文件名清洗与临时路径。
- [ ] `zmodem_tests` 覆盖 codec/detector/filename。
- [ ] 可选的 `zmodem_peer` CLI。

## 3. 终端

- [ ] TerminalView 集成 libvterm、damage 合并、滚动历史。
- [ ] ANSI/256 色/宽字符/属性/光标。
- [ ] 键盘、IME、粘贴、选择、复制、滚轮、字体缩放。
- [ ] 终端清屏与主题切换。

## 4. 串口与 UI

- [ ] SerialWorker 线程、打开/关闭、错误、热插拔。
- [ ] SerialPanel 参数与可编辑端口。
- [ ] 多标签、折叠面板、窗口配置恢复。
- [ ] 浅色/深色、中英文字符串表。

## 5. ZMODEM 集成

- [ ] SerialZmodemIO（阻塞读、背压写、cancel、drain）。
- [ ] 自动识别双向、remainder 注入。
- [ ] sender/receiver 回调、多文件、1 KiB 窗口。
- [ ] 发送选文件暂停/恢复；接收临时目录/逐个另存为。
- [ ] 进度条、取消、错误处理、尾流限长。

## 6. 验收

- [ ] `zmodem_tests` 全部通过。
- [ ] PTY + lrzsz：双向单文件、多文件、空文件、二进制 SHA256。
- [ ] Xvfb GUI：终端收发、切换标签、手工 PTY、复制粘贴、滚动。
- [ ] 启动计时 Linux；记录结果。
- [ ] 板端 4 MiB 双向或至少 256 KiB 双向 SHA256。
- [ ] Windows 构建（工具链就绪后）与启动计时。
- [ ] 更新 native/README 与本计划状态。

## 7. 仓库

- [ ] 设计/计划提交。
- [ ] native 实现按逻辑分批提交，保持旧 Python 应用测试与行为不变。
