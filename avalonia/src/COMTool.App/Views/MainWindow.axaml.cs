using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.Services;
using COMTool.App.Tools;
using COMTool.App.ViewModels;
using COMTool.Connections;
using COMTool.Connections.NetworkScan;
using COMTool.Core.Config;
using COMTool.Core.Macros;
using COMTool.Sftp;
using COMTool.Terminal;
using COMTool.Terminal.Highlight;
using COMTool.Zmodem;
using Iciclecreek.Terminal;

namespace COMTool.App.Views;

public partial class MainWindow : Window
{
    private static readonly Cursor WestEastCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor NorthSouthCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor NorthWestSouthEastCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor NorthEastSouthWestCursor = new(StandardCursorType.TopRightCorner);
    private const double DefaultBrowserSidebarWidth = 460;

    private bool _sidebarVisible = true;
    private double _sidebarWidth = 260;

    // The SSH browser needs the room of MobaXterm's panel. The width of
    // the sessions panel is kept apart, so closing the browser brings the
    // sessions list back at its own size.
    private double _browserSidebarWidth = DefaultBrowserSidebarWidth;
    private bool _loaded;
    private AppConfigService? _config;
    private DispatcherTimer? _portTimer;
    private bool _windowPointerPressed;
    // 录制去重：KeyDown 路径刚喂过的字符+时刻（防 Win32 WM_CHAR 回声把同一字符双录）
    private char _lastKeyFeedChar = '\0';
    private int _lastKeyFeedTick;
    private ConnectionSettingsWindow? _settingsWindow;
    private readonly Dictionary<TerminalTab, ZmodemTiming> _zmodemTimings = new();
    private readonly Dictionary<TerminalTab, ZmodemStream> _zmodemStreams = new();
    // r39：per-终端语法高亮着色器（LineFed 驱动，方案来自 config，见 AttachTerminalHighlighter）
    private readonly Dictionary<TerminalControl, TerminalHighlighter> _highlighters = new();
    // r40：进入全屏前的窗口态（退出全屏时恢复；MobaXterm 行为，用户实机反馈：
    // 最大化窗口 F11 进全屏、再 F11 应回到最大化，而不是 Normal）
    private WindowState _preFullScreenState = WindowState.Normal;
    private readonly Dictionary<TerminalTab, DetachedTabState> _detachedTabs = new();
    private readonly Dictionary<TerminalTab, SerialTabState> _serialTabs = new();
    // r67：标签拖动换位状态（按指针 id 区分，防止多指针互相干扰）。
    private const double TabDragThreshold = 5.0;
    private Point _tabDragStart;
    private int _tabDragPointerId = -1;
    private IPointer? _tabDragPointer;
    private TerminalTab? _tabDragCandidate;
    private TerminalTab? _tabDragTab;
    // r67.1：MobaXterm 式换位动画——拖动中被拖标签直接跟手（不走计时器），
    // 其余标签 120ms ease-out 滑入新槽位；计时器只在有活跃动画时运行。
    private const int TabSlideDurationMs = 120;
    private readonly Dictionary<TerminalTab, TabSlide> _tabSlides = new();
    private DispatcherTimer? _tabSlideTimer;

    private sealed class TabSlide
    {
        public required double FromX { get; init; }
        public required DateTime Start { get; init; }
    }
    private readonly HashSet<TerminalTab> _sshRestarts = new();
    private readonly HashSet<TerminalTab> _telnetRestarts = new();

    internal Func<SerialConnectionConfig, ISerialConnection> SerialConnectionFactory { get; set; } =
        config => SerialConnection.Open(config);

    internal Func<SessionItem, Task>? SessionActivationOverrideForTests { get; set; }

    /// <summary>Config service used by tool windows (null before load/demo mode).</summary>
    internal AppConfigService? ConfigService => _config;
    private static readonly IBrush StatusInfoBrush =
        new SolidColorBrush(Color.Parse("#008200"));
    private static readonly IBrush StatusWarningBrush =
        new SolidColorBrush(Color.Parse("#e65100"));
    private static readonly IBrush StatusErrorBrush =
        new SolidColorBrush(Color.Parse("#c62828"));
    private static readonly IBrush StatusProgressBrush =
        new SolidColorBrush(Color.Parse("#1565c0"));
    private const double DefaultWindowWidth = 850;
    private const double DefaultWindowHeight = 500;
    private const double MinimumWindowWidth = 500;
    private const double MinimumWindowHeight = 320;
    private double _lastNormalWidth = DefaultWindowWidth;
    private double _lastNormalHeight = DefaultWindowHeight;
    // 全局终端字号（Ctrl+滚轮 缩放后写入 config 的 terminal.fontSize）。
    private double _terminalFontSize = TerminalFontSize;

    // 终端行尾仿真开关（对齐 MobaXterm「实时终端重配置 → 终端」的 Implicit CR/LF 两项）。
    // 所有终端的 LineEndingStream 共享此对象：设置对话框里改动立即对所有已连接终端生效。
    private readonly LineEndingOptions _lineEndingOptions = new();

    // 终端查找（TERM-23）：_searchTerminal 为当前查找条绑定的终端；_searchHasQuery
    // 标记是否已有生效的查找词（决定 Enter 走 FindInBuffer 还是 FindNext）。
    private TerminalControl? _searchTerminal;
    private bool _searchHasQuery;

    // 终端输出录制（TERM-25）：订阅所选终端的 OutputReceived，把原始字节追加写文件。
    // OutputReceived 可能在读线程触发，写入前加 _recordingGate 锁。
    private TerminalControl? _recordingTerminal;
    private Stream? _recordingStream;
    private long _recordingBytes;
    private string _recordingPath = "";
    private readonly object _recordingGate = new();
    private EventHandler<Iciclecreek.Terminal.OutputReceivedEventArgs>? _recordingHandler;

    public MainWindow()
    {
        InitializeComponent();
        // ContextMenu events cannot be wired in XAML (Avalonia 12); the
        // remote-monitoring menu syncs its check states when it opens.
        MonitorBarMenu.Opening += OnMonitorMenuOpening;
        WarmUpTerminalFonts();

        // Every platform stays frameless (WindowDecorations=None in XAML) so
        // DWM never draws a window border and the X11 E2E harness keeps its
        // window-relative coordinates. On top of that, Windows marks the
        // window resizable by adding WS_THICKFRAME itself: Aero Snap and
        // Win+Arrow only work for windows with that style, and because the
        // client area covers the whole window (Avalonia's None mode answers
        // WM_NCCALCSIZE with the full rect) the style bit adds no visible
        // frame.
        EnsureWindowsSnapFrame();

        UpdateMaximizeIcon();
        SizeChanged += OnWindowSizeChanged;
        ApplySavedWindowSizeBeforeShow();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnRecordingTextInput, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnWindowPointerWheelChanged, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(
            PointerCaptureLostEvent,
            OnWindowPointerCaptureLost,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Sidebar.SessionActivated += OnSessionActivated;
        Sidebar.EditRequested += OnSessionEditRequested;
        Sidebar.RemoveRequested += OnSessionRemoveRequested;
        Sidebar.NewSessionRequested += OnSidebarNewSessionRequested;
        Sidebar.MacroRecordToggleRequested += OnMacroRecordToggleRequested;
        Sidebar.MacroExecuteRequested += OnMacroExecuteRequested;
        Sidebar.MacroRenameRequested += OnMacroRenameRequested;
        Sidebar.MacroEditRequested += OnMacroEditRequested;
        Sidebar.MacroDuplicateRequested += OnMacroDuplicateRequested;
        Sidebar.MacroDeleteRequested += OnMacroDeleteRequested;
        Sidebar.MacroHotkeyRequested += OnMacroHotkeyRequested;
        Sidebar.MacroImportRequested += OnMacroImportRequested;
        Sidebar.MacroExportRequested += OnMacroExportRequested;
        Sidebar.RailTabRequested += OnRailTabRequested;
        Sidebar.SshBrowserRequested += OnSshBrowserRequested;
        Sidebar.ToolRequested += OnToolRequested;
        Opened += OnOpened;
        Activated += OnWindowActivated;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            UpdateMaximizeIcon();
            EnsureWindowsSnapFrame();
            // 最大化/还原立即落盘，崩溃或强杀也不丢本次窗口状态。
            PersistWindowSize();
        }
    }

    private void OnWindowActivated(object? sender, EventArgs e) =>
        EnsureWindowsSnapFrame();

    /// <summary>
    /// Aero Snap (drag to a screen edge, Win+Arrow) only works for windows
    /// that carry the WS_THICKFRAME style. The window itself stays frameless
    /// (WindowDecorations.None answers WM_NCCALCSIZE with the full rect, so
    /// DWM has no non-client area to draw a frame into and no visible border
    /// appears - verified against the Win10 DWM frame the old BorderOnly
    /// decoration left behind). The bit is added after the handle exists and
    /// re-applied on activation/state changes because Avalonia rewrites the
    /// style from its own decoration model.
    /// </summary>
    private void EnsureWindowsSnapFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle.Handle, GwlStyle);
        if ((style.ToInt64() & WsThickFrame) != 0)
        {
            return;
        }

        SetWindowLongPtr(handle.Handle, GwlStyle, new IntPtr(style.ToInt64() | WsThickFrame));
        SetWindowPos(
            handle.Handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
    }

    private const int GwlStyle = -16;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private void OnOpened(object? sender, EventArgs e)
    {
        EnsureWindowsSnapFrame();

        if (_loaded || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _loaded = true;
        viewModel.SelectedTabChanging += OnSelectedTabChanging;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (Environment.GetCommandLineArgs().Contains("--demo"))
        {
            viewModel.LoadDemoSessions();
        }
        else
        {
            try
            {
                _config = AppConfigService.Load();
                AppLog.Info($"Config loaded: {_config.ConfigPath}");
                StartupClock.Mark("config-loaded（含首次串口扫描）");
                viewModel.LoadMonitorOptions(_config.Config.RemoteMonitor);
                // "COMTool 启动时启动 Cron 服务器" 勾选时随主窗口自启
                _ = CronAutoStart.EnsureAsync(_config.Config.Servers.Cron);
                ThemeManager.Apply(_config.Config.Skin);
                ApplySidebarConfig();
                ApplyWindowSizeConfig();
                ApplyTerminalConfig();
                RefreshSidebar();
                LoadSidebarData();
                _portTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _portTimer.Tick += (_, _) => RefreshPorts();
                _portTimer.Start();
                StartupClock.Mark("loaded（侧栏/窗口配置就绪）");
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to load config", ex);
                _ = ShowErrorAsync($"配置加载失败：{ex.Message}");
            }
        }

        if (Environment.GetCommandLineArgs().Contains("--terminal"))
        {
            AddLocalTerminal();
        }
        // --selftest-fontdlg：自动打开字体对话框并打印真实几何后退出（免 RDP 实机复验，
        // 见 RunFontDialogSelfTestAsync）。
        if (Environment.GetCommandLineArgs().Contains("--selftest-fontdlg"))
        {
            _ = RunFontDialogSelfTestAsync();
        }
        // --selftest-r38：实机自检 r38 三项（RTF 剪贴板/事件日志/打印），
        // 结果写 run.log 后退出，见 RunR38SelfTestAsync。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r38"))
        {
            _ = RunR38SelfTestAsync();
        }
        // --selftest-r39：实机自检 r39 三子菜单（终端显示/语法高亮/特殊命令），
        // 结果写 run.log 后退出，见 RunR39SelfTestAsync。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r39"))
        {
            _ = RunR39SelfTestAsync();
        }
        // --selftest-r44：实机自检 r44 标签类型图标（串口/SSH 区别 + 选中态白图标），
        // 开一对假 Serial+SSH tab 后落盘 main-content.png（含标签条），见 RunR44SelfTestAsync。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r44"))
        {
            _ = RunR44SelfTestAsync();
        }
        // --selftest-r45：实机自检 r45 鼠标选中自动复制（MobaXterm 对齐）。开一个本地终端，
        // 写入探测行后模拟鼠标拖选整行，读回系统剪贴板核对；选区保留在截图中。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r45"))
        {
            _ = RunR45SelfTestAsync();
        }
        // --selftest-r46：实机自检 r46 SSH 重启进度条清除。打开配置里的第一个 SSH 会话，
        // 连接成功后触发一次 R 重启，重启完成后断言 IsRestarting 已清、状态栏进度区隐藏。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r46"))
        {
            _ = RunR46SelfTestAsync();
        }
        // --selftest-r47：实机自检 r47 终端鼠标交互（双击整路径选中 + 悬停 IBeam 光标）。
        // 开一个本地终端写入探测路径行，模拟悬停与双击，核对选区内容与光标类型，
        // 落盘 main-content.png（含路径选区高亮）。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r47"))
        {
            _ = RunR47SelfTestAsync();
        }
        // --selftest-r48：实机自检 r48 标签图标选中态保持类型色（MobaXterm 对齐）。
        // 开一对假 Serial+SSH tab（SSH 选中），断言选中的 SSH 图标仍是金色、
        // 未选中串口仍是绿色（r44 曾「选中变白」，用户反馈后 r48 撤销），
        // 落盘 main-content.png（含标签条）供截图核对。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r48"))
        {
            _ = RunR48SelfTestAsync();
        }
        // --selftest-r49：实机自检 r49 会话设置对话框布局（Key file 浏览按钮垂直居中 +
        // TCP「Auto reconnect」标签不再顶到复选框）。打开真实对话框，切 SSH/TCP 面板，
        // 断言按钮与输入框垂直中心差 ≤1px、四面板标签列宽 ≥100，落盘 r49-ssh-dialog.png /
        // r49-tcp-dialog.png 供像素级核对。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r49"))
        {
            _ = RunR49SelfTestAsync();
        }
        // --selftest-r50：实机自检 r50 对话框 OPEN/Close 按钮文字垂直居中
        // （用户反馈「open、x close 好像也没居中，有点中间偏上」）。
        // 校验 5 个动作按钮 VerticalContentAlignment=Center 且高度非写死，
        // 落盘 r50-dialog.png（整窗截图，默认 Serial 面板，OPEN 按钮可见）供像素核对。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r50"))
        {
            _ = RunR50SelfTestAsync();
        }
        // --selftest-r51：启动性能自检——打印各启动阶段耗时（[Startup] 系列日志）、
        // 单独串口扫描耗时与进程内总耗时，供打包策略 A/B 对比（自检脚本另记外层 wall time，
        // 二者之差 = 单文件自提取 + CLR 初始化）。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r51"))
        {
            _ = RunR51SelfTestAsync();
        }
        // --selftest-r53：实机自检 CJK/Latin 混排基线（用户反馈「英文比中文高了一点」）。
        // 开本地终端写入探测混排行，读每条 run 的真实绘制基线（GlyphRun 快路径 vs
        // FormattedText 路径）、回退链各字体面的墨迹度量，落盘 r53-terminal.png 供对比。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r53"))
        {
            _ = RunR53SelfTestAsync();
        }
        // --selftest-r54：实机自检 r54 三项（连字符路径双击整选 / SSH 连接成功自动
        // 打开 SFTP 浏览器 / 浅色主题 MobaXterm 1:1 取色），落盘 r54-theme.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r54"))
        {
            _ = RunR54SelfTestAsync();
        }
        // --selftest-r55：实机自检 r55 三项（标题栏下沿 1px 分隔线 / 标题栏会话按钮已移除 /
        // rail 会话图标折叠与展开侧栏），落盘 r55-titlebar.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r55"))
        {
            _ = RunR55SelfTestAsync();
        }
        // --selftest-r56：实机自检 r56 两项（SGR 粗体对齐 MobaXterm：无粗体面/不提亮 /
        // EWS 方案 MAC 地址品红规则），落盘 r56-terminal.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r56"))
        {
            _ = RunR56SelfTestAsync();
        }
        // --selftest-r57：实机自检 r57 语法高亮实时着色回归（命令回显后连续两轮
        // ifconfig 输出都必须着色，验证不再“只生效一次”），落盘 r57-terminal.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r57"))
        {
            _ = RunR57SelfTestAsync();
        }
        // --selftest-r58：实机自检 r58 三项（用户同板 ifconfig 对照截图）：netmask
        // 不染品红（IPv4 首段 1..254）、unspec 连字符长链不染品红（MAC 边界排除 -）、
        // 品红取色对齐用户 ini #ff4dff，落盘 r58-terminal.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r58"))
        {
            _ = RunR58SelfTestAsync();
        }
        // --selftest-r59：实机自检 r59「标准关键字」方案：时间戳→黄、null/none→品红、
        // EWS 原有 error 红/IP 品红不回退，落盘 r59-terminal.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r59"))
        {
            _ = RunR59SelfTestAsync();
        }
        // --selftest-r60：实机自检 r60 SFTP 浏览器右键菜单与打开行为：
        // ① 右键菜单 11 项（MobaXterm 顺序）② 双击文本文件开内建查看器
        // ③ 路径送入终端（中键语义），落盘 r60-browser.png / r60-viewer.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r60"))
        {
            _ = RunR60SelfTestAsync();
        }
        // --selftest-r61：实机自检 r61 SFTP 浏览器列表修复：
        // ① 隐藏文件默认关闭（MobaXterm 对齐）② 打开开关后追加显示隐藏文件
        // ③ 符号链接排在目录组（不再沉底），落盘 r61-browser.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r61"))
        {
            _ = RunR61SelfTestAsync();
        }
        // --selftest-r62：实机自检 r62 SFTP 右键菜单与权限窗（对齐 MobaXterm 截图）：
        // ① 菜单 16 项+3 分隔线 ② 空白区/条目菜单变体 ③ 新建文件（创建后清理）
        // ④ 权限窗标题+rwx/八进制同步 ⑤ 符号链接目录组回归，落盘 r62-browser.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r62"))
        {
            _ = RunR62SelfTestAsync();
        }
        // --selftest-r63：实机自检 r63 权限窗版式（对齐 MobaXterm 图2）：
        // ① 居中粗体抬头 ② 标签/版式（冒号/右对齐/粗体/按钮居中/窗宽≥400）
        // ③ 应用→「正在修改权限...」→SFTP 同值写回→自动关窗 ④ 写回后状态栏
        // 落盘 r63-perm.png（权限窗）/ r63-browser.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r63"))
        {
            _ = RunR63SelfTestAsync();
        }
        // --selftest-r64：实机自检 r64 SFTP 列表空白区左键清除选中（MobaXterm 行为）：
        // ① 选中文件→模拟空白区左键→选中清除 ② 行上左键按下不清除选中（回归），
        // 落盘 r64-browser.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r64"))
        {
            _ = RunR64SelfTestAsync();
        }
        // --selftest-r65：实机自检 r65 SFTP 空白菜单启用态刷新（首连修复）：
        // SSH 会话连接、SFTP 列表加载完成后（无选中条目），空白区右键菜单
        // 5 项（上一级/新建文件夹/新建文件/刷新/上传）必须全部启用且可见，
        // 条目菜单项必须隐藏。落盘 r65-browser.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r65"))
        {
            _ = RunR65SelfTestAsync();
        }
        // --selftest-r66：实机自检 r66 远程监控开关（同 IP 双会话点击失效修复）：
        // ② 隧道按下翻转一次且按钮视觉同步 ③ 轮询数据 MonitorText 出现
        // ④ 按钮内部翻转路径不污染 VM（OneWay 绑定屏蔽）。落盘 r66-monitor.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r66"))
        {
            _ = RunR66SelfTestAsync();
        }
        // --selftest-r67：实机自检 r67 标签拖动换位（实时换位）。开三个假 tab
        // （本地终端/COM99 串口/devuser@192.0.2.48 SSH，即用户截图场景），在标签条上
        // 模拟「按下→3px（阈值内不换）→10px（开拖）→越过相邻中线（实时换位）→松开」，
        // 断言顺序 a,b,c→a,c,b→c,a,b、选中不变、dragging 样式随开拖/松开生效、
        // 普通点击不换顺序。落盘 r67-tabs.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r67"))
        {
            _ = RunR67SelfTestAsync();
        }
        // --selftest-r68：实机自检 r68 串口断线自动重连（EOF/设备消失路径）。
        // 用一个真实串口开标签 → 模拟断线（MarkSerialDisconnected，与读流 EOF 同一条
        // 上膛路径）→ 不等任何操作，只靠 2s 端口扫描自动重开 → 断言标签恢复 Connected。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r68"))
        {
            _ = RunR68SelfTestAsync();
        }
        // --selftest-r69：实机自检 r69 终端滚动条（MobaXterm 式）：开本地终端灌满回滚，
        // 断言滚动条常显 16px、滑块同步到底部、点一次翻一整屏；再在滑块上方按住左键
        // 1.2s，断言回滚位置每 250ms 连续上翻（≥3 次）且松手后立刻停止。
        // DispatcherTimer 的重复在 headless 不跑，必须实机验。落盘 r69-scrollbar.png。
        if (Environment.GetCommandLineArgs().Contains("--selftest-r69"))
        {
            _ = RunR69SelfTestAsync();
        }
    }

    private volatile bool _portScanBusy;

    // r51：串口扫描（GetPortNames + SetupAPI 友好名）原先每 2s 在 UI 线程同步执行，
    // 带 USB 串口适配器的机器上会周期性卡 UI（用户反馈「运行卡卡的」）。
    // 现移到工作线程，结果回 UI 线程应用；上一轮扫描未结束时跳过本次。
    internal void RefreshPorts()
    {
        if (_config is null || _portScanBusy)
        {
            return;
        }

        _portScanBusy = true;
        var config = _config;
        _ = Task.Run(() =>
        {
            var scan = PortScanOverrideForTests is { } f ? f() : SerialPortCatalog.Scan();
            Dispatcher.UIThread.Post(() =>
            {
                _portScanBusy = false;
                if (!ReferenceEquals(_config, config))
                {
                    return;
                }

                var previous = config.Ports
                    .Select(port => port.Device)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                config.RefreshPorts(scan);
                ApplyPortScan(previous, scan);
            });
        });
    }

    // r51：测试/自检注入点（慢扫描），生产恒为 null。
    internal Func<SerialPortScanResult>? PortScanOverrideForTests { get; set; }

    internal void ApplyPortScanForTests(SerialPortScanResult scan)
    {
        if (_config is null)
        {
            return;
        }

        var previous = _config.Ports
            .Select(port => port.Device)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _config.RefreshPorts(scan);
        ApplyPortScan(previous, scan);
    }

    private void ApplyPortScan(
        IReadOnlySet<string> previous,
        SerialPortScanResult scan)
    {
        if (!scan.Success)
        {
            // A failed scan means "do not prune remembered sessions", not
            // "stop watching live tabs". Connection state is independent of
            // port enumeration.
            UpdateConnectionStates();
            return;
        }

        var current = scan.Ports
            .Select(port => port.Device)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = previous
            .Except(current, StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var serialState in _serialTabs.Values.ToList())
        {
            if (!serialState.Closed && missing.Contains(serialState.Port))
            {
                MarkSerialDisconnected(serialState);
            }
        }

        // Also retry when the device disappeared and returned between two
        // scans, or when the transport failed while the port stayed present.
        var now = DateTime.UtcNow;
        foreach (var serialState in _serialTabs.Values.ToList())
        {
            if (serialState.Closed
                || !serialState.AwaitingReconnect
                || !current.Contains(serialState.Port)
                || now < serialState.NextReconnectAt)
            {
                continue;
            }

            TryReconnectSerial(serialState);
        }

        if (!previous.SetEquals(current))
        {
            RefreshSidebar();
        }
        else
        {
            UpdateConnectionStates();
        }
    }

    internal void UpdateConnectionStatesForTests() => UpdateConnectionStates();

    internal void OpenSerialSessionForTests(string port) => OpenSerialSession(port);

    internal Task ActivateSessionForTests(SessionItem item) => ActivateSessionAsync(item);

    internal Task OpenTelnetSessionForTests(string key) => OpenTelnetSessionAsync(key);

    /// <summary>
    /// Test seam: runs the same restart path the stopped terminal's R key
    /// uses, so a failed restart (progress bar plus ask dialog) can be
    /// exercised without a live SSH server.
    /// </summary>
    internal Task RestartSshSessionForTests(TerminalTab tab) => RestartSshSessionAsync(tab);

    /// <summary>
    /// Test seam: the last shown restart-ask dialog (null when closed).
    /// </summary>
    internal Window? LastRestartDialogForTests { get; private set; }

    internal void StopPortTimerForTests() => _portTimer?.Stop();

    internal void CloseTabForTests(TerminalTab tab) => CloseTab(tab);

    /// <summary>
    /// Test seam: the SSH password dialog while it is on screen (null once closed),
    /// so a headless test can cancel it and drive the no-connection restart exit.
    /// </summary>
    internal SshPasswordDialog? LastSshPasswordDialogForTests { get; private set; }

    private (HashSet<string> Serial, HashSet<string> Ssh, HashSet<string> Telnet, HashSet<string> Tcp)
        ConnectedSessions()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        var serial = AllTabs()
            .Where(tab => tab.Kind == SessionKind.Serial && IsTabConnected(tab))
            .Select(tab => tab.SessionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ssh = AllTabs()
            .Where(tab => tab.Kind == SessionKind.Ssh && IsTabConnected(tab))
            .Select(tab => tab.SessionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var telnet = AllTabs()
            .Where(tab => tab.Kind == SessionKind.Telnet && IsTabConnected(tab))
            .Select(tab => tab.SessionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tcp = AllTabs()
            .Where(tab => tab.Kind == SessionKind.Tcp && IsTabConnected(tab))
            .Select(tab => tab.SessionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (serial, ssh, telnet, tcp);
    }

    private void UpdateConnectionStates()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        foreach (var tab in AllTabs())
        {
            if (!tab.IsAlive || tab.IsTransportOpen is null)
            {
                continue;
            }

            var connected = false;
            try
            {
                connected = tab.IsTransportOpen();
            }
            catch
            {
                // A disposed SSH client or serial port is simply disconnected.
            }

            if (!connected)
            {
                // r67.6：串口读流结束（拔设备/驱动失效）时 ZmodemStream 只把
                // _completed 置 true，不会触发 PtyStreamConnection.ProcessExited
                // （那个事件只在主动 Close() 时发），所以重连状态机不会被上膛，
                // 标签就永久停在 Connection lose、再也不重试。这里把「传输已死」
                // 统一当作断线处理，交给 ApplyPortScan 的重连循环接管。
                if (tab.Kind == SessionKind.Serial
                    && _serialTabs.TryGetValue(tab, out var serialState)
                    && !serialState.Closed
                    && !serialState.AwaitingReconnect)
                {
                    MarkSerialDisconnected(serialState);
                }

                if (tab.StatusMessage != "Connection lose")
                {
                    tab.StatusMessage = "Connection lose";
                    tab.StatusBrush = StatusWarningBrush;
                    tab.TabDotBrush = StatusWarningBrush;
                }
            }
        }

        var (serial, ssh, telnet, tcp) = ConnectedSessions();
        viewModel.UpdateConnectionStates(serial, ssh, tcp, telnet);
    }

    private void RefreshSidebar()
    {
        if (_config is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var (serial, ssh, telnet, tcp) = ConnectedSessions();
        viewModel.LoadSessions(_config, serial, ssh, tcp, telnet);
    }

    private async void OnSessionActivated(object? sender, SessionItem item) =>
        await ActivateSessionAsync(item);

    internal async Task ActivateSessionAsync(SessionItem item)
    {
        if (DataContext is not MainViewModel viewModel
            || _config is null
            || item.IsConnecting)
        {
            return;
        }

        // SSH, Telnet and TCP clients are saved endpoints, not a single
        // connection. If one is already live, every click opens another
        // independent connection so the same endpoint can be used in parallel.
        var opensNewConnection = item.Kind is SessionKind.Ssh or SessionKind.Telnet
            || (item.Kind == SessionKind.Tcp && !IsTcpServerSession(item.SessionId));
        if (opensNewConnection)
        {
            var tabs = AllTabs().ToList();
            if (SessionActivationPolicy.ShouldOpenNewSsh(tabs, item.SessionId, IsTabConnected))
            {
                foreach (var dead in SessionActivationPolicy.DeadSessionTabsToClose(
                    tabs,
                    item.SessionId,
                    item.Kind,
                    IsTabConnected))
                {
                    if (_detachedTabs.TryGetValue(dead, out var state))
                    {
                        state.SuppressReattach = true;
                        state.Window.Close();
                    }
                    else if (viewModel.Tabs.Contains(dead))
                    {
                        CloseTab(dead);
                    }
                }
            }
        }
        // Serial ports and TCP servers stay exclusive to one tab.
        else
        {
            var detached = FindDetachedTab(item);
            if (detached is not null)
            {
                if (IsTabConnected(detached))
                {
                    _detachedTabs[detached].Window.Activate();
                    (detached.Terminal ?? (Control)detached.Content).Focus();
                    return;
                }

                _detachedTabs[detached].SuppressReattach = true;
                _detachedTabs[detached].Window.Close();
            }

            var existing = viewModel.FindTab(item.SessionId, item.Kind);
            if (existing is not null && IsTabConnected(existing))
            {
                viewModel.SelectedTab = existing;
                (existing.Terminal ?? (Control)existing.Content).Focus();
                return;
            }

            if (existing is not null)
            {
                CloseTab(existing);
            }
        }

        item.IsConnecting = true;
        try
        {
            if (SessionActivationOverrideForTests is { } activate)
            {
                await activate(item);
            }
            else if (item.Kind == SessionKind.Serial)
            {
                OpenSerialSession(item.SessionId);
            }
            else if (item.Kind == SessionKind.Ssh)
            {
                await OpenSshSessionAsync(item.SessionId);
            }
            else if (item.Kind == SessionKind.Telnet)
            {
                await OpenTelnetSessionAsync(item.SessionId);
            }
            else if (item.Kind == SessionKind.Tcp)
            {
                await OpenTcpSessionAsync(item.SessionId);
            }
        }
        // .NET 10 起 OCE 移入 System 命名空间（System.Threading 下已无此类型）
        catch (OperationCanceledException)
        {
            // 取消 ≠ 连接失败（连接中被用户中断/会话被移除）：MobaXterm 对
            // 取消不弹错误框，只复位 Connecting 状态（finally 处理）。
            AppLog.Info($"Session {item.SessionId} connection cancelled");
        }
        catch (Exception ex)
        {
            var action = item.Kind == SessionKind.Serial
                || (item.Kind == SessionKind.Tcp && IsTcpServerSession(item.SessionId))
                ? "打开失败"
                : "连接失败";
            await ShowErrorAsync($"{action}\n{ex.Message}");
        }
        finally
        {
            item.IsConnecting = false;
        }
    }

    private void OnSessionEditRequested(object? sender, SessionItem item)
    {
        if (item.Kind == SessionKind.Local || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var openTab = viewModel.FindTab(item.SessionId, item.Kind);
        if (openTab is not null)
        {
            ShowConnectionSettings(openTab);
            return;
        }

        ShowConnectionSettings(null, item.Kind, item.SessionId, item.Name);
    }

    private async void OnSessionRemoveRequested(object? sender, SessionItem item)
    {
        if (_config is null || DataContext is not MainViewModel)
        {
            return;
        }

        string label;
        string kind;
        if (item.Kind == SessionKind.Ssh)
        {
            var session = _config.SshSessions.Get(item.SessionId);
            label = session is null
                ? item.SessionId
                : $"{SshSessionRules.DisplayName(session)} ({SshSessionRules.Detail(session)})";
            kind = "SSH";
        }
        else if (item.Kind == SessionKind.Telnet)
        {
            var session = _config.TelnetSessions.Get(item.SessionId);
            label = session is null
                ? item.SessionId
                : $"{TelnetSessionRules.DisplayName(session)} ({TelnetSessionRules.Detail(session)})";
            kind = "Telnet";
        }
        else if (item.Kind == SessionKind.Tcp)
        {
            var session = _config.TcpSessions.Get(item.SessionId);
            label = session is null
                ? item.SessionId
                : $"{TcpSessionRules.DisplayName(session)} ({TcpSessionRules.Detail(session)})";
            kind = "TCP";
        }
        else
        {
            label = item.SessionId;
            kind = "serial";
        }

        var confirmed = await ShowConfirmAsync(
            "Remove session",
            $"Remove this {kind} session from memory?\n{label}");
        if (!confirmed)
        {
            return;
        }

        if (item.Kind == SessionKind.Ssh)
        {
            _config.SshSessions.Remove(item.SessionId);
            _config.PasswordCache.Remove(item.SessionId);
        }
        else if (item.Kind == SessionKind.Telnet)
        {
            _config.TelnetSessions.Remove(item.SessionId);
            _config.PasswordCache.Remove(item.SessionId);
        }
        else if (item.Kind == SessionKind.Tcp)
        {
            _config.TcpSessions.Remove(item.SessionId);
        }
        else
        {
            _config.SerialSessions.Remove(item.SessionId);
        }

        _config.Save();
        RefreshSidebar();
    }

    private void OpenSerialSession(string port)
    {
        if (_config is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var session = _config.SerialSessions.Get(port);
        if (session is null)
        {
            throw new InvalidOperationException($"Serial session {port} was not found.");
        }

        var connection = SerialConnectionFactory(session.Serial);
        var terminal = CreateTerminal();
        var state = new SerialTabState
        {
            Port = session.Port,
            Config = session.Serial,
            Terminal = terminal,
        };
        var tab = new TerminalTab
        {
            Title = session.Port,
            SessionId = session.Port,
            Kind = SessionKind.Serial,
            Content = terminal,
            Terminal = terminal,
            Connection = connection,
            StatusMessage = $"Connected {session.Port}",
            IsTransportOpen = () => state.IsTransportOpen,
            CloseAction = () => CloseSerialState(state),
        };
        state.Tab = tab;
        _serialTabs[tab] = state;
        AttachSerialTransport(state, connection);

        var terminalAttached = false;
        terminal.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (terminalAttached)
                    {
                        return;
                    }

                    terminalAttached = true;
                    state.TerminalAttached = true;
                    if (state.Connection is not { } current)
                    {
                        return;
                    }

                    AttachTerminal(tab, terminal, current.Pty);
                    FocusTerminalWhenSelected(tab, terminal);
                },
                DispatcherPriority.Loaded);

        viewModel.AddTab(tab);
        UpdateConnectionStates();
    }

    private void AttachSerialTransport(SerialTabState state, ISerialConnection connection)
    {
        state.Connection = connection;
        if (state.Tab is { } tab)
        {
            SubscribeZmodem(tab, connection.Pty.Zmodem);
        }

        connection.Pty.ProcessExited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            // Dispose can synchronously raise ProcessExited for the old
            // transport after a reconnect has already installed a new one.
            if (state.Closed || !ReferenceEquals(state.Connection, connection))
            {
                return;
            }

            MarkSerialDisconnected(state);
            UpdateConnectionStates();
        });
    }

    private void MarkSerialDisconnected(SerialTabState state)
    {
        if (state.Closed)
        {
            return;
        }

        state.AwaitingReconnect = true;
        state.NextReconnectAt = DateTime.MinValue;
        state.LastReconnectError = null;
        ReleaseSerialConnection(state);

        if (state.Tab is not { } tab)
        {
            return;
        }

        _zmodemTimings.Remove(tab);
        _zmodemStreams.Remove(tab);
        tab.IsAlive = false;
        tab.IsZmodemActive = false;
        tab.StatusMessage = "Connection lose";
        tab.StatusBrush = StatusWarningBrush;
        tab.TabDotBrush = StatusWarningBrush;
    }

    private void TryReconnectSerial(SerialTabState state)
    {
        if (state.Closed || state.Tab is not { } tab)
        {
            return;
        }

        ISerialConnection connection;
        try
        {
            connection = SerialConnectionFactory(state.Config);
        }
        catch (Exception ex)
        {
            // r67.6：重试过程要可见（用户反馈「插回设备后一直 Connection lose，
            // 不知道有没有在重试」）。前 5 次每 2s 重试，之后降到 5s，避免一个
            // 幽灵端口（驱动一直挂着、实际打不开）让 UI 每 2s 空转。
            state.ReconnectAttempts++;
            var delay = state.ReconnectAttempts <= 5 ? 2 : 5;
            state.NextReconnectAt = DateTime.UtcNow.AddSeconds(delay);
            tab.IsAlive = false;
            tab.StatusMessage = $"Reconnecting {state.Port} (attempt {state.ReconnectAttempts})...";
            tab.StatusBrush = StatusWarningBrush;
            tab.TabDotBrush = StatusWarningBrush;
            if (!string.Equals(state.LastReconnectError, ex.Message, StringComparison.Ordinal))
            {
                state.LastReconnectError = ex.Message;
                AppLog.Info($"Serial reconnect {state.Port} failed: {ex.Message}");
            }

            return;
        }

        state.AwaitingReconnect = false;
        state.NextReconnectAt = DateTime.MinValue;
        state.LastReconnectError = null;
        state.ReconnectAttempts = 0;
        AttachSerialTransport(state, connection);

        tab.IsAlive = true;
        tab.StatusMessage = $"Connected {state.Port}";
        tab.StatusBrush = StatusInfoBrush;
        tab.TabDotBrush = StatusInfoBrush;

        if (state.TerminalAttached)
        {
            AttachTerminal(tab, state.Terminal, connection.Pty);
        }

        if (tab.IsSelected)
        {
            FocusTerminalWhenSelected(tab, state.Terminal);
        }

        AppLog.Info($"Serial session {state.Port} reconnected");
    }

    private static void ReleaseSerialConnection(SerialTabState state)
    {
        var connection = state.Connection;
        state.Connection = null;
        try
        {
            connection?.Dispose();
        }
        catch
        {
            // The device may already be gone.
        }
    }

    private void CloseSerialState(SerialTabState state)
    {
        if (state.Closed)
        {
            return;
        }

        state.Closed = true;
        state.AwaitingReconnect = false;
        try
        {
            state.Terminal.DetachConnection();
        }
        catch
        {
            // The template may not have been applied if opening failed.
        }

        ReleaseSerialConnection(state);
        if (state.Tab is { } tab)
        {
            _serialTabs.Remove(tab);
            _zmodemTimings.Remove(tab);
            _zmodemStreams.Remove(tab);
            tab.IsZmodemActive = false;
        }
    }

    private async Task OpenSshSessionAsync(string key)
    {
        if (_config is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var session = _config.SshSessions.Get(key);
        if (session is null)
        {
            throw new InvalidOperationException($"SSH session {key} was not found.");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var connection = await ConnectSshSessionAsync(session, this);
        if (connection is null)
        {
            return;
        }

        var terminal = CreateTerminal();
        var pane = new SftpPaneViewModel(
            () => connection.CreateSftpFileSystem(),
            new SystemLocalFileSystem());
        var sessionView = new SshSessionView { Terminal = terminal };
        var tab = new TerminalTab
        {
            Title = SshSessionRules.DisplayName(session),
            SessionId = session.Key,
            Kind = SessionKind.Ssh,
            Content = sessionView,
            Terminal = terminal,
            SftpPane = pane,
            // MobaXterm 式记忆：会话配置里记忆的监控开关随会话恢复
            // （标签选中后 MonitorOnSelectedTabChanged 自动开始轮询）
            MonitorEnabled = session.Monitor,
            StatusMessage = $"Connected {SshSessionRules.Detail(session)}",
        };
        // r66: 新标签按钮初态必须与其运行态标志一致。IsMonitorOn 默认为 false，
        // 而同 Key 的第二个标签会从共享配置恢复 MonitorEnabled=true——若不在这里
        // 同步，按钮显示与轮询意图不一致（选中该标签前一直是灰的）。
        pane.SyncMonitorOn(session.Monitor);
        AttachSshTransport(tab, terminal, connection);

        // The "远程监控" toggle of the SFTP pane drives the per-tab monitor flag
        // and the poller through the view model; the on/off state is also
        // remembered on the saved session (MobaXterm behaviour) so the next
        // launch of this session restores it automatically.
        pane.MonitorToggled = v =>
        {
            AppLog.Info(
                $"Remote monitor toggle: {session.Key} -> {(v ? "on" : "off")} " +
                $"(selected={ReferenceEquals(viewModel.SelectedTab, tab)})");
            viewModel.SetMonitorEnabled(tab, v);
            _config.SshSessions.SetMonitor(session.Key, v);
            _config.Save();
        };

        // r60: SFTP 浏览器右键「将文件路径复制到终端」与鼠标中键单击：
        // 把远程路径文本注入本标签终端（不带回车，MobaXterm 中键粘贴语义）。
        pane.PathToTerminal = path =>
        {
            if (tab.Terminal is { } term)
            {
                _ = term.SendInputAsync(path);
            }
        };

        var stoppedBehavior = new SessionStoppedBehavior(
            terminal,
            tab,
            () => CloseTab(tab),
            () => RestartSshSessionAsync(tab),
            () => SaveStoppedTerminalOutputAsync(tab));
        stoppedBehavior.Attach();

        var terminalAttached = false;
        terminal.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (terminalAttached)
                    {
                        return;
                    }

                    terminalAttached = true;
                    if (!ReferenceEquals(tab.Connection, connection))
                    {
                        return;
                    }

                    AttachTerminal(tab, terminal, connection.Pty);
                    FocusTerminalWhenSelected(tab, terminal);
                    AppLog.Info(
                        $"SSH session {key} terminal attached after {stopwatch.ElapsedMilliseconds} ms");
                },
                DispatcherPriority.Loaded);

        viewModel.AddTab(tab);
        UpdateConnectionStates();
        // r54 ③: MobaXterm parity (AutoStartSSHGUI=1) — every freshly
        // connected SSH session opens its SFTP browser automatically.
        OpenSshBrowserFor(tab);
        AppLog.Info(
            $"SSH session {key} tab created after {stopwatch.ElapsedMilliseconds} ms");
    }

    private async Task<SshConnection?> ConnectSshSessionAsync(
        SshSession session,
        Window owner)
    {
        if (_config is null)
        {
            return null;
        }

        string? password = null;
        var remember = false;
        if (string.IsNullOrWhiteSpace(session.SshKey))
        {
            password = _config.PasswordCache.Get(session.Key);
            if (string.IsNullOrEmpty(password))
            {
                password = SecretProtector.Unprotect(session.EncryptedPassword);
            }
        }

        for (var attempt = 0; ; attempt++)
        {
            if (string.IsNullOrWhiteSpace(session.SshKey) && string.IsNullOrEmpty(password))
            {
                var dialog = new SshPasswordDialog(SshSessionRules.Detail(session));
                LastSshPasswordDialogForTests = dialog;
                try
                {
                    var accepted = await dialog.ShowDialog<bool>(owner);
                    if (!accepted)
                    {
                        return null;
                    }

                    password = dialog.Password;
                    remember = dialog.Remember;
                }
                finally
                {
                    if (ReferenceEquals(LastSshPasswordDialogForTests, dialog))
                    {
                        LastSshPasswordDialogForTests = null;
                    }
                }
            }

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var connection = await Task.Run(() => SshConnection.Connect(
                    new SshConnectionOptions(
                        session.Host,
                        session.Port,
                        session.User,
                        Password: password,
                        KeyFile: session.SshKey,
                        Columns: 100,
                        Rows: 30)));
                stopwatch.Stop();
                AppLog.Info(
                    $"SSH connect {session.Host}:{session.Port} ({session.User}) took " +
                    $"{stopwatch.ElapsedMilliseconds} ms " +
                    $"(handshake {connection.ConnectTime.TotalMilliseconds:F0} ms)");
                _config.PasswordCache.Set(session.Key, password);
                if (remember && !string.IsNullOrEmpty(password))
                {
                    _config.RememberSshPassword(session, password, remember: true);
                }

                return connection;
            }
            catch (Renci.SshNet.Common.SshAuthenticationException)
                when (string.IsNullOrWhiteSpace(session.SshKey) && attempt < 2)
            {
                _config.PasswordCache.Remove(session.Key);
                password = null;
                remember = false;
            }
        }
    }

    private void AttachSshTransport(
        TerminalTab tab,
        TerminalControl terminal,
        SshConnection connection)
    {
        tab.Connection = connection;
        tab.IsTransportOpen = () => connection.IsConnected;
        tab.CloseAction = () =>
        {
            // The monitor polls through this connection; stop it before the
            // connection dies so the exec channel is not used post-dispose.
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.MonitorStopForTab(tab);
            }

            // A transfer that runs when the tab is closed would write
            // against a connection that is about to die.
            tab.SftpPane?.CancelTransfer();

            try
            {
                terminal.DetachConnection();
            }
            catch
            {
                // The template may not have been applied if opening failed.
            }

            connection.Dispose();
        };

        SubscribeZmodem(tab, connection.Pty.Zmodem);
        connection.Pty.ProcessExited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(tab.Connection, connection))
            {
                return;
            }

            tab.IsAlive = false;
            tab.IsSessionStopped = true;
            tab.StatusMessage = "Session stopped";
            tab.StatusBrush = StatusWarningBrush;
            tab.TabDotBrush = StatusWarningBrush;
            UpdateConnectionStates();
            // The shell is gone: stop the monitor (the per-tab flag stays on,
            // so an R restart re-attaches and resumes polling automatically).
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.MonitorStopForTab(tab);
            }
        });

        // A freshly attached transport is by definition alive: resume
        // monitoring if this tab has it enabled (first connect or R restart).
        if (DataContext is MainViewModel viewModel2)
        {
            viewModel2.MonitorOnTransportAttached(tab);
        }
    }

    private async Task RestartSshSessionAsync(TerminalTab tab)
    {
        if (_config is null
            || tab.Kind != SessionKind.Ssh
            || !_sshRestarts.Add(tab))
        {
            return;
        }

        var terminal = tab.Terminal;
        SshConnection? connection = null;
        try
        {
            if (!IsTabOpen(tab))
            {
                return;
            }

            var session = _config.SshSessions.Get(tab.SessionId);
            if (session is null)
            {
                tab.StatusMessage = "SSH session not found";
                tab.StatusBrush = StatusErrorBrush;
                return;
            }

            tab.StatusMessage = $"Restarting {SshSessionRules.Detail(session)}...";
            tab.StatusBrush = StatusProgressBrush;
            tab.IsRestarting = true;

            connection = await ConnectSshSessionAsync(session, OwnerFor(tab));
            if (connection is null)
            {
                tab.StatusMessage = "Session stopped";
                tab.StatusBrush = StatusWarningBrush;
                tab.IsRestarting = false;
                if (terminal is not null)
                {
                    RestoreStoppedTerminalFocus(tab, terminal);
                }

                return;
            }

            if (!_sshRestarts.Contains(tab) || !IsTabOpen(tab) || terminal is null)
            {
                connection.Dispose();
                tab.IsRestarting = false;
                return;
            }

            var previousClose = tab.CloseAction;
            AttachSshTransport(tab, terminal, connection);
            try
            {
                terminal.DetachConnection();
            }
            catch
            {
                // The old shell may already have torn down its view.
            }

            try
            {
                previousClose?.Invoke();
            }
            catch
            {
                // The old transport is already gone.
            }

            // The drawer of the session talks to the new connection now.
            // Whether it is open and how high it is stay as they are.
            tab.SftpPane?.SetRemoteFactory(() => connection.CreateSftpFileSystem());

            terminal.ClearScreen();
            terminal.AttachConnection(connection.Pty);
            tab.IsAlive = true;
            tab.IsSessionStopped = false;
            tab.StatusMessage = $"Connected {SshSessionRules.Detail(session)}";
            tab.StatusBrush = StatusInfoBrush;
            tab.TabDotBrush = StatusInfoBrush;
            // The attempt is over and the session is live again: clear the
            // restart flag here as well (previously only the catch path did,
            // so the status bar kept showing "Restarting..." after a
            // successful reconnect).
            tab.IsRestarting = false;
            UpdateConnectionStates();
            RestoreStoppedTerminalFocus(tab, terminal);
        }
        catch (Exception ex)
        {
            if (connection is not null && ReferenceEquals(tab.Connection, connection))
            {
                try
                {
                    terminal?.DetachConnection();
                }
                catch
                {
                    // The new shell may already have failed during attach.
                }

                connection.Dispose();
                tab.Connection = null;
                tab.CloseAction = null;
                tab.IsTransportOpen = null;
                if (DataContext is MainViewModel viewModel2)
                {
                    viewModel2.MonitorStopForTab(tab);
                }
            }

            tab.IsAlive = false;
            tab.IsSessionStopped = true;
            tab.StatusMessage = $"Restart failed: {ex.Message}";
            tab.StatusBrush = StatusErrorBrush;
            tab.TabDotBrush = StatusWarningBrush;
            AppLog.Error($"SSH restart failed for {tab.SessionId}", ex);
            if (terminal is not null)
            {
                RestoreStoppedTerminalFocus(tab, terminal);
            }

            // The attempt is over: drop the progress bar, release the
            // re-entry guard, then ask whether to keep trying.
            tab.IsRestarting = false;
            _sshRestarts.Remove(tab);
            if (IsTabOpen(tab) && await ShowRestartAskAsync(OwnerFor(tab), ex.Message) && IsTabOpen(tab))
            {
                _ = RestartSshSessionAsync(tab);
            }
        }
        finally
        {
            _sshRestarts.Remove(tab);
        }
    }

    private async Task SaveStoppedTerminalOutputAsync(TerminalTab tab)
    {
        if (tab.Content is not TerminalControl terminal)
        {
            return;
        }

        var owner = OwnerFor(tab);
        try
        {
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save terminal output",
                SuggestedFileName = $"{SafeFileName(tab.Title)}-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                DefaultExtension = "txt",
            });
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek)
            {
                stream.SetLength(0);
            }

            await TerminalTranscript.SaveAsync(terminal, stream);
            tab.StatusMessage = $"Saved terminal output: {file.Name}";
            tab.StatusBrush = StatusInfoBrush;
        }
        catch (Exception ex)
        {
            tab.StatusMessage = $"Save failed: {ex.Message}";
            tab.StatusBrush = StatusErrorBrush;
            AppLog.Error("Failed to save terminal output", ex);
        }
        finally
        {
            RestoreStoppedTerminalFocus(tab, terminal);
        }
    }

    private bool IsTabOpen(TerminalTab tab) =>
        (DataContext is MainViewModel viewModel && viewModel.Tabs.Contains(tab))
        || _detachedTabs.ContainsKey(tab);

    private Window OwnerFor(TerminalTab tab) =>
        _detachedTabs.TryGetValue(tab, out var state) ? state.Window : this;

    private void RestoreStoppedTerminalFocus(TerminalTab tab, TerminalControl terminal)
    {
        if (!IsTabOpen(tab))
        {
            return;
        }

        if (_detachedTabs.ContainsKey(tab))
        {
            terminal.Focus();
            return;
        }

        FocusTerminalWhenSelected(tab, terminal);
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());
        sanitized = sanitized.Trim();
        return string.IsNullOrEmpty(sanitized) ? "terminal-output" : sanitized;
    }

    private static bool IsTabConnected(TerminalTab tab) =>
        tab.IsAlive && (tab.IsTransportOpen?.Invoke() ?? true);

    private bool IsTcpServerSession(string key)
    {
        var session = _config?.TcpSessions.Get(key);
        return session is not null
            && (TcpSessionRules.NormalizeProtocol(session.Protocol) == TcpSessionRules.UdpProtocol
                || TcpSessionRules.NormalizeMode(session.Mode) == TcpSessionRules.ServerMode);
    }

    private async Task OpenTelnetSessionAsync(string key)
    {
        if (_config is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var session = _config.TelnetSessions.Get(key);
        if (session is null)
        {
            throw new InvalidOperationException($"Telnet session {key} was not found.");
        }

        var connection = await ConnectTelnetSessionAsync(session);
        var terminal = CreateTerminal();
        var tab = new TerminalTab
        {
            Title = TelnetSessionRules.DisplayName(session),
            SessionId = session.Key,
            Kind = SessionKind.Telnet,
            Content = terminal,
            Terminal = terminal,
            StatusMessage = $"Connected {TelnetSessionRules.Detail(session)}",
        };
        AttachTelnetTransport(tab, terminal, connection);

        // Telnet uses the same MobaXterm-style stopped screen as SSH:
        // Enter closes the tab, R reconnects and S saves the output.
        var stoppedBehavior = new SessionStoppedBehavior(
            terminal,
            tab,
            () => CloseTab(tab),
            () => RestartTelnetSessionAsync(tab),
            () => SaveStoppedTerminalOutputAsync(tab));
        stoppedBehavior.Attach();

        var terminalAttached = false;
        terminal.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (terminalAttached
                        || !IsTabOpen(tab)
                        || !ReferenceEquals(tab.Connection, connection))
                    {
                        return;
                    }

                    terminalAttached = true;
                    AttachTerminal(tab, terminal, connection.Pty);
                    FocusTerminalWhenSelected(tab, terminal);
                },
                DispatcherPriority.Loaded);

        viewModel.AddTab(tab);
        UpdateConnectionStates();
    }

    /// <summary>
    /// Opens a Telnet transport for a saved session. The password comes from
    /// the in-memory cache first and from the DPAPI token second. A session
    /// without a stored password reconnects without auto-login so the server
    /// prompt stays in the terminal instead of blocking on a dialog.
    /// </summary>
    private async Task<TelnetConnection> ConnectTelnetSessionAsync(TelnetSession session)
    {
        if (_config is null)
        {
            throw new InvalidOperationException("The configuration is not loaded.");
        }

        var password = _config.PasswordCache.Get(session.Key);
        if (string.IsNullOrEmpty(password))
        {
            password = SecretProtector.Unprotect(session.EncryptedPassword);
        }

        var connection = await TelnetConnection.ConnectAsync(
            new TelnetConnectionOptions(
                session.Host,
                session.Port,
                session.User,
                password));
        if (!string.IsNullOrEmpty(password))
        {
            _config.PasswordCache.Set(session.Key, password);
        }

        return connection;
    }

    private void AttachTelnetTransport(
        TerminalTab tab,
        TerminalControl terminal,
        TelnetConnection connection)
    {
        tab.Connection = connection;
        tab.IsTransportOpen = () => connection.IsConnected;
        tab.CloseAction = () =>
        {
            try
            {
                terminal.DetachConnection();
            }
            catch
            {
                // The template may not have been applied if opening failed.
            }

            connection.Dispose();
        };

        SubscribeZmodem(tab, connection.Pty.Zmodem);
        connection.Pty.ProcessExited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(tab.Connection, connection))
            {
                return;
            }

            tab.IsAlive = false;
            tab.IsSessionStopped = true;
            tab.StatusMessage = "Session stopped";
            tab.StatusBrush = StatusWarningBrush;
            tab.TabDotBrush = StatusWarningBrush;
            UpdateConnectionStates();
        });
    }

    private async Task RestartTelnetSessionAsync(TerminalTab tab)
    {
        if (_config is null
            || tab.Kind != SessionKind.Telnet
            || !_telnetRestarts.Add(tab))
        {
            return;
        }

        var terminal = tab.Terminal;
        TelnetConnection? connection = null;
        try
        {
            if (!IsTabOpen(tab))
            {
                return;
            }

            var session = _config.TelnetSessions.Get(tab.SessionId);
            if (session is null)
            {
                tab.StatusMessage = "Telnet session not found";
                tab.StatusBrush = StatusErrorBrush;
                return;
            }

            tab.StatusMessage = $"Restarting {TelnetSessionRules.Detail(session)}...";
            tab.StatusBrush = StatusProgressBrush;
            tab.IsRestarting = true;

            connection = await ConnectTelnetSessionAsync(session);
            if (!_telnetRestarts.Contains(tab) || !IsTabOpen(tab) || terminal is null)
            {
                connection.Dispose();
                tab.IsRestarting = false;
                return;
            }

            var previousClose = tab.CloseAction;
            AttachTelnetTransport(tab, terminal, connection);
            try
            {
                terminal.DetachConnection();
            }
            catch
            {
                // The old transport may already have torn down its view.
            }

            try
            {
                previousClose?.Invoke();
            }
            catch
            {
                // The old transport is already gone.
            }

            terminal.ClearScreen();
            terminal.AttachConnection(connection.Pty);
            tab.IsAlive = true;
            tab.IsSessionStopped = false;
            tab.StatusMessage = $"Connected {TelnetSessionRules.Detail(session)}";
            tab.StatusBrush = StatusInfoBrush;
            tab.TabDotBrush = StatusInfoBrush;
            // Same as the SSH restart: the successful reconnect ends the attempt,
            // so the progress area goes away with it.
            tab.IsRestarting = false;
            UpdateConnectionStates();
            RestoreStoppedTerminalFocus(tab, terminal);
        }
        catch (Exception ex)
        {
            if (connection is not null && ReferenceEquals(tab.Connection, connection))
            {
                try
                {
                    terminal?.DetachConnection();
                }
                catch
                {
                    // The new transport may already have failed during attach.
                }

                connection.Dispose();
                tab.Connection = null;
                tab.CloseAction = null;
                tab.IsTransportOpen = null;
            }

            tab.IsAlive = false;
            tab.IsSessionStopped = true;
            tab.StatusMessage = $"Restart failed: {ex.Message}";
            tab.StatusBrush = StatusErrorBrush;
            tab.TabDotBrush = StatusWarningBrush;
            AppLog.Error($"Telnet restart failed for {tab.SessionId}", ex);
            if (terminal is not null)
            {
                RestoreStoppedTerminalFocus(tab, terminal);
            }

            // The attempt is over: drop the progress bar, release the
            // re-entry guard, then ask whether to keep trying.
            tab.IsRestarting = false;
            _telnetRestarts.Remove(tab);
            if (IsTabOpen(tab) && await ShowRestartAskAsync(OwnerFor(tab), ex.Message) && IsTabOpen(tab))
            {
                _ = RestartTelnetSessionAsync(tab);
            }
        }
        finally
        {
            _telnetRestarts.Remove(tab);
        }
    }

    private async Task OpenTcpSessionAsync(string key)
    {
        if (_config is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var session = _config.TcpSessions.Get(key);
        if (session is null)
        {
            throw new InvalidOperationException($"TCP session {key} was not found.");
        }

        ITcpSession connection;
        string status;
        var protocol = TcpSessionRules.NormalizeProtocol(session.Protocol);
        if (protocol == TcpSessionRules.UdpProtocol)
        {
            if (!TcpTarget.TryParse(session.Target, out var target) || target is null)
            {
                throw new InvalidOperationException("UDP target must be host[:port].");
            }

            connection = await UdpSession.CreateAsync(
                new UdpSessionOptions(target.Host, target.Port, session.Port));
            status = $"UDP local :{session.Port} -> {target}";
        }
        else if (TcpSessionRules.NormalizeMode(session.Mode) == TcpSessionRules.ServerMode)
        {
            connection = new TcpServerSession(new TcpServerOptions(session.Port));
            status = $"Listening on 0.0.0.0:{session.Port}";
        }
        else
        {
            if (!TcpTarget.TryParse(session.Target, out var target) || target is null)
            {
                throw new InvalidOperationException("TCP target must be host[:port].");
            }

            connection = await TcpClientSession.ConnectAsync(
                new TcpClientOptions(
                    target.Host,
                    target.Port,
                    AutoReconnect: session.AutoReconnect,
                    ReconnectInterval: TimeSpan.FromSeconds(
                        TcpSessionRules.NormalizeReconnectInterval(session.ReconnectInterval))));
            status = $"Connected {target}";
        }

        var terminal = CreateTerminal();
        var tab = new TerminalTab
        {
            Title = TcpSessionRules.DisplayName(session),
            SessionId = key,
            Kind = SessionKind.Tcp,
            Content = terminal,
            Terminal = terminal,
            Connection = connection,
            StatusMessage = status,
            IsTransportOpen = () => connection.IsConnected,
            CloseAction = () =>
            {
                try
                {
                    terminal.DetachConnection();
                }
                catch
                {
                    // The template may not have been applied if opening failed.
                }

                connection.Dispose();
            },
        };

        SubscribeZmodem(tab, connection.Pty.Zmodem);
        if (connection is TcpClientSession client)
        {
            client.StatusChanged += (_, args) => Dispatcher.UIThread.Post(() =>
            {
                if (!tab.IsAlive)
                {
                    return;
                }

                tab.StatusMessage = args.Message;
                var brush = args.IsConnected ? StatusInfoBrush : StatusWarningBrush;
                tab.StatusBrush = brush;
                tab.TabDotBrush = brush;
                UpdateConnectionStates();
            });
        }

        if (connection is TcpServerSession server)
        {
            server.ClientsChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (!tab.IsAlive)
                {
                    return;
                }

                var clients = server.Clients;
                tab.StatusMessage = clients.Count == 0
                    ? $"Listening on 0.0.0.0:{server.Port}"
                    : $"{clients.Count} client(s): {string.Join(", ", clients.Select(client => client.Endpoint))}";
                tab.StatusBrush = StatusInfoBrush;
                tab.TabDotBrush = StatusInfoBrush;
                UpdateConnectionStates();
            });
        }

        var terminalAttached = false;
        terminal.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (terminalAttached
                        || !IsTabOpen(tab)
                        || !ReferenceEquals(tab.Connection, connection))
                    {
                        return;
                    }

                    terminalAttached = true;
                    AttachTerminal(tab, terminal, connection.Pty);
                    FocusTerminalWhenSelected(tab, terminal);
                },
                DispatcherPriority.Loaded);
        connection.Pty.ProcessExited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            tab.IsAlive = false;
            tab.StatusMessage = "Connection lose";
            tab.StatusBrush = StatusWarningBrush;
            tab.TabDotBrush = StatusWarningBrush;
            UpdateConnectionStates();
        });

        viewModel.AddTab(tab);
        UpdateConnectionStates();
    }

    internal void AttachTerminal(
        TerminalTab tab,
        TerminalControl terminal,
        PtyStreamConnection pty)
    {
        if (!tab.IsAlive)
        {
            return;
        }

        try
        {
            // 套上行尾仿真层（MobaXterm「Implicit CR/LF」两项）。必须在 attach 之前：
            // 库的读循环在 attach 启动后才取 ReaderStream，此时替换立即对全部读取生效；
            // 两个开关都关时 LineEndingStream 走整块直通，零额外开销。
            pty.WrapReader(new LineEndingStream(pty.ReaderStream, _lineEndingOptions));
            terminal.AttachConnection(pty);
        }
        catch (Exception ex)
        {
            tab.IsAlive = false;
            _ = ShowErrorAsync($"终端附加失败：{ex.Message}");
            return;
        }

        // r39：挂语法高亮着色器（re-attach 时先卸旧实例，防重复着色）
        AttachTerminalHighlighter(terminal);

        // 把 pty 同步到当前视口尺寸（MobaXterm 行为）：附加时终端网格已按最终窗口
        // 布局完毕，而库的 resize 只在“尺寸变化”时发送——这个首个真实尺寸永远不会
        // 推给远端，pty 停在建连时的 100x30，top 等全屏程序只能画 30 行。
        // Background 优先级让本任务在当前布局完成后再执行（度量已就绪）。
        Dispatcher.UIThread.Post(
            () => SyncPtySizeToViewport(tab, terminal, pty),
            DispatcherPriority.Background);
    }

    /// <summary>
    /// 把终端当前视口的 cols/rows 推给远端 pty（算法与库 ArrangeOverride 一致）。
    /// 只用于附加时的首个尺寸；之后窗口再变化由库自身的 resize 路径跟进。
    /// </summary>
    internal static void SyncPtySizeToViewport(
        TerminalTab tab, TerminalControl terminal, PtyStreamConnection pty)
    {
        if (!tab.IsAlive
            || terminal.Bounds.Width <= 0
            || terminal.Bounds.Height <= 0)
        {
            return;
        }

        var charWidth = terminal.CharWidth;
        var charHeight = terminal.CharHeight;
        if (charWidth <= 0 || charHeight <= 0)
        {
            return;
        }

        var cols = (int)((terminal.Bounds.Width - Math.Max(0, terminal.GutterWidth)) / charWidth);
        var rows = (int)(terminal.Bounds.Height / charHeight);
        if (cols <= 0 || rows <= 0)
        {
            return;
        }

        pty.Resize(cols, rows);
        AppLog.Info($"Pty size synced to viewport: {cols}x{rows}");
    }

    // MobaXterm's terminal font is Consolas 10pt. Consolas carries no CJK
    // glyphs; on Windows the system CJK fallback (Microsoft YaHei UI) answers
    // for them — that is exactly what MobaXterm shows (r41.1: user side-by-side
    // screenshots from the same machine proved MobaXterm's CJK glyphs are
    // YaHei-style, NOT the lighter/bundled Noto face that r41 briefly made
    // primary). YaHei UI first, bundled Noto Sans Mono CJK SC (Regular+Bold)
    // second: it keeps the r33 two-cell alignment and guarantees CJK coverage
    // on Linux / headless where YaHei does not exist.
    // 「字体设置」菜单项可改写首选族（写 config terminal.fontFamily）；回退链
    // 恒保留，保证中英混排 2 格对齐（r33）不被单族字体破坏。
    internal static string TerminalFontPrimary = "Consolas";
    internal const string TerminalFontFallback =
        "Microsoft YaHei UI," +
        "avares://COMTool.App/Assets/Fonts/NotoSansMonoCJKsc-Regular.otf#Noto Sans Mono CJK SC," +
        "avares://COMTool.App/Assets/Fonts/NotoSansMonoCJKsc-Bold.otf#Noto Sans Mono CJK SC";

    internal static FontFamily BuildTerminalFontFamily(string primary)
    {
        var head = string.IsNullOrWhiteSpace(primary) ? "Consolas" : primary;
        return new FontFamily(head + "," + TerminalFontFallback);
    }

    internal static FontFamily TerminalFontFamily() => BuildTerminalFontFamily(TerminalFontPrimary);

    // MobaXterm's terminal cell measures 7.94px across on the user's machine
    // (MobaFont 11pt). Consolas 14.5px advances 7.97px per cell, so the whole
    // screen — the CJK glyphs included — is the same physical size as
    // MobaXterm's. 13.5px (the pre-r42 default) advanced only 7.44px, which is
    // what made our Chinese look a size smaller in the side-by-side shots.
    internal const double TerminalFontSize = TerminalConfig.DefaultFontSize;

    // Aero Snap (drag to a screen edge) only works when the window carries
    // the native resize style. The window itself stays frameless on every
    // platform: Avalonia's WindowDecorations.None answers WM_NCCALCSIZE with
    // the full rect, so on Windows the bit enables snapping without any
    // visible DWM border (the BorderOnly decoration that used to provide the
    // frame left a blue/white strip above the title bar on Windows 10).
    internal const long SnapFrameStyle = 0x00040000; // WS_THICKFRAME

    internal static bool WantsSnapFrame(bool isWindows) => isWindows;

    internal static long WithSnapFrame(long style) => style | SnapFrameStyle;

    /// <summary>
    /// Warms Avalonia's typeface cache off the UI thread. The bundled CJK
    /// fallback is a 16 MB CFF font; parsing it when the first terminal is
    /// created delayed the first SSH tab by a noticeable amount. Resolving the
    /// same typefaces at startup moves that cost out of the connect path.
    /// </summary>
    private static void WarmUpTerminalFonts()
    {
        var family = TerminalFontFamily();
        _ = Task.Run(() =>
        {
            try
            {
                foreach (var weight in new[] { FontWeight.Normal, FontWeight.Bold })
                {
                    var typeface = new Typeface(family, FontStyle.Normal, weight);
                    if (typeface.GlyphTypeface is { } glyphs)
                    {
                        _ = glyphs.CharacterToGlyphMap.TryGetGlyph('L', out _);
                        _ = glyphs.CharacterToGlyphMap.TryGetGlyph('個', out _);
                    }
                }
            }
            catch
            {
                // Best effort: the terminal still loads the font on demand.
            }
        });
    }

    internal TerminalControl CreateTerminal()
    {
        var terminal = new TerminalControl
        {
            // The control defaults Process to bash/cmd and auto-launches it when
            // loaded. These tabs always attach an external serial/SSH stream, so
            // an empty Process stops a local shell from starting on reparent and
            // writing its own prompt into the terminal buffer.
            Process = string.Empty,
            // r56：MobaXterm 默认字体无粗体面，远端 SGR 1（粗体）视觉无效果
            // （用户同板对照截图：ifconfig 数值在 MobaXterm 为细体）。
            // Consolas 有粗体面，若照实渲染会画成粗字，与 MobaXterm 不一致，
            // 故默认关闭粗体面渲染（BoldFont=true 可回退原行为）。
            BoldFont = false,
            FontFamily = TerminalFontFamily(),
            FontSize = _terminalFontSize,
            Background = new SolidColorBrush(Color.Parse(TerminalTheme.Background)),
            Foreground = new SolidColorBrush(Color.Parse(TerminalTheme.Foreground)),
            CursorColor = Color.Parse(TerminalTheme.Cursor),
            BufferSize = 9999,
        };
        if (terminal.Options is { } terminalOptions)
        {
            terminalOptions.RightClickSelectsWord = false;
            terminalOptions.Theme = TerminalTheme.CreateMobaXtermTheme();
        }

        terminal.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    ApplyTerminalTheme(terminal);
                    ApplyTerminalScrollBar(terminal);
                },
                DispatcherPriority.Loaded);
        // r67.7：模板可能晚于 AttachedToVisualTree 才应用（Loaded 回调先跑），
        // 之后再补一次，保证 LargeChange（一屏翻页）与按钮重复节奏一定装上。
        terminal.TemplateApplied += (_, _) => ApplyTerminalScrollBar(terminal);
        // 终端右键被库抢先：模板内最深命中目标 TerminalView 在自己的 OnPointerPressed 里
        // 同步置 e.Handled=true 并执行复制/粘贴，TerminalControl 上的冒泡阶段 handler
        // 永远收不到真实右键（实机 Shift+右键菜单不弹的根因）。改在隧道阶段订阅
        // （自根向目标传播，先于库的类 handler 到达）拦截：
        // Shift+右键 = MobaXterm 式终端菜单；普通右键 = 复制/粘贴（规范化粘贴）。
        terminal.AddHandler(
            InputElement.PointerPressedEvent,
            OnTerminalPointerPressed,
            RoutingStrategies.Tunnel);
        terminal.KeyDown += OnTerminalKeyDown;
        // r39「只读终端」：隧道阶段拦 KeyDown（先于库内部键输入→pty 路径触发）。
        // 窗口级 handler（F11/宏热键/Ctrl+Shift+F）在更上层隧道先跑，不受影响。
        terminal.AddHandler(
            InputElement.KeyDownEvent,
            OnTerminalReadOnlyGuard,
            RoutingStrategies.Tunnel);
        // r40.3：终端显示区双击不再切换最大化/还原（用户实机确认 MobaXterm
        // 只有最顶层标题栏有双击最大化；r15 旧实现已移除）。
        return terminal;
    }

    private static void ApplyTerminalTheme(TerminalControl terminal)
    {
        // A tab can be closed before the queued Loaded callback runs. There is
        // no XTerm instance to update in that case.
        if (TopLevel.GetTopLevel(terminal) is null)
        {
            return;
        }

        // The control seeds its palette from the Foreground/Background brushes
        // when the template is applied, which happens after the options are
        // created. Re-apply the MobaXterm palette once the XTerm instance exists.
        var theme = TerminalTheme.CreateMobaXtermTheme();
        if (terminal.Options is { } options)
        {
            options.Theme = theme;
        }

        terminal.Terminal.Colors.ApplyTheme(theme);
    }

    /// <summary>
    /// r67.7：把库模板里的滚动条变成 MobaXterm 式的常显滚动条。
    /// 宽度/配色由 AppStyles 的 TerminalScrollBarTheme 负责；这里只补两件样式
    /// 表达不了的事：
    /// 1) <c>LargeChange = ViewportSize</c>——点一下/按住重复一次滚一整屏
    ///    （库只设了 Minimum/Maximum/ViewportSize，LargeChange 是 RangeBase 默认 10 行）；
    /// 2) 上下翻页按钮的重复节奏 300ms 首延迟 + 250ms 间隔，对齐 MobaXterm 实机
    ///    （Avalonia 默认 300/100 太快，按住会飞）。
    /// </summary>
    internal static void ApplyTerminalScrollBar(TerminalControl terminal)
    {
        foreach (var scrollBar in terminal.GetVisualDescendants().OfType<ScrollBar>())
        {
            ApplyTerminalScrollBar(scrollBar);
        }
    }

    internal static void ApplyTerminalScrollBar(ScrollBar scrollBar)
    {
        scrollBar.Width = TerminalScrollBarWidth;
        scrollBar.MinWidth = TerminalScrollBarWidth;
        scrollBar.LargeChange = Math.Max(1, scrollBar.ViewportSize);

        if (!_terminalScrollBarsApplied.TryGetValue(scrollBar, out _))
        {
            _terminalScrollBarsApplied.Add(scrollBar, null);
            scrollBar.PropertyChanged += (_, e) =>
            {
                if (e.Property == ScrollBar.ViewportSizeProperty)
                {
                    scrollBar.LargeChange = Math.Max(1, scrollBar.ViewportSize);
                }
            };
        }

        foreach (var button in scrollBar.GetVisualDescendants().OfType<RepeatButton>())
        {
            if (button.Name is "PART_PageUpButton" or "PART_PageDownButton")
            {
                button.Delay = 300;
                button.Interval = 250;
            }
        }
    }

    /// <summary>r67.7：终端滚动条宽度（MobaXterm 实机 16px）。</summary>
    private const double TerminalScrollBarWidth = 16;

    private static readonly ConditionalWeakTable<ScrollBar, object?> _terminalScrollBarsApplied = new();

    private void SubscribeZmodem(TerminalTab tab, ZmodemStream? zmodem)
    {
        if (zmodem is null)
        {
            return;
        }

        _zmodemStreams[tab] = zmodem;
        zmodem.SendRequested += request =>
            Dispatcher.UIThread.Post(() =>
            {
                tab.IsZmodemActive = true;
                tab.IsZmodemIndeterminate = true;
                tab.IsZmodemCancellable = true;
                tab.ZmodemLabel = "Select files to send...";
                tab.ZmodemProgress = 0;
                _ = PromptZmodemSendAsync(tab, zmodem);
            });
        zmodem.Progress += progress => Dispatcher.UIThread.Post(() =>
        {
            var percent = progress.Total is > 0
                ? (int)Math.Clamp(progress.Sent * 100 / progress.Total.Value, 0, 100)
                : 0;
            if (!_zmodemTimings.TryGetValue(tab, out var timing)
                || !string.Equals(timing.Name, progress.Name, StringComparison.Ordinal))
            {
                timing = new ZmodemTiming(progress.Name, DateTime.UtcNow, progress.Sent);
                _zmodemTimings[tab] = timing;
            }

            var elapsed = Math.Max(
                0.001,
                (DateTime.UtcNow - timing.StartedAt).TotalSeconds);
            var rate = Math.Max(0, progress.Sent - timing.StartBytes) / elapsed;
            var detail = rate > 0 ? $" {FormatRate(rate)}" : "";
            if (progress.Total is > 0 && rate > 0 && progress.Total.Value > progress.Sent)
            {
                detail += $" ETA {FormatEta((progress.Total.Value - progress.Sent) / rate)}";
            }

            tab.IsZmodemActive = true;
            tab.IsZmodemIndeterminate = progress.Total is not > 0;
            tab.IsZmodemCancellable = true;
            tab.ZmodemLabel = progress.Name;
            tab.ZmodemProgress = percent;
            tab.StatusMessage =
                $"{(progress.IsSend ? "Sending" : "Receiving")} {progress.Name} {percent}%{detail}";
            tab.StatusBrush = StatusProgressBrush;
        });
        zmodem.TransferCompleted += result =>
            Dispatcher.UIThread.Post(() => { _ = CompleteZmodemAsync(tab, result); });
        zmodem.TransferFailed += message => Dispatcher.UIThread.Post(() =>
        {
            _zmodemTimings.Remove(tab);
            tab.IsZmodemActive = false;
            tab.StatusMessage = $"ZMODEM error: {message}";
            tab.StatusBrush = StatusErrorBrush;
        });
        zmodem.TransferCancelled += () => Dispatcher.UIThread.Post(() =>
        {
            _zmodemTimings.Remove(tab);
            tab.IsZmodemActive = false;
            tab.StatusMessage = "File transfer cancelled";
            tab.StatusBrush = StatusWarningBrush;
        });
    }

    private async Task PromptZmodemSendAsync(TerminalTab tab, ZmodemStream zmodem)
    {
        if (!tab.IsAlive)
        {
            zmodem.CancelSend();
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files to send",
            AllowMultiple = true,
        });
        var paths = files
            .Select(file => file.Path.LocalPath)
            .Where(File.Exists)
            .ToArray();
        if (paths.Length == 0 || !tab.IsZmodemActive)
        {
            zmodem.CancelSend();
            tab.IsZmodemActive = false;
            tab.StatusMessage = "File transfer cancelled";
            tab.StatusBrush = StatusWarningBrush;
            return;
        }

        tab.IsZmodemActive = true;
        tab.IsZmodemIndeterminate = true;
        tab.IsZmodemCancellable = true;
        tab.ZmodemLabel = "Sending...";
        tab.ZmodemProgress = 0;
        tab.StatusMessage = "Sending...";
        tab.StatusBrush = StatusProgressBrush;
        zmodem.StartSend(paths);
    }

    private async Task CompleteZmodemAsync(TerminalTab tab, ZmodemTransferResult result)
    {
        _zmodemTimings.Remove(tab);
        tab.IsZmodemActive = false;
        if (result.IsSend)
        {
            tab.StatusMessage = "Files sent";
            tab.StatusBrush = StatusInfoBrush;
            return;
        }

        if (!tab.IsAlive)
        {
            TryDeleteDirectory(result.TempDirectory);
            return;
        }

        var saved = 0;
        foreach (var received in result.Files)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save received file",
                SuggestedFileName = received.Name,
                DefaultExtension = Path.GetExtension(received.Name).TrimStart('.'),
            });
            if (file is null)
            {
                TryDeleteFile(received.Path);
                continue;
            }

            try
            {
                var target = file.Path.LocalPath;
                File.Move(received.Path, target, overwrite: true);
                saved++;
            }
            catch (Exception ex)
            {
                tab.StatusMessage = $"Save failed: {ex.Message}";
                tab.StatusBrush = StatusErrorBrush;
            }
        }

        TryDeleteDirectory(result.TempDirectory);
        tab.StatusMessage = $"Received {saved} file(s)";
        tab.StatusBrush = StatusInfoBrush;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }

    private void AddLocalTerminal()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var terminal = CreateTerminal();
        var tab = new TerminalTab
        {
            Title = "terminal",
            SessionId = $"terminal:{Guid.NewGuid():N}",
            Kind = SessionKind.Local,
            Content = terminal,
            Terminal = terminal,
            StatusMessage = "Connected terminal",
            CloseAction = () => terminal.Kill(),
        };
        var launched = false;
        terminal.AttachedToVisualTree += (_, _) =>
        {
            if (launched || !tab.IsAlive)
            {
                return;
            }

            launched = true;
            try
            {
                terminal.LaunchProcess();
                FocusTerminalWhenSelected(tab, terminal);
            }
            catch (Exception ex)
            {
                launched = false;
                tab.IsAlive = false;
                _ = ShowErrorAsync($"终端启动失败：{ex.Message}");
            }
        };

        viewModel.AddTab(tab);
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: TerminalTab tab } && DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedTab = tab;
            // The content host swaps its child during the selection change.
            // Focusing immediately can run before the new terminal is in the
            // visual tree, leaving focus on the tab strip or the window.
            // An SSH tab's content is the session view, so the terminal is
            // taken from the tab instead of the content.
            if ((tab.Terminal ?? tab.Content as TerminalControl) is { } terminal)
            {
                FocusTerminalWhenSelected(tab, terminal);
            }

            // r67：记录拖动换位候选。关闭按钮上的按下不进入拖动流程；
            // 是否真正开拖由 TabStripGrid 的 PointerMoved 按 5px 阈值判定，
            // 因此单击选中和双击脱离窗口均不受影响。
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && e.Source is Control src
                && src.FindAncestorOfType<Button>(includeSelf: true) is null
                && this.FindControl<Grid>("TabStripGrid") is { } strip)
            {
                _tabDragCandidate = tab;
                _tabDragTab = null;
                _tabDragPointerId = e.Pointer.Id;
                _tabDragStart = e.GetPosition(strip);
            }
        }
    }

    // ---- r67：标签拖动换位（实时换位，MobaXterm/Chrome 手感）----
    // 捕获挂在标签条容器（稳定元素）上而不是被拖的 Border 上：
    // ObservableCollection.Move 会重排 ItemsControl 容器，若捕获在被拖
    // 标签自身上，容器重建会丢失捕获。

    private void OnTabStripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Grid strip || e.Pointer?.Id != _tabDragPointerId)
        {
            return;
        }

        if (_tabDragCandidate is not { } tab)
        {
            return;
        }

        if (DataContext is not MainViewModel viewModel || !viewModel.Tabs.Contains(tab))
        {
            _tabDragCandidate = null;
            return;
        }

        // 松开后残留的候选要清理（例如按下后在标签条外松开，Released 不会冒泡到这里）。
        if (!e.GetCurrentPoint(strip).Properties.IsLeftButtonPressed)
        {
            _tabDragCandidate = null;
            return;
        }

        var pos = e.GetPosition(strip);
        if (!ReferenceEquals(_tabDragTab, tab))
        {
            // 未开拖：先过阈值，避免普通点击误触发。
            if (Math.Abs(pos.X - _tabDragStart.X) < TabDragThreshold
                && Math.Abs(pos.Y - _tabDragStart.Y) < TabDragThreshold)
            {
                return;
            }

            _tabDragTab = tab;
            // 捕获挂在稳定容器上：ObservableCollection.Move 会重排标签条目，
            // 若捕获在被拖 Border 自身，容器重建会丢捕获。
            e.Pointer.Capture(strip);
            _tabDragPointer = e.Pointer;
            Cursor = WestEastCursor;
        }

        // r67.1：MobaXterm 式换位动画。
        // ① 先量「当前视觉中线」（含正在滑动的位移）作为起点，再清掉所有
        //    RenderTransform 回到布局位；
        // ② 用视觉中线算目标索引（语义不变：越过相邻中线即插入其前）；
        // ③ 换位后其余标签从「视觉位置」滑向「新布局槽位」（120ms ease-out），
        //    连续换位时从当前滑动位置续接，不跳变；
        // ④ 被拖标签不走计时器：直接设位移让标签中心停在指针 X 下（跟手零延迟）。
        var visualOld = MeasureTabCenters(strip);
        ClearTabRenderTransforms(strip);

        var otherCenters = visualOld
            .Where(kv => !ReferenceEquals(kv.Key, tab))
            .Select(kv => kv.Value)
            .OrderBy(x => x)
            .ToList();
        var target = TabReorderIndex.Resolve(pos.X, otherCenters);

        var layoutOld = MeasureTabCenters(strip);
        var layoutNew = layoutOld;
        if (target != viewModel.Tabs.IndexOf(tab))
        {
            viewModel.MoveTab(tab, target);
            strip.UpdateLayout();
            layoutNew = MeasureTabCenters(strip);
        }

        // r67.2：MoveTab 重排会重建 ItemsControl 容器（旧容器上的 dragging 样式与
        // Z 序随之丢弃），必须在重排完成后对新容器重新施加：
        // ① dragging 半透明提示重新生效；
        // ② 提升被拖标签 Z 序，修复「移动过去就消失」——实时换位后树序可能把
        //    被拖标签排在被它视觉覆盖的相邻标签之前（典型：向左拖），默认按树序
        //    渲染时跟手标签会被邻居整块盖住；置顶后跟手标签恒在最上层
        //    （MobaXterm 同行为）。每次移动都重申，防止后续重排再次丢失。
        SetTabDragClass(strip, tab, true);
        RaiseTabDragZIndex(strip, tab);

        foreach (var kv in layoutNew)
        {
            if (ReferenceEquals(kv.Key, tab))
            {
                continue;
            }

            // r67.2：换位瞬态中若某标签容器短暂脱离视觉树（量不到），不能
            // 从 0 造一个巨大偏移，否则该标签会被瞬移到屏外再滑回来。
            if (!visualOld.TryGetValue(kv.Key, out var visX))
            {
                _tabSlides.Remove(kv.Key);
                continue;
            }

            var fromX = visX - kv.Value;
            if (Math.Abs(fromX) > 0.5)
            {
                // 同步落初始位移（不等计时器首个 tick），随后计时器接管推进。
                if (FindTabBorderInStrip(strip, kv.Key) is { } slideBorder)
                {
                    slideBorder.RenderTransform = new TranslateTransform { X = fromX };
                }

                _tabSlides[kv.Key] = new TabSlide { FromX = fromX, Start = DateTime.UtcNow };
            }
            else
            {
                _tabSlides.Remove(kv.Key);
            }
        }

        if (_tabSlides.Count > 0)
        {
            EnsureTabSlideTimer();
        }

        if (layoutNew.TryGetValue(tab, out var dragCenter))
        {
            var dragBorder = FindTabBorderInStrip(strip, tab);
            if (dragBorder is not null)
            {
                dragBorder.RenderTransform = new TranslateTransform { X = pos.X - dragCenter };
            }
        }
    }

    /// <summary>量各标签在标签条坐标系中的视觉中线（含 RenderTransform 位移）。</summary>
    private static Dictionary<TerminalTab, double> MeasureTabCenters(Grid strip)
    {
        var map = new Dictionary<TerminalTab, double>();
        foreach (var border in strip.GetVisualDescendants().OfType<Border>())
        {
            if (border.Tag is not TerminalTab tab)
            {
                continue;
            }

            var transform = border.TransformToVisual(strip);
            if (transform.HasValue)
            {
                map[tab] = transform.Value.M31 + border.Bounds.Width / 2;
            }
        }

        return map;
    }

    private static void ClearTabRenderTransforms(Grid strip)
    {
        foreach (var border in strip.GetVisualDescendants().OfType<Border>())
        {
            if (border.Tag is TerminalTab && border.RenderTransform != null)
            {
                border.RenderTransform = null;
            }
        }
    }

    private static Border? FindTabBorderInStrip(Grid strip, TerminalTab tab) =>
        strip.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => ReferenceEquals(b.Tag, tab));

    private void EnsureTabSlideTimer()
    {
        if (_tabSlideTimer is not null)
        {
            // r67.3：动画全部落位后计时器只是 Stop（对象仍存活）。只判 null 就返回
            // 会让此后的每次拖动都不再推进——松手位移永远清不掉，被拖标签就停在
            // 屏外的偏移上（被标签条裁剪后看起来「整条标签消失」）。停用则重启。
            if (!_tabSlideTimer.IsEnabled)
            {
                _tabSlideTimer.Start();
            }

            return;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += OnTabSlideTimerTick;
        _tabSlideTimer = timer;
        timer.Start();
    }

    private void OnTabSlideTimerTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var strip = this.FindControl<Grid>("TabStripGrid");
        foreach (var kv in _tabSlides.ToList())
        {
            var slide = kv.Value;
            var border = FindTabBorderInStrip(strip, kv.Key);
            if (border is null || border.Parent is null)
            {
                // 标签已在动画中途被关闭/脱离：静默丢弃。
                _tabSlides.Remove(kv.Key);
                continue;
            }

            var p = (now - slide.Start).TotalMilliseconds / TabSlideDurationMs;
            if (p >= 1)
            {
                border.RenderTransform = null;
                _tabSlides.Remove(kv.Key);
            }
            else
            {
                var eased = 1 - Math.Pow(1 - p, 3);
                border.RenderTransform = new TranslateTransform { X = slide.FromX * (1 - eased) };
            }
        }

        if (_tabSlides.Count == 0)
        {
            _tabSlideTimer?.Stop();
        }
    }

    private void OnTabStripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndTabDrag(sender as Grid, e.Pointer?.Id, releaseCapture: true);
    }

    private void OnTabStripPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndTabDrag(sender as Grid, e.Pointer?.Id, releaseCapture: false);
    }

    private void EndTabDrag(Grid? strip, int? pointerId, bool releaseCapture)
    {
        if (pointerId != _tabDragPointerId)
        {
            return;
        }

        if (_tabDragTab is not null && strip is not null)
        {
            SetTabDragClass(strip, _tabDragTab, false);

            // r67.2：恢复全部标签 Z 序（含被拖标签），避免残留置顶。
            ResetTabDragZIndexes(strip);

            // r67.1：松手时被拖标签从当前跟手偏移滑回槽位（与其余标签同款 120ms）。
            var dragBorder = FindTabBorderInStrip(strip, _tabDragTab);
            if (dragBorder?.RenderTransform is TranslateTransform tt && Math.Abs(tt.X) > 0.5)
            {
                _tabSlides[_tabDragTab] = new TabSlide { FromX = tt.X, Start = DateTime.UtcNow };
            }
        }

        if (_tabSlides.Count > 0)
        {
            EnsureTabSlideTimer();
        }

        if (releaseCapture)
        {
            // 正常情况下 pointer-up 会自动释放捕获；这里显式兜底，
            // 覆盖「按下后在标签条外松开」这类 Released 不冒泡回来的路径。
            _tabDragPointer?.Capture(null);
            _tabDragPointer = null;
        }

        Cursor = null;
        _tabDragTab = null;
        _tabDragCandidate = null;
        _tabDragPointerId = -1;
    }

    private static void SetTabDragClass(Grid strip, TerminalTab tab, bool on)
    {
        var border = strip.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => ReferenceEquals(b.Tag, tab));
        border?.Classes.Set("dragging", on);
    }

    // r67.2：拖动中被拖标签置顶，防止被实时换位后的相邻标签按树序盖住。
    // 实时换位只改 ObservableCollection 顺序，ItemsControl 重排容器后，被拖标签
    // 可能排在邻居之前；当它视觉上与邻居重叠（跟手位移指向邻居一侧）时，默认
    // 按树序渲染会被邻居整块盖住 →「移动过去就消失」。提升 Z 序后跟手标签恒在
    // 最上层，落位动画期间也可见。
    // 注意：Avalonia 12 中 ZIndex 是 Visual 实例属性（Panel.SetZIndex 已移除），
    // 且只有 Panel 的直接子节点参与 Z 序排序 → 设值对象是标签条 StackPanel 的
    // 直接子节点（标签容器），而不是 Border 本身。
    private const int TabDragZIndex = 100;

    /// <summary>沿视觉祖先向上找第一个「父节点是 Panel」的节点（即该 Panel 的直接子节点）。</summary>
    private static Visual? FindPanelChildAncestor(Visual v)
    {
        var current = v;
        while (current is not null)
        {
            var parent = current.GetVisualParent();
            if (parent is Panel)
            {
                return current;
            }

            current = parent;
        }

        return null;
    }

    private static void RaiseTabDragZIndex(Grid strip, TerminalTab tab)
    {
        var border = FindTabBorderInStrip(strip, tab);
        if (border is not null && FindPanelChildAncestor(border) is Visual container)
        {
            container.ZIndex = TabDragZIndex;
        }
    }

    private static void ResetTabDragZIndexes(Grid strip)
    {
        foreach (var border in strip.GetVisualDescendants().OfType<Border>())
        {
            if (border.Tag is not TerminalTab)
            {
                continue;
            }

            if (FindPanelChildAncestor(border) is { ZIndex: not 0 } container)
            {
                container.ZIndex = 0;
            }
        }
    }

    private static void OnSelectedTabChanging(TerminalTab? tab)
    {
        if (tab is not null
            && (tab.Terminal ?? tab.Content as TerminalControl) is { } terminal)
        {
            // Tab switching detaches the previous content control. Keep its PTY
            // reader alive until it is selected and attached again.
            terminal.BeginReparent();
        }
    }

    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TerminalTab tab })
        {
            e.Handled = true;
            CloseTab(tab);
        }
    }

    /// <summary>
    /// r67.6：串口标签右键「Reconnect now」。绕开退避立即重开一次，
    /// 让用户不必等 2s/5s 轮询，也给「幽灵端口」一个手动出口。
    /// </summary>
    private void OnTabReconnectClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: TerminalTab tab })
        {
            e.Handled = true;
            ReconnectSerialNow(tab);
        }
    }

    internal void ReconnectSerialNowForTests(TerminalTab tab) => ReconnectSerialNow(tab);

    private void ReconnectSerialNow(TerminalTab tab)
    {
        if (!_serialTabs.TryGetValue(tab, out var state) || state.Closed)
        {
            return;
        }

        if (!state.AwaitingReconnect)
        {
            MarkSerialDisconnected(state);
        }

        state.NextReconnectAt = DateTime.MinValue;
        TryReconnectSerial(state);
        UpdateConnectionStates();
    }

    private void OnTabDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { Tag: TerminalTab tab }
            || DataContext is not MainViewModel viewModel
            || viewModel.Tabs.IndexOf(tab) < 0)
        {
            return;
        }

        if (!tab.IsAlive
            || (e.Source is Control source
                && source.FindAncestorOfType<Button>(includeSelf: true) is not null))
        {
            return;
        }

        // Removing the control from the visual tree must not be treated as the
        // end of its process. The terminal library has an explicit reparent
        // handshake for exactly this case.
        tab.Terminal?.BeginReparent();
        var index = viewModel.RemoveTabForDetach(tab);
        if (index < 0)
        {
            tab.Terminal?.EndReparent();
            return;
        }

        e.Handled = true;
        Dispatcher.UIThread.Post(
            () => ShowDetachedTab(tab, index),
            DispatcherPriority.Loaded);
    }

    private void ShowDetachedTab(TerminalTab tab, int index)
    {
        if (!tab.IsAlive)
        {
            // The connection can drop between the double-click and this post.
            // Keep the tab in the strip instead of losing it.
            tab.Terminal?.EndReparent();
            if (DataContext is MainViewModel deadViewModel)
            {
                deadViewModel.InsertDetachedTab(tab, index);
            }

            return;
        }

        Window? window = null;
        try
        {
            window = new Window
            {
                Title = tab.Title,
                Width = 850,
                Height = 500,
                MinWidth = 500,
                MinHeight = 320,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Background,
                FontFamily = FontFamily,
                FontSize = FontSize,
                Icon = Icon,
                Content = tab.Content,
            };
            var state = new DetachedTabState(window, index);
            _detachedTabs[tab] = state;
            // Closing fires while the terminal is still in this window's visual
            // tree, before Avalonia detaches it. Suppress the terminal library's
            // implicit process cleanup for the trip back to the main window.
            window.Closing += (_, _) => tab.Terminal?.BeginReparent();
            window.Closed += (_, _) => OnDetachedTabClosed(tab, state);
            window.Show();
            // Multiple detached windows otherwise land exactly on top of each
            // other, hiding every window but the last one.
            var cascade = Math.Max(0, _detachedTabs.Count - 1);
            if (cascade > 0)
            {
                const int offset = 24;
                window.Position = new PixelPoint(
                    window.Position.X + (offset * cascade),
                    window.Position.Y + (offset * cascade));
            }

            tab.Terminal?.EndReparent();
            (tab.Terminal ?? (Control)tab.Content).Focus();
        }
        catch (Exception ex)
        {
            _detachedTabs.Remove(tab);
            if (window is not null)
            {
                window.Content = null;
                window.Close();
            }

            tab.Terminal?.EndReparent();
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.InsertDetachedTab(tab, index);
            }

            _ = ShowErrorAsync($"标签分离失败：{ex.Message}");
        }
    }

    private void OnDetachedTabClosed(TerminalTab tab, DetachedTabState state)
    {
        if (!_detachedTabs.Remove(tab))
        {
            return;
        }

        if (state.SuppressReattach || !tab.IsAlive)
        {
            DisposeDetachedTab(tab);
            return;
        }

        tab.Terminal?.BeginReparent();
        state.Window.Content = null;
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.InsertDetachedTab(tab, state.Index);
            tab.Terminal?.EndReparent();
            (tab.Terminal ?? (Control)tab.Content).Focus();
        }

        UpdateConnectionStates();
    }

    private void DisposeDetachedTab(TerminalTab tab)
    {
        try
        {
            tab.CloseAction?.Invoke();
        }
        catch
        {
            // The transport may already be gone with its detached window.
        }

        StopRecordingForTabIfAny(tab);
        ReleaseHighlighterForTab(tab);
        _sshRestarts.Remove(tab);
        tab.IsAlive = false;
        tab.IsZmodemActive = false;
        _zmodemTimings.Remove(tab);
        _zmodemStreams.Remove(tab);
    }

    private TerminalTab? FindDetachedTab(SessionItem item)
    {
        foreach (var tab in _detachedTabs.Keys)
        {
            if (tab.Kind == item.Kind
                && string.Equals(tab.SessionId, item.SessionId, StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }

        return null;
    }

    private IEnumerable<TerminalTab> AllTabs()
    {
        if (DataContext is MainViewModel viewModel)
        {
            foreach (var tab in viewModel.Tabs)
            {
                yield return tab;
            }
        }

        foreach (var tab in _detachedTabs.Keys)
        {
            yield return tab;
        }
    }

    // ---- Remote monitoring: status bar right-click menu (MobaXterm 13 items) ----

    private void OnMonitorMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        // Checkable items carry their option tag; sync the check state from
        // the view model each time the menu opens.
        foreach (var item in menu.Items)
        {
            if (item is MenuItem mi && mi.Tag is string tag)
            {
                mi.IsChecked = viewModel.MonitorMenu.IsOption(tag);
            }
        }
    }

    private void OnMonitorOptionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } item
            || DataContext is not MainViewModel viewModel || _config is null)
        {
            return;
        }

        viewModel.MonitorMenu.SetOption(tag, item.IsChecked);
        viewModel.ReformatMonitorLine();
        viewModel.MonitorMenu.SaveTo(_config.Config.RemoteMonitor);
        _config.Save();
    }

    private void OnMonitorCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || string.IsNullOrEmpty(viewModel.MonitorText))
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(viewModel.MonitorText));
            _ = topLevel.Clipboard.SetDataAsync(data);
        }
    }

    private async void OnMonitorSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || string.IsNullOrEmpty(viewModel.MonitorText))
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save remote monitor status",
            SuggestedFileName = $"remote-monitor-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            DefaultExtension = "txt",
        });
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek)
        {
            stream.SetLength(0);
        }

        await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(viewModel.MonitorText));
    }

    private void CloseTab(TerminalTab tab)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            tab.CloseAction?.Invoke();
        }
        catch
        {
            // Closing a half-open transport must not keep the tab alive.
        }

        _sshRestarts.Remove(tab);
        tab.IsAlive = false;
        tab.IsZmodemActive = false;
        _zmodemTimings.Remove(tab);
        _zmodemStreams.Remove(tab);

        // Recording on this terminal stops with the tab: keep the captured
        // steps and ask for a name (MobaXterm behavior on disconnect).
        if (viewModel.ActiveRecording is { Target: var recTarget }
            && ReferenceEquals(recTarget, tab.Terminal))
        {
            var recSteps = viewModel.StopRecording();
            UpdateMacroBanner();
            if (recSteps is not null)
            {
                _ = SaveRecordedAsync(recSteps);
            }
        }

        // A macro executing on this terminal is cancelled; the runner's
        // finally-block resets IsExecutingMacro and the banner follows.
        viewModel.CancelIfExecutingOn(tab.Terminal);

        StopRecordingForTabIfAny(tab);
        ReleaseHighlighterForTab(tab);

        viewModel.CloseTab(tab);
        UpdateConnectionStates();
        RestoreTerminalFocus();
    }

    private void OnZmodemCancelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || viewModel.SelectedTab is not { } tab
            || !_zmodemStreams.TryGetValue(tab, out var zmodem))
        {
            return;
        }

        tab.IsZmodemCancellable = false;
        tab.ZmodemLabel = "Cancelling...";
        zmodem.CancelSend();
        zmodem.CancelTransfer();
    }

    private async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            Height = 190,
            MinWidth = 320,
            MinHeight = 160,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var yes = new Button
        {
            Content = "Yes",
            Width = 80,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        yes.Click += (_, _) => dialog.Close(true);
        var no = new Button
        {
            Content = "No",
            Width = 80,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        no.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { no, yes },
                },
            },
        };
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task ShowInfoAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            Height = 190,
            MinWidth = 320,
            MinHeight = 160,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                },
                ok,
            },
        };
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Asks the user whether to keep retrying after a failed restart.
    /// Returns true when "继续重启" is chosen, false for "不重连" or closing.
    /// </summary>
    internal async Task<bool> ShowRestartAskAsync(Window owner, string reason)
    {
        var dialog = new Window
        {
            Title = "重连失败",
            Width = 460,
            Height = 200,
            MinWidth = 320,
            MinHeight = 160,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var retry = new Button
        {
            Content = "继续重启",
            Width = 96,
            MinWidth = 0,
            MinHeight = 0,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        retry.Click += (_, _) =>
        {
            dialog.Tag = true;
            dialog.Close();
        };
        var giveUp = new Button
        {
            Content = "不重连",
            Width = 96,
            MinWidth = 0,
            MinHeight = 0,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        giveUp.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"重连失败：{reason}\n是否继续重启？",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { retry, giveUp },
                },
            },
        };
        LastRestartDialogForTests = dialog;
        try
        {
            await dialog.ShowDialog(owner);
        }
        finally
        {
            if (ReferenceEquals(LastRestartDialogForTests, dialog))
            {
                LastRestartDialogForTests = null;
            }
        }

        return Equals(dialog.Tag, true);
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = CreateDialog(440, 190);
        var ok = new Button
        {
            Content = DialogButtonContent("确定", "\uf00c", "#16a34a"),
            Width = 96,
            Height = 30,
        };
        ok.IsDefault = true;
        ok.IsCancel = true;
        ok.Click += (_, _) => dialog.Close();
        var icon = new TextBlock
        {
            Text = "\uf05e",
            FontFamily = IconFont(),
            FontSize = 28,
            Foreground = new SolidColorBrush(Color.Parse("#dc2626")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 14, 0),
        };
        var text = new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var buttons = BottomButtonRow(ok);
        var grid = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(buttons);
        Grid.SetColumn(icon, 0);
        Grid.SetRowSpan(icon, 2);
        Grid.SetColumn(text, 1);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        dialog.Content = grid;
        LastErrorDialogForTests = dialog;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            if (ReferenceEquals(LastErrorDialogForTests, dialog))
            {
                LastErrorDialogForTests = null;
            }
        }
    }

    internal Window? LastErrorDialogForTests { get; private set; }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false)
        {
            return;
        }

        if (IsTitleBarButtonSource(e.Source))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsTitleBarButtonSource(e.Source))
        {
            return;
        }

        ToggleMaximize();
        e.Handled = true;
    }

    private static bool IsTitleBarButtonSource(object? source) =>
        source is Control control
        && (control.FindAncestorOfType<Button>(includeSelf: true) is not null
            || control.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null);

    private void OnPinClick(object? sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinIcon.Text = Topmost ? "\uf9f7" : "\uf670";
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            // 1) Ctrl+Space popup open: the nav keys are consumed by the popup.
            if (MacroPopup.IsVisible)
            {
                switch (e.Key)
                {
                    case Key.Up:
                        MoveMacroPopupSelection(-1);
                        e.Handled = true;
                        return;
                    case Key.Down:
                        MoveMacroPopupSelection(1);
                        e.Handled = true;
                        return;
                    case Key.Enter:
                        ExecuteMacroPopupSelection();
                        e.Handled = true;
                        return;
                    case Key.Escape:
                        HideMacroPopup();
                        e.Handled = true;
                        return;
                }
            }

            if (viewModel.IsRecording)
            {
                // 2) Recording: keys inside the recording target's subtree are
                //    fed to the recorder but NOT consumed, so the terminal
                //    still receives them. Special keys -> FeedKey; printable
                //    characters -> FeedText (derived from Key+Shift, US layout):
                //    real keyboards never raise TextInputEvent for terminal
                //    input (the terminal control consumes KeyDown itself), so
                //    text MUST be captured here; IME-composed text still
                //    arrives via OnRecordingTextInput, deduped against this
                //    path. Ctrl/Alt combos produce control sequences, not
                //    text, so they are not recorded (known MobaXterm deviation).
                if (viewModel.ActiveRecording is { } recording
                    && e.Source is Visual recordingSource
                    && IsInSubtree(recordingSource, recording.Target))
                {
                    if (MapKeyToMacroName(e.Key) is { } macroKeyName)
                    {
                        recording.FeedKey(macroKeyName);
                    }
                    else if (MapKeyToChar(e.Key, e.KeyModifiers) is { } ch)
                    {
                        _lastKeyFeedChar = ch[0];
                        _lastKeyFeedTick = Environment.TickCount;
                        recording.FeedText(ch);
                    }
                }
            }
            else if (!viewModel.IsExecutingMacro)
            {
                // 0) 查找条打开且焦点在其内：Enter=下一个、Shift+Enter=上一个、Esc 关闭；
                //    其余按键直接交给输入框，且不得误触发宏热键。
                if (SearchBar.IsVisible
                    && e.Source is Visual searchSource
                    && IsInSubtree(searchSource, SearchBar))
                {
                    if (e.Key == Key.Escape)
                    {
                        HideSearchBar();
                        e.Handled = true;
                        return;
                    }

                    if (e.Key == Key.Enter)
                    {
                        MoveSearch(!e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                        e.Handled = true;
                        return;
                    }

                    return;
                }

                // 5) Ctrl+Shift+F：打开/聚焦终端查找条（MobaXterm 同快捷键）。
                if (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
                {
                    ShowTerminalSearchBar();
                    e.Handled = true;
                    return;
                }

                // 3) Ctrl+Space toggles the macro popup (MobaXterm behavior).
                if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
                {
                    if (MacroPopup.IsVisible)
                    {
                        HideMacroPopup();
                    }
                    else
                    {
                        ShowMacroPopup();
                    }
                    e.Handled = true;
                    return;
                }

                // 4) Per-macro hotkeys (window-level, not system-wide).
                if (viewModel.TryMatchHotkey(e.Key, e.KeyModifiers, out var hotMacro)
                    && hotMacro is not null)
                {
                    _ = viewModel.ExecuteMacroAsync(hotMacro);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.Key != Key.F11)
        {
            return;
        }

        // Python's title-bar mixin treats F11 as a full-screen toggle and
        // hides the title bar while full screen is active.
        ToggleFullScreen();
        e.Handled = true;
    }

    /// <summary>
    /// Tunnel-level text input: while recording, printable text typed into
    /// the recording target's terminal is fed to the recorder. The event is
    /// NOT marked handled, so the terminal/pty still receives it.
    /// </summary>
    private void OnRecordingTextInput(object? sender, TextInputEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || viewModel.ActiveRecording is not { } recording
            || e.Source is not Visual source
            || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        // 去重：同一物理按键在部分平台（如 Win32 的 WM_CHAR）会先后触发
        // KeyDown 和 TextInput 两次；KeyDown 路径 100ms 内已喂过同一字符时
        // 丢弃，防止双录。IME 组合文本（多字符或与上次不同）不受影响。
        if (e.Text.Length == 1
            && e.Text[0] == _lastKeyFeedChar
            && (Environment.TickCount - _lastKeyFeedTick) < 100)
        {
            return;
        }

        if (IsInSubtree(source, recording.Target))
        {
            recording.FeedText(e.Text);
        }
    }

    /// <summary>
    /// 录制用：按键 → 可打印字符（US 布局，Shift 映射大写/符号）。
    /// 真实键盘在终端控件上敲字符不会触发 TextInputEvent（控件自己消费
    /// KeyDown 送 pty），所以文本必须在 KeyDown 路径派生捕获，保证
    /// "终端收到什么，宏就录什么"。Ctrl/Alt 组合产生控制序列而非文本，
    /// 不记录（已知偏差）。非 US 布局的标点位置会录出 US 字符（已知偏差）。
    /// </summary>
    private static string? MapKeyToChar(Key key, KeyModifiers mods)
    {
        if ((mods & ~KeyModifiers.Shift) != KeyModifiers.None)
        {
            return null;
        }

        var shift = (mods & KeyModifiers.Shift) != 0;
        char c;

        if (key >= Key.A && key <= Key.Z)
        {
            c = (char)((shift ? 'A' : 'a') + (int)(key - Key.A));
            return c.ToString();
        }
        if (key >= Key.D0 && key <= Key.D9)
        {
            var i = (int)(key - Key.D0);
            return shift ? "!\"#$%&'()".Substring(i, 1) : ((char)('0' + i)).ToString();
        }
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return ((char)('0' + (int)(key - Key.NumPad0))).ToString();
        }
        if (key == Key.Space)
        {
            return " ";
        }

        // US 布局标点（Avalonia 12.1.4 Key 值：OemSemicolon=140..OemBackslash=154）
        switch (key)
        {
            case Key.OemSemicolon: c = shift ? ':' : ';'; break;
            case Key.OemPlus: c = shift ? '*' : '+'; break;
            case Key.OemComma: c = shift ? '<' : ','; break;
            case Key.OemMinus: c = shift ? '_' : '-'; break;
            case Key.OemPeriod: c = shift ? '>' : '.'; break;
            case Key.OemQuestion: c = shift ? '?' : '/'; break;
            case Key.OemTilde: c = shift ? '~' : '`'; break;
            case Key.OemOpenBrackets: c = shift ? '{' : '['; break;
            case Key.OemPipe: c = shift ? '|' : '\\'; break;
            case Key.OemCloseBrackets: c = shift ? '}' : ']'; break;
            case Key.OemQuotes: c = shift ? '"' : '\''; break;
            default: return null;
        }
        return c.ToString();
    }

    /// <summary>Maps an Avalonia key to a <see cref="COMTool.Core.Macros.MacroKeys"/> name, or null.</summary>
    private static string? MapKeyToMacroName(Key key) => key switch
    {
        Key.Enter => "RETURN",
        Key.Tab => "TAB",
        Key.Back => "BACKSPACE",
        Key.Escape => "ESC",
        Key.Up => "UP",
        Key.Down => "DOWN",
        Key.Left => "LEFT",
        Key.Right => "RIGHT",
        Key.Home => "HOME",
        Key.End => "END",
        Key.PageUp => "PAGEUP",
        Key.PageDown => "PAGEDOWN",
        Key.Insert => "INSERT",
        Key.Delete => "DELETE",
        >= Key.F1 and <= Key.F12 => $"F{(int)key - (int)Key.F1 + 1}",
        _ => null,
    };

    /// <summary>True when <paramref name="source"/> is <paramref name="root"/> or inside it.</summary>
    private static bool IsInSubtree(Visual source, Visual root)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, root))
            {
                return true;
            }
        }

        return false;
    }

    private void OnLightThemeClick(object? sender, RoutedEventArgs e) =>
        ApplyTheme("light");

    private void OnDarkThemeClick(object? sender, RoutedEventArgs e) =>
        ApplyTheme("dark");

    private async void OnAboutClick(object? sender, RoutedEventArgs e) =>
        await ShowAboutAsync();

    private async Task ShowAboutAsync()
    {
        var dialog = new Window
        {
            Title = "About COMTool",
            Width = 560,
            // 高度随内容自适应：固定 Height=430 时内容只占约 330px，
            // OK 按钮悬在窗体中部、下方留 ~120px 空白（实机反馈“OK 位置有问题”）。
            SizeToContent = Avalonia.Controls.SizeToContent.Height,
            MinWidth = 420,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var links = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        links.Children.Add(LinkButton("GitHub", "https://github.com/Neutree/COMTool"));
        links.Children.Add(LinkButton(
            "Releases",
            "https://github.com/Neutree/COMTool/releases"));
        links.Children.Add(LinkButton(
            "Issues",
            "https://github.com/Neutree/COMTool/issues"));
        links.Children.Add(LinkButton("LGPL-3.0", "https://github.com/Neutree/COMTool#license"));
        links.Children.Add(LinkButton("Donate", "https://neucrack.com/donate"));

        var ok = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "COMTool",
                    FontSize = 26,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#009688")),
                },
                new TextBlock
                {
                    Text = "v0.01",
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#009688")),
                },
                new TextBlock
                {
                    Text = "A cross-platform serial port and SSH terminal.\n"
                        + "Open source, created by Neucrack and contributors.\n"
                        + "Welcome to improve it together and add plugins.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Config path:\n" + (_config?.ConfigPath ?? "(not loaded)"),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8,
                },
                new TextBlock
                {
                    Text = "License: LGPL-3.0\n"
                        + "You can buy me half a cup of coffee if this software helps.",
                    TextWrapping = TextWrapping.Wrap,
                },
                links,
                ok,
            },
        };
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// 终端设置对话框：复刻 MobaXterm “Live terminal reconfiguration → Terminal”，
    /// 目前只含行尾仿真的两项（Implicit CR in every LF / Implicit LF in every CR）。
    /// 确定后立即对所有已连接终端生效（共享 LineEndingOptions），并持久化到 config.json。
    /// </summary>
    private async Task ShowTerminalSettingsAsync()
    {
        if (_config is null)
        {
            return;
        }

        var dialog = new Window
        {
            Title = "实时终端重配置",
            Width = 560,
            Height = 280,
            MinWidth = 420,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var implicitCrInLf = new CheckBox
        {
            Content = "每个 LF 隐式携带 CR（Implicit CR in every LF）",
            IsChecked = _lineEndingOptions.ImplicitCrInLf,
        };
        var implicitLfInCr = new CheckBox
        {
            Content = "每个 CR 隐式携带 LF（Implicit LF in every CR）",
            IsChecked = _lineEndingOptions.ImplicitLfInCr,
        };

        var ok = new Button
        {
            Content = "确定",
            Width = 80,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        ok.Click += (_, _) =>
        {
            _lineEndingOptions.ImplicitCrInLf = implicitCrInLf.IsChecked == true;
            _lineEndingOptions.ImplicitLfInCr = implicitLfInCr.IsChecked == true;
            _config!.Config.Terminal.ImplicitCrInLf = _lineEndingOptions.ImplicitCrInLf;
            _config!.Config.Terminal.ImplicitLfInCr = _lineEndingOptions.ImplicitLfInCr;
            _config.Save();
            AppLog.Info($"Terminal line endings: implicitCrInLf={_lineEndingOptions.ImplicitCrInLf}, " +
                $"implicitLfInCr={_lineEndingOptions.ImplicitLfInCr}");
            dialog.Close();
        };
        var cancel = new Button
        {
            Content = "取消",
            Width = 80,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "终端仿真选项",
                    FontSize = 14,
                    FontWeight = FontWeight.Bold,
                },
                implicitCrInLf,
                implicitLfInCr,
                new TextBlock
                {
                    Text = "隐式 CR：收到 LF 时先把光标移回行首再换行。\n"
                        + "隐式 LF：收到 CR 时移到行首后再额外换一行。\n"
                        + "默认均关闭（与 MobaXterm 一致），仅对接按老式行尾约定输出的设备时使用。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                    FontSize = 12,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { ok, cancel },
                },
            },
        };
        await dialog.ShowDialog(this);
    }

    private Button LinkButton(string text, string url)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 4),
        };
        button.Click += async (_, _) =>
        {
            try
            {
                await Launcher.LaunchUriAsync(new Uri(url));
            }
            catch
            {
                // No browser is available in some headless sessions.
            }
        };
        return button;
    }

    private void ApplyTheme(string skin)
    {
        if (_config is null)
        {
            return;
        }

        ThemeManager.Apply(skin);
        _config.Config.Skin = skin;
        _config.Save();
    }

    /// <summary>
    /// A rail icon switches to its panel. Clicking the icon of the panel
    /// that is already open collapses the panel while the rail stays on
    /// screen, so the same icon opens it again.
    /// </summary>
    private void OnRailTabRequested(object? sender, SidebarTab tab)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (_sidebarVisible && viewModel.SidebarTab == tab)
        {
            SetSidebarVisible(false);
            return;
        }

        viewModel.SidebarTab = tab;
        if (!_sidebarVisible)
        {
            SetSidebarVisible(true);
        }
    }

    /// <summary>
    /// The SSH-browser icon of the rail opens the SFTP browser of the
    /// current SSH session in the sidebar. When the current tab is not an
    /// SSH tab, the most recent connected SSH tab is selected first.
    /// Clicking the icon again while the browser is open collapses the
    /// panel and leaves the rail on screen.
    /// </summary>
    private void OnSshBrowserRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (_sidebarVisible && viewModel.IsSshBrowserTab)
        {
            SetSidebarVisible(false);
            return;
        }

        var tab = viewModel.SelectedTab;
        if (tab is null || !tab.IsSshTab || !IsTabConnected(tab))
        {
            tab = viewModel.Tabs.FirstOrDefault(
                candidate => candidate.IsSshTab && IsTabConnected(candidate));
        }

        if (tab?.SftpPane is null)
        {
            return;
        }

        OpenSshBrowserFor(tab);
    }

    /// <summary>
    /// MobaXterm parity (r54, AutoStartSSHGUI=1): a connected SSH session
    /// starts with its graphical SFTP browser open. Selects the tab,
    /// switches the sidebar to the browser page and shows it. No-op for
    /// non-SSH/disconnected tabs or tabs without a pane.
    /// </summary>
    internal void OpenSshBrowserFor(TerminalTab tab)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (tab is null || !tab.IsSshTab || !IsTabConnected(tab))
        {
            return;
        }

        if (tab.SftpPane is null)
        {
            return;
        }

        if (!ReferenceEquals(viewModel.SelectedTab, tab))
        {
            viewModel.SelectedTab = tab;
        }

        viewModel.SidebarTab = SidebarTab.SshBrowser;
        if (!_sidebarVisible)
        {
            SetSidebarVisible(true);
        }
    }

    private void RestoreTerminalFocus()
    {
        Dispatcher.UIThread.Post(FocusSelectedTerminal, DispatcherPriority.Input);
    }

    private void FocusSelectedTerminal()
    {
        if (DataContext is MainViewModel { SelectedTab.Content: { } content })
        {
            content.Focus();
        }
    }

    private void FocusTerminalWhenSelected(TerminalTab tab, TerminalControl terminal)
    {
        // Switching tabs detaches the old terminal and attaches the new one
        // during the content-host update. A dispatcher post alone can run
        // before that attach and then lose focus with the old control, so wait
        // for the selected terminal to enter the visual tree.
        void FocusIfStillSelected()
        {
            if (DataContext is MainViewModel viewModel
                && TerminalFocusPolicy.ShouldFocus(viewModel, tab))
            {
                terminal.EndReparent();
                terminal.Focus();
            }
        }

        if (terminal.IsAttachedToVisualTree())
        {
            Dispatcher.UIThread.Post(FocusIfStillSelected, DispatcherPriority.Loaded);
            return;
        }

        EventHandler<VisualTreeAttachmentEventArgs>? onAttached = null;
        onAttached = (_, _) =>
        {
            terminal.AttachedToVisualTree -= onAttached;
            Dispatcher.UIThread.Post(FocusIfStillSelected, DispatcherPriority.Loaded);
        };
        terminal.AttachedToVisualTree += onAttached;

        // Covers the case where the content host attaches synchronously before
        // the event subscription; the attach handler is then a no-op.
        Dispatcher.UIThread.Post(FocusIfStillSelected, DispatcherPriority.Loaded);
    }

    private void SetSidebarVisible(bool visible, bool persist = true)
    {
        _sidebarVisible = visible;
        ApplySidebarMode();

        if (persist)
        {
            PersistSidebarConfig();
        }
    }

    /// <summary>
    /// The sidebar becomes the SSH browser and back. The rail keeps its
    /// place, the content starts at the splitter while the browser is
    /// open, and the terminal gets the whole right side. When the panel
    /// is collapsed only the 40px rail remains; the content inset shrinks
    /// by the same amount, so the terminal keeps its absolute position.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSshBrowserTab))
        {
            ApplySidebarMode();
        }

        if (e.PropertyName == nameof(MainViewModel.IsRecording)
            || e.PropertyName == nameof(MainViewModel.IsExecutingMacro)
            || e.PropertyName == nameof(MainViewModel.ExecutingMacroName))
        {
            UpdateMacroBanner();
        }
    }

    private void ApplySidebarMode()
    {
        var vm = DataContext as MainViewModel;
        var browser = vm?.IsSshBrowserTab == true;

        // Rail icon highlights follow the actual open/collapsed state, not
        // just the selected tab (user feedback 2026-10-29).
        if (vm is not null)
        {
            vm.IsSidebarVisible = _sidebarVisible;
        }

        // The rail is always on screen: it is the only way to reopen the
        // panel after a collapse. The panel content simply loses its star
        // column when the dock is collapsed.
        Sidebar.IsVisible = true;
        BodyGrid.ColumnDefinitions[0].Width = new GridLength(
            _sidebarVisible ? CurrentSidebarWidth() : 40);
        SidebarSplitter.IsVisible = _sidebarVisible;
        BodyGrid.ColumnDefinitions[1].Width = new GridLength(_sidebarVisible ? 3 : 0);

        ContentArea.Margin = !_sidebarVisible
            ? new Thickness(8, 0, 0, 0)
            : browser
                ? new Thickness(0, 0, 0, 0)
                : new Thickness(32, 0, 0, 0);
    }

    private double CurrentSidebarWidth() =>
        (DataContext as MainViewModel)?.IsSshBrowserTab == true
            ? BrowserSidebarWidth()
            : ClampSidebarWidth(_sidebarWidth);

    private double BrowserSidebarWidth()
    {
        var available = Bounds.Width > 0 ? Bounds.Width : DefaultWindowWidth;
        var limit = Math.Max(300, available - 260);
        return Math.Clamp(
            _browserSidebarWidth,
            Math.Min(360, limit),
            Math.Min(720, limit));
    }

    internal void SetSidebarWidthForTests(double width)
    {
        if ((DataContext as MainViewModel)?.IsSshBrowserTab == true)
        {
            _browserSidebarWidth = width;
        }
        else
        {
            _sidebarWidth = ClampSidebarWidth(width);
        }

        ApplySidebarMode();
    }

    internal ConnectionSettingsWindow? SettingsWindowForTests => _settingsWindow;

    // Ctrl + mouse wheel resizes the terminal font like MobaXterm. The size is
    // global: every open terminal follows, and the value is written to
    // config.json (terminal.fontSize) so a restart keeps it.
    internal const double TerminalFontSizeMin = 8.0;
    internal const double TerminalFontSizeMax = 48.0;

    internal static double AdjustTerminalFontSize(double current, double deltaY)
    {
        if (double.Equals(deltaY, 0.0))
        {
            return current;
        }

        var next = current + (deltaY > 0 ? 1 : -1);
        if (next < TerminalFontSizeMin)
        {
            next = TerminalFontSizeMin;
        }
        else if (next > TerminalFontSizeMax)
        {
            next = TerminalFontSizeMax;
        }

        return next;
    }

    /// <summary>
    /// 配置里读出的字号先钳制到 Ctrl+滚轮 的可用区间；
    /// 非法值（0、NaN 等）回落到对齐 MobaXterm 的默认 14.5。
    /// </summary>
    internal static double ClampTerminalFontSize(double value)
    {
        // 0/NaN/∞ 视为无效（旧配置或手改坏），回到默认 14.5；
        // 其余值钳制到 Ctrl+滚轮 的可用区间。
        if (!double.IsFinite(value) || value <= 0)
        {
            return TerminalFontSize;
        }

        return Math.Clamp(value, TerminalFontSizeMin, TerminalFontSizeMax);
    }

    private void OnWindowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Source is not Visual source
            || source.FindAncestorOfType<TerminalControl>(includeSelf: true) is not TerminalControl terminal)
        {
            return;
        }

        var next = AdjustTerminalFontSize(terminal.FontSize, e.Delta.Y);
        if (next != terminal.FontSize)
        {
            // MobaXterm 的字号是全局设置：一次缩放应用到所有已打开的终端，
            // 并立即写入 config，重启后仍用这个字号。
            _terminalFontSize = next;
            ApplyTerminalFontSizeToAll(next);
            PersistTerminalFontSize(next);
        }

        e.Handled = true;
    }

    private void ApplyTerminalFontSizeToAll(double fontSize)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        foreach (var tab in viewModel.Tabs)
        {
            if ((tab.Terminal ?? tab.Content as TerminalControl) is { } terminal)
            {
                terminal.FontSize = fontSize;
                terminal.FontFamily = TerminalFontFamily();
            }
        }
    }

    private void PersistTerminalFontSize(double fontSize)
    {
        if (_config is null || _config.Config.Terminal.FontSize == fontSize)
        {
            return;
        }

        _config.Config.Terminal.FontSize = fontSize;
        _config.SaveConfig();
        AppLog.Info($"Terminal font size saved: {fontSize}");
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(this);

        if (e.Source is Control source
            && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            Cursor = null;
            return;
        }

        Cursor = GetResizeEdge(position) switch
        {
            WindowEdge.North or WindowEdge.South => NorthSouthCursor,
            WindowEdge.West or WindowEdge.East => WestEastCursor,
            WindowEdge.NorthWest or WindowEdge.SouthEast => NorthWestSouthEastCursor,
            WindowEdge.NorthEast or WindowEdge.SouthWest => NorthEastSouthWestCursor,
            _ => null,
        };
    }

    /// <summary>
    /// True when the pointer-down originates on a button-family control
    /// (Button / ToggleButton / RepeatButton) or its descendant. Such
    /// controls that sit at a window edge must keep their click rather than
    /// starting a window resize drag. Avalonia 12 has no shared ButtonBase
    /// type, so the family is enumerated explicitly.
    /// </summary>
    internal static bool IsEdgeInteractiveControl(Control control) =>
        control.FindAncestorOfType<Button>(includeSelf: true) is not null
        || control.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null
        || control.FindAncestorOfType<RepeatButton>(includeSelf: true) is not null;

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _windowPointerPressed = true;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || WindowState != WindowState.Normal)
        {
            return;
        }

        // Window controls at the very edge must still receive their click
        // instead of starting a window resize drag. This covers the title-bar
        // buttons and, importantly, the full-width SFTP "远程监控" band, which
        // is a ToggleButton (NOT a Button) sitting on the window's bottom
        // edge. Checking only for Button is exactly why the monitor toggle
        // intermittently swallowed its first click.
        if (e.Source is Control source
            && IsEdgeInteractiveControl(source))
        {
            return;
        }

        var edge = GetResizeEdge(e.GetPosition(this));
        if (edge is not null)
        {
            BeginResizeDrag(edge.Value, e);
            e.Handled = true;
        }
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _windowPointerPressed = false;
    }

    private void OnWindowPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _windowPointerPressed = false;
    }

    private WindowEdge? GetResizeEdge(Point point)
    {
        const double margin = 5;
        var left = point.X <= margin;
        var right = point.X >= Bounds.Width - margin;
        var top = point.Y <= margin;
        var bottom = point.Y >= Bounds.Height - margin;

        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (_, true, true, _) => WindowEdge.NorthEast,
            (true, _, _, true) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.West,
            (_, true, _, _) => WindowEdge.East,
            (_, _, true, _) => WindowEdge.North,
            (_, _, _, true) => WindowEdge.South,
            _ => null,
        };
    }

    private void ApplySidebarConfig()
    {
        if (_config is null)
        {
            return;
        }

        var config = _config.Config.SessionSidebar;
        _sidebarWidth = ClampSidebarWidth(config.Width);
        // The dock only changes when the title-bar button is clicked, so the
        // saved choice is the state to restore — no automatic reveal/collapse.
        SetSidebarVisible(config.Visible, persist: false);
    }

    private void ApplyWindowSizeConfig()
    {
        if (_config is null)
        {
            return;
        }

        var config = _config.Config.Window;
        if (config is null)
        {
            return;
        }

        ApplyWindowSize(config);
    }

    private void ApplyTerminalConfig()
    {
        if (_config is null)
        {
            return;
        }

        _terminalFontSize = ClampTerminalFontSize(_config.Config.Terminal.FontSize);
        TerminalFontPrimary = string.IsNullOrWhiteSpace(_config.Config.Terminal.FontFamily)
            ? "Consolas"
            : _config.Config.Terminal.FontFamily;
        ApplyTerminalFontSizeToAll(_terminalFontSize);

        // 从 config 恢复行尾仿真开关（旧配置缺键时反序列化为默认 false）。
        _lineEndingOptions.ImplicitCrInLf = _config.Config.Terminal.ImplicitCrInLf;
        _lineEndingOptions.ImplicitLfInCr = _config.Config.Terminal.ImplicitLfInCr;
    }

    /// <summary>
    /// 恢复窗口尺寸/最大化之前（Show 之前）只读窗口配置，
    /// 终端字号在 OnOpened 的 ApplyTerminalConfig 里应用。
    /// </summary>
    private void ApplySavedWindowSizeBeforeShow()
    {
        if (Design.IsDesignMode || Environment.GetCommandLineArgs().Contains("--demo"))
        {
            return;
        }

        var config = AppConfigService.TryReadWindowConfig();
        if (config is not null)
        {
            ApplyWindowSize(config);
        }
    }

    private void ApplyWindowSize(WindowConfig config)
    {
        var width = NormalizeWindowSize(
            config.Width,
            DefaultWindowWidth,
            MinimumWindowWidth);
        var height = NormalizeWindowSize(
            config.Height,
            DefaultWindowHeight,
            MinimumWindowHeight);
        // 保存的尺寸可能来自更大的显示器：直接还原会让窗口底部（SFTP 底栏
        // “远程监控”）落到任务栏后面/屏幕外，按钮点不到。这里先按当前屏幕
        // 工作区（已排除任务栏）钳制；窗口是无边框模式（WindowDecorations=None），
        // Width/Height 就是窗口外框尺寸，所以不需要额外扣边框。
        (width, height) = FitWindowToPrimaryScreen(width, height);
        Width = width;
        Height = height;
        _lastNormalWidth = width;
        _lastNormalHeight = height;
        // 窗口状态记忆（MobaXterm 行为）：最大化状态随下次启动恢复，
        // 尺寸存的是 normal 态大小，恢复后再切最大化。
        if (config.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private static double NormalizeWindowSize(int value, double fallback, double minimum) =>
        value >= minimum && value <= 10000 ? value : fallback;

    /// <summary>
    /// 把保存的窗口宽高钳制到主屏工作区（DIP）。取不到屏幕信息（headless、
    /// 平台未就绪）时原样返回，保持旧行为。
    /// </summary>
    private (double Width, double Height) FitWindowToPrimaryScreen(double width, double height)
    {
        // Avalonia 12 的 Screens 在平台未提供 IScreenImpl 时可能为 null（静态类型却是非空）。
        var screens = Screens;
        var screen = screens?.Primary;
        if (screen is null || screen.Scaling <= 0)
        {
            return (width, height);
        }

        var area = new Size(
            screen.WorkingArea.Width / screen.Scaling,
            screen.WorkingArea.Height / screen.Scaling);
        var clamped = ClampWindowSizeToWorkArea(
            new Size(width, height),
            area,
            new Size(MinimumWindowWidth, MinimumWindowHeight));
        return (clamped.Width, clamped.Height);
    }

    /// <summary>
    /// 纯函数版本，便于单测：窗口不允许超过工作区，但不小于 <paramref name="minimum"/>；
    /// 工作区比最小值还小的极端情况下保底为最小值（窗口仍可能超出，属不可避免）。
    /// </summary>
    internal static Size ClampWindowSizeToWorkArea(Size desired, Size workArea, Size minimum)
    {
        if (!double.IsFinite(workArea.Width)
            || !double.IsFinite(workArea.Height)
            || workArea.Width <= 0
            || workArea.Height <= 0)
        {
            return desired;
        }

        var maxWidth = Math.Max(minimum.Width, workArea.Width);
        var maxHeight = Math.Max(minimum.Height, workArea.Height);
        return new Size(
            Math.Min(desired.Width, maxWidth),
            Math.Min(desired.Height, maxHeight));
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (WindowState != WindowState.Normal
            || !double.IsFinite(e.NewSize.Width)
            || !double.IsFinite(e.NewSize.Height)
            || e.NewSize.Width < MinimumWindowWidth
            || e.NewSize.Height < MinimumWindowHeight)
        {
            return;
        }

        _lastNormalWidth = e.NewSize.Width;
        _lastNormalHeight = e.NewSize.Height;
    }

    private void PersistWindowSize()
    {
        if (_config is null)
        {
            return;
        }

        try
        {
            var width = (int)Math.Round(
                Math.Clamp(_lastNormalWidth, MinimumWindowWidth, 10000));
            var height = (int)Math.Round(
                Math.Clamp(_lastNormalHeight, MinimumWindowHeight, 10000));
            var maximized = WindowState == WindowState.Maximized;
            var config = _config.Config.Window;
            if (config is null)
            {
                config = new WindowConfig();
                _config.Config.Window = config;
            }

            if (config.Width == width && config.Height == height && config.Maximized == maximized)
            {
                return;
            }

            config.Width = width;
            config.Height = height;
            config.Maximized = maximized;
            _config.Save();
            AppLog.Info($"Window size saved: {width}x{height} maximized={maximized}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to save window size", ex);
        }
    }

    private void OnSidebarSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        var width = BodyGrid.ColumnDefinitions[0].ActualWidth;

        if ((DataContext as MainViewModel)?.IsSshBrowserTab == true)
        {
            // The browser width is a property of this session, not of the
            // saved sessions panel, so it is not written to the config.
            _browserSidebarWidth = width;
            return;
        }

        _sidebarWidth = ClampSidebarWidth(width);
        PersistSidebarConfig();
    }

    private static double ClampSidebarWidth(double width) =>
        double.IsFinite(width) ? Math.Clamp(width, 260, 360) : 260;

    private void PersistSidebarConfig()
    {
        if (_config is null)
        {
            return;
        }

        var config = _config.Config.SessionSidebar;
        config.Visible = _sidebarVisible;
        config.Width = (int)Math.Round(ClampSidebarWidth(_sidebarWidth));
        _config.Save();
    }

    // ------------------------------------------------------------------
    // Sidebar rail data: Macros / Tools (network scanner)
    // ------------------------------------------------------------------

    /// <summary>
    /// Loads the persisted macro list into the view model. Legacy macros
    /// (single command text + appendEnter) are migrated to the step model on
    /// load (MobaXterm parity), keeping the exact bytes that were sent.
    /// Called once after the config is available.
    /// </summary>
    private void LoadSidebarData()
    {
        if (_config is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.Macros.Clear();
        var idAssigned = false;
        var migrated = false;
        foreach (var macro in _config.Config.Macros)
        {
            if (string.IsNullOrWhiteSpace(macro.Name))
            {
                continue;
            }

            if (string.IsNullOrEmpty(macro.Id))
            {
                macro.Id = Guid.NewGuid().ToString("N");
                idAssigned = true;
            }

            // Legacy migration: no steps but text present -> steps.
            if ((macro.Steps is null || macro.Steps.Count == 0)
                && !string.IsNullOrEmpty(macro.Text))
            {
                macro.Steps = MacroMigrator.FromLegacy(macro.Text, macro.AppendEnter)
                    .Select(step => step.ToDto())
                    .ToList();
                macro.Text = string.Empty;
                migrated = true;
            }

            var steps = (macro.Steps ?? new List<MacroStepDto>())
                .Select(MacroStep.FromDto)
                .ToList();
            if (steps.Count == 0)
            {
                continue;
            }

            viewModel.Macros.Add(new MacroItem(macro.Id, macro.Name, steps, macro.Hotkey));
        }

        if (idAssigned || migrated)
        {
            _config.Save();
        }
    }

    /// <summary>
    /// Full resync of vm.Macros into config + save. All macro mutations
    /// (record/rename/duplicate/delete/edit/import) funnel through here.
    /// </summary>
    internal void PersistMacros()
    {
        if (_config is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var stored = _config.Config.Macros;
        stored.Clear();
        foreach (var item in viewModel.Macros)
        {
            stored.Add(new MacroConfig
            {
                Id = item.Id,
                Name = item.Name,
                Steps = item.Steps.Select(s => s.ToDto()).ToList(),
                Hotkey = item.Hotkey,
                Text = string.Empty,
                AppendEnter = false,
            });
        }

        _config.Save();
    }

    private async void OnMacroRecordToggleRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsRecording)
        {
            var steps = viewModel.StopRecording();
            UpdateMacroBanner();
            if (steps is not null)
            {
                await SaveRecordedAsync(steps);
            }
            // Empty recording: discarded silently, matching MobaXterm.
        }
        else if (viewModel.StartRecording())
        {
            UpdateMacroBanner();
        }
    }

    /// <summary>
    /// After a recording produces steps, prompt for a name (default derived
    /// from the first recorded line) and persist.
    /// </summary>
    private async Task SaveRecordedAsync(IReadOnlyList<MacroStep> steps)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var name = await ShowMacroNameAsync(MainViewModel.DefaultRecordedName(steps));
        if (name is null)
        {
            return;
        }

        if (viewModel.SaveNewMacro(name, steps))
        {
            PersistMacros();
        }
    }

    private void OnMacroExecuteRequested(object? sender, MacroItem item)
    {
        if (item is not null && DataContext is MainViewModel viewModel)
        {
            _ = viewModel.ExecuteMacroAsync(item);
        }
    }

    private async void OnMacroRenameRequested(object? sender, MacroItem item)
    {
        if (item is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var name = await ShowMacroNameAsync(item.Name);
        if (name is null)
        {
            return;
        }

        viewModel.RenameMacro(item, name);
        PersistMacros();
    }

    private async void OnMacroEditRequested(object? sender, MacroItem item)
    {
        if (item is null || DataContext is not MainViewModel)
        {
            return;
        }

        await OpenMacroEditorAsync(item);
    }

    private void OnMacroDuplicateRequested(object? sender, MacroItem item)
    {
        if (item is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.DuplicateMacro(item);
        PersistMacros();
    }

    private async void OnMacroDeleteRequested(object? sender, MacroItem item)
    {
        if (item is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!await ShowMacroDeleteConfirmAsync(item.Name))
        {
            return;
        }

        viewModel.RemoveMacro(item);
        PersistMacros();
    }

    private async void OnMacroHotkeyRequested(object? sender, MacroItem item)
    {
        if (item is null || DataContext is not MainViewModel)
        {
            return;
        }

        // MobaXterm: "Assign hotkey to X" opens the same macro editor; the
        // hotkey combo sits at the bottom of that window.
        await OpenMacroEditorAsync(item);
    }

    /// <summary>
    /// Opens the "Macro 编辑" step editor for the given macro. On 确定 the
    /// working copy (steps + hotkey) is written back to the item and
    /// persisted; on cancel nothing changes.
    /// </summary>
    private async Task OpenMacroEditorAsync(MacroItem item)
    {
        var editor = MacroEditorWindow.Create();
        editor.DataContext = item;
        editor.LoadFrom(item);
        LastMacroEditorWindowForTests = editor;
        try
        {
            await editor.ShowDialog(this);
        }
        finally
        {
            if (ReferenceEquals(LastMacroEditorWindowForTests, editor))
            {
                LastMacroEditorWindowForTests = null;
            }
        }

        var result = editor.Result;
        if (result is null)
        {
            return; // 取消: item untouched
        }

        item.SetSteps(result.Value.Steps);

        var hotkey = result.Value.Hotkey;
        if (hotkey != item.Hotkey && DataContext is MainViewModel viewModel)
        {
            if (!viewModel.AssignHotkey(item, hotkey))
            {
                _ = ShowErrorAsync("快捷键不可用（保留组合或已被其他宏占用），未分配。");
                return;
            }
        }

        PersistMacros();
    }

    internal Window? LastMacroEditorWindowForTests { get; private set; }

    /// <summary>
    /// 导入宏: 选择 .mxtmacros → 解析 → 重名加 " (imported)" 后缀 → 持久化。
    /// 解析告警取前 3 条随结果提示。
    /// </summary>
    private async void OnMacroImportRequested(object? sender, EventArgs e)
    {
        if (_config is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var pickers = MacroFilePickers.Create(this);
        if (pickers is null)
        {
            return;
        }

        var path = await pickers.PickImportPathAsync("导入宏");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return; // 取消选择
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync($"读取宏文件失败：{ex.Message}");
            return;
        }

        var (imported, warnings) = viewModel.ImportMacrosText(content);
        PersistMacros();

        var message = $"导入 {imported} 个宏";
        if (warnings.Count > 0)
        {
            var lines = new List<string>();
            for (var i = 0; i < warnings.Count && i < 3; i++)
            {
                lines.Add(warnings[i]);
            }
            message += $"，警告 {warnings.Count} 条：\n" + string.Join("\n", lines);
            if (warnings.Count > 3)
            {
                message += "\n…";
            }
        }
        _ = ShowMacroInfoAsync("导入宏", message);
    }

    /// <summary>
    /// 导出宏: MobaXterm 明文警告（英文原文，是/否）→ 选择文件 → 写
    /// .mxtmacros → 成功提示。否 = 中止，不产生文件。
    /// </summary>
    private async void OnMacroExportRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!await ShowMacroExportWarnAsync())
        {
            return;
        }

        var pickers = MacroFilePickers.Create(this);
        if (pickers is null)
        {
            return;
        }

        var path = await pickers.PickExportPathAsync("导出宏", MacroFile.DefaultFileName);
        if (string.IsNullOrEmpty(path))
        {
            return; // 取消选择
        }

        try
        {
            File.WriteAllText(path, viewModel.ExportMacrosText());
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync($"导出宏失败：{ex.Message}");
            return;
        }

        _ = ShowMacroInfoAsync("导出宏", $"宏已导出到 \"{path}\"");
    }

    /// <summary>
    /// 导出前的明文警告（2026-10-28 应用户要求汉化）。"是(Y)" 返回 true。
    /// </summary>
    internal async Task<bool> ShowMacroExportWarnAsync()
    {
        var dialog = CreateDialog(460, 200);
        var yes = new Button
        {
            Name = "MacroExportWarnYesButton",
            Content = DialogButtonContent("是(Y)", "\uf00c", "#16a34a"),
            Width = 96,
            Height = 30,
        };
        yes.IsDefault = true;
        yes.Click += (_, _) =>
        {
            dialog.Tag = true;
            dialog.Close();
        };
        var no = new Button
        {
            Name = "MacroExportWarnNoButton",
            Content = DialogButtonContent("否(N)", "\uf00d", "#dc2626"),
            Width = 96,
            Height = 30,
        };
        no.IsCancel = true;
        no.Click += (_, _) => dialog.Close();
        var icon = new TextBlock
        {
            Text = "\uf05a",
            FontFamily = IconFont(),
            FontSize = 28,
            Foreground = new SolidColorBrush(Color.Parse("#0078d7")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 14, 0),
        };
        var message = new TextBlock
        {
            Text = "警告：宏将在不加密的情况下导出。"
                 + "如果宏包含机密信息，这可能是危险的。"
                 + "您确定要继续吗？",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var buttons = BottomButtonRow(yes, no);
        var grid = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        grid.Children.Add(icon);
        grid.Children.Add(message);
        grid.Children.Add(buttons);
        Grid.SetColumn(icon, 0);
        Grid.SetRowSpan(icon, 2);
        Grid.SetColumn(message, 1);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        dialog.Content = grid;
        LastMacroExportWarnDialogForTests = dialog;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            if (ReferenceEquals(LastMacroExportWarnDialogForTests, dialog))
            {
                LastMacroExportWarnDialogForTests = null;
            }
        }

        return Equals(dialog.Tag, true);
    }

    internal Window? LastMacroExportWarnDialogForTests { get; private set; }

    /// <summary>导出/导入成功提示框（蓝色 info 图标 + 确定，对齐 MobaXterm 提示样式）。</summary>
    internal async Task ShowMacroInfoAsync(string title, string message)
    {
        var dialog = CreateDialog(460, 190);
        var ok = new Button
        {
            Name = "MacroInfoOkButton",
            Content = DialogButtonContent("确定", "\uf00c", "#16a34a"),
            Width = 96,
            Height = 30,
        };
        ok.Click += (_, _) => dialog.Close();
        var icon = new TextBlock
        {
            Text = "\uf05a",
            FontFamily = IconFont(),
            FontSize = 28,
            Foreground = new SolidColorBrush(Color.Parse("#0078d7")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 14, 0),
        };
        var text = new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var buttons = BottomButtonRow(ok);
        var grid = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(buttons);
        Grid.SetColumn(icon, 0);
        Grid.SetRowSpan(icon, 2);
        Grid.SetColumn(text, 1);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        dialog.Content = grid;
        LastMacroInfoDialogForTests = dialog;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            if (ReferenceEquals(LastMacroInfoDialogForTests, dialog))
            {
                LastMacroInfoDialogForTests = null;
            }
        }
    }

    internal Window? LastMacroInfoDialogForTests { get; private set; }

    /// <summary>
    /// <summary>图标字体（Font Awesome 6 Solid，与主题资源同源）。</summary>
    private static FontFamily IconFont() => new(
        "avares://COMTool.App/Assets/Fonts/fontawesome6-solid.ttf#Font Awesome 6 Free Solid");

    /// <summary>对话框按钮内容：图标 + 文字（MobaXterm 风格 ✓确定 / ✗取消）。</summary>
    private static StackPanel DialogButtonContent(string label, string faGlyph, string colorHex)
    {
        var panel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        panel.Children.Add(new TextBlock
        {
            Text = faGlyph,
            FontFamily = IconFont(),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse(colorHex)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
        return panel;
    }

    /// <summary>
    /// 底部居中的按钮行（系统消息框版式：消息在上、按钮沉底且左右对称，
    /// 2026-10-29 用户截图要求）。
    /// </summary>
    private static StackPanel BottomButtonRow(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        foreach (var b in buttons)
        {
            row.Children.Add(b);
        }

        return row;
    }

    /// <summary>
    /// 统一的宏对话框工厂：标题固定 COMTool、不显示任务栏、居中于主窗口、不可缩放，
    /// 并在 Windows 下剥离最小化/最大化按钮（CanResize=false 在 VDI 上仍显示灰显的
    /// min/max，2026-10-29 用户截图反馈），仅保留关闭按钮，贴近系统消息框观感。
    /// </summary>
    private Window CreateDialog(double width, double height)
    {
        var dialog = new Window
        {
            Title = "COMTool",
            Width = width,
            Height = height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        // 主窗口先例：Avalonia 会从自身装饰模型重写 Win32 样式，激活时补一次
        // （StripDialogMinimizeMaximize 幂等，无变化时直接返回）。
        dialog.Opened += (_, _) => StripDialogMinimizeMaximize(dialog);
        dialog.Activated += (_, _) => StripDialogMinimizeMaximize(dialog);
        return dialog;
    }

    /// <summary>
    /// Windows 下移除对话框的最小化/最大化框（WS_MINIMIZEBOX / WS_MAXIMIZEBOX），
    /// 仅保留关闭按钮；非 Windows 平台（含 headless 测试）无操作。
    /// </summary>
    private void StripDialogMinimizeMaximize(Window dialog)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = dialog.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle.Handle, GwlStyle).ToInt64();
        var newStyle = style & ~WsMinimizeBox & ~WsMaximizeBox;
        if (newStyle == style)
        {
            return;
        }

        SetWindowLongPtr(handle.Handle, GwlStyle, new IntPtr(newStyle));
        SetWindowPos(
            handle.Handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
    }

    /// <summary>
    /// "请为此宏输入一个名称" — MobaXterm 版式：左侧铅笔图标、右侧标签+输入框
    /// （默认名预填并选中）、底部居中 ✓确定/✗取消。OK 返回去空格后的名称，取消/空返回 null。
    /// </summary>
    internal async Task<string?> ShowMacroNameAsync(string defaultName)
    {
        var dialog = CreateDialog(460, 200);
        var box = new TextBox { Name = "MacroNameBox", FontSize = 13, Margin = new Thickness(0, 8, 0, 0) };
        box.Text = defaultName;
        box.Loaded += (_, _) => box.SelectAll();
        var ok = new Button
        {
            Name = "MacroNameOkButton",
            Content = DialogButtonContent("确定", "\uf00c", "#16a34a"),
            Width = 96,
            Height = 30,
        };
        ok.IsDefault = true;
        ok.Click += (_, _) =>
        {
            dialog.Tag = box.Text.Trim();
            dialog.Close();
        };
        var cancel = new Button
        {
            Name = "MacroNameCancelButton",
            Content = DialogButtonContent("取消", "\uf00d", "#dc2626"),
            Width = 96,
            Height = 30,
        };
        cancel.Click += (_, _) => dialog.Close();

        var icon = new TextBlock
        {
            Text = "\uf303",
            FontFamily = IconFont(),
            FontSize = 40,
            Foreground = new SolidColorBrush(Color.Parse("#2f7ed8")),
            Margin = new Thickness(0, 4, 18, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var inputArea = new StackPanel();
        inputArea.Children.Add(new TextBlock
        {
            Text = "请为此宏输入一个名称",
            FontSize = 12,
        });
        inputArea.Children.Add(box);
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { ok, cancel },
        };
        var grid = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { icon, inputArea, buttons },
        };
        Grid.SetColumn(icon, 0);
        Grid.SetRowSpan(icon, 2);
        Grid.SetColumn(inputArea, 1);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        dialog.Content = grid;
        LastMacroNameDialogForTests = dialog;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            if (ReferenceEquals(LastMacroNameDialogForTests, dialog))
            {
                LastMacroNameDialogForTests = null;
            }
        }

        var result = dialog.Tag as string;
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    internal Window? LastMacroNameDialogForTests { get; private set; }

    /// <summary>是/否 delete confirmation. Returns true on "是(Y)".</summary>
    internal async Task<bool> ShowMacroDeleteConfirmAsync(string name)
    {
        var dialog = CreateDialog(460, 170);
        var yes = new Button
        {
            Name = "MacroDeleteYesButton",
            Content = DialogButtonContent("是(Y)", "\uf00c", "#16a34a"),
            Width = 96,
            Height = 30,
        };
        yes.IsDefault = true;
        yes.Click += (_, _) =>
        {
            dialog.Tag = true;
            dialog.Close();
        };
        var no = new Button
        {
            Name = "MacroDeleteNoButton",
            Content = DialogButtonContent("否(N)", "\uf00d", "#dc2626"),
            Width = 96,
            Height = 30,
        };
        no.IsCancel = true;
        no.Click += (_, _) => dialog.Close();

        var icon = new TextBlock
        {
            Text = "\uf05a",
            FontFamily = IconFont(),
            FontSize = 28,
            Foreground = new SolidColorBrush(Color.Parse("#0078d7")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 14, 0),
        };
        var message = new TextBlock
        {
            Text = $"确定删除宏 \"{name}\" 吗？",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };
        // 系统消息框版式：消息在上，按钮行沉底水平居中（左右对称）
        var grid = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        var buttons = BottomButtonRow(yes, no);
        grid.Children.Add(icon);
        grid.Children.Add(message);
        grid.Children.Add(buttons);
        Grid.SetColumn(icon, 0);
        Grid.SetRowSpan(icon, 2);
        Grid.SetColumn(message, 1);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        dialog.Content = grid;
        LastMacroDeleteDialogForTests = dialog;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            if (ReferenceEquals(LastMacroDeleteDialogForTests, dialog))
            {
                LastMacroDeleteDialogForTests = null;
            }
        }

        return Equals(dialog.Tag, true);
    }

    internal Window? LastMacroDeleteDialogForTests { get; private set; }

    // ------------------------------------------------------------------
    // Macro status banner + Ctrl+Space popup
    // ------------------------------------------------------------------

    /// <summary>
    /// Updates the red strip above the terminal: recording (Stop) only;
    /// macro EXECUTION shows no banner at all (MobaXterm parity, user
    /// request 2026-10-28).
    /// </summary>
    private void UpdateMacroBanner()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsRecording)
        {
            MacroBannerText.Text = "● 正在录制新宏: 您在当前终端中键入的所有内容都将被保存。";
            MacroBannerActionButton.Content = "停止";
            MacroStatusBanner.IsVisible = true;
        }
        else
        {
            MacroStatusBanner.IsVisible = false;
        }
    }

    private async void OnMacroBannerActionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsRecording)
        {
            var steps = viewModel.StopRecording();
            UpdateMacroBanner();
            if (steps is not null)
            {
                await SaveRecordedAsync(steps);
            }
        }
    }

    private int _macroPopupSelected;

    private void ShowMacroPopup()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.Macros.Count == 0)
        {
            return;
        }

        _macroPopupSelected = 0;
        MacroPopup.IsVisible = true;
        UpdateMacroPopupHighlight();
    }

    private void HideMacroPopup()
    {
        MacroPopup.IsVisible = false;
    }

    private void MoveMacroPopupSelection(int delta)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.Macros.Count == 0)
        {
            return;
        }

        _macroPopupSelected = (_macroPopupSelected + delta + viewModel.Macros.Count) % viewModel.Macros.Count;
        UpdateMacroPopupHighlight();
    }

    private void UpdateMacroPopupHighlight()
    {
        var rows = MacroPopupList.GetVisualDescendants().OfType<Button>().ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Background = i == _macroPopupSelected
                ? new SolidColorBrush(Color.Parse("#cfe3ff"))
                : null;
        }
    }

    private void ExecuteMacroPopupSelection()
    {
        if (DataContext is MainViewModel viewModel
            && viewModel.Macros.Count > 0
            && _macroPopupSelected < viewModel.Macros.Count)
        {
            var item = viewModel.Macros[_macroPopupSelected];
            HideMacroPopup();
            _ = viewModel.ExecuteMacroAsync(item);
        }
        else
        {
            HideMacroPopup();
        }
    }

    private async void OnMacroPopupRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MacroItem item }
            && DataContext is MainViewModel viewModel)
        {
            HideMacroPopup();
            await viewModel.ExecuteMacroAsync(item);
        }
    }

    /// <summary>
    /// Tool windows opened from the 工具 tab, keyed by ToolCatalog id, so a
    /// second click activates the existing window instead of opening another.
    /// </summary>
    private readonly Dictionary<string, Window> _toolWindows = new(StringComparer.Ordinal);

    internal IReadOnlyCollection<Window> ToolWindowsForTests => _toolWindows.Values;

    internal Window? FindToolWindowForTests(string id) =>
        _toolWindows.TryGetValue(id, out var window) ? window : null;

    private void OnToolRequested(object? sender, string id)
    {
        var descriptor = ToolCatalog.Find(id);
        if (descriptor is null || !descriptor.IsEnabled)
        {
            return;
        }

        if (_toolWindows.TryGetValue(id, out var existing))
        {
            if (!existing.IsVisible)
            {
                existing.Show();
            }
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }
            existing.Activate();
            return;
        }

        var window = descriptor.CreateWindow(this);
        window.Closed += (_, _) => _toolWindows.Remove(id);
        _toolWindows[id] = window;
        // 与 MobaXterm 一致的任务栏归组行为：不设 Owner。Show(this) 会建立 Win32
        // 从属关系——工具窗跟随主窗最小化（用户明确不要）、也不出独立任务栏缩略图。
        // 用普通 Show + 工具窗 ShowInTaskbar=true：各窗为独立顶层窗，同进程 AUMID
        // 自动归到同一任务栏按钮（悬停出主窗+各工具窗缩略图预览、点预览单独激活
        // 对应窗）；关主窗时由 OnClosed 显式关闭全部工具窗。
        window.Show();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaximizeIcon();
        RestoreTerminalFocus();
    }

    private void UpdateMaximizeIcon()
    {
        if (MaximizeIcon is null)
        {
            return;
        }

        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\U000F05B2" : "\U000F05AF";
        ToolTip.SetTip(MaximizeButton, WindowState == WindowState.Maximized ? "Restore" : "Maximize");
    }

    private void OnNewSessionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag })
        {
            OpenNewSession(tag);
        }
    }

    private void OnSidebarNewSessionRequested(object? sender, string tag) =>
        OpenNewSession(tag);

    private void OpenNewSession(string tag)
    {
        var kind = tag switch
        {
            "SSH" => SessionKind.Ssh,
            "Telnet" => SessionKind.Telnet,
            "TCP" => SessionKind.Tcp,
            "Local" => SessionKind.Local,
            _ => SessionKind.Serial,
        };

        if (kind == SessionKind.Local)
        {
            AddLocalTerminal();
            return;
        }

        ShowConnectionSettings(null, kind);
    }

    private void ShowConnectionSettings(
        TerminalTab? tab,
        SessionKind initialKind = SessionKind.Serial,
        string? editSessionId = null,
        string? editTitle = null)
    {
        if (_config is null)
        {
            return;
        }

        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var window = new ConnectionSettingsWindow(
            _config,
            tab,
            OpenSerialFromSettingsAsync,
            OpenSshFromSettingsAsync,
            OpenTelnetFromSettingsAsync,
            OpenTcpFromSettingsAsync,
            ShowErrorAsync,
            initialKind,
            editSessionId,
            editTitle);
        _settingsWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, window))
            {
                _settingsWindow = null;
            }

            RestoreTerminalFocus();
        };
        window.Show(this);
    }

    private async Task OpenSerialFromSettingsAsync(string port, TerminalTab? currentTab)
    {
        if (DataContext is MainViewModel viewModel)
        {
            var existing = currentTab is { Kind: SessionKind.Serial } currentSerial
                && string.Equals(currentSerial.SessionId, port, StringComparison.OrdinalIgnoreCase)
                && viewModel.Tabs.Contains(currentSerial)
                ? currentSerial
                : viewModel.FindTab(port, SessionKind.Serial);
            if (existing is not null)
            {
                CloseTab(existing);
            }
        }

        OpenSerialSession(port);
        RefreshSidebar();
        await Task.CompletedTask;
    }

    private async Task OpenSshFromSettingsAsync(string key, TerminalTab? currentTab)
    {
        if (DataContext is MainViewModel viewModel
            && SettingsTabPolicy.TabToReplace(
                viewModel.Tabs,
                currentTab,
                key,
                SessionKind.Ssh) is { } currentSsh)
        {
            // Replace only the tab whose settings were edited; other parallel
            // connections to the same saved session must stay open.
            CloseTab(currentSsh);
        }

        await OpenSshSessionAsync(key);
        RefreshSidebar();
    }

    private async Task OpenTelnetFromSettingsAsync(string key, TerminalTab? currentTab)
    {
        if (DataContext is MainViewModel viewModel
            && SettingsTabPolicy.TabToReplace(
                viewModel.Tabs,
                currentTab,
                key,
                SessionKind.Telnet) is { } currentTelnet)
        {
            CloseTab(currentTelnet);
        }

        await OpenTelnetSessionAsync(key);
        RefreshSidebar();
    }

    private async Task OpenTcpFromSettingsAsync(string key, TerminalTab? currentTab)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var isServer = IsTcpServerSession(key);
        if (isServer)
        {
            // A listening port can only have one owner. Reuse an already-live
            // matching tab unless the user explicitly edited that same tab.
            var live = AllTabs().FirstOrDefault(tab =>
                tab.Kind == SessionKind.Tcp
                && string.Equals(tab.SessionId, key, StringComparison.OrdinalIgnoreCase)
                && !ReferenceEquals(tab, currentTab)
                && IsTabConnected(tab));
            if (live is not null)
            {
                if (_detachedTabs.TryGetValue(live, out var state))
                {
                    state.Window.Activate();
                }
                else
                {
                    viewModel.SelectedTab = live;
                }

                (live.Terminal ?? (Control)live.Content).Focus();
                RestoreTerminalFocus();
                return;
            }
        }

        if (SettingsTabPolicy.TabToReplace(
            viewModel.Tabs,
            currentTab,
            key,
            SessionKind.Tcp) is { } currentTcp)
        {
            CloseTab(currentTcp);
        }

        await OpenTcpSessionAsync(key);
        RefreshSidebar();
    }

    /// <summary>
    /// 隧道阶段 handler（CreateTerminal 里 AddHandler(Tunnel) 注册）：
    /// 在库 TerminalView 的类 handler 抢先处理右键之前拦截。
    /// 非右键直接放行（不碰事件），左键选择/鼠标上报等库行为不受影响。
    /// </summary>
    private async void OnTerminalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 隧道 handler 的 sender 可能是注册目标（TerminalControl）或事件源
        // （模板内深层元素），两种情况都解析回所属终端控件。
        var terminal = sender as TerminalControl
            ?? (e.Source as Visual)?.FindAncestorOfType<TerminalControl>();
        if (terminal is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(terminal);
        if (!point.Properties.IsRightButtonPressed)
        {
            return;
        }

        e.Handled = true;

        // 抢先后库自己的 Focus() 被跳过，这里补上焦点（库原本聚焦内层
        // TerminalView，右击粘贴与后续键盘输入依赖它）。
        var view = terminal.GetVisualDescendants()
            .OfType<Iciclecreek.Terminal.TerminalView>()
            .FirstOrDefault();
        if (view is not null)
        {
            view.Focus();
        }
        else
        {
            terminal.Focus();
        }

        // MobaXterm 行为：Shift+右键 打开终端菜单（含“更改终端设置…”）；
        // 普通右键保持原行为（有选区=复制，无选区=粘贴）。
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            OpenTerminalContextMenu(terminal, e.GetPosition(terminal));
            return;
        }

        if (terminal.Terminal.Selection.HasSelection)
        {
            await terminal.CopyAsync();
            terminal.Terminal.Selection.ClearSelection();
            return;
        }

        await PasteNormalizedAsync(terminal);
    }

    // ==================== 终端查找（TERM-23） ====================
    // MobaXterm 式：Ctrl+Shift+F 或终端菜单「查找」在终端区底部弹出内联查找条，
    // 底层用库的 BufferSearch（FindInBuffer/FindNext/FindPrevious/ClearSearch）。

    private void ShowTerminalSearchBar()
    {
        if (DataContext is not MainViewModel viewModel
            || viewModel.SelectedTab is not { } tab
            || (tab.Terminal ?? tab.Content as TerminalControl) is not { } terminal)
        {
            return;
        }

        _searchTerminal = terminal;
        _searchHasQuery = false;
        SearchBar.IsVisible = true;
        SearchBox.Text = "";
        SearchCount.Text = "";
        SearchBox.Focus();
    }

    private void HideSearchBar()
    {
        if (_searchTerminal is { } terminal)
        {
            terminal.ClearSearch();
            _searchTerminal = null;
        }

        _searchHasQuery = false;
        if (SearchBar.IsVisible)
        {
            SearchBar.IsVisible = false;
        }
    }

    private void OnSearchBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_searchTerminal is not { } terminal)
        {
            return;
        }

        var text = SearchBox.Text ?? "";
        if (string.IsNullOrEmpty(text))
        {
            terminal.ClearSearch();
            _searchHasQuery = false;
            SearchCount.Text = "";
            return;
        }

        terminal.FindInBuffer(text, new XTerm.Search.SearchOptions());
        _searchHasQuery = true;
        // FindInBuffer 只登记命中、不定位首个命中（CurrentIndex 仍为 -1），
        // 手动前进一步使首个命中高亮，对齐 MobaXterm 首次搜索即定位的行为。
        if (terminal.SearchHitCount > 0 && terminal.SearchCurrentIndex < 0)
        {
            terminal.FindNext();
        }
        UpdateSearchCount(terminal);
    }

    private void OnSearchPrevClick(object? sender, RoutedEventArgs e) => MoveSearch(false);

    private void OnSearchNextClick(object? sender, RoutedEventArgs e) => MoveSearch(true);

    private void OnSearchCloseClick(object? sender, RoutedEventArgs e) => HideSearchBar();

    private void MoveSearch(bool next)
    {
        if (_searchTerminal is not { } terminal || !_searchHasQuery)
        {
            return;
        }

        if (next)
        {
            terminal.FindNext();
        }
        else
        {
            terminal.FindPrevious();
        }

        UpdateSearchCount(terminal);
    }

    private void UpdateSearchCount(TerminalControl terminal)
    {
        var hits = terminal.SearchHitCount;
        if (hits <= 0)
        {
            SearchCount.Text = "0/0";
            return;
        }

        var current = terminal.SearchCurrentIndex;
        if (current < 0)
        {
            current = 0;
        }

        SearchCount.Text = $"{current + 1}/{hits}{(terminal.SearchTruncated ? "+" : "")}";
    }

    // ---- 测试接缝（headless 驱动查找条 / 读取录制态）----
    internal void ShowSearchBarForTests() => ShowTerminalSearchBar();
    internal bool IsSearchBarVisibleForTests => SearchBar.IsVisible;
    internal string SearchCountTextForTests => SearchCount.Text;
    internal bool IsOutputRecordingForTests { get { lock (_recordingGate) return _recordingTerminal is not null; } }
    internal long RecordingBytesForTests { get { lock (_recordingGate) return _recordingBytes; } }
    internal void StopOutputRecordingForTests() => StopTerminalOutputRecording();

    // 字体设置对话框测试接缝（与 LastRestartDialogForTests 同模式）。
    internal Window? LastFontDialogForTests { get; private set; }
    internal Task ShowFontDialogForTestsAsync() => ShowTerminalFontDialogAsync();

    // ==================== 字体设置（TERM-24） ====================
    // MobaXterm 式：终端菜单「字体设置」对话框，选字体族+字号；全局生效
    // （与 Ctrl+滚轮 一致）并落盘 config（terminal.fontFamily / terminal.fontSize）。

    internal static readonly string[] TerminalFontFamilies =
    {
        "Consolas", "Cascadia Mono", "Courier New", "Microsoft YaHei UI", "Noto Sans Mono CJK SC",
    };

    /// <summary>精确客户区窗口：Avalonia 12 中 <c>TopLevel.ClientSize</c> 的 setter 是 protected，
    /// 外部代码（MainWindow）无法直接给客户区赋值（CS1540）。经此子类暴露，
    /// 保证窗口以精确客户区尺寸创建（Width/Height 含边框与标题栏，DPI 相关，不可用）。
    /// 字体设置/事件日志等需要精确客户区尺寸的对话框共用。</summary>
    private sealed class ClientSizedWindow : Window
    {
        public void SetClientSize(Size size) => ClientSize = size;
    }

    /// <summary>把已排布的可视子树渲染成 PNG 落盘（实机自检用，免截图目检）。
    /// 透明度保留：调用方用图像工具合成到白底后再量像素。</summary>
    // r54: AOT 下 XAML 字面色编译为 ImmutableSolidColorBrush，资源刷仍是
    // SolidColorBrush；自检读刷值时两种都要认，否则字面色会被误读为默认黑。
    private static Color BrushColor(IBrush? brush) => brush switch
    {
        SolidColorBrush s => s.Color,
        Avalonia.Media.Immutable.ImmutableSolidColorBrush i => i.Color,
        _ => Color.FromRgb(0, 0, 0),
    };

    private static void SaveVisualPng(Visual? visual, string fileName)
    {
        try
        {
            if (visual is null || visual.Bounds.Width < 1 || visual.Bounds.Height < 1)
            {
                AppLog.Info($"[SelfTest] png {fileName}: skipped (bounds={visual?.Bounds})");
                return;
            }

            var size = new PixelSize(
                (int)Math.Ceiling(visual.Bounds.Width),
                (int)Math.Ceiling(visual.Bounds.Height));
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(visual);
            var dir = Path.GetDirectoryName(AppLog.Path) ?? ".";
            var path = Path.Combine(dir, fileName);
            bitmap.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            AppLog.Info($"[SelfTest] png {fileName}: {size.Width}x{size.Height} -> {path}");
        }
        catch (Exception ex)
        {
            AppLog.Info($"[SelfTest] png {fileName}: failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>对话框文字按钮（字体设置/事件日志等共用）。
    /// 实机缺陷（2026-11-03 用户反馈「取消与确定这两个字显示不完全，底部有一部分没显示」）：
    /// Avalonia 12.1.4 Fluent 主题的 Button 默认 <c>HorizontalContentAlignment</c> /
    /// <c>VerticalContentAlignment</c> 都是 Stretch —— 字符串内容被包成 TextBlock 并拉伸。
    /// 后果有两个：(a) 文字在按钮里左对齐（实测 80px 宽按钮里文字起点 x=9）；
    /// (b) 固定 Height=28 时内容内框只有 28-(5+6)=17px，而 Windows 上 14px 微软雅黑
    /// 的 CJK 行高约 19px，汉字底部两行被内框裁掉（截图里「取消/确定」字底缺一截）。
    /// 故这里显式两轴居中 + 不写死 Height（由真实行高+内边距撑开，MinHeight 仅保底）。
    /// 不要改回 <c>Height = 28</c>：DPI/字体变化会再次裁切。</summary>
    private static Button CreateDialogTextButton(string text, double minWidth) => new()
    {
        Content = text,
        MinWidth = minWidth,
        MinHeight = 28,
        Padding = new Thickness(12, 4, 12, 4),
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
    };

    private async Task ShowTerminalFontDialogAsync()
    {
        if (_config is null)
        {
            return;
        }

        var currentFamily = TerminalFontPrimary;
        var currentSize = ClampTerminalFontSize(_config.Config.Terminal.FontSize);

        var dialog = new ClientSizedWindow
        {
            Title = "字体设置",
            MinWidth = 380,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            // 教训（实机四轮复验）：
            // - 固定 Height=240：客户区不够，底部按钮被裁切；
            // - 固定 Height=320：按钮完整但底部留 ~134px 空白，观感"不完整"；
            // - SizeToContent.Height：Win32 下 Height 未定时平台按系统默认尺寸建窗
            //   （实机两次量出 420×720），自适应失效。
            // 故改为显式 ClientSize（客户区 420 宽 × 初始估计高 230），Opened 首帧布局
            // 完成后按当前机器字体重测内容高度再精确校正——任何 DPI/字体下都贴合。
        };

        var familyCombo = new ComboBox
        {
            Width = 220,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        foreach (var name in TerminalFontFamilies)
        {
            familyCombo.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        }

        var idx = Array.IndexOf(TerminalFontFamilies, currentFamily);
        familyCombo.SelectedIndex = idx >= 0 ? idx : 0;

        var sizeDec = (decimal)currentSize;
        var sizeText = sizeDec.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sizeSpin = new NumericUpDown
        {
            Minimum = (decimal)TerminalFontSizeMin,
            Maximum = (decimal)TerminalFontSizeMax,
            Value = sizeDec,
            // 同时显式设置 Text：库 OnApplyTemplate 时是 TextBox.Text = Text（读字符串属性）。
            Text = sizeText,
            // 根因（实机 [FontDlg] 日志坐实）：12.1.4 Fluent 模板的 ButtonSpinner 是
            // Dock=Right 横排两个 MinWidth=34 的按钮（共 68px），控件宽 90px 时内部
            // TextBox 只剩 ~20px，"13.5" 被裁成 "1"。加宽到 120px 保证文本完整可见。
            Width = 120,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };

        var ok = CreateDialogTextButton("确定", 80);
        var cancel = CreateDialogTextButton("取消", 80);
        ok.Click += (_, _) =>
        {
            var family = (familyCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Consolas";
            var size = ClampTerminalFontSize((double)sizeSpin.Value);
            ApplyTerminalFont(family, size);
            _config!.Config.Terminal.FontFamily = family;
            _config.Config.Terminal.FontSize = size;
            _config.SaveConfig();
            AppLog.Info($"Terminal font saved: family={family}, size={size}");
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "字体：", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                        familyCombo,
                    },
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "字号：", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                        sizeSpin,
                    },
                },
                new TextBlock
                {
                    Text = "对全部终端即时生效，并保存后重启保持。字号范围 8–48（与 Ctrl+滚轮 一致）。",
                    FontSize = 12,
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                    // SizeToContent.Height 下必须固定换行元素的宽度：Windows 首次布局时
                    // 客户区宽度可能尚为 0，本行折成 ~44 行导致窗口被量成 720px 高
                    // （实机 dlgBounds=420,720）。固定 380=420-20*2 后高度测量与
                    // 窗口宽度解耦，任意平台/DPI 下恒定 2 行。
                    Width = 380,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };
        // 显示前预测量：把内容面板临时挂到主窗根（主题已生效的真实排版上下文）量出
        // 精确内容高度，再以该高度创建对话框客户区。这是 Win32 下唯一稳定的定点——
        // 显示后改尺寸会被陈旧布局趟推回初始值（实机复验证实：校正 200 四次全部
        // 被弹回 230；SizeToContent.Height 在 Win32 两次量出 720）。
        // 探针必须放在 420 宽的容器里，与对话框客户区同宽，换行/排版才一致。
        double probeH = 0;
        if (this.Content is Panel rootPanel)
        {
            var probeHost = new Grid
            {
                Width = 420,
                Opacity = 0,
                IsHitTestVisible = false,
            };
            probeHost.Children.Add(content);
            rootPanel.Children.Add(probeHost);
            // 先跑一次主窗布局趟：确保新挂入的 content 子树已应用主题/字体样式，
            // 下面的显式测量才用的是真实排版度量（此趟把 content 钳到所在行高，
            // 结果丢弃，仅取其"样式已生效"的副作用）。
            UpdateLayout();
            // 显式测量（420×∞）而不是读布局趟结果：探针落在主窗固定高的行里时，
            // 布局趟会把 DesiredSize 钳到行高（headless 实测 35px 标题栏行）。
            // 显式测量宽度与对话框客户区一致、高度无约束，与真实对话框排版相同，
            // 且不受探针所在行高影响——确定性取值。
            content.Measure(new Size(420, double.PositiveInfinity));
            probeH = content.DesiredSize.Height;
            // Avalonia 12 的视觉子树不会自动迁移父级：必须先显式把 content 从
            // probeHost 摘下，否则下面 dialog.Content = content 会抛
            // "already has a visual parent Grid"（headless 复现）。
            probeHost.Children.Remove(content);
            rootPanel.Children.Remove(probeHost);
            UpdateLayout();
        }
        double dlgH = probeH > 40 ? Math.Ceiling(probeH) + 2 : 230;
        AppLog.Info($"[FontDlg] pre-measure: probeH={probeH:0.#} dlgH={dlgH:0.#}");
        dialog.Content = content;
        dialog.SetClientSize(new Size(420, dlgH));

        // 实机（RDP/Windows 渲染）下 NumericUpDown 对象初始化器里设置的 Value
        // 可能来不及把文本刷进模板内部 TextBox（headless 同步 RunJobs 时不可复现），
        // 表现为字号框显示残缺（如 "1"）。构造期已显式设置 Text，Opened 后再强制
        // 同步一次 Value→Text，双保险。
        dialog.Opened += (_, _) =>
        {
            sizeSpin.Value = sizeDec;
            sizeSpin.Text = sizeText;
            // 诊断日志：实机打开后记录真实状态。若字号框仍显示异常，经 SSH 读
            // run.log 的 [FontDlg] 行即可判定 Value/Text/FontSize/Bounds 实际值。
            var tb = sizeSpin.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            AppLog.Info($"[FontDlg] opened: currentSize={currentSize} value={sizeSpin.Value} " +
                $"text=[{sizeSpin.Text}] spinFontSize={sizeSpin.FontSize} tbFontSize={tb?.FontSize} " +
                $"tbText=[{tb?.Text}] spinBounds={sizeSpin.Bounds} tbBounds={tb?.Bounds} " +
                $"dlgBounds={dialog.Bounds}");

            // 首帧布局（含主题样式应用）完成后精确校正客户区高度——这是 SizeToContent 在
            // Win32 失效（实机两次 420×720）后的确定性替代：
            // 目标高 = 确定/取消按钮底边 + 20px 下边距（与内容面板 margin 一致）。
            // 平台 resize 回环是异步的（headless/Win32 均如此），陈旧布局趟可能把旧尺寸
            // 重新发回平台覆盖校正，故用"设置→低优先级复验"收敛，最多 3 次防乒乓。
            Dispatcher.UIThread.Post(() => CorrectFontDialogHeight(dialog, content, 0),
                DispatcherPriority.Background);
        };

        LastFontDialogForTests = dialog;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            if (ReferenceEquals(LastFontDialogForTests, dialog))
            {
                LastFontDialogForTests = null;
            }
        }
    }

    /// <summary>校正字体对话框客户区高度 = 确定/取消按钮底边 + 20px 下边距（与内容面板 margin 一致）。
    /// 先 UpdateLayout 确保主题/字体已生效再读真实排布几何。
    /// 平台 resize 回环是异步的（headless 经 Send 优先级 Post，Win32 经窗口消息），
    /// 陈旧布局趟可能把旧尺寸重新发回平台、覆盖本次校正（headless 实测复现），
    /// 故每次设置后再调度一次低优先级（ContextIdle）复验，最多 3 次，防乒乓死循环。</summary>
    private void CorrectFontDialogHeight(ClientSizedWindow dialog, StackPanel content, int attempt)
    {
        if (!dialog.IsVisible)
        {
            return;
        }

        dialog.UpdateLayout();
        var buttonBottom = 0.0;
        foreach (var b in dialog.GetVisualDescendants().OfType<Button>())
        {
            if (b.Content is not string label || label is not ("确定" or "取消"))
            {
                continue;
            }
            var p = b.TranslatePoint(new Point(0, b.Bounds.Height), dialog);
            if (p is { } q)
            {
                buttonBottom = Math.Max(buttonBottom, q.Y);
            }
        }
        double target;
        if (buttonBottom > 0)
        {
            target = buttonBottom + 20;
        }
        else
        {
            // 兜底：按内容 DesiredSize（含上下 margin）估计
            var measureW = Math.Max(100.0, dialog.ClientSize.Width - 40);
            content.Measure(new Size(measureW, double.PositiveInfinity));
            target = content.DesiredSize.Height;
        }

        if (Math.Abs(dialog.ClientSize.Height - target) < 2)
        {
            AppLog.Info($"[FontDlg] post-layout: stable clientSize={dialog.ClientSize} dlgBounds={dialog.Bounds} " +
                $"(buttonBottom={buttonBottom:0.#}, attempt={attempt})");
            return;
        }

        dialog.SetClientSize(new Size(dialog.ClientSize.Width, target));
        // 再失效一次窗口自身测量/排布并同步布局，尽量让后续布局趟使用最新尺寸
        // （平台级陈旧约束由"显示前预测量"从源头消除，此处为兜底）。
        dialog.InvalidateMeasure();
        dialog.InvalidateArrange();
        dialog.UpdateLayout();
        AppLog.Info($"[FontDlg] post-layout: set {dialog.ClientSize} target={target:0.#} " +
            $"buttonBottom={buttonBottom:0.#} attempt={attempt}");

        if (attempt < 3)
        {
            Dispatcher.UIThread.Post(() => CorrectFontDialogHeight(dialog, content, attempt + 1),
                DispatcherPriority.ContextIdle);
        }
    }

    /// <summary>
    /// <c>--selftest-fontdlg</c>：启动后自动打开「字体设置」对话框，等首帧布局校正完成后
    /// 把真实几何（clientSize / dlgBounds / 确定与取消按钮底边及间隙）打进 run.log，然后退出。
    /// 用途：免 RDP、免用户操作的实机复验——SSH 读 run.log 的 <c>[SelfTest]</c> 与
    /// <c>[FontDlg] post-layout</c> 行，核对窗口高是紧凑值（约 230–250px）而非平台默认 720px。
    /// </summary>
    private async Task RunFontDialogSelfTestAsync()
    {
        try
        {
            AppLog.Info("[SelfTest] fontdlg: 等主窗稳定 1.5s 后自动打开字体对话框");
            await Task.Delay(1500);

            var showTask = ShowFontDialogForTestsAsync();
            var waited = 0;
            while (LastFontDialogForTests is null && waited < 10_000)
            {
                await Task.Delay(100);
                waited += 100;
            }

            var dialog = LastFontDialogForTests;
            if (dialog is null)
            {
                AppLog.Error("[SelfTest] FAILED: 字体对话框 10s 内未打开");
                (Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(1);
                return;
            }

            // 等 Opened 里的 Post(Background) 校正执行并稳定
            await Task.Delay(1500);

            AppLog.Info($"[SelfTest] clientSize={dialog.ClientSize} dlgBounds={dialog.Bounds}");
            foreach (var button in dialog.GetVisualDescendants().OfType<Button>())
            {
                if (button.Content is not string label || label is not ("确定" or "取消"))
                {
                    continue;
                }
                var bottomLeft = button.TranslatePoint(new Point(0, button.Bounds.Height), dialog);
                if (bottomLeft is { } p)
                {
                    AppLog.Info($"[SelfTest] button[{label}] bounds={button.Bounds} bottomY={p.Y:0.#} " +
                        $"clientH={dialog.ClientSize.Height:0.#} gap={dialog.ClientSize.Height - p.Y:0.#}");
                }

                // 2026-11-03 缺陷复验：汉字「底部一部分没显示」的定量证据——
                // 内容 TextBlock 若被 ContentPresenter 内框压扁（Bounds.Height <
                // DesiredSize.Height）或横向被拉伸（文字左对齐），这里逐项写日志。
                var presenter = button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().FirstOrDefault();
                var textBlock = button.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
                AppLog.Info($"[SelfTest] btn[{label}] pad={button.Padding} hca={button.HorizontalContentAlignment} " +
                    $"vca={button.VerticalContentAlignment} cp={presenter?.Bounds}/{presenter?.DesiredSize} " +
                    $"tb={textBlock?.Bounds} tbDesired={textBlock?.DesiredSize} " +
                    $"tbSqueezedH={textBlock is not null && textBlock.Bounds.Height + 0.01 < textBlock.DesiredSize.Height} " +
                    $"tbStretchedW={textBlock is not null && textBlock.Bounds.Width > textBlock.DesiredSize.Width + 0.5}");
            }

            // 像素级证据：对话框内容与主窗根各渲染一张 PNG 落盘（软件渲染，Windows 真实
            // 字体/DPI），随后经 SSH 取回逐像素量汉字墨迹是否有被裁掉的行、文字是否居中。
            SaveVisualPng(dialog.Content as Visual, "fontdlg-content.png");
            ShowTerminalSearchBar();
            UpdateLayout();

            // 同类缺陷全局排查：主窗里所有“字符串内容”按钮的 TextBlock 是否被内框压扁
            // （汉字底部裁切）或横向拉伸（文字左对齐）。日志只列有问题的按钮。
            var issues = 0;
            foreach (var mainButton in this.GetVisualDescendants().OfType<Button>())
            {
                if (mainButton.Content is not string mainLabel)
                {
                    continue;
                }
                var mainText = mainButton.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
                if (mainText is null)
                {
                    continue;
                }
                var squeezed = mainText.Bounds.Height + 0.01 < mainText.DesiredSize.Height;
                var stretched = mainText.Bounds.Width > mainText.DesiredSize.Width + 0.5;
                if (!squeezed && !stretched)
                {
                    continue;
                }
                issues++;
                AppLog.Info($"[SelfTest] main-btn[{mainLabel}] name={mainButton.Name} bounds={mainButton.Bounds} " +
                    $"pad={mainButton.Padding} hca={mainButton.HorizontalContentAlignment} " +
                    $"vca={mainButton.VerticalContentAlignment} tb={mainText.Bounds} " +
                    $"tbDesired={mainText.DesiredSize} squeezed={squeezed} stretched={stretched}");
            }
            AppLog.Info($"[SelfTest] main window string-button layout issues: {issues}");

            SaveVisualPng(Content as Visual, "main-content.png");

            AppLog.Info("[SelfTest] fontdlg 自检完成，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] fontdlg 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>字体族+字号同时应用到所有已打开终端（「字体设置」对话框入口）。</summary>
    internal void ApplyTerminalFont(string family, double fontSize)
    {
        TerminalFontPrimary = string.IsNullOrWhiteSpace(family) ? "Consolas" : family;
        _terminalFontSize = fontSize;
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var ff = TerminalFontFamily();
        foreach (var tab in viewModel.Tabs)
        {
            if ((tab.Terminal ?? tab.Content as TerminalControl) is { } terminal)
            {
                terminal.FontFamily = ff;
                terminal.FontSize = fontSize;
            }
        }
    }

    // ==================== 录制终端输出到文件（TERM-25） ====================
    // MobaXterm 式：菜单项切换；首次点击弹保存框选文件后开始把该终端的原始
    // 输出字节追加写盘，再次点击（或该 tab 关闭）停止。

    private async Task ToggleTerminalOutputRecordingAsync(TerminalTab? tab, TerminalControl terminal)
    {
        if (ReferenceEquals(_recordingTerminal, terminal))
        {
            StopTerminalOutputRecording();
            return;
        }

        if (_recordingTerminal is not null)
        {
            StopTerminalOutputRecording();
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "录制终端输出到文件",
            SuggestedFileName = $"{SafeFileName(tab?.Title ?? "terminal")}-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            DefaultExtension = "log",
        });
        if (file is null)
        {
            return;
        }

        try
        {
            StartTerminalOutputRecording(terminal, file.Path.LocalPath, tab?.Title ?? "terminal");
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync($"启动终端输出录制失败：{ex.Message}");
        }
    }

    internal void StartTerminalOutputRecording(TerminalControl terminal, string path, string tabTitle)
    {
        lock (_recordingGate)
        {
            _recordingStream?.Dispose();
            _recordingStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            _recordingPath = path;
            _recordingTerminal = terminal;
            _recordingBytes = 0;
            _recordingHandler = OnTerminalOutputForRecording;
            terminal.OutputReceived += _recordingHandler;
        }

        AppLog.Info($"Terminal output recording started: {path} (tab: {tabTitle})");
    }

    private void StopTerminalOutputRecording()
    {
        TerminalControl? terminal = null;
        long bytes = 0;
        string path = "";
        lock (_recordingGate)
        {
            terminal = _recordingTerminal;
            bytes = _recordingBytes;
            path = _recordingPath;
            _recordingTerminal = null;
            _recordingPath = "";
            _recordingStream?.Dispose();
            _recordingStream = null;
        }

        if (terminal is not null)
        {
            if (_recordingHandler is not null)
            {
                terminal.OutputReceived -= _recordingHandler;
            }

            AppLog.Info($"Terminal output recording stopped: {path} ({bytes} bytes)");
        }
    }

    private void OnTerminalOutputForRecording(object? sender, Iciclecreek.Terminal.OutputReceivedEventArgs e)
    {
        var len = e.Bytes.Length;
        if (len == 0)
        {
            return;
        }

        var bytes = e.Bytes.ToArray();
        lock (_recordingGate)
        {
            if (_recordingStream is { } stream)
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                _recordingBytes += bytes.Length;
            }
        }
    }

    /// <summary>tab 关闭/分离窗口销毁时若正在录它，自动停止（防悬挂句柄）。</summary>
    private void StopRecordingForTabIfAny(TerminalTab? tab)
    {
        if (tab is not null
            && ReferenceEquals(_recordingTerminal, tab.Terminal ?? tab.Content as TerminalControl))
        {
            StopTerminalOutputRecording();
        }
    }

    /// <summary>
    /// 终端 Shift+右键菜单：复刻 MobaXterm 终端右键菜单（22.1，19 项+4 分隔线，
    /// 顺序/快捷键标注一致，已按本项目汉化要求译成中文）。“更改终端设置…”
    /// 即 MobaXterm “Change terminal settings...”，打开“实时终端重配置”对话框。
    /// COMTool 已实现的功能接线生效，未实现的功能置灰（与 MobaXterm 在
    /// 不适用的会话里置灰部分菜单项的惯例一致）。
    /// r39：终端显示/语法高亮/特殊命令 三项由置灰占位升级为子菜单。
    /// </summary>
    private void OpenTerminalContextMenu(TerminalControl terminal, Point position)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var tab = viewModel.Tabs.FirstOrDefault(t =>
            ReferenceEquals(t.Terminal, terminal) || ReferenceEquals(t.Content, terminal));

        var hasSelection = terminal.Terminal.Selection.HasSelection;
        var live = terminal.IsLive;
        var zmodemBusy = tab?.IsZmodemActive ?? false;
        var recordingThisTerminal = ReferenceEquals(_recordingTerminal, terminal);
        var readOnly = tab?.IsReadOnlyTerminal == true;

        var menu = new ContextMenu();
        menu.Items.Add(TerminalMenuItem("复制", null, hasSelection,
            async () => { await terminal.CopyAsync(); }));
        menu.Items.Add(TerminalMenuItem("复制全部", null, live,
            async () =>
            {
                terminal.Terminal.Selection.SelectAll();
                await terminal.CopyAsync();
            }));
        menu.Items.Add(TerminalMenuItem("复制带格式文本（RTF）", null, hasSelection,
            async () => { await CopyTerminalRtfAsync(terminal, tab); }));
        menu.Items.Add(TerminalMenuItem("粘贴", "Shift+INSERT", live && !readOnly,
            async () => { await PasteNormalizedAsync(terminal); }));
        menu.Items.Add(TerminalMenuItem("查找", "Ctrl+Shift+F", true,
            () =>
            {
                ShowTerminalSearchBar();
                return Task.CompletedTask;
            }));
        menu.Items.Add(new Separator());
        menu.Items.Add(TerminalMenuItem("字体设置", null, true,
            () =>
            {
                _ = ShowTerminalFontDialogAsync();
                return Task.CompletedTask;
            }));
        menu.Items.Add(BuildDisplayMenuItem(tab, terminal));
        menu.Items.Add(BuildSyntaxHighlightMenuItem());
        menu.Items.Add(new Separator());
        menu.Items.Add(TerminalMenuItem("执行宏", "Ctrl+空格", viewModel.Macros.Count > 0,
            () =>
            {
                ShowMacroPopup();
                return Task.CompletedTask;
            }));
        var canRecord = viewModel.CanToggleRecording
            && tab is not null
            && ReferenceEquals(viewModel.SelectedTab, tab);
        menu.Items.Add(TerminalMenuItem("录制宏", null, canRecord,
            () =>
            {
                if (viewModel.IsRecording)
                {
                    viewModel.StopRecording();
                }
                else
                {
                    viewModel.StartRecording();
                }

                return Task.CompletedTask;
            }));
        menu.Items.Add(TerminalMenuItem(
            ReferenceEquals(_recordingTerminal, terminal) ? "停止录制终端输出" : "录制终端输出到文件",
            null,
            tab is not null,
            async () =>
            {
                await ToggleTerminalOutputRecordingAsync(tab, terminal);
            }));
        menu.Items.Add(TerminalMenuItem("保存到文件", "Ctrl+Shift+S", live,
            async () => { await SaveTerminalOutputAsync(tab, terminal); }));
        menu.Items.Add(TerminalMenuItem("打印", "Ctrl+Shift+P", live,
            async () => { await PrintTerminalAsync(terminal, tab); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(TerminalMenuItem("使用 Zmodem 接收文件", null, live && !zmodemBusy,
            async () =>
            {
                // 远端运行 sz 把文件发过来，ZmodemStream 自动识别并完成接收。
                await terminal.SendInputAsync("sz\r");
            }));
        menu.Items.Add(TerminalMenuItem("使用 Zmodem 发送文件", null, live && !zmodemBusy,
            async () =>
            {
                // 远端运行 rz，ZmodemStream 检测到后弹出本地文件选择框发送。
                await terminal.SendInputAsync("rz\r");
            }));
        menu.Items.Add(new Separator());
        menu.Items.Add(TerminalMenuItem("更改终端设置…", null, true,
            () =>
            {
                _ = ShowTerminalSettingsAsync();
                return Task.CompletedTask;
            }));
        menu.Items.Add(BuildEncodingMenuItem());
        menu.Items.Add(BuildSpecialCommandMenuItem(tab, terminal));
        menu.Items.Add(TerminalMenuItem("事件日志", null, true,
            () =>
            {
                _ = ShowEventLogDialogAsync();
                return Task.CompletedTask;
            }));

        LastTerminalMenuForTests = menu;
        try
        {
            menu.Open(terminal);
        }
        catch (Exception ex)
        {
            // 弹窗失败不应影响终端本身（headless 无 popup 呈现层等场景）。
            AppLog.Warn($"Terminal context menu failed to open: {ex.Message}");
        }
    }

    /// <summary>最近一次 Shift+右键弹出的终端菜单（headless 测试钩子）。</summary>
    internal ContextMenu? LastTerminalMenuForTests { get; private set; }

// ==================== r39：终端显示 / 语法高亮 / 特殊命令 ====================

    /// <summary>查找指定终端所属的 tab（主窗 Tabs 或已分离窗）。</summary>
    internal TerminalTab? FindTabForTerminal(TerminalControl terminal)
    {
        if (terminal is null)
        {
            return null;
        }

        if (DataContext is MainViewModel viewModel)
        {
            var tab = viewModel.Tabs.FirstOrDefault(t =>
                ReferenceEquals(t.Terminal, terminal) || ReferenceEquals(t.Content, terminal));
            if (tab is not null)
            {
                return tab;
            }
        }

        foreach (var detached in _detachedTabs.Keys)
        {
            if (ReferenceEquals(detached.Terminal, terminal)
                || ReferenceEquals(detached.Content, terminal))
            {
                return detached;
            }
        }

        return null;
    }

    /// <summary>
    /// 只读终端守卫（r39）：隧道阶段 KeyDown。终端处于只读态时拦截按键，
    /// 阻断库内部“键输入→pty”路径；窗口级功能键（F11/宏热键/查找）在更上层
    /// 隧道先处理，不受影响。粘贴路径由 PasteNormalizedAsync 内守卫兜底。
    /// </summary>
    private void OnTerminalReadOnlyGuard(object? sender, KeyEventArgs e)
    {
        var terminal = sender as TerminalControl
            ?? (e.Source as Visual)?.FindAncestorOfType<TerminalControl>();
        if (terminal is null)
        {
            return;
        }

        if (FindTabForTerminal(terminal)?.IsReadOnlyTerminal == true)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// 挂语法高亮着色器（r39）：订阅核心 XTerm 的 LineFed，按 config 方案
    /// （全局单值，terminal.syntaxHighlight）对新输出行着色。同一终端 re-attach
    /// （重启等）时先卸旧实例。
    /// </summary>
    internal void AttachTerminalHighlighter(TerminalControl terminal)
    {
        if (_highlighters.TryGetValue(terminal, out var oldHl))
        {
            oldHl.Dispose();
        }

        if (terminal.Terminal is { } core)
        {
            var scheme = HighlightSchemes.FromName(_config?.Config.Terminal.SyntaxHighlight);
            _highlighters[terminal] = new TerminalHighlighter(core, scheme);
            return;
        }

        // XTerm 核心在终端完成首次布局后才创建（单测中 AttachTerminal 可能先于布局；
        // 生产里异步连接附加发生在已实现之后，正常不会走到此分支）。用与
        // CreateTerminal 相同的 Loaded 优先级延迟重试一次；届时仍未实现则放弃
        // （着色器不挂，绝不影响终端本身）。
        Dispatcher.UIThread.Post(() =>
        {
            if (terminal.Terminal is { } c && !_highlighters.ContainsKey(terminal))
            {
                var s = HighlightSchemes.FromName(_config?.Config.Terminal.SyntaxHighlight);
                _highlighters[terminal] = new TerminalHighlighter(c, s);
            }
        }, DispatcherPriority.Loaded);
    }

    private void ReleaseHighlighterForTab(TerminalTab? tab)
    {
        if (tab is null)
        {
            return;
        }

        var terminal = tab.Terminal ?? tab.Content as TerminalControl;
        if (terminal is { } t && _highlighters.Remove(t, out var hl))
        {
            hl.Dispose();
        }
    }

    /// <summary>「重置终端输出」（MobaXterm）：RIS 全复位——清屏+清回滚+复位
    /// 终端全部状态（颜色/模式/tab stops），与向远端发 reset 命令无关，纯本地。</summary>
    internal void ResetTerminalOutput(TerminalControl terminal)
    {
        try
        {
            terminal.Terminal.Reset();
            terminal.Refresh(); // RIS 不经过输出流，显式通知控件重绘（否则界面看似无变化）
            AppLog.Info("Terminal output reset (RIS)");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Terminal reset failed: {ex.Message}");
        }
    }

    /// <summary>「清除终端回滚」（MobaXterm）：丢弃回滚历史 + 清空当前屏（MobaXterm
    /// 官方 changelog："clear the screen AND the scrollback buffer"，r40 用户实机对齐）；
    /// 终端状态（颜色/模式/光标）不动——那是「重置终端输出」(RIS) 的职责。</summary>
    internal void ClearTerminalScrollback(TerminalControl terminal)
    {
        try
        {
            terminal.Terminal.Buffer.ClearScrollback();
            terminal.Terminal.Clear();
            terminal.Refresh(); // 直接改缓冲区不经过输出流，显式通知控件重绘
            AppLog.Info("Terminal scrollback + screen cleared");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Clear scrollback failed: {ex.Message}");
        }
    }

    /// <summary>「设置终端标题」（MobaXterm）：输入对话框改本 tab 标题（会话级，不持久化）。
    /// 高度用显示前预测量定值（同字体对话框：Win32 下固定 Height/SizeToContent 均不可靠）。</summary>
    internal async Task SetTerminalTitleAsync(TerminalTab tab)
    {
        var input = new TextBox
        {
            Width = 280,
            Text = tab.Title,
        };
        var ok = CreateDialogTextButton("确定", 80);
        var cancel = CreateDialogTextButton("取消", 80);
        string? result = null;
        var dialog = new ClientSizedWindow
        {
            Title = "设置终端标题",
            MinWidth = 320,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        ok.Click += (_, _) =>
        {
            var t = input.Text?.Trim();
            if (!string.IsNullOrEmpty(t))
            {
                result = t;
            }
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "终端标题：", Width = 280 },
                input,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };
        // 显示前预测量（同字体对话框方案）：Win32 固定 Height/SizeToContent 均不可靠，
        // 把内容挂进主窗真实排版上下文显式 Measure 取高，一次性 SetClientSize 定窗。
        double probeH = 0;
        if (this.Content is Panel rootPanel)
        {
            var probeHost = new Grid { Width = 340, Opacity = 0, IsHitTestVisible = false };
            probeHost.Children.Add(content);
            rootPanel.Children.Add(probeHost);
            UpdateLayout();
            content.Measure(new Size(340, double.PositiveInfinity));
            probeH = content.DesiredSize.Height;
            probeHost.Children.Remove(content);
            rootPanel.Children.Remove(probeHost);
            UpdateLayout();
        }
        double dlgH = probeH > 40 ? Math.Ceiling(probeH) + 2 : 120;
        dialog.Content = content;
        dialog.SetClientSize(new Size(340, dlgH));

        LastTitleDialogForTests = dialog;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            if (ReferenceEquals(LastTitleDialogForTests, dialog))
            {
                LastTitleDialogForTests = null;
            }
        }

        if (result is not null)
        {
            tab.Title = result;
            AppLog.Info($"Terminal title set: {result}");
        }
    }

    /// <summary>「切换终端滚动条」（MobaXterm）：toggle 终端模板内 ScrollBar 可见性
    /// （r67.7：默认 16px 常显；关掉时 Width=0 让终端重新分列，再打开时重新装配
    /// 一屏翻页与按钮重复节奏）。</summary>
    internal bool ToggleTerminalScrollBar(TerminalControl terminal)
    {
        var bars = terminal.GetVisualDescendants().OfType<ScrollBar>().ToList();
        if (bars.Count == 0)
        {
            AppLog.Warn("Toggle scrollbar: no ScrollBar in terminal template");
            return false;
        }

        var currentlyHidden = !bars[0].IsVisible || bars[0].Width == 0;
        foreach (var bar in bars)
        {
            bar.IsVisible = currentlyHidden;
            if (!currentlyHidden)
            {
                bar.Width = 0;
                bar.MinWidth = 0;
                continue;
            }

            ApplyTerminalScrollBar(bar);
        }

        AppLog.Info($"Terminal scrollbar toggled: visible={currentlyHidden}");
        return true;
    }

    /// <summary>「全屏终端」（MobaXterm F11）：FullScreen 切换 + 标题栏显隐，
    /// 与 F11 键共用同一逻辑。</summary>
    internal void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            // 退出：恢复到进入全屏前的窗口态（MobaXterm 行为）
            WindowState = _preFullScreenState;
            TitleBar.IsVisible = true;
            RootGrid.RowDefinitions[0].Height = new GridLength(35);
            AppLog.Info($"Terminal fullscreen: off (restore {_preFullScreenState})");
        }
        else
        {
            _preFullScreenState = WindowState;
            WindowState = WindowState.FullScreen;
            TitleBar.IsVisible = false;
            RootGrid.RowDefinitions[0].Height = new GridLength(0);
            AppLog.Info($"Terminal fullscreen: on (from {_preFullScreenState})");
        }
    }

    /// <summary>「语法高亮」子菜单（MobaXterm）：6 选项单选，当前方案打勾；
    /// 全局单值，落盘 config.json，对全部终端立即生效（含回滚历史重染）。</summary>
    internal MenuItem BuildSyntaxHighlightMenuItem()
    {
        var current = _config?.Config.Terminal.SyntaxHighlight ?? "default";
        var parent = new MenuItem { Header = "语法高亮" };
        foreach (var entry in SyntaxHighlightMenuItems())
        {
            var (key, label, enabled) = entry;
            var child = new MenuItem
            {
                Header = label,
                IsEnabled = enabled,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "TerminalSyntaxHighlight",
                IsChecked = enabled && string.Equals(key, current, StringComparison.OrdinalIgnoreCase),
            };
            if (enabled)
            {
                var k = key;
                child.Click += (_, _) => SetSyntaxHighlightScheme(k);
            }

            parent.Items.Add(child);
        }

        return parent;
    }

    private static (string Key, string Label, bool Enabled)[] SyntaxHighlightMenuItems() =>
    [
        ("default", "默认", true),
        // r59：对齐 MobaXterm 内建 6 项，新增「标准关键字」（SyntaxType=1）
        ("standard", "标准关键字", true),
        ("error_warning", "错误/警告/成功关键字", true),
        ("unix_shell", "Unix shell 脚本", true),
        ("cisco", "Cisco（网络配置）", true),
        ("sql", "SQL 语法", true),
        // r40.2：「逐行」已移除——MobaXterm 公开资料（文档/changelog/ini 导出/论坛）
        // 均无该项的确切语义（用户决定去掉；日后语义明确可再加回）
    ];

    internal void SetSyntaxHighlightScheme(string key)
    {
        if (_config is null)
        {
            return;
        }

        _config.Config.Terminal.SyntaxHighlight = key;
        _config.SaveConfig();
        var scheme = HighlightSchemes.FromName(key);
        foreach (var pair in _highlighters)
        {
            pair.Value.Scheme = scheme;
            pair.Value.ApplyToAll();   // 全量重染（含回滚历史）
        }

        AppLog.Info($"Syntax highlight scheme: {key} (terminals={_highlighters.Count})");
    }

    /// <summary>「终端显示」子菜单（MobaXterm）：6 项，MobaXterm 顺序。</summary>
    internal MenuItem BuildDisplayMenuItem(TerminalTab? tab, TerminalControl terminal)
    {
        var parent = new MenuItem { Header = "终端显示" };
        parent.Items.Add(TerminalMenuItem("重置终端输出", null, true,
            () => { ResetTerminalOutput(terminal); return Task.CompletedTask; }));
        parent.Items.Add(TerminalMenuItem("清除终端回滚", null, true,
            () => { ClearTerminalScrollback(terminal); return Task.CompletedTask; }));
        parent.Items.Add(TerminalMenuItem("设置终端标题", null, tab is not null,
            async () => await SetTerminalTitleAsync(tab!)));
        parent.Items.Add(TerminalMenuItem("切换终端滚动条", null, true,
            () => { ToggleTerminalScrollBar(terminal); return Task.CompletedTask; }));
        parent.Items.Add(TerminalMenuItem("全屏终端", "F11", true,
            () => { ToggleFullScreen(); return Task.CompletedTask; }));
        var roItem = TerminalMenuItem("只读终端", null, tab is not null,
            () =>
            {
                if (tab is not null)
                {
                    tab.IsReadOnlyTerminal = !tab.IsReadOnlyTerminal;
                    AppLog.Info($"Terminal read-only: {tab.IsReadOnlyTerminal}");
                }
                return Task.CompletedTask;
            });
        roItem.IsChecked = tab?.IsReadOnlyTerminal == true;
        parent.Items.Add(roItem);
        return parent;
    }

    /// <summary>「特殊命令」子菜单（MobaXterm Special Command）：顶层 5 项 + 更多信号。
    /// r40.2：「忽略消息」已移除（用户决定不实现）。</summary>
    internal MenuItem BuildSpecialCommandMenuItem(TerminalTab? tab, TerminalControl terminal)
    {
        var kind = tab?.Kind ?? SessionKind.Local;
        var parent = new MenuItem { Header = "特殊命令" };
        foreach (var def in SpecialCommands.TopLevel)
        {
            AddSpecialCommandChild(parent, tab, terminal, kind, def);
        }

        var more = new MenuItem { Header = "更多信号" };
        foreach (var def in SpecialCommands.More)
        {
            AddSpecialCommandChild(more, tab, terminal, kind, def);
        }
        parent.Items.Add(more);
        return parent;
    }

    private void AddSpecialCommandChild(
        MenuItem parent, TerminalTab? tab, TerminalControl terminal, SessionKind kind, SpecialCommandDef def)
    {
        var avail = SpecialCommands.IsAvailable(def, kind);
        var d = def;
        Func<Task>? action = null;
        if (avail)
        {
            action = async () => await DispatchSpecialCommandAsync(tab, terminal, d);
        }

        parent.Items.Add(TerminalMenuItem(d.Label, null, avail, action));
    }

    /// <summary>
    /// 特殊命令派发（r39 定稿矩阵）：
    /// - SSH：协议信号/break（RFC 4254 §6.5/§6.7，直达远端会话 leader）；
    /// - 串口：硬件 Break；有控制字节等价物者（^C/^\/^Z）走 pty 发送路径；
    /// - TCP/Telnet：仅控制字节等价物；Local：无可用通道。
    /// </summary>
    internal async Task DispatchSpecialCommandAsync(TerminalTab? tab, TerminalControl terminal, SpecialCommandDef def)
    {
        var kind = tab?.Kind ?? SessionKind.Local;
        var ok = false;
        try
        {
            if (kind == SessionKind.Ssh && tab is not null && tab.Connection is SshConnection ssh)
            {
                ok = def.IsBreak ? ssh.TrySendBreak() : ssh.TrySendSignal(def.SshSignal!);
            }
            else if (kind == SessionKind.Serial && def.IsBreak
                     && tab is not null && tab.Connection is SerialConnection serial)
            {
                await serial.SendBreakAsync();
                ok = true;
            }
            else if (def.CtrlByte is { } b && kind is SessionKind.Serial or SessionKind.Tcp or SessionKind.Telnet)
            {
                await terminal.SendInputAsync(new string((char)b, 1));
                ok = true;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Special command {def.Label} failed: {ex.Message}");
        }

        AppLog.Info($"[SpecialCmd] {def.Label} (tab={tab?.SessionId}, kind={kind}) -> {(ok ? "sent" : "unsupported/failed")}");
        if (!ok)
        {
            _ = ShowErrorAsync($"无法发送「{def.Label}」（当前会话类型不支持或连接已断开）");
        }
    }

    /// <summary>测试接缝：最近一次「设置终端标题」对话框（同 LastFontDialogForTests 模式）。</summary>
    internal Window? LastTitleDialogForTests { get; private set; }
    internal Task ShowTitleDialogForTestsAsync(TerminalTab tab) => SetTerminalTitleAsync(tab);
    internal TerminalHighlighter? GetHighlighterForTests(TerminalControl terminal)
        => _highlighters.TryGetValue(terminal, out var hl) ? hl : null;
    internal bool IsTerminalInputBlockedForTests(TerminalControl terminal)
        => FindTabForTerminal(terminal)?.IsReadOnlyTerminal == true;

    /// <summary>

    /// <summary>
    /// 终端菜单项：左侧标签 + 右侧灰色快捷键标注（MobaXterm 样式）。
    /// action 为 null 表示未实现，置灰显示。
    /// </summary>
    private static MenuItem TerminalMenuItem(
        string label,
        string? shortcut,
        bool enabled,
        Func<Task>? action)
    {
        object header;
        if (string.IsNullOrEmpty(shortcut))
        {
            header = label;
        }
        else
        {
            var shortcutText = new TextBlock
            {
                Text = shortcut,
                Margin = new Thickness(32, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.Parse("#9e9e9e")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            Grid.SetColumn(shortcutText, 1);

            header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    },
                    shortcutText,
                },
            };
        }

        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (action is not null)
        {
            item.Click += async (_, _) => await action();
        }

        return item;
    }

    /// <summary>
    /// 终端菜单「编码」子菜单：从标签栏右侧编码下拉框移入（MobaXterm 式
    /// Shift+右键入口）。6 种编码与移入前一致，当前值打勾；选择后落盘 config.json
    /// （与移入前相同：仅持久化，终端解码路径尚未消费该值）。
    /// </summary>
    private MenuItem BuildEncodingMenuItem()
    {
        var current = _config?.Config.Encoding ?? "UTF-8";
        var parent = new MenuItem { Header = "编码" };
        foreach (var name in TerminalEncodings)
        {
            var child = new MenuItem
            {
                Header = name,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "TerminalEncoding",
                IsChecked = string.Equals(name, current, StringComparison.OrdinalIgnoreCase),
            };
            child.Click += (_, _) => SetTerminalEncoding(name);
            parent.Items.Add(child);
        }

        return parent;
    }

    private static readonly string[] TerminalEncodings =
    {
        "ASCII", "UTF-8", "UTF-16", "GBK", "GB2312", "GB18030",
    };

    private void SetTerminalEncoding(string encoding)
    {
        if (_config is null)
        {
            return;
        }

        _config.Config.Encoding = encoding;
        _config.Save();
    }

    /// <summary>
    /// 终端菜单“保存到文件”：把当前终端完整回滚缓冲存成文本
    /// （与停止会话的“保存终端输出”同一捕获逻辑，活终端同样可用）。
    /// </summary>
    private async Task SaveTerminalOutputAsync(TerminalTab? tab, TerminalControl terminal)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存终端输出",
                SuggestedFileName = $"{SafeFileName(tab?.Title ?? "terminal")}-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                DefaultExtension = "txt",
            });
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek)
            {
                stream.SetLength(0);
            }

            await TerminalTranscript.SaveAsync(terminal, stream);
            if (tab is not null)
            {
                tab.StatusMessage = $"已保存终端输出：{file.Name}";
                tab.StatusBrush = StatusInfoBrush;
            }
        }
        catch (Exception ex)
        {
            _ = ShowErrorAsync($"保存终端输出失败：{ex.Message}");
        }
    }

    // ==================== r38：RTF 复制 / 事件日志 / 打印 ====================

    /// <summary>
    /// 复制带格式文本（RTF）：选区 → RTF（保留逐格颜色）+ 纯文本，三格式写剪贴板。
    /// "Rich Text Format" 平台格式名与 Windows 预注册 CF_RTF(0x0305) 精确匹配，
    /// Word/WordPad 可还原（Win32 剪贴板对平台名走 RegisterClipboardFormat，源码坐实）；
    /// "text/rtf" 供 X11/Wayland MIME 兼容；纯文本兜底保证任何应用可粘贴。
    /// </summary>
    internal async Task CopyTerminalRtfAsync(TerminalControl terminal, TerminalTab? tab)
    {
        try
        {
            var fontSize = _config is null
                ? (TerminalFontSizeMin + TerminalFontSizeMax) / 2
                : ClampTerminalFontSize(_config.Config.Terminal.FontSize);
            var rtf = TerminalRtf.BuildRtf(terminal.Terminal, fontSize, TerminalFontPrimary);
            if (rtf is null)
            {
                if (tab is not null)
                {
                    tab.StatusMessage = "无选区可复制";
                    tab.StatusBrush = StatusInfoBrush;
                }
                return;
            }

            var plain = terminal.Terminal.Selection.GetSelectionText();
            // Windows 直写 CF_RTF(ANSI)+CF_UNICODETEXT（Avalonia 剪贴板按 UTF-16 写
            // CF_RTF 会被标准读取方截断，见 Win32RtfClipboard）；其他平台走 DataTransfer。
            var writeOk = await TerminalRtfClipboard.SetAsync(TopLevel.GetTopLevel(terminal), rtf, plain);
            if (tab is not null)
            {
                tab.StatusMessage = writeOk ? "已复制带格式文本（RTF）" : "复制 RTF 失败：无法写入系统剪贴板";
                tab.StatusBrush = writeOk ? StatusInfoBrush : StatusErrorBrush;
            }
        }
        catch (Exception ex)
        {
            if (tab is not null)
            {
                tab.StatusMessage = $"复制 RTF 失败：{ex.Message}";
                tab.StatusBrush = StatusErrorBrush;
            }
        }
    }

    internal Window? LastEventLogDialogForTests { get; private set; }
    internal Task ShowEventLogForTestsAsync() => ShowEventLogDialogAsync();

    /// <summary>事件日志对话框：显示 run.log 尾部 200 行（只读）+「刷新」按钮重读。</summary>
    private async Task ShowEventLogDialogAsync()
    {
        var dialog = new ClientSizedWindow
        {
            Title = "事件日志",
            CanResize = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var box = new TextBox
        {
            Name = "EventLogBox",
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
            FontSize = 12,
            Margin = new Thickness(10, 10, 10, 4),
        };
        var refresh = CreateDialogTextButton("刷新", 88);
        refresh.Margin = new Thickness(0, 0, 8, 0);
        var close = CreateDialogTextButton("关闭", 88);
        refresh.Click += (_, _) => box.Text = ReadEventLogTail();
        close.Click += (_, _) => dialog.Close();
        var buttonRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 10, 10),
            Children = { refresh, close },
        };
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);
        root.Children.Add(box);
        dialog.Content = root;
        dialog.SetClientSize(new Size(780, 480));
        box.Text = ReadEventLogTail();
        LastEventLogDialogForTests = dialog;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            if (ReferenceEquals(LastEventLogDialogForTests, dialog))
            {
                LastEventLogDialogForTests = null;
            }
        }
    }

    private static string ReadEventLogTail(int maxLines = 200)
    {
        var path = AppLog.Path;
        try
        {
            if (!File.Exists(path))
            {
                return "（日志文件尚不存在）" + path;
            }
            var lines = File.ReadLines(path)
                .Where(l => !string.IsNullOrEmpty(l))
                .TakeLast(maxLines)
                .ToArray();
            return lines.Length == 0 ? "（日志为空）" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return "（读取日志失败：" + ex.Message + "）";
        }
    }

    /// <summary>
    /// 打印终端内容（选区优先，无选区取整屏转录）。Avalonia 12 无打印 API，
    /// Windows 下走 PowerShell + WinForms 系统打印对话框（见 TerminalPrint）。
    /// </summary>
    internal async Task PrintTerminalAsync(TerminalControl terminal, TerminalTab? tab)
    {
        var term = terminal.Terminal;
        var text = term.Selection.HasSelection
            ? term.Selection.GetSelectionText()
            : TerminalTranscript.Capture(terminal);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (tab is not null)
            {
                tab.StatusMessage = "无内容可打印";
                tab.StatusBrush = StatusInfoBrush;
            }
            return;
        }
        var (ok, message) = await TerminalPrint.PrintTextAsync(text);
        if (tab is not null)
        {
            tab.StatusMessage = message;
            tab.StatusBrush = ok ? StatusInfoBrush : StatusErrorBrush;
        }
    }

    /// <summary>
    /// <c>--selftest-r38</c>：实机自检 r38 三项，结果写 run.log 后退出：
    /// (1) RTF 剪贴板 CF_RTF 回读——经与生产同路径（Win32 直写 CF_RTF ANSI）写入，
    ///     再由 WinForms Clipboard（绕过 Avalonia）回读完整内容验证可还原性；
    /// (2) 事件日志对话框——打开、核验标记行、关闭；
    /// (3) 打印——IsSupported 与 PowerShell 脚本可解析性（不实际弹系统打印对话框，
    ///     避免干扰用户桌面）。
    /// </summary>
    private async Task RunR38SelfTestAsync()
    {
        try
        {
            AppLog.Info("[SelfTest] r38: 等主窗稳定 1.5s 后自检 RTF/事件日志/打印");
            await Task.Delay(1500);

            // 1) RTF 剪贴板 CF_RTF 回读（经 WinForms 直读 CF_RTF 字节内容，绕过 Avalonia，
            //    验证生产写入路径产出的 CF_RTF 可被标准读取方完整还原）
            if (OperatingSystem.IsWindows())
            {
                var rtfMarker = "R38-RTF-" + DateTime.Now.ToString("HHmmss");
                var rtf = "{\\rtf1\\ansi{\\colortbl ;\\red255\\green0\\blue0;}" + rtfMarker + "\\par}";
                // 与菜单动作同一路径：Win32 P/Invoke 直写（CF_RTF ANSI + CF_UNICODETEXT）。
                var writeOk = Win32RtfClipboard.SetRtfAndText(rtf, rtfMarker);
                // 回读必须 id 基直读（与 Word 读 CF_RTF 同 API：GetClipboardData(0x0305)+ANSI 解码）：
                // PS 5.1 Get-Clipboard -Format 无 RTF（实机报错坐实）；本 VM user32 被剥离
                // IsClipboardFormatPresent/GetClipboardFormatName/RegisterClipboardFormat
                // （GetProcAddress 枚举坐实）→ 名称基查找不可用；WinForms OLE 读对裸
                // SetClipboardData 写的格式不可见（实机 RTF=ABSENT 坐实）。
                const string readback = @"
$ProgressPreference='SilentlyContinue'
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition '
using System;
using System.Runtime.InteropServices;
public static class ClipCheck {
    [DllImport(""user32.dll"")] public static extern bool OpenClipboard(IntPtr h);
    [DllImport(""user32.dll"")] public static extern bool CloseClipboard();
    [DllImport(""user32.dll"")] public static extern IntPtr GetClipboardData(uint f);
    [DllImport(""kernel32.dll"")] public static extern IntPtr GlobalLock(IntPtr h);
    [DllImport(""kernel32.dll"")] public static extern bool GlobalUnlock(IntPtr h);
    public static string ReadFormat(uint fmt, bool asAnsi) {
        if (!OpenClipboard(IntPtr.Zero)) { return ""OPEN-FAIL""; }
        try {
            IntPtr h = GetClipboardData(fmt);
            if (h == IntPtr.Zero) { return ""ABSENT""; }
            IntPtr p = GlobalLock(h);
            if (p == IntPtr.Zero) { GlobalUnlock(h); return ""LOCK-FAIL""; }
            string s = asAnsi ? Marshal.PtrToStringAnsi(p) : Marshal.PtrToStringUni(p);
            GlobalUnlock(h);
            return ""LEN="" + s.Length + "" | HEAD="" + s.Substring(0, Math.Min(160, s.Length));
        } finally { CloseClipboard(); }
    }
}'
'FORMATS=' + ([System.Windows.Forms.Clipboard]::GetDataObject().GetFormats() -join '|')
'RTF=' + [ClipCheck]::ReadFormat(0x0305, $true)
'UTF16=' + [ClipCheck]::ReadFormat(13, $false)
";
                var (code, outp, err) = await TerminalPrint.RunPowershellAsync(readback);
                AppLog.Info($"[SelfTest] r38: rtf-clip write={writeOk} code={code} hasMarker={(outp ?? "").Contains(rtfMarker)} " +
                    $"out=[{TerminalPrint.TrimNoise(outp)}] err=[{TerminalPrint.TrimNoise(err)}]");
            }
            else
            {
                // 非 Windows：生产路径为 Avalonia DataTransfer（X11 UTF-8/MIME），
                // 无 WinForms 可回读，只核验写入不抛。
                var rtfMarker = "R38-RTF-" + DateTime.Now.ToString("HHmmss");
                var rtf = "{\\rtf1\\ansi{\\colortbl ;\\red255\\green0\\blue0;}" + rtfMarker + "\\par}";
                var writeOk = await TerminalRtfClipboard.SetAsync(TopLevel.GetTopLevel(this), rtf, rtfMarker);
                AppLog.Info($"[SelfTest] r38: rtf-clip(non-win) write={writeOk} (Avalonia DataTransfer 路径)");
            }

            // 2) 事件日志对话框
            var marker = "R38-EVENTLOG-" + DateTime.Now.ToString("HHmmss");
            AppLog.Info(marker);
            var logTask = ShowEventLogDialogAsync();
            var waited = 0;
            while (LastEventLogDialogForTests is null && waited < 10_000)
            {
                await Task.Delay(100);
                waited += 100;
            }
            if (LastEventLogDialogForTests is { } dlg)
            {
                var box = dlg.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                AppLog.Info($"[SelfTest] r38: eventlog hasMarker={box?.Text.Contains(marker)} " +
                    $"textLen={(box?.Text?.Length) ?? 0}");
                var closeBtn = dlg.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(b => string.Equals(b.Content, "关闭"));
                closeBtn?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else
            {
                AppLog.Warn("[SelfTest] r38: FAILED: 事件日志对话框 10s 未打开");
            }
            await logTask;

            // 3) 打印脚本可解析性（不实际弹打印对话框）
            var script = TerminalPrint.BuildScript(Path.Combine(Path.GetTempPath(), "comtool-selftest-print.txt"));
            var scriptFile = Path.Combine(Path.GetTempPath(), "comtool-selftest-print.ps1");
            File.WriteAllText(scriptFile, script);
            try
            {
                var parseScript =
                    $"$sb=[scriptblock]::Create((Get-Content -Raw '{scriptFile.Replace("'", "''")}')); 'PARSE-OK'";
                var (code, outp, err) = await TerminalPrint.RunPowershellAsync(parseScript);
                AppLog.Info($"[SelfTest] r38: print supported={TerminalPrint.IsSupported} " +
                    $"scriptParse code={code} out=[{TerminalPrint.TrimNoise(outp)}] err=[{TerminalPrint.TrimNoise(err)}]");
            }
            finally
            {
                try
                {
                    File.Delete(scriptFile);
                }
                catch
                {
                    // best-effort
                }
            }

            AppLog.Info("[SelfTest] r38: 自检完成，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r38: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r39</c>：实机自检 r39 三子菜单，结果写 run.log 后退出：
    /// (0) 几何诊断（r41）——窗口/终端尺寸、库网格度量（CharWidth/CharHeight/
    ///     GutterWidth/grid CxR）、实测字形步进与 DPI 缩放，核对 pty 列数一致性；
    /// (1) 语法高亮——独立终端（不接 pty）+ error_warning 方案，写探测行后回读
    ///     单元格前景色码，验证 LineFed 着色链路（红/黄/绿/品红）；
    /// (2) 终端显示——清回滚（YBase 归零且当前屏保留）、RIS 复位（全屏空白）、
    ///     滚动条 toggle、全屏 toggle、设置标题对话框自动确认、只读标志；
    /// (3) 特殊命令——打印 5 类会话可用性矩阵，本地派发 SIGINT 走「不支持」
    ///     提示路径（错误对话框自动确认）。
    /// 退出前把高亮方案恢复为原值，避免污染用户配置。
    /// </summary>
    private async Task RunR39SelfTestAsync()
    {
        var originalScheme = _config?.Config.Terminal.SyntaxHighlight ?? "default";
        try
        {
            AppLog.Info("[SelfTest] r39: 等主窗稳定 1.5s 后自检三子菜单");
            await Task.Delay(1500);

            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            // 独立自检终端：不附加任何 pty，直接写 XTerm 核心缓冲区（确定性）
            var terminal = CreateTerminal();
            var tab = new TerminalTab
            {
                Title = "r39-selftest",
                SessionId = "terminal:r39selftest",
                Kind = SessionKind.Local,
                Content = terminal,
                Terminal = terminal,
                IsAlive = true,
                CloseAction = () => { },
            };
            viewModel.AddTab(tab);
            await Task.Delay(400); // 等布局（滚动条模板实例化）

            // ── 0) 几何诊断（r41）──
            // 记录窗口/终端尺寸、库网格度量（= pty 协商依据）与实际字形步进，
            // 用于核对 pty 列数是否与视口实际可见列一致（右缘裁切排查）。
            var dpiScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            var charW = terminal.CharWidth;
            var charH = terminal.CharHeight;
            var gutter = terminal.GutterWidth;
            var gridCols = terminal.Terminal?.Cols ?? 0;
            var gridRows = terminal.Terminal?.Rows ?? 0;
            var calcCols = (charW > 0)
                ? (int)((terminal.Bounds.Width - Math.Max(0, gutter)) / charW) : 0;
            var measAdvance = 0.0;
            try
            {
                var measTypeface = new Typeface(TerminalFontFamily(), FontStyle.Normal, FontWeight.Normal);
                measAdvance = new FormattedText(
                    "MMMMMMMMMM",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    measTypeface,
                    terminal.FontSize,
                    Brushes.White).Width / 10.0;
            }
            catch
            {
                // 度量失败不影响自检主流程
            }
            AppLog.Info(
                $"[SelfTest] r41: geometry win={Bounds.Width:0.#}x{Bounds.Height:0.#} " +
                $"term={terminal.Bounds.Width:0.#}x{terminal.Bounds.Height:0.#} " +
                $"charW={charW:0.###} charH={charH:0.###} gutter={gutter:0.###} " +
                $"grid={gridCols}x{gridRows} calcCols={calcCols} " +
                $"glyphAdvance={measAdvance:0.###} dpi={dpiScale:0.###}");

            // ── 0b) 中英文混排度量（r43）──
            // 用户实屏：中英文混排时中文比英文「低一截、看着也小」，MobaXterm 是居中的。
            // 这里把「字体链在 FontSize 下被量成什么」和「真正画出来的像素」一起留证：
            // 前者能排除 Avalonia 的形状/回退问题，后者供逐像素比对（拉 cjk-content.png）。
            try
            {
                var cjkTypeface = new Typeface(TerminalFontFamily(), FontStyle.Normal, FontWeight.Normal);
                var latFmt = new FormattedText(
                    "MMMM", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, cjkTypeface, terminal.FontSize, Brushes.White);
                var cjkFmt = new FormattedText(
                    "方案搭建", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, cjkTypeface, terminal.FontSize, Brushes.White);
                AppLog.Info(
                    $"[SelfTest] r43: metrics size={terminal.FontSize:0.###} " +
                    $"cell={terminal.CharWidth:0.###}x{terminal.CharHeight:0.###} | " +
                    $"latin W={latFmt.Width:0.###} H={latFmt.Height:0.###} base={latFmt.Baseline:0.###} | " +
                    $"cjk W={cjkFmt.Width:0.###} H={cjkFmt.Height:0.###} base={cjkFmt.Baseline:0.###}");
            }
            catch (Exception ex)
            {
                AppLog.Info($"[SelfTest] r43: metrics failed {ex.GetType().Name}: {ex.Message}");
            }

            terminal.Terminal.Write("CJK 方案搭建.docx ABC 中文 12345\r\n");
            await Task.Delay(300);
            SaveVisualPng(terminal, "cjk-content.png");

            // ── 1) 语法高亮 ──
            // 先设方案再挂着色器：core 未实现时着色器走 Loaded 延迟路径，
            // 延迟创建会读 config 当前方案；且必须等着色器就位后再写探测行
            // （水印在创建时定格，之后的行才染）。
            SetSyntaxHighlightScheme("error_warning");
            AttachTerminalHighlighter(terminal);
            var waitedHl = 0;
            while (GetHighlighterForTests(terminal) is null && waitedHl < 10_000)
            {
                await Task.Delay(100);
                waitedHl += 100;
            }
            var buf = terminal.Terminal.Buffer;
            var probe = "R39PROBE Error: fatal crash warning success 192.168.1.1 localhost";
            terminal.Terminal.Write(probe + "\r\n");
            await Task.Delay(100);
            var probeLine = buf.GetLine(0);
            var probeText = probeLine?.TranslateToString(true, 0, buf.Cols) ?? "";
            if (probeLine is null || !probeText.Contains("R39PROBE"))
            {
                AppLog.Warn($"[SelfTest] r39: highlight FAILED: 探测行缺失 line0=[{probeText}]");
            }
            else
            {
                int cErr = probeLine[probeText.IndexOf("Error", StringComparison.Ordinal)].Attributes.GetFgColor();
                int cCrash = probeLine[probeText.IndexOf("crash", StringComparison.Ordinal)].Attributes.GetFgColor();
                int cWarn = probeLine[probeText.IndexOf("warning", StringComparison.Ordinal)].Attributes.GetFgColor();
                int cOk = probeLine[probeText.IndexOf("success", StringComparison.Ordinal)].Attributes.GetFgColor();
                int cIp = probeLine[probeText.IndexOf("192.168.1.1", StringComparison.Ordinal)].Attributes.GetFgColor();
                int cLoc = probeLine[probeText.IndexOf("localhost", StringComparison.Ordinal)].Attributes.GetFgColor();
                int cPlain = probeLine[0].Attributes.GetFgColor(); // 'R' of R39PROBE（非关键字）
                AppLog.Info($"[SelfTest] r39: highlight line=[{probeText.TrimEnd()}] " +
                    $"Error_fg={cErr}(1红) crash_fg={cCrash}(1红) warning_fg={cWarn}(3黄) " +
                    $"success_fg={cOk}(2绿) ip_fg={cIp}(5品红) localhost_fg={cLoc}(5品红) " +
                    $"plain_fg={cPlain}(256默认) " +
                    $"ok={cErr == 1 && cCrash == 1 && cWarn == 3 && cOk == 2 && cIp == 5 && cLoc == 5 && cPlain == 256}");
            }

            // ── 2) 终端显示 ──
            for (var i = 0; i < 60; i++)
            {
                terminal.Terminal.Write($"R39FILL {i:00}\r\n");
            }
            await Task.Delay(100);
            var ybaseBefore = buf.YBase;
            ClearTerminalScrollback(terminal);
            var screenBlank = true;
            for (var y = 0; y < buf.Rows; y++)
            {
                var t = buf.GetLine(y)?.TranslateToString(true, 0, buf.Cols) ?? "";
                if (t.Trim().Length > 0)
                {
                    screenBlank = false;
                }
            }
            AppLog.Info($"[SelfTest] r39: clear-scrollback YBase {ybaseBefore} -> {buf.YBase} " +
                $"ok={ybaseBefore > 0 && buf.YBase == 0 && screenBlank} screenBlank={screenBlank}");

            var sbToggled = ToggleTerminalScrollBar(terminal);
            var bars = terminal.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>().ToList();
            var sbShown = bars.Count > 0 ? bars[0].IsVisible : (bool?)null; // 第一次 toggle 后（显示态）
            ToggleTerminalScrollBar(terminal); // 还原
            AppLog.Info($"[SelfTest] r39: scrollbar toggle ok={sbToggled} bars={bars.Count} " +
                $"visibleAfterShow={sbShown}");

            // 全屏 F11（r40 用户场景：最大化窗口 F11 进全屏，再 F11 必须回最大化）
            var stateOrig = WindowState;
            WindowState = WindowState.Maximized;
            ToggleFullScreen();
            var stateFs = WindowState;
            ToggleFullScreen();
            var stateRestored = WindowState;
            var fsOk = stateFs == WindowState.FullScreen && stateRestored == WindowState.Maximized;
            WindowState = stateOrig; // 还原用户原窗口态
            AppLog.Info($"[SelfTest] r39: fullscreen Maximized -> {stateFs} -> {stateRestored} " +
                $"ok={fsOk}{(OperatingSystem.IsWindows() ? "" : "（X11 无 WM 环境可能不支持，以 Win32 实机为准）")}");

            // 设置标题：自动确认对话框
            var titleTask = SetTerminalTitleAsync(tab);
            var waited = 0;
            while (LastTitleDialogForTests is null && waited < 10_000)
            {
                await Task.Delay(100);
                waited += 100;
            }
            var titleDlgSize = new Size(0, 0);
            if (LastTitleDialogForTests is { } tdlg)
            {
                var input = tdlg.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (input is not null)
                {
                    input.Text = "R39-标题自检";
                }
                titleDlgSize = tdlg.ClientSize;
                var okBtn = tdlg.GetVisualDescendants().OfType<Button>().FirstOrDefault(b =>
                    string.Equals(b.Content, "确定")
                    || b.GetVisualDescendants().OfType<TextBlock>().Any(tb => tb.Text == "确定"));
                okBtn?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await titleTask;
            AppLog.Info($"[SelfTest] r39: set-title result=[{tab.Title}] ok={tab.Title == "R39-标题自检"} " +
                $"dlgClientSize={titleDlgSize}");

            tab.IsReadOnlyTerminal = true;
            var roBlocked = IsTerminalInputBlockedForTests(terminal);
            tab.IsReadOnlyTerminal = false;
            AppLog.Info($"[SelfTest] r39: readonly flag -> blocked={roBlocked} ok={roBlocked}");

            // ── 3) 特殊命令 ──
            foreach (var kind in new[] { SessionKind.Local, SessionKind.Serial, SessionKind.Tcp, SessionKind.Telnet, SessionKind.Ssh })
            {
                var top = SpecialCommands.TopLevel.Count(d => SpecialCommands.IsAvailable(d, kind));
                var more = SpecialCommands.More.Count(d => SpecialCommands.IsAvailable(d, kind));
                AppLog.Info($"[SelfTest] r39: special-cmd availability {kind}: top {top}/5 more {more}/14");
            }

            // 本地会话派发 SIGINT：应判定「不支持」并弹错误对话框（自动确认）
            var dispatchTask = DispatchSpecialCommandAsync(tab, terminal, SpecialCommands.TopLevel[1]);
            var waited2 = 0;
            while (LastErrorDialogForTests is null && waited2 < 10_000)
            {
                await Task.Delay(100);
                waited2 += 100;
            }
            if (LastErrorDialogForTests is { } edlg)
            {
                var closeBtn = edlg.GetVisualDescendants().OfType<Button>().FirstOrDefault(b =>
                    string.Equals(b.Content, "确定")
                    || b.GetVisualDescendants().OfType<TextBlock>().Any(tb => tb.Text == "确定"));
                closeBtn?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await dispatchTask;
            AppLog.Info("[SelfTest] r39: special-cmd dispatch local-SIGINT done (期望 unsupported/failed，错误框已自动确认)");

            // ── 清理：恢复方案、关闭自检终端 ──
            SetSyntaxHighlightScheme(originalScheme);
            AppLog.Info($"[SelfTest] r39: 高亮方案已恢复为 {originalScheme}");
            ReleaseHighlighterForTab(tab);
            viewModel.CloseTab(tab);

            AppLog.Info("[SelfTest] r39: 自检完成，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0);
        }
        catch (Exception ex)
        {
            try
            {
                SetSyntaxHighlightScheme(originalScheme);
            }
            catch
            {
                // 恢复失败不影响退出
            }
            AppLog.Error("[SelfTest] r39: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r44</c>：实机自检 r44 标签类型图标（r44：标签头用会话类型图标取代统一绿点）。
    /// 开一对「假」Serial + SSH tab（不接真实连接，CreateTerminal 空终端），SSH 选中、串口未选中，
    /// 打印两 tab 的字形/图标刷颜色/选中态与几何，落盘 <c>main-content.png</c>（主窗整体，含标签条）
    /// 供实机核对「串口 vs SSH 图标区别分明、选中态白图标」。
    /// </summary>
    private async Task RunR44SelfTestAsync()
    {
        string Hex(Avalonia.Media.IBrush? b) => b is Avalonia.Media.ISolidColorBrush sb
            ? string.Format("#{0:X2}{1:X2}{2:X2}", sb.Color.R, sb.Color.G, sb.Color.B) : "?";
        try
        {
            AppLog.Info("[SelfTest] r44: 等主窗稳定 1.5s 后开 Serial+SSH tab 验证标签类型图标");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var t1 = CreateTerminal();
            var serialTab = new TerminalTab
            {
                Title = "COM99",
                SessionId = "serial:r44selftest",
                Kind = SessionKind.Serial,
                Content = t1,
                Terminal = t1,
                IsAlive = true,
                CloseAction = () => { },
            };
            var t2 = CreateTerminal();
            var sshTab = new TerminalTab
            {
                Title = "devuser@192.0.2.48",
                SessionId = "ssh:r44selftest",
                Kind = SessionKind.Ssh,
                Content = t2,
                Terminal = t2,
                IsAlive = true,
                CloseAction = () => { },
            };
            viewModel.AddTab(serialTab);
            viewModel.AddTab(sshTab);   // 依次 AddTab：最终 sshTab 被选中、serialTab 未选中
            await Task.Delay(500);
            UpdateLayout();

            AppLog.Info($"[SelfTest] r44: Serial tab glyph=\u201c{serialTab.KindIconGlyph}\u201d color={Hex(serialTab.KindIconBrush)} selected={serialTab.IsSelected}");
            AppLog.Info($"[SelfTest] r44: SSH    tab glyph=\u201c{sshTab.KindIconGlyph}\u201d color={Hex(sshTab.KindIconBrush)} selected={sshTab.IsSelected}");

            // 几何（r41 右缘裁切排查项顺带再记一次）
            var dpiScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            var term = sshTab.Terminal;
            var charW = term?.CharWidth ?? 0;
            var charH = term?.CharHeight ?? 0;
            var gutter = term?.GutterWidth ?? 0;
            var gridCols = term?.Terminal?.Cols ?? 0;
            var gridRows = term?.Terminal?.Rows ?? 0;
            var calcCols = (charW > 0 && term != null) ? (int)((term.Bounds.Width - Math.Max(0, gutter)) / charW) : 0;
            AppLog.Info($"[SelfTest] r41: geometry win={Bounds.Width:0.#}x{Bounds.Height:0.#} term={(term?.Bounds.Width ?? 0):0.#}x{(term?.Bounds.Height ?? 0):0.#} charW={charW:0.###} charH={charH:0.###} gutter={gutter:0.###} grid={gridCols}x{gridRows} calcCols={calcCols} dpi={dpiScale:0.###}");

            SaveVisualPng(Content as Visual, "main-content.png");
            AppLog.Info("[SelfTest] r44: 已落盘 main-content.png，自检完成，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r44: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r45</c>：实机自检鼠标选中自动复制（MobaXterm 对齐）。开一个本地终端，
    /// 写入探测行后在内层 TerminalView 上模拟按下→拖动→松开，随后读回系统剪贴板核对
    /// 内容并确认选区保留（高亮不消失）。结果写 run.log，落盘 main-content.png（含选区高亮）。
    /// </summary>
    private async Task RunR45SelfTestAsync()
    {
        const string probe = "SELECT TO COPY R45"; // 18 个字符，列 0..17
        try
        {
            AppLog.Info("[SelfTest] r45: 等主窗稳定 1.5s 后开本地终端验证选中自动复制");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var terminal = CreateTerminal();
            var tab = new TerminalTab
            {
                Title = "r45-selftest",
                SessionId = "local:r45selftest",
                Kind = SessionKind.Local,
                Content = terminal,
                Terminal = terminal,
                IsAlive = true,
                CloseAction = () => { },
            };
            viewModel.AddTab(tab);
            await Task.Delay(500);
            UpdateLayout();
            terminal.Terminal.Write(probe + "\r\n");
            await Task.Delay(500);
            UpdateLayout();

            var view = terminal.GetVisualDescendants()
                .OfType<Iciclecreek.Terminal.TerminalView>()
                .FirstOrDefault();
            if (view is null)
            {
                throw new InvalidOperationException("未找到内层 TerminalView");
            }
            var charW = view.CharWidth;
            var charH = view.CharHeight;
            if (charW <= 0 || charH <= 0)
            {
                throw new InvalidOperationException($"终端度量未就绪: charW={charW} charH={charH}");
            }
            AppLog.Info($"[SelfTest] r45: charW={charW:0.###} charH={charH:0.###} gutter={view.GutterWidth:0.###} grid={terminal.Terminal.Cols}x{terminal.Terminal.Rows}");

            // 模拟整行拖选：列 0 按下 → 列 17（末字符）松开，松开左键即触发选中即复制。
            // 事件位置以窗口（TopLevel）坐标系给出：先算 view 局部坐标再 TranslatePoint
            // 换算（库内 GetPosition(view) 会从 root 坐标换算回 view 局部，直接给 view 局部
            // 坐标会整体平移一个 view 在窗口中的偏移——首轮实机选区偏左 58px 即此因）。
            var win = TopLevel.GetTopLevel(view)!;
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            Point WinPoint(double col, double rowFrac)
            {
                var local = new Point(view.GutterWidth + charW * (col + 0.5), charH * rowFrac);
                return view.TranslatePoint(local, win) ?? local;
            }
            var p0 = WinPoint(0, 0.5);
            var p1 = WinPoint(17, 0.5);
            view.RaiseEvent(new PointerPressedEventArgs(view, pointer, win,
                p0, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None)
            {
                RoutedEvent = InputElement.PointerPressedEvent,
            });
            await Task.Delay(100);
            view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view, pointer, win,
                p1, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None));
            await Task.Delay(100);
            view.RaiseEvent(new PointerReleasedEventArgs(view, pointer, win,
                p1, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                KeyModifiers.None, MouseButton.Left)
            {
                RoutedEvent = InputElement.PointerReleasedEvent,
            });
            await Task.Delay(800); // 异步剪贴板写入留出余量

            bool hasSelection = terminal.Terminal.Selection.HasSelection;
            string? clip = null;
            try
            {
                clip = await win.Clipboard?.TryGetTextAsync();
            }
            catch (Exception ex)
            {
                AppLog.Info($"[SelfTest] r45: 剪贴板读取失败: {ex.Message}");
            }
            // 断言剪贴板整行等于探测文本（允许首尾空白差异）；空/无则说明自动复制未生效。
            bool copied = string.Equals((clip ?? "").Trim(), probe, StringComparison.Ordinal);
            AppLog.Info($"[SelfTest] r45: selection={hasSelection} clipboard='{clip}' autoCopy={(copied ? "OK" : "FAILED")}");

            UpdateLayout();
            SaveVisualPng(Content as Visual, "main-content.png");
            AppLog.Info($"[SelfTest] r45: 自检完成，选中自动复制={(copied ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r45: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r46</c>：实机自检「SSH 重启成功后 Restarting 进度条不再挂死」。
    /// 打开配置中第一个 SSH 会话，连接成功后触发一次 R 重启（RestartSshSessionAsync），
    /// 重启完成后断言 IsRestarting==false 且状态栏进度区隐藏（r46 修复前成功路径漏清该
    /// 标志，右下角「Restarting...」会一直显示），落盘 main-content.png 取证。
    /// </summary>
    private async Task RunR46SelfTestAsync()
    {
        try
        {
            AppLog.Info("[SelfTest] r46: 等主窗稳定 1.5s 后打开 SSH 会话验证重启进度条清除");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var firstKey = _config?.SshSessions.Sessions.FirstOrDefault()?.Key;
            if (string.IsNullOrEmpty(firstKey))
            {
                throw new InvalidOperationException("config 中没有 SSH 会话，无法跑 r46 自检");
            }
            AppLog.Info($"[SelfTest] r46: 打开 SSH 会话 {firstKey}");

            var openTask = OpenSshSessionAsync(firstKey!);
            TerminalTab? tab = null;
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!(openTask.IsCompleted && tab is not null && tab.IsAlive))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("SSH 会话 60s 内未连接成功");
                }
                tab = viewModel.Tabs.FirstOrDefault(t => t.SessionId == firstKey);
                await Task.Delay(250);
            }
            await openTask;
            AppLog.Info($"[SelfTest] r46: 首次连接成功 status='{tab!.StatusMessage}' alive={tab.IsAlive} restarting={tab.IsRestarting}");

            // 触发一次 R 重启：r46 修复前成功路径漏清 IsRestarting，
            // 重连成功后右下角「Restarting...」进度条会一直挂着。
            var restartTask = RestartSshSessionAsync(tab);
            deadline = DateTime.UtcNow.AddSeconds(90);
            while (!(restartTask.IsCompleted && !tab.IsRestarting))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("SSH 重启 90s 内未完成或 IsRestarting 未清");
                }
                await Task.Delay(250);
            }
            await restartTask;
            await Task.Delay(500);

            bool ok = !tab.IsRestarting && !tab.ShowStatusProgress && tab.IsAlive
                && tab.StatusMessage.StartsWith("Connected", StringComparison.Ordinal);
            AppLog.Info($"[SelfTest] r46: 重启后 IsRestarting={tab.IsRestarting} ShowStatusProgress={tab.ShowStatusProgress} status='{tab.StatusMessage}' alive={tab.IsAlive} => {(ok ? "OK" : "FAILED")}");

            UpdateLayout();
            SaveVisualPng(Content as Visual, "main-content.png");
            AppLog.Info($"[SelfTest] r46: 自检完成，重启成功后进度条清除={(ok ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r46: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r47</c>：实机自检 MobaXterm 对齐的终端鼠标交互：
    /// 1) 双击路径选中整条路径（而非仅一个词段）；2) 指针悬停终端显示区域时呈文本型 IBeam。
    /// 开本地终端写探测路径行，合成悬停+双击，核对选区内容与光标类型，截图留证选区高亮。
    /// </summary>
    private async Task RunR47SelfTestAsync()
    {
        const string probePath = "/home/devuser/tftp_dir/patent"; // 28 字符，列 0..27
        try
        {
            AppLog.Info("[SelfTest] r47: 等主窗稳定 1.5s 后开本地终端验证双击路径选中与 IBeam 光标");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var terminal = CreateTerminal();
            var tab = new TerminalTab
            {
                Title = "r47-selftest",
                SessionId = "local:r47selftest",
                Kind = SessionKind.Local,
                Content = terminal,
                Terminal = terminal,
                IsAlive = true,
                CloseAction = () => { },
            };
            viewModel.AddTab(tab);
            await Task.Delay(500);
            UpdateLayout();
            terminal.Terminal.Write(probePath + "\r\n");
            await Task.Delay(500);
            UpdateLayout();

            var view = terminal.GetVisualDescendants()
                .OfType<Iciclecreek.Terminal.TerminalView>()
                .FirstOrDefault();
            if (view is null)
            {
                throw new InvalidOperationException("未找到内层 TerminalView");
            }
            var charW = view.CharWidth;
            var charH = view.CharHeight;
            if (charW <= 0 || charH <= 0)
            {
                throw new InvalidOperationException($"终端度量未就绪: charW={charW} charH={charH}");
            }
            AppLog.Info($"[SelfTest] r47: charW={charW:0.###} charH={charH:0.###} gutter={view.GutterWidth:0.###} grid={terminal.Terminal.Cols}x{terminal.Terminal.Rows}");

            // 事件位置以窗口（TopLevel）坐标系给出：先算 view 局部坐标再 TranslatePoint 换算
            // （库内 GetPosition(view) 从 root 坐标换算，直接给 view 局部坐标会整体偏掉 view
            // 在窗口中的 x 偏移——r45 实机教训）。
            var win = TopLevel.GetTopLevel(view)!;
            Point WinPoint(double col, double rowFrac)
            {
                var local = new Point(view.GutterWidth + charW * (col + 0.5), charH * rowFrac);
                return view.TranslatePoint(local, win) ?? local;
            }

            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var p0 = WinPoint(8, 0.5); // 路径中段（第 8 列 'g'，devuser 词段内）

            // 1) 悬停：光标应由箭头变为 IBeam
            view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view, pointer, win,
                p0, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None));
            await Task.Delay(100);
            var cursorName = view.Cursor?.ToString() ?? "null";
            bool cursorOk = cursorName == "Ibeam";
            AppLog.Info($"[SelfTest] r47: 悬停 cursor='{cursorName}' => {(cursorOk ? "OK" : "FAILED")}");

            // 2) 双击路径中段：应选中整条路径
            var pressProps = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            view.RaiseEvent(new PointerPressedEventArgs(view, pointer, win,
                p0, 0, pressProps, KeyModifiers.None, 1)
            {
                RoutedEvent = InputElement.PointerPressedEvent,
            });
            await Task.Delay(100);
            view.RaiseEvent(new PointerPressedEventArgs(view, pointer, win,
                p0, 0, pressProps, KeyModifiers.None, 2)
            {
                RoutedEvent = InputElement.PointerPressedEvent,
            });
            await Task.Delay(300);

            bool hasSelection = terminal.Terminal.Selection.HasSelection;
            string? selected = terminal.Terminal.Selection.GetSelectionText();
            bool selectOk = selected == probePath;
            AppLog.Info($"[SelfTest] r47: selection={hasSelection} selectedText='{selected}' => {(selectOk ? "OK" : "FAILED")}");

            bool ok = cursorOk && selectOk;
            UpdateLayout();
            SaveVisualPng(Content as Visual, "main-content.png");
            AppLog.Info($"[SelfTest] r47: 自检完成，双击路径选中={(selectOk ? "OK" : "失败")}，IBeam 光标={(cursorOk ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r47: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r48</c>：实机自检「标签类型图标选中态保持原色」（MobaXterm 对齐）。
    /// r44 曾实现选中 tab 图标变白，用户反馈 MobaXterm 选中 tab 只换底色、图标保持
    /// 类型色（金色钥匙/绿色插头），r48 撤销。开一对假 Serial+SSH tab（SSH 选中），
    /// 断言两 tab 图标刷颜色均为类型色，落盘 main-content.png 供截图核对。
    /// </summary>
    private async Task RunR48SelfTestAsync()
    {
        string Hex(Avalonia.Media.IBrush? b) => b is Avalonia.Media.ISolidColorBrush sb
            ? string.Format("#{0:X2}{1:X2}{2:X2}", sb.Color.R, sb.Color.G, sb.Color.B) : "?";
        try
        {
            AppLog.Info("[SelfTest] r48: 等主窗稳定 1.5s 后开 Serial+SSH tab 验证选中态图标保持类型色");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var t1 = CreateTerminal();
            var serialTab = new TerminalTab
            {
                Title = "COM99",
                SessionId = "serial:r48selftest",
                Kind = SessionKind.Serial,
                Content = t1,
                Terminal = t1,
                IsAlive = true,
                CloseAction = () => { },
            };
            var t2 = CreateTerminal();
            var sshTab = new TerminalTab
            {
                Title = "devuser@192.0.2.48",
                SessionId = "ssh:r48selftest",
                Kind = SessionKind.Ssh,
                Content = t2,
                Terminal = t2,
                IsAlive = true,
                CloseAction = () => { },
            };
            viewModel.AddTab(serialTab);
            viewModel.AddTab(sshTab);   // 依次 AddTab：最终 sshTab 被选中、serialTab 未选中
            await Task.Delay(500);
            UpdateLayout();

            var serialColor = Hex(serialTab.KindIconBrush);
            var sshColor = Hex(sshTab.KindIconBrush);
            bool ok = serialTab.IsSelected == false
                && sshTab.IsSelected == true
                && serialColor == "#43A047"   // 串口插头绿
                && sshColor == "#F0B429";     // SSH 钥匙金（选中态保持金色，不再变白）
            AppLog.Info($"[SelfTest] r48: Serial glyph=\u201c{serialTab.KindIconGlyph}\u201d color={serialColor} selected={serialTab.IsSelected}");
            AppLog.Info($"[SelfTest] r48: SSH    glyph=\u201c{sshTab.KindIconGlyph}\u201d color={sshColor} selected={sshTab.IsSelected}");
            AppLog.Info($"[SelfTest] r48: 选中态图标保持类型色={(ok ? "OK" : "FAILED")}（期望 Serial=#43A047 SSH=#F0B429）");

            SaveVisualPng(Content as Visual, "main-content.png");
            AppLog.Info($"[SelfTest] r48: 自检完成，选中态图标类型色={(ok ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r48: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r49</c>：实机自检会话设置对话框布局修复：
    /// ① SSH Key file 浏览按钮（FontAwesome f141 省略号）与输入框同高且垂直中心对齐；
    /// ② 四面板标签列宽 ≥100，TCP「Auto reconnect」标签与复选框之间留出空隙。
    /// 打开真实对话框（不建连），切 SSH/TCP 面板各截一张图。
    /// </summary>
    private async Task RunR49SelfTestAsync()
    {
        try
        {
            AppLog.Info("[SelfTest] r49: 等主窗稳定 1.5s 后打开会话设置对话框验证布局");
            await Task.Delay(1500);
            var service = _config ?? AppConfigService.Load();
            Func<string, TerminalTab?, Task> open = (_, _) => Task.CompletedTask;
            var dlg = new ConnectionSettingsWindow(service, null, open, open, open, open, _ => Task.CompletedTask);
            dlg.Show();
            await Task.Delay(800);
            UpdateLayout();
            dlg.UpdateLayout();

            var combo = dlg.FindControl<ComboBox>("ConnectionTypeCombo")!;
            var ok = true;

            // ── SSH 面板：Key file 行 ──
            combo.SelectedIndex = 1;
            await Task.Delay(400);
            UpdateLayout();
            dlg.UpdateLayout();
            var btn = dlg.FindControl<Button>("KeyFileBrowseButton");
            var box = dlg.FindControl<TextBox>("KeyFileBox");
            if (btn is null || box is null || btn.Bounds.Height < 1)
            {
                AppLog.Error("[SelfTest] r49: KeyFileBrowseButton/KeyFileBox 未找到或未布局");
                ok = false;
            }
            else
            {
                var btnC = btn.Bounds.Y + btn.Bounds.Height / 2;
                var boxC = box.Bounds.Y + box.Bounds.Height / 2;
                var diff = Math.Abs(btnC - boxC);
                var sameH = Math.Abs(btn.Bounds.Height - box.Bounds.Height) <= 1.0;
                ok = ok && diff <= 1.0 && sameH;
                AppLog.Info($"[SelfTest] r49: SSH KeyFile button=({btn.Bounds.X:F0},{btn.Bounds.Y:F0},{btn.Bounds.Width:F0}x{btn.Bounds.Height:F0}) box=({box.Bounds.X:F0},{box.Bounds.Y:F0},{box.Bounds.Width:F0}x{box.Bounds.Height:F0}) 中心差={diff:F2}px 同高={(sameH ? "OK" : "FAILED")}");
                AppLog.Info($"[SelfTest] r49: 浏览按钮垂直居中={(diff <= 1.0 && sameH ? "OK" : "FAILED")}（字形 f141，FontAwesome 居中设计）");
            }
            SaveVisualPng(dlg, "r49-ssh-dialog.png");

            // ── TCP 面板：Auto reconnect 行 ──
            combo.SelectedIndex = 3;
            await Task.Delay(400);
            UpdateLayout();
            dlg.UpdateLayout();
            var widths = new[] { "SerialPanel", "SshPanel", "TelnetPanel", "TcpPanel" }
                .Select(n => dlg.FindControl<Grid>(n)?.ColumnDefinitions[0].Width.Value ?? 0)
                .ToArray();
            var colOk = widths.All(w => w >= 100) && widths.Distinct().Count() == 1;
            ok = ok && colOk;
            AppLog.Info($"[SelfTest] r49: 四面板标签列宽={string.Join("/", widths.Select(w => w.ToString("F0")))}（≥100 且一致: {(colOk ? "OK" : "FAILED")}）");
            var label = dlg.FindControl<TextBlock>("TcpAutoReconnectLabel");
            var check = dlg.FindControl<CheckBox>("TcpAutoReconnectCheck");
            if (label is not null && check is not null && label.Bounds.Height > 0)
            {
                AppLog.Info($"[SelfTest] r49: TCP AutoReconnect label=({label.Bounds.X:F0},{label.Bounds.Y:F0},{label.Bounds.Width:F0}x{label.Bounds.Height:F0}) checkbox=({check.Bounds.X:F0},{check.Bounds.Y:F0},{check.Bounds.Width:F0}x{check.Bounds.Height:F0})");
                // 实测标签文字渲染宽度与标签列宽的余量（复现用户「t 顶框」问题：
                // 旧 86px 列宽下余量仅约 2px；现 108px 列宽下余量应 ≥8px）
                var probe = new TextBlock { Text = "Auto reconnect" };
                if (!double.IsNaN(label.FontSize))
                {
                    probe.FontSize = label.FontSize;
                }
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var colW = dlg.FindControl<Grid>("TcpPanel")!.ColumnDefinitions[0].Width.Value;
                var margin = colW - probe.DesiredSize.Width;
                ok = ok && margin >= 8;
                AppLog.Info($"[SelfTest] r49: TCP AutoReconnect 文字实测宽={probe.DesiredSize.Width:F1}px 标签列宽={colW:F0}px 右侧余量={margin:F1}px（≥8px: {(margin >= 8 ? "OK" : "FAILED")}）");
            }
            SaveVisualPng(dlg, "r49-tcp-dialog.png");

            dlg.Close();
            AppLog.Info($"[SelfTest] r49: 自检完成，对话框布局={(ok ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r49: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r50</c>：实机自检会话设置对话框 OPEN/Close 按钮文字垂直居中。
    /// r50：Fluent 主题 Button 默认 VerticalContentAlignment=Stretch，文字顶到内容框上方，
    /// 用户截图反馈 OPEN 与 Close 按钮文字「中间偏上」。修复为显式两轴居中 + MinHeight 保底。
    /// 自检校验 5 个动作按钮的布局属性并整窗截图，供像素法核对文字是否居中。
    /// </summary>
    private async Task RunR50SelfTestAsync()
    {
        try
        {
            AppLog.Info("[SelfTest] r50: 等主窗稳定 1.5s 后打开会话设置对话框验证 OPEN/Close 按钮文字垂直居中");
            await Task.Delay(1500);
            var service = _config ?? AppConfigService.Load();
            Func<string, TerminalTab?, Task> open = (_, _) => Task.CompletedTask;
            var dlg = new ConnectionSettingsWindow(service, null, open, open, open, open, _ => Task.CompletedTask);
            dlg.Show();
            await Task.Delay(800);
            UpdateLayout();
            dlg.UpdateLayout();

            var ok = true;
            var names = new[] { "SerialOpenButton", "SshOpenButton", "TelnetOpenButton", "TcpOpenButton", "CloseButton" };
            foreach (var name in names)
            {
                var btn = dlg.FindControl<Button>(name);
                if (btn is null)
                {
                    AppLog.Error($"[SelfTest] r50: 未找到按钮 {name}");
                    ok = false;
                    continue;
                }
                var centred = btn.VerticalContentAlignment == Avalonia.Layout.VerticalAlignment.Center
                    && btn.HorizontalContentAlignment == Avalonia.Layout.HorizontalAlignment.Center;
                var heightFree = double.IsNaN(btn.Height) && btn.MinHeight >= 28;
                ok = ok && centred && heightFree;
                AppLog.Info($"[SelfTest] r50: {name} bounds=({btn.Bounds.X:F0},{btn.Bounds.Y:F0},{btn.Bounds.Width:F0}x{btn.Bounds.Height:F0}) 两轴居中={(centred ? "OK" : "FAILED")} 高度非写死={(heightFree ? "OK" : "FAILED")}");
            }
            // 默认 Serial 面板，其 OPEN 按钮可见，整窗截图供像素核对
            var openBtn = dlg.FindControl<Button>("SerialOpenButton");
            if (openBtn is not null)
            {
                AppLog.Info($"[SelfTest] r50: SerialOpen 文字=“OPEN” 按钮中心 y={(openBtn.Bounds.Y + openBtn.Bounds.Height / 2):F1}");
            }
            SaveVisualPng(dlg, "r50-dialog.png");

            dlg.Close();
            AppLog.Info($"[SelfTest] r50: 自检完成，OPEN/Close 按钮文字居中={(ok ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r50: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r51</c>：启动性能自检。启动阶段的 [Startup] 日志在窗口就绪前已陆续落盘
    /// （见 StartupClock.Mark），此处再补：独立串口扫描耗时、进程内总耗时（T0=Main 首行；
    /// 单文件自提取 + CLR 初始化发生在 T0 之前，用自检脚本外层 wall time 减去本值即可反出）。
    /// </summary>
    private async Task RunR51SelfTestAsync()
    {
        try
        {
            AppLog.Info("[SelfTest] r51: 启动性能自检（打印各阶段耗时后退出）");
            await Task.Delay(300);
            UpdateLayout();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var scan = SerialPortCatalog.Scan();
            sw.Stop();
            AppLog.Info($"[SelfTest] r51: 独立串口扫描耗时={sw.ElapsedMilliseconds}ms ports={scan.Ports.Count} success={scan.Success}");
            StartupClock.Mark("selftest-ready");
            AppLog.Info($"[SelfTest] r51: 进程内总耗时 T0→now={StartupClock.Ms:F0}ms（不含自提取/CLR 初始化）");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r51: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r53</c>：实机自检 CJK/Latin 混排基线。
    /// 开本地终端写入探测混排行，把整块 TerminalView 渲染进 DrawingGroup 并走查每条
    /// GlyphRun 的真实 baselineOrigin 与字体面，再逐一解析回退链各面的 'o'/'编' 墨迹
    /// 度量（YBearing/Height），落盘 r53-terminal.png。用于判定 AOT 实机下 CJK 走
    /// GlyphRun 快路径（共享主面基线）还是 FormattedText 路径（用 CJK 面自身 ascent），
    /// 以及两个面墨迹中心的真实偏差方向与幅度。
    /// </summary>
    private async Task RunR53SelfTestAsync()
    {
        const string probeLine = "Yocto imx-image-full 编译排查";
        const string probeTabTitle = "r53 编译排查 Yocto";
        try
        {
            AppLog.Info("[SelfTest] r53: 等主窗稳定 1.5s 后开本地终端写混排行并读真实基线");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var terminal = CreateTerminal();
            var tab = new TerminalTab
            {
                Title = probeTabTitle,
                SessionId = "local:r53selftest",
                Kind = SessionKind.Local,
                Content = terminal,
                Terminal = terminal,
                IsAlive = true,
                CloseAction = () => { },
            };
            viewModel.AddTab(tab);
            await Task.Delay(500);
            UpdateLayout();
            terminal.Terminal.Write(probeLine + "\r\n");
            await Task.Delay(600);
            UpdateLayout();

            var view = terminal.GetVisualDescendants()
                .OfType<Iciclecreek.Terminal.TerminalView>()
                .FirstOrDefault();
            if (view is null)
            {
                throw new InvalidOperationException("未找到内层 TerminalView");
            }
            AppLog.Info($"[SelfTest] r53: FontSize={view.FontSize:0.###} charW={view.CharWidth:0.###} charH={view.CharHeight:0.###} grid={terminal.Terminal.Cols}x{terminal.Terminal.Rows}");
            AppLog.Info("[SelfTest] r53: 回退链 = " + string.Join(" | ", view.FontFamily.FamilyNames));

            // 与库内 _measureText 相同的构造：主面（整条 family 链）的 line-top->baseline。
            var measure = new FormattedText("M", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(view.FontFamily), view.FontSize, Brushes.Black);
            AppLog.Info($"[SelfTest] r53: FormattedText('M').Baseline={measure.Baseline:0.###}px（主面基线参考）");

            // 逐一解析回退链各字体面：谁带 'o'/'编'，墨迹度量多少（yBearing 正=向上）。
            foreach (var fam in view.FontFamily.FamilyNames)
            {
                try
                {
                    var gt = new Typeface(fam).GlyphTypeface;
                    var hasO = gt.CharacterToGlyphMap.TryGetGlyph('o', out var go) && go != 0;
                    var hasC = gt.CharacterToGlyphMap.TryGetGlyph('编', out var gc) && gc != 0;
                    var line = $"[SelfTest] r53: 面 '{fam}' -> {gt.FamilyName}/{gt.FaceNames}  o={hasO}  CJK编={hasC}";
                    if (hasO && gt.TryGetGlyphMetrics(go, out var mo))
                        line += $"  o(yB={mo.YBearing:0.###},h={mo.Height:0.###})";
                    if (hasC && gt.TryGetGlyphMetrics(gc, out var mc))
                        line += $"  编(yB={mc.YBearing:0.###},h={mc.Height:0.###})";
                    AppLog.Info(line);
                }
                catch (Exception ex)
                {
                    AppLog.Info($"[SelfTest] r53: 面 '{fam}' 解析失败: {ex.Message}");
                }
            }

            // 真实绘制走查：每条 GlyphRun 的 baselineOrigin + 字体面；非 GlyphRun 绘制记类型。
            var group = new DrawingGroup();
            using (var ctx = group.Open())
                view.Render(ctx);
            var sb = new System.Text.StringBuilder();
            WalkR53Drawings(group, "", sb);
            AppLog.Info("[SelfTest] r53: 绘制走查开始");
            foreach (var l in sb.ToString().Split('\n'))
                AppLog.Info("[SelfTest] r53:" + l.TrimEnd());
            AppLog.Info("[SelfTest] r53: 绘制走查结束");

            // 标签页标题字体（App.axaml.cs 的 Windows 覆盖 = 单族 "Microsoft YaHei"，12px）。
            // 用户截图红框是标签栏：核对该字体链在实机上的存在性与 FormattedText 基线。
            var uiFamily = (FontFamily)Application.Current.Resources["UiFontFamily"];
            AppLog.Info("[SelfTest] r53: UiFontFamily = " + string.Join(" | ", uiFamily.FamilyNames));
            var uiMeasure = new FormattedText(probeTabTitle, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(uiFamily), 12, Brushes.Black);
            AppLog.Info($"[SelfTest] r53: 标签标题 FormattedText(12px).Baseline={uiMeasure.Baseline:0.###} Height={uiMeasure.Height:0.###}");
            foreach (var fam in uiFamily.FamilyNames)
            {
                try
                {
                    var gt = new Typeface(fam).GlyphTypeface;
                    var hasO = gt.CharacterToGlyphMap.TryGetGlyph('o', out var go) && go != 0;
                    var hasC = gt.CharacterToGlyphMap.TryGetGlyph('编', out var gc) && gc != 0;
                    var line = $"[SelfTest] r53: UI面 '{fam}' -> {gt.FamilyName}  o={hasO}  CJK编={hasC}";
                    if (hasO && gt.TryGetGlyphMetrics(go, out var mo))
                        line += $"  o(yB={mo.YBearing:0.###},h={mo.Height:0.###})";
                    if (hasC && gt.TryGetGlyphMetrics(gc, out var mc))
                        line += $"  编(yB={mc.YBearing:0.###},h={mc.Height:0.###})";
                    AppLog.Info(line);
                }
                catch (Exception ex)
                {
                    AppLog.Info($"[SelfTest] r53: UI面 '{fam}' 解析失败: {ex.Message}");
                }
            }
            try
            {
                var fontDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\Fonts";
                var fontFiles = Directory.GetFiles(fontDir, "msyh*")
                    .Concat(Directory.GetFiles(fontDir, "*yahei*"))
                    .Select(Path.GetFileName)
                    .Distinct()
                    .OrderBy(n => n);
                AppLog.Info("[SelfTest] r53: Windows Fonts 中 msyh/yahei 文件: " + string.Join(", ", fontFiles));
            }
            catch (Exception ex)
            {
                AppLog.Info("[SelfTest] r53: 字体目录枚举失败: " + ex.Message);
            }

            AppLog.Info("[SelfTest] r53: 自检完成，退出");
            SaveVisualPng(Content as Visual, "r53-terminal.png");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r53: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r54</c>：实机自检 r54 三项。
    /// ① 双击含连字符路径选中整条路径（库 4.1.6，用户实路径
    ///    /home/kami/nxp/yocto_imx-6.18.20-2.0.0，修复前只能选到片段）；
    /// ② SSH 连接成功后自动打开该会话的 SFTP 浏览器
    ///    （MobaXterm AutoStartSSHGUI=1）；
    /// ③ 浅色主题 MobaXterm 1:1 取色（标题栏白 / 选中 tab 深灰渐变
    ///    #4d4d4d→#1f1f1f / 未选中 tab 白底 #b4b4b4 边 / 终端底 #1c1c1c
    ///    前景 #ececec / 状态栏 #f0f0f0），读取计算值断言并落盘 r54-theme.png。
    /// </summary>
    private async Task RunR54SelfTestAsync()
    {
        const string probePath = "/home/kami/nxp/yocto_imx-6.18.20-2.0.0";
        var pathOk = false;
        var sshOk = false;
        var themeOk = false;
        try
        {
            AppLog.Info("[SelfTest] r54: 等主窗稳定 1.5s 后开始三项自检");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            // ── ① 双击含连字符路径：应选中整条路径而非片段 ──
            var terminal = CreateTerminal();
            var localTab = new TerminalTab
            {
                Title = "r54-selftest",
                SessionId = "local:r54selftest",
                Kind = SessionKind.Local,
                Content = terminal,
                Terminal = terminal,
                IsAlive = true,
                CloseAction = () => { },
            };
            viewModel.AddTab(localTab);
            await Task.Delay(500);
            UpdateLayout();
            terminal.Terminal.Write(probePath + "\r\n");
            await Task.Delay(500);
            UpdateLayout();

            var view = terminal.GetVisualDescendants()
                .OfType<Iciclecreek.Terminal.TerminalView>()
                .FirstOrDefault();
            if (view is null)
            {
                throw new InvalidOperationException("未找到内层 TerminalView");
            }
            var charW = view.CharWidth;
            var charH = view.CharHeight;
            if (charW <= 0 || charH <= 0)
            {
                throw new InvalidOperationException($"终端度量未就绪: charW={charW} charH={charH}");
            }
            AppLog.Info($"[SelfTest] r54: ① charW={charW:0.###} charH={charH:0.###} gutter={view.GutterWidth:0.###} grid={terminal.Terminal.Cols}x{terminal.Terminal.Rows}");

            var win = TopLevel.GetTopLevel(view)!;
            Point WinPoint(double col, double rowFrac)
            {
                var local = new Point(view.GutterWidth + charW * (col + 0.5), charH * rowFrac);
                return view.TranslatePoint(local, win) ?? local;
            }

            // 双击落在 "18"（版本段中、两侧各有 . 和 -），修复前只会选到 "6.18.20"
            var col = probePath.IndexOf("18", StringComparison.Ordinal);
            var p0 = WinPoint(col, 0.5);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var pressProps = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            view.RaiseEvent(new PointerPressedEventArgs(view, pointer, win, p0, 0, pressProps, KeyModifiers.None, 1)
            {
                RoutedEvent = InputElement.PointerPressedEvent,
            });
            await Task.Delay(100);
            view.RaiseEvent(new PointerPressedEventArgs(view, pointer, win, p0, 0, pressProps, KeyModifiers.None, 2)
            {
                RoutedEvent = InputElement.PointerPressedEvent,
            });
            await Task.Delay(300);

            var selected = terminal.Terminal.Selection.GetSelectionText();
            pathOk = string.Equals(selected, probePath, StringComparison.Ordinal);
            AppLog.Info($"[SelfTest] r54: ① 双击路径选区='{selected ?? "(空)"}' => {(pathOk ? "OK" : "FAILED")}（期望整条路径）");

            // ── ② SSH 连接成功后自动打开 SFTP 浏览器 ──
            var firstKey = _config?.SshSessions.Sessions.FirstOrDefault()?.Key;
            if (string.IsNullOrEmpty(firstKey))
            {
                throw new InvalidOperationException("config 中没有 SSH 会话，无法跑 r54 ② 自检");
            }
            AppLog.Info($"[SelfTest] r54: ② 打开 SSH 会话 {firstKey}，验证 SFTP 浏览器自动打开");
            var openTask = OpenSshSessionAsync(firstKey!);
            TerminalTab? sshTab = null;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!(openTask.IsCompleted && sshTab is not null && sshTab.IsAlive))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("SSH 会话 90s 内未连接成功");
                }
                sshTab = viewModel.Tabs.FirstOrDefault(t => t.SessionId == firstKey);
                await Task.Delay(250);
            }
            await openTask;
            await Task.Delay(800);
            UpdateLayout();

            sshOk = viewModel.SidebarTab == SidebarTab.SshBrowser
                && viewModel.IsSidebarVisible
                && ReferenceEquals(viewModel.SelectedTab, sshTab)
                && sshTab.SftpPane is { IsExpanded: true };
            AppLog.Info($"[SelfTest] r54: ② status='{sshTab.StatusMessage}' SidebarTab={viewModel.SidebarTab} sidebarVisible={viewModel.IsSidebarVisible} paneExpanded={(sshTab.SftpPane?.IsExpanded ?? false)} => {(sshOk ? "OK" : "FAILED")}");

            // ── ③ 浅色主题 MobaXterm 1:1 取色（读计算后的刷值，不靠像素猜坐标）──
            var titleBar = this.FindControl<Border>("TitleBar");
            var statusGrid = this.FindControl<Grid>("StatusBar");
            var titleBg = BrushColor(titleBar?.Background);
            var statusBg = BrushColor(statusGrid?.Background);

            var tabBorders = this.GetVisualDescendants()
                .OfType<Border>()
                .Where(b => b.Classes.Contains("TabItemBorder"))
                .ToList();
            var selTab = tabBorders.FirstOrDefault(b => b.Classes.Contains("selected"));
            var unselTab = tabBorders.FirstOrDefault(b => !b.Classes.Contains("selected"));
            var grad = selTab?.Background as LinearGradientBrush;
            var gradTop = grad is { GradientStops.Count: >= 1 } ? grad.GradientStops[0].Color : Color.FromRgb(0, 0, 0);
            var gradBottom = grad is { GradientStops.Count: >= 2 } ? grad.GradientStops[grad.GradientStops.Count - 1].Color : Color.FromRgb(0, 0, 0);
            var unselBg = BrushColor(unselTab?.Background);
            var unselBorder = BrushColor(unselTab?.BorderBrush);
            var selTitle = selTab?.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Classes.Contains("TabTitle"));
            var selTitleFg = BrushColor(selTitle?.Foreground);

            var tc = terminal.Terminal.Colors;
            themeOk = titleBg == Color.Parse("#ffffff")
                && statusBg == Color.Parse("#f0f0f0")
                && gradTop == Color.Parse("#4d4d4d")
                && gradBottom == Color.Parse("#1f1f1f")
                && unselBg == Color.Parse("#ffffff")
                && unselBorder == Color.Parse("#b4b4b4")
                && selTitleFg == Colors.White
                && (tc.Background & 0xffffff) == 0x1c1c1c
                && (tc.Foreground & 0xffffff) == 0xececec
                && (tc.Cursor & 0xffffff) == 0xb4b4c0
                && (tc[4] & 0xffffff) == 0x6060ff;
            AppLog.Info($"[SelfTest] r54: ③ 标题栏=#{titleBg.R:X2}{titleBg.G:X2}{titleBg.B:X2} 状态栏=#{statusBg.R:X2}{statusBg.G:X2}{statusBg.B:X2} " +
                $"选中tab渐变=#{gradTop.R:X2}{gradTop.G:X2}{gradTop.B:X2}→#{gradBottom.R:X2}{gradBottom.G:X2}{gradBottom.B:X2} 选中tab标题=#{selTitleFg.R:X2}{selTitleFg.G:X2}{selTitleFg.B:X2} " +
                $"未选中tab底=#{unselBg.R:X2}{unselBg.G:X2}{unselBg.B:X2} 边=#{unselBorder.R:X2}{unselBorder.G:X2}{unselBorder.B:X2} " +
                $"终端底=#{tc.Background:X6} 前景=#{tc.Foreground:X6} 光标=#{tc.Cursor:X6} 蓝=#{tc[4]:X6} => {(themeOk ? "OK" : "FAILED")}");

            UpdateLayout();
            SaveVisualPng(Content as Visual, "r54-theme.png");

            var ok = pathOk && sshOk && themeOk;
            AppLog.Info($"[SelfTest] r54: 自检完成，连字符路径={(pathOk ? "OK" : "失败")}，SSH 自动开 SFTP={(sshOk ? "OK" : "失败")}，主题取色={(themeOk ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r54: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r55</c>：实机自检 r55 三项。
    /// ① 标题栏下沿 1px 分隔线（BorderThickness=0,0,0,1，线色=TitleBarBorderBrush 资源，
    ///    浅色 #b4b4b4，与参照图白底区 y=91 实测一致）；
    /// ② 标题栏会话按钮（SessionsButton）已按用户反馈移除，rail 会话图标存在；
    /// ③ rail 会话图标可折叠/展开侧栏（折叠列宽 40px，恢复至原宽度）。
    /// 落盘 r55-titlebar.png 供目检分层效果。
    /// </summary>
    private async Task RunR55SelfTestAsync()
    {
        var dividerOk = false;
        var buttonRemovedOk = false;
        var railToggleOk = false;
        try
        {
            AppLog.Info("[SelfTest] r55: 等主窗稳定 1.5s 后开始三项自检");
            await Task.Delay(1500);

            var titleBar = this.FindControl<Border>("TitleBar");
            var body = this.FindControl<Grid>("BodyGrid");
            var sidebar = this.FindControl<SessionSidebar>("Sidebar");
            if (titleBar is null || body is null || sidebar is null)
            {
                throw new InvalidOperationException("TitleBar/BodyGrid/Sidebar 未找到");
            }

            // ── ① 标题栏下沿 1px 分隔线 ──
            var dividerColor = BrushColor(titleBar.BorderBrush);
            var resourceColor = BrushColor(titleBar.TryFindResource("TitleBarBorderBrush", out var dividerResource)
                ? dividerResource as IBrush : null);
            dividerOk = titleBar.BorderThickness.Bottom == 1
                && titleBar.BorderThickness.Top == 0
                && titleBar.BorderThickness.Left == 0
                && titleBar.BorderThickness.Right == 0
                && dividerColor == resourceColor;
            AppLog.Info($"[SelfTest] r55: ① 标题栏下沿线 thickness={titleBar.BorderThickness.Bottom} " +
                $"线色=#{dividerColor.R:X2}{dividerColor.G:X2}{dividerColor.B:X2} " +
                $"资源=#{resourceColor.R:X2}{resourceColor.G:X2}{resourceColor.B:X2} => {(dividerOk ? "OK" : "FAILED")}");

            // ── ② 标题栏会话按钮已移除，rail 会话图标为替代入口 ──
            var sessionsBtn = this.FindControl<Button>("SessionsButton");
            var railBtn = sidebar.FindControl<Button>("RailSessionsButton");
            buttonRemovedOk = sessionsBtn is null && railBtn is not null;
            AppLog.Info($"[SelfTest] r55: ② SessionsButton={(sessionsBtn is null ? "已移除" : "仍存在")} " +
                $"RailSessionsButton={(railBtn is not null ? "存在" : "缺失")} => {(buttonRemovedOk ? "OK" : "FAILED")}");

            // ── ③ rail 会话图标折叠/展开侧栏 ──
            // 不假设初始方向：VM 配置可能恢复为展开（配置宽度）或折叠（40px），
            // 两次点击必须完成"状态翻转 + 原样恢复"，且两态宽度分别落在
            // rail 40px 与配置宽度（>40px）。
            if (railBtn is not null)
            {
                // 若当前不在 Sessions 页（如 SshBrowser），第一次点击会切页
                // （折叠态下还会展开），先补齐一次切页再继续。
                if (DataContext is MainViewModel vm && vm.SidebarTab != SidebarTab.Sessions)
                {
                    railBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, railBtn));
                    await Task.Delay(300);
                    UpdateLayout();
                }
                var initialWidth = body.ColumnDefinitions[0].ActualWidth;
                railBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, railBtn));
                await Task.Delay(300);
                UpdateLayout();
                var toggledWidth = body.ColumnDefinitions[0].ActualWidth;
                railBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, railBtn));
                await Task.Delay(300);
                UpdateLayout();
                var restoredWidth = body.ColumnDefinitions[0].ActualWidth;
                railToggleOk = Math.Abs(toggledWidth - initialWidth) > 5.0
                    && Math.Abs(restoredWidth - initialWidth) < 1.0
                    && Math.Abs(Math.Min(initialWidth, toggledWidth) - 40.0) < 1.0
                    && Math.Max(initialWidth, toggledWidth) > 41.0;
                AppLog.Info($"[SelfTest] r55: ③ rail 会话图标 初始宽={initialWidth:0.#} " +
                    $"一次点击后={toggledWidth:0.#} 二次点击后={restoredWidth:0.#} => {(railToggleOk ? "OK" : "FAILED")}");
            }

            UpdateLayout();
            SaveVisualPng(Content as Visual, "r55-titlebar.png");

            var ok = dividerOk && buttonRemovedOk && railToggleOk;
            AppLog.Info($"[SelfTest] r55: 自检完成，下沿分隔线={(dividerOk ? "OK" : "失败")}，" +
                $"按钮移除={(buttonRemovedOk ? "OK" : "失败")}，rail 折叠展开={(railToggleOk ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r55: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r56</c>：实机自检 r56 两项。
    /// ① SGR 粗体渲染对齐 MobaXterm（其字体无粗体面，SGR 1 视觉无效果）：
    ///    写 SGR 1 / SGR 1;35 探测行，走查真实 GlyphRun——不得出现粗体面，
    ///    粗体品红段按普通品红绘制（不提亮为 BrightMagenta）；
    /// ② EWS 方案 MAC 规则（复刻用户 MobaXterm 个人高亮）：启用
    ///    error_warning 方案后写 ifconfig ether 行，MAC 格子前景=调色板
    ///    索引 5（品红），验证后恢复原方案。落盘 r56-terminal.png 供目检。
    /// </summary>
    private async Task RunR56SelfTestAsync()
    {
        var boldOk = false;
        var macOk = false;
        try
        {
            AppLog.Info("[SelfTest] r56: 等主窗稳定 1.5s 后开始两项自检");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var terminal = CreateTerminal();
            viewModel.AddTab(new TerminalTab
            {
                Title = "r56-selftest",
                SessionId = "local:r56selftest",
                Kind = SessionKind.Local,
                Content = terminal,
                Terminal = terminal,
                IsAlive = true,
                CloseAction = () => { },
            });
            await Task.Delay(500);
            UpdateLayout();

            var view = terminal.GetVisualDescendants()
                .OfType<Iciclecreek.Terminal.TerminalView>().FirstOrDefault();
            if (view is null || terminal.Terminal is null)
            {
                throw new InvalidOperationException("TerminalView/XTerm 未就绪");
            }

            // 本终端不接 pty（不走 AttachTerminal 路径），需显式挂高亮着色器，
            // 否则 _highlighters 无此终端条目，下面 SetSyntaxHighlightScheme 不生效。
            AttachTerminalHighlighter(terminal);

            // ── ① SGR 粗体：无粗体面、不提亮 ──
            terminal.Terminal.Write("\x1b[1mBOLDVAL\x1b[0m \x1b[1;35mBMAG\x1b[0m\r\n");
            await Task.Delay(300);
            UpdateLayout();

            var runs = new List<(string Text, FontWeight Weight, Color Color)>();
            var group = new DrawingGroup();
            using (var ctx = group.Open())
            {
                view.Render(ctx);
            }
            WalkR56Runs(group, runs);
            var hasBoldFace = runs.Any(r => r.Weight.Equals(FontWeight.Bold)
                || r.Weight.Equals(FontWeight.ExtraBold) || r.Weight.Equals(FontWeight.Black));
            var magRun = runs.FirstOrDefault(r => r.Text.Contains("BMAG", StringComparison.Ordinal));
            var magHex = COMTool.Terminal.TerminalTheme.CreateMobaXtermTheme().Magenta;
            var magExpected = Color.Parse(magHex);
            boldOk = runs.Count > 0 && !hasBoldFace
                && magRun.Text.Length > 0
                && magRun.Color == magExpected;
            AppLog.Info($"[SelfTest] r56: ① SGR粗体 glyphrun={runs.Count} 粗体面={hasBoldFace} " +
                $"BMAG色=#{(magRun.Color.R << 16) | (magRun.Color.G << 8) | magRun.Color.B:X6}（期望 {magHex}）=> {(boldOk ? "OK" : "FAILED")}");

            // ── ② EWS 方案 MAC 地址→品红（验证后恢复原方案）──
            var prevScheme = _config?.Config.Terminal.SyntaxHighlight ?? "default";
            SetSyntaxHighlightScheme("error_warning");
            terminal.Terminal.Write("      ether 82:37:a3:fe:87:0b   txqueuelen 1000  (Ethernet)\r\n");
            await Task.Delay(300);
            UpdateLayout();

            const string mac = "82:37:a3:fe:87:0b";
            var lineNo = -1;
            for (var row = 0; row < terminal.Terminal.Rows && lineNo < 0; row++)
            {
                var text = terminal.Terminal.Buffer.GetLine(row)?.TranslateToString(true, 0);
                if (text is null || !text.Contains(mac, StringComparison.Ordinal))
                {
                    continue;
                }
                lineNo = row;
                var line = terminal.Terminal.Buffer.GetLine(row)!;
                var col = text.IndexOf(mac, StringComparison.Ordinal);
                macOk = true;
                for (var x = col; x < col + mac.Length && macOk; x++)
                {
                    macOk = line[x].Attributes.GetFgColorMode() == 0
                        && line[x].Attributes.GetFgColor() == 5; // 5=品红
                }
            }
            SetSyntaxHighlightScheme(prevScheme);
            AppLog.Info($"[SelfTest] r56: ② EWS MAC 行={lineNo} MAC 17 格全品红(index5)={(macOk ? "OK" : "FAILED")}（方案已恢复 {prevScheme}）");

            UpdateLayout();
            SaveVisualPng(Content as Visual, "r56-terminal.png");

            var ok = boldOk && macOk;
            AppLog.Info($"[SelfTest] r56: 自检完成，SGR粗体对齐={(boldOk ? "OK" : "失败")}，EWS MAC品红={(macOk ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r56: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r57</c>：实机自检 r57 语法高亮实时着色回归。
    /// 用户实机反馈“只生效一次，下次执行 ifconfig 就没效果了”：旧实现 LF 时把
    /// 水印推进到游标下一行，命令回显之后的输出行全部被跳过（只有方案切换
    /// ApplyToAll 能把历史一次性染色）。本自检全程不调用 SetSyntaxHighlightScheme，
    /// 先落配置再挂着色器，连续写两轮 ifconfig 形态输出，逐轮核对 MAC=品红、
    /// errors=红；落盘 r57-terminal.png 供目检。
    /// </summary>
    private async Task RunR57SelfTestAsync()
    {
        var ok = false;
        string? prevScheme = null;
        try
        {
            AppLog.Info("[SelfTest] r57: 等主窗稳定 1.5s 后开始");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            // 先落方案再建终端：不调用 SetSyntaxHighlightScheme（那会 ApplyToAll，
            // 掩盖 LineFed 实时着色缺陷）。
            prevScheme = _config?.Config.Terminal.SyntaxHighlight ?? "default";
            if (_config is not null)
            {
                _config.Config.Terminal.SyntaxHighlight = "error_warning";
            }

            var terminal = CreateTerminal();
            viewModel.AddTab(new TerminalTab
            {
                Title = "r57-selftest",
                SessionId = "local:r57selftest",
                Kind = SessionKind.Local,
                Content = terminal,
                Terminal = terminal,
                IsAlive = true,
                CloseAction = () => { },
            });
            await Task.Delay(500);
            UpdateLayout();
            AttachTerminalHighlighter(terminal);
            if (terminal.Terminal is null)
            {
                throw new InvalidOperationException("XTerm 未就绪");
            }

            const string mac = "82:37:a3:fe:87:0b";
            var rounds = new List<string>();
            var allOk = true;
            for (var round = 0; round < 2; round++)
            {
                terminal.Terminal.Write("$ ifconfig\r\n");
                terminal.Terminal.Write("      ether " + mac + "   txqueuelen 1000  (Ethernet)\r\n");
                terminal.Terminal.Write("        RX errors 0  dropped 0  overruns 0  frame 0\r\n");
                await Task.Delay(200);

                var baseRow = round * 3;
                var macRow = baseRow + 1;
                var errRow = baseRow + 2;
                var macLine = terminal.Terminal.Buffer.GetLine(macRow)!;
                var macText = macLine.TranslateToString(true, 0);
                var macCol = macText.IndexOf(mac, StringComparison.Ordinal);
                var macOk = macCol >= 0;
                for (var x = macCol; macOk && x < macCol + mac.Length; x++)
                {
                    macOk = macLine[x].Attributes.GetFgColorMode() == 0
                        && macLine[x].Attributes.GetFgColor() == 5; // 品红
                }

                var errLine = terminal.Terminal.Buffer.GetLine(errRow)!;
                var errText = errLine.TranslateToString(true, 0);
                var errCol = errText.IndexOf("errors", StringComparison.Ordinal);
                var errOk = errCol >= 0
                    && errLine[errCol].Attributes.GetFgColor() == 1; // 红

                allOk &= macOk && errOk;
                rounds.Add($"轮{round + 1} MAC行={macRow}品红={macOk} errors行={errRow}红={errOk}");
                AppLog.Info($"[SelfTest] r57: {rounds[^1]}");
            }

            UpdateLayout();
            SaveVisualPng(Content as Visual, "r57-terminal.png");
            ok = allOk;
            AppLog.Info($"[SelfTest] r57: 自检完成，实时着色两轮均{(ok ? "OK" : "失败")}（{string.Join("; ", rounds)}），退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r57: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
        finally
        {
            // 自检不落盘方案：恢复内存配置，避免污染用户 config.json。
            if (_config is not null && prevScheme is not null)
            {
                _config.Config.Terminal.SyntaxHighlight = prevScheme;
            }
        }
    }

    /// <summary>
    /// <c>--selftest-r58</c>：实机自检 r58 三项（用户同板 ifconfig 对照截图）。
    /// ① MobaXterm EWS 的 IPv4 规则首段仅 1..254 → netmask 255.255.255.0 保持
    ///    默认前景色，inet/broadcast 地址仍为品红；
    /// ② MAC 规则两侧边界字符类排除 '-' → unspec 的 00-00-…-00 长链保持默认色，
    ///    ether 的冒号 MAC 仍为品红；
    /// ③ 品红取色对齐用户 MobaXterm.ini [Colors] Magenta=255,77,255（#ff4dff），
    ///    用真实 GlyphRun 复核渲染色。
    /// 全程不调用 SetSyntaxHighlightScheme（先落配置再建终端），走实时着色路径；
    /// 落盘 r58-terminal.png 供目检。
    /// </summary>
    private async Task RunR58SelfTestAsync()
    {
        var ok = false;
        string? prevScheme = null;
        try
        {
            AppLog.Info("[SelfTest] r58: 等主窗稳定 1.5s 后开始");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            prevScheme = _config?.Config.Terminal.SyntaxHighlight ?? "default";
            if (_config is not null)
            {
                _config.Config.Terminal.SyntaxHighlight = "error_warning";
            }

            var terminal = CreateTerminal();
            viewModel.AddTab(new TerminalTab
            {
                Title = "r58-selftest",
                SessionId = "local:r58selftest",
                Kind = SessionKind.Local,
                Content = terminal,
                Terminal = terminal,
                IsAlive = true,
                CloseAction = () => { },
            });
            await Task.Delay(500);
            UpdateLayout();
            AttachTerminalHighlighter(terminal);
            if (terminal.Terminal is null)
            {
                throw new InvalidOperationException("XTerm 未就绪");
            }

            const string mac = "02:42:5b:d8:95:d3";
            const string unspec = "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00";
            terminal.Terminal.Write("$ ifconfig\r\n");
            terminal.Terminal.Write("inet 10.0.9.5 netmask 255.255.255.0 broadcast 10.0.9.255\r\n");
            terminal.Terminal.Write("unspec " + unspec + "\r\n");
            terminal.Terminal.Write("ether " + mac + "\r\n");
            await Task.Delay(300);
            UpdateLayout();

            static int Col(string text, string token)
            {
                var col = text.IndexOf(token, StringComparison.Ordinal);
                if (col < 0)
                {
                    throw new InvalidOperationException($"未找到 {token}：{text}");
                }
                return col;
            }

            var inetLine = terminal.Terminal.Buffer.GetLine(1)!;
            var inetText = inetLine.TranslateToString(true, 0);
            var inetOk = inetLine[Col(inetText, "10.0.9.5")].Attributes.GetFgColor() == 5;
            var netmaskOk = inetLine[Col(inetText, "255.255.255.0")].Attributes.GetFgColor() == 256;
            var broadcastOk = inetLine[Col(inetText, "10.0.9.255")].Attributes.GetFgColor() == 5;

            var unspecLine = terminal.Terminal.Buffer.GetLine(2)!;
            var unspecText = unspecLine.TranslateToString(true, 0);
            var unspecOk = unspecLine[Col(unspecText, unspec)].Attributes.GetFgColor() == 256;

            var etherLine = terminal.Terminal.Buffer.GetLine(3)!;
            var etherText = etherLine.TranslateToString(true, 0);
            var macOk = etherLine[Col(etherText, mac)].Attributes.GetFgColor() == 5;

            // ③ 渲染色复核：MAC 段 GlyphRun 的前景必须是 #ff4dff
            var runs = new List<(string Text, FontWeight Weight, Color Color)>();
            var view = terminal.GetVisualDescendants()
                .OfType<Iciclecreek.Terminal.TerminalView>().FirstOrDefault();
            if (view is not null)
            {
                var group = new DrawingGroup();
                using (var ctx = group.Open())
                {
                    view.Render(ctx);
                }
                WalkR56Runs(group, runs);
            }
            var magExpected = Color.Parse("#ff4dff");
            var macRun = runs.FirstOrDefault(r => r.Text.Contains(mac, StringComparison.Ordinal));
            var colorOk = macRun.Text.Length > 0 && macRun.Color == magExpected;

            AppLog.Info($"[SelfTest] r58: ① inet品红={inetOk} netmask默认={netmaskOk} broadcast品红={broadcastOk}");
            AppLog.Info($"[SelfTest] r58: ② unspec默认={unspecOk} etherMAC品红={macOk}");
            AppLog.Info($"[SelfTest] r58: ③ MAC渲染色=#{(macRun.Color.R << 16) | (macRun.Color.G << 8) | macRun.Color.B:X6}（期望 FF4DFF）=> {(colorOk ? "OK" : "FAILED")}");

            UpdateLayout();
            SaveVisualPng(Content as Visual, "r58-terminal.png");
            ok = inetOk && netmaskOk && broadcastOk && unspecOk && macOk && colorOk;
            AppLog.Info($"[SelfTest] r58: 自检完成，三项{(ok ? "全部 OK" : "存在失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r58: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
        finally
        {
            if (_config is not null && prevScheme is not null)
            {
                _config.Config.Terminal.SyntaxHighlight = prevScheme;
            }
        }
    }

    /// <summary>
    /// <c>--selftest-r59</c>：实机自检 r59「标准关键字」（MobaXterm SyntaxType=1）方案。
    /// ① 时间戳（`[ 67705.015]` 与 `2026-11-10 15:23:51`）染黄（调色板 3）；
    /// ② null/none 染品红（调色板 5）；
    /// ③ EWS 既有分组不回退：error 红（1）、IP 品红（5）；
    /// ④ 走真实 GlyphRun 复核渲染色：时间戳=Yellow、none=Magenta（用户 ini 值）。
    /// 全程不调用 SetSyntaxHighlightScheme（先落配置再建终端），走实时着色路径；
    /// 落盘 r59-terminal.png 供目检。
    /// </summary>
    private async Task RunR59SelfTestAsync()
    {
        var ok = false;
        string? prevScheme = null;
        try
        {
            AppLog.Info("[SelfTest] r59: 等主窗稳定 1.5s 后开始");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            prevScheme = _config?.Config.Terminal.SyntaxHighlight ?? "default";
            if (_config is not null)
            {
                _config.Config.Terminal.SyntaxHighlight = "standard";
            }

            var terminal = CreateTerminal();
            viewModel.AddTab(new TerminalTab
            {
                Title = "r59-selftest",
                SessionId = "local:r59selftest",
                Kind = SessionKind.Local,
                Content = terminal,
                Terminal = terminal,
                IsAlive = true,
                CloseAction = () => { },
            });
            await Task.Delay(500);
            UpdateLayout();
            AttachTerminalHighlighter(terminal);
            if (terminal.Terminal is null)
            {
                throw new InvalidOperationException("XTerm 未就绪");
            }

            terminal.Terminal.Write("$ dmesg | head -2\r\n");
            terminal.Terminal.Write("[ 67705.015] variant=\"none\" error on 10.0.9.5\r\n");
            terminal.Terminal.Write("2026-11-10 15:23:51 boot ok\r\n");
            await Task.Delay(300);
            UpdateLayout();

            static int Col(string text, string token)
            {
                var col = text.IndexOf(token, StringComparison.Ordinal);
                if (col < 0)
                {
                    throw new InvalidOperationException($"未找到 {token}：{text}");
                }
                return col;
            }

            var line1 = terminal.Terminal.Buffer.GetLine(1)!;
            var text1 = line1.TranslateToString(true, 0);
            var tsOk = line1[Col(text1, "[ 67705.015]")].Attributes.GetFgColor() == 3;
            var noneOk = line1[Col(text1, "\"none\"") + 1].Attributes.GetFgColor() == 5;
            var errorOk = line1[Col(text1, "error")].Attributes.GetFgColor() == 1;
            var ipOk = line1[Col(text1, "10.0.9.5")].Attributes.GetFgColor() == 5;

            var line2 = terminal.Terminal.Buffer.GetLine(2)!;
            var text2 = line2.TranslateToString(true, 0);
            var dateOk = line2[Col(text2, "2026-11-10 15:23:51")].Attributes.GetFgColor() == 3;

            // ④ 渲染色复核
            var runs = new List<(string Text, FontWeight Weight, Color Color)>();
            var view = terminal.GetVisualDescendants()
                .OfType<Iciclecreek.Terminal.TerminalView>().FirstOrDefault();
            if (view is not null)
            {
                var group = new DrawingGroup();
                using (var ctx = group.Open())
                {
                    view.Render(ctx);
                }
                WalkR56Runs(group, runs);
            }
            var yellow = Color.Parse(COMTool.Terminal.TerminalTheme.CreateMobaXtermTheme().Yellow);
            var magenta = Color.Parse(COMTool.Terminal.TerminalTheme.CreateMobaXtermTheme().Magenta);
            var tsRun = runs.FirstOrDefault(r => r.Text.Contains("67705", StringComparison.Ordinal));
            var noneRun = runs.FirstOrDefault(r => r.Text.Contains("none", StringComparison.Ordinal));
            var renderOk = tsRun.Text.Length > 0 && tsRun.Color == yellow
                && noneRun.Text.Length > 0 && noneRun.Color == magenta;

            AppLog.Info($"[SelfTest] r59: ① 时间戳黄={tsOk} none品红={noneOk} error红={errorOk} ip品红={ipOk}");
            AppLog.Info($"[SelfTest] r59: ② 日期时间黄={dateOk}");
            AppLog.Info($"[SelfTest] r59: ③ 渲染色 时间戳=#{(tsRun.Color.R << 16) | (tsRun.Color.G << 8) | tsRun.Color.B:X6}" +
                $"（期望 {COMTool.Terminal.TerminalTheme.CreateMobaXtermTheme().Yellow}）" +
                $" none=#{(noneRun.Color.R << 16) | (noneRun.Color.G << 8) | noneRun.Color.B:X6}" +
                $"（期望 {COMTool.Terminal.TerminalTheme.CreateMobaXtermTheme().Magenta}）=> {(renderOk ? "OK" : "FAILED")}");

            UpdateLayout();
            SaveVisualPng(Content as Visual, "r59-terminal.png");
            ok = tsOk && noneOk && errorOk && ipOk && dateOk && renderOk;
            AppLog.Info($"[SelfTest] r59: 自检完成，{(ok ? "全部 OK" : "存在失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r59: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
        finally
        {
            if (_config is not null && prevScheme is not null)
            {
                _config.Config.Terminal.SyntaxHighlight = prevScheme;
            }
        }
    }

    /// <summary>
    /// <c>--selftest-r60</c>：实机自检 r60 SFTP 浏览器右键菜单与打开行为。
    /// 连上配置里第一个 SSH 会话后：① 右键菜单 11 项按 MobaXterm 顺序齐全
    /// （含 3 条分隔线）；② 双击文本文件打开内建文本查看器（截图
    /// r60-viewer.png）；③ 把远程路径送入终端（中键粘贴语义，不带回车），
    /// 回读终端缓冲验证路径文本已出现。落盘 r60-browser.png。
    /// </summary>
    private async Task RunR60SelfTestAsync()
    {
        var menuOk = false;
        var viewerOk = false;
        var viewerSkipped = false;
        var termOk = false;
        try
        {
            AppLog.Info("[SelfTest] r60: 等主窗稳定 1.5s 后开始");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var firstKey = _config?.SshSessions.Sessions.FirstOrDefault()?.Key;
            if (string.IsNullOrEmpty(firstKey))
            {
                throw new InvalidOperationException("config 中没有 SSH 会话，无法跑 r60 自检");
            }

            AppLog.Info($"[SelfTest] r60: 打开 SSH 会话 {firstKey}");
            var openTask = OpenSshSessionAsync(firstKey!);
            TerminalTab? sshTab = null;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!(openTask.IsCompleted && sshTab is not null && sshTab.IsAlive))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("SSH 会话 90s 内未连接成功");
                }

                sshTab = viewModel.Tabs.FirstOrDefault(t => t.SessionId == firstKey);
                await Task.Delay(250);
            }

            await openTask;
            await Task.Delay(800);
            UpdateLayout();

            var paneVm = sshTab.SftpPane;
            if (paneVm is null)
            {
                throw new InvalidOperationException("SFTP 面板未创建");
            }

            var dl = DateTime.UtcNow.AddSeconds(30);
            while (paneVm.RemoteEntries.Count == 0 && DateTime.UtcNow < dl)
            {
                await Task.Delay(200);
                UpdateLayout();
            }
            AppLog.Info($"[SelfTest] r60: SFTP 列表 {paneVm.RemoteEntries.Count} 项 @ {paneVm.RemotePath}");
            if (paneVm.RemoteEntries.Count == 0)
            {
                throw new TimeoutException("SFTP 列表 30s 内未加载");
            }

            var sftpPane = this.FindControl<SessionSidebar>("Sidebar")
                ?.FindControl<SshBrowserPanel>("SshBrowserPanel")?.SftpPaneControl;
            UpdateLayout();

            // ① 右键菜单：16 项按 MobaXterm 顺序（5 空白区 + 11 条目）+ 3 条分隔线
            var menu = sftpPane?.RemoteContextMenuControl;
            var headers = menu is null
                ? new List<string>()
                : menu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? "").ToList();
            var separators = menu?.Items.OfType<Separator>().Count() ?? 0;
            var expected = new[]
            {
                "上一级目录", "新建文件夹", "新建文件", "刷新当前文件夹列表", "上传到当前文件夹",
                "打开", "使用默认文本文编辑器打开", "打开方式...", "使用默认程序打开...",
                "下载", "删除", "重命名", "复制文件路径",
                "将文件路径复制到终端（鼠标中键单击）", "属性", "权限",
            };
            menuOk = menu is not null && separators == 3 && headers.SequenceEqual(expected);
            AppLog.Info($"[SelfTest] r60: ① 右键菜单 {headers.Count}/16 项、{separators}/3 分隔线 => {(menuOk ? "OK" : "FAILED")}");

            SaveVisualPng(Content as Visual, "r60-browser.png");

            // ② 双击文本文件 → 内建文本查看器
            var textEntry = paneVm.RemoteEntries
                .FirstOrDefault(e => !e.IsDirectory && !e.IsSymbolicLink
                    && RemoteFileClassifier.Classify(e.Name) == RemoteFileKind.Text);
            SftpTextViewerWindow? textWin = null;
            if (textEntry is null)
            {
                viewerSkipped = true;
                AppLog.Info("[SelfTest] r60: ② SKIP 当前目录没有文本文件");
            }
            else
            {
                paneVm.SelectedRemoteEntry = textEntry;
                if (sftpPane is not null)
                {
                    await sftpPane.OpenEntryAsync(textEntry, SftpOpenMode.Auto);
                }

                var dl2 = DateTime.UtcNow.AddSeconds(30);
                var lifetime = Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                while (textWin is null && DateTime.UtcNow < dl2)
                {
                    textWin = lifetime?.Windows.OfType<SftpTextViewerWindow>().FirstOrDefault();
                    await Task.Delay(100);
                    UpdateLayout();
                }

                if (textWin is not null)
                {
                    viewerOk = textWin.TextHostControl.Text.Length > 0;
                    AppLog.Info($"[SelfTest] r60: ② 双击 {textEntry.Name} 打开内建文本查看器 => {(viewerOk ? "OK" : "FAILED")}（{textWin.TextHostControl.Text.Length} 字符）");
                    UpdateLayout();
                    SaveVisualPng(textWin, "r60-viewer.png");
                }
                else
                {
                    AppLog.Info($"[SelfTest] r60: ② FAILED 未出现文本查看器窗口（{textEntry.Name}）");
                }
            }

            // ③ 路径送入终端（中键/菜单同一路径），回读终端缓冲验证
            var fileEntry = textEntry ?? paneVm.RemoteEntries.FirstOrDefault(e => !e.IsDirectory && !e.IsSymbolicLink);
            if (fileEntry is not null && sshTab.Terminal?.Terminal is not null)
            {
                paneVm.SelectedRemoteEntry = fileEntry;
                paneVm.SendSelectedPathToTerminal();
                await Task.Delay(2000);

                var term = sshTab.Terminal.Terminal;
                var sb = new System.Text.StringBuilder();
                for (var row = 0; row < term.Buffer.Length; row++)
                {
                    var line = term.Buffer.GetLine(row);
                    if (line is not null)
                    {
                        sb.Append(line.TranslateToString(true, 0));
                    }
                }

                var flat = sb.ToString().Replace("\r", "").Replace("\n", "");
                termOk = flat.Contains(fileEntry.Path, StringComparison.Ordinal);
                AppLog.Info($"[SelfTest] r60: ③ 路径送入终端 {fileEntry.Path} => {(termOk ? "OK" : "FAILED")}");
            }
            else
            {
                termOk = true;
                AppLog.Info("[SelfTest] r60: ③ SKIP 无可发送的文件条目或终端未就绪");
            }

            textWin?.Close();
            var ok = menuOk && (viewerOk || viewerSkipped) && termOk;
            AppLog.Info($"[SelfTest] r60: 自检完成，菜单={(menuOk ? "OK" : "失败")} 查看器={(viewerOk ? "OK" : viewerSkipped ? "SKIP" : "失败")} 送终端={(termOk ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r60: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r61</c>：实机自检 r61 SFTP 浏览器列表修复。
    /// 连上配置里第一个 SSH 会话后：① 隐藏文件默认关闭，列表无点开头条目；
    /// ② 打开「显示隐藏文件」后点开头条目追加出现，再关闭恢复；
    /// ③ 符号链接（如 tftp_dir）排在目录组而非文件区末尾。
    /// 落盘 r61-browser.png。
    /// </summary>
    private async Task RunR61SelfTestAsync()
    {
        var hiddenDefaultOk = false;
        var toggleOk = false;
        var toggleSkipped = false;
        var linkOk = false;
        var linkSkipped = false;
        try
        {
            AppLog.Info("[SelfTest] r61: 等主窗稳定 1.5s 后开始");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var firstKey = _config?.SshSessions.Sessions.FirstOrDefault()?.Key;
            if (string.IsNullOrEmpty(firstKey))
            {
                throw new InvalidOperationException("config 中没有 SSH 会话，无法跑 r61 自检");
            }

            AppLog.Info($"[SelfTest] r61: 打开 SSH 会话 {firstKey}");
            var openTask = OpenSshSessionAsync(firstKey!);
            TerminalTab? sshTab = null;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!(openTask.IsCompleted && sshTab is not null && sshTab.IsAlive))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("SSH 会话 90s 内未连接成功");
                }

                sshTab = viewModel.Tabs.FirstOrDefault(t => t.SessionId == firstKey);
                await Task.Delay(250);
            }

            await openTask;
            await Task.Delay(800);
            UpdateLayout();

            var paneVm = sshTab.SftpPane;
            if (paneVm is null)
            {
                throw new InvalidOperationException("SFTP 面板未创建");
            }

            var dl = DateTime.UtcNow.AddSeconds(30);
            while (paneVm.RemoteEntries.Count == 0 && DateTime.UtcNow < dl)
            {
                await Task.Delay(200);
                UpdateLayout();
            }
            AppLog.Info($"[SelfTest] r61: SFTP 列表 {paneVm.RemoteEntries.Count} 项 @ {paneVm.RemotePath}");
            if (paneVm.RemoteEntries.Count == 0)
            {
                throw new TimeoutException("SFTP 列表 30s 内未加载");
            }

            var sftpPane = this.FindControl<SessionSidebar>("Sidebar")
                ?.FindControl<SshBrowserPanel>("SshBrowserPanel")?.SftpPaneControl;
            UpdateLayout();

            // ① 默认不显示隐藏文件（MobaXterm 对齐）
            var dotCount = paneVm.RemoteEntries.Count(e => e.Name.StartsWith("."));
            hiddenDefaultOk = !paneVm.ShowHidden && dotCount == 0;
            AppLog.Info($"[SelfTest] r61: ① ShowHidden={paneVm.ShowHidden} 点开头条目={dotCount} => {(hiddenDefaultOk ? "OK" : "FAILED")}");

            // ② 打开开关后追加显示隐藏文件，再关闭恢复
            paneVm.ShowHidden = true;
            UpdateLayout();
            var shownDots = paneVm.RemoteEntries.Count(e => e.Name.StartsWith("."));
            paneVm.ShowHidden = false;
            UpdateLayout();
            if (shownDots == 0)
            {
                toggleSkipped = true;
                toggleOk = true;
                AppLog.Info("[SelfTest] r61: ② SKIP 当前目录没有隐藏文件");
            }
            else
            {
                toggleOk = shownDots > 0
                    && paneVm.RemoteEntries.All(e => !e.Name.StartsWith("."));
                AppLog.Info($"[SelfTest] r61: ② 开=显示 {shownDots} 个隐藏条目、关=恢复 => {(toggleOk ? "OK" : "FAILED")}");
            }

            // ③ 符号链接排在目录组（首个普通文件之前）
            var entriesList = paneVm.RemoteEntries.ToList();
            var linkEntry = entriesList.FirstOrDefault(e => e.IsSymbolicLink);
            var firstFileIndex = entriesList.FindIndex(e => !e.IsDirectory && !e.IsSymbolicLink);
            if (linkEntry is null)
            {
                linkSkipped = true;
                linkOk = true;
                AppLog.Info("[SelfTest] r61: ③ SKIP 当前目录没有符号链接");
            }
            else
            {
                var linkIndex = entriesList.IndexOf(linkEntry);
                linkOk = firstFileIndex < 0 || linkIndex < firstFileIndex;
                AppLog.Info($"[SelfTest] r61: ③ 符号链接 {linkEntry.Name} @ 行{linkIndex + 1}/共{paneVm.RemoteEntries.Count}，首个文件 @ 行{firstFileIndex + 1} => {(linkOk ? "OK" : "FAILED")}");
            }

            SaveVisualPng(Content as Visual, "r61-browser.png");

            var ok = hiddenDefaultOk && toggleOk && linkOk;
            AppLog.Info($"[SelfTest] r61: 自检完成，默认隐藏={(hiddenDefaultOk ? "OK" : "失败")} 开关={(toggleSkipped ? "SKIP" : toggleOk ? "OK" : "失败")} 链接排序={(linkSkipped ? "SKIP" : linkOk ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r61: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r62</c>：实机自检 r62 SFTP 右键菜单与权限窗（对齐 MobaXterm 截图）。
    /// 连上配置里第一个 SSH 会话后：① 菜单 16 项（5 空白区 + 11 条目）+ 3 分隔线；
    /// ② 菜单变体（无选中=空白区菜单 / 选中文件=条目菜单）；③ 新建文件（创建→
    /// 校验 0 字节→删除清理）；④ 权限窗标题含文件名且 rwx 串与八进制同步
    /// （只读检查，不写回远程）；⑤ 符号链接目录组排序（r61 回归）。落盘 r62-browser.png。
    /// </summary>
    private async Task RunR62SelfTestAsync()
    {
        var structureOk = false;
        var variantOk = false;
        var newFileOk = false;
        var permOk = false;
        var linkOk = false;
        var linkSkipped = false;
        try
        {
            AppLog.Info("[SelfTest] r62: 等主窗稳定 1.5s 后开始");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var firstKey = _config?.SshSessions.Sessions.FirstOrDefault()?.Key;
            if (string.IsNullOrEmpty(firstKey))
            {
                throw new InvalidOperationException("config 中没有 SSH 会话，无法跑 r62 自检");
            }

            AppLog.Info($"[SelfTest] r62: 打开 SSH 会话 {firstKey}");
            var openTask = OpenSshSessionAsync(firstKey!);
            TerminalTab? sshTab = null;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!(openTask.IsCompleted && sshTab is not null && sshTab.IsAlive))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("SSH 会话 90s 内未连接成功");
                }

                sshTab = viewModel.Tabs.FirstOrDefault(t => t.SessionId == firstKey);
                await Task.Delay(250);
            }

            await openTask;
            await Task.Delay(800);
            UpdateLayout();

            var paneVm = sshTab.SftpPane;
            if (paneVm is null)
            {
                throw new InvalidOperationException("SFTP 面板未创建");
            }

            var dl = DateTime.UtcNow.AddSeconds(30);
            while (paneVm.RemoteEntries.Count == 0 && DateTime.UtcNow < dl)
            {
                await Task.Delay(200);
                UpdateLayout();
            }
            AppLog.Info($"[SelfTest] r62: SFTP 列表 {paneVm.RemoteEntries.Count} 项 @ {paneVm.RemotePath}");
            if (paneVm.RemoteEntries.Count == 0)
            {
                throw new TimeoutException("SFTP 列表 30s 内未加载");
            }

            var sftpPane = this.FindControl<SessionSidebar>("Sidebar")
                ?.FindControl<SshBrowserPanel>("SshBrowserPanel")?.SftpPaneControl;
            UpdateLayout();

            // ① 菜单结构：16 项（5 空白区 + 11 条目）+ 3 条分隔线
            var menu = sftpPane?.RemoteContextMenuControl;
            var itemLabels = menu is null
                ? Array.Empty<string>()
                : menu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? "").ToArray();
            var separatorCount = menu?.Items.OfType<Separator>().Count() ?? 0;
            var expected = new[]
            {
                "上一级目录", "新建文件夹", "新建文件", "刷新当前文件夹列表", "上传到当前文件夹",
                "打开", "使用默认文本文编辑器打开", "打开方式...", "使用默认程序打开...",
                "下载", "删除", "重命名", "复制文件路径",
                "将文件路径复制到终端（鼠标中键单击）", "属性", "权限",
            };
            structureOk = menu is not null && separatorCount == 3 && itemLabels.SequenceEqual(expected);
            AppLog.Info($"[SelfTest] r62: ① 右键菜单 {itemLabels.Length}/16 项、{separatorCount}/3 分隔线 => {(structureOk ? "OK" : "FAILED")}");

            // ② 菜单变体：无选中=空白区菜单；选中文件=条目菜单
            var variantDetail = "";
            if (sftpPane is not null && menu is not null)
            {
                paneVm.SelectedRemoteEntry = null;
                UpdateLayout();
                var blankVisible = sftpPane.MenuBlankUpControl.IsVisible
                    && sftpPane.MenuBlankNewFolderControl.IsVisible
                    && sftpPane.MenuBlankNewFileControl.IsVisible
                    && sftpPane.MenuBlankRefreshControl.IsVisible
                    && sftpPane.MenuBlankUploadControl.IsVisible;
                var entryHidden = !menu.Items.OfType<MenuItem>().Skip(5).Any(m => m.IsVisible);

                var fileEntry = paneVm.RemoteEntries.FirstOrDefault(e => !e.IsDirectory && !e.IsSymbolicLink);
                if (fileEntry is not null)
                {
                    paneVm.SelectedRemoteEntry = fileEntry;
                    UpdateLayout();
                }

                var entryVisible = fileEntry is not null
                    && sftpPane.MenuOpenControl.IsVisible
                    && sftpPane.MenuDownloadControl.IsVisible
                    && sftpPane.MenuDeleteControl.IsVisible
                    && sftpPane.MenuCopyPathControl.IsVisible
                    && sftpPane.MenuPropertiesControl.IsVisible
                    && sftpPane.MenuPermissionsControl.IsVisible;
                variantOk = blankVisible && entryHidden && entryVisible;
                variantDetail = $"blankVisible={blankVisible} entryHidden={entryHidden} entryVisible={entryVisible}";
            }
            else
            {
                variantOk = false;
                variantDetail = "未取到面板/菜单";
            }
            AppLog.Info($"[SelfTest] r62: ② 菜单变体切换 => {(variantOk ? "OK" : "FAILED")}（{variantDetail}）");

            // ③ 新建文件：创建 → 校验 0 字节 → 删除清理
            const string probeName = "comtool_r62_probe.txt";
            var probeCreated = false;
            var probeDeleted = false;
            try
            {
                probeCreated = await paneVm.NewRemoteFileAsync(probeName);
                if (probeCreated)
                {
                    var probe = paneVm.RemoteEntries.FirstOrDefault(e => e.Name == probeName);
                    if (probe is not null && probe.Size == 0)
                    {
                        paneVm.SelectedRemoteEntry = probe;
                        await paneVm.DeleteRemoteAsync();
                        await paneVm.RefreshRemoteAsync();
                        probeDeleted = !paneVm.RemoteEntries.Any(e => e.Name == probeName);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("[SelfTest] r62: ③ 新建文件探测异常", ex);
            }
            newFileOk = probeCreated && probeDeleted;
            AppLog.Info($"[SelfTest] r62: ③ 新建文件 创建/清理 {probeName} => {(newFileOk ? "OK" : "FAILED")}（created={probeCreated} deleted={probeDeleted}）");

            // ④ 权限窗：标题含文件名 + rwx 串与八进制同步（不写回远程）
            SftpPermissionsWindow? permWin = null;
            try
            {
                var permEntry = paneVm.RemoteEntries.First(e => !e.IsDirectory && !e.IsSymbolicLink);
                permWin = new SftpPermissionsWindow
                {
                    Permission = permEntry.Permission,
                    TargetName = permEntry.Name,
                };
                permWin.Show();
                UpdateLayout();
                var titleOk = permWin.Title == $"Permissions for \"{permEntry.Name}\"";
                var headingOk = permWin.HeadingText == $"Permissions for \"{permEntry.Name}\"";
                permWin.OctalText = "755";
                UpdateLayout();
                var syncOk = permWin.RwxText == "rwxr-xr-x"
                    && permWin.OwnerExecuteBoxControl.IsChecked == true;
                permOk = titleOk && headingOk && syncOk;
                AppLog.Info($"[SelfTest] r62: ④ 权限窗 标题/抬头/rwx 同步 => {(permOk ? "OK" : "FAILED")}（titleOk={titleOk} headingOk={headingOk} syncOk={syncOk}）");
            }
            finally
            {
                permWin?.Close();
            }

            // ⑤ 符号链接位于目录组（r61 回归检查）
            var linkEntry = paneVm.RemoteEntries.FirstOrDefault(e => e.IsSymbolicLink);
            if (linkEntry is null)
            {
                linkSkipped = true;
                linkOk = true;
                AppLog.Info("[SelfTest] r62: ⑤ SKIP 当前目录没有符号链接");
            }
            else
            {
                var entriesList = paneVm.RemoteEntries.ToList();
                var linkIndex = entriesList.IndexOf(linkEntry);
                var firstFileIndex = entriesList.FindIndex(e => !e.IsDirectory && !e.IsSymbolicLink);
                linkOk = firstFileIndex < 0 || linkIndex < firstFileIndex;
                AppLog.Info($"[SelfTest] r62: ⑤ 符号链接 {linkEntry.Name} @ 行{linkIndex + 1}/共{entriesList.Count}，首个普通文件 @ 行{firstFileIndex + 1} => {(linkOk ? "OK" : "FAILED")}");
            }

            SaveVisualPng(Content as Visual, "r62-browser.png");

            var ok = structureOk && variantOk && newFileOk && permOk && linkOk;
            AppLog.Info($"[SelfTest] r62: 自检完成，菜单={(structureOk ? "OK" : "失败")} 变体={(variantOk ? "OK" : "失败")} 新建文件={(newFileOk ? "OK" : "失败")} 权限窗={(permOk ? "OK" : "失败")} 链接={(linkSkipped ? "SKIP" : linkOk ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r62: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r63</c>：实机自检 r63 权限窗版式（对齐 MobaXterm 图2）。
    /// 连上配置里第一个 SSH 会话后：① 居中粗体抬头 Permissions for "name"；
    /// ② 标签/版式（权限:/用户:/群组:/其他:/八进制模式:，用户群组其他粗体，
    /// 按钮行居中，窗口宽 ≥400）；③ 应用→标题栏瞬态「正在修改权限...」→SFTP
    /// 写回（同值回写，无害）→自动关窗；④ 状态栏 "Permissions set to" 且权限值不变。
    /// 落盘 r63-perm.png（权限窗本体）/ r63-browser.png。
    /// </summary>
    private async Task RunR63SelfTestAsync()
    {
        var headingOk = false;
        var layoutOk = false;
        var applyOk = false;
        var statusOk = false;
        try
        {
            AppLog.Info("[SelfTest] r63: 等主窗稳定 1.5s 后开始");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var firstKey = _config?.SshSessions.Sessions.FirstOrDefault()?.Key;
            if (string.IsNullOrEmpty(firstKey))
            {
                throw new InvalidOperationException("config 中没有 SSH 会话，无法跑 r63 自检");
            }

            AppLog.Info($"[SelfTest] r63: 打开 SSH 会话 {firstKey}");
            var openTask = OpenSshSessionAsync(firstKey!);
            TerminalTab? sshTab = null;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!(openTask.IsCompleted && sshTab is not null && sshTab.IsAlive))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("SSH 会话 90s 内未连接成功");
                }

                sshTab = viewModel.Tabs.FirstOrDefault(t => t.SessionId == firstKey);
                await Task.Delay(250);
            }

            await openTask;
            await Task.Delay(800);
            UpdateLayout();

            var paneVm = sshTab.SftpPane;
            if (paneVm is null)
            {
                throw new InvalidOperationException("SFTP 面板未创建");
            }

            var dl = DateTime.UtcNow.AddSeconds(30);
            while (paneVm.RemoteEntries.Count == 0 && DateTime.UtcNow < dl)
            {
                await Task.Delay(200);
                UpdateLayout();
            }
            AppLog.Info($"[SelfTest] r63: SFTP 列表 {paneVm.RemoteEntries.Count} 项 @ {paneVm.RemotePath}");
            if (paneVm.RemoteEntries.Count == 0)
            {
                throw new TimeoutException("SFTP 列表 30s 内未加载");
            }

            var fileEntry = paneVm.RemoteEntries.FirstOrDefault(e => !e.IsDirectory && !e.IsSymbolicLink);
            if (fileEntry is null)
            {
                throw new InvalidOperationException("当前目录没有普通文件，无法跑 r63 自检");
            }

            // SetRemotePermissionsAsync 以 SelectedRemoteEntry 为操作对象，先选中目标文件
            paneVm.SelectedRemoteEntry = fileEntry;

            var permWin = new SftpPermissionsWindow
            {
                Permission = fileEntry.Permission,
                TargetName = fileEntry.Name,
            };
            permWin.Show();
            UpdateLayout();

            // ① 抬头
            var headingText = permWin.HeadingText;
            headingOk = headingText == $"Permissions for \"{fileEntry.Name}\"";
            AppLog.Info($"[SelfTest] r63: ① 抬头 \"{headingText}\" => {(headingOk ? "OK" : "FAILED")}");

            // ② 标签/版式
            layoutOk = permWin.RwxLabelControl.Text == "权限:"
                && permWin.OwnerLabelControl.Text == "用户:"
                && permWin.GroupLabelControl.Text == "群组:"
                && permWin.OthersLabelControl.Text == "其他:"
                && permWin.OctalLabelControl.Text == "八进制模式:"
                && permWin.HeadingControl.FontWeight == FontWeight.Bold
                && permWin.OwnerLabelControl.FontWeight == FontWeight.Bold
                && permWin.GroupLabelControl.FontWeight == FontWeight.Bold
                && permWin.OthersLabelControl.FontWeight == FontWeight.Bold
                && permWin.HeadingControl.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Center
                && permWin.ButtonRowControl.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Center
                && permWin.Width >= 400;
            AppLog.Info($"[SelfTest] r63: ② 标签/版式（粗体抬头/冒号标签/按钮居中/窗宽 {permWin.Width}）=> {(layoutOk ? "OK" : "FAILED")}");

            SaveVisualPng(permWin, "r63-perm.png");

            // ③ 应用：标题栏瞬态 → SFTP 写回（同值回写）→ 自动关窗
            var origOctal = fileEntry.Permission.ToOctalText();
            var closed = false;
            permWin.Closed += (_, _) => closed = true;
            permWin.PermissionApplied = permission =>
                paneVm.SetRemotePermissionsAsync(permission.ToOctal());
            permWin.ApplyButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout();
            var transientTitle = permWin.Title;
            var transientOk = transientTitle == "正在修改权限...";

            var closeDeadline = DateTime.UtcNow.AddSeconds(15);
            while (!closed && DateTime.UtcNow < closeDeadline)
            {
                await Task.Delay(50);
                UpdateLayout();
            }
            applyOk = transientOk && closed;
            AppLog.Info($"[SelfTest] r63: ③ 应用瞬态标题 \"{transientTitle}\" + 写回后自动关窗 => {(applyOk ? "OK" : "FAILED")}（transientOk={transientOk} closed={closed}）");

            // ④ 写回结果：轮询等状态栏（chmod+刷新列表有网络往返，固定延时易竞态）
            var pollDeadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < pollDeadline && !paneVm.StatusMessage.Contains("Permissions set to"))
            {
                await Task.Delay(100);
            }
            var refreshed = paneVm.RemoteEntries.FirstOrDefault(e => e.Name == fileEntry.Name);
            statusOk = paneVm.StatusMessage.Contains("Permissions set to")
                && refreshed is not null
                && refreshed.Permission.ToOctalText() == origOctal;
            AppLog.Info($"[SelfTest] r63: ④ 状态栏 \"{paneVm.StatusMessage}\" + 权限仍为 {origOctal} => {(statusOk ? "OK" : "FAILED")}");

            SaveVisualPng(Content as Visual, "r63-browser.png");

            var ok = headingOk && layoutOk && applyOk && statusOk;
            AppLog.Info($"[SelfTest] r63: 自检完成，抬头={(headingOk ? "OK" : "失败")} 版式={(layoutOk ? "OK" : "失败")} 应用/关窗={(applyOk ? "OK" : "失败")} 写回={(statusOk ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r63: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r64</c>：实机自检 r64 SFTP 列表空白区左键清除选中（MobaXterm 行为）。
    /// 连上配置里第一个 SSH 会话后：① 选中一个文件→模拟空白区左键单击（向列表
    /// 直接发 PointerPressed，事件源非行容器）→选中清除（SelectedIndex==-1、VM 选中
    /// 为空）；② 行上左键按下不清除已有选中（回归守卫，与单测同一合成事件方式，
    /// App 工程不引用 Avalonia.Headless 故不用 MouseDown）。落盘 r64-browser.png。
    /// </summary>
    private async Task RunR64SelfTestAsync()
    {
        var clearOk = false;
        var rowOk = false;
        try
        {
            AppLog.Info("[SelfTest] r64: 等主窗稳定 1.5s 后开始");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var firstKey = _config?.SshSessions.Sessions.FirstOrDefault()?.Key;
            if (string.IsNullOrEmpty(firstKey))
            {
                throw new InvalidOperationException("config 中没有 SSH 会话，无法跑 r64 自检");
            }

            AppLog.Info($"[SelfTest] r64: 打开 SSH 会话 {firstKey}");
            var openTask = OpenSshSessionAsync(firstKey!);
            TerminalTab? sshTab = null;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!(openTask.IsCompleted && sshTab is not null && sshTab.IsAlive))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("SSH 会话 90s 内未连接成功");
                }

                sshTab = viewModel.Tabs.FirstOrDefault(t => t.SessionId == firstKey);
                await Task.Delay(250);
            }

            await openTask;
            await Task.Delay(800);
            UpdateLayout();

            var paneVm = sshTab.SftpPane;
            if (paneVm is null)
            {
                throw new InvalidOperationException("SFTP 面板未创建");
            }

            var dl = DateTime.UtcNow.AddSeconds(30);
            while (paneVm.RemoteEntries.Count == 0 && DateTime.UtcNow < dl)
            {
                await Task.Delay(200);
                UpdateLayout();
            }
            AppLog.Info($"[SelfTest] r64: SFTP 列表 {paneVm.RemoteEntries.Count} 项 @ {paneVm.RemotePath}");
            if (paneVm.RemoteEntries.Count == 0)
            {
                throw new TimeoutException("SFTP 列表 30s 内未加载");
            }

            var sftpPane = this.FindControl<SessionSidebar>("Sidebar")
                ?.FindControl<SshBrowserPanel>("SshBrowserPanel")?.SftpPaneControl;
            var list = sftpPane?.RemoteListControl;
            if (list is null)
            {
                throw new InvalidOperationException("未找到 SFTP 列表控件");
            }

            UpdateLayout();
            var fileEntry = paneVm.RemoteEntries.FirstOrDefault(e => !e.IsDirectory && !e.IsSymbolicLink);
            if (fileEntry is null)
            {
                throw new InvalidOperationException("当前目录没有普通文件，无法跑 r64 自检");
            }

            // ① 选中文件 → 模拟空白区左键单击 → 选中被清除
            paneVm.SelectedRemoteEntry = fileEntry;
            UpdateLayout();
            var selectedBefore = list.SelectedIndex;
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var windowPoint = list.TranslatePoint(
                new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), this) ?? new Point(10, 10);
            list.RaiseEvent(new PointerPressedEventArgs(
                list,
                pointer,
                this,
                windowPoint,
                0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
                KeyModifiers.None,
                clickCount: 1));
            UpdateLayout();
            clearOk = selectedBefore >= 0
                && list.SelectedIndex == -1
                && paneVm.SelectedRemoteEntry is null;
            var vmSel = paneVm.SelectedRemoteEntry is null ? "空" : "非空";
            AppLog.Info($"[SelfTest] r64: ① 空白区左键清除选中（{fileEntry.Name}：SelectedIndex {selectedBefore} → {list.SelectedIndex}，VM 选中={vmSel}）=> {(clearOk ? "OK" : "FAILED")}");

            // ② 行上的左键按下不清除已有选中（回归守卫：空白区新逻辑的 onItem
            // 分支不得误清行上选中）。与单测 SFTP14_RowClickDoesNotClearSelection
            // 同一合成事件方式（App 工程不引用 Avalonia.Headless，实机自检无法用
            // MouseDown 扩展方法；真实点击的选中链路由该单测在完整输入管线覆盖）。
            var lastIndex = list.Items.Count - 1;
            var lastEntry = paneVm.RemoteEntries[lastIndex];
            paneVm.SelectedRemoteEntry = lastEntry;
            UpdateLayout();
            selectedBefore = list.SelectedIndex;

            // 列表为虚拟化面板，末行容器可能尚未生成：先滚动到可视区再重试取容器
            var container = (Control)list.ContainerFromIndex(lastIndex);
            for (var attempt = 0; container is null && attempt < 30; attempt++)
            {
                list.ScrollIntoView(lastIndex);
                UpdateLayout();
                await Task.Delay(50);
                container = (Control)list.ContainerFromIndex(lastIndex);
            }
            if (container is null)
            {
                throw new InvalidOperationException("无法获取行容器（index=" + lastIndex + "）");
            }

            // 事件在行的子控件上发出：隧道阶段经过列表，真实触发
            // OnRemoteListPointerPressed 的 onItem 守卫分支
            var target = container.GetVisualDescendants().OfType<Control>().FirstOrDefault() ?? container;
            var rowPoint = target.TranslatePoint(
                new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), this)
                ?? new Point(10, 10);
            var rowPointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            target.RaiseEvent(new PointerPressedEventArgs(
                target,
                rowPointer,
                this,
                rowPoint,
                0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
                KeyModifiers.None,
                clickCount: 1));
            UpdateLayout();
            rowOk = selectedBefore >= 0
                && list.SelectedIndex == selectedBefore
                && paneVm.SelectedRemoteEntry is not null
                && paneVm.SelectedRemoteEntry!.Name == lastEntry.Name;
            AppLog.Info($"[SelfTest] r64: ② 行按下不清选中（selected={paneVm.SelectedRemoteEntry?.Name ?? "null"}）=> {(rowOk ? "OK" : "FAILED")}");

            SaveVisualPng(Content as Visual, "r64-browser.png");

            var ok = clearOk && rowOk;
            AppLog.Info($"[SelfTest] r64: 自检完成，空白区清选={(clearOk ? "OK" : "失败")} 行选中={(rowOk ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r64: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r65</c>：实机自检 r65 SFTP 空白菜单启用态刷新（首连修复）。
    /// 首次 SSH 连接后、SFTP 列表加载完成且无选中条目时，空白区右键菜单 5 项
    /// （上一级/新建文件夹/新建文件/刷新/上传）必须全部启用且可见，条目菜单项
    /// 必须隐藏。落盘 r65-browser.png。
    /// </summary>
    private async Task RunR65SelfTestAsync()
    {
        var ok = false;
        try
        {
            AppLog.Info("[SelfTest] r65: 等主窗稳定 1.5s 后开始");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var firstKey = _config?.SshSessions.Sessions.FirstOrDefault()?.Key;
            if (string.IsNullOrEmpty(firstKey))
            {
                throw new InvalidOperationException("config 中没有 SSH 会话，无法跑 r65 自检");
            }

            AppLog.Info($"[SelfTest] r65: 打开 SSH 会话 {firstKey}");
            var openTask = OpenSshSessionAsync(firstKey!);
            TerminalTab? sshTab = null;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!(openTask.IsCompleted && sshTab is not null && sshTab.IsAlive))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("SSH 会话 90s 内未连接成功");
                }

                sshTab = viewModel.Tabs.FirstOrDefault(t => t.SessionId == firstKey);
                await Task.Delay(250);
            }

            await openTask;
            await Task.Delay(800);
            UpdateLayout();

            var paneVm = sshTab.SftpPane;
            if (paneVm is null)
            {
                throw new InvalidOperationException("SFTP 面板未创建");
            }

            var dl = DateTime.UtcNow.AddSeconds(30);
            while (paneVm.RemoteEntries.Count == 0 && DateTime.UtcNow < dl)
            {
                await Task.Delay(200);
                UpdateLayout();
            }
            AppLog.Info($"[SelfTest] r65: SFTP 列表 {paneVm.RemoteEntries.Count} 项 @ {paneVm.RemotePath}");
            if (paneVm.RemoteEntries.Count == 0)
            {
                throw new TimeoutException("SFTP 列表 30s 内未加载");
            }

            var sftpPane = this.FindControl<SessionSidebar>("Sidebar")
                ?.FindControl<SshBrowserPanel>("SshBrowserPanel")?.SftpPaneControl;
            if (sftpPane is null)
            {
                throw new InvalidOperationException("未找到 SFTP 面板控件");
            }
            UpdateLayout();

            // 无选中条目 → 空白区右键菜单变体；加载完成后 5 项必须全部启用+可见。
            var up = sftpPane.MenuBlankUpControl;
            var nf = sftpPane.MenuBlankNewFolderControl;
            var nfo = sftpPane.MenuBlankNewFileControl;
            var refi = sftpPane.MenuBlankRefreshControl;
            var upl = sftpPane.MenuBlankUploadControl;
            var upOk = up.IsVisible && up.IsEnabled;
            var nfOk = nf.IsVisible && nf.IsEnabled;
            var nfoOk = nfo.IsVisible && nfo.IsEnabled;
            var refOk = refi.IsVisible && refi.IsEnabled;
            var uplOk = upl.IsVisible && upl.IsEnabled;
            var entryHidden = !sftpPane.MenuOpenControl.IsVisible
                && !sftpPane.MenuDeleteControl.IsVisible;
            ok = upOk && nfOk && nfoOk && refOk && uplOk && entryHidden;
            AppLog.Info($"[SelfTest] r65: 空白菜单启用态 上一级={(upOk ? "OK" : "FAIL")} " +
                $"新建夹={(nfOk ? "OK" : "FAIL")} 新建文={(nfoOk ? "OK" : "FAIL")} " +
                $"刷新={(refOk ? "OK" : "FAIL")} 上传={(uplOk ? "OK" : "FAIL")} " +
                $"条目项隐藏={(entryHidden ? "OK" : "FAIL")} => {(ok ? "OK" : "FAILED")}");

            SaveVisualPng(Content as Visual, "r65-browser.png");

            AppLog.Info($"[SelfTest] r65: 自检完成，空白菜单启用态刷新={(ok ? "OK" : "失败")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r65: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// <c>--selftest-r66</c>：实机自检 r66 远程监控按钮切换（同 IP 双会话点击失效修复）。
    /// 验证：② 隧道按下翻转一次且按钮视觉同步 ③ 轮询数据 MonitorText 出现
    /// ④ 直接触发按钮内部翻转路径不污染 VM（OneWay 绑定屏蔽）。
    /// 落盘 r66-monitor.png。
    /// </summary>
    private async Task RunR66SelfTestAsync()
    {
        var ok = false;
        try
        {
            AppLog.Info("[SelfTest] r66: 等主窗稳定 1.5s 后开始");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var firstKey = _config?.SshSessions.Sessions.FirstOrDefault()?.Key;
            if (string.IsNullOrEmpty(firstKey))
            {
                throw new InvalidOperationException("config 中没有 SSH 会话，无法跑 r66 自检");
            }

            AppLog.Info($"[SelfTest] r66: 打开 SSH 会话 {firstKey}");
            var openTask = OpenSshSessionAsync(firstKey!);
            TerminalTab? sshTab = null;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!(openTask.IsCompleted && sshTab is not null && sshTab.IsAlive))
            {
                if (openTask.IsCompleted && openTask.IsFaulted)
                {
                    throw new InvalidOperationException("SSH 会话连接失败", openTask.Exception);
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("SSH 会话 90s 内未连接成功");
                }

                sshTab = viewModel.Tabs.FirstOrDefault(t => t.SessionId == firstKey);
                await Task.Delay(250);
            }

            await openTask;
            await Task.Delay(800);
            UpdateLayout();

            var paneVm = sshTab!.SftpPane;
            if (paneVm is null)
            {
                throw new InvalidOperationException("SFTP 面板未创建");
            }

            var sftpPane = this.FindControl<SessionSidebar>("Sidebar")
                ?.FindControl<SshBrowserPanel>("SshBrowserPanel")?.SftpPaneControl;
            if (sftpPane is null)
            {
                throw new InvalidOperationException("未找到 SFTP 面板控件");
            }

            UpdateLayout();

            var button = sftpPane.FindControl<Avalonia.Controls.Primitives.ToggleButton>("MonitorToggleButton");
            if (button is null)
            {
                throw new InvalidOperationException("未找到 MonitorToggleButton");
            }

            // ① 记录初始状态
            var initialOn = paneVm.IsMonitorOn;
            AppLog.Info($"[SelfTest] r66: 初始状态 IsMonitorOn={initialOn} IsChecked={button.IsChecked}");

            // ② 通过隧道处理器模拟一次左键按下（真实用户操作路径）
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var btnPoint = button.TranslatePoint(
                new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), this) ?? new Point(10, 10);
            button.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(
                button, pointer, this, btnPoint, 0,
                new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton,
                    Avalonia.Input.PointerUpdateKind.LeftButtonPressed),
                Avalonia.Input.KeyModifiers.None,
                clickCount: 1));
            await Task.Delay(500);
            UpdateLayout();

            var afterFlip = paneVm.IsMonitorOn;
            var flipOk = afterFlip != initialOn;
            var visualOk = button.IsChecked == afterFlip;
            AppLog.Info($"[SelfTest] r66: ② 翻转 {initialOn}→{afterFlip} visual同步={visualOk} => {(flipOk && visualOk ? "OK" : "FAILED")}");

            // ③ 验证 MonitorText（若刚开为 on）
            var dataOk = true;
            if (afterFlip)
            {
                dataOk = false;
                var dl2 = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < dl2)
                {
                    if (viewModel.MonitorText is { Length: > 0 })
                    {
                        dataOk = true;
                        break;
                    }
                    await Task.Delay(200);
                    UpdateLayout();
                }
            }

            AppLog.Info($"[SelfTest] r66: ③ 轮询数据 MonitorText={(dataOk ? "非空 OK" : "为空 FAIL/SKIP")}");

            // ④ 模拟按钮内部翻转路径的最坏情况（Toggle() 语义即本地写
            // IsChecked = !IsChecked）：验证 OneWay 绑定屏蔽 VM，视图模型不被污染
            var beforeDirect = paneVm.IsMonitorOn;
            button.IsChecked = !beforeDirect;
            await Task.Delay(200);
            UpdateLayout();
            var directNoop = paneVm.IsMonitorOn == beforeDirect;
            // 内部路径只改本地 IsChecked；恢复视觉态跟随 VM，保证截图一致
            button.IsChecked = beforeDirect;
            await Task.Delay(100);
            UpdateLayout();
            AppLog.Info($"[SelfTest] r66: ④ 内部翻转不污染VM(OneWay)={(directNoop ? "OK" : "FAILED")}");

            // ⑤ 恢复自检前的监控状态（避免改动用户已保存的开关）
            if (paneVm.IsMonitorOn != initialOn)
            {
                paneVm.IsMonitorOn = initialOn;
                await Task.Delay(300);
            }

            ok = flipOk && visualOk && dataOk && directNoop;
            SaveVisualPng(Content as Visual, "r66-monitor.png");
            AppLog.Info($"[SelfTest] r66: 自检完成 翻转={(flipOk ? "OK" : "FAIL")} 视觉同步={(visualOk ? "OK" : "FAIL")} 数据={((dataOk) ? "OK" : "SKIP/FAIL")} 内部路径隔离={(directNoop ? "OK" : "FAIL")}，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(ok ? 0 : 2);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r66: 自检异常", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    private static void WalkR56Runs(Avalonia.Media.Drawing d, List<(string Text, FontWeight Weight, Color Color)> list)
    {
        if (d is Avalonia.Media.GlyphRunDrawing grd && grd.GlyphRun is not null)
        {
            var color = grd.Foreground is ISolidColorBrush s ? s.Color : default;
            list.Add((grd.GlyphRun.Characters.ToString(), grd.GlyphRun.GlyphTypeface.Weight, color));
        }
        else if (d is Avalonia.Media.DrawingGroup dg)
        {
            foreach (var c in dg.Children)
            {
                WalkR56Runs(c, list);
            }
        }
    }

    private static void WalkR53Drawings(Avalonia.Media.Drawing d, string indent, System.Text.StringBuilder sb)
    {
        if (d is Avalonia.Media.GlyphRunDrawing grd && grd.GlyphRun is not null)
        {
            var gr = grd.GlyphRun;
            sb.AppendLine(indent + "GLYPHRUN text='" + gr.Characters + "' baselineOrigin=" + gr.BaselineOrigin
                + " emSize=" + gr.FontRenderingEmSize + " face=" + gr.GlyphTypeface.FamilyName);
        }
        else if (d is Avalonia.Media.DrawingGroup dg)
        {
            foreach (var c in dg.Children)
                WalkR53Drawings(c, indent + "  ", sb);
        }
        else
        {
            sb.AppendLine(indent + "OTHER " + d.GetType().Name);
        }
    }

    private async void OnTerminalKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TerminalControl terminal || e.Key != Key.V)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        var paste = modifiers == KeyModifiers.Control
            || modifiers == (KeyModifiers.Control | KeyModifiers.Shift);
        if (!paste)
        {
            return;
        }

        e.Handled = true;
        await PasteNormalizedAsync(terminal);
    }

    /// <summary>
    /// 把剪贴板行尾规范成单个 \r 再写入 pty（MobaXterm 行为）：Windows 剪贴板文本
    /// 是 CRLF，原样写入时远端 tty 的 ICRNL 把 \r 转成提交、随后的 \n 再多打一个
    /// 换行 → 多行粘贴的每条命令之间多出一行空提示符。有选区时回退库默认粘贴
    /// （保留“粘贴替换选区”行为），无文本时同样回退。
    /// </summary>
    internal static string NormalizePtyPasteText(string text)
    {
        // 所有行尾统一为单个 \r；末尾连续的空行（多余 \r）去掉，
        // 只保留最后一个换行符，使多行粘贴在远端逐行执行且不产生空行
        // （与 MobaXterm 粘贴行为一致）。
        var normalized = text.Replace("\r\n", "\r").Replace('\n', '\r');
        var end = normalized.Length;
        while (end > 0 && normalized[end - 1] == '\r')
        {
            end--;
        }

        if (end == 0)
        {
            return string.Empty;
        }

        return end == normalized.Length
            ? normalized
            : normalized[..end] + "\r";
    }

    /// <summary>
    /// 把文本行尾规范成单个 \r 后写入终端（bracketed-paste 模式时加包裹）。
    /// 拆成独立方法便于测试（不依赖剪贴板）。
    /// </summary>
    internal static Task SendPasteTextAsync(TerminalControl terminal, string text)
    {
        text = NormalizePtyPasteText(text);
        if (terminal.Terminal.BracketedPasteMode)
        {
            text = "\u001b[200~" + text + "\u001b[201~";
        }

        return terminal.SendInputAsync(text);
    }

    internal async Task PasteNormalizedAsync(TerminalControl terminal)
    {
        // r39 只读终端：所有粘贴入口（菜单/右键/Ctrl+V）都汇聚到这里
        if (FindTabForTerminal(terminal)?.IsReadOnlyTerminal == true)
        {
            return;
        }

        if (terminal.Terminal.Selection.HasSelection)
        {
            // 粘贴替换选区：走库默认路径（保留其删除选区序列的行为）。
            await terminal.PasteAsync();
            return;
        }

        string? text = TopLevel.GetTopLevel(terminal)?.Clipboard is { } clip
            ? await clip.TryGetTextAsync()
            : null;

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        await SendPasteTextAsync(terminal, text);
    }

    private static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
        }

        if (bytesPerSecond >= 1024)
        {
            return $"{bytesPerSecond / 1024:0.0} KB/s";
        }

        return $"{bytesPerSecond:0} B/s";
    }

    private static string FormatEta(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }

    private sealed record ZmodemTiming(string Name, DateTime StartedAt, long StartBytes);

    private sealed class SerialTabState
    {
        public required string Port { get; init; }
        public required SerialConnectionConfig Config { get; init; }
        public required TerminalControl Terminal { get; init; }
        public TerminalTab? Tab { get; set; }
        public ISerialConnection? Connection { get; set; }
        public bool TerminalAttached { get; set; }
        public bool AwaitingReconnect { get; set; }
        public bool Closed { get; set; }
        public DateTime NextReconnectAt { get; set; }
        public int ReconnectAttempts { get; set; }
        public string? LastReconnectError { get; set; }

        public bool IsTransportOpen =>
            Connection is { IsOpen: true } && !Connection.Pty.IsInputCompleted;
    }

    private sealed record DetachedTabState(Window Window, int Index)
    {
        public bool SuppressReattach { get; set; }
    }

    /// <summary>
    /// <c>--selftest-r68</c>：实机自检 r68 串口断线自动重连（EOF/设备消失路径）。
    /// 取一个真实串口开标签，然后走与读流 EOF 相同的上膛路径（MarkSerialDisconnected）
    /// 模拟断线；不做任何操作，只等 2s 端口扫描自动重开，断言标签恢复 Connected。
    /// 结果写 run.log 后退出。
    /// </summary>
    private async Task RunR68SelfTestAsync()
    {
        string? tempPort = null;
        TerminalTab? tab = null;
        try
        {
            AppLog.Info("[SelfTest] r68: 等主窗稳定 1.5s 后验证串口断线自动重连");
            await Task.Delay(1500);
            if (_config is null || DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainWindow 未就绪");
            }

            RefreshPorts();
            await Task.Delay(1200);

            var candidates = _config.Ports
                .Select(port => port.Device)
                .Where(device => _config.SerialSessions.Get(device) is null)
                .ToList();
            if (candidates.Count == 0)
            {
                AppLog.Info(
                    "[SelfTest] r68: SKIP 没有空闲串口（当前端口: "
                    + string.Join(",", _config.Ports.Select(port => port.Device)) + "）");
                (Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0);
                return;
            }

            Exception? lastError = null;
            foreach (var candidate in candidates)
            {
                try
                {
                    _config.SerialSessions.Upsert(
                        candidate,
                        new SerialConnectionConfig { Port = candidate, Baudrate = 115200 },
                        candidate);
                    tempPort = candidate;
                    OpenSerialSession(candidate);
                    await Task.Delay(700);
                    tab = viewModel.Tabs.FirstOrDefault(item =>
                        item.Kind == SessionKind.Serial
                        && string.Equals(item.SessionId, candidate, StringComparison.OrdinalIgnoreCase));
                    if (tab is null)
                    {
                        throw new InvalidOperationException("标签未创建");
                    }

                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    AppLog.Info(
                        $"[SelfTest] r68: 打开 {candidate} 失败: {ex.Message}，尝试下一个");
                    if (tempPort is not null)
                    {
                        _config.SerialSessions.Remove(tempPort);
                        tempPort = null;
                    }

                    tab = null;
                }
            }

            if (tab is null || tempPort is null)
            {
                throw lastError ?? new InvalidOperationException("没有可用的串口");
            }

            var state = _serialTabs[tab];
            if (!tab.IsAlive || !state.IsTransportOpen)
            {
                throw new InvalidOperationException(
                    $"打开后状态异常 IsAlive={tab.IsAlive} IsTransportOpen={state.IsTransportOpen}");
            }

            AppLog.Info(
                $"[SelfTest] r68: ① {tempPort} 标签已连接 IsAlive=True IsTransportOpen=True OK");

            MarkSerialDisconnected(state);
            UpdateConnectionStates();
            if (tab.IsAlive)
            {
                throw new InvalidOperationException("断线后 IsAlive 仍为 true");
            }

            AppLog.Info(
                $"[SelfTest] r68: ② 模拟读流 EOF 断线: 状态=\"{tab.StatusMessage}\" IsAlive=False OK");

            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline && !tab.IsAlive)
            {
                await Task.Delay(300);
            }

            if (!tab.IsAlive)
            {
                throw new InvalidOperationException(
                    $"12s 内未自动重连（状态=\"{tab.StatusMessage}\"）");
            }

            var reopened = _serialTabs[tab];
            if (!reopened.IsTransportOpen)
            {
                throw new InvalidOperationException("自动重连后传输未打开");
            }

            AppLog.Info(
                $"[SelfTest] r68: ③ 2s 扫描后自动重连: 状态=\"{tab.StatusMessage}\" IsTransportOpen=True OK");
            AppLog.Info("[SelfTest] r68: 自检完成 断线上膛=OK 自动重连=OK 标签恢复=OK，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r68: 自检失败", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
        finally
        {
            if (tempPort is not null && _config is not null)
            {
                _config.SerialSessions.Remove(tempPort);
            }
        }
    }

    /// <summary>
    /// <c>--selftest-r67</c>：实机自检 r67 标签拖动换位（实时换位，MobaXterm/Chrome 式）。
    /// 开三个假 tab（本地终端/COM99 串口/devuser@192.0.2.48 SSH，即用户截图场景），
    /// 在标签条上模拟「按下→3px 位移（阈值内不换位）→10px 位移（开拖）→越过 b 中线
    /// （实时换位 a,c,b）→越过 a 中线（c,a,b）→松开」，断言顺序、选中项、
    /// dragging 半透明样式与标签数。结果写 run.log，落盘 r67-tabs.png。
    /// </summary>
    private async Task RunR67SelfTestAsync()
    {
        try
        {
            AppLog.Info("[SelfTest] r67: 等主窗稳定 1.5s 后开 3 个 tab 验证拖动换位");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var t1 = CreateTerminal();
            var aTab = new TerminalTab
            {
                Title = "local-a",
                SessionId = "local:r67a",
                Kind = SessionKind.Local,
                Content = t1,
                Terminal = t1,
                IsAlive = true,
                CloseAction = () => { },
            };
            var t2 = CreateTerminal();
            var bTab = new TerminalTab
            {
                Title = "COM99",
                SessionId = "serial:r67b",
                Kind = SessionKind.Serial,
                Content = t2,
                Terminal = t2,
                IsAlive = true,
                CloseAction = () => { },
            };
            var t3 = CreateTerminal();
            var cTab = new TerminalTab
            {
                Title = "devuser@192.0.2.48",
                SessionId = "ssh:r67c",
                Kind = SessionKind.Ssh,
                Content = t3,
                Terminal = t3,
                IsAlive = true,
                CloseAction = () => { },
            };
            viewModel.AddTab(aTab);
            viewModel.AddTab(bTab);
            viewModel.AddTab(cTab);
            await Task.Delay(500);
            UpdateLayout();

            var strip = this.FindControl<Grid>("TabStripGrid");
            if (strip is null)
            {
                throw new InvalidOperationException("未找到 TabStripGrid");
            }

            Border FindTab(TerminalTab tab) =>
                strip.GetVisualDescendants()
                    .OfType<Border>()
                    .First(b => ReferenceEquals(b.Tag, tab));

            var win = TopLevel.GetTopLevel(this);
            if (win is null)
            {
                throw new InvalidOperationException("TopLevel 未就绪");
            }

            Point CenterPt(TerminalTab tab)
            {
                var b = FindTab(tab);
                var p = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), win);
                return p ?? new Point(0, 0);
            }

            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            string Order() => string.Join(",", viewModel.Tabs.Select(t => t.Title));
            const string Oabc = "local-a,COM99,devuser@192.0.2.48";

            // ① 普通点击（按下+原地松开，无位移）：只选中，不换顺序。
            var aCenter = CenterPt(aTab);
            var aBorder = FindTab(aTab);
            aBorder.RaiseEvent(new PointerPressedEventArgs(
                aBorder, pointer, win, aCenter, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, clickCount: 1)
            {
                RoutedEvent = InputElement.PointerPressedEvent,
            });
            await Task.Delay(80);
            aBorder.RaiseEvent(new PointerReleasedEventArgs(
                aBorder, pointer, win, aCenter, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                KeyModifiers.None, MouseButton.Left)
            {
                RoutedEvent = InputElement.PointerReleasedEvent,
            });
            await Task.Delay(80);
            if (Order() != Oabc)
            {
                throw new InvalidOperationException($"① 单击不应换位，实际 {Order()}");
            }
            AppLog.Info($"[SelfTest] r67: ① 单击无位移: 顺序={Order()} 选中={viewModel.SelectedTab?.Title} OK");

            // ② 按下第 3 个 tab：3px 位移（阈值内）不开拖、不换顺序；10px 开拖但仍在自身区域。
            var cCenter = CenterPt(cTab);
            FindTab(cTab).RaiseEvent(new PointerPressedEventArgs(
                FindTab(cTab), pointer, win, cCenter, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, clickCount: 1)
            {
                RoutedEvent = InputElement.PointerPressedEvent,
            });
            await Task.Delay(60);

            void MoveTo(double x, double y) => strip.RaiseEvent(new PointerEventArgs(
                InputElement.PointerMovedEvent, strip, pointer, win,
                new Point(x, y), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
                KeyModifiers.None));

            MoveTo(cCenter.X - 3, cCenter.Y);
            await Task.Delay(60);
            if (Order() != Oabc)
            {
                throw new InvalidOperationException($"② 阈值内不应换位，实际 {Order()}");
            }

            MoveTo(cCenter.X - 10, cCenter.Y);
            await Task.Delay(60);
            if (Order() != Oabc)
            {
                throw new InvalidOperationException($"② 开拖未越中线不应换位，实际 {Order()}");
            }
            if (!FindTab(cTab).Classes.Contains("dragging"))
            {
                throw new InvalidOperationException("② 开拖后被拖 tab 应带 dragging 半透明样式");
            }
            AppLog.Info($"[SelfTest] r67: ② 3px 内不换 OK；10px 开拖 OK（dragging 样式生效）顺序={Order()}");

            // ③ 越过 b 中线：c 实时插入 b 之前。
            var aMid = CenterPt(aTab).X;
            var bMid = CenterPt(bTab).X;
            MoveTo((aMid + bMid) / 2, cCenter.Y);
            await Task.Delay(60);
            if (Order() != "local-a,devuser@192.0.2.48,COM99")
            {
                throw new InvalidOperationException($"③ c 应插入 b 之前，实际 {Order()}");
            }
            AppLog.Info($"[SelfTest] r67: ③ 越过 b 中线实时换位: {Order()} OK");

            // ④ 越过 a 中线：c 排到最前。
            aMid = CenterPt(aTab).X;
            MoveTo(aMid - 5, cCenter.Y);
            await Task.Delay(60);
            if (Order() != "devuser@192.0.2.48,local-a,COM99")
            {
                throw new InvalidOperationException($"④ c 应排最前，实际 {Order()}");
            }
            AppLog.Info($"[SelfTest] r67: ④ 越过 a 中线实时换位: {Order()} OK");

            // ④.5 r67.1：动画断言——被拖标签跟手位移非零，被挤开的标签 a 正在滑动。
            var cB = FindTab(cTab);
            var aB = FindTab(aTab);
            if (cB.RenderTransform is not Avalonia.Media.TranslateTransform ct || Math.Abs(ct.X) <= 0.5)
            {
                throw new InvalidOperationException($"④.5 被拖标签应带跟手位移，实际 {cB.RenderTransform}");
            }
            if (aB.RenderTransform is not Avalonia.Media.TranslateTransform at2 || Math.Abs(at2.X) <= 0.5)
            {
                throw new InvalidOperationException($"④.5 被挤开标签应带滑动位移，实际 {aB.RenderTransform}");
            }
            // r67.2：拖动中被拖标签容器 Z 序应置顶（防止被换位后的邻居盖住「消失」）
            var cCp = FindPanelChildAncestor(cB);
            if (cCp is null || cCp.ZIndex != TabDragZIndex)
            {
                throw new InvalidOperationException($"④.5 被拖标签容器应 Z={TabDragZIndex}，实际 {cCp?.ZIndex}");
            }
            AppLog.Info($"[SelfTest] r67: ④.5 动画: 被拖跟手 X={ct.X:0.#} 被挤开滑动 X={at2.X:0.#} 被拖容器 Z={cCp.ZIndex} OK");
            SaveVisualPng(Content as Visual, "r67-drag-mid.png");

            // ⑤ 松开：顺序保持、选中项不变（按下时已选 c）、dragging 样式清除。
            strip.RaiseEvent(new PointerReleasedEventArgs(
                strip, pointer, win, new Point(aMid - 5, cCenter.Y), 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                KeyModifiers.None, MouseButton.Left)
            {
                RoutedEvent = InputElement.PointerReleasedEvent,
            });
            await Task.Delay(80);
            if (Order() != "devuser@192.0.2.48,local-a,COM99")
            {
                throw new InvalidOperationException($"⑤ 松开后顺序应保持 c,a,b，实际 {Order()}");
            }
            if (viewModel.SelectedTab?.Title != "devuser@192.0.2.48")
            {
                throw new InvalidOperationException($"⑤ 拖动不应改变选中项，当前 {viewModel.SelectedTab?.Title}");
            }
            if (FindTab(cTab).Classes.Contains("dragging"))
            {
                throw new InvalidOperationException("⑤ 松开后 dragging 样式应清除");
            }
            if (viewModel.Tabs.Count != 3)
            {
                throw new InvalidOperationException($"⑤ tab 数应为 3，实际 {viewModel.Tabs.Count}");
            }
            AppLog.Info($"[SelfTest] r67: ⑤ 松开落位: 顺序={Order()} 选中={viewModel.SelectedTab?.Title} 样式已恢复 OK");

            // ⑤.5 r67.1：等 120ms 落位动画结束，全部位移清零。
            await Task.Delay(400);
            foreach (var t in new[] { aTab, bTab, cTab })
            {
                var border = FindTab(t);
                if (border.RenderTransform is not null)
                {
                    throw new InvalidOperationException($"⑤.5 落位后位移应清零，{t.Title} 仍为 {border.RenderTransform}");
                }
                // r67.2：松手后 Z 序也应全部恢复 0
                var cp = FindPanelChildAncestor(border);
                if (cp is not null && cp.ZIndex != 0)
                {
                    throw new InvalidOperationException($"⑤.5 落位后 Z 序应清零，{t.Title} 容器 Z={cp.ZIndex}");
                }
            }
            AppLog.Info("[SelfTest] r67: ⑤.5 落位动画完成，全部位移清零、Z 序恢复 OK");

            // ⑥ r67.3：两个「拖过去就消失」的真因回归。
            //   ① 标签条不得裁剪跟手标签：Avalonia 12 的 ItemsControl 默认
            //      ClipToBounds=True，把标签条裁到「所有槽位总和」那么宽，被拖标签
            //      一移出这个矩形就被裁成一条缝甚至完全不可见；
            //   ② 二次拖动必须落位：落位动画结束会把计时器 Stop（对象仍存活），
            //      若 Ensure 只判 null 就返回，此后拖动位移永不清零，标签停在屏外。
            var items = this.FindControl<ItemsControl>("TabItemsControl")
                ?? throw new InvalidOperationException("⑥ 找不到 TabItemsControl");
            if (items.ClipToBounds)
            {
                throw new InvalidOperationException("⑥ 标签条不应裁剪跟手标签（ClipToBounds 应为 false）");
            }

            var firstTab = viewModel.Tabs[0];
            var fCenter = CenterPt(firstTab);
            FindTab(firstTab).RaiseEvent(new PointerPressedEventArgs(
                FindTab(firstTab), pointer, win, fCenter, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, clickCount: 1)
            {
                RoutedEvent = InputElement.PointerPressedEvent,
            });
            await Task.Delay(60);
            var farX = fCenter.X + 260;
            MoveTo(farX, fCenter.Y);
            await Task.Delay(120);
            if (FindTab(firstTab).RenderTransform is not Avalonia.Media.TranslateTransform ft
                || Math.Abs(ft.X) <= 50)
            {
                throw new InvalidOperationException(
                    $"⑥ 跟手位移应把标签拖出标签条，实际 {FindTab(firstTab).RenderTransform}");
            }
            var farB = FindTab(firstTab);
            var farRight = farB.TranslatePoint(new Point(farB.Bounds.Width, 0), items)?.X ?? 0;
            if (farRight <= items.Bounds.Width)
            {
                throw new InvalidOperationException(
                    $"⑥ 被拖标签右沿({farRight:0}) 应越出标签条宽({items.Bounds.Width:0})");
            }

            // 拖动中取证：标签越出标签条也完整可见（裁剪修复的实机凭据）。
            SaveVisualPng(Content as Visual, "r67-drag-out.png");

            strip.RaiseEvent(new PointerReleasedEventArgs(
                strip, pointer, win, new Point(farX, fCenter.Y), 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                KeyModifiers.None, MouseButton.Left)
            {
                RoutedEvent = InputElement.PointerReleasedEvent,
            });
            await Task.Delay(600);
            foreach (var t in viewModel.Tabs.ToList())
            {
                if (FindTab(t).RenderTransform is not null)
                {
                    throw new InvalidOperationException(
                        $"⑥ 二次拖动松手后位移应清零，{t.Title} 仍为 {FindTab(t).RenderTransform}");
                }
            }
            AppLog.Info($"[SelfTest] r67: ⑥ 拖出标签条不裁剪 + 二次拖动落位清零 OK");

            SaveVisualPng(Content as Visual, "r67-tabs.png");
            AppLog.Info("[SelfTest] r67: 自检完成 单击不换=OK 阈值内不换=OK 开拖=OK 实时换位(a,c,b→c,a,b)=OK 选中不变=OK 样式恢复=OK 动画(跟手+滑动+落位清零)=OK 不裁剪+二次拖动落位=OK，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r67: 自检失败", ex);
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    /// <summary>
    /// r69：实机自检终端滚动条（MobaXterm 式）。headless 的 DispatcherTimer 不随
    /// RunJobs 触发，所以「按住轨道连续翻页」只能在真实窗口里验。
    /// </summary>
    private async Task RunR69SelfTestAsync()
    {
        try
        {
            AppLog.Info("[SelfTest] r69: 等主窗稳定 1.5s 后开本地终端灌回滚");
            await Task.Delay(1500);
            if (DataContext is not MainViewModel viewModel)
            {
                throw new InvalidOperationException("MainViewModel 未就绪");
            }

            var terminal = CreateTerminal();
            var tab = new TerminalTab
            {
                Title = "r69-scroll",
                SessionId = "local:r69",
                Kind = SessionKind.Local,
                Content = terminal,
                Terminal = terminal,
                IsAlive = true,
                CloseAction = () => { },
            };
            viewModel.AddTab(tab);
            await Task.Delay(500);
            UpdateLayout();

            // 直接写模拟器缓冲区（不经过 PTY）灌满回滚，再显式改一次 ViewportY，
            // 让库的 UpdateScrollBar 把 Maximum/Value 同步到真实滚动条。
            for (var i = 0; i < 300; i++)
            {
                terminal.Terminal.Write($"scroll line {i:000}\r\n");
            }
            terminal.ViewportY = 0;
            await Task.Delay(150);
            terminal.ViewportY = terminal.MaxScrollback;
            await Task.Delay(250);
            UpdateLayout();

            var bar = terminal.GetVisualDescendants().OfType<ScrollBar>().SingleOrDefault()
                ?? throw new InvalidOperationException("终端模板里没有 ScrollBar");
            if (!bar.IsVisible || bar.Bounds.Width < 15.5 || bar.Bounds.Width > 16.5)
            {
                throw new InvalidOperationException(
                    $"滚动条应常显 16px：visible={bar.IsVisible} width={bar.Bounds.Width:0.##}");
            }
            if (terminal.MaxScrollback < 3 * bar.ViewportSize)
            {
                throw new InvalidOperationException(
                    $"回滚不足：max={terminal.MaxScrollback} viewport={bar.ViewportSize}");
            }
            if (Math.Abs(bar.LargeChange - bar.ViewportSize) > 0.01)
            {
                throw new InvalidOperationException(
                    $"翻页量应为一整屏：LargeChange={bar.LargeChange} ViewportSize={bar.ViewportSize}");
            }
            // 断言「滑块跟着回滚位置」而不是死盯最底部：写缓冲区后可能还有 1–2 行
            // 在排布阶段滚进回滚区，MaxScrollback 会再涨一点（实机首跑 279 vs 281）。
            if (Math.Abs(bar.Value - terminal.ViewportY) > 0.5
                || terminal.ViewportY < terminal.MaxScrollback - 3)
            {
                throw new InvalidOperationException(
                    $"滑块未同步到底部：Value={bar.Value} ViewportY={terminal.ViewportY} " +
                    $"MaxScrollback={terminal.MaxScrollback}");
            }
            AppLog.Info(
                $"[SelfTest] r69: ① 常显 16px + 滑块同步底部 + LargeChange=一屏({bar.ViewportSize}) OK");

            var pageUp = bar.GetVisualDescendants().OfType<RepeatButton>()
                .FirstOrDefault(b => b.Name == "PART_PageUpButton")
                ?? throw new InvalidOperationException("找不到 PART_PageUpButton");
            var thumb = bar.GetVisualDescendants().OfType<Thumb>().FirstOrDefault()
                ?? throw new InvalidOperationException("找不到 Thumb");
            if (pageUp.Delay != 300 || pageUp.Interval != 250)
            {
                throw new InvalidOperationException(
                    $"按住节奏应为 300/250：{pageUp.Delay}/{pageUp.Interval}");
            }

            var root = (Visual)this;
            var win = TopLevel.GetTopLevel(this)
                ?? throw new InvalidOperationException("TopLevel 未就绪");
            var barCenterX = bar.TranslatePoint(new Point(bar.Bounds.Width / 2, 0), root)!.Value.X;
            var thumbTop = thumb.TranslatePoint(new Point(0, 0), root)!.Value.Y;
            var pageUpTop = pageUp.TranslatePoint(new Point(0, 0), root)!.Value.Y;
            var press = new Point(barCenterX, Math.Max(pageUpTop + 4, thumbTop - 40));

            // ② 点一次 = 翻一整屏。
            var beforeClick = terminal.ViewportY;
            pageUp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(200);
            var afterClick = terminal.ViewportY;
            if (beforeClick - afterClick != (int)bar.ViewportSize)
            {
                throw new InvalidOperationException(
                    $"点一次应翻一屏：{beforeClick} -> {afterClick}（屏 {bar.ViewportSize}）");
            }
            AppLog.Info(
                $"[SelfTest] r69: ② 单击翻一整屏 {beforeClick}->{afterClick} OK");

            // ③ 按住滑块上方 1.2s：连续上翻（≥3 次），松手立即停。
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            pageUp.RaiseEvent(new PointerPressedEventArgs(
                pageUp, pointer, win, press, 0,
                new PointerPointProperties(
                    RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, clickCount: 1)
            {
                RoutedEvent = InputElement.PointerPressedEvent,
            });

            var steps = new List<int> { terminal.ViewportY };
            var hold = System.Diagnostics.Stopwatch.StartNew();
            while (hold.ElapsedMilliseconds < 1200)
            {
                await Task.Delay(40);
                if (terminal.ViewportY != steps[^1])
                {
                    steps.Add(terminal.ViewportY);
                }
            }
            SaveVisualPng(Content as Visual, "r69-scrollbar.png");

            pageUp.RaiseEvent(new PointerReleasedEventArgs(
                pageUp, pointer, win, press, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                KeyModifiers.None, MouseButton.Left)
            {
                RoutedEvent = InputElement.PointerReleasedEvent,
            });
            await Task.Delay(150);
            var afterRelease = terminal.ViewportY;
            await Task.Delay(700);
            if (terminal.ViewportY != afterRelease)
            {
                throw new InvalidOperationException(
                    $"松手后应停止翻页：{afterRelease} -> {terminal.ViewportY}");
            }
            for (var i = 1; i < steps.Count; i++)
            {
                if (steps[i] >= steps[i - 1])
                {
                    throw new InvalidOperationException(
                        $"按住应连续上翻，实际序列 {string.Join(",", steps)}");
                }
            }
            if (steps.Count < 4)
            {
                throw new InvalidOperationException(
                    $"按住 1.2s 应至少翻 3 次，实际 {steps.Count - 1} 次：{string.Join(",", steps)}");
            }
            AppLog.Info(
                $"[SelfTest] r69: ③ 按住连续上翻 {steps.Count - 1} 次（{string.Join(",", steps)}），" +
                $"松手停在 {afterRelease} OK");

            AppLog.Info("[SelfTest] r69: 自检完成 常显16px=OK 滑块同步=OK 单击一屏=OK 按住连续翻页=OK 松手即停=OK，退出");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(0);
        }
        catch (Exception ex)
        {
            AppLog.Error("[SelfTest] r69: 自检失败", ex);
            SaveVisualPng(Content as Visual, "r69-failure.png");
            (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.TryShutdown(2);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        PersistWindowSize();
        // 退出时停止网络服务器服务（对话框本身关闭已不停服务——对齐 MobaXterm，
        // 服务由进程级 ServersSession 持有）；CronAutoStart 覆盖应用自启的实例。
        _ = ServersSession.Shared?.StopAllAsync();
        _ = CronAutoStart.StopAsync();
        _portTimer?.Stop();
        _portTimer = null;
        SizeChanged -= OnWindowSizeChanged;
        Sidebar.SessionActivated -= OnSessionActivated;
        Sidebar.EditRequested -= OnSessionEditRequested;
        Sidebar.RemoveRequested -= OnSessionRemoveRequested;
        Sidebar.NewSessionRequested -= OnSidebarNewSessionRequested;

        foreach (var state in _detachedTabs.Values.ToList())
        {
            state.SuppressReattach = true;
            state.Window.Close();
        }

        // 工具窗是独立顶层窗（无 Owner，任务栏靠同进程 AUMID 归组），
        // 关主窗不会自动带关，这里显式收尾，保证任何平台行为一致。
        foreach (var toolWindow in _toolWindows.Values.ToList())
        {
            toolWindow.Close();
        }
        _toolWindows.Clear();

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            foreach (var tab in viewModel.Tabs.ToList())
            {
                CloseTab(tab);
            }
        }

        base.OnClosed(e);
    }
}
