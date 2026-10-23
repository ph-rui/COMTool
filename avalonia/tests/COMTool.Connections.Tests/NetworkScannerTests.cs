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
    public void NET05_ParsesSingleCidrRangeAndCommaLists()
    {
        var cases = new (string Input, string[] Expected)[]
        {
            ("192.168.1.5", new[] { "192.168.1.5" }),
            ("10.0.0.1, 10.0.0.2", new[] { "10.0.0.1", "10.0.0.2" }),
            ("192.168.1.10-13", new[] { "192.168.1.10", "192.168.1.11", "192.168.1.12", "192.168.1.13" }),
            ("192.168.1.10-192.168.1.11", new[] { "192.168.1.10", "192.168.1.11" }),
        };
        foreach (var (input, expectedIps) in cases)
        {
            Assert.True(NetworkScanTargets.TryParse(input, out var targets, out var error),
                $"parse failed '{input}': {error}");
            Assert.Equal(expectedIps.Length, targets.Count);
            for (var i = 0; i < expectedIps.Length; i++)
            {
                Assert.Equal(expectedIps[i], targets[i].ToString());
            }
        }
    }

    [Fact]
    public void NET05_CidrExpandsToAllHosts()
    {
        foreach (var input in new[] { "192.168.1.0/24", "10.0.0.0/24" })
        {
            Assert.True(NetworkScanTargets.TryParse(input, out var targets, out var error),
                $"parse failed '{input}': {error}");
            Assert.Equal(256, targets.Count);
            Assert.Equal(input.Split('/')[0], targets[0].ToString());
        }
    }

    [Fact]
    public void NET05_RangeExpandsToAllHosts()
    {
        Assert.True(NetworkScanTargets.TryParse("10.0.0.10-10.0.0.12", out var targets, out var error),
            $"parse failed: {error}");
        Assert.Equal(3, targets.Count);
        Assert.Equal("10.0.0.10", targets[0].ToString());
        Assert.Equal("10.0.0.12", targets[2].ToString());
    }

    [Fact]
    public void NET05_RejectsInvalidTargets()
    {
        var invalid = new[]
        {
            "",
            "   ",
            "not-an-ip",
            "192.168.1",
            "192.168.1.256",
            "192.168.1.0/33",
            "10.0.0.1-10.0.0.2000",
            "192.168.1.50-192.168.1.10",
            "fe80::1",
        };
        foreach (var input in invalid)
        {
            Assert.False(NetworkScanTargets.TryParse(input, out _, out var error),
                $"unexpectedly accepted '{input}'");
            Assert.NotNull(error);
        }
    }

    [Fact]
    public void NET05_CapsHostCountAt1024()
    {
        // /23 = 512 hosts is allowed; /22 = 1024 is right at the cap: allowed.
        Assert.True(NetworkScanTargets.TryParse("10.0.0.0/23", out _, out var okErr));
        Assert.Null(okErr);
        Assert.True(NetworkScanTargets.TryParse("10.0.0.0/22", out _, out var atCapErr));
        Assert.Null(atCapErr);

        // /21 = 2048 hosts exceeds the cap.
        Assert.False(NetworkScanTargets.TryParse("10.0.0.0/21", out _, out var overErr));
        Assert.NotNull(overErr);

        // 1024 + 512 = 1536 > cap when combined.
        Assert.False(NetworkScanTargets.TryParse("10.0.0.0/22,10.4.0.0/23", out _, out _));
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
    public void NET05_StartEndRangeExpandsToAllHosts()
    {
        Assert.True(
            NetworkScanTargets.TryParse("127.0.0.1", "127.0.0.5", out var targets, out var error),
            $"parse failed: {error}");
        Assert.Equal(5, targets.Count);
        Assert.Equal("127.0.0.1", targets[0].ToString());
        Assert.Equal("127.0.0.5", targets[4].ToString());

        // 结束地址留空时保持旧单字段全部格式
        Assert.True(NetworkScanTargets.TryParse("192.168.1.0/24", "", out var cidr, out _));
        Assert.Equal(256, cidr.Count);

        Assert.True(NetworkScanTargets.TryParse("10.0.0.1, 10.0.0.9", "", out var list, out _));
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void NET05_StartEndRejectsInvalid()
    {
        // 两字段皆空
        Assert.False(NetworkScanTargets.TryParse("", "", out _, out var e1));
        Assert.NotNull(e1);

        // 起始不是 IP
        Assert.False(NetworkScanTargets.TryParse("not-an-ip", "10.0.0.2", out _, out var e2));
        Assert.NotNull(e2);

        // 起始填 CIDR/范围 且结束地址非空 → 冲突
        Assert.False(NetworkScanTargets.TryParse("192.168.1.0/24", "192.168.1.20", out _, out var e3));
        Assert.NotNull(e3);

        // 结束小于起始
        Assert.False(NetworkScanTargets.TryParse("192.168.1.50", "10.0.0.10", out _, out var e4));
        Assert.NotNull(e4);
        Assert.False(NetworkScanTargets.TryParse("192.168.1.50", "192.168.1.10", out _, out var e4b));
        Assert.NotNull(e4b);

        // 范围超 1024 上限 (10.0.0.1..10.0.5.1 = 1281 台)
        Assert.False(NetworkScanTargets.TryParse("10.0.0.1", "10.0.5.1", out _, out var e5));
        Assert.NotNull(e5);
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
