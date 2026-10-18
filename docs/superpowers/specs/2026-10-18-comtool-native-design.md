# COMTool Native 设计（v1）

日期：2026-10-18  
状态：实现基线  
目标仓库：本仓库 `native/` 子目录，独立构建、独立可执行文件

## 1. 目标与边界

COMTool Native 是一个真正的原生串口终端，面向“打开即用”的日常串口/板端调试场景，和现有 Python 版 COMTool 并行存在。它不是 Python 版的替代补丁，也不加载 Python 运行时。

### 1.1 v1 必须提供

- 原生 C++17 + Qt Widgets 桌面应用，Windows 10+ 与 Linux x86_64 可构建。
- 串口终端：打开/关闭、参数设置、UTF-8、256 色、宽字符、IME、复制/粘贴、选择、5000 行滚动历史。
- 端口热插拔刷新；串口被拔出后只标记断开，不自动重连。
- 多会话标签，每个标签独立串口、独立终端、独立 ZMODEM 状态。
- ZMODEM 双向自动识别：接收 `sz` 发来的文件，向等待接收的 `rz` 发送文件；多文件；进度、取消、接收文件逐个“另存为”。
- 与 Linux `lrzsz`（`rz`/`sz`）互通，不调用外部传输程序。
- 配置持久化：窗口几何、主题、字体大小、滚动行数、上次端口参数、标签列表、最近目录；启动恢复标签但不自动打开串口。
- 中英双语 UI，跟随系统语言；浅色默认，支持深色。

### 1.2 v1 明确不做

- Send Receive 面板、protocol 面板、graph 面板。
- SSH/TCP、插件系统、日志记录、定时发送、脚本。
- 自动重连、端口独占探测、复杂会话恢复。
- macOS 发布包（代码保持可移植，构建脚本后续补）。
- 声音/桌面通知、自动更新。

### 1.3 与旧 Python 版的关系

- 旧版入口、配置、依赖、打包、测试保持不变。
- native 使用自己的配置目录与可执行文件，不读写旧版配置。
- 旧版功能不被删除或改写；本设计只新增 `native/`。

## 2. 技术选型

| 项 | 选择 | 理由 |
|---|---|---|
| 语言 | C++17 | Qt 5.15 与现有工具链成熟，部署无运行时 |
| UI | Qt 5 Widgets | 启动快、控件稳定、无 QML 运行时开销 |
| 串口 | QSerialPort | 跨平台、热插拔枚举、参数完整 |
| 终端内核 | vendored libvterm 0.3.3（MIT） | 成熟 VT 解析、UTF-8/宽字符/256 色 |
| 构建 | CMake + Ninja | Windows/Linux 一致 |
| 测试 | CTest + 独立 zmodem peer + GUI 手工验收 | 协议不依赖 GUI，端到端用 PTY |

不引入 Python、Electron、QML、WebEngine。libvterm 以源码 vendored 到 `native/third_party/libvterm/`，构建不联网。

## 3. 目录结构

```
native/
  CMakeLists.txt
  README.md
  src/
    main.cpp
    mainwindow.{h,cpp}
    sessiontab.{h,cpp}
    serialworker.{h,cpp}
    serialconfig.h
    terminalview.{h,cpp}
    appconfig.{h,cpp}
    theme.{h,cpp}
    i18n.{h,cpp}
    zmodem/
      codec.{h,cpp}
      crc.{h,cpp}
      io.h
      reader.{h,cpp}
      detector.{h,cpp}
      filename.{h,cpp}
      sender.{h,cpp}
      receiver.{h,cpp}
  third_party/libvterm/   # 上游源码 + LICENSE
  tests/
    zmodem_tests.cpp      # codec/detector 单元测试
    zmodem_peer.cpp       # 与 lrzsz 互通的 CLI peer（Linux）
  packaging/              # 后续 NSIS/AppImage
```

CMake 目标：

- `libvterm`：静态 C 库。
- `comtool-native`：GUI 可执行文件。
- `zmodem_tests`：CTest 单元测试。
- `zmodem_peer`（非 Windows 默认）：协议互通测试工具。

## 4. 运行时结构

```
MainWindow
  QTabWidget
    SessionTab (每个串口会话)
      SerialPanel (端口/参数/Open)
      TerminalView (libvterm + 渲染 + 输入)
      TransferBar (进度/取消)
      SerialWorker (QThread，独占 QSerialPort 与 ZMODEM)
```

### 4.1 线程模型

- UI 线程只做 Qt 控件、libvterm 渲染、文件对话框、配置。
- 每个 `SessionTab` 在打开串口时创建一个 `SerialWorker` 线程。
- `SerialWorker` 独占 `QSerialPort`，串口读写、ZMODEM 状态机、文件 I/O 都在该线程内完成。
- UI → worker：串口发送字节、取消、发送文件列表，通过线程安全队列/条件变量传递。
- worker → UI：终端字节、连接状态、ZMODEM 文件进度、错误、接收文件临时路径，通过 queued signal。
- 关闭会话：设置 stop 标志，唤醒等待，`wait()` 后销毁线程；不跨线程直接 delete `QSerialPort`。

这样 ZMODEM 引擎可以保持同步、阻塞式移植，不需要把协议状态拆成回调；也避免 UI 线程被串口读写卡住。

### 4.2 串口生命周期

1. 用户点 Open：UI 读取参数，创建并启动 worker。
2. worker 在自己线程创建/配置 `QSerialPort`；成功发 `opened`，失败发 `openFailed` 并退出。
3. 正常运行时 `waitForReadyRead(10)` 轮询；收到字节先交给 `ZmodemDetector`，非协议字节发终端。
4. Close/端口消失/错误：worker 退出循环，关闭端口，发 `closed`；UI 恢复控件，不清空终端。
5. 端口热插拔由 UI 线程 `QTimer` 每秒枚举 `QSerialPortInfo`；端口下拉框可编辑，支持手工输入 `/dev/pts/N` 或特殊设备。
6. 不自动重连；端口重新出现后由用户再次 Open。

### 4.3 ZMODEM 自动识别

worker 内维护 `ZmodemDetector`：

- 扫描输入中的 ZMODEM 头。
- `ZRQINIT`：远端在发送 → 本端进入接收。
- `ZRINIT`：远端在等待接收 → 本端进入发送。
- 识别到协议头后，header 后的 remainder 交给新会话的 IO 缓冲；会话期间所有串口字节直接进入 `ZmodemIO`，不再送终端。
- 会话结束把最多 4 KiB 尾字节送终端，防止异常大尾流卡住 UI。
- 识别头之前的普通字节立即显示，只保留最多一个不完整头的尾部等待下一批数据。

发送方向识别到 `ZRINIT` 后需要用户选文件：worker 暂停并请求 UI 打开多选文件对话框；用户取消则清空发送队列并写 8×CAN + 8×BS，远端回到终端。选择完成后 worker 用已识别的 `ZRINIT` 作为 initial header 启动 sender。

### 4.4 取消

- UI Cancel 设置 worker 的原子 cancel 标志。
- `ZmodemIO` 每 50 ms 检查一次；抛 `ZmodemAbort`。
- worker 清空串口发送缓冲，直接写 `0x18 * 8 + 0x08 * 8`，删除未完成临时文件，发 `zmodemCancelled`。
- 取消后清空输入缓冲并恢复终端，避免残包被当终端数据显示。

## 5. 终端实现

### 5.1 libvterm 集成

`TerminalView` 拥有：

- `VTerm*`、`VTermScreen*`。
- 固定大小字符单元（等宽字体 metrics）。
- 滚屏历史环形数组（默认 5000 行，可配置）。
- 光标、选择、滚动条、主题、字体。

调用：

- `vterm_input_write()` 喂串口/尾字节。
- `vterm_screen_flush_damage()` 后合并重绘（约 60 fps，单次 `QTimer` 合并）。
- `vterm_screen_get_cell()` 读取单元格。
- damage/movecursor/settermprop/bell/resize/sb_pushline 回调。
- `sb_pushline` 把滚出屏幕的行复制进历史；历史只读，视图自己绘制。
- `vterm_screen_set_damage_merge(VTERM_DAMAGE_ROW)` 降低回调频率。

### 5.2 渲染

- 背景按行合并同色矩形，再按属性分段绘制文本；避免逐单元格画背景。
- 默认前景/背景、ANSI 0–15、256 色索引、24 位 RGB 全部支持。
- bold/italic/underline/double underline/strike/reverse/conceal 生效；blink 不闪烁，保持可读。
- 宽字符由 libvterm 标记 width=2，占两格，跳过 continuation 单元格。
- 光标为半透明块；失焦时只画轮廓。
- 滚屏：`scrollbar value == history.size()` 表示贴底；向上滚动显示历史，新输出到达时若原本贴底则自动贴底，否则保持当前位置。
- 选择：鼠标拖拽按单元格选区，Ctrl+Shift+C 复制；右键菜单复制/粘贴/全选/清屏。
- 粘贴：Ctrl+Shift+V，原样发送 UTF-8。
- 字体：系统等宽字体，默认 11 pt；Ctrl+滚轮缩放，保存到配置。

### 5.3 键盘与输入法

- Enter→CR，Backspace→DEL(0x7F)，Tab→TAB，Esc→ESC。
- 方向键/Home/End/Insert/Delete/PageUp/PageDown/F1–F12 发标准 xterm 序列。
- Ctrl+字母→对应 C0 控制码；Alt+字符→ESC 前缀。
- 可打印文本按 UTF-8 发送；`inputMethodEvent` 提交的 IME 字符串直接发送。
- Ctrl+Shift+C/V 是本地快捷键，不发给远端。
- 点击终端、切换标签、窗口激活时自动聚焦终端。

### 5.4 终端安全与性能

- libvterm 输入最多按 8 KiB 分块喂入；单次事件循环不超过一个块，避免大尾流卡死。
- 历史行数硬上限；超限丢最旧行。
- ZMODEM 尾流最多显示 4096 字节。
- 重绘用 `QTimer` 合并，串口高速输出时不会每个字节触发 paint。

## 6. ZMODEM 协议引擎

从现有 Python 引擎逐语义移植，保持与 `lrzsz` 已验证的行为一致。

### 6.1 模块

- `crc`：XMODEM CRC16（poly 0x1021，初值 0，线上大端）；IEEE CRC32（线上小端）。
- `codec`：hex/binary header、ZDLE 编解码、data subpacket、offset 打包。
- `reader`：带缓冲的头/subpacket 读取；8 连 CAN 检测；超时。
- `detector`：终端流中识别 ZRQINIT/ZRINIT。
- `filename`：UTF-8 文件名、路径清洗、Windows 保留名、重名临时路径。
- `sender`：ZFILE → ZRPOS → ZDATA 窗口 → ZEOF → ZFIN。
- `receiver`：ZRINIT → ZFILE → ZRPOS → ZDATA → ZEOF → ZFIN；多文件。
- `io.h`：阻塞字节 IO 抽象，worker 提供 QSerialPort 实现。

### 6.2 关键参数

- 1 KiB 发送窗口；1024 字节 chunk。
- 最小 ZDLE 转义（ZDLE/DLE/XON/XOFF/高位变体、`@` 后 CR）。
- 每窗口 ZCRCW，等 ACK 后再发下一窗口；ACK 超时 10 s。
- header/data 超时 30 s；同位置最多 5 次重传后中止。
- 支持 CRC16/CRC32；发送方按对端 ZRINIT CANFC32 选择。
- 发送结束 ZFIN 握手；接收结束 ZFIN + `OO`。
- 接收端把每文件写入独立临时文件；会话结束由 UI 逐个另存为；不静默覆盖。

### 6.3 文件语义

- 发送：用户多选文件；metadata 为 `basename\0size mtime mode 0 0 0`。
- 接收：清洗远端文件名，禁止路径穿越；空名/`.`/`..` 变 `download`；Windows 保留名加 `_`；重名临时文件加 ` (n)`。
- 另存为：每个文件弹一次保存对话框，默认最近目录 + 原文件名；用户取消则删除该临时文件；其他文件继续询问。
- 跨文件系统保存用“复制后删除临时文件”，不依赖 rename 原子性。

## 7. UI

### 7.1 主窗口

- `QTabWidget` 多会话标签，标签可移动、可关闭，右上角 `+` 新会话。
- 菜单：File（New Tab、Close Tab、Quit）、View（Dark Theme、Increase/Decrease Font）、Help（About）。
- 窗口标题 `COMTool Native`，激活标签可附带端口名。
- 每个标签左侧是串口面板，可折叠；右侧终端。
- 标签切换自动聚焦终端；关闭有活动传输的标签先确认。

### 7.2 串口面板

- 端口：可编辑下拉框 + 刷新按钮；热插拔自动更新，不覆盖用户手工输入。
- 波特率：可编辑下拉（常用 9600–3000000）。
- 数据位、校验、停止位、流控：完整 QSerialPort 选项。
- Open/Close 按钮；状态文字（Disconnected/Open/Error）。
- 打开后参数控件禁用；关闭后恢复。修改参数需先关闭再打开，避免运行中切换造成半配置状态。

### 7.3 传输条

- 终端下方隐藏式传输条：文件名、百分比/字节、进度条、Cancel。
- 发送多文件显示 “file i/n”；接收多文件逐个更新。
- 未知总大小时使用忙碌进度条。
- 完成后状态保留数秒并显示文件数；错误用状态文字 + 消息框（仅协议错误）。

### 7.4 主题与语言

- 浅色默认；深色切换立即生效，保存配置。
- 终端 ANSI 调色板按主题调整，默认前景/背景跟随主题。
- v1 使用内置轻量中英文字符串表，跟随 `QLocale::system().language()`；不依赖 `.qm` 文件。后续可迁移 Qt Linguist。
- 动态状态文本允许英文文件名/路径原样显示。

## 8. 配置

路径（显式构造，避免 Qt 组织名差异）：

- Linux：`~/.config/comtool-native/config.ini`
- Windows：`%APPDATA%\comtool-native\config.ini`

保存键：

- `window/geometry`、`window/state`
- `ui/darkTheme`、`ui/fontPointSize`、`ui/scrollback`
- `ui/panelVisible`
- `tabs/count`、`tabs/<i>/{port,baud,dataBits,parity,stopBits,flow}`
- `files/lastDir`、`files/zmodemLastDir`

启动时恢复标签数量与参数，但所有标签都是关闭状态，不自动打开串口。首次启动创建一个空标签。

## 9. 错误处理

| 场景 | 行为 |
|---|---|
| 端口打开失败 | 状态显示错误、消息框；控件恢复；worker 退出 |
| 运行中端口消失/IO 错误 | 标记断开、关闭端口、保留终端内容；不自动重连 |
| ZMODEM 超时/CRC/协议错误 | 清空缓冲、临时文件删除、状态/消息框；终端恢复 |
| 发送方文件读取失败 | 中止传输，通知对端，显示错误 |
| 接收另存为失败 | 保留临时文件并提示路径；继续处理下一个文件 |
| 取消 | 写 CAN、清队列、恢复终端；不弹错误框 |
| 关闭有传输的标签/窗口 | 先确认；取消后等待 worker 退出 |

所有跨线程异常都转换为 worker 的 error signal；UI 不直接触碰串口对象。

## 10. 构建与部署

### 10.1 Linux

```bash
cmake -S native -B build-native -G Ninja \
  -DCMAKE_PREFIX_PATH=/path/to/Qt/5.15.2/gcc_64
cmake --build build-native
./build-native/comtool-native
```

依赖：Qt 5.15 Widgets、Qt 5 SerialPort、CMake ≥3.16、C++17 编译器。libvterm 源码自带。

### 10.2 Windows

- Qt 5.15.2 MinGW 64-bit（或 MSVC 2019 + Qt 同 ABI）。
- CMake + Ninja；`windeployqt` 收集 Qt DLL。
- v1 交付 onedir 目录；NSIS 安装包和签名后续补。
- Linux AppImage 后续补；v1 先保证可执行文件与依赖清单。

### 10.3 启动性能目标

- Linux 冷启动到窗口可交互 ≤0.3 s。
- Windows 冷启动到窗口可交互 ≤0.5 s。
- 不使用启动画面；不预创建多余标签；不加载网络/插件/图表。
- 提供 `--benchmark-startup`：窗口 show 后记录从 `main()` 起的时间，打印并退出，供 CI/手工测量。

## 11. 测试与验收

### 11.1 单元/协议

- CRC16/CRC32 已知向量。
- ZDLE 编解码往返、最小转义、XON/XOFF 忽略。
- header 编解码、损坏 CRC、截断输入。
- subpacket 解析、坏 CRC、跨块输入。
- detector：普通文本、分块 header、ZRQINIT/ZRINIT、非会话头。
- filename 清洗/保留名/重名。

### 11.2 互通

- `zmodem_peer` 在 PTY 一侧，`lrz`/`lsz` 在另一侧：
  - peer 发送单文件/多文件/空文件/二进制文件，`rz` 接收并比对 SHA256。
  - peer 接收单文件/多文件，`sz` 发送并比对 SHA256。
  - 262144 字节双向基准，记录 wire bytes 与重传。
- GUI 端到端：Xvfb + socat PTY + bash；终端收发、窗口尺寸变化、标签切换、手动输入 PTY 路径。
- 真实板端 `rz`/`sz` 双向；4 MiB 文件 SHA256；取消与超时。

### 11.3 GUI 手工清单

- 冷启动计时；无启动画面。
- 端口下拉热插拔；手工输入 `/dev/pts/N`。
- 中文/宽字符/颜色/vim/less 显示；IME；复制粘贴；滚动历史。
- 多标签独立串口；关闭标签；恢复标签不自动打开。
- 浅色/深色切换；字体缩放；窗口几何恢复。
- ZMODEM 发送多文件、接收多文件、另存为取消、传输中取消、端口拔出。

## 12. 风险与取舍

- **libvterm 0.3.3 与 Qt 5.15 的编译兼容**：C99 + 独立静态库，风险低；若 Windows MSVC 有 C99 问题，用 MinGW 构建。
- **串口线程同步**：ZMODEM 需要背压，worker 同步写并等 `bytesWritten`；不做无界异步队列。
- **UI 文件对话框阻塞 worker**：发送选文件期间 worker 暂停；远端重复 ZRINIT 在串口缓冲中，选完后仍可继续。若用户长时间不选，可能丢重复头，但 sender 使用已识别的 initial header，不依赖新头。
- **接收临时目录清理**：UI 保存/取消后删除；应用崩溃/强退可能留下系统临时目录，属可接受残留，README 说明。
- **高波特率终端吞吐**：libvterm + 合并重绘足够；极端无限流输出仍可能积压，必要时后续加输入限流。
- **v1 语言表非标准翻译流程**：先保证中英可用，后续迁移 `.ts/.qm`。

## 13. 里程碑

1. 工程骨架、libvterm、CMake、空窗口启动。
2. ZMODEM codec/reader/detector + 单元测试。
3. 终端 libvterm 渲染/输入/历史。
4. SerialWorker + 端口面板 + 多标签。
5. sender/receiver + 进度/取消/另存为。
6. PTY/lrzsz 互通、GUI 验收、启动计时。
7. Windows 构建/打包与最终文档。
