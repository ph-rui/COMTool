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

   - 追加（2026-10-29，第 19 轮）：远程监控状态栏 MobaXterm 式分段 UI + 会话监控记忆
     （用户反馈 2026-10-29：“mobaxterm 是一隔一隔分开的，而且还有对应的图标”；
     “远程监控有记忆功能的……下次打开应用时，自动开启远程监控”）：
     ① 状态栏分段：MonitorTextBlock 单行文本 → Row3 col0 改为一行分段盒子
        （每指标=带边框 Border+FontAwesome 图标+文本，主题色 MonSeg* Light/Dark）：
        host f390 / cpu f624+56px 迷你 CPU 条（宽=56*pct/100）/ ram f538 / netup f062 绿 /
        netdown f063 蓝 / clients f0c0 / processes f53f / fds f15b / uptime f017 / users f007 /
        partitions f0a0（分区每区一盒，首盒带图标，≥80% 橙色 #e2590b）。
        VM 新增 Seg*Visible/Seg*Text/SegCpuBarWidth/Partitions（RefreshMonitorSegments 挂
        ApplySnapshot/ReformatMonitorLine，StopMonitorPolling 清段）；
        整串 MonitorText 保留供“复制/保存到文件”（RM01 锚定的格式不变）。
     ② 实机 bug 修复（RDP 发现）：Row3 Grid Background=null → Avalonia 中 null 背景 Panel
        不参与指针命中测试 → 右键穿透到窗口根、13 项菜单弹不出（标题栏 Border 有背景，右键正常）。
        Row3 加 Background="Transparent"；回归测试 RM04_StatusBarGridHasBackgroundForContextMenuHitTest。
     ③ 会话监控记忆（MobaXterm 行为）：SshSession 加 monitor 键（旧配置缺键=false）；
        OpenSshSessionAsync 按会话恢复 tab.MonitorEnabled（标签选中即自动恢复轮询）；
        SFTP 面板开关回调经 SshSessionStore.SetMonitor+Save 持久化（编辑会话 Upsert 不丢开关）。
     ④ 测试：App RM04_SegmentsRenderPerEnabledOptionAndFollowToggles（真实引擎 40% CPU 确定性/
        分区 hot/图标/XAML 绑定/勾选即时重画/停止清段）；Core RM03_SshSessionMonitorFlagRoundTripsAndPersistsThroughStore
        （往返/旧配置/SetMonitor/Upsert 不丢）；E2E RM05_MonitorMemoryAcrossRestartE2E（第一阶段手动开
        监控→轮询，杀进程重启，重连不点任何按钮轮询恢复=记忆链路通）。
     App 212/212、Core 86/86、TestGate 49/49；gate 第 19 轮全 PASS（E2E 15/15，含 RM05）；提交 bbb39b53，发布部署双份。
     Windows 实机 WIN-28 补核：分段盒子+图标外观、菜单外观、复制/保存、记忆行为。

   - 追加（2026-10-29，第 20 轮）：状态栏图标/配色对齐 MobaXterm + 远程监控按钮改版
     （用户反馈 2026-10-29：“上面是 mobaxterm 的，在优化一些图标与颜色；远程监控的点击按钮也优化一下”）：
     ① 图标/配色（fontTools 核对捆绑 fontawesome6-solid.ttf 码位，参考图 4x 放大比对）：
        host f390→f108 深灰；cpu f624→f2db 绿 #2e9e4f；ram f538 绿；netup f062→f093 绿；
        netdown f063→f019 蓝 #2f7ed8；clients f0c0 青 #1aa3d8；processes f53f→f085 橙 #e8701a；
        fds f15b→f15c 蓝；uptime f017 灰绿（MonIconUptimeBrush）；users f007 深灰、分隔符逗号→空格；
        partitions f0a0 灰蓝（MonIconDiskBrush）；占用最高分区按 MobaXterm 实样改为粗体+斜体橙红字
        （MonSegHotBrush），不再用填充底色。新增主题刷 MonIconDark/MonIconDisk/MonIconUptime/MonSegHot（Light/Dark）。
     ② DataTemplate 陷阱：分区模板里内联 DynamicResource 在实例化时解析不到主题资源（实测回退 MonIconBrush 灰，
        被单测抓出），改由类样式 MonIconDisk/MonSegHot 提供，视觉才真正生效。
     ③ 监控按钮：ToggleButton.MonitorToggle 由纯文字改为 显示器图标 e163 + “远程监控”文字；
        关=白底 #ffffff/边框 #d5dce3/灰字 #666666，开=浅绿底 #e8f5ee/边框 #b7dfc9/绿字 #0f8a63 半粗，
        圆角 3、Padding 7,1；并覆盖 :pointerover/:checked/:pressed 的 ContentPresenter 背景
        （默认模板会用主题高亮色盖掉自定义底色）。
     ④ 测试：RM06_SegmentIconsAndColorsMatchMobaXtermReference（11 个图标码位+配色、热分区粗斜体、
        用户空格分隔）、RM06_MonitorButtonShowsDisplayIconAndGreenOnState（图标/文字/开关三态配色）。
     App 214/214、Core 86/86、TestGate 49/49；gate 第 20 轮全 PASS（E2E 15/15）；提交 53bee6e，发布部署双份。
     Windows 实机 WIN-28 补核：分段图标/颜色与 MobaXterm 并排、热分区粗斜体、按钮开关态。

   - 追加（2026-10-30，第 21 轮）：移除 4 项指标 + 热分区去斜体 + 监控按钮改通栏
     （用户反馈 2026-10-30：“为啥最后这个分区字体是斜着的？把已连接客户端、进程数、文件描述符、连接用户都删了不要了；
     远程监控的按钮的宽度，mobaxterm是跟侧边栏一样宽的”）：
     ① 热分区：去掉 FontStyle=Italic，保留橙红 MonSegHotBrush + 粗体（RM06 断言 FontStyle.Normal）。
     ② 指标精简为 MobaXterm 默认 7 项：引擎 dump 命令删除 MON_CLIENTS/MON_PROC/MON_FD/MON_WHO；
        DumpData/MonitorSnapshot/MonitorOptions/RemoteMonitorConfig 删除对应字段；状态栏分段与右键菜单删除
        客户端/进程/FD/用户 4 项；菜单 13→9 项（复制/保存到文件/分隔线 + 7 项勾选且默认全开）。
        旧 config 里的 show_clients 等键由 JsonExtensionData 保留，不影响加载。
     ③ 监控按钮：从状态行左侧小按钮移到抽屉底部独立第 8 行通栏 Border（#f1f1f1 底、1px 上边框），
        ToggleButton HorizontalAlignment=Stretch、内容居中，宽度=侧栏宽度；关=灰字，开=浅绿底 #e8f5ee/绿字 #0f8a63 半粗。
     ④ 测试：RM04_StatusBarMenuHas9ItemsWithAllSevenCheckedByDefault；RM06 断言
        MonSegClients/Processes/Fds/Users 已不存在、按钮宽度与通栏一致；删除 RM01_FormatOptionalCountersBareNumbers。
     App 214/214、Core 86/86、Connections 135/135、TestGate 49/49；gate 第 21 轮全 PASS（E2E 15/15）；提交 ce2c433，发布部署双份。
     Windows 实机 WIN-28 补核：通栏按钮宽度、热分区粗体无斜体、菜单只剩 7 项指标。

   - 追加（2026-10-30，第 22 轮）：CPU 迷你条随负载变色 + 修复“远程监控”按钮偶发点不动
     （用户反馈 2026-10-30：附 MobaXterm 参考图“这个进度条要红色，与 mobaxterm 一样”；
     另报“有时候点击远程监控这个按钮没反应，需要多点击几下”）：
     ① CPU 迷你条：填充色由固定浅灰改为随负载变色（对齐 MobaXterm，参考图 81% 取色
        #d84a2f 红）：<50% 绿 #2e9e4f / 50–79% 橙 #e8a31a / ≥80% 红 #d84a2f。
        MainViewModel 新增 CpuBarBrush/CpuBarColor；XAML 填充 Background 改绑 SegCpuBarBrush；
        删除不再使用的 MonCpuBarFillBrush（Light/Dark）。注意 Avalonia 12 的 SolidColorBrush
        线程亲和（跨线程读 .Color 抛 InvalidOperationException），故不缓存静态画刷、每次新建。
     ② 按钮偶发点不动根因：MainWindow 的无边框窗口边缘 resize 隧道处理器
        OnWindowPointerPressed 在距窗口边缘 5px 内会 BeginResizeDrag + e.Handled=true 吞掉点击，
        而豁免判断只认 Button；“远程监控”是 ToggleButton（Avalonia 12 无 ButtonBase 公共基类），
        又恰是贴窗口底边的整条通栏，故点击落在底边 5px 内时首击被吞 → “要再点几下”。
        修：抽出 IsEdgeInteractiveControl，豁免 Button/ToggleButton/RepeatButton 三类。
     ③ 测试：RM06_CpuBarBrushTracksLoad（0/40 绿、50/79 橙、80/81/120 红）、
        RM06_EdgeResizeDragExemptsToggleButton（ToggleButton/Button 豁免、TextBlock/Border 不豁免）、
        RM04 补 40%→绿 断言。
     App 216/216（连跑 3 次无 flaky）、Core 86/86、Connections 135/135、TestGate 49/49；
     gate 第 22 轮全 PASS（E2E 15/15）；提交 2d3116e，发布部署双份。
     Windows 实机 WIN-28 补核：高负载时 CPU 条变红、点“远程监控”一次即生效。

   - 追加（2026-10-30，第 23 轮）：CPU 条改为示波器 roll 时间序列 + 热分区恢复粗斜体
     （用户 SSH 实机对照 MobaXterm 后指出：“mobaxterm cpu进度条是类似于示波器roll模式下的，
     横轴是时间；关于这个分区信息，是根据不同的占用显示不同的颜色？”）：
     ① 取色/形态核实：参考图 CPU 黑框内只有底部右侧一小段绿色笔迹（#017701 深绿，
        x98~111 约 7 小格贴底），即 0~10% 的低载历史，不是“宽度=当前百分比”的静态填充；
        分区段只有 82% 那个是橙红高亮、49%/1% 为正常深色 → MobaXterm 是阈值高亮（≥80%），
        不是按占用分档渐变，且高亮样式为粗体+斜体。
     ② CPU 条实现：MainViewModel 新增 CpuBarColumn（Percent/Height/Brush，Percent 变化时
        自动算高与色）；固定 28 列（2px/列，56px 轨道，28×2s≈56 秒历史）的
        CpuColumns 集合，PushCpuSample 左移一列、新样本进最右列，列高=该样本负载%×12px，
        每列按自身负载着色（<50% 绿 #2e9e4f / 50–79% 橙 #e8a31a / ≥80% 红 #d84a2f）。
        只有轮询快照路径推进历史，菜单勾选重排版（ReformatMonitorLine）不推进；
        停监控/清空分段时历史归零。XAML 把内层单条 Border 换成 ItemsControl 列阵，
        底对齐、ClipToBounds 裁圆角。
     ③ 配色分档沿用第 22 轮，保留 CpuBarColor（线程无关 Color）；画刷在 UI 线程创建
        （Avalonia 12 SolidColorBrush 线程亲和）。
     ④ 热分区：TextBlock.MonSegHot 增补 FontStyle=Italic，恢复 MobaXterm 的粗体+斜体。
     ⑤ 测试：新增 RM06_CpuBarHistoryRollsLikeScope（28 列、右端进新样本/左滚丢旧样本、
        每列按自身负载着色与高度、清空归零）；RM06_SegmentIconsAndColorsMatchMobaXtermReference
        改为增量 CPU dump 并断言 28 列渲染（最右列=40%、绿、4.8px）；热分区断言改回 Italic。
     App 217/217、Core 86/86、Connections 135/135、TestGate 49/49；gate 第 23 轮全 PASS（E2E 15/15）；
     ⑥ 渲染校验：RM06 断言 28 列正好铺满 56px 轨道（首列 x=0、末列右缘=56），
        确认 ItemsControl 模板无内边距导致溢出/少一列。
     提交 56d5af8，发布部署双份。
     gate 第 23 轮首次运行 App 测试进程在 130/217 处 exit 134（历史已知偶发崩溃，
     与 r21b 的 exit 139 同类）；独立 Release 复跑 217/217 连续两次通过后整轮 gate 全绿（gate-r23b.log）。
     Windows 实机 WIN-28 补核：CPU 条为自右向左滚动的历史柱状（负载升高时红柱出现在最右侧），
     热分区为橙红粗斜体。

   - 追加（2026-10-30，第 24 轮）：状态栏整排放大，比例对齐 MobaXterm
     （用户反馈 2026-10-30：附 MobaXterm/COMTool 同屏对照图“下面这排显示偏小了”）：
     ① 实测对照（取参考图像素）：MobaXterm CPU 条 46×22px、小格外高约 26px、图标约 16px；
        我们原先 CPU 条 56×12px、小格 22px、图标 11px、状态栏行高 30px，整排偏薄偏小。
     ② 调整（MainWindow.axaml）：状态栏行高 30→34；MonSeg Padding 6,2→7,3；
        MonSegText 12→13px；MonIcon 11→13px；CPU 轨道 56×12→56×18。
        MainViewModel CpuBarMaxHeight 12→18（roll 列高按此换算）。
     ③ 渲染实测（Xvfb 单独跑 E2E RM01 截图 rm01-statusbar.png 量像素）：小格外高 24px、
        CPU 轨道 56×18、28 列仍正好铺满轨道；RM01 点击坐标不受影响
        （监控按钮在侧栏 SFTP 面板底部，与状态栏行高无关）。
     ④ 测试：RM06 既有断言随常量自适应；run-ui-e2e.sh 的 RM01 增加常规截图
        rm01-statusbar.png（视觉核对证据）。
     App 217/217、Core 86/86、Connections 135/135、TestGate 49/49；gate 第 24 轮全 PASS
     （E2E 15/15，gate-r24.log）；提交 5e4677c，发布部署双份
     （SHA256 5384b6febfe886cca1fb06f5cf5bade8243302dde752c9af08fb8709589d7c1a）。
     Windows 实机 WIN-28 补核：状态栏整排高度/CPU 条厚度/图标大小与 MobaXterm 并排一致。

   - 追加（2026-10-30，第 25 轮）：窗口最大化状态记忆
     （用户反馈 2026-10-30：“最大化之后，下次打开不是最大化”；对齐 MobaXterm 窗口状态记忆）：
     ① 根因：原有窗口尺寸持久化（PersistWindowSize/ApplyWindowSize）只存 width/height，
        WindowState 从不落盘、启动从不恢复，故最大化重启后必回 normal。
     ② 实现：WindowConfig 新增 `maximized` 键（默认 false，旧配置缺键不报错）；
        ApplyWindowSize 应用尺寸后按 config.Maximized 切 WindowState.Maximized
        （构造期 Show 前设置与 OnOpened 的 ApplyWindowSizeConfig 路径均覆盖）；
        PersistWindowSize 一并保存（early-return 条件加入 maximized，尺寸存 normal 态、
        恢复后再最大化）；OnPropertyChanged 的 WindowStateProperty 分支增加
        PersistWindowSize()——最大化/还原切换立即落盘，崩溃/强杀也不丢本次状态。
     ③ 测试：ConfigTests.SaveAndLoad_RoundTripsWindowMaximized（往返 + 旧配置缺键默认 false）、
        TitleBarTests.UI01_WindowMaximizedStateRestoredOnRestart（seed maximized=true →
        新窗口 Show 后即 Maximized）、TitleBarTests.UI01_WindowMaximizedStatePersistedOnClose
        （Normal→Maximized→Close → config 文件 maximized=true）。
     App 219/219（Release）、Core 87/87；gate 第 25 轮全 PASS（E2E 15/15，gate-r25.log）；提交 caea774，
     发布部署双份（SHA256 99da66321dfa0137c2704331aa49569681a872681632d8c2d2801de0af2e95f3）。
     Windows 实机 WIN-01 补核：最大化后关闭应用，重开窗口自动最大化；还原后关闭则恢复普通窗口。

   - 追加（2026-10-30，第 26 轮）：终端字号记忆 + 远程监控首击修复
     （用户反馈 2026-10-30：“Ctrl+滚轮调字号，下次启动要记住”，选 A 方案＝全局生效并持久化；
     另“刚连上 SSH 后第一次点远程监控无效，按钮还是绿色，再点一下才变灰”）：
     ① 字号记忆（A 方案）：ComtoolConfig 新增 `terminal`/`terminal.fontSize`（默认 13.5，
        旧配置缺键不报错）；Ctrl+滚轮 ±1（8–48）后更新 `_terminalFontSize`，
        ApplyTerminalFontSizeToAll 同步所有已打开标签的终端，PersistTerminalFontSize 立即
        SaveConfig 落盘（不重写会话存储）；启动时 ApplyTerminalConfig 读盘并钳制，
        CreateTerminal 用记忆值，故重启后新开终端沿用；ClampTerminalFontSize 为纯函数
        （0/NaN/∞ → 13.5，越界钳制 8–48）。
     ② 首击修复：监控 ToggleButton 由默认 ClickMode.Release 改为 `ClickMode="Press"`
        （按下即切换，不再依赖抬起时指针仍在按钮上——刚连上 SSH 时窗口激活/布局抖动会丢抬起点击）；
        SetMonitorEnabled 对 SSH 分支始终 SyncMonitorOn 回写按钮态防漂移，非 SSH 分支记日志并回滚；
        MainWindow 的 MonitorToggled 增加 `Remote monitor toggle:` 日志便于 run.log 定位。
     ③ 测试：ConfigTests.SaveAndLoad_RoundTripsTerminalFontSize；
        CjkTerminalFontTests.TERM18_ClampTerminalFontSizeFallsBackAndBounds、
        TERM18_CtrlWheelZoomAppliesToEveryOpenTerminalAndPersists、
        TERM18_SavedFontSizeIsUsedByTerminalsAfterRestart；
        RemoteMonitorFeatureTests.RM04_MonitorToggleFirstRealClickTurnsOffTheRunningMonitor
        （真实 MouseDown/MouseUp，断言 ClickMode=Press 且一次点击即关闭）。
     App 223/223（Release）、Core 88/88；提交 03a0a55（字号）、9450440（首击）、72be086（文档）。
     gate 第 26 轮全 PASS（build + Core 88/Sftp 42/Terminal 11/Connections 135/Zmodem 32/TestGate 49/App 223，
     E2E 15/15，gate-r26.log）；发布 win-x64 214,961,086 字节，双份部署校验 OK
     （SHA256 0b25eba882310e2f6ee715ab71808f536d0a4c53850e721b652eb4deb7548057）。
     Windows 实机待核：Ctrl+滚轮 调字号 → 重启后字号保持且所有标签一致；刚连 SSH 后点一次监控即变灰。

   - 追加（2026-10-30，第 27 轮）：热分区去掉斜体，修复启动后偶发崩溃退出
     （用户反馈 2026-10-30：附状态栏截图“为啥最后一个分区信息是斜体的？；有时候打开应用会卡住然后退出”）：
     ① 定位：Windows 事件日志 Application Error 1000 + .NET Runtime 1026（14:53:43/14:31:24/10:55:49
        三次同签名）：异常 0xc0000005，栈顶 SkiaSharp.SkiaApi.sk_stream_read →
        Avalonia.Skia.SkiaTypeface.TryGetStream → FontCollectionBase.TryCreateSyntheticGlyphTypeface
        → TextBlock.MeasureOverride；均在 SSH `terminal attached` 同一秒（首个监控快照渲染时）崩溃。
        run.log 无记录，因为 AccessViolation 属 corrupted-state，CLR 直接终止、托管异常处理器不执行。
     ② 根因：UI 字体 Microsoft YaHei UI 没有真斜体，状态栏热分区（占用 ≥80%）设了
        FontStyle=Italic，Avalonia 让 Skia 现场合成斜体字形，读字体流时 AV；该 VDI 的
        /mnt/sangfor/vdb 恒为 82% → 每次监控一开就命中。全应用仅此一处 Italic。
     ③ 修复：MonSegHot 只保留橙红 MonSegHotBrush，去掉 FontStyle=Italic 与 FontWeight=Bold。
        用户追问“为啥要粗体，跟 mobaxterm 一样吗”后，用 2026-10-30 的并排对照图（上 MobaXterm/
        下 COMTool）4 倍放大复核：MobaXterm 的热分区是橙红、常规字重、正体，粗体与斜体都是我们
        早期误读，遂一并去掉；RM06 断言 FontWeight.Normal + FontStyle.Normal，既对齐实样也避免
        任何 Skia 合成字形路径。
     ④ 排查产物：/tmp/opencode/crash-1453.dmp（dump）、crash1026.txt（事件栈）；
        崩溃转储位于 VDI `%LOCALAPPDATA%\CrashDumps`；并排对照放大图为
        /tmp/opencode/cmp-hot-4x.png（上 MobaXterm / 下 COMTool）。
     App 223/223（Release）、Core 88/88；提交 6e99ebb（去斜体）、7fd756d（去粗体，只留橙红）、
     f43a608（文档）。gate 第 27 轮全 PASS（含 Core/Sftp/Terminal/Connections/Zmodem/TestGate/
     App 223，E2E 15/15，gate-r27b.log；首次 gate 因样式再调整主动中止并重跑）；
     发布 win-x64 214,961,086 字节，双份部署校验 OK
     （SHA256 fa46129b94f7efe9ebbed16905d11483a80df2259383ffa57f636e03ab62edef）。
     Windows 实机待核：82% 分区为橙红常规正体；开着监控反复重启应用不再崩溃退出
     （若再出现，查 `%LOCALAPPDATA%\CrashDumps` 是否新增 dump 及事件日志 1000/1026）。

   - 追加（2026-10-30，第 28 轮）：多行粘贴空行修复 + pty 尺寸同步（top 只显示半截）
     （用户反馈 2026-10-30：截图“复制粘贴多行命令时，mobaxterm 命令行之间没有空行，
     COMTool 有；top 命令 MobaXterm 可以显示全屏，COMTool 只显示半截”）：
     ① 粘贴空行根因：库 PasteAsync 把剪贴板文本原样写入 pty；Windows 剪贴板行尾 CRLF，
        远端 tty line discipline 的 ICRNL 把 \r 转成换行提交、随后的 \n 再多打一个换行
        → 每条命令之间一行空提示符。修：粘贴入口（右键 + Ctrl+V）改走
        MainWindow.PasteNormalizedAsync——行尾统一规范为单个 \r（CRLF/LF→\r，
        NormalizePtyPasteText 纯函数；bracketed-paste 模式加 \u001b[200~ 包裹；有选区时回退
        库默认 PasteAsync 保留“粘贴替换选区”），经 SendPasteTextAsync→SendInputAsync 发送。
        不改 pty 流本身，避免污染 Zmodem 二进制通道。
     ② top 半截根因：SSH 建连固定 CreateShellStream(100x30)；库 TerminalView.ArrangeOverride
        只在网格尺寸“变化”时调 pty.Resize，而 AttachConnection 发生在终端首次布局之后
        → 首个真实视口尺寸永不推给远端，pty 停在 100x30（用户截图 top 恰画 30 行证实）。
        修：AttachTerminal 在 AttachConnection 后以 DispatcherPriority.Background 追加
        SyncPtySizeToViewport（按 CharWidth/CharHeight/GutterWidth 算 cols/rows，算法与库
        ArrangeOverride 一致，pty.Resize 推给远端并记 AppLog；窗口后续变化仍由库自身
        ArrangeOverride→ChangeWindowSize 路径跟进，无重复逻辑）。
     ③ 测试：TerminalPtyFeatureTests 3 项——TERM19_PasteLineEndingsNormalizeToSingleCarriageReturn
        （纯函数：CRLF/LF/CR/无行尾 四例）、TERM19_PasteNormalizedSendsSingleCrPerLineToPty
        （headless 真实 AttachConnection 捕获 pty 写入，断言只收到 `l1\rl2\r` 无空行）、
        TERM19_SyncPtySizeMatchesViewportAfterAttach（断言 resize 回调收到与视口度量一致的
        cols/rows）。粘贴 e2e 不依赖 headless 剪贴板（剪贴板 async 在 UI 线程 GetResult 会
        死锁，实测 15 分钟挂起证实；改用 SendPasteTextAsync 直发 + env.RunAsync）。
     App 226/226（Release，含 3 新测试；中途两次 179/217 处宿主偶发 SIGSEGV exit 139，
     历史已知非代码问题，复跑全过）、Core 88/88；提交 0ab3aac（代码+测试）、b266099（文档）。
     gate 第 28 轮全 PASS（Core 88/Sftp 42/Terminal 11/Connections 135/Zmodem 32/TestGate 49/
     App 226，E2E 15/15，gate-r28b.log；gate-r28.log 因 App 宿主偶发 SIGSEGV FAIL 后重跑）；
     发布 win-x64 214,973,374 字节，双份部署校验 OK
     （SHA256 71fc0d68a7760dd15d4c4fa9817bbc4a1ee0ef08d4d971de920b3fc7c60ed52e，
     与用户自行构建放入目录的 exe 完全一致）。
     Windows 实机待核：多行粘贴无空行、top 铺满终端视口。

   - 追加（2026-10-30，第 29 轮）：终端行尾仿真选项（MobaXterm「实时终端重配置」两项）
     （用户反馈 2026-10-30：“mobaxterm有红框这两项配置的”，附 MobaXterm「实时终端重配置 → 终端」
     面板截图，红框 Implicit CR in every LF / Implicit LF in every CR，两项默认均不勾）：
     ① 选项语义（接收端仿真开关）：Implicit CR in every LF——收到裸 \n 时先回行首（等效 \r\n，
        裸 LF 当整行换行）；Implicit LF in every CR——收到裸 \r 时额外换下一行（\r\n 成对不重复处理，
        故常规 SSH/串口回显不受影响）。用于对接按老式行尾约定输出的设备；MobaXterm 默认双关。
     ② 实现：TerminalConfig 增 implicitCrInLf / implicitLfInCr（默认 false，旧配置缺键不报错）；
        COMTool.Connections 新增 LineEndingOptions（共享可变开关，所有终端共用一份，对话框改动即时生效）
        + LineEndingStream（套在 ReaderStream 外的 Stream，1 字节前瞻区分裸 \r 与 \r\n 成对；
        双关时整块直通零开销；只处理显示字节，Zmodem 二进制不经过此层不受影响）；
        PtyStreamConnection 加 public WrapReader（须在 AttachConnection 之前调用——库读循环每次
        取 ReaderStream 属性且 reader 线程 attach 才启动）；MainWindow.AttachTerminal 在 attach 前
        统一套层（SSH/串口/Telnet 均走此路径）。
     ③ UI：标题栏右键菜单新增“终端设置”（Light/Dark 与 About 之间）→ “实时终端重配置”对话框
        （代码生成 Window，同 About 风格）：两个复选框（中文名 + MobaXterm 英文原名）+ 说明 +
        确定/取消；确定=共享 options 立即对所有已连接终端生效（Live）+ 写 config.json。
     ④ 测试：LineEndingStreamTests 9 项（双开关全组合、\r\n 成对不重复、裸 \r/\n 变换、
        EOF 尾部 \r 结算、跨 chunk 边界、双关整块直通）；
        ConfigTests.SaveAndLoad_RoundTripsLineEndingOptions（往返 + 旧配置默认 false）；
        TerminalPtyFeatureTests.TERM20_AttachTerminalWrapsReaderWithLineEndingStream（headless 真实
        AttachConnection：开 ImplicitLfInCr 后远端发裸 \r 显示为换行、关闭后恢复，证明 Live 生效）。
        对话框的剪贴板/ShowDialog 在 headless 有 async 死锁风险，e2e 不走 UI 点击路径，
        改验证 config→options→stream 链路。
     提交 2b1ee5b（代码+测试，8 文件 +706/-5；含 TestGate 修正：BothOn Theory(4 用例) 拆为 4 个裸名 Fact、
     FTM ChunkBoundary 名 CrAndLF→CrAndLf 对齐）。gate-r29.log 首轮在 testgate 阶段报 2 处 FTM 测试名
     不匹配（Theory 用例名带 (input:...) 参数无法按裸名匹配 + CrAndLF 大小写笔误），修正后复跑。
     gate-r29b.log 全 PASS：Core 89/Sftp 42/Terminal 11/Connections 148/Zmodem 32/TestGate 49/App 227，
     E2E 15/15。发布 win-x64 单文件 R2R 95,077,582 字节
     （SHA256 9731B9E7130A59DB4D9681A9F57C1666F2DBD03805AE7523490C74EA1A54EBFA；本轮按 README 规范命令
     启用 EnableCompressionInSingleFile 压缩，故小于 r28 的 214,973,374 字节，为压缩口径差异，功能完整），
     deploy_r3.py 双份部署+SHA256 校验 OK（9731B9E7…A54EBFA）。Windows 实机待核：标题栏右键→终端设置
     对话框（默认双不勾）、勾选后对已连接终端 Live 即时生效、重启后勾选状态保留、双关时无任何行为回归。

   - 追加（2026-10-30，第 30 轮）：行尾开关黑屏修复 + 终端 Shift+右键菜单 + 两处 UI 对齐
     （用户 RDP 实机反馈：使能任一开关后 SSH 黑屏无显示；连接设置 Key file「...」不居中；About OK 位置；
     终端设置应像 MobaXterm 一样 shift+右键弹出）：
     ① 黑屏根因：LineEndingStream 慢路径 `while (total < count)` 坚持填满读循环的 256KB 请求
        （TerminalView.ReadPtyOutputAsync 用 0x40000 buffer 调 ReaderStream.Read）；内层 ZmodemStream
        语义是“≥1 字节即返回当前可用量”，数据耗尽后阻塞等下一批——远端输出不足 256KB 时 Read 永不返回，
        读循环停摆，已到手的变换文本无法上屏（双关走快路径整块直通，故仅使能开关后复现）。单测全用
        MemoryStream（读干即 EOF）无法暴露。修复：慢路径改“有数据即返回”——已有变换输出立即返回；
        否则至多阻塞内层一次，变换后有输出即返回；仅 EOF 且无剩余才返回 0；整块被扣作待决 \r 时
        继续拉下一字节判定（不提前报 EOF）。回归测试 ReadReturnsAvailable_DoesNotWaitForFullBuffer
        （BlockingStream 第二次读挂起即证“最多阻塞一次”）与 PendingCrAtTail_ReturnsPriorTextFirst
        （前段文本先交付、尾 \r 扣住待决）。
     ② 终端 Shift+右键菜单（MobaXterm 22.1 同构，18 项+4 分隔线，汉化）：OnTerminalPointerPressed
        增加 Shift 门控——普通右键保持原行为（有选区=复制/无选区=粘贴），Shift+右键调
        OpenTerminalContextMenu 建菜单并 Open(terminal)（Avalonia 12.1.4 无 OpenAt，锚定控件）。
        接线项：复制(按选区启用)/复制全部(SelectAll+Copy)/粘贴(规范粘贴 Shift+INSERT 标注)/执行宏
        (Ctrl+空格→宏浮层，无宏置灰)/录制宏(启停切换，仅选中 tab)/保存到文件(Ctrl+Shift+S→
        TerminalTranscript 全缓冲存盘)/Zmodem 接收(向远端发 sz\r)/Zmodem 发送(发 rz\r→本地选框)/
        更改终端设置…(打开实时终端重配置对话框)；置灰占位：复制 RTF/查找/字体设置/终端显示/语法高亮/
        录制输出到文件/打印/特殊命令/事件日志。标题栏右键菜单移除“终端设置”项（入口移入终端菜单，
        对齐 MobaXterm）；OnTerminalSettingsClick 仅保留 ShowTerminalSettingsAsync 供菜单调用。
        测试：TerminalPtyFeatureTests.TERM21（headless 合成 Shift+右键 PointerPressed：18 项顺序/文案、
        分隔线 4 条、普通右键不弹菜单、复制按选区置灰、粘贴/更改终端设置…启用、Shift+INSERT 标注、
        Zmodem 接收启用态）；LastTerminalMenuForTests 钩子供断言。
     ③ 连接设置 Key file「...」按钮：内容 "..."→"…"（三点基线偏下致视觉不居中），补
        HorizontalContentAlignment/VerticalContentAlignment=Center。
     ④ About 窗：固定 Height=430 致内容下 ~120px 死区、OK 悬空（截图 549×455 实测 OK 底 y=330）；
        改 SizeToContent=Height（保留 Width=560），OK 贴底右标准位。
     ⑤ 文档：parity/FTM 增 TERM-21 行，FTM TERM-20 补 2 回归测试名与黑屏根因注记；
        manual WIN-30 补黑屏补充核项与入口变更，新增 WIN-31。
          结果（2026-10-30）：提交 30e1dbc（代码+测试，6 文件 +450/-31）。gate-r30.log 全 PASS：Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 228（含 TERM21 与 2 个黑屏回归），E2E 15/15。发布 win-x64 单文件 R2R 95,085,784 字节（SHA256 DC1AB0A2E143B9EA28F0E090184AE970C57345E9B7A99177C34D10CDAC3D713B），deploy_r3.py 双份部署+SHA256 校验 OK。Windows 实机待核：使能任一开关后 SSH 正常显示（黑屏修复）、终端 Shift+右键菜单（18 项，更改终端设置…→实时终端重配置）、普通右键行为不变、Key file「…」居中、About OK 贴底、标题栏右键不再有“终端设置”。

   - 追加（2026-10-30，第 31 轮）：终端 Shift+右键菜单实机不弹（用户 RDP 反馈，针对 dc1ab0a2 构建）
     ① 根因：库 TerminalView（TerminalControl 模板内最深命中目标）的 OnPointerPressed 对右键
        无条件 e.Handled=true（先于 await 同步置位）并做 复制/粘贴（TerminalView.Mouse.cs L76-103），
        因此我们挂在 TerminalControl 冒泡阶段的 PointerPressed handler 永远收不到真实右键事件。
        TERM21 旧版直接在 TerminalControl 上 RaiseEvent（源=TerminalControl，不经过 TerminalView
        的类 handler），故单测全绿而实机不弹——典型的“测试绕过真实路径”假阳性。
     ② 修复（MainWindow.CreateTerminal）：订阅改为隧道阶段
        terminal.AddHandler(InputElement.PointerPressedEvent, OnTerminalPointerPressed, RoutingStrategies.Tunnel)。
        TerminalControl 是 TerminalView 的祖先，隧道（根→目标）先于库类 handler 到达：
        - Shift+右键 → e.Handled=true + OpenTerminalContextMenu（18 项菜单）；
        - 普通右键 → e.Handled=true + Focus 内层 TerminalView + 有选区 CopyAsync/无选区
          PasteNormalizedAsync（顺带修复：普通右击粘贴此前由库处理、未走 r28 行尾规范化路径）；
        - 非右键不触碰事件（左键选择/双击选词/鼠标上报模式不受影响）。
        窗口级 OnWindowPointerPressed（隧道）仅处理左键边缘拖动，右键提前 return，无冲突。
     ③ 测试：TERM21 的 RaiseTerminalRightClick 改为对内层 TerminalView（GetVisualDescendants 查找，
        找不到则失败断言）RaiseEvent，事件路由与实机一致（隧道先到我们的 handler，置 Handled 后
        库的类 handler 被跳过）。
          结果（2026-10-30）：提交 562ed2c（代码+测试，2 文件 +55/-8）。gate-r31.log 全 PASS：Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 228（TERM21 改真实隧道路径后仍绿），E2E 15/15。发布 win-x64 单文件 R2R 95,085,774 字节（SHA256 D9144DDE538E3579BD87C74B62F1CA8DDC9598663E2AB5FA6B02E6E636D47AD0），deploy_r3.py 双份部署+SHA256 校验 OK。Windows 实机待核：终端内 Shift+右键弹出 18 项菜单（此前 dc1ab0a2 构建实机不弹）、普通右键=复制选区/无选区时规范粘贴（行为变化：右击粘贴现也走r28 行尾规范化，多行粘贴无空行）、左键选择/双击选词不受影响。

   - 追加（2026-10-30，第 32 轮）：终端编码选择从标签栏下拉框移入 Shift+右键菜单（用户指图要求）
     ① 移除：标签栏右侧 EncodingCombo（StackPanel 整块，标签栏栅格列 *,Auto→*）、死样式
        ComboBox.EncodingCombo（AppStyles）、ApplyEncodingConfig/OnEncodingChanged/
        OnEncodingDropDownClosed/_applyingEncoding（RestoreTerminalFocus/FocusSelectedTerminal
        多处使用，保留）。
     ② 新增：OpenTerminalContextMenu「更改终端设置…」后加「编码」子菜单——
        ASCII/UTF-8/UTF-16/GBK/GB2312/GB18030 六项，Avalonia 12 无 IsCheckable，用
        MenuItemToggleType.Radio + GroupName=TerminalEncoding 实现单选（点击自动互斥），
        当前值（config.json terminal.encoding，默认 UTF-8）打勾，点击 SetTerminalEncoding 落盘。
     ③ 行为说明：编码值自引入起即仅持久化、未接入 XTerm.NET 解码路径（库 UTF-8 硬编码），
        本轮仅移动入口、不改变该行为（接线解码属后续单独任务）。
     ④ 测试：TERM21 18→19 项 + 编码子菜单 6 项/唯一打勾断言；TabStripHasNoSftpChip 的
        EncodingCombo 断言 NotNull→Null（回归守卫：标签栏不得再有编码框）。
     结果（2026-10-30）：提交 5e57b5b（代码+测试，5 文件 +60/-101）。gate-r32.log 全 PASS：
     Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 228，E2E 15/15。
     发布 win-x64 单文件 R2R 95,085,780 字节
     （SHA256 C4BCA40A8D9B8A5B1EF77BEB8A81224FBB1AFBCCE15E7D30535912187FA1F4BA），
     deploy_r3.py 双份部署+SHA256 校验 OK。Windows 实机待核：Shift+右键菜单含「编码」子菜单
     （当前值打勾、单选、落盘保留）、标签栏右侧无编码下拉框。
   - 追加（2026-10-31，第 33 轮）：终端中英文混排错位/叠印修复（CJK 全角固定 2 格 advance）
     （用户 RDP 反馈：Ubuntu 中文欢迎页等中英文混排行错位叠印，MobaXterm 同屏正常，问“是不是字体不一样”）：
     ① 根因（渲染层，非字体选择）：库 TerminalView 把每个文本 run 画在绝对列位置
        （startX = col×CharWidth，CharWidth 由主字体 Consolas 的“W”度量），但全角（CJK）字符
        （buffer 中 width-2 单元格，单字 run：text.Length==1, cellCount==2）被 glyph 快速路径
        TryBuildGlyphRun 以 text.Length!=cellCount 拒绝 → 走 FormattedText 路径按字体自然度量定位。
        Windows 上 CJK 回退到比例字体 Microsoft YaHei UI，其字宽≠2×Consolas 格宽 → CJK 与后续拉丁
        不再对齐（库 UpdateTextMetrics 注释描述的“网格是等宽、字形不是”同类失效）。MobaXterm 把每个
        CJK 强制摆进恰好 2 格故正常。Linux/headless 上 CJK 回退到捆绑等宽 Noto Sans Mono CJK SC 故不明显。
     ② 修复（库 Iciclecreek.Avalonia.Terminal 渲染层）：TryBuildGlyphRun 新增宽度-2 分支——对单个全角字符
        用整个 FontFamily 的 GlyphTypeface（探针证实它带 CJK fallback，逐名字解析反而解析不到捆绑 avares
        字体）查字形，用 TryGetGlyphMetrics 验证字形真实（非 .notdef），发射单字形且 advance 精确
        2×CharWidth 的 GlyphRun（与宽度-1 路径同源“按格定位”设计），任一条件不满足回退 FormattedText。
        字形仍由现有字体链解析（Windows=YaHei、Linux=捆绑 Noto），只修正其占位宽度，不改字体选择。
     ③ 库打包基础设施（首次把库改动打进 app 构建）：库版本 4.1.0→4.1.1（GeneratePackageOnBuild 产 nupkg），
        nupkg 提交至 avalonia/third_party/iciclecreek/（129KB，使构建自包含），新增 avalonia/NuGet.config
        （clear + 本地 feed third_party/iciclecreek + nuget.org；*.config 被 .gitignore 忽略故 git add -f），
        COMTool.Terminal.csproj 包引用 4.1.0→4.1.1。库另加 InternalsVisibleTo("COMTool.App.Tests")
        供回归测试直接断言全角快速路径。
     ④ 测试：CjkTerminalFontTests.TERM22_WideCjkGlyphIsPinnedToTwoCellAdvances——headless 建终端、
        取内层 TerminalView、调 internal TryBuildGlyphRun("個",2)：断言非 null、单字形、
        GlyphAdvance==2×CharWidth（精度 4），并断言拉丁 "A" 仍 1×CharWidth（防宽度-1 路径被破坏）。
          结果（2026-10-31）：提交 3616e5a（代码+测试+库打包，4 文件）。gate-r33.log 全 PASS：Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 229（含 TERM22），E2E 15/15。发布 win-x64 单文件 R2R 95,085,875 字节（SHA256 C6C16B2F13DD18B2A699032DDA7E424BFF36CBE60E90E6F2B03632CBB36D8C50），deploy_r3.py 双份部署+SHA256 校验 OK。Windows 实机待核：SSH 中文欢迎页等中英文混排行对齐、无错位叠印（对照 MobaXterm）；注：修复在渲染层（全角固定 2 格 advance），headless(Linux) 已验证机制，Windows 字形来自 YaHei 的视觉效果需 RDP 确认。

     （用户反馈 2026-10-30：附 MobaXterm/COMTool 同屏对照图“对比 mobaxterm，下面这排显示偏小了”）：
     ① 实测对照（取参考图像素）：MobaXterm CPU 条 46×22px、小格外高约 26px、图标约 16px；
        我们原先 CPU 条 56×12px、小格 22px、图标 11px、状态栏行高 30px，整排偏薄偏小。
     ② 调整（MainWindow.axaml）：状态栏行高 30→34；MonSeg Padding 6,2→7,3；
        MonSegText 12→13px；MonIcon 11→13px；CPU 轨道 56×12→56×18。
        MainViewModel CpuBarMaxHeight 12→18（roll 列高按此换算）。
     ③ 渲染实测（E2E RM01 截图 rm01-statusbar.png）：小格外高 24px、CPU 轨道 56×18、
        28 列仍正好铺满（首列 x=0、末列右缘=56）；RM01 E2E 点击坐标不受影响
        （监控按钮在侧栏 SFTP 面板底部，与状态栏行高无关）。
     ④ 测试：RM06 既有断言随常量自适应（217 项）；run-ui-e2e.sh 的 RM01 增加
        rm01-statusbar.png 常规截图（视觉核对证据）。
     App 217/217、Core 86/86、Connections 135/135、TestGate 49/49；gate 第 24 轮全 PASS（E2E 15/15，
     gate-r24.log）；提交 5e4677c，发布部署双份。
     Windows 实机 WIN-28 补核：状态栏整排高度/CPU 条厚度/图标大小与 MobaXterm 并排一致。

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
