using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Connections.RemoteMonitoring;
using COMTool.Core.Config;
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

    [AvaloniaFact]
    public void RM04_MonitorToggleFirstRealClickTurnsOffTheRunningMonitor()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                var viewModel = (MainViewModel)window.DataContext!;

                // 监控已开启的 SSH 标签（MobaXterm 记忆：重开会话自动恢复），
                // 模拟用户刚连上 SSH 后打开 SFTP 面板，第一下真实鼠标点击。
                var runner = new FakeExecRunner(() => Task.FromResult(
                    Dump("cpu 100 0 0 900 0", "1000 2000")));
                var pane = new SftpPaneViewModel(() => new InMemoryFileSystem("/remote"));
                var terminal = window.CreateTerminal();
                var tab = new TerminalTab
                {
                    Title = "first-click",
                    SessionId = "first-click",
                    Kind = SessionKind.Ssh,
                    Content = terminal,
                    Terminal = terminal,
                    SftpPane = pane,
                    Connection = runner,
                    MonitorEnabled = true,
                    CloseAction = () => { },
                };
                viewModel.AddTab(tab);
                viewModel.SidebarTab = SidebarTab.SshBrowser;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.True(pane.IsMonitorOn);
                var button = window.GetVisualDescendants()
                    .OfType<ToggleButton>()
                    .FirstOrDefault(b => b.Name == "MonitorToggleButton");
                Assert.NotNull(button);
                Assert.True(button!.IsChecked);
                // 首击必须按下即生效（Release 模式在窗口激活/布局抖动时会丢失）。
                Assert.Equal(ClickMode.Press, button.ClickMode);

                var toggles = new List<bool>();
                pane.MonitorToggled = v =>
                {
                    toggles.Add(v);
                    viewModel.SetMonitorEnabled(tab, v);
                };

                // 第一下点击必须立即关掉（按钮变灰、轮询停止），
                // 不能出现"第一下无效、第二下才生效"。
                var center = button.TranslatePoint(
                    new Point(button.Bounds.Width / 2, button.Bounds.Height / 2),
                    window);
                Assert.NotNull(center);
                window.MouseDown(center!.Value, MouseButton.Left);
                window.MouseUp(center.Value, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.False(button.IsChecked);
                Assert.Same(pane, button.DataContext);
                Assert.Single(toggles);
                Assert.False(toggles[0]);
                Assert.False(pane.IsMonitorOn);
                Assert.False(tab.MonitorEnabled);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void RM04_MonitorToggleFirstPressAfterConnectTogglesOnceNoDoubleFire()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                var viewModel = (MainViewModel)window.DataContext!;

                // 刚连上 SSH：监控开启（按钮绿色）。用户第一下真实鼠标点击。
                // 第 34 轮：按钮自身点击依赖 hover 状态，首按下可能尚未建立而吞掉
                // 第一击（用户反馈：第一下无效、第二下才变灰）。改为隧道阶段按下即翻转。
                var runner = new FakeExecRunner(() => Task.FromResult(
                    Dump("cpu 100 0 0 900 0", "1000 2000")));
                var pane = new SftpPaneViewModel(() => new InMemoryFileSystem("/remote"));
                var terminal = window.CreateTerminal();
                var tab = new TerminalTab
                {
                    Title = "first-press",
                    SessionId = "first-press",
                    Kind = SessionKind.Ssh,
                    Content = terminal,
                    Terminal = terminal,
                    SftpPane = pane,
                    Connection = runner,
                    MonitorEnabled = true,
                    CloseAction = () => { },
                };
                viewModel.AddTab(tab);
                viewModel.SidebarTab = SidebarTab.SshBrowser;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.True(pane.IsMonitorOn);
                var button = window.GetVisualDescendants()
                    .OfType<ToggleButton>()
                    .FirstOrDefault(b => b.Name == "MonitorToggleButton");
                Assert.NotNull(button);
                Assert.True(button!.IsChecked);

                var toggles = new List<bool>();
                pane.MonitorToggled = v =>
                {
                    toggles.Add(v);
                    viewModel.SetMonitorEnabled(tab, v);
                };

                var center = button.TranslatePoint(
                    new Point(button.Bounds.Width / 2, button.Bounds.Height / 2),
                    window);
                Assert.NotNull(center);

                // 第一下：必须立即翻转（关），恰好一次 —— 不丢首击、也不双触发。
                window.MouseDown(center!.Value, MouseButton.Left);
                window.MouseUp(center.Value, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.False(button.IsChecked);
                Assert.False(pane.IsMonitorOn);
                Assert.False(tab.MonitorEnabled);
                Assert.Single(toggles);
                Assert.False(toggles[0]);

                // 第二下：再翻回（开），累计恰好两次。
                window.MouseDown(center.Value, MouseButton.Left);
                window.MouseUp(center.Value, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.True(button.IsChecked);
                Assert.True(pane.IsMonitorOn);
                Assert.True(tab.MonitorEnabled);
                Assert.Equal(2, toggles.Count);
                Assert.True(toggles[1]);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
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
            // CPU 条是 roll 时间序列：最右一列=最新样本，高度=40%×轨道高，低负载绿色。
            Assert.Equal(MainViewModel.CpuBarColumnCount, vm.CpuColumns.Count);
            var newest = vm.CpuColumns[^1];
            Assert.Equal(40, newest.Percent, 2);
            Assert.Equal(MainViewModel.CpuBarMaxHeight * 0.4, newest.Height, 2);
            Assert.Equal(Color.Parse("#2e9e4f"), ((SolidColorBrush)newest.Brush).Color);
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

            long cpuBusy = 0, cpuIdle = 0;
            var runner = new FakeExecRunner(() =>
            {
                cpuBusy += 200;
                cpuIdle += 300;
                return Task.FromResult(Dump($"cpu {cpuBusy} 0 0 {cpuIdle} 0", "0 0"));
            });
            var tab = SshTab(runner);
            vm.AddTab(tab);
            vm.SetMonitorEnabled(tab, true);

            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!(vm.SegPartitionsVisible && vm.SegCpuVisible) && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.SegPartitionsVisible);
            Assert.True(vm.SegCpuVisible);

            // 已移除的四个指标（客户端/进程/FD/用户）不再有分段。
            Assert.Null(window.FindControl<Border>("MonSegClients"));
            Assert.Null(window.FindControl<Border>("MonSegProcesses"));
            Assert.Null(window.FindControl<Border>("MonSegFds"));
            Assert.Null(window.FindControl<Border>("MonSegUsers"));

            // 图标码位与配色按 MobaXterm 实机状态栏采样（r54 1:1）：主机黑、
            // CPU/RAM 绿（#4aaf4f，用户局部裁剪图实测）、上行深橙、下行亮橙、运行时长青。
            // 取值断言走资源键，颜色调整时测试自动跟随。
            var dark = ResourceColor(window, "MonIconDarkBrush");
            var uptime = ResourceColor(window, "MonIconUptimeBrush");
            var cpu = ResourceColor(window, "MonCpuIconBrush");
            var ram = ResourceColor(window, "MonRamIconBrush");
            var netUp = ResourceColor(window, "MonNetUpIconBrush");
            var netDown = ResourceColor(window, "MonNetDownIconBrush");

            AssertIcon(window, "MonSegHost", '\uF108', dark);
            AssertIcon(window, "MonSegCpu", '\uF2DB', cpu);
            AssertIcon(window, "MonSegRam", '\uF538', ram);
            AssertIcon(window, "MonSegNetUp", '\uF093', netUp);
            AssertIcon(window, "MonSegNetDown", '\uF019', netDown);
            AssertIcon(window, "MonSegUptime", '\uF017', uptime);

            // CPU 条是 28 列的时间序列（示波器 roll），不是单条静态填充。
            var cpuSegment = window.FindControl<Border>("MonSegCpu")!;
            var cpuColumns = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(cpuSegment)
                .OfType<Border>()
                .Where(b => b.Width == MainViewModel.CpuBarColumnWidth)
                .ToList();
            Assert.Equal(MainViewModel.CpuBarColumnCount, cpuColumns.Count);
            // 最新样本在最右列（示波器 roll 从右端进入），40% -> 绿色、4.8px 高；
            // 其余列是更早的采样（初始为 0，尚未填满窗口）。
            Assert.All(cpuColumns, b =>
                Assert.InRange(b.Height, 0, MainViewModel.CpuBarMaxHeight * 0.4));
            Assert.Equal(MainViewModel.CpuBarMaxHeight * 0.4, cpuColumns[^1].Height, 2);
            AssertColor(cpuColumns[^1].Background, "#2e9e4f");

            // 28 列必须正好铺满 56px 轨道，最新样本贴右端（模板不得有内边距导致溢出/裁剪）。
            var track = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(cpuSegment)
                .OfType<Border>()
                .Single(b => b.Width == MainViewModel.CpuBarMaxWidth);
            var firstPos = cpuColumns[0].TranslatePoint(new Point(0, 0), track);
            var newestPos = cpuColumns[^1].TranslatePoint(new Point(0, 0), track);
            Assert.Equal(0, firstPos!.Value.X, 1);
            Assert.Equal(MainViewModel.CpuBarMaxWidth,
                newestPos!.Value.X + cpuColumns[^1].Bounds.Width, 1);

            // 分区：磁盘图标灰蓝；占用最高的分区只换橙红色，字重/字形与其它分区一致
            // （MobaXterm 22.1 实样：常规字重正体；斜体会触发 Skia 合成字形崩溃）。
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
            // MobaXterm 只把高占用分区标成橙红，不加粗、不斜体；
            // 斜体还会触发 Skia 合成字形并崩溃。
            Assert.Equal(FontWeight.Normal, hot.FontWeight);
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
        Assert.Equal(Color.Parse("#2e9e4f"), MainViewModel.CpuBarColor(0));
        Assert.Equal(Color.Parse("#2e9e4f"), MainViewModel.CpuBarColor(40));
        Assert.Equal(Color.Parse("#e8a31a"), MainViewModel.CpuBarColor(50));
        Assert.Equal(Color.Parse("#e8a31a"), MainViewModel.CpuBarColor(79));
        Assert.Equal(Color.Parse("#d84a2f"), MainViewModel.CpuBarColor(80));
        Assert.Equal(Color.Parse("#d84a2f"), MainViewModel.CpuBarColor(81));
        Assert.Equal(Color.Parse("#d84a2f"), MainViewModel.CpuBarColor(120)); // clamped to 100
    }

    [Fact]
    public void RM06_CpuBarHistoryRollsLikeScope()
    {
        // MobaXterm 的 CPU 条是示波器 roll 模式：新样本从右端进入，旧样本左滚。
        var vm = new MainViewModel();
        Assert.Equal(MainViewModel.CpuBarColumnCount, vm.CpuColumns.Count);

        vm.PushCpuSample(20);
        vm.PushCpuSample(60);
        vm.PushCpuSample(90);

        var n = MainViewModel.CpuBarColumnCount;
        var tail = vm.CpuColumns.Skip(n - 3).ToList();
        Assert.Equal(new[] { 20d, 60d, 90d }, tail.Select(c => c.Percent).ToArray());
        Assert.Equal(MainViewModel.CpuBarMaxHeight * 0.2, tail[0].Height, 2);
        Assert.Equal(MainViewModel.CpuBarMaxHeight * 0.9, tail[2].Height, 2);
        // 每列按自身负载着色：历史里的高档样本仍是红色，低档仍是绿色。
        Assert.Equal(Color.Parse("#2e9e4f"), ((SolidColorBrush)tail[0].Brush).Color);
        Assert.Equal(Color.Parse("#e8a31a"), ((SolidColorBrush)tail[1].Brush).Color);
        Assert.Equal(Color.Parse("#d84a2f"), ((SolidColorBrush)tail[2].Brush).Color);
        Assert.Equal(0, vm.CpuColumns[0].Percent);

        // 灌满窗口（28 列）后左滚：最旧样本被丢掉。
        for (var i = 0; i < n; i++)
        {
            vm.PushCpuSample(90);
        }

        Assert.All(vm.CpuColumns, c => Assert.Equal(90, c.Percent, 2));

        // 停监控/清空后历史归零。
        vm.PushCpuSample(10);
        vm.ClearMonitorSegments();
        Assert.All(vm.CpuColumns, c => Assert.Equal(0, c.Percent, 2));
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
