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
        SessionKind.Ssh => "\uf233",
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
    public required string Title { get; init; }
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

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isAlive = true;

    [ObservableProperty]
    private bool _isSessionStopped;

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

    public ObservableCollection<SidebarSection> Sections { get; } = new();

    public ObservableCollection<TerminalTab> Tabs { get; } = new();

    public bool HasSessions { get; private set; }

    public bool HasTabs => Tabs.Count > 0;

    public bool IsSidebarVisible { get; set; } = true;

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
            OnPropertyChanged(nameof(CanSendMacro));
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
        var serial = new SidebarSection { Title = "Serial", ShowHeader = true };
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
    }

    public void LoadSessions(
        AppConfigService config,
        IReadOnlySet<string> connectedSerial,
        IReadOnlySet<string> connectedSsh,
        IReadOnlySet<string> connectedTcp,
        IReadOnlySet<string>? connectedTelnet = null)
    {
        Sections.Clear();

        var serial = new SidebarSection { Title = "Serial", ShowHeader = true };
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
