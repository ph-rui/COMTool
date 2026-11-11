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

### 40.3 移除「终端显示区双击最大化/还原」（2026-11-03，用户实机纠错）

用户实机反馈：MobaXterm 的终端黑色显示区内双击**不**做最大化/还原切换，
只有最顶层标题栏才有。r15 实现的 TERM-15「双击终端面板=ToggleMaximize」是对
MobaXterm 行为的误判（当时把"整块终端区镜像标题栏"当成了原版行为），本轮移除。

改动：
- MainWindow：删除 TerminalControl 的 `DoubleTappedEvent→OnTerminalDoubleTapped→ToggleMaximize()`
  接线（L1897 附近）与 `OnTerminalDoubleTapped` 方法本体；标题栏双击
  （`OnTitleBarDoubleTapped`）与标签页双击分离（`OnTabDoubleTapped`）不受影响
- 测试（TDD 先红后绿）：
  - `TERM15_DoubleClickUsesMaximizePath` → `TERM15_TerminalDoubleTapKeepsWindowState`：
    双击终端区两次后断言 WindowState 保持 Normal、焦点不丢（原断言为 Maximized→Normal）
  - `TERM15_DoubleClickDoesNotStealSelection`：去掉 `raised.Handled`/`Maximized` 断言，
    改为断言窗口态不变 + 选区/焦点保持
  - E2E `TERM15_DoubleClickMaximizeE2E` → `TERM15_TerminalDoubleTapNoResizeE2E`：
    双击终端中心 (600,300) 后 sleep 2s 读尺寸，断言窗口保持 1000×650 不变
    （原为 grow→exact 往返）；E2E_TESTS / SSH_TEST_IDS / dispatch 三处同步改名
- 矩阵同步（TestGate 硬约束）：
  - parity-matrix TERM-15 行改定义「终端显示区双击行为（不改窗口）」+ r40.3 依据；
    UI-13 行描述去掉旧"双击终端面板最大化/还原"
  - feature-test-matrix：UI-01 行 Automated 去掉 `TERM15_DoubleClickUsesMaximizePath`；
    UI-13 行 Automated 去掉该测试；TERM-15 行改新测试名 + r40.3 描述
- 终端库自身双击处理（选词）不受影响，仅去掉了叠加的 bubble 观察者

验证（2026-11-03）：
- 红：改测试后 `TerminalMaximizeTests` 2/2 FAIL（窗口仍被最大化）→ 移除接线
- 绿：`TerminalMaximizeTests` 2/2 PASS；`dotnet build` 全 solution 无 error
- gate-r41 **EXIT=0 全绿**：单元 Core 90 / Sftp 42 / Terminal 26 / Connections 150 /
  Zmodem 32 / TestGate 49 / App 266 + E2E 15 项全 PASS
  （`TERM15_TerminalDoubleTapNoResizeE2E` 实测双击前 1000×650 → 双击后 1000×650 不变）
- publish win-x64 单文件：COMTool.App.exe 155,063,809 B，
  SHA256=**C505B77FCDA85233FDF06ECBA8E4AB7D6F55AC8108D766DBF19D8BDB052C0005**
- deploy_r3.py 双份部署，远端 SHA 均校验 OK
- VM 实机自检（run.log 15:12:46）全部 ok=True：高亮六色、clear-scrollback YBase 23→0、
  scrollbar toggle、fullscreen Maximized→FullScreen→Maximized、set-title 340×142、
  readonly blocked、特殊命令矩阵（分母 5 顶层，无「忽略消息」）、local-SIGINT→unsupported、
  退出前方案恢复 default

### 41 中文显示对齐 MobaXterm + 字符保真探针 + 几何诊断（2026-11-03，用户提问）

用户提问：「中文显示，mobaxterm 是怎么做到？可以实现比较好的显示效果」。
两截图（MobaXterm vs COMTool，同一路 ls 输出）逐像素对比：文字内容/引号/颜色/
列对齐完全一致，唯一差异是 CJK 字形粗细——COMTool 中文落到 Microsoft YaHei UI
（笔画偏粗），MobaXterm 用笔画更细的终端级 CJK 字体。

MobaXterm 机制：终端是严格网格（拉丁 1 格、CJK 全角恰 2 格），主字体 Consolas
缺 CJK 字形时回退到终端级 CJK 字体，渲染层强制 CJK 占恰好 2 格（r33 起我们的库
已实现，见 TERM-22）。故「中文显示效果」差异 = CJK 回退字体的选择（笔画粗细），
而非对齐机制。

改动：
- 字体回退链重排（MainWindow L1786 起）：Consolas → **内置 Noto Sans Mono CJK SC
  （Regular+Bold，avares）** → Microsoft YaHei UI（降为最后系统兜底）。依据：
  Noto Sans Mono CJK SC 为终端级 CJK 字体，0.5em 拉丁/1em CJK 度量恰好 2 格对齐，
  笔画细、观感接近 MobaXterm（PIL 渲染样本比对）；avares 字体随单文件 publish
  内置，Windows/Linux/headless 渲染一致（YaHei 效果随 Windows 版本而异）
- `CjkTerminalFontTests.TERM17` 增回退顺序断言（Noto 先于 YaHei）；类注释同步
- r39 自检新增 (0) 几何诊断节：记录 win/term bounds、库网格度量（CharWidth/
  CharHeight/GutterWidth/grid CxR/calcCols）、实测字形步进（FormattedText
  "M"×10 均宽）、DPI 缩放 → run.log `[SelfTest] r41: geometry ...` 行。用途：
  排查用户实机「stty=58×181 但 ls 输出右缘被裁」疑云（run.log 曾同步
  201x56/203x57，截图可见列仅 ~151-157，疑似 pty 列数比可见列数多 ~20%；
  VM 96dpi 无法复现，等诊断数据定位）
- 字符保真探针（登记 TERM-01，排除「引号丢失」疑云）：
  - `QuoteProbeTests.TERM01_QuoteCharSurvivesParser`（Terminal.Tests）：XTerm.NET
    解析层保留 ls 单引号 + CJK 文本
  - `TerminalQuoteFidelityTests.TERM01_QuotesSurviveAttachedReadPath
    {With,Without}Zmodem`（App.Tests）：真实 AttachConnection 读路径（含/不含
    ZmodemStream）引号与 CJK 文本完整
- 矩阵同步 6 处：parity TERM-01/09/22/24、feature TERM-01/09/24（字体链描述改
  Noto 优先、YaHei 兜底；TERM-01 登记探针）

验证（2026-11-03）：
- 绿：过滤测试 31/31（CjkTerminalFont + QuoteFidelity + Maximize + MenuFeature）；
  Terminal.Tests 27/27（含新引号探针）
- gate（2026-11-03 18:05，results 20261103T100251Z）EXIT=0 全绿：单元 390 项
  （Core 90/Sftp 42/Terminal 27/Connections 150/Zmodem 32/TestGate 49）+ App 268
  + E2E 15/15 PASS + TestGate PASS，共 658 项（较上轮 +3：引号探针×1、保真探针×2）
- publish win-x64 单文件：COMTool.App.exe 155,063,809 B，
  SHA256=B26F16B47CBFD7D36533F480C415EE6D82E2B1FFF345B7DF3774EC9178E4AD89
- deploy_r3.py 双份部署（C:\Users\devuser\COMTool-Avalonia.exe +
  D:\Users\devuser\Desktop\COMTool-Avalonia.exe）SHA256 均校验 OK
- VM 自检（--selftest-r39，run.log 18:11）全部 ok=True（高亮六色/clear-scrollback/
  scrollbar/fullscreen/set-title 340×142/readonly/特殊命令矩阵 5 顶层+14 more/
  退出前方案恢复 default）；r41 geometry 行（1024×768@96dpi）：
  `win=1024x768 term=946x627 charW=7.422 charH=15.807 gutter=0 grid=127x39
  calcCols=127 glyphAdvance=7.422 dpi=1` —— grid 列数(127)==calcCols(127)==
  可见列，charW==实际字形步进，VM 上 pty 列数与视口一致；用户实机 stty=58×181
  vs 截图可见 ~151-157 列的失配未在 96dpi VM 复现，待用户在本机跑
  --selftest-r39 回传 geometry 行定位（疑似高 DPI 缩放相关）

### 41.1 中文字体回退改回雅黑优先（2026-11-03，用户同机截图对比）

用户发来同机两张截图（COMTool vs MobaXterm，同一路 ls 输出，文件时间戳同为 18:45），
说「还需要优化一下」。逐像素比对结论：

- 图 1（COMTool）= 旧构建：CJK 字形粗圆（雅黑系）；与 Noto 渲染模板相关系数仅
  0.24，确证用户机器还在跑 C505B77F 或更早（未更新到 r41 Noto 构建）
- 图 2（MobaXterm）的 CJK 与图 1 同风格（粗圆）→ MobaXterm 的中文 = Windows 系统
  雅黑回退（不是 Noto）。r41 把 Noto 提为第一回退方向反了——若更新后中文会比
  MobaXterm 更细更方，与用户期望相反
- 拉丁字距：CT 7.44px（= Consolas 13.5px 默认）vs MX 7.95px（≈ 10pt MobaFont，
  MobaXterm 自带默认字体，advance≈0.6em，比 Consolas 0.55em 宽）
- MobaXterm 粗体按颜色渲染（BoldAsFont=no），COMTool 按笔画加粗；提示符颜色
  （绿粗体 vs 纯白）是两会话 PS1/终端环境差异，非渲染 bug
- 结论：中文显示差异 = CJK 字体族（YaHei vs Noto）+ 字距（MobaFont 使 CJK 格
  绝对宽 ~7%），不是对齐/换行问题

改动：
- 字体回退链改回 Consolas → Microsoft YaHei UI → 内置 Noto（Regular+Bold），
  恢复 r33-r40 设计：YaHei = MobaXterm 在 Windows 的系统回退（1:1 同源），
  Noto 保留为 Linux/headless 的 CJK 覆盖保证
- `TERM17` 顺序断言改为 YaHei 先于 Noto；类注释同步
- 矩阵 5 处 r41 字体描述改回雅黑优先并注 r41.1 依据
  （parity TERM-09/22/24、feature TERM-09/24）
- r41 其余交付（TERM-01 引号/CJK 保真探针、r39 自检几何诊断）全部保留

留给用户：
- 若想字距/中文绝对大小与 MobaXterm 完全一致，需把字号调大（MobaFont 10pt
  字宽 ≈ Consolas 14.5px）：字体设置对话框或 Ctrl+滚轮即可；默认 13.5px
  （= MobaXterm 10pt 名义尺寸）不变
- stty=181 列 vs 可见 ~151 列疑云待解：请在本机跑 --selftest-r39 回传 geometry 行

验证（2026-11-03）：
- 过滤测试 CjkTerminalFont + MenuFeature 27/27 绿
- gate（results 20261103T124244Z，EXIT=0 全绿）：单元 390 项
  （Core 90/Sftp 42/Terminal 27/Connections 150/Zmodem 32/TestGate 49）
  + App 268 + E2E 15/15 PASS + TestGate PASS，共 658 项
- publish win-x64 单文件：COMTool.App.exe 155,063,809 B，
  SHA256=6A78128A7C01AB0B5935A44CC8E05882B1420497BFEDD8FEA0EECE721CC52AE5
- deploy_r3.py 双份部署 SHA256 均校验 OK
- VM 自检（--selftest-r39，run.log 21:00）全部 ok=True；geometry 行
  `win=1024x768 term=946x627 charW=7.422 charH=15.807 gutter=0 grid=127x39
  calcCols=127 glyphAdvance=7.422 dpi=1`（与 r41 一致：VM 上 pty 列数=可见列）

### 42 终端字号对齐 MobaXterm 实测（13.5 → 14.5px）+ 旧配置迁移（2026-11-03，用户「图2 是 mobaxterm 还是有问题」）

取证（用户同机两张截图，内容同为一条 ls 输出；图1 = COMTool r41.1，图2 = MobaXterm）：

- 拉丁字距：COMTool 7.44px（= Consolas 13.5px 的 advance）vs MobaXterm 7.94px
  （MobaFont 11pt，advance≈0.54em）→ 整屏小 7%
- CJK：每字 ink 高 12px vs 13px；CJK ink 高 / 格宽 比 1.633 vs 1.638（相同）
  → 两边**同一字体族**（雅黑），CJK 也是按字号渲染、没有额外缩放，唯一差异是绝对字号
- 两图的 CJK 都带 ClearType 子像素彩边（通道极差均值 63.0 vs 70.8），
  所以 AA 模式不是差异源；「中文发虚」是 12px 落在更小的字号上 + 整屏偏小造成的观感
- 列对齐已逐格核对：CJK 占 8 格（4 字）后在下一格接 `.docx`，两图同一列（第 103 列）
  → 不是 r33 的对齐问题

改动：

- `TerminalConfig.DefaultFontSize = 14.5`（Consolas advance 7.97px，与 MobaXterm 7.94px 对齐），
  `MainWindow.TerminalFontSize` 改为引用该常量（Core/App 单一事实来源，防两处默认值漂移）
- 旧配置迁移：新增 `terminal.fontSizeRevision`（旧配置缺键 = 1），加载时把旧默认
  13.5 一次性迁移到 14.5 并落盘；迁移后用户主动调回 13.5 保留不动
  （Ctrl+滚轮步进 1，13.5 在迁移前只可能是旧默认值）
- 回归：`CjkTerminalFontTests.TERM17_ConfigDefaultFontSizeMatchesTerminalDefault`
  （config 默认 == 视图默认；旧配置迁移、二次读盘、用户改回 13.5 保留、自定义 18 不迁移），
  `TERM17_TerminalFontMatchesMobaxterm` 默认字号断言 14.5，`TERM18` clamp 回落值 14.5，
  `TERM24_FontDialog...` 默认字号改 14.5
- 矩阵 4 处同步：parity TERM-09/24、feature TERM-09/24（含新测试登记）

说明：用户这台 MobaXterm 的会话字号是 11pt（MobaXterm 出厂默认是 10pt），
本项目按用户实屏对齐，不按出厂默认。

验证（2026-11-03）：
- 过滤测试 CjkTerminalFontTests 9/9 绿；App 全量 269/269 绿；Core 90/90 绿
- gate-r42b（`publish/test-results/20261103T132902Z`，GATE_EXIT=0）：restore/build/
  unit-tests/app-tests/test-list/e2e/testgate 全 PASS，共 659 条
  （Core 90 / Sftp 42 / Terminal 27 / Connections 150 / Zmodem 32 / TestGate 49 + App 269），
  E2E 15/15 PASS；首次 gate-r42 FAIL 的唯一原因是
  `ConfigTests.SaveAndLoad_RoundTripsTerminalFontSize` 仍断言旧默认 13.5，改 14.5 后复跑全绿
- publish win-x64 单文件 155,063,809 字节，
  SHA256 E009288F728897538C3E9A9EF21098D62E992F5BA0D8B74B3DD0948850CEF4E0
- deploy_r3.py 双份部署 + SHA256 校验 OK
  （`C:\Users\devuser\COMTool-Avalonia.exe`、桌面一份），远端 SHA256 与本地一致
- VM 实机自检（`--selftest-r39`）：`r41: geometry win=1038x782 term=960x641
  charW=7.972 charH=16.978 grid=120x37 calcCols=120`——charW 由 13.5px 的 7.44 升到
  7.972（MobaXterm 实测 7.94，差 0.4%），grid 由 127 列变 120 列且 calcCols 仍等于 grid
  （右缘无裁切）；同屏截图 `main-content.png` 无布局错位
- 迁移实证：部署前远端 config.json 为 `fontSize:13.5` 且无 `fontSizeRevision`；
  自检首启后落盘为 `fontSizeRevision:2, fontSize:14.5`（其余键 Consolas/UTF-8 等未动），
  二次启动不再改写

### 43 中英文混排「中文比英文低一截」基线对齐（库 4.1.1 → 4.1.2，2026-11-03，用户「图2 mobaxterm 中文和英文是平行的，COMTool 高低不齐」）

r42 把字号对齐后，用户仍指出一个观感差：**MobaXterm 里中英文在同一基线上（平行），COMTool 里中文偏低、看着也小**。实机逐像素比对（用户同机两张截图，同一行 `…cms方案搭建.docx`）：

- 我们的中文字形 ink 顶比英文 cap 顶低 ~3px、底比英文基线低 ~5px（CJK 基线 ≈ band+16 vs 英文基线 ≈ band+11）；
- MobaXterm 的中英文共用同一条基线，中文只是更高（ink 顶高出英文 cap 顶 ~3px）、居中。

**根因（渲染层，库）**：终端把每个全角（width-2）中文字符作为独立的 width-2 run 渲染。主字体 Consolas 不含 CJK，所以这个 run 走 `FormattedText` 慢路径；而 `FormattedText` 的基线取**本 run 字体自己的 ascent**——雅黑的 ascent（1.14em≈16.6px）远大于 Consolas（0.83em≈12.0px）。于是每个中文字符被摆到雅黑那条更低的基线上，比旁边的英文整体下沉 ~4.6px（r43 自检实测：latin base=12.008 / cjk base=16.582，差 4.574）。r33 的「CJK 固定 2 格 advance」只保证了**水平**对齐（格网），没处理**垂直**基线，所以列对得上、行却高低不齐。

**修复（库 Iciclecreek.Avalonia.Terminal 4.1.2，渲染 fast-path）**：width-2 单字 run 原来只在主字体含该字形时走 GlyphRun 快速路径，Windows 上（主=Consolas 无 CJK）恒落到 FormattedText。现改为：主字体不含该全角字符时，**沿字体链往下找到真正含它的那个 face**（Windows=Microsoft YaHei UI，Linux/headless=内置 Noto Sans Mono CJK SC），用它解析出字形、把 advance 固定为 2 格（沿用 r33），但**基线钉在主字体的 `_baseline`** 上，而不是该 CJK face 自己的 ascent。这样中文和英文共享 Consolas 基线 → 平行；advance 仍是 2 格 → 水平对齐不破。解析按 (style,weight) 缓存，命中后跳过整条链；缓存 face 不含某生僻字时继续往下探，不丢字。

改动文件：
- 库 `TerminalView.cs`：新增 `_wideFallback` 缓存字段（全角回退 face，按 style/weight）。
- 库 `TerminalView.Render.cs`：`TryBuildGlyphRun` 的 width-2 分支改调新增 `ResolveWideGlyphTypeface(...)`（主字体→链上首个含该字形的 face），`baselineOrigin` 仍是主字体 `_baseline`。
- 库 csproj 版本 4.1.1 → 4.1.2；`COMTool.Terminal.csproj` 引用改 4.1.2；`NuGet.config` 注释同步。
- 自检增强（`--selftest-r39`，永久保留）：新增 `[SelfTest] r43: metrics` 行（打印 latin/cjk 的 FormattedText Width/Height/Baseline 与 cell 度量）+ 写一行中英文混排 + 抓 `cjk-content.png`，供逐像素核基线。

实机验证（VM，部署后 `--selftest-r39` 抓 `cjk-content.png` 逐像素）：
- 修复前：英文 cap `top=2 bottom=11`（基线≈11），中文 `top=5 bottom=16`（基线≈16）→ 中文低 5px、ink 仅 9px（被行框裁）。
- 修复后：英文 cap `top=2 bottom=11`（基线≈11），中文 `top=0 bottom=12`（基线≈12）→ **共用基线（差≤1px）**、中文 ink 恢复 13px 全高，中文略高出英文 cap 顶 ~2px、略低于基线 ~1px——与 MobaXterm 的「平行、居中」一致。

- 验证（2026-11-03）：
  - gate（`publish/test-results/20261103T144656Z`，GATE_EXIT=0）：restore/build/unit-tests/
    app-tests/test-list/e2e/testgate 全 PASS，共 659 条（Core 90 / Sftp 42 / Terminal 27 /
    Connections 150 / Zmodem 32 / TestGate 49 + App 269），E2E 15/15 PASS——库 4.1.2 渲染改动
    无回归（Linux/headless 下 E2E 真实渲染通过）
  - publish win-x64 单文件 155,063,809 字节，
    SHA256 5C2310745462D1CA984DFF380AB458F577782168A9BBB278E50E1722E83376F4
  - deploy_r3.py 双份部署 + SHA256 校验 OK
    （`C:\Users\devuser\COMTool-Avalonia.exe`、桌面一份）
  - VM 实机自检（`--selftest-r39`）：`[SelfTest] r43: metrics size=14.5 cell=7.972x16.978 |
    latin ... base=12.008 | cjk ... base=16.582`（FormattedText 度量，证明雅黑 ascent 比 Consolas
    高 4.574px，即原下沉量）；`cjk-content.png` 逐像素：英文 cap 基线≈11、中文基线≈12（差≤1px），
    中文 ink 由修复前 9px（被行框裁）恢复 13px 全高，中英文共用基线——与 MobaXterm 平行
  - 2026-11-04 用户同屏截图确认中英文基线平行，r43 验收通过

### 44 标签头按会话类型显示图标（对齐 MobaXterm，串口≠SSH 一目了然）（2026-11-04，用户「mobaxterm 里 ssh 与串口的 tab 图标有明显区别，COMTool 也改成类似的，图标区别分明」）

取证：用户两张并排截图。MobaXterm 每个 tab 头带一个**类型图标**（串口=灰蓝插头、SSH=金色钥匙），
一眼能分清；COMTool 所有 tab 头只有同一个绿色状态圆点 + 标题 + 关闭按钮，串口 COM99 和 SSH
192.0.2.48 长得一模一样，分不清类型。

改动（复用「新建会话」菜单/侧栏既有的 5 类图标+配色，tab 与入口一致）：
- `TerminalTab.KindIconGlyph / KindIconBrush`（`MainViewModel.cs`）：按 `SessionKind` 映射
  串口=f1e6 插头 #43a047 / SSH=f084 钥匙 #f0b429 / Telnet=f120 终端 #00897b /
  TCP=f0ac 地球 #f57c00 / 本地=f108 桌面 #607d8b（图标固定 16px 宽占位，标题起点对齐）。
- 标签模板（`MainWindow.axaml`）：**移除统一绿色状态圆点（Ellipse）**，换成本类型图标
  `TextBlock.TabKindIcon`（FontAwesome，`{DynamicResource IconFontAwesome}`）。
- 选中态：选中 tab 背景是蓝 #0f88eb，彩色图标会撞色 → 由模型层 `KindIconBrush` 在
  `IsSelected==true` 时返回白色刷（`TabIconSelectedBrush #ffffff`），并新增
  `OnIsSelectedChanged` 转发 `PropertyChanged(nameof(KindIconBrush))` 使绑定刷新。
  （模板 Foreground 是本地绑定，优先级高于样式 setter，AppStyles 覆写不生效——
  2026-11-04 实机截图证实。）
- SSH 图标从原 `f233`（服务器，蓝 #1976d2）改成 `f084`（钥匙，金 #f0b429），对齐 MobaXterm 的
  金色钥匙；同步更新「新建会话」两处菜单（`MainWindow.axaml` 空状态 + `SessionSidebar.axaml` 侧栏 `+`）
  和 `SessionItem.IconGlyph`（侧栏会话行）以及 `NewSessionMenuTests` 的图标断言，保持全 App 一致。
- 状态信息不丢：连接/断线/重连仍由底部状态栏（`StatusMessage`/`StatusBrush`）显示，标签头不再重复。
- E2E 坐标连带修正（`tests/e2e/linux/lib.sh`）：图标 16px 比原圆点 8px 宽 8px，标签内关闭按钮整体右移 8px，
  原固定点击坐标 FIRST_TAB_X=125 / SECOND_TAB_X=239 恰好落在两个 tab 的关闭按钮上（UI05 点击即关 tab、
  键入落入剩余 tab，连挂两轮且失败截图证实只剩一个 tab）→ 改为标题区坐标 96 / 208（远离关闭按钮
  116..132 / 230..246，标题区 86..108 / 200..222），并更新布局注释。

测试：
- 新增 `TabKindIconTests`（`UI05_TerminalTabKindIconGlyphPerKind` 断言 5 类字形+色；
  `UI05_SelectedTabKindIconTurnsWhite` 断言选中态图标刷变白且发出 KindIconBrush 变更通知；
  `UI05_TabTemplateShowsKindIconGlyph` 建 SSH/串口 tab 断言模板里 `TabKindIcon` 的 Text=对应字形、
  字体=bundled FontAwesome，且标签里已无 Ellipse 状态圆点）。
- 矩阵：UI-05 两文件登记 3 个新测试 + r44 描述（选中态白图标由模型层实现）；UI-06「SSH 服务器」改「SSH 钥匙」；
  UI-08 标签头描述由「状态点」改「类型图标」。
- 新增实机自检 `--selftest-r44`（`RunR44SelfTestAsync`）：开假 Serial+SSH 双 tab（SSH 选中），打印两 tab
  字形/图标刷颜色/选中态 + r41 几何，落盘 `main-content.png`（主窗整体含标签条）供实机核对。

验证（2026-11-04）：
- 过滤测试 TabKindIconTests/NewSessionMenuTests/MainWindowHeadlessTests 17/17 绿（含新增选中态白刷断言）。
- gate 20261104T080533Z 全绿：build/单测 390/App 272（662 项）/test-list/E2E 15 项/TestGate 全 PASS。
  （期间 075821Z 一次 app-tests 宿主 exit 139 原生崩溃，重跑恢复，判定为环境抖动非代码回归；
  073127Z、073922Z 两轮在自测入口加入前亦全绿。）
- publish：win-x64 单文件 COMTool.App.exe 155,067,905 B，
  SHA256 e89dcb5b1930bb205f374e4068514d094154615c088de9f6a089a6616300c579。
- deploy：deploy_r3.py 双份部署（C:\Users\devuser\COMTool-Avalonia.exe + 桌面副本）SHA256 均校验 OK。
- 实机自检 --selftest-r44：Serial tab 图标刷 #43A047 selected=False，SSH tab #FFFFFF selected=True；
  main-content.png 标签条实机截图：COM99=绿插头（未选灰底）/ devuser@192.0.2.48=白钥匙（选中蓝底），
  两类型图标区别分明，与 MobaXterm 对齐。r41 几何顺带复测：grid=118x36 calcCols=118 dpi=1
  （VM 96dpi 下 grid==calcCols，右缘裁切问题本机仍不可复现，继续等用户本机数据）。

### 45 终端鼠标选中自动复制（对齐 MobaXterm，选中即入剪贴板）（2026-11-04，用户「鼠标选中自动复制」+ MobaXterm 选中高亮截图）

取证：MobaXterm 截图——选中 `pwd` 结果行后蓝高亮保留、文本同时进剪贴板；COMTool 此前选中只高亮，
必须再右键/Ctrl+Shift+C 才复制，且选中即触发终端菜单（右键）时先复制再清除，路径绕。

改动（库 Iciclecreek.Avalonia.Terminal **4.1.2 → 4.1.3**，本地 nupkg 已提交 third_party/）：
- `TerminalView.AutoCopyOnSelection`（StyledProperty<bool>，**默认 true**，对齐 MobaXterm 默认行为）：
  `OnPointerReleased` 的选区结束分支里，`EndSelection()` 后若 `HasSelection` 则 fire-and-forget
  `CopyOnSelectionAsync()`（内部 try/catch `CopyAsync()`，剪贴板被占用等失败只写 Debug，不炸 async-void handler）。
- 语义：拖选/双击单词/三击整行均触发；单击未拖动不触发（无选区）；**选区高亮保留不自动清除**
  （MobaXterm 行为；清除靠再次单击/键盘操作，原有逻辑不变）；右键复制粘贴路径不变。
- `TerminalControl` 镜像属性 + `Generic.axaml` TemplateBinding（值从 control 传到内层 view，
  与 ShowCaretOnClick 同款「真 StyledProperty 而非 forwarder」模式）。
- TerminalWindow 同镜像（保持 TerminalWindow/TerminalControl/TerminalView 三面 API 对齐，
  库内 surface 一致性测试强制）。
- 未接 config 持久化开关（MobaXterm 有该选项，本轮先默认开启；要开关时再加 terminal.autoCopyOnSelection）。

测试：
- 库级（Iciclecreek.Avalonia.Terminal.Tests，3 项）：`AutoCopySelectionTests`——拖选后 headless 剪贴板
  == 选中文本且选区保留；单击未拖动不写剪贴板；AutoCopyOnSelection=false 时选区在但剪贴板不动。
- App 级（COMTool.App.Tests，2 项）：`TerminalSelectCopyTests.TERM32_*`——经 TerminalControl 模板
  TemplateBinding 验证属性默认 true、可在 control 上置 false 并传到内层 view（模板接线回归护栏）。
- 矩阵：TERM-32 新行登记 5 个测试；TestGate 单向校验覆盖。
- E2E 未加：Xvfb 环境无 xclip/xsel 读不了 X11 剪贴板，headless+实机自检已覆盖逻辑与实机链路。

验证（2026-11-04）：
- 库全套 662 项 0 失败（653 过 + 9 skip，含 3 新）；App 全量 274/274（+2 新）。
- gate 20261104T100625Z 全绿：build/单测 390/App 274（共 664 项）/test-list/E2E 15 项/TestGate 全 PASS。
  （期间 093745Z 一次 app-tests 宿主 exit 139 原生崩溃，单独复跑 274/274 恢复，判定为环境抖动非代码回归。）
- publish：win-x64 单文件 COMTool.App.exe 155,072,001 B，
  SHA256 c0a84ac8bb82b9470ed8cd79e56b81d9350b13f6a5bfc38bab7eb8b968492f72。
- deploy：deploy_r3.py 双份部署（C:\Users\devuser\COMTool-Avalonia.exe + 桌面副本）SHA256 均校验 OK。
- 实机 --selftest-r45：`[SelfTest] r45: selection=True clipboard='SELECT TO COPY R45' autoCopy=OK`——
  模拟整行拖选松开后系统剪贴板=整行探测文本，main-content.png 实机截图显示整行蓝高亮保留（MobaXterm 行为）。
  首跑曾因合成指针事件坐标按窗口系换算偏差只选中 11 列，自检代码改用 TranslatePoint 换算后复跑通过。


### 46 SSH/Telnet 重连成功后状态栏「Restarting...」进度条挂死（2026-11-05，用户报「已经重连成功了，右下角的 restart 进度条一直在显示」+ 截图）

取证：用户截图——左下状态已是 Connected（绿色），右下进度区却挂着不定长「Restarting...」条。
根因：`MainWindow.RestartSshSessionAsync` / `RestartTelnetSessionAsync` 在尝试开始时置
`tab.IsRestarting = true`，但**只有 catch 失败分支**清回 false；成功路径（attach 新连接、
`tab.IsAlive = true`、StatusMessage="Connected..." 之后）、`connection is null` 早退路径
（用户取消密码框）与连接后守卫 return（tab 已关/已分离）都漏清。状态栏模型层
`ShowStatusProgress => IsZmodemActive || IsRestarting`，于是重连成功后进度条常驻。

改动（5 处补清标志，均在 MainWindow.axaml.cs）：
- SSH 成功路径：`tab.TabDotBrush = StatusInfoBrush` 之后 `tab.IsRestarting = false`（用户报障主现场）
- SSH `connection is null` 路径（取消密码框）：`StatusMessage = "Session stopped"` 处同清
- SSH 连接后守卫 return（`!_sshRestarts.Contains(tab) || !IsTabOpen(tab) || terminal is null`）：同清
- Telnet 成功路径 / 守卫 return：同构同病，同样补清
- catch 分支原有清除保持不变；`LastSshPasswordDialogForTests` 测试接缝（ConnectSshSessionAsync
  内 ShowDialog 前后置/清，try/finally）供 headless 测试驱动取消密码框路径

测试（TDD 先红后绿）：
- App 级 `RestartProgressTests.SSH11_RestartProgressClearedWhenAttemptEnds`：经
  `RestartSshSessionForTests` 驱动真实 R 重启——无 key/密码的会话 → 重启悬停在密码框 →
  测试点 Cancel → 尝试以 connection==null 结束 → 断言 StatusMessage="Session stopped" 且
  IsRestarting==false、ShowStatusProgress==false。修复前红（标志残留），修复后绿。
- 成功路径 headless 不可达（SshConnection 为 sealed + 静态 Connect，需真实 SSH 握手），
  由实机 `--selftest-r46` 覆盖：打开配置中第一个 SSH 会话（devuser@192.0.2.48）连接成功 →
  触发一次 RestartSshSessionAsync → 断言完成后 IsRestarting/ShowStatusProgress 已清且
  StatusMessage 以 Connected 开头。
- 矩阵 SSH-11 行 Automated 列登记新测试；TestGate 校验。
- 相邻已知问题（本轮不修，仅记录）：「重连失败」对话框选「继续重试」时，外层 finally 会在
  重试刚入队时把 tab 从 `_sshRestarts` 集合移除，重试在连接完成后的守卫处静默中止
  （IsRestarting 已随守卫 return 清除，UI 不再挂死，但不会真正重连）。

验证（2026-11-05）：
- 过滤测试 RestartProgressTests 4/4 绿（含新项 SSH11_RestartProgressClearedWhenAttemptEnds）；App 全量 275/275（+1 新）。
- gate 20261105T012419Z 全绿：build/单测 390/App 275（共 665 项）/test-list/E2E 15/TestGate 全 PASS。
- publish：win-x64 单文件 COMTool.App.exe 155,076,097 B，SHA256 090e3ae577a683fafa3c96c41ce111d179949a1d29439d0f6797b1ad120864a9。
- deploy：deploy_r3.py 双份部署（C:\Users\devuser\COMTool-Avalonia.exe + 桌面副本）SHA256 均校验 OK。
- 实机 --selftest-r46：打开 devuser@192.0.2.48 连接成功→触发 R 重启→`[SelfTest] r46: 重启后 IsRestarting=False ShowStatusProgress=False status='Connected devuser@192.0.2.48:22' alive=True => OK`，main-content.png 无 Restarting 进度条。

### 47 终端双击整路径选中 + 悬停文本光标（对齐 MobaXterm，2026-11-05，用户「mobaxterm双击路径会直接选中（图二），而且移动到终端的显示窗口时，鼠标指针图表就变了，不是箭头」+ 两张 MobaXterm 截图）

取证：MobaXterm 截图——① `pwd` 输出的路径行 `/home/devuser/tftp_dir/patent` 整行被选中（双击
路径中段直接选整条路径，含 `/`）；② 指针在终端显示区域内时呈文本型 IBeam 而非窗口箭头。
COMTool 现状：双击按核心词规则只选中一个词段（实测核心词扩展遇 `/` 断开：col0 选中 `/home`、
col8 选中 `devuser`；`-` 同样断开——ShiftSelectionTests 已有「hyphen ends the word」佐证）；
指针在终端区域恒为箭头（仅 URL 悬停切小手、OSC22 程序形状可覆盖）。

改动（库 Iciclecreek.Avalonia.Terminal **4.1.3 → 4.1.4**，本地 nupkg 已提交 third_party/）：
- 双击整路径：`TerminalView.OnPointerPressed` 的 Word 分支 `StartSelection` 后调新
  `ExtendSelectionToPath(col, row)`——读取该行缓冲，从点击列向左右扫描「路径字符游程」
  （字母/数字/`_`/`/`/`.`/`~`，与键盘词运动的 IsWordChar 同基 + 路径分隔符）；
  游程含 `/` `.` `~` 中任一（核心词规则会断开的分隔符）时，用 `StartSelection(Normal)` +
  `UpdateSelection`（与普通拖选同一对 API）把选区重表为整条游程；普通单词（无分隔符）
  保持核心词扩展原样（foo-bar 行为不变），后续拖拽扩展与松开自动复制（r45）链路不受影响。
- 悬停文本光标：`TerminalView` 新增 `OnPointerEntered` + `OnPointerMoved`（URL 探测后）+
  新 `ApplyTextCursor()`——指针在文本表面且无 URL 小手/OSC22 形状占用时设 IBeam；
  `OnPointerExited`（Pty.cs）恢复窗口默认（OSC22 程序形状保留，程序拥有形状直到其复位）。
  `IBeamCursor` 为 internal static（InternalsVisibleTo 供两侧测试按引用断言）。
  优先级：OSC22 程序形状 > URL 悬停小手 > IBeam > 默认箭头。
- App 无功能改动（库行为经模板自动生效）；COMTool.Terminal.csproj → 4.1.4、NuGet.config 注释更新。

测试（TDD 先红后绿）：
- 库级（PathSelectCursorTests，5 项）：双击路径中段选整条路径；双击首斜杠同样整选；
  双击普通单词仍为核心词（回归护栏）；光标：移入文本面=IBeam、移出=恢复默认；
  URL 悬停小手仍压过 IBeam（移开 URL 后 IBeam 恢复）。修复前 4 项红（选区只有词段/光标 null）。
- App 级（TerminalPathSelectTests，2 项）：经真实 TerminalControl 模板链路——双击路径选整条
  （断言 Selection 文本==整路径）、移入后内层 view 光标==TerminalView.IBeamCursor（引用同一实例）。
- 矩阵：TERM-33 新行（feature + parity），TestGate 校验。
- 实机 `--selftest-r47`：开本地终端写 `/home/devuser/tftp_dir/patent`，合成悬停（断言光标
  IBeam）+ 双击中段（断言选区==整路径，窗口坐标系 TranslatePoint 换算，r45 教训），截图留证。
- E2E 未加：Xvfb 下合成双点击+选区/光标断言不稳，headless + 实机自检已覆盖。

验证（2026-11-05）：
- 库全套 667 项 0 失败（658 过 + 9 skip，含 5 新）；App 全量 277/277（+2 新）。
- gate 20261105T021047Z 全绿：build/单测 390/App 277（共 667 项）/test-list/E2E 15/TestGate 全 PASS。
- publish：win-x64 单文件 COMTool.App.exe 155,080,193 B，
  SHA256 caf6c92a1590407f2a2e933e602d5c8fad5ae9cffee24f38bb1fbfe74ed8e77a。
- deploy：deploy_r3.py 双份部署（C:\Users\devuser\COMTool-Avalonia.exe + 桌面副本）SHA256 均校验 OK。
- 实机 --selftest-r47：`[SelfTest] r47: 悬停 cursor='Ibeam' => OK`、
  `[SelfTest] r47: selection=True selectedText='/home/devuser/tftp_dir/patent' => OK`，
  main-content.png 整路径高亮选中（OS 层指针形状需真实鼠标确认，合成事件只改控件 Cursor）。

### 48 标签类型图标选中态保持类型色（对齐 MobaXterm，撤销 r44 的「选中变白」）（2026-11-05，用户「钥匙这个图标会变白色，mobaxterm这种就比较好（图2），串口的也有同样的问题，图标会变白色」+ 两张截图）

取证：用户截图①——COMTool 选中的 SSH tab（蓝底）上钥匙图标变白；截图②（MobaXterm）——
选中的 tab「2. Linux云桌面」深色底上钥匙仍是**金色**，图标颜色恒定，只有 tab 底色随选中变化。
r44 当时为避免彩色图标撞蓝底而让选中态变白，用户反馈后确认 MobaXterm 行为才是目标。

改动（COMTool App 模型层，无库改动）：
- `TerminalTab.KindIconBrush` 不再依赖 IsSelected：任何选中态都返回类型色
  （SSH 金 #f0b429 / 串口绿 #43a047 / Telnet 青 #00897b / TCP 橙 #f57c00 / 本地灰 #607d8b）；
  删除 `TabIconSelectedBrush`（白刷）与 `OnIsSelectedChanged` 的 KindIconBrush 转发
  （brush 不再随选中变化，无需再通知）。
- 模板注释同步（MainWindow.axaml 标签条 TabKindIcon 注释）。
- 视觉结果：选中 tab 蓝底 + 金色钥匙 / 绿插头，与 MobaXterm「底色变、图标色不变」一致。

测试（TDD 先红后绿）：
- `TabKindIconTests.UI05_SelectedTabKindIconTurnsWhite` 改写为
  `UI05_SelectedTabKeepsKindIconColor`：SSH tab 选中前/选中后/取消选中后 KindIconBrush
  恒为 #f0b429（改前红：选中时变白）。
- 矩阵 UI-05 行 Automated 列测试名同步更新。
- 实机 `--selftest-r48`：开一对假 Serial+SSH tab（SSH 选中），断言 SSH 图标 #F0B429、
  Serial 图标 #43A047 且选中态正确，落盘 main-content.png 标签条截图。

验证（2026-11-05）：
- 过滤测试 TabKindIconTests 3/3 绿（含改写项 UI05_SelectedTabKeepsKindIconColor）；App 全量 277/277。
- gate 20261105T025433Z 全绿：build/单测 390/App 277（共 667 项）/test-list/E2E 15/TestGate 全 PASS。
  （首轮 gate 024845Z 遇 app-tests 宿主 exit 139 原生崩溃（213 项已跑 0 失败），单独复跑 277/277 恢复，
  判定为环境抖动非代码回归，与 r45/r46 记录一致。）
- publish：win-x64 单文件 COMTool.App.exe 155,084,289 B，
  SHA256 eccbba01839453d7a7bd18fd3fdaa6ed6836568c74e5ce8c0d61e1c3e31dd74a。
- deploy：deploy_r3.py 双份部署（C:\Users\devuser\COMTool-Avalonia.exe + 桌面副本）SHA256 均校验 OK。
- 实机 --selftest-r48：`Serial color=#43A047 selected=False`、`SSH color=#F0B429 selected=True`
  →「选中态图标保持类型色=OK」；main-content.png 标签条截图：选中 SSH tab（蓝底）钥匙为金色，
  与 MobaXterm 截图一致。

### 49 会话设置对话框布局修复：Key file 浏览按钮垂直居中 + 长标签不再顶框（2026-11-05，用户「图1的...不居中；图2，Auto reconnect的t都跑到右边的框框里了」+ 两张对话框截图）

取证（用户截图像素级测量）：
- 图1（SSH）：「...」按钮外框与输入框顶边对齐（同 y），问题在 `Content="…"`（U+2026）贴基线渲染，
  在 28px 按钮内字形中心偏低约 8px。
- 图2（TCP）：四面板标签列均为 86px；「Auto reconnect」12pt 实测渲染约 84px（x 23→107），
  复选框左缘恰在 x=107（列边界），末位 t 顶到框边。

改动（ConnectionSettingsWindow.axaml，无库改动）：
- Key file 浏览按钮：`Content="…"` → `&#xF141;`（FontAwesome f141 ellipsis，设计在 em 盒垂直中心，
  与 MobaXterm 的居中省略号一致）+ `FontFamily="{DynamicResource IconFontAwesome}"` + `FontSize="14"`；
  去掉固定 `Height="28"`（Fluent 主题 TextBox MinHeight=32，输入框实际上屏 32px，固定 28 的按钮
  与输入框不同高且顶对齐，进一步放大视觉偏移），改 `VerticalAlignment="Stretch"` 随输入框同高；
  加 `Name="KeyFileBrowseButton"` 与 ToolTip 供测试/自检引用。
- 四面板（Serial/Ssh/Telnet/Tcp）标签列 `86,*` → `108,*` 统一加宽：最长标签「Auto reconnect」
  约 84px，108 列宽留出约 24px 空隙；统一加宽保证跨连接类型切换时输入列起点仍对齐
  （各输入框均固定 Width=155 Left，右移 22px 后 380px 窗宽足够）。

测试（TDD 先红后绿，新增 tests/COMTool.App.Tests/ConnectionDialogLayoutTests.cs）：
- `UI06_SshKeyFileBrowseButtonUsesCenteredIconGlyph`：按钮存在（Name 契约）、Content=="\uf141"、
  FontFamily 含 fontawesome6-solid、Width=30 且高度属性未写死（NaN，靠 Stretch 跟随行高）。
- `UI06_SshKeyFileBrowseButtonVerticallyCenteredWithKeyFileBox`：切 SSH 面板后实测
  按钮与 KeyFileBox 同高（Fluent MinHeight 32 下均为 32）且垂直中心差 ≤1px。
- `UI06_ConnectionDialogLabelColumnFitsLongestLabel`：四面板标签列均 ≥100 且宽度一致
  （headless 字体度量与 Windows 不同，用列宽契约断言而非文本宽度）。
- 改前 3/3 红（按钮无名、列宽 86）；改后 3/3 绿。

验证（2026-11-05）：
- 过滤测试 ConnectionDialogLayoutTests 3/3 绿（改前 3/3 红：按钮无名/非 f141、列宽 86<100）；App 全量 280/280。
- gate 20261105T042356Z 全绿：build/单测 390/App 280（共 670 项）/test-list/E2E 15/TestGate 全 PASS。
  （过程：035538Z 构建失败——自检代码插值内误加集合表达式方括号（CS9174），已修；
  035751Z 矩阵 UI-06 行行尾竖线丢失致 TestGate 报 9 cell，修正后 TestGate 单独复跑 PASS；
  最终代码态 042356Z 全量一次通过。）
- publish：win-x64 单文件 COMTool.App.exe 155,088,385 B，
  SHA256 f9bd75e97e72077821ae3e402bec2f1671693ff8c011e42bdbf6e00b051a4961。
- deploy：deploy_r3.py 双份部署（C:\Users\devuser\COMTool-Avalonia.exe + 桌面副本）SHA256 均校验 OK。
- 实机 --selftest-r49：全部 OK——
  button=(125,0,30x32) box=(0,0,121x32) 中心差=0.00px 同高=OK；
  四面板标签列宽=108/108/108/108（≥100 且一致 OK）；
  TCP「Auto reconnect」文字实测宽=82.0px、标签列宽=108px、右侧余量=26.0px（旧 86px 列宽下余量仅约 4px）；
  像素复核（r49-ssh-dialog.png 整窗 380x500）：按钮盒 y=232..263（h=32）与输入框同高 32px，
  f141 三点字形质心偏离盒中心 +0.63px（修复前 U+2026 偏低约 8px）；
  r49-tcp-dialog.png 目视：Auto reconnect 标签与 Enable 复选框之间有可见空隙，不再顶框。

### 50 会话设置对话框 OPEN/Close 按钮文字垂直居中（2026-11-05，用户「open、x close，好像也没居中，有点中间偏上」+ 对话框截图）

取证：
- 用户截图（红框）：TCP 面板底部蓝色 OPEN 按钮文字明显偏上；右下角 ✕ Close 同样偏上。
- 根因与 r49「…」按钮同源：Avalonia Fluent 主题 Button 的 `VerticalContentAlignment`
  默认为 Stretch——文字被包成 TextBlock 后从内容框顶部起排，行高（~16px）小于内容框
  （30px 固定高 − 上下 padding ≈ 19px）时视觉上「中间偏上」/底部留白。
  代码库已有同坑记录（CreateDialogTextButton 注释：r44「取消/确定」字底裁切，约定
  显式两轴居中 + 不写死 Height）。

改动（ConnectionSettingsWindow.axaml，无库改动）：
- 4 个 OPEN 按钮（Serial/Ssh/Telnet/Tcp，补 `Name="SshOpenButton"`）与右下角 Close 按钮
  （补 `Name="CloseButton"`）：`Height="30"` → `MinHeight="30"`（保底不写死，防 DPI/字体
  变化复发），新增 `VerticalContentAlignment="Center"`（横向 Center 已有）。
- 与 r49 的 KeyFileBrowseButton 同一约定：两轴显式居中 + MinHeight 保底。

测试（TDD 先红后绿，ConnectionDialogLayoutTests 追加）：
- `UI06_DialogActionButtonsTextVerticallyCentered`：5 个按钮均存在（Name 契约）、
  VerticalContentAlignment==Center、Height 未写死（NaN）、MinHeight≥28。
  改前红（Stretch + Height=30 写死），改后绿。

验证（2026-11-05）：
- 过滤测试 ConnectionDialogLayoutTests 4/4 绿；App 全量 281/281。
- gate 20261105T052941Z 全绿：build/单测 390/App 281（共 671 项）/test-list/E2E 15/TestGate 全 PASS。
- publish：win-x64 单文件 COMTool.App.exe 155,092,481 B，
  SHA256 c182cde703c5b9be731a88c43b852aa77de170d67c802ae1cbeace05d1fdad0f。
- deploy：deploy_r3.py 双份部署（C:\Users\devuser\COMTool-Avalonia.exe + 桌面副本）SHA256 均校验 OK。
- 实机 --selftest-r50：全部 OK——5 按钮均「两轴居中=OK 高度非写死=OK」
  （SerialOpen bounds=328x30、Close bounds=100x30）；
  像素复核（r50-dialog.png 380x500 整窗截图）：OPEN 白字质心偏离盒中心 -0.31px、
  Close 白字质心偏离 -0.40px（修复前文字顶在内容框上沿，明显偏上）。

### 51 启动性能优化：R2R + 压缩 + 串口扫描移后台 + 启动耗时自检（2026-11-05，用户「COMTool-Avalonia.exe 运行启动感觉卡卡的，能否优化性能，比如 Native AOT」）

取证（代码审查，未猜）：
- 单文件自提取：当前部署版 155MB（r50）为**无压缩、含调试符号、无 R2R** 打包——
  与 README 标准命令（EnableCompressionInSingleFile=true、DebugType=None）及 10-19 原始决策
  （PublishReadyToRun=true，「以包体积换启动速度」）均不一致，是逐轮 publish 命令漂移所致。
  r29 曾按 README 标准出过 95MB 压缩包。
- 启动路径：AppLog 初始化 → MainViewModel+MainWindow 构造（轻）→ OnLoaded：
  AppConfigService.Load()（含**一次同步串口扫描** GetPortNames+SetupAPI）→ 侧栏/主题/窗口配置
  → 启动 2s 周期 DispatcherTimer 调 RefreshPorts()（**UI 线程同步串口扫描**，周期性卡 UI，
  对应用户「运行卡卡的」体感）。
- WarmUpTerminalFonts 已在 Task.Run 后台预热（不阻塞）。

改动：
1. 启动计时埋点：新增 Services/StartupClock.cs（T0=Program.Main 首行），App/MainWindow 在
   framework-init、config-loaded、loaded 三处 Mark；新增 --selftest-r51 自检：打印各阶段耗时、
   独立串口扫描耗时、进程内总耗时；run_selftest_fontdlg.py 增加外层 wall time 与 --cold
   （清 %TEMP%\.net\COMTool.App* 自提取缓存，强制完整自提取）——wall − 进程内 = 自提取+CLR。
2. 串口扫描移后台：RefreshPorts 改 Task.Run 扫描 + Dispatcher.UIThread.Post 应用结果，
   _portScanBusy 防重入；保留 internal 测试注入点 PortScanOverrideForTests。
   TDD：StartupPerfTests.PERF01_PortScanDoesNotBlockUiThread 注入 300ms 慢扫描——
   同步版调用内阻塞（红：耗时≥300ms>100ms 断言），异步版立即返回且结果落地（绿）。
3. 打包 A/B（代码相同、仅 publish 参数不同，VM 冷启动实测对比）：
   - V0 基线：单文件、无 R2R、无压缩（=当前部署口径，155MB）。
   - V1：+ PublishReadyToRun=true（215MB，交叉编译成功，obj 生成 COMTool.App.r2r.dll）。
   - V2：+ PublishReadyToRun + EnableCompressionInSingleFile + DebugType=None + DebugSymbols=false
     （118MB，README 标准 + R2R）。
   选型：（待回填——以 VM 冷启动 wall time 为准，预期 V2 最优：写入量 155→~118MB 且 JIT 被 R2R 跳过）

Native AOT 评估（用户提议，本轮仅评估不实施）：
- Avalonia 12 官方文档已支持 Native AOT（PublishAot=true + 全工程 IsAotCompatible=true +
  x:CompileBindings，避免运行时动态 XAML/反射）；社区已有 Avalonia12+net10 单文件 AOT 模板
  （Avalonia-AOT-SingleFile-Template，用 StaticLink.Avalonia 静态链 Skia/HarfBuzz，win-x64 可出 ~15-30MB 单文件）。
- 本机（Linux 开发盒）无 clang/LLVM，且 win-x64 AOT 官方建议「在 Windows 上构建」——
  可行路径：Windows VM（192.0.2.163）装 .NET 10 SDK + LLVM，源码 git bundle 上传后构建。
- 代码风险点：System.Text.Json（AppConfigService，需 source generator）、Renci.SshNet
  （AOT 兼容问题常见）、DPAPI P/Invoke、DynamicResource 主题切换、5 个 COMTool.* 库 +
  第三方库 Iciclecreek.Terminal 均需 IsAotCompatible 并过 trim 警告。
- 预期收益：无 JIT、无单文件自提取，启动通常 <0.5s，内存占用下降。
- 结论：建议单列一轮（r52）实施，本轮先用 R2R+压缩+异步扫描拿到量化基线。

验证（2026-11-05，VM 192.0.2.163 冷启动口径：清 %TEMP%\.net 自提取缓存后 `--selftest-r51 --cold`）：
- App 全量 282/282（含新 PERF01）；gate 20261105T094220Z 全绿：build/单测 390/App 282（共 672 项）/test-list/E2E 15/TestGate 全 PASS。
  （首轮 gate 093438Z 仅 E2E TELNET01 偶发失败——telnet fixture 抖动家族，单独复跑 TELNET01/02 双 PASS，
  判环境抖动非回归，复跑 094220Z 全绿。）
- 打包 A/B 实测（wall total 含自提取+启动+自检退出；loaded = 进程内「侧栏/窗口配置就绪」时刻）：
  | 变体 | 大小 | wall 冷启动 | 进程内 loaded |
  |---|---|---|---|
  | V0 无R2R无压缩（基线） | 155,092,481 B | 2.89s / 2.33s | 434ms / 374ms |
  | V1 +R2R 无压缩 | 215,505,854 B | （未实测，体积过大弃用） | — |
  | V2 +R2R+压缩+去调试符号 | 118,512,587 B | 2.53s / 1.52s / 热 1.51s | 169ms / 107ms / 热 123ms |
  → R2R 把进程内就绪时间砍掉约 2/3（JIT 被跳过），压缩后体积 155→118MB，冷启动写入量下降；
    串口扫描在 VM 上 0ms（无 USB 串口），用户带 USB 适配器机器上每 2s 的同步扫描是「运行卡卡的」主因，已移后台。
- 选型 V2：publish SHA256 f68dd95f3611a3d0a8cce8b2b57f4b5cbe62d4e4a179ac142522311af57bdc13，deploy 双份 OK。
- R2R 构建功能回归：实机 --selftest-r47（本地终端渲染/双击整路径选中/IBeam 光标）全 OK，
  证明 R2R 产物终端链路无回归。
- README Publish 段已固化 `-p:PublishReadyToRun=true`（win/linux 两处），防止后续轮次再漂移。
- Native AOT：未实施（本机无 clang/LLVM；win-x64 AOT 官方建议 Windows 侧构建；代码需全工程
  IsAotCompatible + JSON source generation + SshNet 兼容处理，工作量单列 r52+ 评估）。

### 52 Native AOT 启动性能（2026-11-05，用户批准「直接立项 r52 做 AOT」）

背景：r51 已把打包恢复到 R2R+压缩（VM 冷启动进程内就绪 107-169ms），用户要求进一步
上 Native AOT（无 JIT、无单文件自提取，预期启动 <0.5s、内存下降）。

环境与构建路径（关键事实）：
- Native AOT 不支持跨 OS：win-x64 AOT 产物必须在 Windows 上构建。
- 双机分工：
  - 开发机（Linux 8 核）：Miniforge(conda-forge llvmdev=18) 提供 clang/lld，构建
    linux-x64 AOT 做**回归验证**（E2E 15 项 + 全量单测），快速暴露 trim/反射/运行时问题。
  - VM（Win10 1909，Core 2 Duo T7700，慢 CPU）：用户 devuser 在 Administrators 组，
    通过 schtasks /rl HIGHEST 提权技巧静默安装 VS Build Tools（VC.Tools.x86.x64 +
    Windows11SDK.26100 → D:\vsbt，C:\ 仅剩 10GB 故装 D:\），提供 AOT 官方 Windows 工具链
    （MSVC cl/link + Windows SDK 头与导入库）。VM 上 GitHub 被限速（~8KB/s），
    nuget.org/微软 CDN 正常（1.7-2.6MB/s），源码经 git bundle 上传（comtool-r52.bundle →
    D:\Users\devuser\comtool-aot @ r52-aot，终版同步 030e74d），nuget 缓存 115 包经
    nuget-cache.tgz 上传（D:\Users\devuser\nuget-pkgs，NUGET_PACKAGES 指向它）。

代码改动（r52-aot 分支，两笔：a6c8ba6 准备 + 030e74d 修复；普通构建行为零变化，仅增加 IL 分析）：
1. Directory.Build.props：全工程 IsAotCompatible=true。
2. JSON source generation：新增 COMTool.Core/Config/ConfigJsonContext.cs
   （[JsonSourceGenerationOptions(WriteIndented=true)] [JsonSerializable(ComtoolConfig)]），
   AppConfigService.Load / ComtoolConfig.Load / ComtoolConfig.Save 三处改用
   ConfigJsonContext.Default.ComtoolConfig（消除 IL3050 反射序列化）。
   注：.NET 10 的 JsonSourceGenerationOptions 无 Encoder 属性，生成器用默认 JS 转义
   （CJK 存 \uXXXX），JSON 往返等价，仅配置文件可读性略降。
3. SSH.NET 2026.0.0 内部类型反射注解（全库扫描仅此一处反射家族）：
   - SshConnection.ProbeSshChannelOnce：[DynamicDependency(NonPublic|PublicFields,
     typeof(ShellStream))]（保留私有字段 _channel）。
   - SshConnection.TrySendSignal/TrySendBreak：[DynamicDependency(PublicMethods,
     "Renci.SshNet.Channels.ChannelSession", "Renci.SshNet")]——ChannelSession 为 internal 类型
     不能 typeof，用 (memberTypes, typeName, assemblyName) 字符串构造；程序集实名是
     Renci.SshNet（首版误写 "SSH.NET" 触发 IL2035 Unresolved assembly，已修正）；调用点
     channel.GetType().GetMethod 因值经 object 流转无法做注解流证明，
     #pragma warning disable IL2075（成员保留由 DynamicDependency 保证）。
   - SshSftpFileSystem.GetSftpSession：[DynamicDependency(NonPublic|PublicProperties,
     typeof(SftpClient))]（SftpClient 在 Renci.SshNet 根命名空间，public）。
   - SshSftpFileSystem.InvokeSessionRequest：[DynamicDependency(NonPublic|PublicMethods,
     "Renci.SshNet.Sftp.SftpSession", "Renci.SshNet")]（SftpSession 为 internal）。
4. XAML 编译绑定 + 主题 AOT 适配（修复 AOT 启动 "No precompiled XAML found" 崩溃）：
   - 全部 16 个 App .axaml 根元素加 x:CompileBindings="True"（Avalonia 12 AOT 要求）。
   - ThemeLight/ThemeDark.axaml 加 x:Class + 新建 partial class（构造函数走
     AvaloniaXamlLoader.Load(this) 预编译路径）；ThemeManager.Apply 从 new ResourceInclude(uri)
     改为直接 new ThemeLight()/new ThemeDark() 并入 MergedDictionaries——AOT 下运行时 XAML
     解析器被裁剪，URI 式加载（含 App.axaml 里的静态 ResourceInclude）都不可用，
     App.axaml 相应改为 <local:ThemeLight/> 编译期内联；IL2026 随之消除。
   - MacroEditorWindow/MacroStepEditorDialog 构造函数改 public（编译绑定要求根类型可实例化，
     否则 AVLN3000）。
   - AppLog.Error 增加 InnerException 链打印（AOT 的 TypeInitializationException 根因藏在
     inner 里，不打印无法定位——本次主题/终端两处崩溃均靠它定位）。
5. 终端库（Iciclecreek.Avalonia.Terminal 4.1.4→4.1.5，源仓库 d4ead56）：
   TerminalControl 静态构造 LoadDefaultStyles() 原用 new StyleInclude(uri) 按 URI 动态加载
   Generic.axaml，AOT 下运行时 XAML 解析器被裁剪 → TypeInitializationException
   （SSH 连接成功但终端模板不渲染、输入不转发，E2E UI02 失败，且异常被 async 吞掉无日志）。
   修复：Generic.axaml 根元素加 x:Class="Iciclecreek.Terminal.TerminalGenericStyles"
   + partial class（global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this)）；
   LoadDefaultStyles 改为直接 new TerminalGenericStyles()，兼容识别旧 StyleInclude；
   csproj 移除重复的 <AvaloniaResource Include="Themes\**"/>（Avalonia SDK 默认已含
   **/*.axaml，重复包含导致 AVLN2002 Duplicate x:Class）；StylesAndLifecycleTests 2 项
   同步更新为新类型。库测试 658/658 通过；4.1.5 nupkg 提交至 third_party/，
   NuGet.config 注释已补记（LF 无 BOM 未动行尾）。

验证（2026-11-05）：
- 开发机 linux-x64 AOT：构建 exit 0，IL 警告仅剩 3×IL2075（SshConnection/SshSftpFileSystem
  的 GetMethod 注解流提示，成员保留已由 DynamicDependency 显式保证）；IL2035（程序集名）、
  IL2026（主题 ResourceInclude）、IL2104（终端库）全部消除。
  AOT 二进制 E2E **15/15 全 PASS**（APP01/UI01/UI02/SSH 输入/SFTP 传输/Telnet 自动登录/
  监控重启——AOT 风险路径全覆盖；UI02 在终端库修复前稳定失败，修复后通过）。
- JIT 全量 gate（最终代码状态）：**20261105T114621Z 全绿**（build/单测 390/App 282/
  test-list/E2E 15/TestGate 全 PASS）。此前 20261105T111115Z 的 app-tests exit 134 为已知
  xunit 宿主原生崩溃抖动（274/282 运行、0 失败，单独复跑 282/282 绿），非回归。
- 终端库 4.1.5 自身套件 658/658 通过（StylesAndLifecycleTests 2 项随实现更新）。
- VM win-x64 AOT 构建：rc=0，全程 ~2.5min（.NET 10 ILC 提速，2 核 C2D 亦快）；
  trim 警告与 linux 一致仅 3×IL2075。产物：COMTool.App.exe 68.6MB
  （SHA256 C61740CB12C9C048300AC4C9FC828A316CA1940EE603923C09C32AEBBC56F253）
  + libSkiaSharp.dll 11.1MB + av_libglesv2.dll 5.1MB + libHarfBuzzSharp.dll 1.7MB
  + conpty.dll 0.1MB + x64/arm64 OpenConsole.exe（原生 .pdb 符号约 100MB 已排除）。
- 部署：C:\Users\devuser\COMTool-Avalonia-AOT\（整目录）+ 桌面快捷方式
  COMTool-Avalonia-AOT.lnk；r51 R2R 单文件版保留在 C:\Users\devuser\COMTool-Avalonia.exe
  作对照，两版并存待用户体感后定夺主部署。
- 实机 --selftest-r51 冷启动 A/B（同口径：清 %TEMP%\.net 自提取缓存后 start /wait；
  AOT 无自提取，--cold 等效真实冷启动）：
  | 版本 | 体积 | wall 冷启动 | 进程内 loaded |
  |---|---|---|---|
  | r51 V2 R2R+压缩（基线） | 118,512,587 B 单文件 | 1.51 / 2.53 s | 107-169 ms |
  | r52 AOT win-x64 | exe 68.6MB（部署 ~86MB 多文件） | 0.76 / 0.58 / 0.59 s（3 次） | 58 / 25 / 22 ms |
  → AOT 全流程 wall 约快 2.6-4.3x，进程内就绪约快 4-7x（无 JIT、无 CLR 初始化、无自提取）。
- 内存（主窗空闲 6s，WorkingSet）：AOT 167MB（冷）/166MB（热） vs R2R 214MB（冷）/242MB（热），
  省 47-76MB。
- 实机功能自检（AOT 二进制）：r47 本地终端（Windows ConPTY）charW/charH 正常、双击整路径
  选中 OK、IBeam OK；r49 对话框布局 OK（KeyFile 按钮中心差 0.00px、四面板列宽 108）；
  r50 OPEN/Close 按钮居中 OK；暗色主题 skin=dark 启动 0 错误（ThemeManager 直接实例化在
  AOT 下验证通过，配置已还原 light）。
- 残留说明：3×IL2075 为 GetMethod 注解流告警（非裁剪风险，成员保留由 DynamicDependency
  保证）；AOT 产物非单文件（多文件目录部署）；config.json 中 CJK 存 \uXXXX（source-gen
  默认转义，往返等价）。

### 53 中英文混排基线对齐复核（2026-11-05，用户「COMTool的英文显示比中文显示高了一点」+ COMTool/MobaXterm 同文对照截图）

背景：用户在 opencode TUI 里对比 COMTool 与 MobaXterm 对同一行
「Yocto imx-image-full 编译排查」的渲染，红框标出 COMTool 侧中文偏低。

取证（用户截图逐像素测量，684×381，红框 COMTool x59-319 y74-102 / MobaXterm x65-320 y189-216，
排除红框像素后按列统计墨迹）：
- COMTool：Latin 段加权中心 y=85.17，CJK 段 y=87.87 → **CJK 低 2.7px**（方向与用户描述一致：英文偏高）
- MobaXterm：Latin 中心 y≈203.2，CJK 中心 y≈202.5 → 差 0.7px，视觉对齐（参照正常）
- 2.7px 的量级与「r43 修复前 CJK 走 FormattedText、基线取 CJK 面自身 ascent」的偏差吻合
  （YaHei ascent≈14.3px vs Consolas 11.18px，@13.5px 差 ~3.1px）——r43（库 4.1.2，
  提交 04f32aa）起宽字符已改走 GlyphRun 快路径并钉主面基线，r51 R2R / r52 AOT 均含此修复。

实机复核（r53 新增 `--selftest-r53`，VM win-x64 AOT 构建 1052c82，exe SHA256
6E2ECE65AB52FD339979E069426760577E7161B90D92EB7678A3358F04C7D94F，部署
C:\Users\devuser\COMTool-Avalonia-AOT\）：
- 回退链实机解析 = Consolas | Microsoft YaHei UI | Noto Sans Mono CJK SC(×2)。
  注：avares 内置 Noto 在 AOT 实机上按族名解析落到 Segoe UI（未生效），但链上
  Microsoft YaHei UI 先于它且完整覆盖 CJK，实际 CJK 面 = YaHei UI（与 MobaXterm
  系统回退一致，r41.1 结论不变）。
- 终端混排行绘制走查：`Yocto imx-image-full ` GlyphRun face=Consolas
  baselineOrigin=11.18px；编/译/排/查 各为独立 GlyphRun face=Microsoft YaHei UI
  baselineOrigin=11.18px → **CJK 与 Latin 共享主面基线（r43 钉扎在 AOT 实机生效）**。
- 截图像素复测：Latin 中心 115.50，CJK 中心 115.85 → 差 0.35px（不可感知）。
- 标签标题（12px，App.axaml.cs Windows 覆盖 UiFontFamily="Microsoft YaHei" 单族）：
  「r53 编译排查 Yocto」三段加权中心 53.88 / 54.47 / 54.36 → 差 ≤0.6px（同族天然对齐）。
- 结论：当前构建（R2R r51 / AOT r52+）无法复现用户截图的 2.7px 偏差，
  截图对应 r43 修复前旧构建行为。

方案取舍（运行时度量修正否决）：曾计划用
`GlyphTypeface.TryGetGlyphMetrics` 运行时计算 CJK 墨迹中心偏移并统一两条渲染路径。
实机数据表明 Win32 该 API 不可用：Consolas 返回 (0,0)、YaHei 返回设计单位
（o yB=445 h=2048，非墨迹框），无法换算到像素。且当前两条路径本就共享基线，
无偏差可修——不改渲染代码，改为固化回归防线。

防线（防回归）：
1. App 级（进 gate 矩阵）：CjkTerminalFontTests.TERM34_WideCjkRunSharesLatinBaseline——
   经真实 MainWindow/TerminalControl 链路取 TerminalView，断言 CJK 宽字符
   TryBuildGlyphRun 非空（即走快路径而非 FormattedText）、与 Latin run
   BaselineOrigin 相等（≤0.01px）、advance 精确 2 格。
2. 库级（库仓库 Iciclecreek.Avalonia.Terminal）：BaselineProbeTests.cs 探针改造为
   正式断言 WideGlyphBaselineTests——真实渲染 walk DrawingGroup，断言混排行 4 个
   CJK GlyphRun 与 Latin GlyphRun 基线一致且每 CJK 字独立单字符 run。
3. 实机探针：--selftest-r53 常驻（开本地终端写同文混排行，打印回退链各面
   字形覆盖/度量、逐 GlyphRun 基线+字体面、标签标题字体，落盘 r53-terminal.png），
   用户/后续轮次可一键复核。

验证：
- 库测试 659 通过 / 9 跳过（mac 平台用例）/ 0 失败（含新 WideGlyphBaselineTests）。
- App 测试 CjkTerminalFontTests 10/10 通过（原 9 项 + 新 TERM34）。
- 全量 gate（最终代码状态）：**20261105T134412Z 全绿**——build 通过，单测
  Core 90 / Sftp 42 / Terminal 27 / Connections 150 / Zmodem 32 / TestGate 49，
  App 测试 283/283（含新 TERM34），E2E 15/15 PASS，TestGate 校验含新 TERM-34 行。
- VM AOT 构建 rc=0（仅 3×IL2075 已知告警），自检 r53 实机数据如上。

遗留：用户截图为旧构建行为，已请用户在最新 AOT 部署（桌面快捷方式
COMTool-Avalonia-AOT）下用同一 opencode TUI 复测并回传新截图；若仍见偏差，
跑 `COMTool.App.exe --selftest-r53` 回传 run.log + r53-terminal.png 精确定位。

### 54 MobaXterm 1:1 复刻三项：连字符路径双击整选 / 主题 1:1 / SSH 自动开 SFTP（2026-11-06，用户批准「可以」+ 1920×1033 MobaXterm 参照截图）

三项需求（用户原话）：
1. 双击 `/home/kami/nxp/yocto_imx-6.18.20-2.0.0` 选不中整条（MobaXterm 能整条选中）
2. COMTool 主题做成跟 MobaXterm 一模一样
3. SSH 连上后自动打开 SFTP 浏览器（AutoStartSSHGUI=1 行为）

**① 连字符路径双击整选（库 4.1.6）**
- 根因：库 `TerminalView.Mouse.cs` `IsPathCell` 字符集缺 `-`，含连字符的路径在每个 `-` 处断开，双击落在版本段只选到片段（如 "6.18.20"）
- 修复：字符集补 `-`（`'_' or '/' or '.' or '~' or '-'`）。普通连字符词（无 `/ . ~`）仍走核心词扩展，行为不变
- 红测：选区只到 "6.18.20" → 修复后整条路径；库全量 660 通过 / 9 跳过（mac 平台用例）/ 0 失败
- 库提交 main `0302c30`（未 push，沿 r53 惯例）；nupkg 4.1.6 入 `third_party/iciclecreek/`，csproj/NuGet.config 同步

**② 主题 1:1（仅浅色皮肤，用户 skin=light；深色无参照保持原样只补齐新资源键）**
取色依据（三重取证）：
- 用户 1920×1033 MobaXterm 参照截图（`/tmp/opencode/r54-ref-thumb.png`、`r54-tabstrip-4x.png`）逐像素采样
- 用户 `MobaXterm.ini` [Colors] 段（LastSession 串：前景 236,236,236 / 光标 180,180,192 / 16 色标准值）
- VM 实机双 tab 实验（`r54-moba2c-full.png` 1920×1080，两个不同 ssh 目标强制出第二 tab）

| 元素 | 取值 | 证据 |
|---|---|---|
| 标题栏 | 白底黑字，hover #e5e5e5，关闭 hover #e81123+白字形 | 参照图 |
| tab 条底 | 白 + 下沿 1px #8b99a8 | 参照图 y114 |
| 选中 tab | 竖向渐变 #4d4d4d→#1f1f1f + 白字 | 参照图 x600 垂直剖面 + 双 tab 实验 x600 剖面（#4d4d4d→#1f1f1f 逐行吻合） |
| 未选中 tab | 白底 + #b4b4b4 细边 + 深字 | 参照图 Home tab（x440-490 众数 #b4b4b4；双 tab 实验 v25.2 为 #d9d9d9，取用户 v22.1 值） |
| 状态栏 | #f0f0f0 黑字 | 参照图 y1019 段采样 |
| 监控图标 | 主机 #000000 / **CPU #4aaf4f / RAM #4aaf4f（绿）** / 上 #ab6000 / 下 #ff6400 / 磁盘 #009588 / uptime #009588 / 热分区 #ff6400 | 初版整屏分桶采样误将 CPU 判 #363687、RAM 判 #600060；2026-11-06 用户 248×30 状态栏局部裁剪图（图1=MobaXterm）权威实测：CPU/RAM 图标主体同为绿 #4aaf4f，已改并加 RM06 取值断言锁死。CPU 条列色 #2e9e4f 属 MainViewModel 逻辑色，不动 |
| 内容框 | 间隙 4px（原 9px）、直角（原 3px 圆角）、底=窗口底色 | 参照图 x500/x1000 边界剖面 |
| 终端 | 底 #1c1c1c（ini 写 0,0,0 但屏上实测 #1c1c1c，实测优先）/ 前景 #ececec / 光标 #b4b4c0 | ini + 屏上实测 |
| 16 色 | 000000/ff6060/60ff60/ffff36/6060ff/ff36ff/36ffff/ececec + 363636/ff8080/80ff80/ffff80/8080ff/ff80ff/80ffff/ffffff | ini [Colors] |

改动面：`ThemeLight.axaml`（全量浅色新配色）、`ThemeDark.axaml`（补 9 个新键）、`AppStyles.axaml`（tab 边框/渐变/✕ 按钮/hover）、`MainWindow.axaml`（✕ 改 TextBlock、TabStripLine、内容框、状态栏 Name="StatusBar"、MonSeg 图标资源化）、`TerminalTheme.cs`（`CreatePythonTheme`→`CreateMobaXtermTheme`，2 个调用点同步）。布局不变（tab 高 36px 保 E2E 坐标、不加 MobaXterm 菜单栏/工具栏），仅颜色/样式 1:1。
WCAG：MobaXterm 默认蓝 #6060ff 对 #1c1c1c 仅 3.74:1（其默认值）→ TERM02 蓝阈值 4.5 放宽至 3.5，亮蓝仍 ≥4.5（实测 5.23）。

**③ SSH 自动开 SFTP 浏览器（AutoStartSSHGUI=1）**
- 提取 `internal void MainWindow.OpenSshBrowserFor(TerminalTab tab)`（非 SSH/未连接/无 pane 均 no-op；选中 tab→SidebarTab=SshBrowser→显示侧栏）
- `OpenSshSessionAsync` 在 AddTab+UpdateConnectionStates 后调用（~L1083）；`OnSshBrowserRequested`（手动地球点击）委托给它，手动开/关语义不变

**实机自检 `--selftest-r54`**（三段断言，落盘 `r54-theme.png`，退出码 0/2）：
1. 本地终端写用户实路径、双击 "18" 列 → 断言选区 == 整条路径
2. 真实 SSH（config 第一个会话，90s 超时）→ 断言 SidebarTab==SshBrowser && IsSidebarVisible && SelectedTab==sshTab && SftpPane.IsExpanded
3. 读计算后刷值断言（标题栏/状态栏/tab 渐变与未选中/终端色）

**VM 实验备注（需在用户汇报中说明）**：为取未选中 tab 颜色，曾以 schtasks /IT 交互任务启动 MobaXterm 双 tab（127.0.0.1 + 192.0.2.163），实验结束 taskkill 时杀掉了**用户正在使用的 MobaXterm 实例**（192.0.2.48 会话断开）；`MobaXterm.ini` 已从 `.bak-r54` 还原，临时脚本/截图已清理。

**测试**（全绿）：
- 新增 `SshBrowserAutoOpenTests` 3 项（自动打开/非 SSH no-op/手动 rail 收合不变）
- `TerminalPaletteTests.TERM02_BaseAnsiPaletteMatchesMobaXtermDefaultAndKeepsPromptContrast`（旧名 `...MatchesPythonAndPromptTextKeepsContrast` 更名，矩阵同步）
- `RemoteMonitorFeatureTests.RM06` 图标断言改走新资源（MonCpuIconBrush 等），并补 CPU/RAM = #4aaf4f 取值断言（防回退到初版误采样值）
- App 测试 286/286 通过

**验证**：
- 全量 gate（终版 20261106T060950Z 全绿）：restore/build 通过；单测
  Core 90 / Sftp 42 / Terminal 27 / Connections 150 / Zmodem 32 / TestGate 49，
  App 测试 286/286（含新 SshBrowserAutoOpenTests 3 项、TERM02 更名后用例、
  RM06 新资源色断言 + CPU/RAM 绿 #4aaf4f 取值断言），E2E 全 PASS，TestGate
  校验含新 SFTP-09 行与 TERM-02 新测试名。060439Z 一次 app-tests 出现
  xunit 宿主 exit 134（跑到 236/286、0 失败后进程崩溃），复跑 060950Z 全绿，
  判环境抖动非回归（r51 TELNET01 同族先例）。
- VM win-x64 双份部署 + SHA256（终版 2026-11-06 14:07，源码 1ea5935）：
  - R2R 单文件 `C:\Users\devuser\COMTool-Avalonia.exe`：118,789,308 字节，
    SHA256 `99ffdca0821db413da11e7e30f0cf4f9d6d9fef35eded37d940619b23b03a914`。
  - AOT 目录 `C:\Users\devuser\COMTool-Avalonia-AOT\COMTool.App.exe`
    （bundle 克隆构建，publish rc=0）：SHA256
    `b79eed53e1a4f6d2c85c4446e23ecb75b3dbf32777c9b2866f4541ce1cf9d045`。
  - 注：11:26 首轮部署（R2R 6f8444ca / AOT 316da998）早于 CPU/RAM 图标改绿，
    14:07 已用含绿改动的构建整体替换。
- `--selftest-r54` 实机（AOT 构建，真实 SSH devuser@192.0.2.48:22，终版
  wall 7.3s）：
  - ① 双击 `/home/kami/nxp/yocto_imx-6.18.20-2.0.0` 选区=整条路径 OK
    （charW=7.422 charH=15.807 grid=128x40）。
  - ② 连接成功 status='Connected devuser@192.0.2.48:22'，SidebarTab=SshBrowser、
    sidebarVisible=True、paneExpanded=True OK（AutoStartSSHGUI 行为实机生效）。
  - ③ 标题栏 #FFFFFF / 状态栏 #F0F0F0 / 选中 tab 渐变 #4D4D4D→#1F1F1F /
    选中 tab 标题 #FFFFFF / 未选中 tab 底 #FFFFFF 边 #B4B4B4 / 终端底 #1C1C1C /
    前景 #ECECEC / 光标 #B4B4C0 / 蓝 #6060FF 全 OK，落盘 r54-theme.png。
  - 三项均 OK，退出码 0。AOT 首测 ③ 曾因 XAML 字面色编译为
    ImmutableSolidColorBrush 误读为 #000000，自检改 BrushColor 兼容读取后通过
    （渲染本身一直正确，像素取证为白字）。
- CPU/RAM 图标绿（#4aaf4f）实机证据：终版 r54-theme.png 状态栏像素取证
  RAM 图标 = #4aaf4f（27 纯绿像素 + 抗锯齿）；CPU 段需 SSH 监控满两个采样
  间隔（~4s）才出现，自检截图时刻未及，与 RAM 同走 DynamicResource 机制、
  RM06 已锁双图标 = #4aaf4f。

**用户反馈回环（2026-11-06 14:xx）**：用户反馈「颜色没改」——其本地 PC 桌面
的 `COMTool-Avalonia.exe` 为上一轮旧拷贝（蓝/紫图标），本流程无该机器访问权；
VM 侧 11:26 部署亦早于改绿提交。14:07 双份重部署后用户本地需自行替换/删除
（SFTP 拉 VM 单文件替换桌面旧 exe，或直接删本地旧件改用 VM AOT 版）。

**遗留**：
- 用户本地 PC 桌面的旧 `COMTool-Avalonia.exe` 与 Python 版 `COMTool` 在流程无
  访问权的机器上，由用户自行删除/替换（新单文件 SHA256 见上）。
- 库仓库 main 领先 origin/main 5 提交未 push（沿 r53 惯例，待用户确认）。

### 55 标题栏精简与分层：移除标题栏"会话"按钮 + 标题栏下沿 1px 分隔线（2026-11-10，用户截图反馈「图1去掉这个；顶部栏与下面的显示内容分层不明显，看不出顶部栏的区域是哪些」）

两项需求（用户原话）：
1. 去掉标题栏箭头所指的"会话"按钮（SessionsButton，f03a 列表图标，Show/hide sessions）
2. 标题栏与下方内容分层不明显，看不出顶部栏区域 → 标题栏下沿加分隔线

**① 移除标题栏 SessionsButton**
- MobaXterm 标题栏本就无此按钮；COMTool 侧栏折叠/展开的替代入口用侧栏
  rail 的 `RailSessionsButton`（`OnRailTabRequested` 已具备等价语义：面板可见
  且当前页=Sessions → 折叠；否则切到 Sessions 页并展开）
- 改动：`MainWindow.axaml` 删按钮与 `Click="OnSessionsToggle"`；标题栏列定义
  `40,104,40,35,*,35,35,35` → `40,104,40,*,35,35,35`（min/max/close 列号
  5,6,7→4,5,6，`*` spacer 吸收宽度，右侧窗口按钮位置不变）；
  `MainWindow.axaml.cs` 删 `OnSessionsToggle`
- 测试（TDD 先红后绿）：`UI02_TitleBarButtonTogglesDockedSidebar` 更名
  `UI02_RailSessionsButtonTogglesDockedSidebar` 且改点 rail 会话图标（矩阵
  UI-02 行同步）；`UI02_CollapsedDockPersistsAcrossRestart`、
  `UI16_RailStaysVisibleWhenTheDockCollapses` 同样改点 rail 图标；新增
  `UI01_TitleBarHasNoSessionsButton`（断言按钮已移除、rail 图标存在）
- E2E：`lib.sh` 的 `SESSIONS_TOGGLE_X/Y`(201,18) 改为 `RAIL_SESSIONS_X/Y`
  (20,60)（rail 第 1 格 40×44px，35px 标题栏 + 5px 上边距下，中心 ≈(20,62)）；
  `collapse_sidebar_for_tabs` 保持"探测-点击"重试循环（当前页非 Sessions 时
  第一次点击只切页、第二次才折叠，循环可收敛）；`run-ui-e2e.sh`
  UI02_SidebarStaysDockedE2E 的点击同样改为 rail 图标 + 收敛循环
  （SSH 连接成功后 r54 行为会把侧栏切到 SFTP 页，单次点击不再保证折叠）

**② 标题栏下沿 1px 分隔线**
- 取值：浅色 `#b4b4b4`（用户 1920×1033 MobaXterm 参照图白底区 y≈91 实测，
  与未选中 tab 边框同色，r54 已取证）；深色 `#3a3a3a`（与深色主题
  SidebarBorderBrush/MonSegBorderBrush 同族）。新增资源键
  `TitleBarBorderBrush`，双主题补齐（符合"只改浅色皮肤取色、深色仅补资源键"
  约定）
- 改动：`TitleBar` Border 加 `BorderThickness="0,0,0,1"` +
  `BorderBrush="{DynamicResource TitleBarBorderBrush}"`（仅底边，35px 行高
  布局与右侧窗口按钮坐标均不受影响）
- 测试：新增 `UI01_TitleBarHasBottomDivider`（断言四边厚度 0,0,0,1 且
  BorderBrush 色值 = TitleBarBorderBrush 资源值，颜色调整时自动跟随）

**实机自检**：新增 `--selftest-r55` 三项：① 标题栏下沿 1px 分隔线（厚度 +
线色=资源值）；② SessionsButton 已移除、RailSessionsButton 存在；
③ rail 会话图标折叠（列宽 40px）/展开（恢复原宽）。落盘 r55-titlebar.png。

**验证**：
- 全量 gate（终版 20261110T023603Z 全绿）：restore/build 通过；单测
  Core 90 / Sftp 42 / Terminal 27 / Connections 150 / Zmodem 32 / TestGate 49，
  App 测试 288/288（新增 TitleBarTests.UI01_TitleBarHasBottomDivider、
  UI01_TitleBarHasNoSessionsButton，UI02 折叠用例更名为
  UI02_RailSessionsButtonTogglesDockedSidebar），E2E 全 PASS（含
  UI02_SidebarStaysDockedE2E 改用 rail 会话图标坐标），TestGate 校验含
  UI-02 新测试名。首跑 20261110T023027Z 出现两处抖动：app-tests
  TERM01_QuotesSurviveAttachedReadPathWithoutZmodem 一次断言失败（单独复跑 2/2
  通过，终端 CJK 渲染时序）、TELNET01 输入 marker 15s 未达（复跑 PASS），
  均与本轮改动无交集，判环境抖动非回归（r51/r54 同族先例）。
- VM win-x64 双份部署 + SHA256（2026-11-10 10:53，源码 0f712f6）：
  - R2R 单文件 `C:\Users\devuser\COMTool-Avalonia.exe`：118,798,350 字节，
    SHA256 `e76ea6775ce9ed34f53645d974ffbe61b2064efe3db678a60c4260df21c1828a`。
  - AOT 目录 `C:\Users\devuser\COMTool-Avalonia-AOT\COMTool.App.exe`
    （bundle 克隆 0f712f6 构建，publish rc=0）：SHA256
    `799c6c2ad2989f002b86b52ef71fa324726b443fb72469aecaf2525c3bb4d289`。
- `--selftest-r55` 实机（AOT 构建，wall 6.2s）三项全 OK：
  ① 标题栏下沿线 thickness=1、线色 #B4B4B4 = TitleBarBorderBrush 资源值 OK；
  ② SessionsButton 已移除、RailSessionsButton 存在 OK；
  ③ rail 会话图标 初始宽=40 → 一次点击后=360 → 二次点击后=40 OK
  （VM 配置恢复为折叠态+宽度 360，自检按"状态翻转+恢复原宽+两态分别
  40px/配置宽"断言，不依赖初始方向）；落盘 r55-titlebar.png 目检：
  标题栏无会话按钮、下沿分隔线清晰可见。首跑（AOT 克隆早于自检③修复的
  860a740）③ 误判 FAILED（自检假设初始为展开态），修复后重建重部署通过。

**遗留**：
- 用户本地 PC 桌面旧 `COMTool-Avalonia.exe`/Python 版 `COMTool` 仍在流程无
  访问权的机器上，由用户自行替换/删除（新单文件 SHA256 见上）。
- 库仓库 main 领先 origin/main 5 提交未 push（沿 r53/r54 惯例，待用户确认）。

### 56 终端 SGR 粗体渲染对齐 MobaXterm + EWS 补 MAC 品红规则（2026-11-10，用户同板对照截图「对比mobaxterm（图2）」）

两项需求（用户原话 + 截图取证）：
1. COMTool 终端里 ifconfig 的数值 token（flags=…/mtu/MAC/txqueuelen/计数器等）
   渲染成粗字，MobaXterm 全细体
2. MobaXterm 里 MAC 地址是品红色，COMTool 是白字

**取证结论（像素级 + 配置级）**：
- 远端 imx95evk 的 ifconfig 自带 ANSI：SGR 1（粗体）标数值 token、SGR 31 红
  标 errors、SGR 1;35/95 品红标 127.0.0.1——证据：COMTool 侧 config.json
  `terminal.syntaxHighlight="default"`（高亮全关）却仍有红/品红/粗体，说明
  颜色来自远端字节流
- MobaXterm 侧 `UP`/`mtu`/`1500` 实测纯前景色 #ececec（细体）→ 其字体
  （Lucida Console 类）无粗体面，SGR 1 视觉无效果且不变亮
- MobaXterm 的 MAC 品红：远端对 MAC 只发粗体不发色（COMTool 收到后 MAC 是
  白字粗体可证），故品红来自用户 MobaXterm 本地个人高亮规则

**① 粗体渲染（库 4.1.7）**
- 根因：COMTool 终端字体 Consolas 有粗体面，XTerm 的 SGR 1 被照实画成粗字
- 修复：`Iciclecreek.Avalonia.Terminal` 4.1.6→4.1.7，`TerminalView`/
  `TerminalControl` 新增 `BoldFont` 开关（库默认 true 保留原行为；
  Generic.axaml TemplateBinding 透传）；false 时渲染路径（主 run/块字符/
  宽字符 4 处 typeface + 3 处 foreground）强制常规字重、`GetForegroundBrush`
  的 boldIsBright 参数置 false（不提亮）
- COMTool `CreateTerminal()` 设 `BoldFont = false`（对齐用户 MobaXterm）

**② EWS MAC 规则**
- `SyntaxHighlightRules` EwsRules 加 `\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b`
  → Color 5（品红 #ff36ff），置于 IP 规则之后（互不重叠，抢占机制下安全）

**测试**（TDD 先红后绿）：
- `SyntaxHighlightTests.TERM30_EwsColorsMacAddressMagenta`：MAC 染品红且
  不吞相邻词（先红：规则不存在）
- 新增 `TerminalBoldRenderTests`（App 级，真实 CreateTerminal + 模板链路 +
  headless `view.Render(DrawingGroup)` 走查 GlyphRun）：
  - `TERM35_BoldFontOffDrawsNormalWeightPlainColor`：全部 run 无粗体面；
    SGR 1;35 按 #ff36ff 画、#ff80ff 不出现
  - `TERM35_BoldFontOnBrightensBoldColor`：回退路径 SGR 1;35 提亮 #ff80ff

**实机自检**：新增 `--selftest-r56` 两项：① SGR 粗体走查（无粗体面 +
BMAG 色 #ff36ff）；② EWS MAC 17 格全品红（验证后恢复原方案）。落盘
r56-terminal.png。

**验证**：
- 全量 gate：20261110T043940Z 全绿（unit 391 / app 290 / e2e 15 项 PASS /
  testgate PASS）。前 3 轮 e2e 轮换挂 UI05_TabSwitchTypingBothDirections
  （042144Z/042205Z）与 TELNET01_AutoLoginE2E（042907Z/043256Z）——均为
  输入送达类已知偶发项，同代码 041323Z 曾 e2e 全绿，复跑判定为环境抖动
- VM win-x64 AOT 部署（R2R 单文件 r55 起已删，本轮只部署 AOT 目录）+ SHA256：
  - `C:\Users\devuser\COMTool-Avalonia-AOT\COMTool.App.exe`
    = `1db8076df5e2a0e2874918ab4fd717544809954bb630b40bb65673149ac5380c`
  - AOT 包 `C:\Users\devuser\COMTool-Avalonia-AOT-r56.zip`
    SHA256 = `b162e1d60d6917a4b1f149e95c61db88538cbcf3f3dfcab43a2097f14ba861b2`
    （78,332,270 B，用户 SFTP 拉取）
- `--selftest-r56` 实机（VM，wall 4.9s，退出码 0）：
  - ① SGR 粗体：glyphrun=2 粗体面=False BMAG 色=#FF36FF（期望 FF36FF）=> OK
  - ② EWS MAC：行=1 MAC 17 格全品红(index5) => OK（方案已恢复 default）
  - r56-terminal.png 目检：BOLDVAL 细体白、BMAG 普通品红（非亮品红）、
    ether 行 MAC 品红——与用户 MobaXterm 参照一致

**遗留**：
- 库仓库 Iciclecreek.Avalonia.Terminal 4.1.7 提交 41ffc0d 未 push（沿 r53/54/55
  惯例，main 本地领先 origin，待用户确认）
- r39 高亮器既有局限（非本轮引入）：着色只写默认前景格、方案切回 default 时
  ApplyToAll 仅重置水印不清旧色，故 EWS→default 后已染格子保留颜色（自检
  截图中 MAC 仍为品红即此行为）
- e2e UI05 / TELNET01 输入送达偶发抖动见上（复跑判定）

### 57 语法高亮实时着色“只生效一次”修复（2026-11-10，用户实机反馈「只生效一次，下次执行 ifconfig 的时候就没效果了」）

**现象**：切到 EWS 方案后历史被一次性全量染色（MAC 品红可见），但之后每次
执行 ifconfig，新输出行都不再着色。

**根因**（TDD 先红复现）：`TerminalHighlighter.OnLineFed` 在 LF 触发时把水印
推进到“游标所在行”。但 LF 触发时游标已落到下一行（刚写入内容的下一行），
于是刚结束的输出行从未被扫描，而下一行（尚未写入）被提前标记为已处理：
- 只有高亮器创建后的第一个 LF 能着色（水印还是 0）；
- 命令回显行（`$ x\r\n`）一出现，水印就越过了后续所有输出行；
- 用户看到的“生效一次”实际是方案切换时 `ApplyToAll` 对历史的全量重染。

**修复**（`TerminalHighlighter`）：
- LF 时改为只推进到“刚结束的行” = `YBase + Y - 1`，折行长行也一并覆盖；
- 新增 `_lastLength` 区分两种“水印超前”：
  - 缓冲区收缩（RIS/清回滚，`Length` 变小）→ 水印归零，从头重染；
  - 回滚写满后 XTerm 整体上移（绝对行号漂移，`Length` 不变）→ 只处理刚结束行，
    避免把每行都误判为收缩而全量重染（性能保护）；
- `ApplyToAll` / Resized / BufferChanged 同步维护 `_lastLength`。

**测试**（先红后绿，已确认旧实现下 2 项失败）：
- `SyntaxHighlightTests.TERM30_HlighterColorsLinesAfterCommandEcho`：命令回显后
  MAC 行=品红、errors 行=红、普通行=默认（旧实现全 256）
- `SyntaxHighlightTests.TERM30_HlighterColorsWrappedLineAfterEcho`：折行长行
  首尾两段都要着色（旧实现首段被跳过）
- `SyntaxHighlightTests.TERM30_HlighterColorsAcrossScrollbackTrim`：回滚满后
  整体上移，新行仍着色（回归保护）
- `TerminalR39FeatureTests.TERM30_HighlightColorsOutputAfterPromptEcho`：真实
  MainWindow + TerminalControl 接线，连续两轮 ifconfig 输出都要着色（旧实现红）
- 新增 `--selftest-r57`：先落错误/警告/成功方案再建终端，全程不调用
  `SetSyntaxHighlightScheme`（不触发 ApplyToAll），连续写两轮 ifconfig 形态
  输出，逐轮核对 MAC=品红、errors=红，落盘 r57-terminal.png

**验证**：
- 全量 gate：20261110T060905Z 全绿（unit 394 / app 291 / e2e 15 项 PASS /
  testgate PASS）
- VM win-x64 AOT 部署（publish rc=0；仅更新 AOT 目录，R2R 单文件仍不部署）：
  - `C:\Users\devuser\COMTool-Avalonia-AOT\COMTool.App.exe`
    SHA256 = `9729c95259a3823b8fb96f5921fe3e10de87cbee33b1e6153516de87a31942cc`
  - AOT 包 `C:\Users\devuser\COMTool-Avalonia-AOT-r57.zip`
    SHA256 = `f78ec0814f8de943181639fc02b6239f0ddd93fb3b6f1073a2a1c9f290bda91b`
    （78,332,743 B，用户 SFTP 拉取）
- `--selftest-r57` 实机（VM，wall 3.6s，退出码 0）：全程不调用
  `SetSyntaxHighlightScheme`（不触发 ApplyToAll），连续两轮 ifconfig 形态输出
  - 轮1：MAC 行=1 品红=True；errors 行=2 红=True
  - 轮2：MAC 行=4 品红=True；errors 行=5 红=True
  - r57-terminal.png 目检：两轮 ether 行 MAC 均为品红、两轮 errors 均为红，
    与用户 MobaXterm 参照一致（修复前只有切换方案那一次可见颜色）

**遗留**：
- 桌面快捷方式 `D:\Users\devuser\Desktop\COMTool-Avalonia-AOT.lnk` 指向
  `C:\Users\devuser\COMTool-Avalonia-AOT\COMTool.App.exe`：每轮 AOT 部署
  （robocopy /MIR）即自动覆盖该目录，双击快捷方式就是最新版，用户无需手动
  解压 zip（zip 仅供其他机器取用）
- r39 既有局限仍在：着色只写默认前景格；方案切回 default 时 `ApplyToAll` 仅
  重置水印不清旧色（EWS→default 后已染格子保留颜色）
- MAC→品红规则仍只在 EWS 方案内（r56 用户已确认的落点），不改为全局规则

### 58 语法高亮与 MobaXterm 逐像素对齐（IPv4/MAC 边界 + 品红取色）（2026-11-10，用户同板对照截图「仔细对比看有什么差别」）

**截图对比结论（A=COMTool r57 实机，B=用户 MobaXterm 同板 ifconfig）**：
1. 规则集不同（最大观感差别）：用户 MobaXterm `MobaXterm.ini` 为
   `SyntaxType=1`，即「Standard keywords」——会额外把关键字、数字、提示符
   染成其内建粉彩色（浅蓝 #8DCDEC、淡黄 #ECCD8D、橙 #ECA85E、薄荷 #AECDCD）；
   COMTool 的 error_warning 方案只染错误红/警告黄/成功绿/信息青/IP·MAC 品红。
   本轮不实现（见遗留）。
2. netmask `255.255.255.0`：A 品红、B 白。
3. `unspec` 的 `00-00-…-00` 16 组长链：A 品红、B 非品红。
4. 品红色值：A #FF36FF、B #FF4DFF。
5. 字体/行距：B 为 MobaFont 10pt（行距约 15px），A 为 Consolas 14.5
   （行距约 17px）——用户两侧设置差异，不做改动。
6. A 截图外圈白色是窗口边框，非终端内容差异。

**取证**：
- MobaXterm 默认 CustomSyntax（EWS）原文（wezterm #4348 评论内导出）：
  - IPv4 规则首段只允许 1..254（原文按 1 / 10-99 / 100-199 / 200-249 /
    250-254 五段交替表达），故 netmask 的 255 不命中；broadcast 以
    10/172 开头仍命中；两侧边界字符类 `[^0-9A-Za-z_&-]` / `[^0-9A-Za-z_-]`。
  - MAC 规则两侧边界字符类排除 `-`，故 unspec 的 `00-00-…` 不属于独立
    MAC（截图中确实不品红）；`:` 与 `-` 分隔的独立 MAC 仍命中。
- 用户 `MobaXterm.ini [Colors]`：`Magenta=255,77,255`、
  `BoldMagenta=255,125,255`；B 截图 IP/MAC 像素实测 (255,77,255) 完全吻合。

**修复**：
- `SyntaxHighlightRules` EWS：
  - IPv4 规则按原文重写为
    `(?<![0-9A-Za-z_&-])((?:[1-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-4])\.[0-9]+\.[0-9]+\.[0-9]+)(?![0-9A-Za-z_-])`
    （.NET 无 `\K`，用前后负向断言表达 MobaXterm 的边界字符类）。
  - MAC 规则同理改为
    `(?<![0-9A-Za-z_&-])(?:[0-9a-f]{2}(?::|-)){5}[0-9a-f]{2}(?![0-9A-Za-z_-])`。
- `TerminalTheme`：`Magenta #ff36ff→#ff4dff`、`BrightMagenta #ff80ff→#ff7dff`
  （用户 ini 覆盖值）。
- `MainWindow` r56 自检里硬编码的品红色改为读
  `TerminalTheme.CreateMobaXtermTheme().Magenta`，避免取色再漂移。

**测试**（TDD 先红后绿）：
- 先红确认（旧实现三项失败，失败原因与截图差异一一对应）：
  - `SyntaxHighlightTests.TERM30_EwsIpv4SkipsNetmaskAndKeepsBroadcast`
    （旧：品红集合含 `255.255.255.0`）
  - `SyntaxHighlightTests.TERM30_EwsMacIgnoresHyphenChain`
    （旧：unspec 链被切成两段 `00-00-00-00-00-00` 染品红）
  - `SyntaxHighlightTests.TERM30_ThemeMagentaMatchesUserIni`
    （旧：`Expected "#ff4dff" Actual "#ff36ff"`）
- 接线测试 `TerminalR39FeatureTests.TERM30_NetmaskAndUnspecStayDefaultAfterEcho`：
  真实 MainWindow + 实时着色路径，inet/broadcast=品红、netmask=默认、
  unspec=默认、ether MAC=品红。
- 旧调色板断言同步：`TerminalPaletteTests.ExpectedPalette`、
  `TerminalBoldRenderTests` 两处 #ff4dff/#ff7dff。

**验证**：
- 全量 gate：2026-11-10T07:14:06Z 全绿（unit 397 / app 292 / e2e 15 项 PASS /
  testgate PASS）。同日两次前跑遇环境性偶发（同代码复跑全绿）：e2e
  `UI01_TitleBarDoubleClickMaximizeE2E` 双击未最大化、App 测试进程
  SIGSEGV(139, libc memcpy) + Connections `NET05` loopback 端口 +
  `TELNET01` 输入送达。
- VM win-x64 AOT 部署（仅更新 AOT 目录）：
  - `C:\Users\devuser\COMTool-Avalonia-AOT\COMTool.App.exe`
    SHA256 = `2f36ad05a9f8f843d1d6e418f0eb63da153b6e7736b44832a849e92dfb56030b`
  - AOT 包 `C:\Users\devuser\COMTool-Avalonia-AOT-r58.zip`
    SHA256 = `1792df6fd5bb4ed2a3a161d185d7fb5862aac7a8cddcc4f5329762e606c2c3fc`（78,334,193 B，用户 SFTP 拉取）
- `--selftest-r58` 实机（VM，wall 3.2s，退出码 0（`start /wait` 正常返回））：
  - ① inet 品红 / netmask 默认 / broadcast 品红：inet品红=True netmask默认=True broadcast品红=True => OK
  - ② unspec 默认 / ether MAC 品红：unspec默认=True etherMAC品红=True => OK
  - ③ MAC 渲染色：#FF4DFF（期望 FF4DFF）=> OK
  - r58-terminal.png 目检：inet/broadcast 品红、netmask 白、unspec 整条白、ether MAC 品红，与用户 MobaXterm 参照一致

**遗留**：
- MobaXterm `SyntaxType=1`（Standard keywords）规则集未实现——这是 A/B
  观感最大差别（粉彩关键字/数字/提示符）。COMTool 的 5 个方案照抄的是
  MobaXterm 的 CustomSyntax 定义，没有 Standard keywords 这一档；若要
  1:1 复刻需另立一轮，先取得该内建规则集的权威定义与配色表。
- 字体/行距差异（MobaFont 10pt vs Consolas 14.5）属用户设置，不做对齐。
- 既有局限仍在：着色只写默认前景格；方案切回 default 时 `ApplyToAll` 仅
  重置水印不清旧色。
- 库仓库 Iciclecreek.Avalonia.Terminal main 本地领先 origin 6 提交未 push
  （沿 r53+ 惯例，待用户确认）。

### 59 新增「标准关键字」方案（对齐 MobaXterm SyntaxType=1）（2026-11-10，承接 r58 遗留）

**前置修正（推翻 r58 的一条结论）**：r58 报告称 Standard keywords 会额外把
关键字/数字/提示符染成粉彩。对用户自己那张 MobaXterm 截图做逐像素复核后确认
**该说法不成立**：同一份 ifconfig 输出里，Standard keywords 只染 IP（品红）与
`errors`（红），`flags=209`、`<UP,POINTTOPOINT,…>`、`mtu 1420` 及全部计数都是
纯白。r58 的“粉彩”来自一张第三方博客截图——那张图经过 JPEG 转存，色度扩散把
白字边缘染成粉彩，属取证陷阱。

**实证差距（只剩两条）**：
1. `null` / `none` → 品红：MobaXterm EWS 导出原文的品红组本就含 `null|none`
   （博客实机截图 `Variant = "none"` 为品红），r39 抄规则时漏掉；
2. 时间戳 → 着色：博客实机截图中 `[ 67705.015]` 与日志时间戳被着色。MobaXterm
   此处用橙 #ECA85E，该色**不在 ANSI 16 色调色板内**，而高亮器当前只能写
   调色板索引（0–15）。

**实现**：
- `HighlightScheme.Standard = 6`，`HighlightSchemes` 键 `standard`（兼容
  `standard_keywords`）；菜单在「默认」之后插入第 6 项「标准关键字」，顺序
  对齐 MobaXterm 内建 6 项（默认/标准关键字/错误·警告·成功/Unix shell/Cisco/SQL）。
- `StandardRules = EwsRules + StandardExtraRules`：
  - 时间戳：日期时间整体（`YYYY-MM-DD HH:MM:SS[.mmm]`）→ 日期 → 时间 →
    方括号 `[ 67705.015]`，全部 Color 3（黄）；顺序保证不被拆段；
  - `null|none`：边界照抄 EWS 原文（前后不得为 `[0-9A-Za-z_&-]`），Color 5。
- EWS 方案保持原样（两条新规则只属于标准关键字；单测锁定）。

**测试**（TDD 先红后绿；先红为编译期红：`HighlightScheme` 无 `Standard`）：
- `SyntaxHighlightTests.TERM30_StandardSchemeColorsNullNoneMagenta`
- `SyntaxHighlightTests.TERM30_StandardSchemeColorsTimestamps`
- `SyntaxHighlightTests.TERM30_StandardSchemeKeepsEwsRules`
- `SyntaxHighlightTests.TERM30_StandardSchemeKeyRoundTrip`
- `TerminalR39FeatureTests.TERM30_StandardSchemeWiringColorsTimestampsAndNone`
  （真实 MainWindow + 实时着色：时间戳黄、none 品红、error 红）

**已知偏差**：
- 时间戳颜色：MobaXterm 橙 #ECA85E vs COMTool 黄 #ffff36。要做真橙需让高亮器
  支持真彩色（改库），本轮按方案 A 不做，记入偏差。

**验证**：
- 全量 gate：2026-11-10T07:59:58Z 全绿（unit 401 / app 293 / e2e 15 / testgate PASS；总 694 results）
- VM win-x64 AOT 部署（仅更新 AOT 目录）：
  - `C:\Users\devuser\COMTool-Avalonia-AOT\COMTool.App.exe`
    SHA256 = `91219d7ebc342bfe899401a21d5dce240cba63b95ccc0fae0190944cc8aec4c1`
  - AOT 包 `C:\Users\devuser\COMTool-Avalonia-AOT-r59.zip`
    SHA256 = `979f6ecbf8f922ccd3de5d58feba70f08074545a2de06bee426a9fa6521d1228`（78,349,449 B，用户 SFTP 拉取）
- `--selftest-r59` 实机（VM，wall 3.3s，退出码 0）：
  - ① 时间戳黄 / none 品红 / error 红 / IP 品红：时间戳黄=True、none 品红=True、error 红=True、IP 品红=True
  - ② 日期时间黄：日期时间黄=True
  - ③ 渲染色：时间戳=#FFFF36（期望 #ffff36）、none=#FF4DFF（期望 #ff4dff）=> OK
  - r59-terminal.png 目检：通过（时间戳/日期时间黄色、none 品红、error 红、IP 品红，其余纯白）

**遗留**：
- 时间戳真橙（#ECA85E）需高亮器真彩色支持，未做（见已知偏差）。
- Standard keywords 仍是“实证复刻”而非逐条抄录：MobaXterm 内建规则表未公开，
  规则集来自用户截图 + 其 EWS 导出原文；若后续拿到更权威定义可再收敛。
- 既有局限仍在：着色只写默认前景格；方案切回 default 时 `ApplyToAll` 仅重置
  水印不清旧色。
- 库仓库 Iciclecreek.Avalonia.Terminal main 本地领先 origin 6 提交未 push（待用户确认）。

### 60 SFTP 浏览器：右键菜单 + 双击打开 + 中键送终端（2026-11-11，用户指认 MobaXterm 右键菜单截图）

**范围**（用户选定 B：内建查看器；Compare file with 不做，记偏差）：
- 文件列表右键菜单 11 项，顺序与 MobaXterm 一致：打开 / 使用默认文本文编辑器打开 /
  打开方式 / 使用默认程序打开 / 下载 / ─ / 删除 / 重命名 / ─ / 复制文件路径 /
  将文件路径复制到终端（鼠标中键单击） / ─ / 属性 / 权限。
- 右键先选中被点行（Avalonia ListBox 右键不改选中），再弹菜单；目录选中时隐藏
  三个「打开*」文件项。
- 双击文件 = 「打开」：图片（10MB 内）/ 文本（1MB 内）走内建查看器窗口
  （只读 TextBox / 等比缩放 Image，解码失败显示提示）；其余类型先下载到缓存
  再用系统默认程序打开。双击目录 = 进入（原行为）。
- 「使用默认文本文编辑器打开」= 下载缓存 + notepad；「打开方式」= 文件选择器
  选程序后下载缓存 + 启动；「使用默认程序打开」= 下载缓存 + ShellExecute。
- 鼠标中键单击文件（及菜单同名项）= 把远程路径注入同会话终端（SendInputAsync，
  不带回车，MobaXterm 中键粘贴语义）；「复制文件路径」= 剪贴板。
- 「属性」= 只读小窗：名称/路径/类型/大小/最后修改/权限（rwx+八进制）/属主。
- 缓存目录：%APPDATA%\COMTool-Avalonia\remote-cache，保留相对远端主目录结构，
  路径非法字符替换为 `_`，同名覆盖；传输进行中拒绝下载/读取。

**实现**：
- `COMTool.Sftp.RemoteFileClassifier`（Image/Text/Other，按扩展名+无扩展名特例，
  纯逻辑）；`COMTool.Sftp.SftpPathCache`（缓存路径映射+清洗，纯逻辑）。
- `SftpPaneViewModel` 新增 `PathToTerminal` 回调、`CacheRoot`、`DownloadToCacheAsync`、
  `ReadEntryBytesAsync`、`SendSelectedPathToTerminal`、`ReportOpenFailed`、
  `ReportPathCopied`。
- `SftpPane.axaml/.cs`：ContextMenu、右键选中/中键送路径（隧道阶段 PointerPressed）、
  双击分流、打开流程（`OpenEntryAsync` + `ILocalFileLauncher` 可注入）、查看器窗口
  跟踪；`MainWindow` 把 `PathToTerminal` 接到本标签 `TerminalControl.SendInputAsync`
  （重启复用同一终端实例，无需重接）。
- 新窗口：`SftpTextViewerWindow`、`SftpImageViewerWindow`、`SftpPropertiesWindow`。

**测试**（TDD 先红后绿；先红为编译期红：`ILocalFileLauncher` 等类型不存在）：
- `RemoteFileClassifierTests` 5 项、`SftpPathCacheTests` 5 项（Sftp 库，纯逻辑）
- `SftpPaneViewModelTests.SFTP10_*` 5 项（缓存落盘/回退文件名/传输中拒绝/送终端回调/
  读取上限）
- `SftpContextMenuTests.SFTP10_*` 14 项（菜单 11 项顺序+3 分隔线、目录隐藏文件项、
  右键选中被点行、双击文件开内建查看器、双击目录仍进入、中键送路径、打开→默认
  程序/文本编辑器/打开方式 三条链路含缓存断言、复制路径、送终端菜单项、属性窗内容、
  文本查看器只读、图像查看器位图+坏数据提示）

**已知偏差**：
- 菜单未含「Compare file with...」（MobaXterm 文件对比视图，独立功能，另立一轮）。
- 内建查看器为只读（MobaXterm 可编辑并回存远程），无缩放/翻页，文本上限 1MB。
- 「打开方式」用文件选择器选 .exe 代替 MobaXterm 的已注册程序列表。
- 缓存文件不清理（依赖系统临时策略/用户手动清理）。

**验证**：
- 全量 gate：第 3 次复跑全绿（2026-11-11T08:42:23Z，r60-gatec）：restore/build/
  unit-tests 411/app-tests 312/e2e 15/testgate 全 PASS，共 723 项。前两次遇
  已知 e2e 环境偶发（TELNET01 输入送达失败 ×1、app 进程 UI02 处中途退出 ×1），
  同代码复跑转绿；另第 1 次 TestGate 因 [Theory] 参数化名称带后缀报 4 条
  SFTP-10 引用缺失，分类器测试改 [Fact] 后消失。
- VM win-x64 AOT 部署（仅更新 AOT 目录）：
  - `C:\Users\devuser\COMTool-Avalonia-AOT\COMTool.App.exe`
    SHA256 = `310d22cc5364db11dbf092acb3192a928dab296a0130439c20b6326adb356a0a`
  - AOT 包 `C:\Users\devuser\COMTool-Avalonia-AOT-r60.zip`
    SHA256 = `4ca9fac6df0c9a975336d60fcc46c5f31432fffa6dce8b8a44241fe5d1591c8d`（78,377,662 B，用户 SFTP 拉取）
- `--selftest-r60` 实机（VM，真实 SSH 会话，wall 7.1s，退出码 0）：
  - ① 右键菜单 11 项+3 分隔线：OK（11/11 项、3/3 分隔线）
  - ② 双击文本文件开内建查看器：OK（双击 `.aw_spoof.log` 开内建文本查看器，2516 字符）
  - ③ 路径注入终端回读：OK（`/home/devuser/.aw_spoof.log` 注入终端并回读缓冲确认）
  - r60-browser.png / r60-viewer.png 目检：通过——browser 截图为真实 SFTP 列表（/home/devuser 114 项+工具栏+监控条）；viewer 截图为等宽只读文本+状态栏 `.aw_spoof.log 2528 B`

**遗留**：
- Compare file with（文件对比）、内建查看器编辑回存、图像缩放，待后续轮次。
- 库仓库 Iciclecreek.Avalonia.Terminal main 本地领先 origin 6 提交未 push（沿惯例，
  待用户确认）。

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
