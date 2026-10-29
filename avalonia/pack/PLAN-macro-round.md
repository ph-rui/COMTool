# 本轮任务清单（2026-10-28 宏 1:1 复刻 MobaXterm 22.1）

范围（用户以"实施"确认全量 1:1；spec/plan 已批准）：

- [x] Task 1 Core：MacroStep/MacroDefinition/MacroKeys(26 键)/MacroFile(.mxtmacros 解析导出，实机样本验证)/MacroMigrator(旧 text+appendEnter→steps)；MacroConfig 加 steps[]/hotkey，Version 保持 3（commit 0a28378）
- [x] Task 2 Core：MacroRecorder（间隙>1000ms→SLEEP 封顶 1200ms，字符合并，Key 断行）（commit 0a28378）
- [x] Task 3 Core：MacroRunner（文本逐字延迟/按键转义/睡眠/等待字符串 50ms 轮询超时跳过、取消）（commit 0a28378）
- [x] Task 4 App：侧栏宏页 MobaXterm 化（Record 按钮/已保存的宏/◎ 列表+9 项右键菜单）、录制（tunnel 捕获不吞键）、执行（红色横幅+取消）、旧配置迁移、命名框/删除确认、Ctrl+Space 浮层、顶栏"宏"菜单、热键触发（commit af14361）
- [x] Task 5 App：MacroEditorWindow("Macro 编辑" 4 按钮+热键下拉) + MacroStepEditorDialog(4 radio 类型)；Edit/Assign hotkey 均开编辑器；工作副本语义（commit af14361/62cbc9a 部分）
- [x] Task 6 App：快捷键分配校验（保留/重复拒绝）+ Ctrl+Space 浮层 + 顶栏"宏"按钮测试补齐（commit 62cbc9a 前）
- [x] Task 7 App：.mxtmacros 导入/导出（明文警告原文、默认文件名、重名 (imported)、IMacroFilePickers+Scripted 环境变量）（commit a780cd8）
- [x] 修复：Avalonia Key 枚举非 ASCII（(Key)'M'=77 ≠ Key.M=56）导致字母热键永不触发（commit 3356d6e）
- [x] 修复：标题栏加"宏"列后 min/max/close 的 Grid.Column 未后移（5/6/7→6/7/8），关闭按钮落空列导致 E2E APP01 点关闭不退出（commit b72cebd）
- [x] Task 8 文档：parity-matrix MAC-01~08 拆条、feature-test-matrix 登记 22 App+32 Core 测试（MAC-01~08 八行）、manual-acceptance WIN-27、本文件
- [x] 全量 gate（pack/verify.sh，2026-10-28 04:01Z：build/328 单测/192 App/26 项 Xvfb E2E/TestGate 全 PASS，520 项）
- [x] 发布 win-x64 单文件 R2R（154,887,681 B，SHA256 4A39AF2C…）+ deploy_r3.py 双份部署
  （C:\Users\devuser\COMTool-Avalonia.exe 与 D:\Users\devuser\Desktop\ 双份校验 OK）
- [x] SSH 冒烟：进程起来（Session 0 存活 180s+，WS 240MB）、config.json 可读写
  （run.log starting+Config loaded，config 内容完好 v3）、无崩溃日志
  （仅 Skia swap chain 0x887A0022 刷屏 = Session 0 非交互桌面 D3D 设备丢失，
  phase0-report L129 已有同类记录；交互桌面不受影响）
- [ ] 用户实机目验（WIN-27 清单；对照 MobaXterm 22.1 并排）
- [ ] 用户确认后 push（本轮不 push）
- [x] 追加：应用户反馈移除顶栏"宏"按钮（标题栏恢复原 8 列，min/max/close 回 5/6/7；
  宏入口保留左侧栏宏页 + Ctrl+Space + 热键；commit cc9cff4）；gate 第三轮
  （2026-10-28 06:18Z）全 PASS；重新发布（SHA256 FF36D195…）双份部署 + SSH 冒烟通过
- [x] 追加：用户目验反馈（截图）——① 修复"录制新宏"按钮有终端后不亮起的 bug：
  SelectedTab 变化时未通知 CanToggleRecording（补 OnPropertyChanged），新增
  MAC21_RecordButtonEnablesWithActiveTerminal；② 宏 UI 全面汉化：面板标题"宏"、
  "录制新宏/停止录制宏"、9 项右键菜单（重命名/编辑/复制/删除/分配快捷键/执行…）、
  编辑器"宏编辑"+4 按钮+提示、命名框"请为此宏输入一个名称"、导出警告/成功提示改中文；
  文档同步（parity MAC-01/04/05/08、FTM、WIN-27）

- [x] 追加：用户第三次反馈（UI 与 MobaXterm 差异 + "用起来比较卡"）
  - UI 对齐（参照 mx-state3/mx-del-menu/mx-export-dlg 基准图，FA6 码点已验证存在）：
    行图标 f202(⊠)→f110 circle-dot(◎) 蓝 #2f7ed8；录制按钮改双色图标
    （空闲=灰环 f05c + 小红点 f111 常显，录制中=大红点 f111）；9 项右键菜单加图标
    （f044 橙/灰、f24d、f1f8 红、f11b、f144 蓝、f005 金、f305 红、f306 蓝）；
    对话框标题统一 "COMTool"（对齐 MobaXterm 用应用名做标题）；命名框加铅笔 f303
    图标 + 底部居中 ✓确定(f00c 绿)/✗取消(f00d 红) 图标按钮（新增 IconFont()/
    DialogButtonContent() helper）；导出警告/成功提示加蓝色 info 图标 f05a
  - 测试：MacroFeatureTests 按图标+文字按钮更新断言（ButtonByLabel helper），23/23 + 全量 193/193 绿
  - 卡顿诊断：远程机 = SANGFOR VDI（i440FX VM + 2×Sangfor TitanGPU 虚拟显卡，
    driver 11.4.8.914，Win10 Pro）。Session 0 冒烟复现 Skia D3D11 交换链
    0x887A0022 device lost 刷屏（~120 次/秒）→ 头号嫌疑 = 虚拟 GPU 上 D3D11 渲染不稳。
    代码审查：本轮无新增周期负载（仅旧 2s 端口扫描/1s 服务器定时器；MacroRunner
    50ms 轮询仅 Wait 步骤执行时）
  - 软件渲染 A/B 开关：Program.cs BuildAvaloniaApp 增加 COMTOOL_SW_RENDER 环境变量，
    =1 时 With(new Win32PlatformOptions { RenderingMode = { Software } })，
    绕开 AngleEgl(D3D11) 走 CPU framebuffer（Avalonia 12.1.4 API 已用元数据核验：
    Win32PlatformOptions.RenderingMode / Win32RenderingMode.Software 均存在；
    默认行为 { AngleEgl, Software } 按序回退不受影响）
   - gate 第 6 轮（2026-10-28 08:21Z）全 PASS（build/328 单测/193 App/26 E2E/TestGate）
   - 发布 win-x64（154,887,681 B，SHA256 F3C4C987…）+ deploy_r3 双份部署校验 OK；
     用户目录另放 "COMTool-软件渲染.bat"（set COMTOOL_SW_RENDER=1 后启动）
   - Session 0 冒烟 A/B（120s，smoke-ab.ps1）：默认 D3D 路径 CPU 18.98s
     （~15.8% 单核持续占用，虚拟 GPU 上 D3D 心跳/交换链空转）；
     COMTOOL_SW_RENDER=1 时 CPU 2.67s（~2.2%）、日志干净 → 开关生效，
     卡顿主嫌疑锁定虚拟 GPU 的 D3D11 渲染路径；待用户交互桌面实测确认
   - 提交 3445771
   - 追加（2026-10-29）：RDP 实机核对 MobaXterm 22.1 后三项修正：
     ① 执行宏不显示任何横幅（MobaXterm 执行时无 UI，录制横幅保留）；
     ② 单击行即执行宏（RDP 实测 MobaXterm 单击即触发）；
     ③ 行图标改为灰环+蓝色播放三角（实机像素采样 #6e6e6e/#2196f3）；
     f10c 在 solid 字体中是实心盘非环 → 改用矢量 Border 圆环；
     MAC17 改写 + 新增 MAC24/MAC25，197/197，gate 第 8/9 轮全 PASS；
     提交 c215ad2 + 图标修复；发布 15cd67aa 双份部署。
     另：已从用户 MobaXterm.ini 删除 RDP 测试宏 echo moba_test（保持其安全净净）；
     录制文本丢失 bug 修复见 970d1f4（MAC22/23）。
   - 追加（2026-10-29）：宏对话框 UI 优化（用户截图反馈）：新增 CreateMacroDialog
     工厂（COMTool 标题/不显示任务栏/居中/不可缩放）+ Windows 剥离 min/max 按钮
     （SetWindowLongPtr 去 WS_MINIMIZEBOX|WS_MAXIMIZEBOX；CanResize=false 在 VDI 上去不掉）；4 个宏对话框统一（命名/删除/导出警告/成功提示）：f05a info 图标
     + DialogButtonContent 图标按钮（是(Y)✓/否(N)✗，IsDefault/IsCancel）；修复导出警告按钮竖排 bug；
     MAC15/导出警告测试改 ButtonByLabel；197/197，gate 第 10 轮全 PASS；
     提交 efabcab，发布部署双份。
   - 追加（2026-10-29）：收起侧栏时 rail 图标再保持"选中"白底（用户截图反馈：
     收起后星形图标发白你被点住）： VM 增 RailSessions/Tools/Macros/SshBrowserActive
     （=IsSidebarVisible&&对应 Tab）， IsSidebarVisible  ApplySidebarMode  _sidebarVisible； 4 个 rail 按钮 Classes.active ¹ Rail*Active；
     新增 UI16_RailActiveClearedWhenDockCollapsed； 198/198， gate 第 10c 轮全 PASS；
     提交 c1abecb，发布部署双份。
   - 追加（2026-10-29）：错误弹窗/会话右键菜单/分组标题汉化 + 连接取消不弹错（用户截图反馈）：
     ① 会话右键菜单：打开/编辑会话/移除会话（原 Open/Edit session/Remove session）；
     ② 错误弹窗：标题“错误”、绿勾“确定”图标按钮、红 exclamation 图标，走 CreateDialog 工厂（无 min/max）；
        前缀 连接失败/打开失败（原 Connect/Open Failed）+ 4 处消息前缀汉化（配置加载/终端附加/终端启动/标签分离）；
     ③ 连接被取消（OCE）不再弹错误框（MobaXterm 行为，仅复位 Connecting 状态 + 记日志）；
     ④ 环境坑：.NET 10 起 OCE 移入 System 命名空间（System.Threading.OCE 编译 CS0234，最小复现证实），
        catch 改无修饰名（经隐式 using System 解析）；
     ⑤ 侧栏分组标题 Serial→串口（SSH/Telnet/TCP 协议名保留）；设置窗测试连接同套文案同步。
     SessionEditTests/SerialHotplugTests/MacroFeatureTests（错误框 OK 按钮）断言同步；
     198/198，gate 第 12 轮全 PASS；提交 b518e1c，发布部署双份。
   - 追加（2026-10-29）：对话框按钮版式改“消息在上、按钮沉底水平居中对称”（用户截图要求“是、否放在下面一点，然后对称”，系统消息框版式）：
     ① MainWindow 新增 BottomButtonRow 助手（水平居中按钮行）；删除确认/导出警告/成功提示/错误框 4 个对话框 Grid 改
        RowDefinitions="*,Auto"，按钮行跨列沉底、图标跨两行；
     ② 新增回归 MAC26_DeleteConfirmButtonsCenteredAtBottom（按钮在消息下方+落对话框下半区+两按钮同行+行中心≈对话框中心±2px）。
     两个坑：Avalonia 12 TranslatePoint 返回 Point?（Nullable 值类型），null-forgiving `!` 对其无效
        （最小复现证实 p!.Y 仍 CS1061，`!` 只去引用类型可空性），须用 .Value.Y/.Value.X；
        纯代码创建的对话框无 NameScope，FindControl 抛 "Could not find parent name scope"，测试须用 ButtonByLabel。
     199/199，gate 第 13 轮全 PASS；提交 11f78dc，发布部署双份。
   - 追加（2026-10-29）：网络扫描名称解析实机修复（用户截图：192.0.2.251 无名，MobaXterm 显示 winhost1.example.com）：
     ① 根因：VDI 慢网段 DNS 反查需 ~4.7s（ps1 诊断实测 GetHostEntry 4680ms、raw PTR 5329ms），旧 DnsTimeoutMs=500 必超时；
        NBSTAT 兜底亦无应答 → 名称空；MobaXterm 无此截短故能显示 FQDN。
     ② DnsTimeoutMs 500→6000（名称解析仅对存活主机、与端口探测并行，存活判定速度不变；
        两秒出存活结果的设计不受影响，仅名称回填在慢 DNS 网段最多多等几秒，与 MobaXterm 行为一致）；
        反查结果 “X.local” 形态（X ≤15 字符且无点，NetBIOS 短名特征）剥 .local 与 MobaXterm 短名显示一致
        （新增 NormalizeReverseName internal 静态方法，FQDN/IP 回显/空名不受影响）。
     ③ 测试：NET05_NormalizeReverseNameStripsLocalSuffixForShortNames 新增；NET05_ScanDefaultsTargetTwoSecondSubnetScan
        的 DNS 断言 ≤500 放宽为 ≤6000（附慢网段实测依据注释）。
     201/201 App + 113/113 Connections，gate 第 16 轮全 PASS；提交 785648b，发布部署双份。
   - 追加（2026-10-29）：网络扫描仪改 MobaXterm 式增量显示（用户截图反馈“扫描到一台之后立即显示”）：
     ① 引擎 ProbeAsync 两阶段：第一个存活信号（ping 回复或任一端口连通）立即上报 discovered
        （名称空、rtt 空、开放端口=当时已确认子集）；全部探测+名称解析完由 progress 报最终结果；
        死主机仍不出现；总时长不变（/24 仍 ~1-2s）；
     ② INetworkScanner.ScanAsync 加 IProgress<NetworkScanHost>? discovered 参数；窗口双通道：
        discovered→立即插行（已确认开放列绿✓、未确认列灰“…”f141），completed→按 Address 原位更新
        （名称回填+列定格）；注入扫描器不上报 discovered 时兜底补行；
     ③ PortCell/ScanGridRow 改 ObservableObject（IsPending/Port/UpdateFrom，Glyph/Foreground 联动通知）；
        状态行保持“扫描中… X/Y”（X=完成数）；扫描中“停止”仍保留已发现行。
     两个坑：Task.WhenAny 返回的是完成的 Task 本身，不能直接取值，需用 ReferenceEquals
        识别后再从对应类型化任务取 Result；Task<int?>/Task<bool> 混排集合表达式报 CS0029（改显式 Add 循环）。
     测试：NetworkScannerTests 加 NET05_DiscoveredReportedBeforeCompleted（discovered 先于
        completed 顺序，回环真实端口）+ 4 处调用加 null 参；SidebarRailTests 加
        NET05_ScanRowsAppearIncrementally（扫描中途行先出现+名称后补+6 个“…”→0），
        FakeScanner 加 DiscoverDelayMs/DiscoveredOpenPorts（500ms 间隔保证中途状态稳定捕获）。
     200/200 App + 112/112 Connections，gate 第 14 轮全 PASS；提交 fe63f06，发布部署双份。
   - 追加（2026-10-29）：修复实机网络扫描“已发现行滞留 …”缺陷（用户 VDI 192.0.2.251 截图反馈：
     扫描状态已“完成”但该行 7 个服务列仍是灰色省略号、名称未回填）：
     ① 根因：行定格依赖 completed 阶段 progress 回调；实机时序下最后完成主机的回调
        与 UI 队列/扫描收尾竞态时该行永不定格。对策=权威对账：ScanAsync 返回存活主机权威列表
        （IReadOnlyList<NetworkScanHost>，按地址排序），窗口 await 后逐行 reconcile（缺行补行、
        有 pending 列则 UpdateFrom 定格），幂等；
     ② INetworkScanner.ScanAsync 返回值改为存活主机权威列表（IReadOnlyList<NetworkScanHost>，
        按地址排序）；NetworkScanner/FakeScanner 两实现同步（忽略返回值的调用点不受影响）；
     ③ 双通道回调加 AppLog 诊断：discovered 插行、completed 兜底补行、最终计数、
        guard 丢弃上报（区分 newScan/stopped 两原因）、reconcile 定格行数；
     ④ 新增回归测试 NET05_ScanReconcilesRowsWhenCompletedReportLost（FakeScanner 加 DropCompletedReports
        模拟 completed 回调丢失，断言扫描结束后行仍被对账定格、无 … 残留）。
     201/201 App + 112/112 Connections，gate 第 15 轮全 PASS；提交 8e58b55，发布部署双份。
   - 追加（2026-10-29，补记）：网络扫描名称解析实机修复（用户截图：192.0.2.251 无名，MobaXterm 显示 winhost1.example.com）：
     ① 根因（ps1 诊断实测，用户 VDI 192.0.2.x 慢 DNS 网段）：反查需 ~4.7s（GetHostEntry 4680ms/raw PTR 5329ms），
        旧 DnsTimeoutMs=500 必超时；NBSTAT 兜底亦无应答；MobaXterm 无此截短故能显示 FQDN；
     ② DnsTimeoutMs 500→6000（名称解析仅对存活主机、与端口探测并行，存活判定速度不变）；
        反查 “X.local” 形态（≤15 字符无点，NetBIOS 短名特征）剥 .local 对齐 MobaXterm 短名显示（NormalizeReverseName）；
     ③ 测试：NET05_NormalizeReverseNameStripsLocalSuffixForShortNames 新增；
        NET05_ScanDefaultsTargetTwoSecondSubnetScan 的 DNS 断言 ≤500 放宽为 ≤6000（附实测依据）；
     201/201 App + 113/113 Connections，gate 第 16 轮全 PASS；提交 785648b，发布部署双份（0fd617e9）。
   - 追加（2026-10-29）：网络服务器窗口欢迎页去 MobaXterm 字样（用户截图圈出“MobaXterm 网络服务器”）：
     ServersManagementWindow.axaml 欢迎页 TextBlock 改 “COMTool 网络服务器”；
     保留不动：宏导出默认文件名 MobaXterm Macros.mxtmacros（互操作约定）+ SSH 面板 MobaSSH 链接文案（1:1 基准），
     代码注释/文档中的 MobaXterm 参照说明全部保留。
     201/201 App，gate 第 17 轮全 PASS；提交 2f93697，发布部署双份。

   - 追加（2026-10-29）：远程监控功能（用户要求 1:1 复刻 MobaXterm remote monitoring，附两张截图；设计获批后实现）：
     ① 引擎（COMTool.Connections.RemoteMonitor 新子模块）：RemoteMetricsParser（MON_* 行协议解析 +
        CpuDelta/NetRate 差分 + FormatUptime 主单位）、MonitorSnapshot.Format（段序=菜单序，段间两空格，
        对齐用户截图样例）、RemoteMonitor 轮询引擎（2s 间隔、顺序 poll、连续 2 次失败 transport/unsupported
        停并事件上报、Stop 幂等、Stop 期间迟到失败不上报）；dump 脚本=单条合并 sh 命令一次取 9 项原始数据。
     ② SshConnection 实现 IExecRunner.RunCommandAsync（SshNet CreateCommand 独立 exec 通道，CommandTimeout
        5s，不动交互 shell 通道；disposed 快抛 ObjectDisposedException）。
     ③ App 胶水：MainViewModel.Monitor.cs（MonitorMenuState 11 勾选 + 启停状态机 + ApplySnapshot；
        TerminalTab.MonitorEnabled per-tab 开关；SftpPaneViewModel.IsMonitorOn/MonitorToggled/SyncMonitorOn）；
        接线：OpenSshSessionAsync 按钮回调、AttachSshTransport 恢复、CloseAction/ProcessExited/重启失败 三处停止。
     ④ UI：SftpPane 底部状态行加“远程监控”ToggleButton（选中绿色 #2e7d32）；MainWindow 底部状态行加监控 TextBlock
        + 右键 13 项 ContextMenu（ToggleType=CheckBox，Opening 码后同步勾选态，复制/保存到文件走剪贴板与 SaveFilePicker）。
     ⑤ 配置：ComtoolConfig.remoteMonitor（11 bool，旧配置缺键取 MobaXterm 默认 7 项）。
     ⑥ 测试：RM01 21 项（解析/差分/格式/引擎）+ RM02 2 项（本地 sshd 集成：exec/shell 独立、disposed 快抛）
        + RM03 2 项（配置往返/缺省）+ RM04 7 项（VM 生命周期 fake-exec）+ E2E RM01_MonitorBarShowsMetricsE2E
        （Xvfb 点“远程监控”按钮→sshd VERBOSE 日志出现 ≥2 次 dump 命令=轮询链路通）。
     仅 Linux 远端有效（用户场景=Ubuntu 192.0.2.48）；非 Linux 静默停止。提交 bd2244f，发布部署双份；
     Windows 实机 WIN-28 由用户核对（或 RDP 自验）。

## 已知偏差（已写入 spec 附录）

1. wait 步骤导出 typeId=4 为 COMTool 自定（MobaXterm 真实编码未实测）；导入未知 typeId 跳过+警告
2. 热键 .mxtmacros 编码未实测：导出恒 0，导入非 0 忽略+警告
3. 录制间隙阈值 1000ms/封顶 1200ms 由实测两点推断，未做梯度验证（值集中在 MacroRecorder 构造参数）
4. 等待字符串超时行为 MobaXterm 未知：实现为超时跳过继续，默认 30s
5. 桌面快捷方式 P2 灰显占位；热键为应用窗口级（MobaXterm 系统级）
6. 录制中 Ctrl/Alt 组合键不记录（只记普通文本+26 种特殊键）

## 测试基线

- Core：32 项宏测试（MacroModel 4/MacroFile 9/MacroRecorder 9/MacroRunner 8/Config 2）
- App：173→193 项全绿（新增 MacroFeatureTests 23 项，含 MAC21；删除旧 MAC01 3 项）
- 全量 gate：2026-10-28 06:53Z 第四轮全 PASS（build/328 单测/193 App/26 项 Xvfb E2E/TestGate，共 521 项）
