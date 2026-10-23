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
}
