using Avalonia.Controls;
using Avalonia.Interactivity;
using COMTool.App.Tools;
using COMTool.App.ViewModels.Tools;
using COMTool.Connections.NetworkScan;

namespace COMTool.App.Views;

/// <summary>
/// Standalone 网络扫描窗口 (MobaXterm "Network scanner" dialog). Owns its scan
/// lifecycle: closing the window cancels a running scan.
/// </summary>
public partial class NetworkScanWindow : Window
{
    private CancellationTokenSource? _cts;
    private INetworkScanner? _scanner;

    public NetworkScanWindow()
    {
        InitializeComponent();
        DataContext = new NetworkScanWindowViewModel();
    }

    internal NetworkScanWindowViewModel ViewModel =>
        (NetworkScanWindowViewModel)DataContext!;

    /// <summary>Test seam: replaces the real scanner in headless tests.</summary>
    internal INetworkScanner? ScannerForTests { get; set; }

    private INetworkScanner Scanner => ScannerForTests ?? (_scanner ??= new NetworkScanner());

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }

    private void OnStartScanClick(object? sender, RoutedEventArgs e) => _ = StartScanAsync();

    private void OnStopScanClick(object? sender, RoutedEventArgs e) => StopScan();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    internal void StopScan() => _cts?.Cancel();

    /// <summary>
    /// 网络扫描仪 Start scan: expands the octet range (MobaXterm /24 form)
    /// and probes the fixed MobaXterm service set per host.
    /// </summary>
    internal async Task StartScanAsync()
    {
        if (_cts is not null)
        {
            return;
        }

        var vm = ViewModel;
        if (!NetworkScanTargets.TryParseSubnet(
                vm.Octet1, vm.Octet2, vm.Octet3, vm.OctetFrom, vm.OctetTo,
                out var targets, out var targetError))
        {
            vm.Error = targetError;
            return;
        }

        var ports = NetworkServices.ServicePorts;

        vm.Error = null;
        vm.Rows.Clear();
        vm.Status = $"扫描中… 0/{targets.Count}";
        vm.IsScanning = true;
        _cts = new CancellationTokenSource();
        var cts = _cts;

        var progress = new UiProgress<NetworkScanProgress>(p =>
        {
            // 停止后忽略已排队的进度，避免“已停止”被迟到的“完成”覆盖
            if (!ReferenceEquals(_cts, cts) || cts.IsCancellationRequested)
            {
                return;
            }

            if (p.Host is { } host)
            {
                vm.Rows.Add(ToScanGridRow(host));
            }

            if (p.Completed >= p.Total)
            {
                // 最终进度上报时写终止状态，保证与增量更新顺序一致
                vm.IsScanning = false;
                vm.Status = $"完成：发现 {vm.Rows.Count} 台在线设备";
            }
            else
            {
                vm.Status = $"扫描中… {p.Completed}/{p.Total}";
            }
        });

        try
        {
            await Scanner.ScanAsync(targets, ports, progress, cts.Token);
            // 兜底：若扫描器未上报最终计数
            if (vm.IsScanning)
            {
                vm.IsScanning = false;
                vm.Status = $"完成：发现 {vm.Rows.Count} 台在线设备";
            }
        }
        catch (OperationCanceledException)
        {
            vm.IsScanning = false;
            vm.Status = $"已停止（已发现 {vm.Rows.Count} 台）";
        }
        catch (Exception ex)
        {
            vm.IsScanning = false;
            vm.Error = ex.Message;
            vm.Status = "";
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
            }
        }
    }

    internal static ScanGridRow ToScanGridRow(NetworkScanHost host)
    {
        var openPorts = new HashSet<int>(host.OpenPorts);
        return new ScanGridRow
        {
            Address = host.Address.ToString(),
            HostName = host.HostName ?? "",
            Cells = NetworkServices.ServiceColumns
                .Select(c => new PortCell { Label = c.Name, IsOpen = openPorts.Contains(c.Port) })
                .ToList(),
        };
    }
}
