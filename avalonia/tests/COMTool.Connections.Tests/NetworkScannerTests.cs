using System.Net;
using System.Net.Sockets;
using System.Threading;
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

    private sealed class HostSyncProgress : IProgress<NetworkScanHost>
    {
        private readonly Action<NetworkScanHost> _action;
        public HostSyncProgress(Action<NetworkScanHost> action) => _action = action;
        public void Report(NetworkScanHost value) => _action(value);
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
        Assert.True(NetworkScanTargets.TryParseIp("192.0.2.163", out var ip, out _));
        Assert.Equal("192.0.2.163", ip.ToString());

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

        // 网络扫描只探测七个固定服务端口，不再探测“其他端口”集合
        Assert.Equal(
            new[] { 22, 3389, 5900, 21, 23, 513, 80 },
            NetworkServices.ServicePorts);
        Assert.DoesNotContain(443, NetworkServices.ServicePorts);
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
            Ips("127.0.0.1"), [port], progress, null, default);

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
            .ScanAsync(Ips("127.0.0.1"), [1], progress, null, default);

        foreach (var host in hosts)
        {
            Assert.DoesNotContain(1, host.OpenPorts);
        }
    }

    [Fact]
    public async Task NET05_DiscoveredReportedBeforeCompleted()
    {
        // discovered 通道：第一个存活信号（任一端口连通/ping 回复）先上报，
        // completed 通道：全部端口 + 名称解析完才上报（2026-10-29 增量显示）
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var events = new List<string>();
        var progress = new SyncProgress(p =>
        {
            if (p.Host is not null)
            {
                events.Add("C");
            }
        });
        var discovered = new HostSyncProgress(_ => events.Add("D"));

        await new NetworkScanner().ScanAsync(
            Ips("127.0.0.1"), [port, port + 1], progress, discovered, default);

        Assert.Contains("D", events);
        Assert.Contains("C", events);
        Assert.True(events.IndexOf("C") > events.IndexOf("D"),
            $"discovered must precede completed: {string.Join(",", events)}");
        listener.Stop();
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
                Ips("127.0.0.1", "127.0.0.2"), [22], progress, null, cts.Token));

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

        // 进度由 7 个线程池任务同步回调写入 seen（跨线程非原子写）。await 返回后
        // 直接读 seen 可能读到中间值（store 顺序/可见性竞态，整库高负载下偶发失败）。
        // 改为原子取最大值：扫描保证上报 done==total(=7)，故终值恒为 7，测试确定性通过。
        int seen = 0;
        var progress = new IntSyncProgress(n =>
        {
            int cur;
            do { cur = Volatile.Read(ref seen); }
            while (n > cur && Interlocked.CompareExchange(ref seen, n, cur) != cur);
        });

        // 只扫监听端口附近的小窗口（随机端口可能是高位端口，扫 1..port 会过慢）
        var open = await new NetworkScanner().ScanHostPortsAsync(
            IPAddress.Loopback, port - 3, port + 3, progress, default);

        Assert.Single(open);
        Assert.Equal(port, open[0]);
        Assert.Equal(7, Volatile.Read(ref seen));

        listener.Stop();
    }

    [Fact]
    public async Task NET05_ScanManyTargetsReturnsPromptlyOnCallerThread()
    {
        // TEST-NET-1（192.0.2.0/24）保证不可达：254 个目标全部走超时路径。
        // 探测任务体（socket 创建等）必须在线程池执行，调用方（UI 线程）的
        // 同步部分要立即返回——否则点击 Start scan 会卡住界面数秒。
        Assert.True(
            NetworkScanTargets.TryParseSubnet("192", "0", "2", "1", "254", out var targets, out var error),
            $"unexpected parse failure: {error}");
        var scanner = new NetworkScanner { PingTimeoutMs = 200, ConnectTimeoutMs = 200 };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var scan = scanner.ScanAsync(targets, new[] { 22 }, null, null, default);
        stopwatch.Stop();
        var blockedMs = stopwatch.ElapsedMilliseconds;

        Assert.True(blockedMs < 500,
            $"ScanAsync blocked the caller for {blockedMs} ms (work must be deferred to the pool)");
        Assert.False(scan.IsCompleted);
        await scan;
    }

    [Fact]
    public void NET05_NormalizeReverseNameStripsLocalSuffixForShortNames()
    {
        var ip90 = IPAddress.Parse("192.0.2.90");
        // Windows DNS client NetBIOS integration returns "X.local"; strip to short name
        Assert.Equal("devuser", NetworkScanner.NormalizeReverseName("devuser.local", ip90));
        // real FQDN untouched
        Assert.Equal("winhost1.example.com",
            NetworkScanner.NormalizeReverseName("winhost1.example.com", ip90));
        // IP echo / empty are not names
        Assert.Null(NetworkScanner.NormalizeReverseName("192.0.2.90", ip90));
        Assert.Null(NetworkScanner.NormalizeReverseName("", ip90));
        Assert.Null(NetworkScanner.NormalizeReverseName(null, ip90));
        // exactly 15 chars (NetBIOS limit): stripped
        Assert.Equal("abcdefghijklmno",
            NetworkScanner.NormalizeReverseName("abcdefghijklmno.local", ip90));
        // dotted or >15 chars: keep as-is
        Assert.Equal("abcdefghijklmnop.local",
            NetworkScanner.NormalizeReverseName("abcdefghijklmnop.local", ip90));
        Assert.Equal("my.host.local",
            NetworkScanner.NormalizeReverseName("my.host.local", ip90));
    }

    [Fact]
    public void NET05_ScanDefaultsTargetTwoSecondSubnetScan()
    {
        // MobaXterm 的 /24 扫描 2 秒内出结果：全部 254 个目标一波并行探测，
        // 各超时都是几百毫秒级（实测 300/300/256 组合约 1 秒）。
        var scanner = new NetworkScanner();
        Assert.True(scanner.Concurrency >= 254,
            "concurrency must probe the whole /24 in one wave");
        Assert.True(scanner.PingTimeoutMs <= 500);
        Assert.True(scanner.ConnectTimeoutMs <= 500);
        Assert.True(scanner.DnsTimeoutMs <= 6000,
            "reverse DNS must wait for slow LAN DNS servers (4680 ms measured on user VDI)");
        Assert.True(scanner.NetbiosTimeoutMs <= 500);
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

    /// <summary>
    /// Real NBSTAT node-status reply captured from a Windows host on the LAN
    /// (192.0.2.163): WINHOST1 station + file server, WORKGROUP workgroup.
    /// </summary>
    private const string NbstatCaptureHex =
        "f92f8400000000010000000020434b4141414141414141414141414141414141414141" +
        "4141414141414141414100002100010000000000650357494e484f5354312020202020" +
        "202000040057494e484f53543120202020202020200400574f524b47524f5550202020" +
        "202020008400fefcfe1ce0280000000000000000000000000000000000000000000000" +
        "0000000000000000000000000000000000";

    [Fact]
    public void NET05_NetbiosNameParsesCapturedReply()
    {
        var response = Convert.FromHexString(NbstatCaptureHex);

        // 取工作站名（后缀 0x00），不取工作组名，并转小写（MobaXterm 显示形式）
        Assert.Equal("winhost1", NetbiosNameResolver.ParseNodeStatusResponse(response, 0xf92f));

        // 事务 ID 不匹配的应答必须忽略，避免串包
        Assert.Null(NetbiosNameResolver.ParseNodeStatusResponse(response, 0x1234));
    }

    [Fact]
    public async Task NET05_NetbiosQueryResolvesStationNameOverUdp()
    {
        // 名称列兜底：反向 DNS 无 PTR 时，UDP 137 NBSTAT 返回工作站名。
        // 组名（工作组/域，flags 0x8000）必须被忽略。
        var name = await QueryFakeNbstatAsync(
            ("WINHOST1", 0x00, 0x0400),
            ("WORKGROUP", 0x00, 0x8400));

        Assert.Equal("winhost1", name);
    }

    [Fact]
    public async Task NET05_NetbiosQueryIgnoresGroupNamesOnly()
    {
        var name = await QueryFakeNbstatAsync(("WORKGROUP", 0x00, 0x8400));

        Assert.Null(name);
    }

    [Fact]
    public async Task NET05_NetbiosQueryReturnsNullWithoutAnswer()
    {
        var port = FreeUdpPort();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var name = await NetbiosNameResolver.QueryAsync(
            IPAddress.Loopback, 150, default, port);

        Assert.Null(name);
        // 兜底查询必须有上限，不能拖慢 /24 扫描
        Assert.True(stopwatch.ElapsedMilliseconds < 2000,
            $"NetBIOS fallback took {stopwatch.ElapsedMilliseconds} ms");
    }

    private static int FreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    /// <summary>
    /// Runs <see cref="NetbiosNameResolver.QueryAsync"/> against a local UDP
    /// responder that answers the NBSTAT request with <paramref name="names"/>.
    /// </summary>
    private static async Task<string?> QueryFakeNbstatAsync(
        params (string Name, byte Suffix, ushort Flags)[] names)
    {
        using var responder = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        responder.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)responder.LocalEndPoint!).Port;

        var reply = Task.Run(async () =>
        {
            var buffer = new byte[512];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            var received = await responder.ReceiveFromAsync(buffer, SocketFlags.None, from);
            var transactionId = (ushort)((buffer[0] << 8) | buffer[1]);
            var response = BuildNbstatResponse(transactionId, names);
            await responder.SendToAsync(response, SocketFlags.None, received.RemoteEndPoint);
        });

        var name = await NetbiosNameResolver.QueryAsync(
            IPAddress.Loopback, 2000, default, port);
        await reply;
        return name;
    }

    private static byte[] BuildNbstatResponse(
        ushort transactionId,
        IReadOnlyList<(string Name, byte Suffix, ushort Flags)> names)
    {
        var rdata = new List<byte> { (byte)names.Count };
        foreach (var (name, suffix, flags) in names)
        {
            var raw = new byte[15];
            Array.Fill(raw, (byte)' ');
            var text = System.Text.Encoding.ASCII.GetBytes(name);
            Array.Copy(text, raw, Math.Min(raw.Length, text.Length));
            rdata.AddRange(raw);
            rdata.Add(suffix);
            rdata.Add((byte)(flags >> 8));
            rdata.Add((byte)flags);
        }

        var packet = new List<byte>
        {
            (byte)(transactionId >> 8), (byte)transactionId,
            0x84, 0x00,             // response, rcode 0
            0, 0, 0, 1, 0, 0, 0, 0, // qd=0, an=1
            0x00,                   // answer name: root
            0x00, 0x21, 0x00, 0x01, // NBSTAT / IN
            0, 0, 0, 0x65,          // ttl
            (byte)(rdata.Count >> 8), (byte)rdata.Count,
        };
        packet.AddRange(rdata);
        return packet.ToArray();
    }
}
