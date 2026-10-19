using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media;
using COMTool.App.Services;
using COMTool.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMTool.App.ViewModels;

public enum SessionKind
{
    Local,
    Serial,
    Ssh,
}

public sealed partial class SessionItem : ObservableObject
{
    public required string Name { get; init; }
    public required string SessionId { get; init; }
    public required SessionKind Kind { get; init; }
    public string? Detail { get; init; }

    [ObservableProperty]
    private bool _isConnected;

    public bool IsSsh => Kind == SessionKind.Ssh;

    public string IconGlyph => IsSsh ? "\uf233" : "\uf1e6";

    public IBrush IconBrush => IsConnected
        ? new SolidColorBrush(Color.Parse("#43a047"))
        : new SolidColorBrush(Color.Parse("#90a4ae"));

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
    public IDisposable? Connection { get; init; }
    public Action? CloseAction { get; init; }
    public Func<bool>? IsTransportOpen { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isAlive = true;

    public bool IsSessionTab => Kind is SessionKind.Serial or SessionKind.Ssh;
}

public partial class MainViewModel : ViewModelBase
{
    private TerminalTab? _selectedTab;

    public ObservableCollection<SidebarSection> Sections { get; } = new();

    public ObservableCollection<TerminalTab> Tabs { get; } = new();

    public bool HasSessions { get; private set; }

    public bool HasTabs => Tabs.Count > 0;

    public bool IsSidebarVisible { get; set; } = true;

    public TerminalTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value))
            {
                return;
            }

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
        }
    }

    public MainViewModel()
    {
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
            IsConnected = true,
        });
        serial.Items.Add(new SessionItem
        {
            Name = "COM3",
            SessionId = "COM3",
            Kind = SessionKind.Serial,
            Detail = "Silicon Labs CP210x",
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
        IReadOnlySet<string> connectedSsh)
    {
        foreach (var item in Sections.SelectMany(section => section.Items))
        {
            item.IsConnected = item.Kind == SessionKind.Serial
                ? connectedSerial.Contains(item.SessionId)
                : connectedSsh.Contains(item.SessionId);
        }
    }

    public void LoadSessions(
        AppConfigService config,
        IReadOnlySet<string> connectedSerial,
        IReadOnlySet<string> connectedSsh)
    {
        Sections.Clear();

        var serial = new SidebarSection { Title = "Serial", ShowHeader = true };
        foreach (var session in config.SerialSessions.SortedSessions())
        {
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

        if (serial.Items.Count > 0)
        {
            Sections.Add(serial);
        }

        if (ssh.Items.Count > 0)
        {
            Sections.Add(ssh);
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
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        var wasSelected = ReferenceEquals(SelectedTab, tab);
        Tabs.RemoveAt(index);
        OnPropertyChanged(nameof(HasTabs));

        if (!wasSelected)
        {
            return;
        }

        SelectedTab = Tabs.Count switch
        {
            0 => null,
            _ => Tabs[Math.Min(index, Tabs.Count - 1)],
        };
    }
}
