# COMTool Avalonia 功能测试矩阵

本表以 `docs/parity-matrix.md` 的功能 ID 为主键，逐行登记自动化测试、门禁阶段和人工验收证据。
状态与 `docs/parity-matrix.md` 保持一致（`完成（Avalonia 增强）` 归一化为 `完成`）；未补齐的自动化测试显式写 `-`，`完成` 行必须有自动化测试，`部分` 行才可使用人工验收。
门禁用法见 `docs/superpowers/specs/2026-10-20-comtool-avalonia-test-gate-design.md` 5.1/5.2 节。

| Feature | Status | Automated | Phase | Platform | Command | Manual | Evidence |
|---|---|---|---|---|---|---|---|
| UI-01 | 部分 | APP01_MainWindowShows,TERM15_DoubleClickUsesMaximizePath | verify | all | dotnet test | WIN-01 | 窗口和终端双击最大化路径已有自动化；标题栏无原生白条、最小化/最大化/还原/关闭按钮、拖拽与 5px 边缘缩放需 Windows 真实桌面确认（WIN-01）；视觉差异不能仅靠截图判定 |
| UI-02 | 完成 | UI02_SidebarExpandsOnLaunch | verify | all | dotnet test | - | 侧栏启动自动展开、宽度恢复和空状态由 headless 测试覆盖；Linux Xvfb 还执行 UI02_SidebarExpandsOnLaunchE2E |
| UI-03 | 部分 | - | manual | all | manual | WIN-20 | 串口/SSH 分组、连接状态圆点和原地状态更新需真实桌面视觉核对，无法自动化 |
| UI-04 | 部分 | - | manual | all | manual | WIN-20 | 侧栏 Open/Remove session 菜单、确认框和配置持久化需真实桌面交互，无法自动化 |
| UI-05 | 完成 | UI05_TabSwitchReparentOrder,UI05_TabSwitchFocusesIncomingTerminal,UI05_TabSwitchKeepsBothTerminalsLive,UI05_RapidSwitchFocus,UI05_CloseSelectedTabSelectsNeighbor,UI05_ClosingLastTabClearsSelection | verify | all | dotnet test | - | 多标签新增/关闭/切换、双向切换后立即可输入由 headless 测试覆盖；Linux Xvfb 执行 UI05_TabSwitchTypingBothDirections |
| UI-06 | 部分 | - | manual | all | manual | WIN-20 | 插件选择器列表顺序、terminal 可用和未实现提示需真实桌面点击核对，无法自动化 |
| UI-07 | 部分 | - | manual | all | manual | WIN-20 | 无标签空状态文案和 + 入口需真实桌面视觉核对，无法自动化 |
| UI-08 | 完成 | UI08_SettingsReplacesEditedTabOnly,UI08_DetachedEditedTabIsNotReplaced,UI08_WrongKindIsNotReplaced,UI08_SessionIdComparisonIsCaseInsensitive | verify | all | dotnet test | - | 连接设置替换/复用标签策略由 SettingsTabPolicyTests 覆盖 |
| UI-09 | 部分 | ConfigTests.SaveAndLoad_RoundTripsSessionSidebarAndTheme | verify | all | dotnet test | WIN-20 | skin 持久化和配置往返已自动化；标题栏右键浅/深色切换和重启生效需真实桌面视觉核对 |
| UI-10 | 未开始 | - | manual | all | manual | - | 未开始 |
| UI-11 | 部分 | - | manual | all | manual | WIN-10 | Windows 真实桌面 About/帮助入口与插件帮助、更新提示的视觉和交互核对；插件帮助和更新检测尚未实现，不能自动化 |
| UI-12 | 部分 | - | manual | all | manual | WIN-20 | 状态栏连接/断线/ZMODEM 进度/取消/成功/错误颜色和超时提示属于视觉与交互判断，无法自动化 |
| UI-13 | 部分 | UI05_RapidSwitchFocus,TERM15_DoubleClickUsesMaximizePath | verify | all | dotnet test | WIN-20 | 终端焦点与双击最大化自动化覆盖；Ctrl+V、方向键/F1-F12、F11、右键复制粘贴等快捷键需真实桌面逐项核对 |
| UI-14 | 部分 | - | manual | all | manual | WIN-02 | Windows 150%/200% DPI 需要真实桌面和显示器，裁切属于视觉判断，无法自动化 |
| UI-15 | 完成 | ConfigTests.SaveAndLoad_RoundTripsWindowSize | verify | all | dotnet test | - | 窗口正常尺寸保存/恢复由 ConfigTests 覆盖 |
| TERM-01 | 完成 | TerminalEmulatorTests.HandlesWrappingTabsAndEraseSequences,TerminalEmulatorTests.MatchesPythonPyteGoldenScreen | verify | all | dotnet test | - | pyte golden 与换行/Tab/擦除序列测试覆盖 |
| TERM-02 | 完成 | TerminalEmulatorTests.UsesXterm256Palette,TerminalEmulatorTests.ParsesAnsiColorAndWideCharacters | verify | all | dotnet test | - | ANSI 256 色、宽字符和调色板测试覆盖 |
| TERM-03 | 完成 | TerminalEmulatorTests.ResizeUpdatesDimensionsAndVisibleRows | verify | all | dotnet test | - | 终端 resize 尺寸与可见行测试覆盖 |
| TERM-04 | 完成 | TerminalEmulatorTests.HandlesScrollingAndScrollback | verify | all | dotnet test | - | 滚动和 scrollback 测试覆盖 |
| TERM-05 | 完成 | TerminalEmulatorTests.SelectionReturnsSelectedText | verify | all | dotnet test | - | 选择文本返回测试覆盖 |
| TERM-06 | 完成 | TerminalEmulatorTests.TracksBracketedPasteMode | verify | all | dotnet test | - | bracketed paste 模式跟踪测试覆盖 |
| TERM-07 | 完成 | MobaKeywordHighlighterTests.ErrorKeywordsAreBrightRed,MobaKeywordHighlighterTests.SuccessKeywordsAreBrightGreen,MobaKeywordHighlighterTests.WarningKeywordsAreBrightYellow,MobaKeywordHighlighterTests.UrlsAreUnderlined | verify | all | dotnet test | - | Moba 关键字高亮颜色、URL 和 ANSI 优先级测试覆盖 |
| TERM-08 | 完成 | TerminalEmulatorTests.TracksCursorVisibilityAndWideCharacterAdvance | verify | all | dotnet test | - | 光标可见性和宽字符步进测试覆盖 |
| TERM-09 | 部分 | - | manual | all | manual | WIN-20 | 平台等宽字体和 15px 字号需真实桌面视觉核对，无法自动化 |
| TERM-10 | 部分 | UI05_TabSwitchKeepsBothTerminalsLive | verify | all | dotnet test | WIN-20 | 文本输入和切换后立即输入已覆盖；方向键/Home/End/Insert/Delete/F1-F12/Ctrl+V 需真实桌面 cat -v 核对 |
| TERM-11 | 完成 | SshIntegrationTests.KeyAuthShellOutputResizeAndReconnect | verify | linux | dotnet test | WIN-17 | SSH 断线/重连与 shell 输出由 Linux 集成测试覆盖；Windows 实机重连见 WIN-17 |
| TERM-12 | 完成 | ZmodemTransferTests.StreamDetectsAndReceivesZmodemSession,ZmodemTransferTests.StreamDetectsAndSendsZmodemSession | verify | all | dotnet test | - | ZMODEM 流双向自动检测测试覆盖 |
| TERM-13 | 完成 | TerminalEmulatorTests.OneMegabyteAtLineRateStaysResponsive,TerminalEmulatorTests.TenMegabyteBurstStaysResponsive | verify | all | dotnet test | - | 1MB/10MB 吞吐与响应性测试覆盖 |
| TERM-14 | 完成 | MobaKeywordHighlighterTests.CompleteKeywordDoesNotWaitForAnotherChunk,MobaKeywordHighlighterTests.PendingSingleCharacterCanBeFlushed,ZmodemTransferTests.IncompleteKeywordPrefixIsFlushedWithoutMoreOutput | verify | all | dotnet test | - | 跨 chunk 关键字和待处理前缀刷新测试覆盖 |
| TERM-15 | 完成 | TERM15_DoubleClickUsesMaximizePath,TERM15_DoubleClickDoesNotStealSelection | verify | all | dotnet test | - | 终端双击最大化/还原和选择保持由 headless 测试覆盖；Linux Xvfb 执行 TERM15_DoubleClickMaximizeE2E |
| SER-01 | 完成 | SerialCatalogTests.DisplayLabelStripsDuplicateWindowsDevice,SerialPortCatalogTests.DisplayTextRemovesDuplicateDeviceSuffix,SerialPortCatalogTests.NormalizeSortsNaturallyAndDeduplicates | verify | all | dotnet test | - | 串口枚举、去重、稳定 key 和显示名测试覆盖 |
| SER-02 | 完成 | SerialCatalogTests.NormalizeSortsCom2BeforeCom10,SerialCatalogTests.NormalizeSortsLinuxPortsNaturally,SerialCatalogTests.SessionStoreUsesNaturalPortOrder | verify | all | dotnet test | - | COM 和 Linux 端口自然排序测试覆盖 |
| SER-03 | 部分 | - | manual | all | manual | WIN-03 | 串口热插拔依赖真实硬件设备，无法自动化 |
| SER-04 | 完成 | SerialCatalogTests.SettingsMapToSystemIoPortsEnums | verify | all | dotnet test | - | 设置值到 System.IO.Ports 枚举映射测试覆盖 |
| SER-05 | 部分 | SerialCatalogTests.SettingsMapToSystemIoPortsEnums | verify | all | dotnet test | WIN-11 | 枚举映射已自动化；真实 RTS/CTS、XON/XOFF、RTS/DTR 行为和 DSR/DTR 缺失提示依赖真实串口硬件和引脚接线，无法自动化 |
| SER-06 | 部分 | - | manual | all | manual | WIN-04 | 端口占用需要真实串口硬件，无法自动化 |
| SER-07 | 部分 | SerialPtyIntegrationTests.SerialConnectionReadsAndWritesThroughPty | verify | linux | dotnet test | WIN-05 | Linux PTY 读写已自动化；Windows 串口回环收发依赖真实硬件回路，无法自动化 |
| SER-08 | 不适用 | - | manual | all | manual | - | 不适用 |
| SER-09 | 部分 | SessionStoreTests.SerialStore_Upsert_NormalizesPortAndKeepsPosition | verify | all | dotnet test | WIN-12 | 保存和端口规范化已自动化；Windows 配置内串口会话保存、重启恢复及 UI 收发依赖真实串口硬件，无法自动化 |
| SER-10 | 部分 | SessionStoreTests.SerialStore_Prune_OnlyRemovesAfterSuccessfulScan | verify | all | dotnet test | WIN-13 | 扫描成功后清理逻辑已自动化；真实设备运行中拔除后的标签、侧栏清理和提示依赖真实串口硬件，无法自动化 |
| SSH-01 | 完成 | SessionStoreTests.SshStore_BuildsStableKeyAndKeepsPasswordWhenMetadataChanges,SessionStoreTests.SshStore_GetReturnsIndependentCopy | verify | all | dotnet test | - | SSH 会话稳定 key、元数据更新和独立副本测试覆盖 |
| SSH-02 | 部分 | - | manual | all | manual | WIN-17 | Windows 密码提示、3 次重试和设置页密码认证需真实 SSH 环境，无法自动化 |
| SSH-03 | 完成 | SshIntegrationTests.KeyAuthShellOutputResizeAndReconnect | verify | linux | dotnet test | WIN-17 | 密钥认证、shell 输出、resize 与重连由 Linux sshd 集成测试覆盖；Windows 实机见 WIN-17 |
| SSH-04 | 完成 | ConfigPhase0Tests.DpapiIsUnsupportedOnNonWindows,ConfigPhase0Tests.DpapiRejectsInvalidTokenOnWindows | verify | all | dotnet test | WIN-06 | DPAPI 非 Windows 不可用和非法 token 拒绝测试覆盖；Windows 记住密码并重连见 WIN-06 |
| SSH-05 | 完成 | ConfigPhase0Tests.SshPasswordCacheKeepsOnlyInMemoryAndCanBeCleared,SshIntegrationTests.KeyAuthShellOutputResizeAndReconnect | verify | all | dotnet test | - | 内存密码缓存、清理和重连测试覆盖 |
| SSH-06 | 完成 | SSH06_ActivationPolicy,SSH06_ConnectingTabIsNotTreatedAsLive,SSH06_LiveConnectionKeepsDeadTabsOpen,SSH06_LiveConnectionOpensAnotherTab,SSH06_NoLiveConnectionClosesDeadTabs,SSH06_OnlyMatchingSshTabsAreConsidered | verify | all | dotnet test | - | SSH 标签激活策略由 policy 测试覆盖；Linux Xvfb 执行 SSH06_MultipleConnectionsE2E |
| SSH-07 | 不适用 | - | manual | all | manual | - | 不适用 |
| SSH-08 | 完成 | SshIntegrationTests.KeyAuthShellOutputResizeAndReconnect | verify | linux | dotnet test | WIN-17 | SSH resize 与 ptmx 行为由 Linux 集成测试覆盖；Windows 实机见 WIN-17 |
| SSH-09 | 完成 | SshIntegrationTests.KeyAuthShellOutputResizeAndReconnect | verify | linux | dotnet test | WIN-17 | SSH shell 输出、重连与命令执行由 Linux 集成测试覆盖；Linux Xvfb 执行 E2E01_OpenSshSession |
| SSH-10 | 部分 | - | manual | all | manual | WIN-17 | 首次/重连延迟需真实 Windows 主机和网络测量，无法自动化 |
| NET-01 | 完成 | TcpSessionTests.NET01_TcpClientLoopbackSendsAndReceives,TcpSessionTests.NET01_TcpServerRoutesClientsAndSupportsSelectionAndDisconnect,TcpSessionTests.NET01_TcpServerCloseEndsTerminalRead,SessionActivationPolicyTests.NET01_TcpClientDeadTabsAreCleanedUp | verify | all | dotnet test | - | TCP 客户端回环收发/断开、服务端多客户端路由/选中发送/断开/关闭，以及 TCP client 独立连接策略由自动化测试覆盖 |
| NET-02 | 未开始 | - | manual | all | manual | - | 未开始 |
| NET-03 | 完成 | TcpSessionCoreTests.NET03_TcpStoreUpsertsClientAndServerSessions,TcpSessionCoreTests.NET03_TcpStoreDeduplicatesAndRemovesByKey,TcpSessionCoreTests.NET03_TcpStoreRejectsInvalidModeData,TcpSessionAppTests.NET03_LoadSessionsAddsTcpSectionAndMarksConnected,TcpSessionAppTests.NET03_TcpConnectionSettingsPanelIsAvailable | verify | all | dotnet test | - | 目标解析（含省略端口、全角冒号、URL 前缀和非法输入）、端口校验、会话存储/去重/删除、侧栏 TCP 分组、连接状态和设置窗口 TCP 面板由自动化测试覆盖 |
| NET-04 | 未开始 | - | manual | all | manual | - | 未开始 |
| ZM-01 | 完成 | ZmodemDetectorTests.BinaryHeaderIsDetected,ZmodemDetectorTests.DetectsHeaderSplitAcrossChunks,ZmodemDetectorTests.InvalidCrcFallsBackToTerminal | verify | all | dotnet test | - | ZMODEM 头检测、跨 chunk 和 CRC 回退测试覆盖 |
| ZM-02 | 完成 | LrzszZmodemTests.RealSzFileIsReceived,ZmodemTransferTests.SenderAndReceiverTransferFile,ZmodemTransferTests.StreamDetectsAndReceivesZmodemSession | verify | all | dotnet test | WIN-07 | 原生对话框接收和真实 sz 接收已覆盖；Windows 对话框见 WIN-07 |
| ZM-03 | 完成 | LrzszZmodemTests.RealRzReceivesSentFile,ZmodemTransferTests.StreamDetectsAndSendsZmodemSession | verify | all | dotnet test | WIN-07 | 原生对话框发送和真实 rz 接收已覆盖；Windows 对话框见 WIN-07 |
| ZM-04 | 部分 | - | manual | all | manual | WIN-18 | 文件传输进度、实时速率和剩余时间属于状态栏视觉判断，最终逐页复核见 WIN-18 |
| ZM-05 | 完成 | ZmodemTransferTests.CancelPendingSendRaisesCancelled,ZmodemTransferTests.CancelActiveReceiveRaisesCancelled,ZmodemTransferTests.TransportEofCompletesInputStream | verify | all | dotnet test | - | 发送/接收取消、EOF 和取消后恢复测试覆盖 |
| ZM-06 | 完成 | LrzszZmodemTests.RealSzFileIsReceived,LrzszZmodemTests.RealRzReceivesSentFile,ZmodemDetectorTests.DetectsRealLrzszSzZRQInit | verify | all | dotnet test | WIN-07 | 真实 lrzsz 双向 64KB 文件和 ZRQ 检测测试覆盖；Windows 对话框见 WIN-07 |
| ZM-07 | 部分 | - | manual | all | manual | WIN-14 | 长时间重复传输的内存曲线需要真实桌面长时间运行观察，无法自动化 |
| PLG-01 | 未开始 | - | manual | all | manual | - | 未开始 |
| PLG-02 | 完成 | TerminalEmulatorTests.MatchesPythonPyteGoldenScreen,TerminalEmulatorTests.HandlesScrollingAndScrollback,ZmodemTransferTests.StreamDetectsAndReceivesZmodemSession | verify | all | dotnet test | - | terminal 插件核心终端模拟和 ZMODEM 流测试覆盖 |
| PLG-03 | 未开始 | - | manual | all | manual | - | 未开始 |
| PLG-04 | 未开始 | - | manual | all | manual | - | 未开始 |
| PLG-05 | 未开始 | - | manual | all | manual | - | 未开始 |
| PLG-06 | 未开始 | - | manual | all | manual | - | 未开始 |
| PLG-07 | 未开始 | - | manual | all | manual | - | 未开始 |
| CFG-01 | 完成 | ConfigTests.SaveAndLoad_RoundTripsPythonConfig,ConfigTests.UnknownFields_ArePreservedOnSave,ConfigTests.AvaloniaConfigDirectory_IsSeparateFromPython | verify | all | dotnet test | - | 读取保留未知字段、独立 Avalonia 目录和 Python 兼容往返测试覆盖 |
| CFG-02 | 完成 | SessionStoreTests.SerialStore_DeduplicatesByNormalizedPort_LastWins,SessionStoreTests.SerialStore_Upsert_NormalizesPortAndKeepsPosition | verify | all | dotnet test | - | 串口会话插入/更新、去重和顺序测试覆盖 |
| CFG-03 | 完成 | SessionStoreTests.SshStore_BuildsStableKeyAndKeepsPasswordWhenMetadataChanges,SessionStoreTests.SshStore_EmptyPasswordArgument_ForgetsPassword,SessionStoreTests.SshStore_InvalidPort_FallsBackTo22 | verify | all | dotnet test | - | SSH 会话 key、密码清除和非法端口测试覆盖 |
| CFG-04 | 完成 | ConfigPhase0Tests.ImportCopiesPythonConfigWithoutChangingSource,ConfigPhase0Tests.ImportDoesNotOverwriteExistingBetaConfig,ConfigPhase0Tests.ImportOverwriteCreatesTimestampedBackup,ConfigPhase0Tests.MissingOrInvalidSourceLeavesTargetUntouched,ConfigPhase0Tests.SameSourceAndTargetIsRejected,ConfigPhase0Tests.UnsupportedVersionIsNotImported | verify | all | dotnet test | - | Python 配置导入、不覆盖、备份和非法源测试覆盖 |
| CFG-05 | 完成 | APP01_MainWindowShows | verify | all | dotnet test | - | 启动时写入独立 Beta 日志路径由 APP01 覆盖；run.log 2MB 轮转实现见文档 |
| CFG-06 | 完成 | ConfigPhase0Tests.DpapiIsUnsupportedOnNonWindows,ConfigPhase0Tests.DpapiRejectsInvalidTokenOnWindows | verify | all | dotnet test | WIN-06 | DPAPI 保护和拒绝非法 token 测试覆盖；Windows 重连见 WIN-06 |
| CFG-07 | 未开始 | - | manual | all | manual | - | 未开始 |
| CFG-08 | 未开始 | - | manual | all | manual | - | 未开始 |
| PKG-01 | 完成 | PKG01_CandidateSmoke | package | all | bash avalonia/pack/linux/pack.sh / pack.ps1 | - | 候选包冒烟启动由 package 阶段 PKG01_CandidateSmoke 覆盖 |
| PKG-02 | 部分 | - | manual | all | manual | WIN-15 | Wayland 与 AppImage 需要真实桌面会话验证，无法自动化 |
| PKG-03 | 未开始 | - | manual | all | manual | - | 未开始 |
| PKG-04 | 部分 | - | manual | all | manual | WIN-08 | Windows/Linux 安装、升级、卸载和保留 Beta 配置需真实桌面安装环境，无法自动化 |
| PKG-05 | 部分 | - | manual | all | manual | WIN-21 | 包体积和启动时间需真实 Windows/Linux 桌面测量并与性能基线对照，无法自动化 |
| PKG-06 | 部分 | - | manual | all | manual | WIN-16 | CI 流水线需在托管 CI 实际运行并核对日志，本地门禁无法自动化 |
| TST-01 | 完成 | CI01_SerializesCandidateInfo | verify | all | dotnet test | - | 完整套件 190 项通过（Core 37/Connections 34/Terminal 11/ZMODEM 32/TestGate 49/App 27）；CI01 覆盖 TestGate 候选元数据 |
| TST-02 | 完成 | TerminalEmulatorTests.MatchesPythonPyteGoldenScreen | verify | all | dotnet test | - | 同一字节流与 Python pyte 0.8.2 的 60x20 屏幕快照一致 |
| TST-03 | 完成 | ZmodemTransferTests.SenderAndReceiverTransferFile,LrzszZmodemTests.RealSzFileIsReceived,LrzszZmodemTests.RealRzReceivesSentFile | verify | all | dotnet test | - | 模拟双向和真实 lrzsz 双向 64KB 二进制文件集成测试覆盖 |
| TST-04 | 部分 | SshIntegrationTests.KeyAuthShellOutputResizeAndReconnect,SerialPtyIntegrationTests.SerialConnectionReadsAndWritesThroughPty | verify | linux | dotnet test | WIN-17 | Linux sshd/PTY 集成已自动化；Windows 实机 SSH/串口集成依赖真实硬件，无法自动化 |
| TST-05 | 部分 | - | manual | all | manual | WIN-18 | 最终逐页像素复核属于视觉判断，无法自动化 |
| TST-06 | 部分 | - | manual | all | manual | WIN-09 | 8 小时稳定性需要真实桌面/硬件持续运行和资源曲线，无法自动化 |
| TST-07 | 部分 | - | manual | all | manual | WIN-19 | 默认版本切换与回退需要真实桌面安装环境演练，无法自动化 |
