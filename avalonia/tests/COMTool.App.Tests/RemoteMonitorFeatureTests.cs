using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Connections.RemoteMonitoring;
using COMTool.Sftp.Testing;
using Xunit;

namespace COMTool.App.Tests;

public sealed class RemoteMonitorFeatureTests
{
    /// <summary>
    /// Fake IExecRunner: scripted dump output, call counter, no transport.
    /// </summary>
    private sealed class FakeExecRunner : IExecRunner, IDisposable
    {
        private readonly Func<Task<string>> _impl;
        private int _calls;

        public FakeExecRunner(Func<Task<string>> impl) => _impl = impl;

        public int Calls => Volatile.Read(ref _calls);

        public Task<string> RunCommandAsync(string command, int timeoutMs = 5000)
        {
            Interlocked.Increment(ref _calls);
            return _impl();
        }

        public void Dispose()
        {
        }
    }

    private static TerminalTab SshTab(FakeExecRunner runner, string sessionId = "s1") =>
        new()
        {
            Title = "t",
            SessionId = sessionId,
            Kind = SessionKind.Ssh,
            Content = new Border(),
            Connection = runner,
            IsTransportOpen = () => true,
            CloseAction = () => { },
        };

    private static string Dump(string cpuLine, string netLine) =>
        "MON_HOST=testhost\n" +
        $"MON_CPU={cpuLine}\n" +
        "MON_MEM=MemTotal:       16384000 kB|MemAvailable:     8388608 kB|\n" +
        "MON_UP=3456000.00 123456.78\n" +
        "MON_CLIENTS=1\n" +
        "MON_PROC=200\n" +
        "MON_FD=512\n" +
        "MON_WHO=root\n" +
        $"MON_NET={netLine}\n" +
        "MON_DF=49% /|82% /data\n" +
        "MON_END\n";

    /// <summary>Pump the headless dispatcher until the monitor line is non-empty.</summary>
    private static void WaitMonitorLine(MainViewModel vm, string? expectContains = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (string.IsNullOrEmpty(expectContains))
            {
                if (!string.IsNullOrEmpty(vm.MonitorText))
                {
                    return;
                }
            }
            else if (vm.MonitorText.Contains(expectContains))
            {
                return;
            }

            Thread.Sleep(20);
        }
    }

    [AvaloniaFact]
    public void RM04_ToggleOnSelectedSshTabStartsPollingAndEmitsText()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var vm = new MainViewModel();
            var runner = new FakeExecRunner(
                () => Task.FromResult(Dump("cpu 100 0 0 900 0", "1000 1000")));
            var tab = SshTab(runner);
            vm.AddTab(tab);

            vm.SetMonitorEnabled(tab, true);
            WaitMonitorLine(vm);

            Assert.True(vm.IsMonitorActive);
            Assert.Contains("testhost", vm.MonitorText);
            Assert.Contains("GB /", vm.MonitorText);
            Assert.Contains("/: 49%", vm.MonitorText);
            Assert.Contains("40 days", vm.MonitorText); // 3456000 s

            vm.SetMonitorEnabled(tab, false);
            Assert.False(vm.IsMonitorActive);
            Assert.Equal(string.Empty, vm.MonitorText);
            Dispatcher.UIThread.RunJobs();
        });
    }

    [AvaloniaFact]
    public void RM04_ToggleOnUnselectedTabDefersUntilSelected()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var vm = new MainViewModel();
            var runnerA = new FakeExecRunner(() => Task.FromResult(Dump("cpu 100 0 0 900 0", "0 0")));
            var runnerB = new FakeExecRunner(() => Task.FromResult(Dump("cpu 100 0 0 900 0", "0 0")));
            var tabA = SshTab(runnerA, "a");
            var tabB = SshTab(runnerB, "b");
            vm.AddTab(tabA);
            vm.AddTab(tabB);
            vm.SelectedTab = tabA;

            // Enable on the unselected tab: the flag is set but no polling starts.
            vm.SetMonitorEnabled(tabB, true);
            Assert.True(tabB.MonitorEnabled);
            Assert.Equal(0, runnerB.Calls);
            Thread.Sleep(100);
            Assert.Equal(0, runnerB.Calls);

            // Selecting the tab resumes the poller.
            vm.SelectedTab = tabB;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (runnerB.Calls == 0 && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }

            Assert.True(runnerB.Calls > 0);
            Assert.Equal(0, runnerA.Calls);
            vm.SetMonitorEnabled(tabB, false);
        });
    }

    [AvaloniaFact]
    public void RM04_SwitchTabStopsPreviousPoll()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var vm = new MainViewModel();
            var runnerA = new FakeExecRunner(() => Task.FromResult(Dump("cpu 100 0 0 900 0", "0 0")));
            var runnerB = new FakeExecRunner(() => Task.FromResult(Dump("cpu 100 0 0 900 0", "0 0")));
            var tabA = SshTab(runnerA, "a");
            var tabB = SshTab(runnerB, "b");
            vm.AddTab(tabA);
            vm.AddTab(tabB);
            vm.SelectedTab = tabA;

            vm.SetMonitorEnabled(tabA, true);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (runnerA.Calls == 0 && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }

            Assert.True(runnerA.Calls > 0);

            // Switch away: the poller must stop (no further polls within one
            // 2 s interval window plus margin).
            vm.SelectedTab = tabB;
            Dispatcher.UIThread.RunJobs();
            var frozen = runnerA.Calls;
            Thread.Sleep(2500);
            Assert.Equal(frozen, runnerA.Calls);
            Assert.Equal(0, runnerB.Calls);

            vm.MonitorStopForTab(tabA);
        });
    }

    [AvaloniaFact]
    public void RM04_SessionStopStopsMonitorAndRestartResumes()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var vm = new MainViewModel();
            var runner = new FakeExecRunner(() => Task.FromResult(Dump("cpu 100 0 0 900 0", "0 0")));
            var tab = SshTab(runner);
            vm.AddTab(tab);

            vm.SetMonitorEnabled(tab, true);
            WaitMonitorLine(vm);
            Assert.True(vm.IsMonitorActive);

            // Session stopped (shell exited): polling stops, the per-tab
            // flag stays on so an R restart resumes automatically.
            vm.MonitorStopForTab(tab);
            Assert.False(vm.IsMonitorActive);
            Assert.Equal(string.Empty, vm.MonitorText);
            Assert.True(tab.MonitorEnabled);

            vm.MonitorOnTransportAttached(tab);
            WaitMonitorLine(vm);
            Assert.True(vm.IsMonitorActive);

            vm.SetMonitorEnabled(tab, false);
        });
    }

    [Fact]
    public void RM04_NonSshTabToggleIsNoOp()
    {
        var vm = new MainViewModel();
        var tab = TestTab.Create(sessionId: "c1", kind: SessionKind.Serial);
        vm.AddTab(tab);

        vm.SetMonitorEnabled(tab, true);

        Assert.False(tab.MonitorEnabled);
        Assert.False(vm.IsMonitorActive);
        Assert.Equal(string.Empty, vm.MonitorText);
    }

    [AvaloniaFact]
    public void RM04_MenuOptionChangeReformatsLineImmediately()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var vm = new MainViewModel();
            var runner = new FakeExecRunner(() => Task.FromResult(Dump("cpu 50 0 0 150 0", "2000 1000")));
            var tab = SshTab(runner);
            vm.AddTab(tab);

            vm.SetMonitorEnabled(tab, true);
            WaitMonitorLine(vm);
            Assert.Contains("/: 49%", vm.MonitorText);
            Assert.Contains("testhost", vm.MonitorText);

            // Uncheck 分区信息 + 显示主机信息: the line reformats immediately,
            // without waiting for the next 2 s poll.
            vm.MonitorMenu.SetOption("partitions", false);
            vm.MonitorMenu.SetOption("host", false);
            vm.ReformatMonitorLine();

            Assert.DoesNotContain("/: 49%", vm.MonitorText);
            Assert.DoesNotContain("testhost", vm.MonitorText);
            Assert.Contains("GB /", vm.MonitorText);
            Assert.True(vm.MonitorMenu.IsOption("cpu"));
            Assert.False(vm.MonitorMenu.IsOption("partitions"));
            Assert.False(vm.MonitorMenu.IsOption("host"));

            vm.SetMonitorEnabled(tab, false);
        });
    }

    [Fact]
    public void RM04_SftpPaneButtonToggleFiresCallbackOnce()
    {
        var pane = new SftpPaneViewModel(() => throw new InvalidOperationException("no remote in unit test"));
        var events = new List<bool>();
        pane.MonitorToggled = v => events.Add(v);

        pane.IsMonitorOn = true;
        Assert.Single(events);
        Assert.True(events[0]);

        // Programmatic sync (tab switch) must not fire the callback.
        pane.SyncMonitorOn(true);
        Assert.Single(events);
        pane.SyncMonitorOn(false);
        Assert.Single(events);
        Assert.False(pane.IsMonitorOn);

        pane.IsMonitorOn = true; // user toggles again
        Assert.Equal(2, events.Count);
        Assert.True(events[1]);
    }

    [Fact]
    public void RM04_MenuStateMapsToConfigRoundTrip()
    {
        var state = new MonitorMenuState();
        Assert.True(state.IsOption("host"));
        Assert.True(state.IsOption("cpu"));
        Assert.True(state.IsOption("uptime"));
        Assert.True(state.IsOption("netdown"));

        state.SetOption("netdown", false);
        state.SetOption("cpu", false);

        var cfg = new COMTool.Core.Config.RemoteMonitorConfig();
        state.SaveTo(cfg);
        var reloaded = new MonitorMenuState();
        reloaded.LoadFrom(cfg);

        Assert.False(reloaded.IsOption("netdown"));
        Assert.False(reloaded.IsOption("cpu"));
        Assert.True(reloaded.IsOption("partitions"));
    }

    [AvaloniaFact]
    public void RM04_StatusBarMenuHas9ItemsWithAllSevenCheckedByDefault()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var vm = new MainViewModel();
            var window = new COMTool.App.Views.MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var menu = window.FindControl<Avalonia.Controls.ContextMenu>("MonitorBarMenu");
            Assert.NotNull(menu);
            // 复制 / 保存到文件 / separator / 7 checkable options:
            // 9 functional items + 1 Separator = 10 entries.
            Assert.Equal(10, menu.Items.Count);
            var items = menu.Items.OfType<Avalonia.Controls.MenuItem>().ToList();
            Assert.Equal(9, items.Count); // 10 entries minus the Separator
            Assert.Equal("复制(O)", items[0].Header);
            Assert.Equal("保存到文件(P)", items[1].Header);
            var checkable = items.Skip(2).ToList();
            Assert.Equal(7, checkable.Count);
            foreach (var mi in checkable)
            {
                Assert.Equal(Avalonia.Controls.MenuItemToggleType.CheckBox, mi.ToggleType);
            }
            var tags = checkable.Select(mi => (string)mi.Tag!).ToList();
            Assert.Equal(
                new[] { "host", "cpu", "ram", "netup", "netdown", "uptime", "partitions" },
                tags);

            // Headless cannot present the ContextMenu popup (Opening fires at
            // presentation time), so verify the wiring and sync logic
            // deterministically: the MainWindow constructor must have
            // subscribed OnMonitorMenuOpening to this menu's Opening event,
            // and invoking that exact delegate must sync the check states
            // from the view model (7 of 11 on by default, per MobaXterm 22.1).
            var field = typeof(Avalonia.Controls.ContextMenu)
                .GetField("Opening", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            var del = field!.GetValue(menu) as System.Delegate;
            Assert.True(del is not null, "no handlers subscribed to MonitorBarMenu.Opening");
            var handler = del!.GetInvocationList().FirstOrDefault(x =>
                x.Method.Name == "OnMonitorMenuOpening" && ReferenceEquals(x.Target, window));
            Assert.NotNull(handler);
            Assert.True(handler is not null,
                "MainWindow ctor did not subscribe OnMonitorMenuOpening to MonitorBarMenu.Opening");
            handler.DynamicInvoke(menu, new System.ComponentModel.CancelEventArgs());
            var byTag = tags.ToDictionary(
                t => t,
                t => checkable.First(mi => (string)mi.Tag! == t));
            Assert.True(byTag["host"].IsChecked);
            Assert.True(byTag["cpu"].IsChecked);
            Assert.True(byTag["ram"].IsChecked);
            Assert.True(byTag["netup"].IsChecked);
            Assert.True(byTag["netdown"].IsChecked);
            Assert.True(byTag["uptime"].IsChecked);
            Assert.True(byTag["partitions"].IsChecked);

            // Toggle one off via the view model (the same path the menu
            // click handler takes) and re-open: check states follow the VM.
            vm.MonitorMenu.SetOption("partitions", false);
            vm.ReformatMonitorLine();
            handler.DynamicInvoke(menu, new System.ComponentModel.CancelEventArgs());
            Assert.False(vm.MonitorMenu.IsOption("partitions"));
            Assert.False(byTag["partitions"].IsChecked);
            Assert.True(byTag["host"].IsChecked);
            window.Close();
        });
    }

    [AvaloniaFact]
    public void RM04_StatusBarGridHasBackgroundForContextMenuHitTest()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var vm = new MainViewModel();
            var window = new COMTool.App.Views.MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var menu = window.FindControl<Avalonia.Controls.ContextMenu>("MonitorBarMenu");
            Assert.NotNull(menu);
            var host = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).ToList()
                .OfType<Avalonia.Controls.Control>()
                .FirstOrDefault(c => c.ContextMenu == menu);
            Assert.True(host is not null, "MonitorBarMenu host grid not found in the visual tree");

            // Root cause of the "status-bar right-click opens nothing" bug:
            // in Avalonia a panel whose Background is null does not
            // participate in pointer hit-testing, so a right-click on the
            // status row falls through to the window root and the
            // ContextMenu never opens. The host grid therefore must carry a
            // (transparent) Background to be a hit-test target.
            var bg = ((Avalonia.Controls.Panel)host!).Background;
            Assert.True(bg is not null,
                "status-bar grid Background is null -> right-click ContextMenu unreachable");
            window.Close();
        });
    }

    [AvaloniaFact]
    public void RM04_SegmentsRenderPerEnabledOptionAndFollowToggles()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var vm = new MainViewModel();
            var window = new COMTool.App.Views.MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Stateful counters: every poll increments the /proc-style
            // counters, so from the second sample on the CPU delta is a
            // deterministic 40% (dTotal=500, dIdle=300).
            long cpuBusy = 0, cpuIdle = 0, netRx = 0, netTx = 0;
            var runner = new FakeExecRunner(() =>
            {
                cpuBusy += 200;
                cpuIdle += 300;
                netRx += 4000;
                netTx += 1000;
                return Task.FromResult(Dump(
                    $"cpu {cpuBusy} 0 0 {cpuIdle} 0", $"{netRx} {netTx}"));
            });
            var tab = SshTab(runner);
            vm.AddTab(tab);
            vm.SetMonitorEnabled(tab, true);

            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!vm.SegCpuVisible && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }

            // Default 7 options on -> 7 segment groups, MobaXterm order/style.
            Assert.True(vm.SegHostVisible);
            Assert.Equal("testhost", vm.SegHostText);
            Assert.True(vm.SegCpuVisible);
            Assert.Equal("40%", vm.SegCpuText);
            Assert.Equal(MainViewModel.CpuBarMaxWidth * 0.4, vm.SegCpuBarWidth, 2);
            // 40% 属低负载 -> 绿色填充（MobaXterm 随负载变色）。
            Assert.Equal(Color.Parse("#2e9e4f"), ((SolidColorBrush)vm.SegCpuBarBrush).Color);
            Assert.True(vm.SegRamVisible);
            Assert.Contains(" GB / ", vm.SegRamText);
            Assert.True(vm.SegNetUpVisible);
            Assert.EndsWith("Mb/s", vm.SegNetUpText);
            Assert.True(vm.SegNetDownVisible);
            Assert.EndsWith("Mb/s", vm.SegNetDownText);
            Assert.True(vm.SegUptimeVisible);
            Assert.Equal("40 days", vm.SegUptimeText);
            Assert.True(vm.SegPartitionsVisible);
            Assert.Equal(2, vm.Partitions.Count);
            Assert.Equal("/: 49%", vm.Partitions[0].Text);
            Assert.True(vm.Partitions[0].ShowIcon);
            Assert.False(vm.Partitions[0].IsHot);
            Assert.Equal("/data: 82%", vm.Partitions[1].Text);
            Assert.False(vm.Partitions[1].ShowIcon);
            Assert.True(vm.Partitions[1].IsHot);

            // XAML bindings: named segments exist and follow the view model.
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Avalonia.Controls.Border>("MonSegCpu")!.IsVisible);
            Assert.True(window.FindControl<Avalonia.Controls.Border>("MonSegNetDown")!.IsVisible);
            var parts = window.FindControl<Avalonia.Controls.ItemsControl>("MonPartitions");
            Assert.True(parts!.IsVisible);
            Assert.Equal(2, parts.Items.Count);

            // Toggling menu options re-renders the segments immediately
            // (same path as the status-bar menu click handler).
            vm.MonitorMenu.SetOption("partitions", false);
            vm.MonitorMenu.SetOption("netdown", false);
            vm.ReformatMonitorLine();
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.SegPartitionsVisible);
            Assert.Empty(vm.Partitions);
            Assert.False(window.FindControl<Avalonia.Controls.ItemsControl>("MonPartitions").IsVisible);
            Assert.False(vm.SegNetDownVisible);
            Assert.False(window.FindControl<Avalonia.Controls.Border>("MonSegNetDown").IsVisible);

            // Stopping the monitor clears every segment.
            vm.SetMonitorEnabled(tab, false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsMonitorActive);
            Assert.False(vm.SegHostVisible);
            Assert.False(vm.SegCpuVisible);
            Assert.False(vm.SegNetDownVisible);
            Assert.False(vm.SegPartitionsVisible);
            Assert.Empty(vm.Partitions);
            window.Close();
        });
    }

    private static TextBlock SegmentIcon(MainWindow window, string borderName)
    {
        var border = window.FindControl<Border>(borderName)!;
        var panel = (StackPanel)border.Child!;
        return panel.Children.OfType<TextBlock>().First();
    }

    private static Color ResourceColor(Control control, string key)
    {
        Assert.True(control.TryFindResource(key, out var value), key + " resource not found");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static void AssertIcon(MainWindow window, string borderName, char code, Color expected)
    {
        var icon = SegmentIcon(window, borderName);
        Assert.Equal(code.ToString(), icon.Text);
        var actual = Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground);
        Assert.Equal(expected, actual.Color);
    }

    private static void AssertColor(IBrush? brush, string hex)
    {
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        Assert.Equal(Color.Parse(hex), solid.Color);
    }

    [AvaloniaFact]
    public void RM06_SegmentIconsAndColorsMatchMobaXtermReference()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var runner = new FakeExecRunner(() =>
                Task.FromResult(Dump("cpu 100 0 0 900 0", "0 0")));
            var tab = SshTab(runner);
            vm.AddTab(tab);
            vm.SetMonitorEnabled(tab, true);

            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!vm.SegPartitionsVisible && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.SegPartitionsVisible);

            // 已移除的四个指标（客户端/进程/FD/用户）不再有分段。
            Assert.Null(window.FindControl<Border>("MonSegClients"));
            Assert.Null(window.FindControl<Border>("MonSegProcesses"));
            Assert.Null(window.FindControl<Border>("MonSegFds"));
            Assert.Null(window.FindControl<Border>("MonSegUsers"));

            // 图标码位与配色必须与参考图一致：主机深灰，CPU/内存/上传绿，
            // 下载蓝，运行时长灰绿，磁盘灰蓝。
            var dark = ResourceColor(window, "MonIconDarkBrush");
            var uptime = ResourceColor(window, "MonIconUptimeBrush");
            var green = Color.Parse("#2e9e4f");
            var blue = Color.Parse("#2f7ed8");

            AssertIcon(window, "MonSegHost", '\uF108', dark);
            AssertIcon(window, "MonSegCpu", '\uF2DB', green);
            AssertIcon(window, "MonSegRam", '\uF538', green);
            AssertIcon(window, "MonSegNetUp", '\uF093', green);
            AssertIcon(window, "MonSegNetDown", '\uF019', blue);
            AssertIcon(window, "MonSegUptime", '\uF017', uptime);

            // 分区：磁盘图标灰蓝；占用最高的分区是粗体橙红文字（不斜体）。
            var parts = window.FindControl<ItemsControl>("MonPartitions")!;
            Assert.True(parts.IsVisible);
            var partTexts = Avalonia.VisualTree.VisualExtensions
                .GetVisualDescendants(parts)
                .OfType<TextBlock>()
                .Where(t => t.IsVisible)
                .ToList();
            var diskIcon = partTexts.First(t => t.Text == "\uF0A0");
            var diskBrush = Assert.IsAssignableFrom<ISolidColorBrush>(diskIcon.Foreground);
            Assert.Equal(ResourceColor(window, "MonIconDiskBrush"), diskBrush.Color);
            var hot = partTexts.Single(t => t.Text == "/data: 82%");
            Assert.Equal(FontWeight.Bold, hot.FontWeight);
            Assert.Equal(FontStyle.Normal, hot.FontStyle);
            var hotBrush = Assert.IsAssignableFrom<ISolidColorBrush>(hot.Foreground);
            Assert.Equal(ResourceColor(window, "MonSegHotBrush"), hotBrush.Color);
            var cool = partTexts.Single(t => t.Text == "/: 49%");
            Assert.Equal(FontWeight.Normal, cool.FontWeight);

            vm.SetMonitorEnabled(tab, false);
            window.Close();
        });
    }

    [AvaloniaFact]
    public void RM06_MonitorButtonIsFullWidthBandWithDisplayIcon()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var paneVm = new SftpPaneViewModel(
                () => throw new InvalidOperationException("no remote in unit test"),
                new InMemoryFileSystem("/local"));
            var pane = new SftpPane { ViewModel = paneVm };
            var window = new Window { Content = pane, Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var button = pane.FindControl<ToggleButton>("MonitorToggleButton")!;
            var bar = pane.FindControl<Border>("MonitorBar")!;
            Assert.False(button.IsChecked);
            var panel = Assert.IsType<StackPanel>(button.Content);
            var icon = Assert.IsType<TextBlock>(panel.Children[0]);
            var label = Assert.IsType<TextBlock>(panel.Children[1]);
            Assert.Equal("\uE163", icon.Text);
            Assert.Equal("远程监控", label.Text);

            // MobaXterm 的“远程监控”是跟侧栏同宽的一条底栏，不是按文字收缩的小按钮。
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, button.HorizontalAlignment);
            AssertColor(bar.Background, "#f1f1f1");
            Assert.True(button.Bounds.Width >= 850,
                $"monitor button width {button.Bounds.Width} is not the pane width");
            Assert.True(Math.Abs(button.Bounds.Width - bar.Bounds.Width) < 0.5,
                "monitor button does not span the full bar");

            // 关：灰字；图标与文字继承按钮前景色。
            AssertColor(button.Foreground, "#666666");
            AssertColor(icon.Foreground, "#666666");
            AssertColor(label.Foreground, "#666666");

            // 开：浅绿底、绿字、半粗体。
            paneVm.IsMonitorOn = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(button.IsChecked);
            AssertColor(button.Background, "#e8f5ee");
            AssertColor(button.Foreground, "#0f8a63");
            AssertColor(icon.Foreground, "#0f8a63");
            Assert.Equal(FontWeight.SemiBold, button.FontWeight);

            window.Close();
        });
    }

    [Fact]
    public void RM06_CpuBarBrushTracksLoad()
    {
        // MobaXterm 的 CPU 迷你条随负载变色：低绿 / 中高橙 / ≥80% 红。
        Assert.Equal(Color.Parse("#2e9e4f"), MainViewModel.CpuBarBrush(0).Color);
        Assert.Equal(Color.Parse("#2e9e4f"), MainViewModel.CpuBarBrush(40).Color);
        Assert.Equal(Color.Parse("#e8a31a"), MainViewModel.CpuBarBrush(50).Color);
        Assert.Equal(Color.Parse("#e8a31a"), MainViewModel.CpuBarBrush(79).Color);
        Assert.Equal(Color.Parse("#d84a2f"), MainViewModel.CpuBarBrush(80).Color);
        Assert.Equal(Color.Parse("#d84a2f"), MainViewModel.CpuBarBrush(81).Color);
        Assert.Equal(Color.Parse("#d84a2f"), MainViewModel.CpuBarBrush(120).Color); // clamped to 100
    }

    [Fact]
    public void RM06_EdgeResizeDragExemptsToggleButton()
    {
        // 窗口边缘 resize 的隧道处理器不能吞掉落在边缘的按钮点击；
        // SFTP“远程监控”通栏是 ToggleButton（ButtonBase，不是 Button）。
        Assert.True(MainWindow.IsEdgeInteractiveControl(new ToggleButton()));
        Assert.True(MainWindow.IsEdgeInteractiveControl(new Button()));
        Assert.False(MainWindow.IsEdgeInteractiveControl(new TextBlock()));
        Assert.False(MainWindow.IsEdgeInteractiveControl(new Border()));
    }
}
