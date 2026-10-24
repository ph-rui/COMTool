using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMTool.App.ViewModels.Tools;

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
/// service column (SSH/RDP/VNC/FTP/Telnet/Rlogin/HTTP), and hostname.
/// </summary>
public sealed class ScanGridRow
{
    public required string Address { get; init; }
    public string HostName { get; init; } = "";
    public required IReadOnlyList<PortCell> Cells { get; init; }
    public bool HasHostName => HostName.Length > 0;
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
