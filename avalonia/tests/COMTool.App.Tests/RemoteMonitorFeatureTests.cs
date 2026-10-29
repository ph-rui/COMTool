using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.Connections.RemoteMonitoring;
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
        Assert.False(state.IsOption("clients"));

        state.SetOption("clients", true);
        state.SetOption("cpu", false);

        var cfg = new COMTool.Core.Config.RemoteMonitorConfig();
        state.SaveTo(cfg);
        var reloaded = new MonitorMenuState();
        reloaded.LoadFrom(cfg);

        Assert.True(reloaded.IsOption("clients"));
        Assert.False(reloaded.IsOption("cpu"));
        Assert.True(reloaded.IsOption("partitions"));
    }

    [AvaloniaFact]
    public void RM04_StatusBarMenuHas13ItemsWithSevenCheckedByDefault()
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
            // 复制 / 保存到文件 / separator / 11 checkable options:
            // 13 functional items + 1 Separator = 14 entries (spec: "13 items").
            Assert.Equal(14, menu.Items.Count);
            var items = menu.Items.OfType<Avalonia.Controls.MenuItem>().ToList();
            Assert.Equal(13, items.Count); // 14 entries minus the Separator
            Assert.Equal("复制(O)", items[0].Header);
            Assert.Equal("保存到文件(P)", items[1].Header);
            var checkable = items.Skip(2).ToList();
            Assert.Equal(11, checkable.Count);
            foreach (var mi in checkable)
            {
                Assert.Equal(Avalonia.Controls.MenuItemToggleType.CheckBox, mi.ToggleType);
            }
            var tags = checkable.Select(mi => (string)mi.Tag!).ToList();
            Assert.Equal(
                new[] { "host", "cpu", "ram", "netup", "netdown", "clients",
                        "processes", "fds", "uptime", "users", "partitions" },
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
            Assert.False(byTag["clients"].IsChecked);
            Assert.False(byTag["processes"].IsChecked);
            Assert.False(byTag["fds"].IsChecked);
            Assert.True(byTag["uptime"].IsChecked);
            Assert.False(byTag["users"].IsChecked);
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
}
