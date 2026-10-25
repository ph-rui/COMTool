using Avalonia.Controls;
using Avalonia.Interactivity;
using COMTool.App.Services;
using COMTool.App.Tools;
using COMTool.App.ViewModels.Tools;
using COMTool.Connections.NetworkScan;

namespace COMTool.App.Views;

/// <summary>
/// Standalone 端口扫描窗口. MobaXterm opens it with the "网络扫描" title; it
/// owns its scan lifecycle, cancels on close and remembers the entered IP
/// and port range.
/// </summary>
public partial class PortScanWindow : Window
{
    private readonly AppConfigService? _config;
    private CancellationTokenSource? _cts;
    private INetworkScanner? _scanner;

    public PortScanWindow()
        : this(null)
    {
    }

    public PortScanWindow(AppConfigService? config)
    {
        InitializeComponent();
        _config = config;
        DataContext = new PortScanWindowViewModel();
        RestoreScanSettings();
    }

    internal PortScanWindowViewModel ViewModel =>
        (PortScanWindowViewModel)DataContext!;

    /// <summary>Test seam: replaces the real scanner in headless tests.</summary>
    internal INetworkScanner? ScannerForTests { get; set; }

    private INetworkScanner Scanner => ScannerForTests ?? (_scanner ??= new NetworkScanner());

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        RememberScanSettings();
        base.OnClosed(e);
    }

    /// <summary>
    /// Restores the last used target IP and port range (MobaXterm remembers
    /// them between invocations). Invalid/absent values keep the defaults.
    /// </summary>
    private void RestoreScanSettings()
    {
        var saved = _config?.Config.PortScan;
        if (saved is null || !NetworkScanTargets.TryParseIp(saved.Ip, out _, out _))
        {
            return;
        }

        var vm = ViewModel;
        vm.Ip = saved.Ip;
        if (int.TryParse(saved.From, out var from)
            && int.TryParse(saved.To, out var to)
            && from is >= 1 and <= 65535
            && to is >= 1 and <= 65535
            && from <= to)
        {
            vm.From = saved.From;
            vm.To = saved.To;
        }
    }

    /// <summary>
    /// Persists the entered IP (and port range, when valid) so the dialog
    /// restores it on the next open. Called on Start scan and on close, so a
    /// value the user typed but never scanned is still remembered. Invalid
    /// inputs never overwrite the last good value.
    /// </summary>
    private void RememberScanSettings()
    {
        var config = _config;
        if (config is null)
        {
            return;
        }

        var vm = ViewModel;
        if (!NetworkScanTargets.TryParseIp(vm.Ip, out _, out _))
        {
            return;
        }

        var saved = config.Config.PortScan;
        var fromValid = int.TryParse(vm.From, out var from) && from is >= 1 and <= 65535;
        var toValid = int.TryParse(vm.To, out var to)
                      && to is >= 1 and <= 65535
                      && (fromValid ? to >= from : true);

        var newFrom = fromValid && toValid ? vm.From : saved.From;
        var newTo = fromValid && toValid ? vm.To : saved.To;
        if (saved.Ip == vm.Ip && saved.From == newFrom && saved.To == newTo)
        {
            return;
        }

        saved.Ip = vm.Ip;
        saved.From = newFrom;
        saved.To = newTo;
        config.SaveConfig();
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

        RememberScanSettings();

        var total = vm.RangeMode ? to - from + 1 : NetworkServices.KnownPortNumbers.Length;
        vm.Error = null;
        vm.Result = "";
        vm.Status = $"正在扫描 {host}：0/{total}";
        vm.IsScanning = true;
        _cts = new CancellationTokenSource();
        var cts = _cts;

        var progress = new UiProgress<int>(done =>
        {
            // Ignore updates queued before the scan finished: they would
            // otherwise overwrite the final status after the completion.
            if (!ReferenceEquals(_cts, cts))
            {
                return;
            }

            vm.Status = $"正在扫描 {host}：{Math.Min(done, total)}/{total}";
        });

        try
        {
            IReadOnlyList<int> openPorts = vm.RangeMode
                ? await Scanner.ScanHostPortsAsync(host, from, to, progress, cts.Token)
                : await Scanner.ScanHostPortsAsync(
                    host, NetworkServices.KnownPortNumbers, progress, cts.Token);
            vm.Result = FormatPortResults(openPorts);
            vm.Status = openPorts.Count == 0
                ? "扫描完成：未发现开放端口"
                : $"扫描完成：发现 {openPorts.Count} 个开放端口";
        }
        catch (OperationCanceledException)
        {
            vm.Result = "扫描已停止";
            vm.Status = "扫描已停止";
        }
        catch (Exception ex)
        {
            vm.Result = "";
            vm.Error = ex.Message;
            vm.Status = "";
        }
        finally
        {
            vm.IsScanning = false;
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
