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

   - 追加（2026-10-31，第 34 轮）：SFTP 抽屉底栏“远程监控”按钮首击丢失修复（隧道阶段按下即翻转）
     （用户 RDP 反馈：第一次连接 SSH，远程监控是开着的（按钮绿色），鼠标点第一下没反应还是绿色，
     得点第二下才生效）：
     ① 取证：VDI run.log 当天会话（10-31 11:47–11:49）零 "Remote monitor toggle" 日志——
        该日志由 pane.MonitorToggled 回调写入（MainWindow 接线处），零日志证明第一按下
        IsMonitorOn 从未翻转，即首击确实被吞（非“翻转了但没显示”）。
     ② 根因：r31 已把按钮改为 ClickMode=Press（按下即切换），但按钮自身的 Click 仍依赖
        hover/pointer-over 状态；刚连上 SSH 后的第一次按下该状态可能尚未建立，
        首击被按钮内部逻辑吞掉，ClickMode=Press 不足以根治。
     ③ 修复：SftpPane code-behind 给 MonitorToggleButton 挂隧道阶段
        PointerPressed（AddHandler(..., RoutingStrategies.Tunnel)）——左键按下时直接调用
        ViewModel.ToggleMonitorFromPress()（翻转 IsMonitorOn，非 suppress 即触发 MonitorToggled，
        上层照常记日志/启停轮询/落盘），并 e.Handled=true 抑制按钮内部 Click（防双切换）；
        保留 IsChecked 的 TwoWay 绑定（状态开关失去 :pressed 视觉/鼠标取焦，可接受）。
        非左键不处理，交回按钮正常逻辑。
     ④ 测试：RemoteMonitorFeatureTests.RM04_MonitorToggleFirstPressAfterConnectTogglesOnceNoDoubleFire——
        headless 建监控开启的 SSH 标签，模拟刚连上后的第一下真实点击（Headless MouseDown/MouseUp）：
        断言首按即切换（按钮变灰、IsMonitorOn=false、tab.MonitorEnabled=false）且恰好一次
        （Assert.Single(toggles)，证明 e.Handled 抑制了按钮自身 Click、无双切换）；第二下再翻回
        （累计恰好两次）。既有 RM04_MonitorToggleFirstRealClickTurnsOffTheRunningMonitor 保持通过。
     结果（2026-10-31）：提交 f9fba2b（代码+测试，3 文件）。gate-r34.log 全 PASS：
     Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 230（含新回归），E2E 15/15。
     发布 win-x64 单文件 R2R 95,085,866 字节
     （SHA256 13E91960A06C1201AD708BCE80F99CF3A40EFA1DCE583C28C0D46BBE53336E3A），
     deploy_r3.py 双份部署+SHA256 校验 OK。Windows 实机待核：刚连上 SSH 后第一次点“远程监控”
     立即变灰/关（第一下就生效，不再需要点第二下）。
     ⑤ RDP 自主实测（2026-10-31 15:58，助手直接在 VDI 192.0.2.163 上操作，xfreerdp 1920×1080）：
        全新启动已部署的 COMTool-Avalonia.exe → 点 devuser@192.0.2.48 会话（15:58:05 连接成功）
        → 点地球图标开 SFTP 抽屉（底栏“远程监控”为绿色=开）→ 第一次物理点击该底栏：
        run.log 15:58:46 立即出现 `Remote monitor toggle: devuser@192.0.2.48:22 -> off (selected=True)`，
        截图底栏变灰；第二次点击 15:59:07 出现 `-> on (selected=True)`，底栏回绿；每次点击恰好一条
        日志（无双触发）。首击丢失问题在 Windows 实机确认已修复。
     ⑥ 同期核对第 33 轮 CJK 对齐（同一 RDP 会话）：终端的欢迎页中英文混排行无叠印；
        `printf` 输出“汉字混排测试|AAAA|”与“abcdefghijkl|AAAA|”两行，管道与竖块像素列完全重合
        （x 均为 968–969 / 973–1002），“中文abc|####|”与“abcdabc|####|”亦重合——全角恰占 2 格。
     ⑦ 附带发现（非本轮缺陷，待用户决定是否修）：窗口按保存尺寸 1443×916 还原时不与屏幕工作区
        取小，在 1280×800 的 RDP 会话里底栏被任务栏遮住（点不到）；已把用户 config 的窗口尺寸
        恢复为原值 1443×916，未改其它配置。

   - 追加（2026-10-31，第 35 轮）：启动恢复窗口尺寸时钳制到主屏工作区（小屏底栏不再被任务栏遮住）
     （用户 2026-10-31 确认：“修，直接钳制并正常保存”）：
     ① 现象与根因：第 34 轮 RDP 实测时在 1280×800 会话里发现——config 保存的窗口尺寸 1443×916 来自更大
        显示器，启动时原样还原、从不与当前屏幕工作区比较，窗口底部（SFTP 底栏“远程监控”）落到任务栏后面，
        按钮完全点不到（点在该位置只会激活后面的窗口）。
     ② 修复（MainWindow.axaml.cs）：ApplyWindowSize() 算出保存宽高后走 FitWindowToPrimaryScreen()——
        取 Screens.Primary，用 WorkingArea/Scaling 换算成 DIP 工作区（已排除任务栏），再交给纯函数
        ClampWindowSizeToWorkArea(desired, workArea, minimum) 取较小值，且不低于现有 500×320；
        窗口是 WindowDecorations=None 无边框模式（自绘标题栏，Width/Height 即外框尺寸），不额外扣边框；
        取不到屏幕信息（headless/平台未就绪）时行为不变；钳制值即 _lastNormalWidth/Height，随正常退出落盘
        覆盖旧值（用户选定语义）；WindowStartupLocation=CenterScreen 会按新尺寸重新居中。
     ③ 测试：新增 WindowSizingTests（7 项）——纯函数 5 项（放得下不改、超大缩到工作区、单轴溢出只缩该轴、
        工作区小于最小值时保底最小值、工作区未知不改）+ headless 真窗 2 项（seed 4000×3000 启动后
        window.Width/Height 不超过 Screens.Primary 工作区；seed 900×600 仍按 900×600 打开）。
     ④ 结果（2026-10-31）：提交 f78871d。gate-r35.log 全 PASS：Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 237（含 7 项 WindowSizingTests），E2E 15/15；RESULT PASS×2。
     ⑤ 实机 RDP 复验：1280×800 会话（任务栏 40px、工作区 1280×760）+ config 预置 1443×916 启动 → 窗口启动即钳到 1280×760，
        底栏完整可见；连 SSH（17:30:41）→ 开 SFTP（底栏绿色=开）→ 首击“远程监控”立即变灰，run.log 17:31:29
        `Remote monitor toggle: devuser@192.0.2.48:22 -> off`，再点回开 17:31:48 `-> on`；正常关闭后 config 记录
        `window.width/height = 1280×760`（钳制值落盘，符合选定语义）。发布 win-x64 单文件 R2R 95,085,865 字节
        （SHA256 543912EB5CC9148819C3456DE9083D28CCDBF0833E711418E6D33DDE3B5AE260），deploy_r3.py 双份部署+SHA256 校验 OK。
        测试后已把用户 config 窗口尺寸恢复为原值 1443×916（侧栏 visible=true、monitor=true 亦保持原状）。

    - 追加（2026-11-02，第 38 轮）：终端菜单实用批三落地——复制带格式文本（RTF）/ 事件日志 / 打印
      （剩余 6 项中的前 3 项实用类；终端显示/语法高亮/特殊命令 3 项参考类待用户 MobaXterm 子菜单截图后定语义）：
      ① 复制带格式文本（RTF）（TERM-26）：选中后菜单项启用（此前置灰）。新服务 TerminalRtf.BuildRtf(term, 字号px, 字体族)：
         SelectionManager.TryGetSelection 取选区（X=列/Y=绝对 buffer 行，源码注释坐实）→ 逐行
         TryGetRowSpan 取列跨度 → 逐格 BufferCellExtensions.GetForegroundColor/GetBackgroundColor
         （null=默认色，回退 ColorSnapshot 的 Foreground/Background）→ 相邻同色格合并 run → 生成
         纯 ASCII RTF（{\rtf1\ansi，\uNNNN 转义非 ASCII，颜色表去重，\fs=px×1.5 半点）。
         剪贴板：DataTransfer 三格式——"Rich Text Format"（Platform 名精确匹配 Windows 预注册
         CF_RTF=0x0305，Word/WordPad 可还原；Avalonia Win32 剪贴板对平台名走 RegisterClipboardFormat，
         源码坐实）+ "text/rtf"（同为 Platform 格式——X11 上即 MIME atom；见④(a) 为何不用 Application
         格式）+ 纯文本兜底。实机核验经 --selftest-r38：PowerShell Get-Clipboard -Format RTF 回读。
      ② 事件日志（TERM-27）：菜单「事件日志」启用，弹对话框显示 run.log 尾部 200 行
         （AppLog.Path，只读 TextBox 等宽字体）+「刷新」按钮重读。复用 ClientSizedWindow 定客户区。
      ③ 打印（TERM-28）：菜单「打印」（Ctrl+Shift+P 仅标签，与既有 Ctrl+Shift+S 惯例一致）。
         Avalonia 12 无打印 API → Windows 专用 PowerShell 辅助（WinForms PrintDialog+PrintDocument，
         文本先落临时文件避免命令行长度限制；OperatingSystem.IsWindows() 门控，非 Windows 返回
         "打印仅支持 Windows 系统"）。打印内容=选区优先，无选区取整屏转录（复用 TerminalTranscript）。
      ④ 实机自检扩展 --selftest-r38：RTF 剪贴板 CF_RTF 回读（系统 PowerShell Get-Clipboard -Format RTF，
         绕过 Avalonia 核验 CF_RTF 真实注册）/ 事件日志对话框标记行核验 / 打印脚本可解析性核验
         （不实际弹系统打印对话框，避免干扰用户桌面）。
         结果（2026-11-02）：新增 5 测试全过——TERM26_RtfBuilder（builder 纯函数：颜色 run/CJK \uNNNN/
         字面 \ { } 转义/无选区 null）、TERM26_RtfCopy_MenuItem（无选区置灰→SelectAll 启用→点击后
         headless 剪贴板内存 stub 完整 round-trip 回读 RTF+纯文本）、TERM27_EventLog（尾部 200 行/
         刷新/关闭）、TERM28_Print_Script（脚本内容+单引号转义+token 替换/非 Windows 门控）、
         TERM28_Print_MenuClick（菜单点击→状态栏“打印仅支持 Windows 系统”）；TERM21 启用态断言
         同步更新（RTF 无选区置灰、打印/事件日志转启用）。两个坑（headless 复现修复）：
         (a) Avalonia 12 application 格式标识符只允许字母/数字/点/连字符，“text/rtf” 带斜杠抛
         ArgumentException（DataFormat 源码+失败消息双坐实）→ 改 CreateStringPlatformFormat("text/rtf")
         （platform 格式不校验原样传平台：X11 即 MIME atom，Win32 仅多注册一格式无副作用）；
         (b) 测试源里字面反斜杠断言被写成了 4 个反斜杠字符（转义翻倍），RTF 中一个字面 \ 实为 2 个
         反斜杠字符，od -c 逐字节核对后修正。
         gate-r38.log 全 PASS：Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/
         App 255（含新增 5 项），E2E 15/15；RESULT PASS×2。
         实机首跑（3160EDA0）暴露两个实机级问题并修复（gate-r38b/c 均全 PASS）：
         (c) PS 5.1 的 Get-Clipboard -Format 仅支持 Text/FileDropList/Image/Audio（无 RTF，实机
         报错坐实）→ 回读改 WinForms Clipboard.GetDataObject()+DataFormats.Rtf 直读 CF_RTF；
         顺带 TerminalPrint.TrimNoise 剔除 PowerShell CLIXML 进度包裹再写日志。
         (d) **CF_RTF 编码截断**（关键）：首跑回读 FORMAT 列表含 "Rich Text Format"（CF_RTF 已
         注册）但 GetData 只回 1 字节 "{"（RTF-LEN=1）——Avalonia Win32 剪贴板把**所有**字符串
         格式一律按 UTF-16 写 HGlobal（OleDataObjectHelper.WriteStringToHGlobal 源码坐实），而
         CF_RTF 规范要求 ANSI(CP_A)，标准读取方（Word/WinForms）按 ANSI 解码遇 NUL 即截断。
         修复：Windows 上 RTF 写入改 Win32 P/Invoke 直写（Win32RtfClipboard：CF_RTF ANSI +
         CF_UNICODETEXT UTF-16，内存先备齐再 EmptyClipboard，失败不毁原剪贴板）；X11 仍走
         Avalonia DataTransfer（X11 字符串 UTF-8、MIME 正常）。分发逻辑 TerminalRtfClipboard。
         实机迭代（3160EDA0→116BDAC5）依次坐实 (c)(d) 并修复。
      ⑤ selftest 回读脚本三连坑与 VM user32 异常（00:25/01:04 实机 + VM 诊断）：
         (e) C# 拼接笔误——三行把 PowerShell 语句起始 ;'FORMATS=' 写成 ';FORMATS='（引号分号
         颠倒）→ ParserError，且错误被 TrimNoise 连同 CLIXML 吞掉；};else{ 分号断开 if/else
         绑定（运行时 else 未识别为命令，走 if 分支时潜伏不爆）。
         (f) 本 VM user32.dll(x64) 导出表被剥离 IsClipboardFormatPresent/GetClipboardFormatName/
         RegisterClipboardFormat（GetProcAddress 枚举=0 坐实；OpenClipboard/SetClipboardData/
         GetClipboardData/Global* 均存在）→ WinForms 名称基查找全灭：GetFormats 回退显示
         Format773、GetDataPresent("Rich Text Format")=false（名称未注册→动态注册出 ≥0xC000
         新 id）。Word 等按 CF_RTF 常量 id(0x0305) 读取不受影响。
         (g) WinForms OLE 读（GetDataObject().GetData）对**裸** SetClipboardData 写入的格式
         不可见（实测 RTF=ABSENT，而 WinForms 自写的 OLE 数据可读）——回读必须走与 Word 相同
         的 API 链：OpenClipboard→GetClipboardData(0x0305)→GlobalLock→ANSI 解码（id 基）。
         修复（c99d890）：selftest 回读改 id 基 P/Invoke 直读；WinForms GetFormats 仅留作信息行。
         VM 先用与 Win32RtfClipboard 完全相同的裸写 P/Invoke 模拟端到端验证
         （WRITE=True / RTF=LEN=65 HEAD 完整）后再发布。
      ⑥ **实机终验（02:06，CFD2DDF1）三项全过**：
         rtf-clip write=True code=0 hasMarker=True，RTF=LEN=64（完整 RTF 含标记，
         HEAD={\rtf1\ansi{\colortbl ;\red255\green0\blue0;}R38-RTF-020610\par}），
         UTF16=LEN=14（纯文本标记完整）——CF_RTF 以完整 ANSI 落在剪贴板，Word/WordPad 可还原；
         eventlog hasMarker=True textLen=19375（对话框尾部 200 行+刷新可用）；
         print supported=True scriptParse code=0 out=[PARSE-OK]（打印脚本可解析）。
         gate-r38e.log 全 PASS（App 255，E2E 15/15）；提交链 f59fa7e→56e9131→bb29b9f→
         4dfc151→c99d890；部署 CFD2DDF1 双份 SHA 校验 OK、无残留进程。**
   - 追加（2026-11-02，第 37 轮）：终端右键菜单三落地——查找 / 字体设置 / 录制终端输出到文件
     （用户 2026-10-31 反馈：附终端 Shift+右键菜单截图“有些是没实现的吧？”→ 确认 9 个置灰占位项，
     用户拍板“全部 9 项都实现”，本轮先落前三项；剩余 6 项 RTF/终端显示/语法高亮/打印/特殊命令/事件日志 后续轮次）：
     ① 查找（TERM-23）：Ctrl+Shift+F 或终端菜单「查找」在终端区底部弹出内联查找条（输入框+n/m 计数+上一个/下一个/关闭，
        默认隐藏，ZIndex 叠加不改变既有布局/E2E 像素探针）。引擎层直接复用库自带 BufferSearch
        （FindInBuffer/FindNext/FindPrevious/ClearSearch，XTerm.NET 3.0.0 原生）；实测发现 FindInBuffer 只登记命中
        不定位（SearchCurrentIndex 保持 -1），首次搜索后补一次 FindNext 定位首个命中（对齐 MobaXterm）；
        默认大小写不敏感；Enter=下一个、Shift+Enter=上一个、Esc/关闭按钮清高亮收起；切标签自动收起并 ClearSearch
        （SelectedTabChanging 钩子）。OnWindowKeyDown 隧道阶段置顶拦截：查找条可见且焦点在其内时 Enter/Esc 归查找条、
        其余键直接放行给输入框（不误触宏热键/Ctrl+空格）；无命中显示 0/0，截断命中显示 n/m+。
     ② 字体设置（TERM-24）：菜单「字体设置」弹对话框（字体族 ComboBox 5 候选：Consolas/Cascadia Mono/Courier New/
        Microsoft YaHei UI/Noto Sans Mono CJK SC + 字号 NumericUpDown 8–48，与 Ctrl+滚轮 同边界同 config 键），
        确定后 BuildTerminalFontFamily 对全部已打开终端即时生效（首选族 + YaHei + 内置 Noto CJK 回退链恒保留，
        保证 r33 中英混排 2 格对齐不被单族字体破坏）并落盘 config.json（terminal.fontFamily 新增键，旧配置缺键默认
        Consolas）；Ctrl+滚轮调字号同样同步字体族（ApplyTerminalFontSizeToAll 补设 FontFamily）。
     ③ 录制终端输出到文件（TERM-25）：菜单项首次点击弹保存框选文件→订阅库 OutputReceived 事件（原始输出字节含 ANSI
        序列）实时追加写盘（_recordingGate 锁保护，事件可能在读线程触发）；录制中菜单项文案动态变「停止录制终端输出」；
        再点停止关文件；关闭对应 tab（CloseTab/DisposeDetachedTab 双路径钩子）自动停止落盘防悬挂句柄；
        开始/停止+字节数写 run.log。
     ④ 测试：新增 TerminalMenuFeatureTests 10 项（TERM23：查找条打开/命中 2/2/下一上一定位与回绕/0/0/清空关条/
        Ctrl+Shift+F 隧道路由；TERM24：字体族构造 3 项含空值回落与 CJK 回退链、config 往返与缺键默认、应用到全部终端；
        TERM25：录制捕获 InMemoryPty 输出字节、关 tab 自动停录）；TERM21 菜单断言更新（查找/字体设置/录制输出
        由置灰改可用）。
     ⑤ 结果（2026-11-02）：提交 6df0c88。gate-r37.log 全 PASS：Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 249（含新增 10 项），E2E 15/15；RESULT PASS×2
     ⑥ 实机验证（1920×1080，2026-11-02；中途用户改为直接操作电脑，RDP 停用，后续走 SSH）：连 SSH（run.log 10:01:52 terminal attached，pty 210x56）→
        Ctrl+Shift+F 弹出底部查找条，搜 R37_SEARCH_TEST_12345 命中 1/2 且首命中橙色高亮 ✓；
        Shift+右键菜单「查找/字体设置/录制终端输出到文件」三项均黑色可用 ✓（截图核对）；
        Shift+右键→字体设置 对话框弹出、字体族 Consolas 正确。
        【发现缺陷】字号框实机显示残缺 "1"（用户坐席确认）。排查：headless 探针 Value=13.5/Text="13.5" 均正常、
        点"确定"后 config.json 正确落盘 fontSize:13.5（内部值无错），定性为纯显示/渲染时序问题；
        录制输出的实机落盘未做端到端（单测已覆盖写文件/关 tab 停录，菜单接线已实机核对）。
     ⑦ 字号显示修复第一轮（提交 062f1ae）：初判根因——Fluent 主题模板内 TextBox.Text 是 TemplateBinding 到
        NumericUpDown 的 Text 字符串属性（构造期只设 Value 时首帧文本未同步，headless 同步 RunJobs 不可复现）。修复：构造期显式
        Text=InvariantCulture("13.5") + dialog.Opened 后强制重同步 Value/Text 双保险 + [FontDlg] 诊断日志
        （实机打开可经 run.log 读真实 Value/Text/FontSize/Bounds）；新增测试接缝 LastFontDialogForTests/
        ShowFontDialogForTestsAsync 与回归测试 TERM24_FontDialogShowsConfiguredFontSizeAndOkKeepsDefault
        （对话框打开→Value=13.5/Text="13.5"→点"确定"→config 无副作用）。
        gate-r37fix2.log 全 PASS：Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 250（含新增 11 项），E2E 15/15；RESULT PASS×2
        发布 SHA256 D2BAE2E2…7F8C（95,098,192 字节）双份部署+校验 OK。
     ⑧ 实机复验（一）（2026-11-02 15:10，用户坐席操作）：字号框仍显示 "1"，第一轮修复未生效。[FontDlg] 诊断日志证明
        数据面完全正常：value=13.5 text=[13.5] tbText=[13.5] spinFontSize=14 tbFontSize=14；而
        spinBounds=(50,0,90,32)、tbBounds=(-1,-1,22,32)——模板内 TextBox 实际排布仅 22px 宽。
        【真根因】12.1.4 Fluent 主题的 ButtonSpinner 模板为 DockPanel 横排两个 MinWidth=34 的按钮（共 68px），
        NumericUpDown Width=90 减去边框 2px 与按钮 68px 后内部 TextBox 仅剩 20px（+Margin=-1 即 22px），
        "13.5" 被裁切只剩 "1"。属几何布局问题，非数据/时序问题；headless 测试只断言属性值未断言渲染宽度，故未拦截。
     ⑨ 字号显示修复第二轮（根因修复，提交 f3a3623）：NumericUpDown Width 90→120（120-2-68=50px 内容区，内部
        TextBox 约 52px，足够显示 "13.5"）；回归测试新增断言：对话框内 TextBox 的 Bounds.Width >= 45。
        gate-r37fix3.log 曾出现一次 test host SIGSEGV（exit 139，TERM02 调色板测试未出结果、249/250、app-tests FAIL）——
        该测试单独跑与连续 3 次全量重跑均过；项目已设 DisableTestParallelization=true 排除并行竞态，判定为环境性偶发；
        gate-r37fix4.log 重跑全 PASS：Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 250，E2E 15/15；RESULT PASS×2。
        发布 SHA256 0EF0F3A4…E53A00（95,098,221 字节）双份部署+校验 OK。
     ⑩ 实机复验（二）（2026-11-02 16:30，用户坐席操作）：字号框完整显示 13.5 ✓（第二轮修复生效）；
        但用户反馈「取消与确定显示不全，底部一些内容没显示」——对话框固定 Height=240 小于实际内容高度
        （边距 20+行 32+12+32+12+说明 2~3 行+12+按钮 28+边距 20 ≈ 202~220，加标题栏后溢出），
        按钮行被窗口底缘裁切。此问题自 r37 首版对话框即存在，此前被字号 "1" 问题掩盖。
     ⑪ 字号对话框底部裁切修复（提交 6b92cd0）：dialog Height 240→320（留 ~55px 余量，CanResize=false
        固定尺寸不变）；回归测试新增断言：确定/取消按钮 Bounds.Bottom <= dialog.ClientSize.Height
        （headless 布局即可拦截同类"固定高度不足裁切内容"问题）。gate-r37fix5.log 全 PASS：
        Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 250，E2E 15/15；RESULT PASS×2。
        发布 SHA256 1BC51A5E…0C5302DC（95,098,177 字节）双份部署+校验 OK。
     ⑫ 实机复验（三）（17:09，用户坐席，构建 1BC51A5E/Height=320）：13.5 ✓、按钮不再裁切，
        但按钮下方留 ~134px 空白（截图像素分析确认），用户反馈「还是不完整」。
     ⑬ 高度自适应（提交 77a4820）：dialog Height 改 SizeToContent.Height（按当前机器字体/DPI 实测
        排版取窗高）；回归测试升级为 TranslatePoint 断言（按钮完整落在客户区 + 底部间隙 <=45px）。
        gate-r37fix7.log 全 PASS（App 250，E2E 15/15）；发布 3E826192… 双份部署。
     ⑭ 实机复验（四）（17:56，用户坐席，构建 3E826192）：用户反馈「不行」；[FontDlg] 日志
        dlgBounds=420,720 —— Windows 首次布局时客户区宽度尚为 0，说明文字（约 412px 长、无固定宽度）
        折成 ~44 行（每行 1 字），内容高度被量成 ~700px。headless 宽度约束正常（420）故 250 项全绿，仅 Windows 现形。
     ⑮ 最终修复（提交 1f165ec）：说明 TextBlock 固定 Width=380（=420-20*2 边距），高度测量与窗口宽度
        解耦，任意平台/DPI 恒定 2 行。gate-r37fix8.log 全 PASS（App 250，E2E 15/15）；发布 888878F8… 双份部署+校验 OK。
     ⑯ 实机复验（五，待用户坐席操作）：新包打开「字体设置」，窗口应紧凑（约 230px 高），13.5 显示、
        按钮贴底部 20px 边距；打开对话框后可经 SSH run.log 的 [FontDlg] dlgBounds 行核对实际窗口高度。
      ⑰ 免 RDP 实机自检通道（提交 99525f0）：本机无 xfreerdp 且非 root 装不了，改在应用内加
         `--selftest-fontdlg` 启动参数——启动 1.5s 后自动打开字体对话框，等校正链执行完，把
         clientSize/dlgBounds/确定/取消按钮底边与间隙逐行写 run.log 后自退出（exit 0）。经 SSH 读
         run.log 即得实机真实几何，比截图目检更精确。同轮改显式 ClientSize(420×230) 初始 +
         Opened 后按钮底边校正（目标=底边+20，ContextIdle 复验≤3 防乒乓）。实机自测 20:11
         （构建 9097C284）：初始 420×230（720 巨幅窗已消失 ✓）、buttonBottom=180、target=200，
         但 4 次校正全部被弹回 230，最终 gap=50 → 显示后校正在实机同样输给回环。
      ⑱ 根因坐实（诊断构建 CE483EC8，实机自测 20:24）：ClientSize 观察者 + Resized 事件 trace
         拿到完整乒乓序列：set 200(Application) → Resized 230(Layout) → set 200 → Resized 230…
         调用栈：陈旧布局趟 ArrangeSetBounds(旧值230) → ResizePlatformImpl(Layout) →
         WindowImpl.Resize → SetWindowPlacement → WM_SIZE → HandleResized → ClientSize 属性被写回
         230。即：布局根（TopLevelHost，internal 不可达）自初始显示缓存了旧测量约束，显示后
         任何 ClientSize 校正都会触发一轮按旧约束的布局趟并把旧尺寸推回平台——校正永远输给
         反馈环，窗口停在创建时的 ClientSize。headless 复现同样序列（230/195 交替，
         headless 平台 Resize 经 Send 优先级异步 Post）。
      ⑲ 终版方案：显示前预测量定高（提交 9d78505 + 69b9498，构建 E911378A）：
         ShowDialog 之前把内容面板临时挂入主窗根（主题/字体已生效的真实排版上下文）里一个
         420 宽（与对话框客户区同宽，换行一致）、不可见的 Grid，先 UpdateLayout 让样式生效，
         再显式 content.Measure(420×∞) 量出精确内容高 probeH，以 ceil(probeH)+2 一次性创建窗口
         （SetClientSize 经 FontSettingsWindow 子类——12 里 TopLevel.ClientSize setter 是
         protected，CS1540）；打开后无显示后 resize → 无回弹源。显示后按钮底边校正
         （CorrectFontDialogHeight，ContextIdle 复验≤3）保留为兜底。
         两个实现坑（headless scratch 复现修复）：(a) Avalonia 12 视觉子树不自动迁移父级，
         探针摘下后必须先 probeHost.Children.Remove(content) 再挂 dialog，否则抛
         "already has a visual parent Grid"（异常被 async 吞进 Task → 对话框永不打开、测试超时）；
         (b) 布局趟会把探针 DesiredSize 钳到所在行高（探针落入 RootGrid 固定 35px 标题栏行，
         实测 probeH=35 走 230 fallback），故必须显式 Measure 取确定性值。
         TERM24 改收敛泵（≤10 次 RunJobs+UpdateLayout 至稳定）+ 不变量断言：按钮不裁切、
         客户区高 <400（拦 720 回归）、底部间隙 ≤75（拦 134px 悬空；headless 平台回环最坏
         停在初始值，实机像素级贴合由 selftest 核验）。
         **实机自测 21:18（E911378A）：pre-measure probeH=200 dlgH=202，clientSize=420×202，
         按钮底边 180，gap=22（20px 边距 + 2px 预测量余量）——五轮实机复验收敛：
         裁切(240)/悬空(320)/巨幅窗(720×2)/gap50(230) 全部消失。gate-r37fix11 全 PASS：
         Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 250，
         E2E 15/15；RESULT PASS×2。构建 E911378A 双份部署+SHA 校验 OK。**

   - 追加（2026-10-31，第 36 轮）：SSH 浏览器地球图标跟随当前选中标签（对齐 MobaXterm）
     （用户 2026-10-31 反馈：附 COM99 标签激活但左栏仍显示地球的截图，“切换到COM的标签，SSH浏览器不会消失，这点需要对齐mobaxterm”）：
     ① 根因：IsSshBrowserVisible 由 connectedSsh.Count > 0 驱动（MainViewModel.UpdateConnectionStates/LoadSessions），
        只统计在线 SSH 会话、与当前选中标签无关；切到 COM 标签时 SelectedTab setter 已把 SidebarTab 从
        SshBrowser 退回 Sessions，但第四格图标条件仍为真，于是残留在左栏。
     ② 修复（MainViewModel.cs）：新增 _connectedSshSessions 保存最近一次连接状态集合；新方法
        RefreshSshBrowserVisibility() 计算 IsSshBrowserVisible = 选中标签有 SftpPane（即 SSH 标签）且该标签的
        SessionId 在在线集合中；SelectedTab setter、UpdateConnectionStates、LoadSessions 都调用它。
        语义保持：断线仍隐藏（在线集合不再包含该会话）、快速连接（无保存会话）仍有图标（按 tab.SessionId 判断）；
        切回仍在线的 SSH 标签图标自动恢复，无需重新登录。
     ③ 测试：SidebarRailTests.UI16_SshBrowserIconHidesWhenASerialTabIsSelected（新增 AddSerialTab 辅助）——
        SSH 连上图标出现 → 切 COM 标签图标消失且 SshBrowserPane 为空 → 切回 SSH 标签图标重现 → 断线后切回不出现。
     ④ 结果（2026-10-31）：提交 b091e5b。gate-r36.log 全 PASS：Core 89/Sftp 42/Terminal 11/Connections 150/Zmodem 32/TestGate 49/App 238（含新增 1 项），E2E 15/15；RESULT PASS×2
     ⑤ 实机 RDP 复验：1920×1080 会话（窗口最大化，config 预置 1443×916 maximized=true）实机操作：连 SSH（17:58:23）后第四格橙色地球出现；
        点 COM99 串口标签（“Connected COM99”）→ 地球立即消失、左栏回 User sessions，只剩三格；切回 SSH 标签 → 地球重新出现、
        点击仍能展开 SFTP 面板；SFTP 展开状态下再切 COM99 → 面板退回会话列表且地球消失；正常关闭后 config 仍为
        1443×916/maximized=true（与测试前一致，无需恢复）。发布 win-x64 单文件 R2R 95,085,894 字节
        （SHA256 5FD6D7B54ACF979F806E1E09A639A2962268E7521C577983FC4FEB5CD357E53），deploy_r3.py 双份部署+SHA256 校验 OK。
        证据截图 rdp-r36-rail-compare.png：左=SSH 选中（有地球）、中=COM99 选中（无地球）、右=切回 SSH（地球恢复）。

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

   - 追加（2026-11-03，第 39 轮）：对话框「取消/确定」汉字底部被裁修复（用户 RDP 截图逐像素坐实）
      ① 缺陷与根因（用户反馈「取消与确定这两个字显示不完全，底部有一部分没显示出现」，415×230 截图）：
         Avalonia 12.1.4 Fluent 主题的 Button 默认 HorizontalContentAlignment / VerticalContentAlignment
         均为 **Stretch**：(a) 字符串内容被包成 TextBlock 横向拉伸 → 文字在按钮里**左对齐**
         （截图逐像素：按钮 x229..308 宽 80px，「取消」墨迹仅占 x238..264，右侧留白 44px）；
         (b) 固定 `Height=28` 时内容内框 = 28-(5+6)=**17px**，而 Windows 14px 微软雅黑的 CJK 行高
         实测 **19px** → 汉字底部 2px 被内框裁掉（截图「取消」墨迹 y193..203，内框下沿 y204，
         正好止于裁切线，底部缺两行）。headless 用 Inter（14px 行高 15.26，内框 17 可容纳）故不可复现。
      ② 修复：新增 `CreateDialogTextButton(text, minWidth)` 帮助方法（MinWidth、MinHeight=28、
         Padding 12,4、两轴 Center、**不写死 Height** → 由真实行高+内边距自适应撑开，DPI/字体变化不再裁切）。
         字体设置「确定/取消」、事件日志「刷新/关闭」统一改用之。**不改全局 `Style Selector="Button"`**：
         Avalonia 中 ToggleButton/CheckBox/RadioButton 派生自 Button，全局居中会把复选框文字/ComboBox
         内容一起居中，风险大 → 逐处显式设置。
      ③ 同类缺陷一并修（MainWindow.axaml）：查找条 3 按钮（Height 26→MinHeight 26、Padding 8,4、两轴
         Center）、宏横幅「停止」（MinHeight 20、Padding 0、FontSize 11、两轴 Center）、Zmodem Cancel
         按钮补 VerticalContentAlignment=Center。
      ④ 实机自检扩展：`--selftest-fontdlg` 新增逐按钮 `pad/hca/vca/cp/tb/tbDesired/tbSqueezedH/
         tbStretchedW` 定量日志 + 全主窗「字符串按钮」挤压/拉伸扫描（`main window string-button layout
         issues: N`）+ SaveVisualPng 落盘 fontdlg-content.png / main-content.png（免截图目检通道）。
         （RenderTargetBitmap 在本 VM 上 CJK 字形渲染为黑块、ASCII 正常，故最终以 [SelfTest] 布局数值
         为证据；VM 会话锁屏时 CopyFromScreen 截屏产出 0 张。）
      ⑤ 测试：新增 TERM24_FontDialogButtonLabelsCenteredAndNotClipped（已做红验证：旧代码失败于
         Expected: Center / Actual: Stretch）、TERM23_SearchBarButtonsCenterLabelsWithoutClipping；
         TERM27_EventLog_DialogShowsTailAndRefreshes 增加「刷新/关闭」居中+不写死高度+未挤压断言。
      ⑥ 实机自测（构建 A36BF414，09:44:11）：`clientSize=420,202`；`button[取消] bounds=0,0,80,29
         bottomY=181 gap=21`；`btn[取消] pad=12,4,12,4 hca=Center vca=Center cp=.../54,29
         tb=26,5,28,19 tbDesired=28,19 tbSqueezedH=False tbStretchedW=False`（确定同，文字块中心
         x=26=(80-28)/2 水平居中）；`post-layout: stable ... attempt=0`（此前 4 次无效 resize 循环消失）；
         `main window string-button layout issues: 0`（全主窗无同类裁切/拉伸）。
      ⑦ gate：gate-r39btn.log 首跑 App 测试进程偶发崩溃（exit 134，92/257 后
         TestPipelineException；单独重跑 257/257 全绿——同 r37 记录的 SIGSEGV/139 环境性
         偶发）；重跑 gate-r39btn2.log 全 PASS：Core 89/Sftp 42/Terminal 11/Connections
         150/Zmodem 32/TestGate 49/App 257（含新增 2 项），E2E 15/15；RESULT PASS×2。
         发布 A36BF41446D596A6960ADCBB3B7D4AFD287877617709CC51D021CE6DA3947E0F
         （95,130,988 字节）双份部署+SHA 校验 OK、无残留进程。
      ⑧ 用户实机确认（2026-11-03）：「取消/确定」字底完整、文字居中，问题关闭。

## 第 40 轮：r39 三子菜单（终端显示 / 语法高亮 / 特殊命令）

目标：终端 Shift+右键菜单原 3 个置灰叶子（终端显示 / 语法高亮 / 特殊命令）升级为
可操作子菜单，1:1 对齐 MobaXterm 22.1（顶层 19 项 + 4 分隔线顺序/文案不变，仅三叶子变子菜单）。

### 语义定稿（用户截图确认）
- 终端显示 6 项全做：重置终端输出=XTerm RIS 全复位；清除终端回滚=Buffer.ClearScrollback()
  （保留当前屏）；设置终端标题=对话框改 tab.Title（会话级不持久化，Title 改可写 ObservableProperty）；
  切换终端滚动条=toggle 模板 ScrollBar（显 14px/隐 0）；全屏终端(F11)=F11 逻辑提取为 ToggleFullScreen()；
  只读终端=per-tab 标志 + tunnel 阶段 KeyDown 守卫(e.Handled) + PasteNormalizedAsync 实例化加守卫
  （粘贴菜单项同步加 !readOnly）。
- 语法高亮 6 项 Radio 单选（GroupName=TerminalSyntaxHighlight）：默认 / 错误·警告·成功关键字 /
  Unix shell 脚本 / Cisco(网络配置) / 逐行（置灰留后，语义待定）/ SQL 语法。全局单值，
  落 config.json 的 terminal.syntaxHighlight，切换即对所有终端 ApplyToAll（含回滚历史重染）。
- 特殊命令按会话类型矩阵：SSH→RFC4254 协议信号 SendSignalRequest("INT"/"TERM"/"KILL"/"HUP"…)+
  SendBreakRequest（ShellStream 不公开通道，反射私有字段 _channel，运行时类型
  Renci.SshNet.Channels.ChannelSession，pin 2026.0.0，失败静默 false）；串口→硬件 Break
  （.NET10 SerialPort.BreakState 布尔脉冲，无 Break()/BreakDuration）+控制字节(SIGINT 0x03/
  SIGTSTP 0x1A/SIGQUIT 0x1C)；TCP/Telnet→仅控制字节；Local→全置灰。顶层 5(Break/SIGINT/SIGTERM/
  SIGKILL/SIGHUP)+更多信号(14，RFC4254 其余)+IGNORE message（置灰留后）。

### 高亮实现路径
- 弃流过滤器（pty 阻塞读下无换行提示符会滞留），改缓冲区后处理 + 订阅 XTerm.Terminal.LineFed
  事件（与解析器同线程同步触发、零延迟、不碰字节流）。只染默认前景色格子(fg==256/mode0)，
  程序自带颜色不覆盖；watermark 只处理新增行；方案切换 ApplyToAll() 全量重染(含回滚)；
  alt 屏(vim/less)跳过；Resize 重置水印；CJK 宽字符按 cell 宽度映射，按物理行着色
  （跨折行不匹配，接受局限）。
- 规则集照抄 MobaXterm 默认 CustomSyntax 原文（wezterm issue #4348 导出的 MobaXterm.ini）：
  EWS(红 error 族/黄 warn 族/绿 success 族/品红 IP·localhost/青 info 族)、Unix shell(注释绿/
  命令替换红/变量黄/控制词品红/内建青)、Cisco(!注释蓝/行首 no+shutdown 红/up·enabled 绿/
  IP·MAC·接口号黄/配置词品红/show 系青)、SQL(五色关键字表)。抢占式着色：先声明者独占字符区间，
  短语规则须排在单词规则前（否则 "permission denied" 的 "denied" 被单词规则先占）。
  .NET 正则无 \K，Cisco 行首 no 用 ^\s*no\b（测试侧 Trim 比较）。
- 接线踩坑：XTerm 核心在终端入树/布局后才创建，单测中 attach 可能先于布局使 terminal.Terminal
  为 null → AttachTerminalHighlighter 加空守卫+Loaded 优先级延迟重试一次（修 TERM21 回归）。

### 关键文件
- COMTool.Terminal/Highlight/SyntaxHighlightRules.cs（纯规则引擎 FindSpans）+ TerminalHighlighter.cs（LineFed 着色器）
- COMTool.App/Services/SpecialCommands.cs（SpecialCommandDef + TopLevel(5)/More(14) 表 + IsAvailable 矩阵）
- COMTool.Connections/SshConnection.cs（TrySendSignal/TrySendBreak 反射段）+ SerialConnection.cs（SendBreakAsync）
- COMTool.App/Views/MainWindow.axaml.cs（14 处接线：_highlighters 字典、隧道只读守卫、AttachTerminal 挂着色器、
  两处 tab 关闭释放、F11 提取、粘贴实例化+守卫、三子菜单 builder、RunR39SelfTestAsync + --selftest-r39 入口）
- COMTool.App/ViewModels/MainViewModel.cs（TerminalTab.Title 可写 + IsReadOnlyTerminal）
- COMTool.Core/Config/ComtoolConfig.cs（TerminalConfig.SyntaxHighlight 字段）

### 测试（TDD 先红后绿）
- COMTool.Terminal.Tests：SyntaxHighlightTests 14 项（规则引擎 8 + LineFed 着色器 6，含默认色编码探针）
- COMTool.App.Tests：TerminalR39FeatureTests 9 项（TERM29×6 显示子菜单结构/复位/回滚/标题框/滚动条/全屏/只读，
  TERM30×1 高亮接线，TERM31×2 可用性矩阵+控制字节派发）
- COMTool.Core.Tests：ConfigTests +1（syntaxHighlight 往返 + 旧配置缺键默认 default）
- 回归：TERM21 等既有菜单顺序/文案断言全绿

### gate（gate-r39b.log，2026-11-03）
- 全 PASS：Core 90 / Sftp 42 / Terminal 25 / Connections 150 / Zmodem 32 / TestGate 49 / App 266（含 9 新）/ E2E 15/15
- RESULT PASS×2，GATE EXIT=0

### 实机自测（VM 192.0.2.163，--selftest-r39，构建 1E4E76D9）
- 高亮 probe：Error/crash fg=1(红)、warning fg=3(黄)、success fg=2(绿)、192.168.1.1/localhost fg=5(品红)、
  非关键字 fg=256(默认) → ok=True
- clear-scrollback：YBase 22→0，screenRetained=True
- scrollbar toggle：ok=True bars=1 visibleAfterShow=True
- fullscreen：Normal→FullScreen→Normal ok=True
- set-title：result=R39-标题自检，dlgClientSize=340×142（Win32 字体度量，无裁切）
- readonly：blocked=True
- special-cmd 矩阵：Local 0/5+0/14、Serial 2/5+2/14、Tcp 1/5+2/14、Telnet 1/5+2/14、Ssh 5/5+14/14
- 本地 SIGINT 派发 → unsupported/failed（错误框自动确认，符合 Local 全置灰语义）
- 退出前高亮方案自动恢复 default

### 部署
- publish win-x64 单文件（self-contained，无 R2R/无压缩，对齐 pack.ps1 去掉 R2R）：
  155,059,201 B，SHA256=1E4E76D99CF68C136223889E47151AEDB0FB055B4C174CD98334E5C31853F6B5
  （体积大于上轮 95MB，源于 SDK 10.0.402 runtime pack，功能实机已验）
- deploy_r3.py 双份部署（C:\Users\devuser + D:\Users\devuser\Desktop）SHA256 均 OK

### 40.1 用户实机反馈修复（2026-11-03 下午，截图红框+口述）

用户反馈：①「重置终端输出/清除终端回滚」点了没反应、与 MobaXterm 不一样；
②「本来就是最大化的，按 F11 之后再按一次，就不是最大化了」。定位并修复四处：

1. **点击无反应的根因**：XTerm 的 Reset()/ClearScrollback() 直接改缓冲区、不经过输出流，
   Iciclecreek 控件收不到重绘通知 → 界面看似无变化。两动作均补 `terminal.Refresh()` 强制重绘。
2. **「清除终端回滚」语义对齐 MobaXterm**：官方 changelog 明确 Clear scrollback =
   "clear the screen AND the scrollback buffer" → 改为 `ClearScrollback() + Clear()`
   （当前屏一并清空；状态复位仍是「重置终端输出」RIS 的职责，分工与 MobaXterm 一致）。
3. **F11 退出全屏恢复原窗口态**：新增 `_preFullScreenState`，进入全屏前记录、退出时恢复
   （最大化进全屏 → 退出回最大化，而非退回 Normal）。
4. **高亮水印收缩复位**：缓冲区 Reset/ClearScrollback 后水印归零，复位后新行不丢着色
   （新增回归测试 TERM30_HlighterRecolorsAfterReset）。

gate（gate-r40fix.log / gate-r40.log，双跑均绿）：Core 90 / Sftp 42 / Terminal 26（+1 回归测试）/
Connections 150 / Zmodem 32 / TestGate 49 / App 266 / E2E 15/15，RESULT PASS×2，EXIT=0。

实机复测（--selftest-r39，构建 F6BE87A3D67325E1D2577CE6DE45E7EE47A805A83DC46A940ABD822586F51E26，
155,063,297 B，双份部署 SHA 校验 OK）全部 ok=True：
- highlight probe：Error/crash=红(1)、warning=黄(3)、success=绿(2)、IP/localhost=品红(5)、非关键字 256
- clear-scrollback：YBase 23→0，**screenBlank=True**（当前屏一并清空）
- scrollbar toggle ok；set-title 340×142 无裁切；readonly blocked=True
- **fullscreen：Maximized → FullScreen → Maximized ok=True**（反馈②修复验证）
- special-cmd 矩阵不变（Local 0/5+0/14、Serial 2/5+2/14、Tcp/Telnet 1/5+2/14、Ssh 5/5+14/14）
- 本地 SIGINT → unsupported/failed（错误框自动确认），退出前方案恢复 default

### 40.2 移除「逐行」与「忽略消息」（2026-11-03，用户决定）

用户指示：忽略消息不用实现（去掉）；逐行「去掉，或者看看 MobaXterm 是什么效果再去实现」。
调研结论：MobaXterm「Per line」无任何公开权威描述——官方文档 3.2.13 只讲自定义语法定义；
官方 blog/changelog（8.5/9.0/9.3/9.4）只列 keywords/Unix shell/Cisco/Perl/SQL/network 规则集；
wezterm #4348 的 MobaXterm.ini 导出（CustomSyntax 原文）也无「Per line」规则集；
ini 里仅有 SyntaxType= 数值，无该选项文档。语义无法确认 → 按用户第一选项直接移除（不再置灰占位）。

改动：
- 语法高亮子菜单 6→5 项（移除「逐行」；HighlightScheme.PerLine 枚举保留，仅供旧 config.json
  "per_line" 兼容，映射后无规则=行为同 default）
- 特殊命令子菜单 7→6 项（移除「忽略消息」；SpecialCommands 表/矩阵不变）
- 顶层 19 项 + 4 分隔线结构不变；测试 TERM29_DisplayMenu_StructureAndLabels 同步改为 5/6 项断言
- 若日后用户给出 MobaXterm「Per line」实机效果描述，可再加回（枚举/配置键已留好）
- feature-test-matrix 补 TERM-26~31 六行（TestGate 49 项断言硬约束：parity-matrix「完成」行必须同步登记）

验证（2026-11-03）：
- gate-r40b EXIT=1：单元全绿，E2E UI01 标题栏双击抖动（14:23 FAIL，14:29 复跑 PASS，窗口状态代码未动）；
  另暴露 feature-test-matrix 缺 TERM-26~31 行（已补）
- gate-r40b2 EXIT=1：E2E TELNET02_RestartStoppedSession fixture 抖动（DROP 后 15s 未收到
  TELNET_DROPPED 标记，非代码路径问题）；TestGate 49/49、App 266/266、UI01 PASS
- gate-r40c **EXIT=0 全绿**：单元 Core 90 / Sftp 42 / Terminal 26 / Connections 150 / Zmodem 32 /
  TestGate 49 / App 266 + E2E 全 PASS
- publish win-x64 单文件：COMTool.App.exe **155,063,809 B**，
  SHA256=**15256332205970DD73D124ED625AEA4BCED241F0C4B065F21E26AACF41E5B4C2**
- deploy_r3.py 双份部署（C:\Users\devuser\COMTool-Avalonia.exe + 桌面副本），远端 SHA 均校验 OK
- VM 实机自检（run.log 14:40:22）全部 ok=True：
  高亮六色 1红/3黄/2绿/5品红/256默认；clear-scrollback YBase 22→0 screenBlank；
  scrollbar toggle；fullscreen Maximized→FullScreen→Maximized（r40.1 修复生效）；
  set-title 340×142；readonly blocked；特殊命令矩阵 Local 0/5·0/14、Serial 2/5·2/14、
  Tcp 1/5·2/14、Telnet 1/5·2/14、Ssh 5/5·14/14（分母已是 5 顶层，无「忽略消息」）；
  local-SIGINT→unsupported/failed 自动确认；退出前方案恢复 default

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
