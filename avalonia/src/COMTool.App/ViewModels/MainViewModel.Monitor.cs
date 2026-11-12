using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.Connections.RemoteMonitoring;
using COMTool.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMTool.App.ViewModels;

/// <summary>
/// 状态栏右键菜单的 7 项勾选态，与 MobaXterm 远程监控菜单一一对应，
/// 默认全部开启（主机/CPU/RAM/网络上传/网络下载/正常运行时间/分区信息）。
/// 持久化映射到 <see cref="RemoteMonitorConfig"/>；写 config.json 由视图层负责。
/// </summary>
public sealed partial class MonitorMenuState : ObservableObject
{
    [ObservableProperty] private bool _showHost = true;
    [ObservableProperty] private bool _showCpu = true;
    [ObservableProperty] private bool _showRam = true;
    [ObservableProperty] private bool _showNetUp = true;
    [ObservableProperty] private bool _showNetDown = true;
    [ObservableProperty] private bool _showUptime = true;
    [ObservableProperty] private bool _showPartitions = true;

    public void LoadFrom(RemoteMonitorConfig cfg)
    {
        ShowHost = cfg.ShowHost;
        ShowCpu = cfg.ShowCpu;
        ShowRam = cfg.ShowRam;
        ShowNetUp = cfg.ShowNetUp;
        ShowNetDown = cfg.ShowNetDown;
        ShowUptime = cfg.ShowUptime;
        ShowPartitions = cfg.ShowPartitions;
    }

    public void SaveTo(RemoteMonitorConfig cfg)
    {
        cfg.ShowHost = ShowHost;
        cfg.ShowCpu = ShowCpu;
        cfg.ShowRam = ShowRam;
        cfg.ShowNetUp = ShowNetUp;
        cfg.ShowNetDown = ShowNetDown;
        cfg.ShowUptime = ShowUptime;
        cfg.ShowPartitions = ShowPartitions;
    }

    public MonitorOptions ToOptions() => new MonitorOptions
    {
        ShowHost = ShowHost,
        ShowCpu = ShowCpu,
        ShowRam = ShowRam,
        ShowNetUp = ShowNetUp,
        ShowNetDown = ShowNetDown,
        ShowUptime = ShowUptime,
        ShowPartitions = ShowPartitions,
    };

    /// <summary>菜单 Tag -> 勾选态（Opening 同步用）。</summary>
    public bool IsOption(string tag) => tag switch
    {
        "host" => ShowHost,
        "cpu" => ShowCpu,
        "ram" => ShowRam,
        "netup" => ShowNetUp,
        "netdown" => ShowNetDown,
        "uptime" => ShowUptime,
        "partitions" => ShowPartitions,
        _ => false,
    };

    public void SetOption(string tag, bool value)
    {
        switch (tag)
        {
            case "host": ShowHost = value; break;
            case "cpu": ShowCpu = value; break;
            case "ram": ShowRam = value; break;
            case "netup": ShowNetUp = value; break;
            case "netdown": ShowNetDown = value; break;
            case "uptime": ShowUptime = value; break;
            case "partitions": ShowPartitions = value; break;
        }
    }
}

/// <summary>
/// 状态栏分区段（MobaXterm 风格：每分区一个盒子，第一个带磁盘图标，
/// 使用率 ≥80% 橙红粗体警示）。
/// </summary>
public sealed partial class PartitionSegment : ObservableObject
{
    [ObservableProperty]
    private string _text = "";

    /// <summary>使用率 ≥80%，橙红粗斜体显示。</summary>
    [ObservableProperty]
    private bool _isHot;

    /// <summary>第一个分区段显示磁盘图标（与 MobaXterm 一致）。</summary>
    [ObservableProperty]
    private bool _showIcon = true;
}

/// <summary>
/// CPU 迷你条的一列（MobaXterm 的 CPU 条是示波器 roll 模式：
/// 横轴时间、纵轴 0~100%，新样本从右端进入、旧样本向左滚动）。
/// 列高按自身百分比，颜色按自身负载分档（低绿 / 中高橙 / ≥80% 红）。
/// </summary>
public sealed partial class CpuBarColumn : ObservableObject
{
    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private double _height;

    [ObservableProperty]
    private IBrush _brush = new SolidColorBrush(MainViewModel.CpuBarColor(0));

    partial void OnPercentChanged(double value)
    {
        Height = MainViewModel.CpuBarMaxHeight * value / 100.0;
        Brush = new SolidColorBrush(MainViewModel.CpuBarColor(value));
    }
}

public partial class MainViewModel
{
    private RemoteMonitor? _remoteMonitor;
    private TerminalTab? _monitoredTab;
    private MonitorSnapshot? _lastSnapshot;

    /// <summary>
    /// 状态栏分段（MobaXterm 风格：每指标一个带图标的小盒子）。
    /// 每个指标一组 {可见性, 文本}，分区信息是可变数量的盒子。
    /// </summary>
    [ObservableProperty]
    private bool _segHostVisible;
    [ObservableProperty]
    private string _segHostText = "";
    [ObservableProperty]
    private bool _segCpuVisible;
    [ObservableProperty]
    private string _segCpuText = "";
    [ObservableProperty]
    private bool _segRamVisible;
    [ObservableProperty]
    private string _segRamText = "";
    [ObservableProperty]
    private bool _segNetUpVisible;
    [ObservableProperty]
    private string _segNetUpText = "";
    [ObservableProperty]
    private bool _segNetDownVisible;
    [ObservableProperty]
    private string _segNetDownText = "";
    [ObservableProperty]
    private bool _segUptimeVisible;
    [ObservableProperty]
    private string _segUptimeText = "";
    [ObservableProperty]
    private bool _segPartitionsVisible;
    [ObservableProperty]
    private ObservableCollection<PartitionSegment> _partitions = new();

    /// <summary>分段 CPU 条最大宽度（px），与 XAML 中 CPU 条轨道宽度一致。</summary>
    internal const double CpuBarMaxWidth = 56;

    /// <summary>CPU 条列数（横轴时间窗口：28 列 × 2s 轮询 ≈ 56 秒历史）。</summary>
    internal const int CpuBarColumnCount = 28;

    /// <summary>单列宽度（px）：56 / 28 = 2。</summary>
    internal const double CpuBarColumnWidth = CpuBarMaxWidth / CpuBarColumnCount;

    /// <summary>单列最大高度（px），与 XAML 中 CPU 条轨道高度一致。</summary>
    internal const double CpuBarMaxHeight = 18;

    /// <summary>
    /// CPU 条每列的颜色，对齐 MobaXterm（低绿 / 中高橙 / ≥80% 红）。
    /// 只返回线程无关的 <see cref="Color"/>；画刷由列对象在 UI 线程创建
    /// （Avalonia 12 的 SolidColorBrush 线程亲和，跨线程访问会抛异常）。
    /// </summary>
    internal static Color CpuBarColor(double cpuPercent)
    {
        var pct = (int)Math.Clamp(Math.Round(cpuPercent), 0, 100);
        return pct >= 80 ? CpuBarRedColor : pct >= 50 ? CpuBarOrangeColor : CpuBarGreenColor;
    }

    private static readonly Color CpuBarGreenColor = Color.Parse("#2e9e4f");
    private static readonly Color CpuBarOrangeColor = Color.Parse("#e8a31a");
    private static readonly Color CpuBarRedColor = Color.Parse("#d84a2f");

    /// <summary>
    /// CPU 迷你条的时间序列列（MobaXterm 的 roll 模式）：固定 28 列，
    /// 最右一列是最新样本，每来一个采样整体左移。
    /// </summary>
    public ObservableCollection<CpuBarColumn> CpuColumns { get; } = CreateCpuColumns();

    private static ObservableCollection<CpuBarColumn> CreateCpuColumns()
    {
        var columns = new ObservableCollection<CpuBarColumn>();
        for (var i = 0; i < CpuBarColumnCount; i++)
        {
            columns.Add(new CpuBarColumn());
        }

        return columns;
    }

    /// <summary>
    /// 追加一个 CPU 采样（示波器 roll：左移一列，新值进最右列）。
    /// 只由轮询快照路径调用；菜单勾选重排版不推进历史。
    /// </summary>
    internal void PushCpuSample(double cpuPercent)
    {
        var pct = Math.Clamp(cpuPercent, 0, 100);
        for (var i = 0; i < CpuColumns.Count - 1; i++)
        {
            CpuColumns[i].Percent = CpuColumns[i + 1].Percent;
        }

        CpuColumns[^1].Percent = pct;
    }

    private void ClearCpuHistory()
    {
        foreach (var column in CpuColumns)
        {
            column.Percent = 0;
        }
    }

    /// <summary>
    /// 远程监控菜单 7 项的勾选态（状态栏右键菜单的数据源）。
    /// </summary>
    public MonitorMenuState MonitorMenu { get; } = new MonitorMenuState();

    /// <summary>
    /// 状态栏当前显示的监控行文本；空串 = 未在监控。
    /// </summary>
    [ObservableProperty]
    private string _monitorText = "";

    /// <summary>
    /// 状态栏是否显示监控行（true 时隐藏常规状态消息）。
    /// </summary>
    [ObservableProperty]
    private bool _isMonitorActive;

    /// <summary>
    /// 配置加载后由视图调用，把持久化的菜单勾选态灌进来。
    /// </summary>
    public void LoadMonitorOptions(RemoteMonitorConfig cfg) => MonitorMenu.LoadFrom(cfg);

    /// <summary>
    /// SFTP 面板"远程监控"按钮被用户切换。非 SSH 标签忽略；
    /// 开且当前标签激活时立即开始轮询，关且正是该标签在轮询时停止。
    /// </summary>
    public void SetMonitorEnabled(TerminalTab tab, bool enabled)
    {
        if (tab.Kind != SessionKind.Ssh)
        {
            AppLog.Info(
                $"Remote monitor toggle ignored: tab {tab.SessionId} is {tab.Kind}, not SSH");
            tab.SftpPane?.SyncMonitorOn(tab.MonitorEnabled);
            return;
        }

        tab.MonitorEnabled = enabled;
        // 按钮态始终以标签开关为准，避免程序路径改动后按钮与轮询状态漂移。
        tab.SftpPane?.SyncMonitorOn(enabled);

        if (enabled && ReferenceEquals(tab, SelectedTab))
        {
            StartMonitorPolling(tab);
        }
        else if (!enabled && ReferenceEquals(tab, _monitoredTab))
        {
            StopMonitorPolling();
        }
    }

    /// <summary>
    /// 选中标签变化（由 SelectedTab setter 调用）：停旧标签轮询、
    /// 新标签为 SSH 且开监控时恢复轮询，并同步 SFTP 面板按钮态。
    /// </summary>
    public void MonitorOnSelectedTabChanged()
    {
        if (!ReferenceEquals(_monitoredTab, SelectedTab))
        {
            StopMonitorPolling();
        }

        var tab = SelectedTab;
        if (tab is not null && tab.MonitorEnabled && tab.IsAlive)
        {
            StartMonitorPolling(tab);
        }

        if (tab?.SftpPane is { } pane)
        {
            pane.SyncMonitorOn(tab.MonitorEnabled);
        }
    }

    /// <summary>
    /// SSH 传输 attach 之后（首连或 R 重启）：连接刚建好即视为存活，
    /// 当前标签开着监控就在新连接上恢复轮询。
    /// </summary>
    public void MonitorOnTransportAttached(TerminalTab tab)
    {
        if (ReferenceEquals(tab, _monitoredTab))
        {
            // 旧轮询用的是旧连接，必须换到新连接上
            StopMonitorPolling();
        }

        if (ReferenceEquals(tab, SelectedTab) && tab.MonitorEnabled)
        {
            StartMonitorPolling(tab);
        }
    }

    /// <summary>
    /// 断线/会话停止/关标签：停该标签的轮询。
    /// 不改 per-tab 开关，R 重启 attach 后自动恢复。
    /// </summary>
    public void MonitorStopForTab(TerminalTab tab)
    {
        if (ReferenceEquals(tab, _monitoredTab))
        {
            StopMonitorPolling();
        }
    }

    /// <summary>
    /// 菜单勾选变化后立即重画当前行（不等下一个 2s 轮询）。
    /// </summary>
    public void ReformatMonitorLine()
    {
        if (_lastSnapshot is not null)
        {
            MonitorText = _lastSnapshot.Format(MonitorMenu.ToOptions());
        }

        IsMonitorActive = MonitorText.Length > 0;
        RefreshMonitorSegments();
    }

    /// <summary>
    /// 按最新快照与菜单勾选态刷新状态栏分段盒子（MobaXterm 风格：
    /// 每指标一个带图标的独立小盒子）。段序与菜单一致：
    /// 主机/CPU/RAM/网络上传/网络下载/运行时长/分区。
    /// 整串 <see cref="MonitorText"/> 供菜单"复制/保存到文件"使用，不受影响。
    /// </summary>
    private void RefreshMonitorSegments()
    {
        var snap = _lastSnapshot;
        if (snap is null)
        {
            ClearMonitorSegments();
            return;
        }

        var o = MonitorMenu.ToOptions();

        SegHostVisible = o.ShowHost && !string.IsNullOrEmpty(snap.Host);
        SegHostText = snap.Host ?? "";

        if (o.ShowCpu && snap.CpuPercent is { } cpu)
        {
            var pct = (int)Math.Clamp(Math.Round(cpu), 0, 100);
            SegCpuVisible = true;
            SegCpuText = pct.ToString(CultureInfo.InvariantCulture) + "%";
        }
        else
        {
            SegCpuVisible = false;
        }

        if (o.ShowRam && snap.Ram is { } ram)
        {
            SegRamVisible = true;
            SegRamText = ram.UsedGb.ToString("0.00", CultureInfo.InvariantCulture)
                + " GB / " + ram.TotalGb.ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }
        else
        {
            SegRamVisible = false;
        }

        if (o.ShowNetUp && snap.NetUpMbps is { } up)
        {
            SegNetUpVisible = true;
            SegNetUpText = up.ToString("0.00", CultureInfo.InvariantCulture) + " Mb/s";
        }
        else
        {
            SegNetUpVisible = false;
        }

        if (o.ShowNetDown && snap.NetDownMbps is { } down)
        {
            SegNetDownVisible = true;
            SegNetDownText = down.ToString("0.00", CultureInfo.InvariantCulture) + " Mb/s";
        }
        else
        {
            SegNetDownVisible = false;
        }

        if (o.ShowUptime && snap.Uptime is { } upTime)
        {
            SegUptimeVisible = true;
            SegUptimeText = RemoteMetricsParser.FormatUptime(upTime);
        }
        else
        {
            SegUptimeVisible = false;
        }

        if (o.ShowPartitions && snap.Partitions is { Length: > 0 } parts)
        {
            SegPartitionsVisible = true;
            Partitions.Clear();
            for (var i = 0; i < parts.Length; i++)
            {
                Partitions.Add(new PartitionSegment
                {
                    Text = parts[i].Mount + ": " + parts[i].UsePct.ToString(CultureInfo.InvariantCulture) + "%",
                    IsHot = parts[i].UsePct >= 80,
                    ShowIcon = i == 0,
                });
            }
        }
        else
        {
            SegPartitionsVisible = false;
            Partitions.Clear();
        }
    }

    internal void ClearMonitorSegments()
    {
        SegHostVisible = false;
        SegCpuVisible = false;
        SegRamVisible = false;
        SegNetUpVisible = false;
        SegNetDownVisible = false;
        SegUptimeVisible = false;
        SegPartitionsVisible = false;
        Partitions.Clear();
        ClearCpuHistory();
    }

    /// <summary>
    /// 应用一次快照（生产路径由引擎线程经 Dispatcher 切 UI 线程调入，
    /// 测试可直接调用）。监控已停时丢弃过期快照。
    /// </summary>
    internal void ApplySnapshot(MonitorSnapshot snapshot)
    {
        if (_remoteMonitor is null)
        {
            return;
        }

        _lastSnapshot = snapshot;
        if (snapshot.CpuPercent is { } cpu)
        {
            PushCpuSample(cpu);
        }

        MonitorText = snapshot.Format(MonitorMenu.ToOptions());
        IsMonitorActive = MonitorText.Length > 0;
        RefreshMonitorSegments();
    }

    /// <summary>
    /// 引擎达到连续失败阈值（transport 断线 / unsupported 非 Linux）。
    /// </summary>
    internal void ApplyMonitorFailed(string reason)
    {
        if (_remoteMonitor is null)
        {
            return;
        }

        AppLog.Error($"Remote monitor stopped: {reason}");
        StopMonitorPolling();
    }

    private void StartMonitorPolling(TerminalTab tab)
    {
        if (tab.Connection is not IExecRunner runner)
        {
            // r66: 此前该分支静默返回——用户看到「按钮是绿的但永远没有数据」
            // 且日志无任何痕迹。现在显式告警，便于区分「没启动」与「启动后失败」。
            AppLog.Warn(
                $"Remote monitor start skipped: tab {tab.SessionId} connection " +
                $"is not an IExecRunner (monitor stays off in practice)");
            return;
        }

        if (_monitoredTab is not null)
        {
            StopMonitorPolling();
        }

        _monitoredTab = tab;
        var monitor = new RemoteMonitor(
            () => runner.RunCommandAsync(RemoteMonitor.DumpScript));
        monitor.SnapshotUpdated += snapshot =>
            Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
        monitor.Failed += reason =>
            Dispatcher.UIThread.Post(() => ApplyMonitorFailed(reason));
        _remoteMonitor = monitor;
        monitor.Start();
        AppLog.Info($"Remote monitor polling started: {tab.SessionId}");
    }

    private void StopMonitorPolling()
    {
        // 引擎事件在停止后不会再触发（循环退出前检查 running 标志），
        // 无订阅需要解除；对象随停止被回收。
        if (_remoteMonitor is not null)
        {
            AppLog.Info(
                $"Remote monitor polling stopped: {_monitoredTab?.SessionId ?? "?"}");
        }
        _remoteMonitor?.Dispose();
        _remoteMonitor = null;
        _monitoredTab = null;
        _lastSnapshot = null;
        MonitorText = "";
        IsMonitorActive = false;
        ClearMonitorSegments();
    }
}
