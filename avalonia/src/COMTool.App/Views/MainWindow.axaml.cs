using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
    private bool _applyingEncoding;
    private AppConfigService? _config;
    private DispatcherTimer? _portTimer;
    private bool _windowPointerPressed;
    // 录制去重：KeyDown 路径刚喂过的字符+时刻（防 Win32 WM_CHAR 回声把同一字符双录）
    private char _lastKeyFeedChar = '\0';
    private int _lastKeyFeedTick;
    private ConnectionSettingsWindow? _settingsWindow;
    private readonly Dictionary<TerminalTab, ZmodemTiming> _zmodemTimings = new();
    private readonly Dictionary<TerminalTab, ZmodemStream> _zmodemStreams = new();
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

    public MainWindow()
    {
        InitializeComponent();
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
                // "COMTool 启动时启动 Cron 服务器" 勾选时随主窗口自启
                _ = CronAutoStart.EnsureAsync(_config.Config.Servers.Cron);
                ThemeManager.Apply(_config.Config.Skin);
                ApplyEncodingConfig();
                ApplySidebarConfig();
                ApplyWindowSizeConfig();
                RefreshSidebar();
                LoadSidebarData();
                _portTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _portTimer.Tick += (_, _) => RefreshPorts();
                _portTimer.Start();
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to load config", ex);
                _ = ShowErrorAsync($"Failed to load config: {ex.Message}");
            }
        }

        if (Environment.GetCommandLineArgs().Contains("--terminal"))
        {
            AddLocalTerminal();
        }
    }

    private void RefreshPorts()
    {
        if (_config is null)
        {
            return;
        }

        var previous = _config.Ports
            .Select(port => port.Device)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ApplyPortScan(previous, _config.RefreshPorts());
    }

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
        catch (Exception ex)
        {
            item.IsConnecting = false;
            var action = item.Kind == SessionKind.Serial
                || (item.Kind == SessionKind.Tcp && IsTcpServerSession(item.SessionId))
                ? "Open Failed"
                : "Connect Failed";
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
            StatusMessage = $"Connected {SshSessionRules.Detail(session)}",
        };
        AttachSshTransport(tab, terminal, connection);

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
                var accepted = await dialog.ShowDialog<bool>(owner);
                if (!accepted)
                {
                    return null;
                }

                password = dialog.Password;
                remember = dialog.Remember;
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
        });
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
                if (terminal is not null)
                {
                    RestoreStoppedTerminalFocus(tab, terminal);
                }

                return;
            }

            if (!_sshRestarts.Contains(tab) || !IsTabOpen(tab) || terminal is null)
            {
                connection.Dispose();
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

    private void AttachTerminal(
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
            terminal.AttachConnection(pty);
        }
        catch (Exception ex)
        {
            tab.IsAlive = false;
            _ = ShowErrorAsync($"Failed to attach terminal: {ex.Message}");
        }
    }

    // MobaXterm's terminal font is Consolas 10pt. Chinese has no Consolas
    // glyphs, so the family list falls back to Microsoft YaHei UI (present on
    // every Windows install) and finally to the bundled Noto Sans Mono CJK SC,
    // which guarantees CJK coverage on Linux and in headless tests.
    internal static FontFamily TerminalFontFamily() => new FontFamily(
        "Consolas," +
        "Microsoft YaHei UI," +
        "avares://COMTool.App/Assets/Fonts/NotoSansMonoCJKsc-Regular.otf#Noto Sans Mono CJK SC," +
        "avares://COMTool.App/Assets/Fonts/NotoSansMonoCJKsc-Bold.otf#Noto Sans Mono CJK SC");

    // MobaXterm's 10pt Consolas is ~13.3px at 96 DPI.
    internal const double TerminalFontSize = 13.5;

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

    private TerminalControl CreateTerminal()
    {
        var terminal = new TerminalControl
        {
            // The control defaults Process to bash/cmd and auto-launches it when
            // loaded. These tabs always attach an external serial/SSH stream, so
            // an empty Process stops a local shell from starting on reparent and
            // writing its own prompt into the terminal buffer.
            Process = string.Empty,
            FontFamily = TerminalFontFamily(),
            FontSize = TerminalFontSize,
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
        terminal.PointerPressed += OnTerminalPointerPressed;
        terminal.KeyDown += OnTerminalKeyDown;
        // The terminal's own double-click handling may mark the event handled
        // for word selection. Observe it anyway so the whole terminal pane
        // mirrors the title bar and maximize button.
        terminal.AddHandler(
            InputElement.DoubleTappedEvent,
            OnTerminalDoubleTapped,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
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
                _ = ShowErrorAsync($"Failed to start terminal: {ex.Message}");
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

            _ = ShowErrorAsync($"Failed to detach tab: {ex.Message}");
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
        var dialog = new Window
        {
            Title = "Error",
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

    private void OnTerminalDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TerminalControl)
        {
            return;
        }

        ToggleMaximize();
        e.Handled = true;
    }

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
        var fullScreen = WindowState == WindowState.FullScreen;
        WindowState = fullScreen ? WindowState.Normal : WindowState.FullScreen;
        TitleBar.IsVisible = fullScreen;
        RootGrid.RowDefinitions[0].Height = new GridLength(fullScreen ? 35 : 0);
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
            Height = 430,
            MinWidth = 420,
            MinHeight = 320,
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

    private void ApplyEncodingConfig()
    {
        if (_config is null)
        {
            return;
        }

        var selected = 1;
        for (var index = 0; index < EncodingCombo.Items.Count; index++)
        {
            if (EncodingCombo.Items[index] is ComboBoxItem item
                && string.Equals(
                    item.Content?.ToString(),
                    _config.Config.Encoding,
                    StringComparison.OrdinalIgnoreCase))
            {
                selected = index;
                break;
            }
        }

        _applyingEncoding = true;
        try
        {
            EncodingCombo.SelectedIndex = selected;
        }
        finally
        {
            _applyingEncoding = false;
        }
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

    private void OnEncodingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingEncoding
            || _config is null
            || sender is not ComboBox combo
            || combo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var encoding = item.Content?.ToString();
        if (string.IsNullOrWhiteSpace(encoding))
        {
            return;
        }

        _config.Config.Encoding = encoding;
        _config.Save();
    }

    private void OnEncodingDropDownClosed(object? sender, EventArgs e)
    {
        // Let the ComboBox finish closing before moving focus back. Otherwise
        // the popup's focus restoration can immediately steal it again.
        RestoreTerminalFocus();
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
        var browser = (DataContext as MainViewModel)?.IsSshBrowserTab == true;

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

    // Ctrl + mouse wheel resizes the font of the terminal under the pointer,
    // like MobaXterm. The change is per-terminal and not persisted, so a
    // restart returns to TerminalFontSize.
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

        terminal.FontSize = AdjustTerminalFontSize(terminal.FontSize, e.Delta.Y);
        e.Handled = true;
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

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _windowPointerPressed = true;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || WindowState != WindowState.Normal)
        {
            return;
        }

        // Window buttons at the very edge must still receive their click.
        if (e.Source is Control source
            && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
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
        Width = width;
        Height = height;
        _lastNormalWidth = width;
        _lastNormalHeight = height;
    }

    private static double NormalizeWindowSize(int value, double fallback, double minimum) =>
        value >= minimum && value <= 10000 ? value : fallback;

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
            var config = _config.Config.Window;
            if (config is null)
            {
                config = new WindowConfig();
                _config.Config.Window = config;
            }

            if (config.Width == width && config.Height == height)
            {
                return;
            }

            config.Width = width;
            config.Height = height;
            _config.Save();
            AppLog.Info($"Window size saved: {width}x{height}");
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
        var dialog = new Window
        {
            Title = "警告",
            Width = 460,
            Height = 200,
            MinWidth = 340,
            MinHeight = 150,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var yes = new Button
        {
            Name = "MacroExportWarnYesButton",
            Content = "是(Y)",
            Width = 80,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        yes.Click += (_, _) =>
        {
            dialog.Tag = true;
            dialog.Close();
        };
        var no = new Button
        {
            Name = "MacroExportWarnNoButton",
            Content = "否(N)",
            Width = 80,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        no.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "警告：宏将在不加密的情况下导出。"
                         + "如果宏包含机密信息，这可能是危险的。"
                         + "您确定要继续吗？",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { yes, no },
                },
            },
        };
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
        var dialog = new Window
        {
            Title = "COMTool",
            Width = 460,
            Height = 190,
            MinWidth = 340,
            MinHeight = 150,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button
        {
            Name = "MacroInfoOkButton",
            Content = "确定",
            Width = 96,
            Height = 30,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
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
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(ok);
        var grid = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { icon, content },
        };
        Grid.SetColumn(content, 1);
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
    /// "请为此宏输入一个名称" — MobaXterm 版式：左侧铅笔图标、右侧标签+输入框
    /// （默认名预填并选中）、底部居中 ✓确定/✗取消。OK 返回去空格后的名称，取消/空返回 null。
    /// </summary>
    internal async Task<string?> ShowMacroNameAsync(string defaultName)
    {
        var dialog = new Window
        {
            Title = "COMTool",
            Width = 460,
            Height = 200,
            MinWidth = 360,
            MinHeight = 160,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
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

    /// <summary>是/否 delete confirmation. Returns true on "是".</summary>
    internal async Task<bool> ShowMacroDeleteConfirmAsync(string name)
    {
        var dialog = new Window
        {
            Title = "删除宏",
            Width = 420,
            Height = 170,
            MinWidth = 320,
            MinHeight = 130,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var yes = new Button
        {
            Name = "MacroDeleteYesButton",
            Content = "是",
            Width = 80,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        yes.Click += (_, _) =>
        {
            dialog.Tag = true;
            dialog.Close();
        };
        var no = new Button
        {
            Name = "MacroDeleteNoButton",
            Content = "否",
            Width = 80,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        no.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = $"确定删除宏 \"{name}\" 吗？",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { yes, no },
                },
            },
        };
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
    /// Updates the red strip above the terminal: recording (Stop) or
    /// executing (Cancel); hidden when neither is active.
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
        else if (viewModel.IsExecutingMacro)
        {
            MacroBannerText.Text = $"正在执行宏: {viewModel.ExecutingMacroName}";
            MacroBannerActionButton.Content = "取消";
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
        else if (viewModel.IsExecutingMacro)
        {
            viewModel.CancelMacroExecution();
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

    private async void OnTerminalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TerminalControl terminal)
        {
            return;
        }

        var point = e.GetCurrentPoint(terminal);
        if (!point.Properties.IsRightButtonPressed)
        {
            return;
        }

        e.Handled = true;
        if (terminal.Terminal.Selection.HasSelection)
        {
            await terminal.CopyAsync();
            terminal.Terminal.Selection.ClearSelection();
            return;
        }

        await terminal.PasteAsync();
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
        await terminal.PasteAsync();
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
