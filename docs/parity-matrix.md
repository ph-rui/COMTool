# COMTool Avalonia 功能对齐清单

- 目标：Avalonia 版本与 Python/PyQt5 版本一比一复刻
- 基线：`7ae55a5`
- 状态：`未开始` / `部分` / `完成` / `不适用`
- 测试门禁矩阵：`docs/testing/feature-test-matrix.md`（自动化测试与人工验收登记；功能状态仍以本表为准）
- 只有 P0、P1 全部完成，且没有阻塞级 bug，才允许切换默认版本。

## 1. 主窗口与通用 UI

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| UI-01 | 自定义标题栏、Logo、窗口按钮 | P0 | 部分 | 无原生边框/白条，Material Design 最小化/最大化/还原/关闭图标，自定义 5px 边缘缩放与光标，拖拽/最小化/最大化/关闭，标题栏双击最大化/还原已自动化并由 Xvfb E2E 验证，Xvfb 基准截图通过；Windows 白条、拖拽和边缘缩放实机确认待用户启动新 exe |
| UI-02 | 左侧 `User sessions` 侧栏 | P0 | 完成 | 2026-10-23 按用户要求改为 MobaXterm 式固定停靠：侧栏常驻，不再随指针悬停自动展开/收起，左边缘浮动把手已删除；仅标题栏“会话”按钮手动收起/展开，收起后不占宽度，宽度可拖动调整，可见性与宽度跨重启恢复；`+` 新建会话菜单展开期间不收起；点击会话后该行立即显示蓝色 `Connecting...`，连接期间重复点击被忽略，成功/失败/取消后清除；见 `session-sidebar-expanded-on-launch-1000x650.png` |
| UI-03 | 串口/SSH/Telnet 分组与状态图标 | P0 | 部分 | 连接状态原地更新；Windows `--ports` 已输出 COM97/99 等 SetupAPI 描述 |
| UI-04 | 侧栏右键菜单 | P0 | 部分 | Open、Edit session、Remove session；Edit session 可在未打开连接时回填已保存主机/端口/用户；确认框、真实桌面右键和配置持久化仍需核对 |
| UI-05 | 标签页与标题 | P0 | 完成 | 多标签新增/关闭/切换、标题规则、选中样式、状态圆点和双击分离/回接已完成；分离后命令可继续执行，关闭分离窗口无重复缓冲；切换标签前对旧终端调用 `BeginReparent`，避免内容宿主移除旧控件时清理 PTY 读取器，切换后直接输入即可继续执行，两个方向均已在打包版实测；见 `tab-switch-input-second-1000x650.png`、`tab-switch-input-first-1000x650.png` |
| UI-06 | `+` 新建会话类型选择器 | P0 | 完成（MobaXterm 方向） | 按用户要求移除 Python 插件占位项（Send Receive/protocol/Graph/Load plugin）；空状态和侧栏标题两个 `+` 入口统一为 Serial/SSH/Telnet/TCP/Local terminal，选择后预选对应设置页或创建本地终端；菜单项使用与 MobaXterm 相近的类型图标（串口插头、SSH 服务器、Telnet 终端、TCP 网络、本地桌面），标题栏/窗口图标为深蓝终端图标；右上角 Functional 箭头按钮及 `Load config` / `Share config` 面板已按用户要求删除；Telnet 已作为 MobaXterm 会话类型接入，后续类型按阶段扩展，未实现类型不显示 |
| UI-07 | 空状态 | P0 | 完成 | 无标签时显示 `Select a session from the left` / `or click + to add a session`，空状态和侧栏标题两个 `+` 入口提示 `New session`；headless 测试覆盖 |
| UI-08 | 设置按钮与设置面板 | P0 | 完成 | 串口/SSH/Telnet 参数、SSH 密钥、记住密码、打开/关闭、侧栏同步已实现；2026-10-22 按用户要求去掉标签页内的连接设置齿轮（标签只留状态点+标题+关闭按钮），连接设置改由侧栏右键 `Edit session` 进入；齿轮时代的截图在 fixtures README 中标为 Historical |
| UI-09 | 主题切换（浅色/深色） | P0 | 部分 | 标题栏右键菜单可切换浅/深色，`skin` 持久化并重启生效 |
| UI-10 | 语言切换 | P1 | 未开始 | 英文/简中/繁中/日文，文案一致 |
| UI-11 | 帮助、关于、捐赠、更新提示 | P1 | 部分 | `About COMTool` 已包含版本、说明、配置路径、License、GitHub/Releases/Issues/捐赠链接；插件帮助和更新检测待补 |
| UI-12 | 状态栏、错误/成功提示 | P0 | 部分 | 连接/断线/ZMODEM 进度/取消/成功/错误颜色和 `Connection lose` 实测；2026-10-20 Windows 实测 ZMODEM 22% 时进度文字可读、文件名不再被进度条覆盖；Python 无状态超时自动清除 |
| UI-13 | 快捷键与鼠标行为 | P0 | 部分 | 终端选择/右键复制粘贴、Ctrl+V/Ctrl+Shift+V、终端功能键、编码下拉关闭后回焦终端、点击 SSH 会话后自动聚焦新终端（本地终端同样验证，串口共用同一附加路径，无需再点终端区域）、标题栏/工具栏按钮不抢焦点、设置/侧栏按钮图标居中、F11 全屏（标题栏整行收起、内容贴顶、再按恢复）、双击终端面板最大化/还原且不丢焦点均已验证；插件编辑器内部快捷键随插件另行实现 |
| UI-14 | 高 DPI | P1 | 部分 | Linux X11 `Xft.dpi=144`（150%）和 192（200%，2560x1600）下窗口、侧栏、标签、编码下拉和终端均无裁切；Windows 150%/200% 待测 |
| UI-15 | 窗口尺寸/位置/分栏恢复 | P1 | 完成（Avalonia 增强） | Python 每次以 850x500 居中启动，不持久化窗口位置；Avalonia 按用户要求新增窗口尺寸记忆：关闭时保存 `window.width/height`，启动时先应用再居中显示；最大化/最小化/全屏关闭时保存最后一次普通窗口尺寸，不保存位置或最大化状态。侧栏宽度会恢复；可见性按 UI-02 在每次启动时强制展开 |
| UI-16 | 侧栏 MobaXterm 式图标栏（三格 + SSH 浏览器） | P1 | 完成（Avalonia 增强） | 2026-10-24 按 MobaXterm 22.1 实机修正：24px 图标栏固定三格（会话/工具/宏，工具图标为螺丝刀+扳手 `f7d9`），按用户要求删除历史页；SSH 登录成功后动态出现第四格橙色地球（`f57d`，提示"SSH 浏览器（SFTP）"），点击展开当前 SSH 会话的 SFTP 面板（当前标签不是 SSH 时先切到最近的已连接 SSH 标签），SSH 全部断开/关闭后隐藏；选中白底、悬停浅底、中文提示；面板标题随页签切换（User sessions/Tools/Macros），头部动作按钮随页签变化（+新建会话/+新建宏）；工具页按用户要求收敛为三项独立窗口工具（网络服务器/网络扫描仪/端口扫描仪，彩色图标、全部可用，无分组标题），点选即打开无 Owner 的独立窗口（可被主窗口盖住），重复点选只激活已打开的窗口，关闭后可重新打开，主窗口关闭时一并关闭；新增工具只需在 `ToolCatalog` 增加一条 descriptor 和一个窗口类，菜单/侧栏/MainWindow 无需改动；会话列表左侧 16px 空白已去除；侧栏宽度 160–320 拖拽与 UI-02 行为不变，折叠时图标栏随面板隐藏；菜单三项、开窗/单例与 SSH 图标显隐由 headless 测试覆盖，Windows 实机外观 WIN-26 核对 |

## 2. 终端

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| TERM-01 | ANSI/VT100 基础序列 | P0 | 完成 | 换行、自动换行、制表、清行/清屏、光标定位与滚动均由 pyte 对照 golden 测试覆盖 |
| TERM-02 | 256 色/真彩色 | P0 | 完成 | 256 色调色板与前景/背景实测通过；基础 16 色改为 Python 主题（blue `#2196f3`、bright blue `#6cb6ff`），ANSI 蓝/亮蓝在 `#212121` 背景上的 WCAG 对比度分别为 5.15/7.49，避免提示符和目录名近乎不可读；Windows 实测见 `docs/fixtures/phase1/terminal-palette-windows-1280x800.png` |
| TERM-03 | 自动换行、窗口 resize | P0 | 完成 | 窗口 resize 后 `stty size` 同步变化 |
| TERM-04 | 滚动回看 | P0 | 完成 | 历史缓冲、滚轮/PageUp/PageDown 已实现；滚动条按 Python 隐藏 |
| TERM-05 | 选择与复制 | P0 | 完成 | 左键拖选、右键复制、高亮已验证 |
| TERM-06 | 粘贴 | P0 | 完成 | 右键、Ctrl+V/Ctrl+Shift+V、中文已验证 |
| TERM-07 | MobaXterm 关键字高亮 | P0 | 完成 | 错误红、成功绿、警告黄、URL 下划线、ANSI 优先；流式高亮测试通过 |
| TERM-08 | 光标显示 | P0 | 完成 | 光标颜色/位置正常，隐藏/显示和宽字符推进已通过单元测试 |
| TERM-09 | 字体/字号配置 | P1 | 部分 | Python 固定 15px；Avalonia 同样固定 15px；2026-10-23 起终端字体为内置 Noto Sans Mono CJK SC（拉丁半宽 + CJK 全宽同一字网格，修复中英文混排基线高低不一），resize/分离渲染正常 |
| TERM-10 | 终端输入映射 | P0 | 部分 | 文本、Enter、Ctrl+C/V、方向键、Home/End、Insert/Delete、F1-F12 已通过 `cat -v` 实测 |
| TERM-11 | 断线/重连显示 | P0 | 完成 | SSH 通道 `Closed`/错误事件立即置为断开；主窗口和分离窗口均显示断线，侧栏圆点变灰；无存活连接时点击会话关闭死标签并用缓存凭据重连且不重复提示；已有存活连接时点击会话另开连接；shell 退出或标签关闭后终端布局再次 resize 不再访问已释放的 `ShellStream`，避免 UI 线程未处理异常导致软件退出 |
| TERM-12 | ZMODEM 字节流接入 | P0 | 完成 | 自动检测、流路由、发送/接收引擎、保存对话框已接入 |
| TERM-13 | 大流量性能 | P0 | 完成 | C# 解析 1MB 约 108ms、10MB 约 240ms；Python/pyte 同载荷约 616ms/5.0s；本地 SSH 10MB 端到端 C# 约 1.0-1.24s、Python 约 7.55-9.26s，见 `performance-2026-10-20.md` |
| TERM-14 | 交互输入即时回显 | P0 | 完成 | Moba 关键字高亮不再无限期扣留可能是关键字前缀的尾字符；`i` 等单字符在 SSH 输入后立即显示，15ms 内无后续分片时释放，跨分片关键字仍可高亮；见 `terminal-single-char-echo-1000x650.png` |
| TERM-15 | 双击终端面板最大化/还原 | P0 | 完成 | 双击黑色终端面板与标题栏最大化按钮行为一致；使用 `DoubleTapped` 并监听已处理事件，不干扰终端词选择；最大化到 1280x800 后输入 `echo FINAL_PACKAGE_OK` 无需重新聚焦，再次双击恢复 1000x650；见 `terminal-double-click-maximize-1280x800.png`、`terminal-double-click-restore-1000x650.png` |

## 3. 串口

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| SER-01 | 端口枚举与描述 | P0 | 完成 | Windows 实机输出 `COM23 USB-SERIAL CH340`、`COM97 USB-Enhanced-SERIAL-B CH344` 等描述，自然排序正确 |
| SER-02 | 自然排序 | P0 | 完成 | COM2 < COM10，`/dev/ttyUSB2` < `ttyUSB10` |
| SER-03 | 热插拔刷新 | P0 | 部分 | UI 每 2 秒扫描；成功扫描后侧栏按可用端口显示，拔除隐藏但保留会话，重新插入自动恢复；硬件热插拔待实测 |
| SER-04 | 波特率/数据位/校验/停止位 | P0 | 完成 | 映射与可编辑自定义波特率设置页已实现；串口侧栏在设备描述下显示 `115200 8N1` 摘要，非默认流控追加 `· RTS/CTS` / `· XON/XOFF` |
| SER-05 | 流控/RTS/DTR | P0 | 部分 | RTS/CTS、XON/XOFF 与 RTS/DTR 引脚映射已实现；`System.IO.Ports` 不提供 DSR/DTR 流控，已避免错误映射为 RTS/CTS+XON/XOFF，原生实现待补 |
| SER-06 | 打开/关闭/占用错误 | P0 | 部分 | Windows COM99/97/23 打开通过；错误对话框统一为 `Error` + `Open Failed`/`Connect Failed`，SSH 拒绝连接已实测；Windows 串口占用场景待硬件 |
| SER-07 | 收发数据 | P0 | 部分 | Linux PTY 收发通过；打开串口时与 Python 一致清空驱动残留收发缓冲区（防止上次 ZMODEM/串口二进制数据被当成新数据读出），失败回归测试先复现后修复；Windows COM99 以 `115200 8N1` 打开后显示干净的 `root@imx95evk:~/depth#` 提示符，见 `docs/fixtures/phase1/serial-clean-prompt-windows-after-buffer-fix.png`；Windows 回环收发待硬件 |
| SER-08 | 自动重连 | P1 | 完成 | 打开中的串口标签在设备拔除后标记 `Connection lose`，2 秒轮询发现同一 COM 重新插入后自动重开并恢复标签；失败自动重试且不弹错误框；手动关闭标签不会重开；硬件拔插见 WIN-03 |
| SER-09 | 会话保存与恢复 | P0 | 部分 | 侧栏/标签生命周期已接入；Windows 配置内串口已 headless 打开，UI 收发待硬件 |
| SER-10 | 设备消失保留与恢复 | P0 | 部分 | 成功扫描后不再删除未连接会话；侧栏隐藏不可用端口，重新插入后恢复；硬件消失场景待实测 |

## 4. SSH

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| SSH-01 | 主机/端口/用户/别名 | P0 | 完成 | 主机/端口/用户/别名、侧栏会话和设置页均已接入 |
| SSH-02 | 密码认证 | P0 | 部分 | 密码提示、3 次重试、设置页、重开缓存均已验证 |
| SSH-03 | 密钥认证 | P0 | 完成 | 密钥连接与设置页文件选择已实现 |
| SSH-04 | 记住密码（DPAPI） | P0 | 完成 | 设置页记住密码、DPAPI 加密保存、Windows 解密连接已验证 |
| SSH-05 | 密码缓存重连 | P0 | 完成 | 关闭标签后再次点击不重复提示（同一进程） |
| SSH-06 | 会话侧栏保存/点击打开 | P0 | 完成 | 按用户要求，同一 SSH 会话已有存活连接时再次点击会新增独立连接（本地实测两个并发 sshd 会话），不再聚焦旧标签；无存活连接时清理死标签后重连；串口仍保持单连接去重；别名、点击自动连接已验证；见 `ssh-multiple-connections-1000x650.png` |
| SSH-07 | 断线重连 | P1 | 不适用 | Python SSH 的 `auto_reconnect` 默认 false 且设置页未暴露；Avalonia 手动点击会话重连、密码缓存不重复提示已验证 |
| SSH-08 | PTY 尺寸 | P0 | 完成 | Linux `stty size` = 40x100 验证通过 |
| SSH-09 | Shell 输出与终端 | P0 | 完成 | SSH shell 已接入多标签终端并通过 UI 验证 |
| SSH-10 | 连接速度 | P0 | 部分 | 首次 187-446ms，重连 33ms |
| SSH-11 | 停止会话界面 | P1 | 完成 | Shell 退出后显示 MobaXterm 风格 “Session stopped”；Enter 关闭标签、R 在同一标签内重连、S 保存完整回滚输出；重连进行中状态行显示不定长 “Restarting...” 进度条，连接失败后弹 “重连失败” 对话框询问 继续重启/不重连；TCP/串口保持原退出提示；Telnet 使用同一停止界面与动作（见 TELNET-06）；由 headless 测试（含进度条状态与对话框两分支）和 Linux Xvfb `SSH11_RestartStoppedSessionE2E` 覆盖 |

## 5. Telnet

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| TELNET-01 | 主机/端口/用户/别名 | P0 | 完成 | 设置页默认端口 23，保存 `telnet:{user}@{host}:{port}`，侧栏 Telnet 分组和连接状态；用户可留空以手动登录 |
| TELNET-02 | Telnet 协议协商 | P0 | 完成 | IAC 解析与转义、TTYPE `XTERM-256COLOR`、NAWS 及 resize 更新、SGA/ECHO/BINARY 接受、未知选项/LINEMODE 拒绝；Linux 回环测试覆盖 |
| TELNET-03 | 自动登录 | P0 | 完成 | 去掉 ANSI 后匹配末尾 `login`/`username`/`user`/`User name`/`Login name`/`Login ID`/`Account name`/中文账号提示、`Please enter …` 前缀、`>>` 前缀及主机名前缀（如 `SonnePower_070_Ax login:`），密码支持 `password`/`passwd`/`passphrase`/`passcode`/`PIN`/`Password for …`/中文密码提示，用户名/密码各发送一次；无匹配提示不发送；Linux Xvfb E2E 验证用户名自动登录和终端输入；Windows RDP 实机验证用户名/密码分阶段发送和 `PING`→`PONG` |
| TELNET-04 | 会话保存与记住密码 | P0 | 完成 | 独立 `telnetSessions`、稳定 key、`passwd_enc` 持久化和内存缓存；非 Windows 仅内存；Windows 10 实机保存密码后 `passwd_enc` 长度 208、配置无明文，重启应用后由 DPAPI 密码自动重连成功；证据见 `docs/fixtures/phase1/telnet-windows-dpapi-2026-10-21.md` |
| TELNET-05 | 分享/导入配置 | P1 | 完成 | 分享去除 `passwd_enc` 和未知字段，导入不写入密码；不读取 Python `config.json` |
| TELNET-06 | 断连停止界面与 R 重连 | P1 | 完成 | 连接断开后标签进入与 SSH 相同的 MobaXterm 风格停止界面（状态 `Session stopped`），Enter 关闭标签、R 在同一标签内重连、S 保存完整回滚输出；重连复用已保存的主机/端口/用户与密码（内存缓存 → DPAPI `passwd_enc`），没有已存密码时直接以无自动登录方式重连，服务器提示留在终端里由用户手输，不弹窗；重连失败时状态显示 `Restart failed: …` 并弹 “重连失败” 对话框（与 SSH 相同，继续重启/不重连），不重连保留停止界面可再次按 R；由 headless 测试和 Linux Xvfb `TELNET02_RestartStoppedSessionE2E` 覆盖；见 `telnet-stopped-screen-1000x650.png`、`telnet-restarted-after-r-1000x650.png` |

## 6. SFTP

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| SFTP-01 | SSH 标签内嵌 SFTP 面板 | P1 | 完成 | 仅 SSH 标签左侧竖向面板，默认收起（终端独占标签），标签栏编码下拉框左侧按钮切换，折叠状态与宽度（默认 420，可拖拽 320-640）按会话保存；第一次展开才建立 SFTP 通道，失败显示 `SFTP unavailable: <原因>` 与 `Retry`；不是新的会话类型，串口/Telnet/TCP/本地终端标签不受影响；由 headless 布局测试和 Linux Xvfb `SFTP01_BrowserTransferE2E` 覆盖 |
| SFTP-02 | 单栏浏览 | P1 | 完成 | 面板内只有一栏远程列表（MobaXterm 风格）：纯图标工具栏（悬停显示中文提示）、远程路径框、名称/大小 (KB)/最后修改/所有权限 可点排序表头（目录恒置顶）、双击进入目录；没有本地栏，本地一侧由传输时的系统选择对话框给出 |
| SFTP-03 | 上传与下载 | P1 | 完成 | 文件与目录递归双向传输，字节一致；上传按钮弹系统文件选择对话框、下载按钮弹系统文件夹选择对话框（取消即不做）；目录复制进入目标目录内部，传输前先统计文件数与总字节 |
| SFTP-04 | 冲突处理 | P1 | 完成 | 每次冲突询问，Overwrite / Skip / Rename / Cancel 四个决定，建议名 `name (1).ext`；`Apply to all` 作用于本次传输、`Never ask again` 作用于本会话；v1 不含续传与“仅较新覆盖” |
| SFTP-05 | 远程文件管理 | P1 | 完成 | 新建目录、重命名（面板内联输入行，确定/取消）、删除（图标变红，两步确认）、chmod 只作用于远程；单栏布局没有本地栏，也就没有本地破坏性命令；chmod 失败显示 `chmod failed: <原因>`，成功显示八进制权限 |
| SFTP-06 | 符号链接与递归限制 | P2 | 完成 | 默认跟随链接并可关闭；递归上限 16 层与 100000 个文件，跳过的文件逐个记入结果 |
| SFTP-07 | 进度、取消与错误 | P1 | 完成 | 进度显示文件数与字节总量；取消删除未完成的文件；一个文件失败不终止队列，状态行给出原因并保留可重试 |
| SFTP-08 | Windows 实机 SFTP 与视觉 | P2 | 部分 | 见 WIN-23 |

## 7. TCP/UDP

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| NET-01 | TCP client/server | P1 | 完成 | 客户端连接/收发/断开、服务端监听/多客户端/选一个或全部发送/断开、标签关闭清理 |
| NET-02 | UDP | P1 | 完成 | 绑定本地端口、目标地址收发、断开清理、TCP/UDP 模式切换 |
| NET-03 | 配置与会话 | P1 | 完成 | 模式/目标/端口字段、目标与端口错误提示、侧栏 TCP 会话、标签标题和连接状态 |
| NET-04 | 自动重连 | P1 | 完成 | TCP 客户端断线后按间隔自动重连，状态提示和端子保持 |
| NET-05 | 网络扫描仪（独立窗口） | P1 | 完成（Avalonia 增强） | 2026-10-23 按用户要求 1:1 对标 MobaXterm v22.1 实测"网络扫描"对话框，并从侧栏工具页改为独立窗口（工具 → 网络扫描仪）：表单为"IP 地址范围"五段输入（前三段固定 + 末段范围 [1]-->[254]，默认 192.168.1.1→254，无本机网段自动探测）；Start scan / 停止按钮；扫描只探测固定 7 个服务端口（22/3389/5900/21/23/513/80），结果为固定 9 列表格：IP 地址/名称/SSH/RDP/VNC/FTP/Telnet/Rlogin/HTTP（开放绿勾/关闭灰叉，名称列反向 DNS 尽力而为）；扫描中可停止并保留已发现结果，状态显示“已停止（已发现 N 台）”；IP 输入框与结果列对齐，表头与数据行共用固定列宽；"关闭"按钮或关窗终止扫描（对齐 MobaXterm 关窗即停扫）；五段解析、服务表、回环端口发现、停止/取消由单元测试覆盖，窗口接线/9 列网格/停止/数值对齐/关闭取消由 headless 测试覆盖（可注入扫描器），Windows 实机局域网扫描 WIN-26 核对 |
| NET-06 | 端口扫描仪（独立窗口） | P1 | 完成（Avalonia 增强） | 2026-10-23 按用户要求 1:1 对标 MobaXterm v22.1 实测"端口扫描仪"对话框，并从侧栏工具页改为独立窗口（工具 → 端口扫描仪）：窗口标题与实测一致为"网络扫描"；表单为"IP 地址"严格点分 IPv4 单输入框 + 单选"仅扫描已知端口"（默认，35 项 IANA 风格服务表，含实测出现端口 22/123/139/445/3389/993/995/6000，完整已知端口表未实测属合理假设）/"扫描端口范围 [1]-->[65535]"（原"深度扫描"功能归入此模式，范围框仅该模式可用）；Start scan + 停止按钮，进度"正在扫描 {ip}：N/total"；结果行格式 "- Port #22 (ssh): listening"（未知端口不带服务名），升序；"关闭"或关窗终止扫描；IP 校验、双模式、进度/停止、结果格式由 headless 测试覆盖（可注入扫描器），回环深扫窗口与已知端口列表探测由单元测试覆盖，Windows 实机扫描 WIN-26 核对 |

## 8. 工具窗口与网络服务器

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| SRV-01 | 网络服务器（Servers management） | P1 | 完成（Avalonia 增强） | 2026-10-23 按用户要求 1:1 对标 MobaXterm v22.1 实测 "Servers management" 对话框，作为工具页"网络服务器"的独立窗口：左列固定 9 行（TFTP/FTP/HTTP/SSH-SFTP/Telnet/NFS/VNC/Cron/Iperf），行高 40，每行 ▶/■/⚙ 三个 23×20 按钮；本轮 HTTP 与 TFTP 真实现，其余 7 行保留但置灰（悬停提示"暂不支持（框架已预留）"）；右侧 410×301 白底面板在欢迎页（红色"网络服务器" + "欢迎使用 / MobaXterm 网络服务器" + 分子球图形）、HTTP 配置（根目录+浏览、允许列出文件夹内容、监听端口 80、服务器运行时间 3600 秒、服务器输出日志）与 TFTP 配置（根目录、下载/上传成功后显示信息、端口 69、运行时间、日志）之间切换；日志行格式 `[HH:mm:ss] message`，运行中行尾显示 `(N)` 倒计时，到时自动停止；端口占用在日志中报错并保持停止；关窗停止全部监听；端口/运行秒数输入框统一 120px 且数值居中；工具窗无 Owner、可被主窗口盖住，主窗口关闭时一并关闭。HTTP 服务基于原始 TcpListener（不用 HttpListener，免管理员 URL ACL），支持 GET/HEAD、目录列表开关与路径穿越防护；TFTP 为 RFC 1350 RRQ/WRQ、512 字节块、1 秒 ACK 超时重传 3 次、根目录外文件拒绝；服务器配置（端口/根目录/选项/运行时长）持久化到 config.json，密码仍只存 DPAPI `passwd_enc`；HTTP/TFTP 引擎、配置往返、9 行/配置切换/启停/失败日志/关窗停止、端口/秒数数值对齐由自动化测试覆盖，Windows 实机 UI 与真实传输由 WIN-26 核对 |
## 9. ZMODEM

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| ZM-01 | 自动识别 `rz`/`sz` | P0 | 完成 | 检测器已接入 `ZmodemStream`，自动切换收发会话 |
| ZM-02 | 接收文件 | P0 | 完成 | 接收引擎、临时目录、重名处理、保存对话框已实现；真实 lrzsz `sz` 64KB 文件对传通过 |
| ZM-03 | 发送文件 | P0 | 完成 | 发送引擎、文件选择、多文件已实现；真实 lrzsz `rz` 64KB 文件对传通过 |
| ZM-04 | 进度/速度/剩余时间 | P0 | 部分 | 状态栏显示文件名、百分比、实时速率和 ETA |
| ZM-05 | 取消/错误/超时 | P0 | 完成 | 取消按钮、ZCAN、错误/超时状态、临时文件清理和输入/提示符恢复已实测 |
| ZM-06 | 与 lrzsz 兼容 | P0 | 完成 | 模拟测试和真实 lrzsz 0.12.21rc 双向 64KB 二进制对传通过 |
| ZM-07 | 大文件/长时间传输 | P1 | 部分 | 本地 SSH 下 200MB `sz` 接收和取消已实测，输入/提示符恢复；长时间重复传输的内存曲线待补 |

## 10. 插件与协议

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| PLG-01 | 插件选择器与生命周期 | P1 | 未开始 | 添加、关闭、配置保存 |
| PLG-02 | terminal 插件 | P0 | 完成 | 内置终端功能对齐（颜色/输入/剪贴板/滚动/ZMODEM/编码）；不加载 Python 插件文件 |
| PLG-03 | Graph 插件 | P1 | 未开始 | 曲线、缩放、数据源、主题 |
| PLG-04 | protocol 插件 | P1 | 未开始 | 协议选择、发送、解析 |
| PLG-05 | debug 插件 | P1 | 未开始 | 断点、变量、日志等现有功能 |
| PLG-06 | 外部 Python 插件 | 未决 | 未开始 | 是否需要二进制兼容待确认 |
| PLG-07 | 插件 API 文档 | P1 | 未开始 | C# 插件接口、示例 |

## 11. 配置、会话与平台

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| CFG-01 | `config.json` 字段兼容 | P0 | 完成 | schema、默认值、Python 单向导入和未知字段保留已测试 |
| CFG-02 | 串口会话存储 | P0 | 完成 | 归一化去重、排序、描述和未连接会话保留已实现并测试 |
| CFG-03 | SSH 会话存储 | P0 | 完成 | key、别名、`passwd_enc`、迁移和独立副本已测试 |
| CFG-04 | 配置导入 | P0 | 完成 | 单向导入、不覆盖、备份、未知字段保留测试通过 |
| CFG-05 | 日志 | P0 | 完成 | `%APPDATA%/COMTool-Avalonia/run.log`，2MB 滚动，异常记录 |
| CFG-06 | Windows DPAPI | P0 | 完成 | 设置页记住密码、DPAPI 加密保存、Windows headless 解密连接通过 |
| CFG-07 | 单实例/多开 | P1 | 未开始 | 与 Python 行为一致 |
| CFG-08 | 自动更新 | P1 | 未开始 | 独立通道，不覆盖稳定版 |
| MAC-01 | 宏（侧栏宏页） | P1 | 完成（Avalonia 增强） | 2026-10-23 按用户要求对标 MobaXterm 宏：全局宏列表（名称/命令/发送后回车），内联编辑面板增删改，发送 = `SendInputAsync` 写入当前活动标签终端（回车可选），无活动终端时发送按钮禁用；配置字段 `macros: [{id,name,text,appendEnter}]`，增删改即持久化，旧配置加载为空列表；CRUD、持久化、发送字节（含 `\r` 断言）、无终端禁用由 headless 测试覆盖 |

## 12. 打包与发布

| ID | 功能 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| PKG-01 | Windows 自包含包 | P0 | 完成 | `win-x64` 单文件自包含启动；应用图标已嵌入 |
| PKG-02 | Linux 包 | P1 | 部分 | 自包含 `linux-x64` tar + per-user install/uninstall 已实测（X11）；Wayland 与 AppImage 待验证 |
| PKG-03 | macOS 包 | P1 | 未开始 | arm64/x64、签名、公证 |
| PKG-04 | 安装/卸载/升级 | P0 | 部分 | Windows per-user 安装/升级/卸载实测；Linux per-user 安装/升级/卸载实测，均保留 Python 配置和 Beta 配置 |
| PKG-05 | 包体积/启动时间 | P1 | 部分 | R2R + 不压缩：Windows zip 68.9MiB / 单文件 167.4MiB，Linux tar 66.3MiB；Linux 同机 warm start 0.70s vs Python 0.60s（原压缩包 1.54s），见 `performance-2026-10-20.md` |
| PKG-06 | CI 构建 | P0 | 部分 | 已有三平台 build/test、Linux real-lrzsz、Windows 打包上传，并新增 Linux 包上传；CI 尚未实际跑通 |

## 13. 测试与验收

| ID | 项目 | 优先级 | 状态 | 验收要点 |
|---|---|---|---|---|
| TST-01 | 单元测试 | P0 | 完成 | 348 项通过（Core 47/Connections 66/Terminal 11/ZMODEM 32/TestGate 49/App 143） |
| TST-02 | 终端 golden 测试 | P0 | 完成 | 同一字节流与 Python pyte 0.8.2（60x20、LNM）屏幕行/光标快照一致 |
| TST-03 | ZMODEM 集成测试 | P0 | 完成 | 模拟双向收发和真实 lrzsz 双向 64KB 二进制文件内容校验通过 |
| TST-04 | SSH/串口集成测试 | P0 | 部分 | 本地 sshd、Windows 配置内 SSH/串口、Linux socat PTY 已测 |
| TST-05 | UI 基准截图 | P0 | 部分 | Phase 1 会话/标签/密码/分离/断线/性能证据已存，浅/深色和 150%/200% DPI 截图已补；最终逐页像素复核待做 |
| TST-06 | 8 小时稳定性 | P0 | 部分 | 无崩溃、无明显泄漏 |
| TST-07 | 切换/回滚演练 | P0 | 部分 | 已演练 Beta 卸载后 Python 配置/应用保留；默认版本切换与回退尚未执行 |
