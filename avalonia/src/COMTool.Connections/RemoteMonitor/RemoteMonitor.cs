namespace COMTool.Connections.RemoteMonitoring;

/// <summary>
/// 远程监控轮询引擎：每隔 <c>interval</c>（默认 2 秒）通过独立的 exec 通道
/// 在远端执行一次 dump 命令，解析 /proc 指标并对 CPU/网络做相邻采样差分，
/// 通过 <see cref="SnapshotUpdated"/> 推送快照。
/// 事件在引擎内部循环线程上触发，上层（UI）自行切线程。
/// 连续失败（exec 异常或输出无任何可解析段）达到阈值后自动停止并触发
/// <see cref="Failed"/>；运行中调用 Stop 不会触发 Failed。
/// </summary>
public sealed class RemoteMonitor : IDisposable
{
    /// <summary>默认轮询间隔，对齐 MobaXterm 手感。</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 单行远端 dump 脚本：每轮监控执行一次，每个指标一行 MON_XXX=value
    /// （spec 2026-10-29-remote-monitoring 3.2 节）。非 Linux 远端输出无
    /// MON_ 段，引擎按 unsupported 处理。
    /// </summary>
    public const string DumpScript =
        "echo \"MON_HOST=$(hostname 2>/dev/null)\"; "
        + "echo \"MON_CPU=$(head -n1 /proc/stat 2>/dev/null)\"; "
        + "echo \"MON_MEM=$(grep -E '^(MemTotal|MemAvailable):' /proc/meminfo 2>/dev/null | tr '\\n' '|')\"; "
        + "echo \"MON_UP=$(cat /proc/uptime 2>/dev/null)\"; "
        + "echo \"MON_CLIENTS=$(who 2>/dev/null | wc -l)\"; "
        + "echo \"MON_PROC=$(ps -e --no-headers 2>/dev/null | wc -l)\"; "
        + "echo \"MON_FD=$(cut -d' ' -f1 /proc/sys/fs/file-nr 2>/dev/null)\"; "
        + "echo \"MON_WHO=$(who 2>/dev/null | awk '{print $1}' | paste -sd, -)\"; "
        + "echo \"MON_NET=$(awk 'NR>2 && $1!=\"lo:\" {rx+=$2; tx+=$10} END{print rx+0\" \"tx+0}' /proc/net/dev 2>/dev/null)\"; "
        + "echo \"MON_DF=$(df -P -x tmpfs -x devtmpfs -x squashfs -x overlay 2>/dev/null | awk 'NR>1 {m=$6; for(i=7;i<=NF;i++) m=m\" \"$i; print $5\" \"m}' | paste -sd'|' -)\"; "
        + "echo MON_END";

    private const int MaxConsecutiveFailures = 2;

    private readonly Func<Task<string>> _dumpSource;
    private readonly TimeSpan _interval;

    private int _running;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private (long Idle, long Total)? _prevCpu;
    private (long Rx, long Tx)? _prevNet;
    private DateTime _prevSampleUtc = DateTime.MinValue;

    /// <summary>每次成功解析后触发，参数为本次指标快照。</summary>
    public event Action<MonitorSnapshot>? SnapshotUpdated;

    /// <summary>达到连续失败阈值后触发。reason: "transport" | "unsupported"。</summary>
    public event Action<string>? Failed;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public RemoteMonitor(Func<Task<string>> dumpSource, TimeSpan? interval = null)
    {
        _dumpSource = dumpSource ?? throw new ArgumentNullException(nameof(dumpSource));
        _interval = interval ?? DefaultInterval;
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return;
        }

        // 新一轮监控重新建立基线，避免用上一轮的旧计数差分
        _prevCpu = null;
        _prevNet = null;
        _prevSampleUtc = DateTime.MinValue;

        var cts = new CancellationTokenSource();
        _cts = cts;
        _loop = Task.Run(() => LoopAsync(cts));
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _running, 0) == 0)
        {
            return;
        }

        _cts?.Cancel();
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task LoopAsync(CancellationTokenSource cts)
    {
        var failures = 0;
        try
        {
            while (!cts.Token.IsCancellationRequested && Volatile.Read(ref _running) == 1)
            {
                var nowUtc = DateTime.UtcNow;
                MonitorSnapshot? snapshot = null;
                string? failureReason = null;

                try
                {
                    var raw = await _dumpSource().ConfigureAwait(false);
                    var data = RemoteMetricsParser.Dump(raw);
                    if (data.HasAnySection)
                    {
                        failures = 0;
                        snapshot = BuildSnapshot(data, nowUtc);
                    }
                    else
                    {
                        // 远端不是 Linux（/proc 不存在）或命令被拒绝
                        failureReason = "unsupported";
                    }
                }
                catch
                {
                    // exec 通道异常（断线、超时、连接被释放等）
                    failureReason = "transport";
                }

                if (failureReason is not null)
                {
                    failures++;
                    if (failures >= MaxConsecutiveFailures && Volatile.Read(ref _running) == 1)
                    {
                        Failed?.Invoke(failureReason);
                        return;
                    }
                }
                else if (snapshot is not null)
                {
                    SnapshotUpdated?.Invoke(snapshot);
                }

                try
                {
                    await Task.Delay(_interval, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            // Only the loop of the current session may clear the flag, so a
            // stale loop from a previous Start/Stop cycle never kills a
            // newer session's running state.
            if (ReferenceEquals(_cts, cts))
            {
                Interlocked.Exchange(ref _running, 0);
                _cts = null;
                cts.Dispose();
            }
        }
    }

    private MonitorSnapshot BuildSnapshot(RemoteMetricsParser.DumpData data, DateTime nowUtc)
    {
        var cpu = RemoteMetricsParser.CpuDelta(_prevCpu, data.Cpu);
        if (data.Cpu is not null)
        {
            _prevCpu = data.Cpu;
        }

        double? netUp = null;
        double? netDown = null;
        if (data.Net is not null)
        {
            var dtSec = _prevSampleUtc == DateTime.MinValue
                ? 0
                : (nowUtc - _prevSampleUtc).TotalSeconds;
            var rate = RemoteMetricsParser.NetRate(_prevNet, data.Net, dtSec);
            netUp = rate.UpMbps;
            netDown = rate.DownMbps;
            _prevNet = data.Net;
            _prevSampleUtc = nowUtc;
        }

        return new MonitorSnapshot
        {
            Host = data.Host,
            CpuPercent = cpu,
            Ram = data.Mem is { } m
                ? (m.UsedKb / 1048576.0, m.TotalKb / 1048576.0)
                : null,
            NetUpMbps = netUp,
            NetDownMbps = netDown,
            ConnectedClients = data.ConnectedClients,
            ProcessCount = data.ProcessCount,
            FileDescriptorCount = data.FileDescriptorCount,
            Uptime = data.UptimeSeconds is { } s ? TimeSpan.FromSeconds(s) : null,
            Users = data.Users,
            Partitions = data.Partitions,
        };
    }
}
