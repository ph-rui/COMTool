using System.Globalization;

namespace COMTool.Connections.RemoteMonitoring;

/// <summary>
/// 单次轮询得到的远端主机指标快照。CPU/网络速率由引擎用相邻两次
/// 采样做差值得到，首次采样时为 null（无基线）。
/// </summary>
public sealed class MonitorSnapshot
{
    public string? Host { get; init; }

    public double? CpuPercent { get; init; }

    /// <summary>(used GB, total GB)，来自 /proc/meminfo 的 MemTotal-MemAvailable。</summary>
    public (double UsedGb, double TotalGb)? Ram { get; init; }

    public double? NetUpMbps { get; init; }

    public double? NetDownMbps { get; init; }

    public TimeSpan? Uptime { get; init; }

    public (string Mount, int UsePct)[]? Partitions { get; init; }

    /// <summary>
    /// 按 MobaXterm 状态栏样式渲染：固定顺序
    /// 主机/CPU/RAM/网络/运行时长/分区，
    /// 关闭或缺失的段省略，段间两个空格，数字一律 InvariantCulture。
    /// </summary>
    public string Format(MonitorOptions options)
    {
        var parts = new List<string>(10);
        if (options.ShowHost && !string.IsNullOrEmpty(Host))
        {
            parts.Add(Host);
        }

        if (options.ShowCpu && CpuPercent is { } cpu)
        {
            parts.Add($"{(int)Math.Clamp(Math.Round(cpu), 0, 100)}%");
        }

        if (options.ShowRam && Ram is { } ram)
        {
            parts.Add(
                $"{ram.UsedGb.ToString("0.00", CultureInfo.InvariantCulture)} GB / " +
                $"{ram.TotalGb.ToString("0.00", CultureInfo.InvariantCulture)} GB");
        }

        var net = new List<string>(2);
        if (options.ShowNetUp && NetUpMbps is { } up)
        {
            net.Add($"\u2191{up.ToString("0.00", CultureInfo.InvariantCulture)}Mb/s");
        }

        if (options.ShowNetDown && NetDownMbps is { } down)
        {
            net.Add($"\u2193{down.ToString("0.00", CultureInfo.InvariantCulture)}Mb/s");
        }

        if (net.Count > 0)
        {
            parts.Add(string.Join(' ', net));
        }

        if (options.ShowUptime && Uptime is { } uptime)
        {
            parts.Add(RemoteMetricsParser.FormatUptime(uptime));
        }

        if (options.ShowPartitions && Partitions is { Length: > 0 } partitions)
        {
            parts.Add(string.Join(' ', partitions.Select(p => $"{p.Mount}: {p.UsePct}%")));
        }

        return string.Join("  ", parts);
    }
}
