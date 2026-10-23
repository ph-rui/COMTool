using Avalonia.Controls;
using Avalonia.Interactivity;
using COMTool.App.Tools;
using COMTool.App.ViewModels.Tools;
using COMTool.Connections.NetworkScan;

namespace COMTool.App.Views;

/// <summary>
/// Standalone 端口扫描窗口. MobaXterm opens it with the "网络扫描" title; it
/// owns its scan lifecycle and cancels on close.
/// </summary>
public partial class PortScanWindow : Window
{
    private CancellationTokenSource? _cts;
    private INetworkScanner? _scanner;

    public PortScanWindow()
    {
        InitializeComponent();
        DataContext = new PortScanWindowViewModel();
    }

    internal PortScanWindowViewModel ViewModel =>
        (PortScanWindowViewModel)DataContext!;

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
    /// 端口扫描仪 Start scan: known-ports mode probes the fixed service table;
    /// range mode probes every port in [from..to] (1..65535 = MobaXterm deep
    /// scan). Results are MobaXterm-style lines: "- Port #22 (ssh): listening".
    /// </summary>
    internal async Task StartScanAsync()
    {
        if (_cts is not null)
        {
            return;
        }

        var vm = ViewModel;
        if (!NetworkScanTargets.TryParseIp(vm.Ip, out var host, out var ipError))
        {
            vm.Error = ipError;
            return;
        }

        int from = 0;
        int to = 0;
        if (vm.RangeMode)
        {
            if (!int.TryParse(vm.From, out from) || from is < 1 or > 65535)
            {
                vm.Error = "起始端口无效（须为 1~65535 的整数）";
                return;
            }
            if (!int.TryParse(vm.To, out to) || to is < 1 or > 65535)
            {
                vm.Error = "结束端口无效（须为 1~65535 的整数）";
                return;
            }
            if (from > to)
            {
                vm.Error = "起始端口不能大于结束端口";
                return;
            }
        }

        var total = vm.RangeMode ? to - from + 1 : NetworkServices.KnownPortNumbers.Length;
        vm.Error = null;
        vm.Result = "";
        vm.Status = $"正在扫描 {host}：0/{total}";
        vm.IsScanning = true;
        _cts = new CancellationTokenSource();
        var cts = _cts;

        var progress = new UiProgress<int>(done =>
        {
            vm.Status = $"正在扫描 {host}：{Math.Min(done, total)}/{total}";
        });

        try
        {
            IReadOnlyList<int> openPorts = vm.RangeMode
                ? await Scanner.ScanHostPortsAsync(host, from, to, progress, cts.Token)
                : await Scanner.ScanHostPortsAsync(
                    host, NetworkServices.KnownPortNumbers, progress, cts.Token);
            vm.Result = FormatPortResults(openPorts);
        }
        catch (OperationCanceledException)
        {
            vm.Result = "扫描已停止";
        }
        catch (Exception ex)
        {
            vm.Result = "";
            vm.Error = ex.Message;
        }
        finally
        {
            vm.IsScanning = false;
            vm.Status = "";
            cts.Dispose();
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
            }
        }
    }

    internal static string FormatPortResults(IReadOnlyList<int> openPorts)
    {
        if (openPorts.Count == 0)
        {
            return "未发现开放端口";
        }

        var sb = new System.Text.StringBuilder();
        foreach (var port in openPorts)
        {
            sb.AppendLine(NetworkServices.FormatPortLine(port));
        }
        return sb.ToString().TrimEnd();
    }
}
