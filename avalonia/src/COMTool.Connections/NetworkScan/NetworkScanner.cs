using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace COMTool.Connections.NetworkScan;

public interface INetworkScanner
{
    /// <summary>
    /// Probes every target host in parallel (bounded by Concurrency).
    /// Discovered: reported once per up host at the FIRST liveness signal
    /// (ping reply or any open port) with the ports confirmed so far, so UI
    /// can show hosts incrementally (MobaXterm style). Up hosts are then
    /// reported through progress as their full probe (all ports + name
    /// resolution) finishes; the task throws OperationCanceledException when
    /// the token is cancelled.
    /// Returns the definitive list of up hosts (sorted by address) so the UI
    /// can reconcile/finalize rows even if individual progress callbacks are
    /// dropped after the scan ends.
    /// </summary>
    Task<IReadOnlyList<NetworkScanHost>> ScanAsync(
        IReadOnlyList<IPAddress> targets,
        IReadOnlyList<int> ports,
        IProgress<NetworkScanProgress>? progress,
        IProgress<NetworkScanHost>? discovered,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deep scan (MobaXterm "Deep scan"): probes every port in the inclusive
    /// range [fromPort..toPort] on a single host, bounded by Concurrency.
    /// Progress reports the number of ports probed so far; the returned list
    /// is the sorted set of open ports.
    /// </summary>
    Task<IReadOnlyList<int>> ScanHostPortsAsync(
        IPAddress host,
        int fromPort,
        int toPort,
        IProgress<int>? scannedProgress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Probes an explicit (sparse) list of ports on a single host, bounded by
    /// Concurrency. Progress reports the number of ports probed so far.
    /// Used by 端口扫描仪 "仅扫描已知端口" mode.
    /// </summary>
    Task<IReadOnlyList<int>> ScanHostPortsAsync(
        IPAddress host,
        IReadOnlyList<int> ports,
        IProgress<int>? scannedProgress,
        CancellationToken cancellationToken);
}

/// <summary>
/// ICMP ping (best effort, e.g. unprivileged Linux may be denied) + TCP
/// connect probe + reverse DNS, MobaXterm network-scanner style. A single
/// failing host never aborts the scan.
/// </summary>
public sealed class NetworkScanner : INetworkScanner
{
    /// <summary>
    /// All 254 addresses of a /24 probed in one wave (MobaXterm-style speed:
    /// sub-second for down hosts, whole scan ~1-2 s).
    /// </summary>
    public int Concurrency { get; init; } = 256;

    public int PingTimeoutMs { get; init; } = 300;

    public int ConnectTimeoutMs { get; init; } = 300;

    public int DnsTimeoutMs { get; init; } = 500;

    /// <summary>
    /// NetBIOS name fallback wait (only for hosts that answered ping/TCP).
    /// 0 disables the fallback.
    /// </summary>
    public int NetbiosTimeoutMs { get; init; } = NetbiosNameResolver.DefaultTimeoutMs;

    public async Task<IReadOnlyList<NetworkScanHost>> ScanAsync(
        IReadOnlyList<IPAddress> targets,
        IReadOnlyList<int> ports,
        IProgress<NetworkScanProgress>? progress,
        IProgress<NetworkScanHost>? discovered,
        CancellationToken cancellationToken)
    {
        if (targets is null || targets.Count == 0)
        {
            return Array.Empty<NetworkScanHost>();
        }

        using var gate = new SemaphoreSlim(Math.Max(1, Concurrency));
        int completed = 0;
        var total = targets.Count;
        var upHosts = new System.Collections.Concurrent.ConcurrentBag<NetworkScanHost>();

        // Task.Run：探测任务体（raw ICMP socket + 7 个 TCP socket 的创建）
        // 必须在线程池上跑。若用 Select(async ...) 内联创建，254 台主机的
        // socket 会全部在调用方（UI）线程上同步铺开，点击 Start scan 卡 ~3s。
        var tasks = targets.Select(ip => Task.Run(async () =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                NetworkScanHost? host = null;
                try
                {
                    host = await ProbeAsync(ip, ports, discovered, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // A single bad host must not kill the whole scan.
                }

                if (host is not null)
                {
                    upHosts.Add(host);
                }

                var done = Interlocked.Increment(ref completed);
                progress?.Report(new NetworkScanProgress(done, total, host));
            }
            finally
            {
                gate.Release();
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks);

        // 权威结果：即使个别 progress 回调在扫描结束后被 UI 丢弃，窗口也能
        // 用这份列表做最终对账（2026-10-29 实机 192.0.2.251 行滞留 "…" 修复）
        return upHosts
            .OrderBy(h => h.Address.GetAddressBytes(),
                System.Collections.Generic.Comparer<byte[]>.Create((a, b) => a.SequenceCompareTo(b)))
            .ToList();
    }

    public Task<IReadOnlyList<int>> ScanHostPortsAsync(
        IPAddress host,
        int fromPort,
        int toPort,
        IProgress<int>? scannedProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (fromPort is < 1 or > 65535 || toPort is < 1 or > 65535 || fromPort > toPort)
        {
            throw new ArgumentOutOfRangeException(nameof(fromPort), "端口范围必须在 1..65535 内且起始不大于结束");
        }

        var ports = System.Linq.Enumerable.Range(fromPort, toPort - fromPort + 1).ToArray();
        return ScanPortsCoreAsync(host, ports, scannedProgress, cancellationToken);
    }

    public Task<IReadOnlyList<int>> ScanHostPortsAsync(
        IPAddress host,
        IReadOnlyList<int> ports,
        IProgress<int>? scannedProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(ports);
        return ScanPortsCoreAsync(host, ports, scannedProgress, cancellationToken);
    }

    private async Task<IReadOnlyList<int>> ScanPortsCoreAsync(
        IPAddress host,
        IReadOnlyList<int> ports,
        IProgress<int>? scannedProgress,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, Concurrency));
        var openPorts = new System.Collections.Concurrent.ConcurrentBag<int>();
        int scanned = 0;
        var total = ports.Count;

        // 同 ScanAsync：任务体放到线程池，避免大批 socket 在 UI 线程内联创建
        var tasks = ports.Select(port => Task.Run(async () =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (await ConnectAsync(host, port, cancellationToken))
                {
                    openPorts.Add(port);
                }

                var done = Interlocked.Increment(ref scanned);
                // 小列表（已知端口模式）逐个上报；大列表（65535 全端口）每 512 报一次
                if (total <= 64 || done == total || done % 512 == 0)
                {
                    scannedProgress?.Report(done);
                }
            }
            finally
            {
                gate.Release();
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();
        return openPorts.OrderBy(p => p).ToList();
    }

    private async Task<NetworkScanHost?> ProbeAsync(
        IPAddress ip,
        IReadOnlyList<int> ports,
        IProgress<NetworkScanHost>? discovered,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var pingTask = PingAsync(ip, ct);
        var connectTasks = ports.Select(port => ConnectAsync(ip, port, ct)).ToArray();

        var openPorts = new List<int>();
        int? rtt = null;
        bool aliveReported = false;

        // 两阶段（2026-10-29 用户要求，MobaXterm 式增量显示）：
        // 第一个存活信号（ping 回复或任一端口连通）立即经 discovered 上报
        // （名称空、rtt 空、开放端口=当时已确认的子集），全部探测完成 +
        // 名称解析后由 ScanAsync 经 progress 上报最终结果。总时长不变。
        // Task.WhenAny 返回非泛型 Task，统一用引用比较识别是哪个探针完成，
        // 再回到对应类型化句柄取结果（避免 Task<bool>[]/Task<int?> 混排歧义）
        var pending = new List<Task>(1 + connectTasks.Length);
        pending.Add(pingTask);
        for (var i = 0; i < connectTasks.Length; i++)
        {
            pending.Add(connectTasks[i]);
        }

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var finished = await Task.WhenAny(pending.ToArray());
            pending.Remove(finished);

            bool isAlive = false;
            if (ReferenceEquals(finished, pingTask))
            {
                rtt = pingTask.Result;
                isAlive = rtt is not null;
            }
            else
            {
                for (var i = 0; i < connectTasks.Length; i++)
                {
                    if (ReferenceEquals(finished, connectTasks[i]))
                    {
                        if (connectTasks[i].Result)
                        {
                            openPorts.Add(ports[i]);
                            isAlive = true;
                        }
                        break;
                    }
                }
            }

            if (isAlive && !aliveReported)
            {
                aliveReported = true;
                discovered?.Report(new NetworkScanHost(ip, null, openPorts.ToList(), null));
            }
        }

        if (rtt is null && openPorts.Count == 0)
        {
            return null;
        }

        // 只对存活主机做名称解析（MobaXterm 同样做法）：给 254 个目标都
        // 做反向 DNS 是旧版 /24 扫描要 7 秒多的主因——无 PTR 的机器每次
        // 都要等满超时。
        var name = await ResolveAsync(ip, ct);
        if (string.IsNullOrEmpty(name))
        {
            // 小型局域网通常没有 PTR 记录；用 NetBIOS 工作站名兜底
            // (MobaXterm 也会显示这类短主机名)。
            name = await NetbiosNameResolver.QueryAsync(ip, NetbiosTimeoutMs, ct);
        }

        return new NetworkScanHost(ip, name, openPorts, rtt);
    }

    private async Task<int?> PingAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, PingTimeoutMs).WaitAsync(ct);
            return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Raw-socket permission denied (unprivileged Linux) or timeout:
            // ICMP is best effort, TCP probing still decides liveness.
            return null;
        }
    }

    private async Task<bool> ConnectAsync(IPAddress ip, int port, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ConnectTimeoutMs);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ip, port, cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> ResolveAsync(IPAddress ip, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DnsTimeoutMs);
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip.ToString()).WaitAsync(cts.Token);
            // 有些平台在无 PTR 时把 IP 本身当作主机名返回；那不算名字。
            return entry.HostName.Length > 0
                   && !string.Equals(entry.HostName, ip.ToString(), StringComparison.OrdinalIgnoreCase)
                ? entry.HostName
                : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
