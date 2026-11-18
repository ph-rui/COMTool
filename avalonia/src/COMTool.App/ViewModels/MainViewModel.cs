using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media;
using COMTool.App.Services;
using COMTool.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using Iciclecreek.Terminal;

namespace COMTool.App.ViewModels;

public enum SessionKind
{
    Local,
    Serial,
    Ssh,
    Telnet,
    Tcp,
}

public sealed partial class SessionItem : ObservableObject
{
    public required string Name { get; init; }
    public required string SessionId { get; init; }
    public required SessionKind Kind { get; init; }
    public string? Detail { get; init; }
    public string? SerialSettings { get; init; }

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    private bool _isConnecting;

    public bool IsSsh => Kind == SessionKind.Ssh;

    public string IconGlyph => Kind switch
    {
        SessionKind.Ssh => "\uf084",
        SessionKind.Telnet => "\uf120",
        SessionKind.Tcp => "\uf0ac",
        _ => "\uf1e6",
    };

    public IBrush IconBrush => IsConnecting
        ? new SolidColorBrush(Color.Parse("#1565c0"))
        : IsConnected
            ? new SolidColorBrush(Color.Parse("#43a047"))
            : new SolidColorBrush(Color.Parse("#90a4ae"));

    /// <summary>
    /// MobaXterm-style single-line session title: "COM117 (USB-Enhanced-SERIAL-A
    /// CH344 (COM117))". Falls back to the bare name when the detail is empty
    /// or just repeats it.
    /// </summary>
    public string RowTitle
    {
        get
        {
            if (Detail is null || Detail.Length == 0
                || string.Equals(Detail, Name, StringComparison.OrdinalIgnoreCase))
            {
                return Name;
            }

            return $"{Name} ({Detail})";
        }
    }

    partial void OnIsConnectedChanged(bool value) =>
        OnPropertyChanged(nameof(IconBrush));
}

public sealed class SidebarSection
{
    public required string Title { get; init; }
    public bool ShowHeader { get; init; }
    public ObservableCollection<SessionItem> Items { get; } = new();
}

public sealed partial class TerminalTab : ObservableObject
{
    private string _title = "";

    /// <summary>标签标题。r39「设置终端标题」可运行时改写（头部 {Binding Title} 自动跟随）。</summary>
    public required string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public required string SessionId { get; init; }
    public required SessionKind Kind { get; init; }
    public required Control Content { get; init; }
    public IDisposable? Connection { get; set; }
    public Action? CloseAction { get; set; }
    public Func<bool>? IsTransportOpen { get; set; }

    /// <summary>
    /// The terminal of the tab. The content of an SSH tab is the session
    /// view that holds the terminal and the SFTP drawer, so the terminal
    /// is named here as well.
    /// </summary>
    public TerminalControl? Terminal { get; init; }

    /// <summary>
    /// The SFTP drawer of an SSH tab. It is null for every other kind of
    /// tab.
    /// </summary>
    public SftpPaneViewModel? SftpPane { get; init; }

    /// <summary>
    /// True for a tab of an SSH session.
    /// </summary>
    public bool IsSshTab => Kind == SessionKind.Ssh;

    /// <summary>
    /// True for a tab of a serial session; the tab context menu offers
    /// "Reconnect now" for these (r67.6).
    /// </summary>
    public bool IsSerialTab => Kind == SessionKind.Serial;

    /// <summary>
    /// The session-type glyph shown at the head of the tab, so a serial and an SSH tab are
    /// distinguishable at a glance instead of both showing a generic status dot. MobaXterm tags each
    /// tab with a type-specific icon (plug for serial, key for SSH); ours mirrors that. The glyphs and
    /// colours match the "new session" menu and the sidebar, so the icon a session picks at creation is
    /// the same one on its tab. FontAwesome 6 Free Solid (see IconFontAwesome).
    /// </summary>
    public string KindIconGlyph => Kind switch
    {
        SessionKind.Ssh => "\uf084",      // key (MobaXterm-style gold key)
        SessionKind.Serial => "\uf1e6",   // plug
        SessionKind.Telnet => "\uf120",   // terminal
        SessionKind.Tcp => "\uf0ac",      // globe
        _ => "\uf108",                    // desktop (Local)
    };

    /// <summary>
    /// Brush for the kind icon. MobaXterm parity (r48): the icon keeps its type colour in
    /// EVERY state, selected or not — MobaXterm's selected tab only changes its background,
    /// the key stays gold and the plug stays green. r44's white-when-selected was reverted
    /// on user feedback (a white icon lost the type-at-a-glance signal the icon exists for).
    /// </summary>
    public IBrush KindIconBrush => Kind switch
    {
        SessionKind.Ssh => TabIconSshBrush,
        SessionKind.Serial => TabIconSerialBrush,
        SessionKind.Telnet => TabIconTelnetBrush,
        SessionKind.Tcp => TabIconTcpBrush,
        _ => TabIconLocalBrush, // Local
    };

    // Warm gold for the SSH key, matching MobaXterm's; the rest reuse the new-session menu palette.
    private static readonly IBrush TabIconSshBrush = new SolidColorBrush(Color.Parse("#f0b429"));
    private static readonly IBrush TabIconSerialBrush = new SolidColorBrush(Color.Parse("#43a047"));
    private static readonly IBrush TabIconTelnetBrush = new SolidColorBrush(Color.Parse("#00897b"));
    private static readonly IBrush TabIconTcpBrush = new SolidColorBrush(Color.Parse("#f57c00"));
    private static readonly IBrush TabIconLocalBrush = new SolidColorBrush(Color.Parse("#607d8b"));

    /// <summary>
    /// 只读终端（MobaXterm「只读终端」）：运行时开关，不持久化。
    /// 拦截键盘输入（tunnel KeyDown）与粘贴（PasteNormalizedAsync 守卫）。
    /// 宏执行/Zmodem 等显式用户操作不受限。
    /// </summary>
    public bool IsReadOnlyTerminal { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isAlive = true;

    [ObservableProperty]
    private bool _isSessionStopped;

    /// <summary>
    /// 远程监控开关（MobaXterm SFTP 面板左下角"远程监控"按钮的 per-tab
    /// 运行态）。会话停止时保留，R 重启 attach 后自动恢复轮询；
    /// 不随进程持久化。
    /// </summary>
    [ObservableProperty]
    private bool _monitorEnabled;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private IBrush _statusBrush = new SolidColorBrush(Color.Parse("#008200"));

    [ObservableProperty]
    private IBrush _tabDotBrush = new SolidColorBrush(Color.Parse("#4caf50"));

    [ObservableProperty]
    private bool _isZmodemActive;

    [ObservableProperty]
    private bool _isZmodemIndeterminate;

    [ObservableProperty]
    private bool _isZmodemCancellable = true;

    [ObservableProperty]
    private string _zmodemLabel = "";

    [ObservableProperty]
    private double _zmodemProgress;

    /// <summary>
    /// True while a stopped session is being restarted with R. The status
    /// row shows its progress area with an indeterminate bar and the label
    /// "Restarting...".
    /// </summary>
    [ObservableProperty]
    private bool _isRestarting;

    /// <summary>
    /// True while the status row should show its progress area: either a
    /// ZMODEM transfer or an in-flight session restart.
    /// </summary>
    public bool ShowStatusProgress => IsZmodemActive || IsRestarting;

    /// <summary>
    /// The label next to the progress area. ZMODEM keeps its own label; a
    /// restart just says it is restarting.
    /// </summary>
    public string StatusProgressLabel => IsZmodemActive ? ZmodemLabel : "Restarting...";

    /// <summary>
    /// A restart cannot report a percentage, so its bar animates until the
    /// attempt succeeds or fails.
    /// </summary>
    public bool StatusProgressIndeterminate =>
        IsRestarting || (IsZmodemActive && IsZmodemIndeterminate);

    /// <summary>
    /// The percentage text only makes sense for a ZMODEM transfer with a
    /// determinate bar.
    /// </summary>
    public bool ShowProgressTextVisible => IsZmodemActive && !IsZmodemIndeterminate;

    partial void OnIsRestartingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStatusProgress));
        OnPropertyChanged(nameof(StatusProgressLabel));
        OnPropertyChanged(nameof(StatusProgressIndeterminate));
        OnPropertyChanged(nameof(ShowProgressTextVisible));
    }

    partial void OnIsZmodemActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStatusProgress));
        OnPropertyChanged(nameof(StatusProgressLabel));
        OnPropertyChanged(nameof(StatusProgressIndeterminate));
        OnPropertyChanged(nameof(ShowProgressTextVisible));
    }

    partial void OnIsZmodemIndeterminateChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusProgressIndeterminate));
        OnPropertyChanged(nameof(ShowProgressTextVisible));
    }

    partial void OnZmodemLabelChanged(string value) =>
        OnPropertyChanged(nameof(StatusProgressLabel));

    public bool IsSessionTab => Kind is SessionKind.Serial or SessionKind.Ssh or SessionKind.Telnet or SessionKind.Tcp;
}

public partial class MainViewModel : ViewModelBase
{
    private TerminalTab? _selectedTab;

    // Session ids of the live SSH logins from the last UpdateConnectionStates
    // call. The rail globe is shown only while the selected tab belongs to one
    // of them (MobaXterm hides the SSH browser on serial/telnet/TCP tabs).
    private IReadOnlySet<string> _connectedSshSessions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<SidebarSection> Sections { get; } = new();

    public ObservableCollection<TerminalTab> Tabs { get; } = new();

    public bool HasSessions { get; private set; }

    public bool HasTabs => Tabs.Count > 0;

    private bool _isSidebarVisible = true;

    /// <summary>
    /// Whether the sidebar panel is expanded (the 40px rail is always on
    /// screen). Kept in sync with the view's _sidebarVisible via
    /// ApplySidebarMode; the rail icons only show their active highlight
    /// while the panel is open on the matching tab.
    /// </summary>
    public bool IsSidebarVisible
    {
        get => _isSidebarVisible;
        set
        {
            if (_isSidebarVisible == value)
            {
                return;
            }

            _isSidebarVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RailSessionsActive));
            OnPropertyChanged(nameof(RailToolsActive));
            OnPropertyChanged(nameof(RailMacrosActive));
            OnPropertyChanged(nameof(RailSshBrowserActive));
        }
    }

    public event Action<TerminalTab?>? SelectedTabChanging;

    public TerminalTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value))
            {
                return;
            }

            // The content host detaches the previous terminal when this changes.
            // Let the view preserve its live connection before that happens.
            SelectedTabChanging?.Invoke(_selectedTab);

            var previousPane = _selectedTab?.SftpPane;

            if (_selectedTab is not null)
            {
                _selectedTab.IsSelected = false;
            }

            _selectedTab = value;
            if (_selectedTab is not null)
            {
                _selectedTab.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SshBrowserPane));
            OnPropertyChanged(nameof(CanExecuteMacro));
            OnPropertyChanged(nameof(CanToggleRecording));

            // The SSH-browser rail icon belongs to the selected tab, so a
            // switch to a serial/telnet/TCP tab hides it (MobaXterm behavior).
            RefreshSshBrowserVisibility();

            if (previousPane is not null && !ReferenceEquals(previousPane, _selectedTab?.SftpPane))
            {
                previousPane.IsExpanded = false;
            }

            if (SidebarTab == SidebarTab.SshBrowser)
            {
                if (_selectedTab?.SftpPane is { } pane)
                {
                    pane.IsExpanded = true;
                    _ = pane.EnsureLoadedAsync(CancellationToken.None);
                }
                else
                {
                    // A serial or local tab has no remote side, so the
                    // rail goes back to the sessions panel.
                    SidebarTab = SidebarTab.Sessions;
                }
            }

            // Remote monitoring follows the selected tab: stop the old tab's
            // poller and resume the new one if its per-tab flag is on.
            MonitorOnSelectedTabChanged();
        }
    }

    public MainViewModel()
    {
        InitializeSidebar();
        var demo = Environment.GetCommandLineArgs().Contains("--demo");
        if (!demo)
        {
            return;
        }

        LoadDemoSessions();
    }

    public void LoadDemoSessions()
    {
        Sections.Clear();
        var serial = new SidebarSection { Title = "串口", ShowHeader = true };
        serial.Items.Add(new SessionItem
        {
            Name = "COM99",
            SessionId = "COM99",
            Kind = SessionKind.Serial,
            Detail = "USB-SERIAL CH340",
            SerialSettings = "115200 8N1",
            IsConnected = true,
        });
        serial.Items.Add(new SessionItem
        {
            Name = "COM3",
            SessionId = "COM3",
            Kind = SessionKind.Serial,
            Detail = "Silicon Labs CP210x",
            SerialSettings = "9600 8N1",
        });

        var ssh = new SidebarSection { Title = "SSH", ShowHeader = true };
        ssh.Items.Add(new SessionItem
        {
            Name = "lab",
            SessionId = "root@192.168.1.10:22",
            Kind = SessionKind.Ssh,
            Detail = "root@192.168.1.10:22",
            IsConnected = true,
        });

        Sections.Add(serial);
        Sections.Add(ssh);
        HasSessions = true;
        OnPropertyChanged(nameof(HasSessions));
    }

    public void UpdateConnectionStates(
        IReadOnlySet<string> connectedSerial,
        IReadOnlySet<string> connectedSsh,
        IReadOnlySet<string> connectedTcp,
        IReadOnlySet<string>? connectedTelnet = null)
    {
        foreach (var item in Sections.SelectMany(section => section.Items))
        {
            item.IsConnected = item.Kind switch
            {
                SessionKind.Serial => connectedSerial.Contains(item.SessionId),
                SessionKind.Ssh => connectedSsh.Contains(item.SessionId),
                SessionKind.Telnet => connectedTelnet?.Contains(item.SessionId) == true,
                SessionKind.Tcp => connectedTcp.Contains(item.SessionId),
                _ => false,
            };
        }

        // The rail browser icon follows live connections, including quick
        // connections that have no saved session behind them, and only shows
        // while the selected tab is one of those SSH sessions.
        _connectedSshSessions = connectedSsh;
        RefreshSshBrowserVisibility();
    }

    /// <summary>
    /// Shows the SSH-browser rail icon only while the selected tab is a live
    /// SSH session that owns an SFTP pane. MobaXterm ties the browser to the
    /// current session, so switching to a COM tab hides the globe even though
    /// the SSH login itself stays connected; switching back brings it up.
    /// </summary>
    private void RefreshSshBrowserVisibility()
    {
        var selected = _selectedTab;
        IsSshBrowserVisible = selected?.SftpPane is not null
            && _connectedSshSessions.Contains(selected.SessionId);
        LeaveBrowserWhenNoSshIsLeft();
    }

    /// <summary>
    /// The browser cannot stay open after the last SSH login is gone.
    /// </summary>
    private void LeaveBrowserWhenNoSshIsLeft()
    {
        if (!IsSshBrowserVisible && SidebarTab == SidebarTab.SshBrowser)
        {
            SidebarTab = SidebarTab.Sessions;
        }
    }

    public void LoadSessions(
        AppConfigService config,
        IReadOnlySet<string> connectedSerial,
        IReadOnlySet<string> connectedSsh,
        IReadOnlySet<string> connectedTcp,
        IReadOnlySet<string>? connectedTelnet = null)
    {
        Sections.Clear();
        _connectedSshSessions = connectedSsh;
        RefreshSshBrowserVisibility();

        var serial = new SidebarSection { Title = "串口", ShowHeader = true };
        var availableSerial = config.PortScanSucceeded
            ? config.PortMap.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        foreach (var session in config.SerialSessions.SortedSessions())
        {
            if (availableSerial is not null && !availableSerial.Contains(session.Port))
            {
                continue;
            }

            var detail = session.Name;
            if (string.IsNullOrWhiteSpace(detail)
                || string.Equals(detail, session.Port, StringComparison.OrdinalIgnoreCase))
            {
                detail = config.PortMap.TryGetValue(session.Port, out var port)
                    ? port.Description
                    : "";
            }

            serial.Items.Add(new SessionItem
            {
                Name = session.Port,
                SessionId = session.Port,
                Kind = SessionKind.Serial,
                Detail = string.IsNullOrWhiteSpace(detail) ? null : detail,
                SerialSettings = SerialSessionRules.ConnectionSummary(session.Serial),
                IsConnected = connectedSerial.Contains(session.Port),
            });
        }

        var ssh = new SidebarSection { Title = "SSH", ShowHeader = true };
        foreach (var session in config.SshSessions.SortedSessions())
        {
            ssh.Items.Add(new SessionItem
            {
                Name = SshSessionRules.DisplayName(session),
                SessionId = session.Key,
                Kind = SessionKind.Ssh,
                Detail = SshSessionRules.Detail(session),
                IsConnected = connectedSsh.Contains(session.Key),
            });
        }

        var telnet = new SidebarSection { Title = "Telnet", ShowHeader = true };
        foreach (var session in config.TelnetSessions.SortedSessions())
        {
            telnet.Items.Add(new SessionItem
            {
                Name = TelnetSessionRules.DisplayName(session),
                SessionId = session.Key,
                Kind = SessionKind.Telnet,
                Detail = TelnetSessionRules.Detail(session),
                IsConnected = connectedTelnet?.Contains(session.Key) == true,
            });
        }

        var tcp = new SidebarSection { Title = "TCP", ShowHeader = true };
        foreach (var session in config.TcpSessions.SortedSessions())
        {
            tcp.Items.Add(new SessionItem
            {
                Name = TcpSessionRules.DisplayName(session),
                SessionId = session.Key,
                Kind = SessionKind.Tcp,
                Detail = TcpSessionRules.Detail(session),
                IsConnected = connectedTcp.Contains(session.Key),
            });
        }

        if (serial.Items.Count > 0)
        {
            Sections.Add(serial);
        }

        if (ssh.Items.Count > 0)
        {
            Sections.Add(ssh);
        }

        if (telnet.Items.Count > 0)
        {
            Sections.Add(telnet);
        }

        if (tcp.Items.Count > 0)
        {
            Sections.Add(tcp);
        }

        HasSessions = Sections.Count > 0;
        OnPropertyChanged(nameof(HasSessions));
    }

    public void AddTab(TerminalTab tab)
    {
        Tabs.Add(tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(HasTabs));
    }

    public TerminalTab? FindTab(string sessionId, SessionKind kind) =>
        Tabs.FirstOrDefault(tab =>
            tab.Kind == kind
            && string.Equals(tab.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

    public void CloseTab(TerminalTab tab)
    {
        if (RemoveTab(tab) >= 0)
        {
            OnPropertyChanged(nameof(HasTabs));
        }
    }

    /// <summary>
    /// Removes a tab from the main tab strip without closing its connection.
    /// The returned index is used to reinsert it when a detached window closes.
    /// </summary>
    public int RemoveTabForDetach(TerminalTab tab)
    {
        var index = RemoveTab(tab);
        if (index >= 0)
        {
            OnPropertyChanged(nameof(HasTabs));
        }

        return index;
    }

    public void InsertDetachedTab(TerminalTab tab, int index)
    {
        index = Math.Clamp(index, 0, Tabs.Count);
        Tabs.Insert(index, tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(HasTabs));
    }

    /// <summary>
    /// r67：拖动标签换位。targetIndex 是目标位置（已按完整列表索引语义给出），
    /// 越界自动夹紧；当前位置不变时不做任何操作。选中态保持不变。
    /// </summary>
    public void MoveTab(TerminalTab tab, int targetIndex)
    {
        var from = Tabs.IndexOf(tab);
        if (from < 0)
        {
            return;
        }

        var to = Math.Clamp(targetIndex, 0, Tabs.Count - 1);
        if (to == from)
        {
            return;
        }

        Tabs.Move(from, to);
    }

    private int RemoveTab(TerminalTab tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return -1;
        }

        var wasSelected = ReferenceEquals(SelectedTab, tab);
        Tabs.RemoveAt(index);

        if (wasSelected)
        {
            SelectedTab = Tabs.Count switch
            {
                0 => null,
                _ => Tabs[Math.Min(index, Tabs.Count - 1)],
            };
        }

        return index;
    }
}
