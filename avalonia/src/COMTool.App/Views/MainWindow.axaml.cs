using System.IO;
using System.Linq;
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

            if (!connected && tab.StatusMessage != "Connection lose")
            {
                tab.StatusMessage = "Connection lose";
                tab.StatusBrush = StatusWarningBrush;
                tab.TabDotBrush = StatusWarningBrush;
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
            state.NextReconnectAt = DateTime.UtcNow.AddSeconds(2);
            tab.IsAlive = false;
            tab.StatusMessage = $"Reconnecting {state.Port}...";
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
            terminalOptions.Theme = TerminalTheme.CreatePythonTheme();
        }

        terminal.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    ApplyTerminalTheme(terminal);
                    HideTerminalScrollBars(terminal);
                },
                DispatcherPriority.Loaded);
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
        // created. Re-apply the Python palette once the XTerm instance exists.
        var theme = TerminalTheme.CreatePythonTheme();
        if (terminal.Options is { } options)
        {
            options.Theme = theme;
        }

        terminal.Terminal.Colors.ApplyTheme(theme);
    }

    private static void HideTerminalScrollBars(TerminalControl terminal)
    {
        foreach (var descendant in terminal.GetVisualDescendants())
        {
            if (descendant is not ScrollBar scrollBar)
            {
                continue;
            }

            scrollBar.IsVisible = false;
            scrollBar.Width = 0;
            scrollBar.MinWidth = 0;
        }
    }

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

    private void OnSessionsToggle(object? sender, RoutedEventArgs e)
    {
        SetSidebarVisible(!_sidebarVisible);
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
    /// （附加时默认隐藏；显示宽度 14px）。</summary>
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
            bar.Width = currentlyHidden ? 14 : 0;
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
        public string? LastReconnectError { get; set; }

        public bool IsTransportOpen =>
            Connection is { IsOpen: true } && !Connection.Pty.IsInputCompleted;
    }

    private sealed record DetachedTabState(Window Window, int Index)
    {
        public bool SuppressReattach { get; set; }
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
