using System.Net;
using System.Net.Sockets;
using COMTool.Connections.NetworkScan;

namespace COMTool.Connections.Tests;

public sealed class NetworkScannerTests
{
    /// <summary>
    /// A progress sink that invokes the callback synchronously so tests
    /// don't race against a posted sync-context callback.
    /// </summary>
    private sealed class SyncProgress : IProgress<NetworkScanProgress>
    {
        private readonly Action<NetworkScanProgress> _action;
        public SyncProgress(Action<NetworkScanProgress> action) => _action = action;
        public void Report(NetworkScanProgress value) => _action(value);
    }

    private sealed class IntSyncProgress : IProgress<int>
    {
        private readonly Action<int> _action;
        public IntSyncProgress(Action<int> action) => _action = action;
        public void Report(int value) => _action(value);
    }

    private static IReadOnlyList<IPAddress> Ips(params string[] ips)
    {
        var list = new List<IPAddress>();
        foreach (var ip in ips)
        {
            Assert.True(IPAddress.TryParse(ip, out var addr));
            list.Add(addr);
        }
        return list;
    }

    [Fact]
    public void NET05_SubnetRangeExpandsLastOctet()
    {
        // MobaXterm 网络扫描仪 "IP 地址范围"：前三段固定 + 末段范围
        Assert.True(
            NetworkScanTargets.TryParseSubnet("192", "168", "1", "1", "254", out var targets, out var error),
            $"parse failed: {error}");
        Assert.Equal(254, targets.Count);
        Assert.Equal("192.168.1.1", targets[0].ToString());
        Assert.Equal("192.168.1.254", targets[253].ToString());

        Assert.True(
            NetworkScanTargets.TryParseSubnet("10", "13", "1", "1", "10", out var small, out var smallError),
            $"parse failed: {smallError}");
        Assert.Equal(10, small.Count);
        Assert.Equal("10.13.1.1", small[0].ToString());
        Assert.Equal("10.13.1.10", small[9].ToString());
    }

    [Fact]
    public void NET05_SubnetRangeRejectsInvalid()
    {
        var invalid = new (string, string, string, string, string)[]
        {
            ("", "168", "1", "1", "254"),       // 段为空
            ("abc", "168", "1", "1", "254"),    // 非数字
            ("256", "168", "1", "1", "254"),    // 段超范围
            ("192", "168", "1", "1", "300"),    // 末段结束超范围
            ("192", "168", "1", "200", "10"),   // 结束小于起始
        };
        foreach (var (a, b, c, f, t) in invalid)
        {
            Assert.False(
                NetworkScanTargets.TryParseSubnet(a, b, c, f, t, out _, out var error),
                $"unexpectedly accepted {a}.{b}.{c}.{f}->{t}");
            Assert.NotNull(error);
        }
    }

    [Fact]
    public void NET05_TryParseIpAcceptsStrictIpv4Only()
    {
        Assert.True(NetworkScanTargets.TryParseIp("172.16.66.163", out var ip, out _));
        Assert.Equal("172.16.66.163", ip.ToString());

        // 传统 2/3 段与 32 位写法必须拒绝（与 MobaXterm 严格格式一致）
        foreach (var bad in new[]
                 {
                     "", "   ", "not-an-ip", "192.168.1", "192.168.1.256",
                     "1.2.3.4.5", "10.0.0.2000", "fe80::1",
                 })
        {
            Assert.False(NetworkScanTargets.TryParseIp(bad, out _, out var error),
                $"unexpectedly accepted '{bad}'");
            Assert.NotNull(error);
        }
    }

    [Fact]
    public void NET05_ServiceColumnsMatchMobaXterm()
    {
        // 网络扫描仪固定服务列顺序与端口（1:1 实测 MobaXterm v22.1）
        Assert.Equal(
            new[] { 22, 3389, 5900, 21, 23, 513, 80 },
            NetworkServices.ServiceColumns.Select(c => c.Port).ToArray());
        Assert.Equal(
            new[] { "SSH", "RDP", "VNC", "FTP", "Telnet", "Rlogin", "HTTP" },
            NetworkServices.ServiceColumns.Select(c => c.Name).ToArray());

        // AllProbePorts = 7 个服务端口 + "其他端口" 探测集
        Assert.Equal(7 + NetworkServices.OtherPorts.Length, NetworkServices.AllProbePorts.Length);
        Assert.All(NetworkServices.AllProbePorts, p => Assert.InRange(p, 1, 65535));
    }

    [Fact]
    public void NET05_FormatPortLineMatchesMobaXterm()
    {
        // 端口扫描仪结果行格式（实测）: "- Port #22 (ssh): listening"
        Assert.Equal("- Port #22 (ssh): listening", NetworkServices.FormatPortLine(22));
        Assert.Equal("- Port #6000 (x11 :0): listening", NetworkServices.FormatPortLine(6000));
        // 未知端口不带服务名
        Assert.Equal("- Port #12345: listening", NetworkServices.FormatPortLine(12345));

        // 已知端口表必须覆盖实测出现的端口
        foreach (var p in new[] { 22, 123, 139, 445, 3389, 993, 995, 6000 })
        {
            Assert.Contains(p, NetworkServices.KnownPortNumbers);
        }
    }

    [Fact]
    public async Task NET05_FindsOpenTcpPortOnLoopback()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var hosts = new List<NetworkScanHost>();
        var progress = new SyncProgress(p =>
        {
            if (p.Host is not null)
            {
                hosts.Add(p.Host);
            }
        });

        await new NetworkScanner().ScanAsync(
            Ips("127.0.0.1"), [port], progress, default);

        Assert.Single(hosts);
        Assert.Equal("127.0.0.1", hosts[0].Address.ToString());
        Assert.Equal([port], hosts[0].OpenPorts);
        // ICMP is best effort: RTT is reported only when ping is allowed.
        Assert.True(hosts[0].RoundTripMs is null || hosts[0].RoundTripMs >= 0);

        listener.Stop();
    }

    [Fact]
    public async Task NET05_ClosedPortIsNeverReportedOpen()
    {
        // Port 1 on loopback is not listening. The host itself may still be
        // reported up via ping (platform dependent), but port 1 must not be
        // listed as open.
        var hosts = new List<NetworkScanHost>();
        var progress = new SyncProgress(p =>
        {
            if (p.Host is not null)
            {
                hosts.Add(p.Host);
            }
        });

        await new NetworkScanner { PingTimeoutMs = 300, ConnectTimeoutMs = 300 }
            .ScanAsync(Ips("127.0.0.1"), [1], progress, default);

        foreach (var host in hosts)
        {
            Assert.DoesNotContain(1, host.OpenPorts);
        }
    }

    [Fact]
    public async Task NET05_CancellationStopsScan()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // An already-cancelled token must abort without probing anything.
        var progress = new SyncProgress(_ =>
        {
            Assert.Fail("progress reported after cancellation");
        });

        var ex = await Record.ExceptionAsync(async () =>
            await new NetworkScanner().ScanAsync(
                Ips("127.0.0.1", "127.0.0.2"), [22], progress, cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task NET05_KnownPortListProbeFindsOpenPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // 显式端口列表（端口扫描仪"仅扫描已知端口"模式）：
        // 一个开放端口 + 一个关闭端口（端口 1），结果只含开放端口且升序
        var ports = new[] { 1, port }.OrderBy(p => p).ToArray();
        var open = await new NetworkScanner().ScanHostPortsAsync(
            IPAddress.Loopback, ports, null, default);
        Assert.Equal(new[] { port }, open);

        listener.Stop();
    }

    [Fact]
    public async Task NET05_DeepScanFindsOpenPortOnLoopback()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var seen = 0;
        var progress = new IntSyncProgress(n => seen = n);

        // 只扫监听端口附近的小窗口（随机端口可能是高位端口，扫 1..port 会过慢）
        var open = await new NetworkScanner().ScanHostPortsAsync(
            IPAddress.Loopback, port - 3, port + 3, progress, default);

        Assert.Single(open);
        Assert.Equal(port, open[0]);
        Assert.Equal(7, seen);

        listener.Stop();
    }

    [Fact]
    public async Task NET05_DeepScanWithCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Record.ExceptionAsync(async () =>
            await new NetworkScanner().ScanHostPortsAsync(
                IPAddress.Loopback, 1, 100, null, cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }
}
