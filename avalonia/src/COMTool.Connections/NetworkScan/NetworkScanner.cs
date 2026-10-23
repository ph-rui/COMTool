using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace COMTool.Connections.NetworkScan;

public interface INetworkScanner
{
    /// <summary>
    /// Probes every target host in parallel (bounded by Concurrency). Up
    /// hosts are reported through progress as they finish; the task throws
    /// OperationCanceledException when the token is cancelled.
    /// </summary>
    Task ScanAsync(
        IReadOnlyList<IPAddress> targets,
        IReadOnlyList<int> ports,
        IProgress<NetworkScanProgress>? progress,
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
}

/// <summary>
/// ICMP ping (best effort, e.g. unprivileged Linux may be denied) + TCP
/// connect probe + reverse DNS, MobaXterm network-scanner style. A single
/// failing host never aborts the scan.
/// </summary>
public sealed class NetworkScanner : INetworkScanner
{
    public int Concurrency { get; init; } = 32;

    public int PingTimeoutMs { get; init; } = 1000;

    public int ConnectTimeoutMs { get; init; } = 500;

    public int DnsTimeoutMs { get; init; } = 1000;

    public async Task ScanAsync(
        IReadOnlyList<IPAddress> targets,
        IReadOnlyList<int> ports,
        IProgress<NetworkScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (targets is null || targets.Count == 0)
        {
            return;
        }

        using var gate = new SemaphoreSlim(Math.Max(1, Concurrency));
        int completed = 0;
        var total = targets.Count;

        var tasks = targets.Select(async ip =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                NetworkScanHost? host = null;
                try
                {
                    host = await ProbeAsync(ip, ports, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // A single bad host must not kill the whole scan.
                }

                var done = Interlocked.Increment(ref completed);
                progress?.Report(new NetworkScanProgress(done, total, host));
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
    }

    public async Task<IReadOnlyList<int>> ScanHostPortsAsync(
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

        using var gate = new SemaphoreSlim(Math.Max(1, Concurrency));
        var openPorts = new System.Collections.Concurrent.ConcurrentBag<int>();
        int scanned = 0;
        var total = toPort - fromPort + 1;

        var tasks = System.Linq.Enumerable.Range(fromPort, total)
            .Select(async port =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    if (await ConnectAsync(host, port, cancellationToken))
                    {
                        openPorts.Add(port);
                    }

                    var done = Interlocked.Increment(ref scanned);
                    if (done == total || done % 512 == 0)
                    {
                        scannedProgress?.Report(done);
                    }
                }
                finally
                {
                    gate.Release();
                }
            })
            .ToArray();

        await Task.WhenAll(tasks);
        return openPorts.OrderBy(p => p).ToList();
    }

    private async Task<NetworkScanHost?> ProbeAsync(
        IPAddress ip,
        IReadOnlyList<int> ports,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var pingTask = PingAsync(ip, ct);
        var dnsTask = ResolveAsync(ip, ct);
        var connectTasks = ports.Select(port => ConnectAsync(ip, port, ct)).ToArray();

        await Task.WhenAll([pingTask, dnsTask, ..connectTasks]);

        var openPorts = new List<int>();
        for (var i = 0; i < connectTasks.Length; i++)
        {
            if (connectTasks[i].Result)
            {
                openPorts.Add(ports[i]);
            }
        }

        int? rtt = pingTask.Result;
        string? name = dnsTask.Result;

        if (rtt is null && openPorts.Count == 0)
        {
            return null;
        }

        return new NetworkScanHost(ip, name, openPorts, rtt);
    }

    private async Task<int?> PingAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, PingTimeoutMs);
            return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : null;
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
            return entry.HostName.Length > 0 ? entry.HostName : null;
        }
        catch
        {
            return null;
        }
    }
}
