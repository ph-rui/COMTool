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
