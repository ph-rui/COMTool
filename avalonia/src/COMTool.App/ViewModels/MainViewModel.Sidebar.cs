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
/// Content of the Tools tab: the MobaXterm-style tool menu, the 网络扫描仪
/// dialog, or the 端口扫描仪 dialog.
/// </summary>
public enum ToolsView
{
    Menu,
    NetworkScanner,
    PortScanner
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
/// One entry of the MobaXterm-style tool menu. Headers (系统/文本/网络) mark
/// the group rows; item rows carry the MobaXterm icon glyph + color.
/// 网络扫描仪 and 端口扫描仪 are wired up; the remaining entries mirror
/// MobaXterm's dropdown and are marked 暂不支持.
/// </summary>
public sealed class ToolMenuItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsEnabled { get; init; }
    public string ToolTip { get; init; } = "";

    /// <summary>True for group header rows (系统/文本/网络).</summary>
    public bool IsHeader { get; init; }

    /// <summary>FontAwesome 6 solid glyph (e.g. "\uf0ac"); empty for headers.</summary>
    public string Glyph { get; init; } = "";

    /// <summary>Icon color (hex), matching MobaXterm's colored menu icons.</summary>
    public string GlyphColor { get; init; } = "";
}

/// <summary>
/// One cell of the 网络扫描仪 result grid (host × service): green check when
/// open, gray cross when closed, matching MobaXterm's scanner result table.
/// </summary>
public sealed class PortCell
{
    public required string Label { get; init; }
    public required bool IsOpen { get; init; }
    public string Glyph => IsOpen ? "\uf00c" : "\uf00d";
    public string Foreground => IsOpen ? "#43a047" : "#9e9e9e";
}

/// <summary>
/// One row of the 网络扫描仪 result grid: a probed host, one cell per fixed
/// service column (SSH/RDP/VNC/FTP/Telnet/Rlogin/HTTP), hostname and the
/// extra open ports (MobaXterm "其他端口" column).
/// </summary>
public sealed class ScanGridRow
{
    public required string Address { get; init; }
    public string HostName { get; init; } = "";
    public required IReadOnlyList<PortCell> Cells { get; init; }
    public string OtherPorts { get; init; } = "";
    public bool HasHostName => HostName.Length > 0;
    public bool HasOtherPorts => OtherPorts.Length > 0;
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

    // ---- 网络扫描仪 (MobaXterm "IP 地址范围" five-segment form: three fixed
    //      octets + last-octet range; default 192.168.1.1 -> 254) ----
    [ObservableProperty]
    private string _scanOctet1 = "192";

    [ObservableProperty]
    private string _scanOctet2 = "168";

    [ObservableProperty]
    private string _scanOctet3 = "1";

    [ObservableProperty]
    private string _scanOctetFrom = "1";

    [ObservableProperty]
    private string _scanOctetTo = "254";

    [ObservableProperty]
    private string _scanStatus = "";

    [ObservableProperty]
    private string? _scanError;

    [ObservableProperty]
    private bool _isScanning;

    // ---- 端口扫描仪 (MobaXterm "IP 地址" + known-ports / port-range modes) ----
    [ObservableProperty]
    private string _portScanIp = "";

    [ObservableProperty]
    private bool _portScanRangeMode;

    [ObservableProperty]
    private string _portScanFrom = "1";

    [ObservableProperty]
    private string _portScanTo = "65535";

    [ObservableProperty]
    private string _portScanStatus = "";

    [ObservableProperty]
    private string? _portScanError;

    [ObservableProperty]
    private bool _isPortScanning;

    [ObservableProperty]
    private string _portScanResult = "";

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
        void Header(string name) =>
            ToolMenu.Add(new ToolMenuItem { Id = "header-" + name, Name = name, IsHeader = true });

        void Add(string id, string name, bool enabled, string tip, string glyph, string color) =>
            ToolMenu.Add(new ToolMenuItem
            {
                Id = id,
                Name = name,
                IsEnabled = enabled,
                ToolTip = tip,
                Glyph = glyph,
                GlyphColor = color,
            });

        // 1:1 with the MobaXterm "工具" dropdown (measured on MobaXterm
        // Professional v22.1): three groups 系统/文本/网络, 19 entries in
        // dropdown order with their colored icons. Only 网络扫描仪 and
        // 端口扫描仪 are implemented; the rest stay 暂不支持.
        Header("系统");
        Add("mobapkg", "MobaPkg 包管理器 (实验性)", false, "暂不支持", "\uf06b", "#1565c0");
        Add("x11-dwm", "带 Dwm 的 X11 标签页", false, "暂不支持", "\uf2d0", "#1976d2");
        Add("x11-fvwm", "带 Fvwm 的 X11 窗口", false, "暂不支持", "\uf2d0", "#1976d2");
        Add("x11-twm", "带 Twm 的 X11 窗口", false, "暂不支持", "\uf2d0", "#1976d2");
        Add("list-hw", "列出硬件设备", false, "暂不支持", "\uf108", "#43a047");
        Add("list-procs", "列出运行中的进程", false, "暂不支持", "\uf70c", "#1976d2");
        Add("cmd-admin", "命令提示符 (管理员)", false, "暂不支持", "\uf121", "#616161");
        Add("powershell-admin", "Windows Powershell (管理员)", false, "暂不支持", "\uf121", "#1565c0");

        Header("文本");
        Add("text-editor", "MobaTextEditor", false, "暂不支持", "\uf303", "#fb8c00");
        Add("moba-diff", "MobaDiff", false, "暂不支持", "\uf15b", "#1976d2");
        Add("ascii-table", "Ascii table", false, "暂不支持", "\uf031", "#1976d2");

        Header("网络");
        Add("net-server", "网络服务器", false, "暂不支持", "\uf013", "#00897b");
        Add("ssh-tunnel", "MobaSSHtunnel (端口转发)", false, "暂不支持", "\uf121", "#43a047");
        Add("keygen", "MobaKeyGen (SSH 密钥生成器)", false, "暂不支持", "\uf084", "#f9a825");
        Add("list-ports", "列出开放的网络端口", false, "暂不支持", "\uf0ac", "#1976d2");
        Add("wol", "网络唤醒", false, "暂不支持", "\uf0f3", "#1976d2");
        Add("scanner", "网络扫描仪", true, "扫描局域网在线设备与端口", "\uf7c0", "#5e35b1");
        Add("port-scanner", "端口扫描仪", true, "扫描指定 IP 的开放端口", "\uf1eb", "#1976d2");
        Add("packet-capture", "网络数据包捕获", false, "暂不支持", "\uf0e7", "#7b1fa2");
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
        OnPropertyChanged(nameof(IsNetworkScannerView));
        OnPropertyChanged(nameof(IsPortScannerView));
        OnPropertyChanged(nameof(IsToolsSubView));
    }

    partial void OnIsScanningChanged(bool value) =>
        OnPropertyChanged(nameof(IsBusy));

    partial void OnIsPortScanningChanged(bool value) =>
        OnPropertyChanged(nameof(IsBusy));

    partial void OnPortScanRangeModeChanged(bool value) =>
        OnPropertyChanged(nameof(IsPortScanKnownMode));

    partial void OnPortScanResultChanged(string value) =>
        OnPropertyChanged(nameof(HasPortScanResult));

    public bool IsSessionsTab => SidebarTab == SidebarTab.Sessions;
    public bool IsToolsTab => SidebarTab == SidebarTab.Tools;
    public bool IsMacrosTab => SidebarTab == SidebarTab.Macros;
    public bool IsHistoryTab => SidebarTab == SidebarTab.History;
    public bool IsToolsMenu => ToolsView == ToolsView.Menu;
    public bool IsNetworkScannerView => ToolsView == ToolsView.NetworkScanner;
    public bool IsPortScannerView => ToolsView == ToolsView.PortScanner;
    public bool IsToolsSubView => ToolsView != ToolsView.Menu;
    public bool IsPortScanKnownMode => !PortScanRangeMode;

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
    public bool HasPortScanResult => PortScanResult.Length > 0;
    public bool IsBusy => IsScanning || IsPortScanning;

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
