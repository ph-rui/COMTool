using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using COMTool.App.Services;
using COMTool.App.Tools;
using COMTool.App.ViewModels.Tools;
using COMTool.Connections.NetworkScan;

namespace COMTool.App.Views;

/// <summary>
/// Standalone 网络扫描窗口 (MobaXterm "Network scanner" dialog). Owns its scan
/// lifecycle: closing the window cancels a running scan and remembers the
/// entered IP range.
/// </summary>
public partial class NetworkScanWindow : Window
{
    private readonly AppConfigService? _config;
    private CancellationTokenSource? _cts;
    private INetworkScanner? _scanner;

    public NetworkScanWindow()
        : this(null)
    {
    }

    public NetworkScanWindow(AppConfigService? config)
    {
        InitializeComponent();
        _config = config;
        DataContext = new NetworkScanWindowViewModel();
        RestoreScanRange();
    }

    internal NetworkScanWindowViewModel ViewModel =>
        (NetworkScanWindowViewModel)DataContext!;

    /// <summary>Test seam: replaces the real scanner in headless tests.</summary>
    internal INetworkScanner? ScannerForTests { get; set; }

    private INetworkScanner Scanner => ScannerForTests ?? (_scanner ??= new NetworkScanner());

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        RememberScanRange();
        base.OnClosed(e);
    }

    /// <summary>
    /// Restores the last used "IP 地址范围" (MobaXterm remembers it between
    /// invocations). Invalid/absent values keep the built-in defaults.
    /// </summary>
    private void RestoreScanRange()
    {
        var saved = _config?.Config.NetworkScan;
        if (saved is null
            || !NetworkScanTargets.TryParseSubnet(
                saved.Octet1, saved.Octet2, saved.Octet3, saved.From, saved.To,
                out _, out _))
        {
            return;
        }

        var vm = ViewModel;
        vm.Octet1 = saved.Octet1;
        vm.Octet2 = saved.Octet2;
        vm.Octet3 = saved.Octet3;
        vm.OctetFrom = saved.From;
        vm.OctetTo = saved.To;
    }

    /// <summary>
    /// Persists the entered "IP 地址范围" when it is valid. Called on Start
    /// scan and on close, so a range the user typed but never scanned is also
    /// remembered.
    /// </summary>
    private void RememberScanRange()
    {
        var config = _config;
        if (config is null)
        {
            return;
        }

        var vm = ViewModel;
        if (!NetworkScanTargets.TryParseSubnet(
                vm.Octet1, vm.Octet2, vm.Octet3, vm.OctetFrom, vm.OctetTo,
                out _, out _))
        {
            return;
        }

        var saved = config.Config.NetworkScan;
        if (saved.Octet1 == vm.Octet1
            && saved.Octet2 == vm.Octet2
            && saved.Octet3 == vm.Octet3
            && saved.From == vm.OctetFrom
            && saved.To == vm.OctetTo)
        {
            return;
        }

        saved.Octet1 = vm.Octet1;
        saved.Octet2 = vm.Octet2;
        saved.Octet3 = vm.Octet3;
        saved.From = vm.OctetFrom;
        saved.To = vm.OctetTo;
        config.SaveConfig();
    }

    /// <summary>
    /// MobaXterm-style octet entry: once a focused box contains a complete,
    /// valid octet (three digits, 0–255), move to the next box. Invalid values
    /// such as 999 stay in place so the existing validation reports them.
    /// </summary>
    private void OnOctetTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box || !box.IsFocused)
        {
            return;
        }

        if (box.Text is not { Length: 3 } text
            || !int.TryParse(text, out var value)
            || value is < 0 or > 255)
        {
            return;
        }

        var next = NextOctetBox(box);
        if (next is null)
        {
            return;
        }

        next.Focus();
        next.SelectAll();
    }

    private void OnOctetGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.SelectAll();
        }
    }

    private TextBox? NextOctetBox(TextBox box) => box.Name switch
    {
        "ScanOctet1Box" => this.FindControl<TextBox>("ScanOctet2Box"),
        "ScanOctet2Box" => this.FindControl<TextBox>("ScanOctet3Box"),
        "ScanOctet3Box" => this.FindControl<TextBox>("ScanOctetFromBox"),
        "ScanOctetFromBox" => this.FindControl<TextBox>("ScanOctetToBox"),
        _ => null,
    };

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
        RememberScanRange();

        vm.Error = null;
        vm.Rows.Clear();
        vm.Status = $"扫描中… 0/{targets.Count}";
        vm.IsScanning = true;
        _cts = new CancellationTokenSource();
        var cts = _cts;

        var discovered = new UiProgress<NetworkScanHost>(h =>
        {
            // 停止后忽略已排队的上报，避免“已停止”被迟到的上报覆盖
            if (!ReferenceEquals(_cts, cts) || cts.IsCancellationRequested)
            {
                return;
            }

            // 发现阶段：立即插行（名称空、已确认开放列✓、未确认列“…”，
            // MobaXterm 式增量显示，2026-10-29 用户要求）
            vm.Rows.Add(ToScanGridRow(h, false));
        });

        var progress = new UiProgress<NetworkScanProgress>(p =>
        {
            // 停止后忽略已排队的进度，避免“已停止”被迟到的“完成”覆盖
            if (!ReferenceEquals(_cts, cts) || cts.IsCancellationRequested)
            {
                return;
            }

            if (p.Host is { } host)
            {
                // 完成阶段：按 Address 原位更新（名称回填 + 列定格）；
                // 兜底：该行不存在（注入的扫描器未上报 discovered）则直接补行
                var row = vm.Rows.FirstOrDefault(r => r.Address == host.Address.ToString());
                if (row is not null)
                {
                    row.UpdateFrom(host);
                }
                else
                {
                    vm.Rows.Add(ToScanGridRow(host, true));
                }
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
            await Scanner.ScanAsync(targets, ports, progress, discovered, cts.Token);
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

    /// <summary>
    /// finalize=false（发现阶段）：名称留空，已确认开放列 ✓、未确认列“…”(IsPending)；
    /// finalize=true（完成阶段/兜底补行）：名称回填、各列定格。
    /// </summary>
    internal static ScanGridRow ToScanGridRow(NetworkScanHost host, bool finalize)
    {
        var openPorts = new HashSet<int>(host.OpenPorts);
        return new ScanGridRow
        {
            Address = host.Address.ToString(),
            HostName = finalize ? (host.HostName ?? "") : "",
            Cells = NetworkServices.ServiceColumns
                .Select(c =>
                {
                    bool isOpen = openPorts.Contains(c.Port);
                    return new PortCell
                    {
                        Label = c.Name,
                        Port = c.Port,
                        IsOpen = isOpen,
                        IsPending = !finalize && !isOpen,
                    };
                })
                .ToList(),
        };
    }
}
