using System.Globalization;
using COMTool.Connections.RemoteMonitoring;
using Xunit;

namespace COMTool.Connections.Tests;

public sealed class RemoteMonitorTests
{
    /// <summary>
    /// Realistic output of the remote dump script on an Ubuntu host
    /// (spec 2026-10-29-remote-monitoring, section 3.2).
    /// </summary>
    private const string FullDump =
        "MON_HOST=winhost1\n" +
        "MON_CPU=cpu  382491 3327 112519 2454520 5706 0 2068 0 0 0\n" +
        "MON_MEM=MemTotal:       16346496 kB|MemAvailable:     8321232 kB|\n" +
        "MON_UP=1234567.89 2345678.90\n" +
        "MON_NET=987654321 123456789\n" +
        "MON_DF=49% /|1% /boot/efi|82% /media/sangfor/vdb\n" +
        "MON_END\n";

    [Fact]
    public void RM01_DumpParsesFullSample()
    {
        var dump = RemoteMetricsParser.Dump(FullDump);

        Assert.True(dump.HasAnySection);
        Assert.Equal("winhost1", dump.Host);
        Assert.NotNull(dump.Cpu);
        // idle = 2454520 + 5706 (iowait), total = sum of all fields
        Assert.Equal((2460226L, 2960631L), dump.Cpu!.Value);
        // used = MemTotal - MemAvailable = 16346496 - 8321232
        Assert.Equal((16346496L, 8025264L), dump.Mem!.Value);
        Assert.Equal((987654321L, 123456789L), dump.Net!.Value);
        Assert.Equal(1234567.89, dump.UptimeSeconds!.Value, 2);
        Assert.NotNull(dump.Partitions);
        Assert.Equal(3, dump.Partitions!.Length);
        Assert.Equal(("/", 49), dump.Partitions[0]);
        Assert.Equal(("/boot/efi", 1), dump.Partitions[1]);
        Assert.Equal(("/media/sangfor/vdb", 82), dump.Partitions[2]);
    }

    [Fact]
    public void RM01_DumpToleratesCrlfAndEmptyValues()
    {
        var dump = RemoteMetricsParser.Dump("MON_HOST=\r\nMON_CPU=cpu 100 0 50 400 0\r\nMON_END\r\n");

        Assert.Null(dump.Host);
        Assert.NotNull(dump.Cpu);
        // idle = 400 (+ 0 iowait), total = 100+0+50+400+0 = 550
        Assert.Equal((400L, 550L), dump.Cpu!.Value);
    }

    [Fact]
    public void RM01_DumpNoSectionsHasNone()
    {
        // A non-Linux shell (e.g. Windows OpenSSH) produces output with no
        // parseable MON_ sections; the parser must not throw and reports
        // HasAnySection = false.
        var dump = RemoteMetricsParser.Dump("hostname: win\n'echo' is not recognized as a command\n");

        Assert.False(dump.HasAnySection);
        Assert.Null(dump.Host);
        Assert.Null(dump.Cpu);
        Assert.Null(dump.Partitions);
    }

    [Fact]
    public void RM01_DumpIgnoresMalformedSections()
    {
        var dump = RemoteMetricsParser.Dump("MON_NET=abc\nMON_DF=52%\nMON_MEM=MemTotal: 100 kB\n");

        Assert.Null(dump.Net);          // not two integers
        Assert.Null(dump.Partitions);   // no mount after the percent
        Assert.Null(dump.Mem);          // MemAvailable missing
    }

    [Fact]
    public void RM01_CpuDeltaFirstSampleNull()
    {
        Assert.Null(RemoteMetricsParser.CpuDelta(null, (100, 1000)));
        Assert.Null(RemoteMetricsParser.CpuDelta((100, 1000), null));
    }

    [Fact]
    public void RM01_CpuDeltaComputesPercent()
    {
        // dTotal = 600, dIdle = 300 -> 100 * 300 / 600 = 50%
        var pct = RemoteMetricsParser.CpuDelta((100, 1000), (400, 1600));

        Assert.NotNull(pct);
        Assert.Equal(50.0, pct!.Value, 6);
    }

    [Fact]
    public void RM01_CpuDeltaNonMonotonicNull()
    {
        Assert.Null(RemoteMetricsParser.CpuDelta((400, 1600), (100, 1000))); // counters reset
        Assert.Null(RemoteMetricsParser.CpuDelta((100, 1000), (100, 1000))); // no elapsed time
    }

    [Fact]
    public void RM01_NetRateExcludesNegativeDelta()
    {
        // rx decreased (NIC reset) -> download clamped to 0
        // tx: (2501000 - 1000) * 8 / 1e6 / 2s = 10.0 Mb/s
        var (up, down) = RemoteMetricsParser.NetRate((1000, 1000), (999, 2501000), 2.0);

        Assert.Equal(10.0, up, 6);
        Assert.Equal(0.0, down, 9);
    }

    [Fact]
    public void RM01_NetRateSmallDtZero()
    {
        var (up, down) = RemoteMetricsParser.NetRate((0, 0), (1000000, 1000000), 0.01);

        Assert.Equal(0.0, up);
        Assert.Equal(0.0, down);
    }

    [Fact]
    public void RM01_FormatUptimeUnits()
    {
        Assert.Equal("38 days", RemoteMetricsParser.FormatUptime(TimeSpan.FromDays(38) + TimeSpan.FromHours(5)));
        Assert.Equal("1 day", RemoteMetricsParser.FormatUptime(TimeSpan.FromDays(1)));
        Assert.Equal("5 hours", RemoteMetricsParser.FormatUptime(TimeSpan.FromHours(5).Add(TimeSpan.FromMinutes(2))));
        Assert.Equal("9 min", RemoteMetricsParser.FormatUptime(TimeSpan.FromMinutes(9)));
        Assert.Equal("30 s", RemoteMetricsParser.FormatUptime(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void RM01_FormatMatchesMobaXtermSample()
    {
        var snap = new MonitorSnapshot
        {
            CpuPercent = 1.4,
            Ram = (6.05, 15.61),
            NetUpMbps = 0.01,
            NetDownMbps = 0.0,
            Uptime = TimeSpan.FromDays(38),
            Partitions = new (string Mount, int UsePct)[]
            {
                ("/", 49), ("/boot/efi", 1), ("/media/sangfor/vdb", 82),
            },
        };

        // Segment separator is two spaces, matching the MobaXterm bar spacing
        // (user screenshot 2026-10-29); inside the net segment a single space.
        Assert.Equal(
            "1%  6.05 GB / 15.61 GB  \u21910.01Mb/s \u21930.00Mb/s  38 days  " +
            "/: 49% /boot/efi: 1% /media/sangfor/vdb: 82%",
            snap.Format(MonitorOptions.Default));
    }

    [Fact]
    public void RM01_FormatOmitsDisabledAndMissing()
    {
        var o = new MonitorOptions
        {
            ShowHost = false,
            ShowCpu = false,
            ShowRam = false,
            ShowNetUp = true,
            ShowNetDown = false,
            ShowUptime = false,
            ShowPartitions = false,
        };
        var snap = new MonitorSnapshot
        {
            Host = "host",
            CpuPercent = 50,
            Ram = (1, 2),
            NetUpMbps = 1.5,
            NetDownMbps = 2.5,
            Uptime = TimeSpan.FromDays(38),
            Partitions = new (string Mount, int UsePct)[] { ("/", 49) },
        };

        Assert.Equal("\u21911.50Mb/s", snap.Format(o));
    }

    [Fact]
    public void RM01_FormatEmptyWhenNothingEnabled()
    {
        var o = new MonitorOptions
        {
            ShowHost = false,
            ShowCpu = false,
            ShowRam = false,
            ShowNetUp = false,
            ShowNetDown = false,
            ShowUptime = false,
            ShowPartitions = false,
        };
        var snap = new MonitorSnapshot
        {
            Host = "h",
            CpuPercent = 1,
            Ram = (1, 2),
            NetUpMbps = 1,
            NetDownMbps = 1,
            Uptime = TimeSpan.FromDays(1),
            Partitions = new (string Mount, int UsePct)[] { ("/", 1) },
        };

        Assert.Equal(string.Empty, snap.Format(o));
    }

    private static string MakeDump(string cpuLine, string netLine)
    {
        return "MON_HOST=testhost\n" +
               "MON_CPU=" + cpuLine + "\n" +
               "MON_MEM=MemTotal:       16384000 kB|MemAvailable:     8388608 kB|\n" +
               "MON_UP=100000.00 0.00\n" +
               "MON_NET=" + netLine + "\n" +
               "MON_DF=49% /\n" +
               "MON_END\n";
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(10);
        }

        return;
    }

    [Fact]
    public async Task RM01_EngineEmitsSnapshotPerPoll()
    {
        var outputs = new Queue<string>();
        outputs.Enqueue(MakeDump("cpu 200 0 0 800 0 0 0 0 0 0", "0 0"));
        // dTotal=400, dIdle=300 -> 25%; dRx=2_000_000 bytes over ~0.1s -> ~160 Mb/s
        outputs.Enqueue(MakeDump("cpu 300 0 0 1100 0 0 0 0 0 0", "2000000 0"));
        for (var i = 0; i < 3; i++)
        {
            outputs.Enqueue(MakeDump("cpu 300 0 0 1100 0 0 0 0 0 0", "2000000 0"));
        }

        var monitor = new RemoteMonitor(
            () => Task.FromResult(outputs.Count > 0 ? outputs.Dequeue() : ""),
            TimeSpan.FromMilliseconds(100));
        var snaps = new List<MonitorSnapshot>();
        monitor.SnapshotUpdated += s =>
        {
            lock (snaps)
            {
                snaps.Add(s);
            }
        };

        monitor.Start();
        await WaitForAsync(() =>
        {
            lock (snaps)
            {
                return snaps.Count >= 2;
            }
        }, 5000);
        monitor.Stop();

        MonitorSnapshot first;
        MonitorSnapshot second;
        lock (snaps)
        {
            Assert.True(snaps.Count >= 2);
            first = snaps[0];
            second = snaps[1];
        }

        Assert.Null(first.CpuPercent);       // no baseline yet
        Assert.Equal(0.0, first.NetDownMbps!.Value); // first net sample is 0 by definition
        Assert.NotNull(first.Ram);
        Assert.NotNull(first.Uptime);
        Assert.Equal(25.0, second.CpuPercent!.Value, 1);
        Assert.NotNull(second.NetDownMbps);
        Assert.True(second.NetDownMbps! > 10);
        Assert.Equal(0.0, second.NetUpMbps!.Value);
    }

    [Fact]
    public async Task RM01_EngineStopsAfterTwoConsecutiveFailures()
    {
        var calls = 0;
        string? failReason = null;
        var monitor = new RemoteMonitor(async () =>
        {
            var n = System.Threading.Interlocked.Increment(ref calls);
            if (n == 1)
            {
                return MakeDump("cpu 200 0 0 800 0 0 0 0 0 0", "0 0");
            }

            throw new System.IO.IOException("boom");
        }, TimeSpan.FromMilliseconds(20));
        monitor.Failed += reason => failReason = reason;

        monitor.Start();
        await WaitForAsync(() => !monitor.IsRunning, 5000);

        Assert.False(monitor.IsRunning);
        Assert.Equal("transport", failReason);
    }

    [Fact]
    public async Task RM01_EngineSurvivesSingleTransientFailure()
    {
        var calls = 0;
        int snapCount = 0;
        string? failReason = null;
        var monitor = new RemoteMonitor(() =>
        {
            var n = System.Threading.Interlocked.Increment(ref calls);
            if (n == 2)
            {
                throw new System.IO.IOException("transient");
            }

            return Task.FromResult(MakeDump("cpu 200 0 0 800 0 0 0 0 0 0", "0 0"));
        }, TimeSpan.FromMilliseconds(20));
        monitor.SnapshotUpdated += _ => System.Threading.Interlocked.Increment(ref snapCount);
        monitor.Failed += reason => failReason = reason;

        monitor.Start();
        await WaitForAsync(() => System.Threading.Volatile.Read(ref snapCount) >= 2, 5000);
        await Task.Delay(100); // give the transient failure a chance to be processed

        Assert.Null(failReason);
        Assert.True(monitor.IsRunning);
        monitor.Stop();
    }

    [Fact]
    public async Task RM01_EngineStopsOnUnsupportedDump()
    {
        string? failReason = null;
        var monitor = new RemoteMonitor(
            () => Task.FromResult("hostname: win\n'echo' is not recognized as a command\n"),
            TimeSpan.FromMilliseconds(20));
        monitor.Failed += reason => failReason = reason;

        monitor.Start();
        await WaitForAsync(() => !monitor.IsRunning, 5000);

        Assert.False(monitor.IsRunning);
        Assert.Equal("unsupported", failReason);
    }

    [Fact]
    public void RM01_EngineStopIsIdempotentAndCancels()
    {
        var monitor = new RemoteMonitor(
            () => Task.Delay(50).ContinueWith(_ => MakeDump("cpu 200 0 0 800 0 0 0 0 0 0", "0 0")),
            TimeSpan.FromMilliseconds(20));
        monitor.Start();
        monitor.Stop();
        monitor.Stop();
        monitor.Dispose();

        Assert.False(monitor.IsRunning);
    }

    [Fact]
    public async Task RM01_EngineDoesNotFireFailedWhenStoppedWhileBlocked()
    {
        string? failReason = null;
        var monitor = new RemoteMonitor(async () =>
        {
            await Task.Delay(500);
            throw new System.IO.IOException("late failure");
        }, TimeSpan.FromMilliseconds(20));
        monitor.Failed += reason => failReason = reason;

        monitor.Start();
        await Task.Delay(50); // first poll now in flight
        monitor.Stop();
        await Task.Delay(600); // the in-flight poll fails after the stop

        Assert.False(monitor.IsRunning);
        Assert.Null(failReason);
    }

    [Fact]
    public void RM01_FormatCpuClampsToHundred()
    {
        var snap = new MonitorSnapshot { CpuPercent = 142.7 };
        var o = new MonitorOptions();
        o.ShowHost = o.ShowRam = o.ShowNetUp = o.ShowNetDown = o.ShowUptime = o.ShowPartitions = false;

        Assert.Equal("100%", snap.Format(o));
    }
}
