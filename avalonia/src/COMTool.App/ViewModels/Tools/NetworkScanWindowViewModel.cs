using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using COMTool.Connections.NetworkScan;

namespace COMTool.App.ViewModels.Tools;

/// <summary>
/// One cell of the 网络扫描仪 result grid (host × service). Three states:
/// pending (probe in flight, gray "…" ellipsis), open (green check), closed
/// (gray cross) - matching MobaXterm's incremental scanner result table.
/// </summary>
public sealed partial class PortCell : ObservableObject
{
    public required string Label { get; init; }
    public required int Port { get; init; }

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>探测未完成：显示 "…" 而不是 √/×（2026-10-29 用户要求增量显示）。</summary>
    [ObservableProperty]
    private bool _isPending;

    public string Glyph => IsPending ? "\uf141" : (IsOpen ? "\uf00c" : "\uf00d");
    public string Foreground => IsPending ? "#9e9e9e" : (IsOpen ? "#43a047" : "#9e9e9e");

    partial void OnIsOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(Foreground));
    }

    partial void OnIsPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(Foreground));
    }
}

/// <summary>
/// One row of the 网络扫描仪 result grid: a probed host, one cell per fixed
/// service column (SSH/RDP/VNC/FTP/Telnet/Rlogin/HTTP), and hostname.
/// Inserted on discovery (name empty, pending cells), then updated in place
/// when the probe completes (name filled, cells finalised).
/// </summary>
public sealed partial class ScanGridRow : ObservableObject
{
    public required string Address { get; init; }

    [ObservableProperty]
    private string _hostName = "";

    public required IReadOnlyList<PortCell> Cells { get; init; }
    public bool HasHostName => HostName.Length > 0;

    partial void OnHostNameChanged(string value) => OnPropertyChanged(nameof(HasHostName));

    /// <summary>完成阶段：原位更新行（名称回填 + 各服务列定格）。</summary>
    public void UpdateFrom(NetworkScanHost host)
    {
        HostName = host.HostName ?? "";
        var openPorts = new HashSet<int>(host.OpenPorts);
        foreach (var cell in Cells)
        {
            cell.IsOpen = openPorts.Contains(cell.Port);
            cell.IsPending = false;
        }
    }
}

/// <summary>
/// State of the standalone 网络扫描 window: MobaXterm's "IP 地址范围" form
/// (three fixed octets plus a last-octet range) and the result grid.
/// </summary>
public sealed partial class NetworkScanWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _octet1 = "192";

    [ObservableProperty]
    private string _octet2 = "168";

    [ObservableProperty]
    private string _octet3 = "1";

    [ObservableProperty]
    private string _octetFrom = "1";

    [ObservableProperty]
    private string _octetTo = "254";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isScanning;

    public ObservableCollection<ScanGridRow> Rows { get; } = new();

    public NetworkScanWindowViewModel()
    {
        Rows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(IsEmpty));
        };
    }

    public bool HasRows => Rows.Count > 0;

    public bool IsEmpty => Rows.Count == 0;
}
