using CommunityToolkit.Mvvm.ComponentModel;

namespace COMTool.App.ViewModels.Tools;

/// <summary>
/// State of the standalone 端口扫描 window (MobaXterm opens it with the same
/// "网络扫描" title): a single IP, either the fixed known-port table or an
/// explicit from/to range, and the textual result list.
/// </summary>
public sealed partial class PortScanWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _ip = "127.0.0.1";

    [ObservableProperty]
    private bool _rangeMode;

    [ObservableProperty]
    private string _from = "1";

    [ObservableProperty]
    private string _to = "65535";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _result = "";

    partial void OnRangeModeChanged(bool value) => OnPropertyChanged(nameof(IsKnownMode));

    public bool IsKnownMode => !RangeMode;
}
