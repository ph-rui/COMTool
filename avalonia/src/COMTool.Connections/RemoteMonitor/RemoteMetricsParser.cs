using System.Globalization;

namespace COMTool.Connections.RemoteMonitor;

/// <summary>
/// 解析远程监控 dump 命令的输出。dump 脚本为单行 shell 命令（见
/// <see cref="RemoteMonitor.DumpScript"/>），每个指标输出一行 "MON_XXX=value"，
/// 多值段用 | 或 , 连接。解析器对缺失/格式错误一律返回 null 而不抛异常
/// （非 Linux 远端或命令失败时引擎按"连续失败"策略处理）。
/// </summary>
public static class RemoteMetricsParser
{
    /// <summary>一次 dump 的解析结果；未取到的段为 null。</summary>
    public sealed class DumpData
    {
        public string? Host { get; set; }
        /// <summary>(idle, total)，/proc/stat 聚合行计数器。</summary>
        public (long Idle, long Total)? Cpu { get; set; }
        /// <summary>(totalKb, usedKb)，used = MemTotal - MemAvailable。</summary>
        public (long TotalKb, long UsedKb)? Mem { get; set; }
        /// <summary>(rxBytes, txBytes)，/proc/net/dev 全接口（除 lo）累计。</summary>
        public (long Rx, long Tx)? Net { get; set; }
        public double? UptimeSeconds { get; set; }
        public int? ConnectedClients { get; set; }
        public int? ProcessCount { get; set; }
        public int? FileDescriptorCount { get; set; }
        public string[]? Users { get; set; }
        public (string Mount, int UsePct)[]? Partitions { get; set; }

        /// <summary>非 Linux 远端（无任何可解析段）时为 false。</summary>
        public bool HasAnySection =>
            !string.IsNullOrEmpty(Host) || Cpu is not null || Mem is not null
            || Net is not null || UptimeSeconds is not null
            || ConnectedClients is not null || ProcessCount is not null
            || FileDescriptorCount is not null || Users is not null
            || Partitions is not null;
    }

    /// <summary>解析整段 dump 输出。null/空输入返回空 DumpData（HasAnySection=false）。</summary>
    public static DumpData Dump(string? output)
    {
        var data = new DumpData();
        if (string.IsNullOrEmpty(output))
        {
            return data;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r', ' ', '\t');
            var eq = line.IndexOf('=');
            if (eq < 5 || !line.StartsWith("MON_", StringComparison.Ordinal))
            {
                continue;
            }

            var key = line.Substring(0, eq);
            var value = line.Substring(eq + 1).Trim();
            if (value.Length == 0)
            {
                continue;
            }

            switch (key)
            {
                case "MON_HOST": data.Host = value; break;
                case "MON_CPU": data.Cpu = ParseCpuLine(value); break;
                case "MON_MEM": data.Mem = ParseMemInfo(value); break;
                case "MON_NET": data.Net = ParseNetDev(value); break;
                case "MON_UP": data.UptimeSeconds = ParseSeconds(value); break;
                case "MON_CLIENTS": data.ConnectedClients = ParseNonNegativeInt(value); break;
                case "MON_PROC": data.ProcessCount = ParseNonNegativeInt(value); break;
                case "MON_FD": data.FileDescriptorCount = ParseNonNegativeInt(value); break;
                case "MON_WHO": data.Users = ParseUsers(value); break;
                case "MON_DF": data.Partitions = ParseDf(value); break;
            }
        }

        return data;
    }

    /// <summary>
    /// CPU 利用率：两次聚合计数做差。100 * (dTotal - dIdle) / dTotal。
    /// 计数器回绕/重置（dTotal <= 0）返回 null。
    /// </summary>
    public static double? CpuDelta((long Idle, long Total)? prev, (long Idle, long Total)? cur)
    {
        if (prev is not { } p || cur is not { } c)
        {
            return null;
        }

        var dTotal = c.Total - p.Total;
        if (dTotal <= 0)
        {
            return null;
        }

        var dIdle = c.Idle - p.Idle;
        return Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0.0, 100.0);
    }

    /// <summary>
    /// 网络速率（Mb/s，十进制 1e6）：(Δbytes * 8) / Δt。
    /// dt 过短（&lt;50ms）或计数回绕（Δ&lt;0）按 0 处理。Rx=下载，Tx=上传。
    /// </summary>
    public static (double UpMbps, double DownMbps) NetRate(
        (long Rx, long Tx)? prev, (long Rx, long Tx)? cur, double dtSec)
    {
        if (prev is not { } p || cur is not { } c || dtSec <= 0.05)
        {
            return (0, 0);
        }

        var dRx = c.Rx - p.Rx; if (dRx < 0) dRx = 0;
        var dTx = c.Tx - p.Tx; if (dTx < 0) dTx = 0;
        return (dTx * 8.0 / 1e6 / dtSec, dRx * 8.0 / 1e6 / dtSec);
    }

    /// <summary>运行时长取主单位：≥1 天 "N days"，≥1 小时 "N hours"，≥1 分 "N min"，否则 "N s"。</summary>
    public static string FormatUptime(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
        {
            var d = (int)ts.TotalDays;
            return $"{d} {(d == 1 ? "day" : "days")}";
        }

        if (ts.TotalHours >= 1)
        {
            var h = (int)ts.TotalHours;
            return $"{h} {(h == 1 ? "hour" : "hours")}";
        }

        if (ts.TotalMinutes >= 1)
        {
            return $"{(int)ts.TotalMinutes} min";
        }

        return $"{(int)ts.TotalSeconds} s";
    }

    private static (long Idle, long Total)? ParseCpuLine(string value)
    {
        var f = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        // 需要至少 cpu user nice system idle 五个字段
        if (f.Length < 5 || !f[0].Equals("cpu", StringComparison.Ordinal))
        {
            return null;
        }

        long total = 0;
        long idle = 0;
        for (var i = 1; i < f.Length; i++)
        {
            if (!long.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                return null;
            }

            total += v;
            // f[1]=user f[2]=nice f[3]=system f[4]=idle f[5]=iowait
            if (i is 4 or 5)
            {
                idle += v;
            }
        }

        return (idle, total);
    }

    private static (long TotalKb, long UsedKb)? ParseMemInfo(string value)
    {
        long? total = null;
        long? avail = null;
        foreach (var token in value.Split('|'))
        {
            var t = token.Trim();
            var i = t.IndexOf(':');
            if (i < 0)
            {
                continue;
            }

            var fields = t.Substring(i + 1).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0
                || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                continue;
            }

            if (t.StartsWith("MemTotal:"))
            {
                total = v;
            }
            else if (t.StartsWith("MemAvailable:"))
            {
                avail = v;
            }
        }

        if (total is null || avail is null || avail.Value > total.Value)
        {
            return null;
        }

        return (total.Value, total.Value - avail.Value);
    }

    private static (long Rx, long Tx)? ParseNetDev(string value)
    {
        var f = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 2)
        {
            return null;
        }

        if (!long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rx)
            || !long.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tx))
        {
            return null;
        }

        return (rx, tx);
    }

    private static double? ParseSeconds(string value)
    {
        var f = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return f.Length > 0
               && double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
               && s >= 0
            ? s
            : null;
    }

    private static int? ParseNonNegativeInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v
            : null;
    }

    private static string[]? ParseUsers(string value)
    {
        var parts = value
            .Split(',')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();
        return parts.Length > 0 ? parts : null;
    }

    private static (string Mount, int UsePct)[]? ParseDf(string value)
    {
        var list = new List<(string Mount, int UsePct)>();
        foreach (var token in value.Split('|'))
        {
            var t = token.Trim();
            var pct = t.IndexOf('%');
            if (pct <= 0)
            {
                continue;
            }

            if (!int.TryParse(t.Substring(0, pct), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
            {
                continue;
            }

            var mount = t.Substring(pct + 1).Trim();
            if (mount.Length == 0)
            {
                continue;
            }

            list.Add((mount, p));
        }

        return list.Count > 0 ? list.ToArray() : null;
    }
}
