using System.Collections.ObjectModel;
using System.Collections.Specialized;
using COMTool.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMTool.App.ViewModels;

public enum SidebarTab
{
    Sessions,
    Tools,
    Macros,
    History
}

/// <summary>
/// Content of the Tools tab: the MobaXterm-style tool menu, or the network
/// scanner dialog after 网络扫描仪 is picked.
/// </summary>
public enum ToolsView
{
    Menu,
    Scanner
}

public sealed partial class MacroItem : ObservableObject
{
    public required string Id { get; init; }

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private bool _appendEnter = true;
}

public sealed class HistoryEntry
{
    public required string Kind { get; init; }
    public required string Id { get; init; }
    public required long At { get; init; }
    public required SessionItem Session { get; init; }
    public string Name => Session.Name;
    public string Detail => Session.Detail ?? "";
    public string IconGlyph => Session.IconGlyph;
    public string TimeText { get; init; } = "";
}

/// <summary>
/// One entry of the MobaXterm-style tool menu. Only 网络扫描仪 is wired up;
/// the remaining entries mirror MobaXterm's dropdown and are marked 暂不支持.
/// </summary>
public sealed class ToolMenuItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsEnabled { get; init; }
    public string ToolTip { get; init; } = "";
}

/// <summary>
/// One cell (host, port) of the scan result grid: green check when open,
/// gray cross when closed, matching MobaXterm's scanner result table.
/// </summary>
public sealed class PortCell
{
    public required string Label { get; init; }
    public required bool IsOpen { get; init; }
    public string Glyph => IsOpen ? "\uf00c" : "\uf00d";
    public string Foreground => IsOpen ? "#43a047" : "#9e9e9e";
}

/// <summary>
/// One row of the scan result grid: a probed host with one cell per scanned
/// port, plus optional hostname and latency.
/// </summary>
public sealed partial class ScanGridRow : ObservableObject
{
    public required string Address { get; init; }
    public string HostName { get; init; } = "";
    public string Latency { get; init; } = "";
    public required IReadOnlyList<PortCell> Cells { get; init; }
    public bool HasHostName => HostName.Length > 0;
    public bool HasLatency => Latency.Length > 0;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class MainViewModel
{
    [ObservableProperty]
    private SidebarTab _sidebarTab = SidebarTab.Sessions;

    [ObservableProperty]
    private ToolsView _toolsView = ToolsView.Menu;

    [ObservableProperty]
    private bool _isMacroEditorOpen;

    [ObservableProperty]
    private string _macroEditorName = "";

    [ObservableProperty]
    private string _macroEditorText = "";

    [ObservableProperty]
    private bool _macroEditorAppendEnter = true;

    [ObservableProperty]
    private string? _macroEditorError;

    [ObservableProperty]
    private string? _editingMacroId;

    [ObservableProperty]
    private string _scanStartIp = "";

    [ObservableProperty]
    private string _scanEndIp = "";

    [ObservableProperty]
    private string _scanPorts = "22,23";

    [ObservableProperty]
    private string _scanStatus = "";

    [ObservableProperty]
    private string? _scanError;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isDeepScanning;

    [ObservableProperty]
    private string _deepScanStatus = "";

    [ObservableProperty]
    private string _deepScanResult = "";

    [ObservableProperty]
    private string? _selectedScanAddress;

    [ObservableProperty]
    private List<string> _scanPortLabels = new();

    public ObservableCollection<MacroItem> Macros { get; } = new();
    public ObservableCollection<HistoryEntry> History { get; } = new();
    public ObservableCollection<ScanGridRow> ScanRows { get; } = new();
    public ObservableCollection<ToolMenuItem> ToolMenu { get; } = new();

    /// <summary>
    /// Wires up collection-changed notifications for the sidebar panels.
    /// Called once from the main constructor.
    /// </summary>
    public void InitializeSidebar()
    {
        Macros.CollectionChanged += OnSidebarCollectionChanged;
        History.CollectionChanged += OnSidebarCollectionChanged;
        ScanRows.CollectionChanged += OnSidebarCollectionChanged;
        InitToolMenu();
        UpdateSidebarEmptyStates();
    }

    private void InitToolMenu()
    {
        void Add(string id, string name, bool enabled, string tip) =>
            ToolMenu.Add(new ToolMenuItem { Id = id, Name = name, IsEnabled = enabled, ToolTip = tip });

        // Mirrors MobaXterm's "Tools" dropdown order; only the network
        // scanner is implemented so far, the rest stay 暂不支持.
        Add("net-server", "网络服务器", false, "暂不支持");
        Add("ssh-tunnel", "MobaSshTunnel (端口转发)", false, "暂不支持");
        Add("keygen", "MobaKeyGen (SSH 密钥生成器)", false, "暂不支持");
        Add("list-ports", "列出开放的网络端口", false, "暂不支持");
        Add("wol", "网络唤醒", false, "暂不支持");
        Add("scanner", "网络扫描仪", true, "扫描局域网在线设备与端口");
        Add("port-scanner", "端口扫描仪", false, "暂不支持");
        Add("packet-capture", "网络数据包捕获", false, "暂不支持");
    }

    partial void OnSidebarTabChanged(SidebarTab value)
    {
        OnPropertyChanged(nameof(IsSessionsTab));
        OnPropertyChanged(nameof(IsToolsTab));
        OnPropertyChanged(nameof(IsMacrosTab));
        OnPropertyChanged(nameof(IsHistoryTab));
        OnPropertyChanged(nameof(SidebarTitle));
    }

    partial void OnToolsViewChanged(ToolsView value)
    {
        OnPropertyChanged(nameof(IsToolsMenu));
        OnPropertyChanged(nameof(IsToolsScanner));
    }

    partial void OnSelectedScanAddressChanged(string? value)
    {
        foreach (var row in ScanRows)
        {
            row.IsSelected = row.Address == value;
        }

        OnPropertyChanged(nameof(IsDeepScanEnabled));
    }

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsDeepScanEnabled));
    }

    partial void OnIsDeepScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsDeepScanEnabled));
    }

    partial void OnScanPortLabelsChanged(List<string> value) =>
        OnPropertyChanged(nameof(HasScanPortLabels));

    public bool IsSessionsTab => SidebarTab == SidebarTab.Sessions;
    public bool IsToolsTab => SidebarTab == SidebarTab.Tools;
    public bool IsMacrosTab => SidebarTab == SidebarTab.Macros;
    public bool IsHistoryTab => SidebarTab == SidebarTab.History;
    public bool IsToolsMenu => ToolsView == ToolsView.Menu;
    public bool IsToolsScanner => ToolsView == ToolsView.Scanner;

    public string SidebarTitle => SidebarTab switch
    {
        SidebarTab.Tools => "Tools",
        SidebarTab.Macros => "Macros",
        SidebarTab.History => "History",
        _ => "User sessions",
    };

    public bool CanSendMacro => SelectedTab?.Terminal is not null;

    private void OnSidebarCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdateSidebarEmptyStates();

    private void UpdateSidebarEmptyStates()
    {
        OnPropertyChanged(nameof(HasMacros));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasScanRows));
    }

    public bool HasMacros => Macros.Count > 0;
    public bool HasHistory => History.Count > 0;
    public bool HasScanRows => ScanRows.Count > 0;
    public bool HasScanPortLabels => ScanPortLabels.Count > 0;
    public bool IsBusy => IsScanning || IsDeepScanning;
    public bool IsDeepScanEnabled => SelectedScanAddress is not null && !IsBusy;

    /// <summary>
    /// Opens the inline macro editor, optionally pre-filled for editing an
    /// existing macro.
    /// </summary>
    public void OpenMacroEditor(MacroItem? existing = null)
    {
        EditingMacroId = existing?.Id;
        MacroEditorName = existing?.Name ?? "";
        MacroEditorText = existing?.Text ?? "";
        MacroEditorAppendEnter = existing?.AppendEnter ?? true;
        MacroEditorError = null;
        IsMacroEditorOpen = true;
    }

    public void CloseMacroEditor()
    {
        IsMacroEditorOpen = false;
        EditingMacroId = null;
        MacroEditorError = null;
    }

    public MacroItem? FindMacro(string? id) =>
        id is null ? null : Macros.FirstOrDefault(m => m.Id == id);

    public static string FormatRelativeTime(long atMillis, long nowMillis)
    {
        long seconds = Math.Max(0, (nowMillis - atMillis) / 1000);
        if (seconds < 60) return "just now";
        if (seconds < 3600) return $"{seconds / 60} min ago";
        if (seconds < 86400) return $"{seconds / 3600} h ago";
        return $"{seconds / 86400} d ago";
    }
}
