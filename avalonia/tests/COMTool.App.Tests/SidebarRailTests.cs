using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Connections.NetworkScan;
using COMTool.Core.Config;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

/// <summary>
/// The sidebar icon rail (UI-16) and its three new panels: the network
/// scanner (NET-01), macros (MAC-01) and recent-session history (HIST-01).
/// </summary>
public sealed class SidebarRailTests
{
    [AvaloniaFact]
    public void UI16_RailSwitchesTabsAndTitles()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);

                // Default: the sessions tab is active.
                Assert.Equal(SidebarTab.Sessions, vm.SidebarTab);
                Assert.Equal("User sessions", vm.SidebarTitle);
                Assert.True(sidebar.FindControl<Grid>("SessionsPanel")!.IsVisible);
                Assert.False(sidebar.FindControl<Grid>("ToolsPanel")!.IsVisible);
                Assert.False(sidebar.FindControl<Grid>("MacrosPanel")!.IsVisible);
                Assert.False(sidebar.FindControl<Grid>("HistoryPanel")!.IsVisible);

                var rail = new[]
                {
                    sidebar.FindControl<Button>("RailToolsButton")!,
                    sidebar.FindControl<Button>("RailMacrosButton")!,
                    sidebar.FindControl<Button>("RailHistoryButton")!,
                    sidebar.FindControl<Button>("RailSessionsButton")!,
                };
                var tabs = new[]
                {
                    SidebarTab.Tools,
                    SidebarTab.Macros,
                    SidebarTab.History,
                    SidebarTab.Sessions,
                };
                var panels = new[] { "ToolsPanel", "MacrosPanel", "HistoryPanel", "SessionsPanel" };
                var titles = new[] { "Tools", "Macros", "History", "User sessions" };

                for (var i = 0; i < rail.Length; i++)
                {
                    Click(window, rail[i]);
                    Assert.Equal(tabs[i], vm.SidebarTab);
                    Assert.Equal(titles[i], vm.SidebarTitle);
                    for (var j = 0; j < panels.Length; j++)
                    {
                        Assert.Equal(i == j, sidebar.FindControl<Grid>(panels[j])!.IsVisible);
                    }

                    for (var j = 0; j < rail.Length; j++)
                    {
                        Assert.Equal(i == j, rail[j].Classes.Contains("active"));
                    }
                }
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI16_RailGoesAwayWithTheDock()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    SessionSidebar = new SessionSidebarConfig { Visible = true, Width = 220 },
                });
                window = CreateWindow(env);
                var sidebar = Sidebar(window);
                var toggle = window.FindControl<Button>("SessionsButton")!;

                Assert.NotNull(sidebar.FindControl<Border>("SidebarRail"));
                Click(window, toggle);
                Assert.False(sidebar.IsVisible);
                Click(window, toggle);
                Assert.True(sidebar.IsVisible);
                Assert.NotNull(sidebar.FindControl<Border>("SidebarRail"));
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI16_ScanRangeDefaultsToLocalSubnet()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);

                Assert.NotEmpty(vm.ScanStartIp);
                Assert.True(
                    NetworkScanTargets.TryParse(vm.ScanStartIp, vm.ScanEndIp, out _, out var error),
                    $"default range not parseable: {error}");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI16_ToolsMenuShowsEightItems()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);

                vm.SidebarTab = SidebarTab.Tools;
                RunJobs(window);

                // 工具页默认显示 8 项菜单，仅"网络扫描仪"可用
                Assert.True(sidebar.FindControl<Border>("ToolsMenuView")!.IsVisible);
                Assert.False(sidebar.FindControl<Grid>("ToolsScannerView")!.IsVisible);
                Assert.Equal(8, vm.ToolMenu.Count);
                Assert.Equal("网络扫描仪", vm.ToolMenu[5].Name);
                Assert.True(vm.ToolMenu[5].IsEnabled);
                Assert.Equal(7, vm.ToolMenu.Count(t => !t.IsEnabled));

                // 点选"网络扫描仪"进入扫描表单
                OpenScannerView(window, vm);
                Assert.Equal(ToolsView.Scanner, vm.ToolsView);
                Assert.True(sidebar.FindControl<TextBox>("ScanStartBox")!.IsVisible);
                Assert.True(sidebar.FindControl<TextBox>("ScanEndBox")!.IsVisible);

                // 返回工具菜单
                Click(window, sidebar.FindControl<Button>("SidebarBackToolsButton")!);
                Assert.Equal(ToolsView.Menu, vm.ToolsView);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC01_SaveEditDeleteMacroPersists()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);
                vm.SidebarTab = SidebarTab.Macros;
                RunJobs(window);

                // Add through the inline editor.
                Click(window, sidebar.FindControl<Button>("SidebarAddMacroButton")!);
                Assert.True(vm.IsMacroEditorOpen);
                sidebar.FindControl<TextBox>("MacroNameBox")!.Text = "重启";
                sidebar.FindControl<TextBox>("MacroTextBox")!.Text = "reboot now";
                RunJobs(window);
                Click(window, sidebar.FindControl<Button>("MacroSaveButton")!);

                Assert.False(vm.IsMacroEditorOpen);
                Assert.Single(vm.Macros);
                Assert.Equal("重启", vm.Macros[0].Name);
                Assert.Equal("reboot now", vm.Macros[0].Text);
                Assert.True(vm.Macros[0].AppendEnter);
                var disk = ReadConfig(env);
                Assert.Single(disk.Macros);
                Assert.Equal("重启", disk.Macros[0].Name);
                Assert.Equal("reboot now", disk.Macros[0].Text);

                // Empty name + text is rejected and keeps the editor open.
                Click(window, sidebar.FindControl<Button>("SidebarAddMacroButton")!);
                sidebar.FindControl<TextBox>("MacroNameBox")!.Text = "";
                sidebar.FindControl<TextBox>("MacroTextBox")!.Text = "";
                RunJobs(window);
                Click(window, sidebar.FindControl<Button>("MacroSaveButton")!);
                Assert.True(vm.IsMacroEditorOpen);
                Assert.NotNull(vm.MacroEditorError);
                Click(window, sidebar.FindControl<Button>("MacroCancelButton")!);
                Assert.False(vm.IsMacroEditorOpen);
                Assert.Single(vm.Macros);

                // Edit the existing macro.
                Click(window, ButtonByTip(window, "编辑"));
                Assert.True(vm.IsMacroEditorOpen);
                Assert.Equal("重启", vm.MacroEditorName);
                sidebar.FindControl<TextBox>("MacroNameBox")!.Text = "重启服务器";
                RunJobs(window);
                Click(window, sidebar.FindControl<Button>("MacroSaveButton")!);
                Assert.Single(vm.Macros);
                Assert.Equal("重启服务器", vm.Macros[0].Name);
                disk = ReadConfig(env);
                Assert.Single(disk.Macros);
                Assert.Equal("重启服务器", disk.Macros[0].Name);

                // Delete it.
                Click(window, ButtonByTip(window, "删除"));
                Assert.Empty(vm.Macros);
                Assert.Empty(ReadConfig(env).Macros);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC01_SendWritesTextAndEnterToActiveTerminal()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        InMemoryPty? pty = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);

                pty = new InMemoryPty();
                var terminal = new TerminalControl { Process = string.Empty };
                vm.AddTab(TestTab.Create(terminal, title: "t", sessionId: "t",
                    kind: SessionKind.Local, terminal: terminal));
                RunJobs(window);
                terminal.ApplyTemplate();
                terminal.AttachConnection(pty);
                RunJobs(window);
                Assert.True(vm.CanSendMacro);

                vm.SidebarTab = SidebarTab.Macros;
                RunJobs(window);
                Click(window, sidebar.FindControl<Button>("SidebarAddMacroButton")!);
                sidebar.FindControl<TextBox>("MacroNameBox")!.Text = "ls";
                sidebar.FindControl<TextBox>("MacroTextBox")!.Text = "ls -la";
                RunJobs(window);
                Click(window, sidebar.FindControl<Button>("MacroSaveButton")!);

                var send = ButtonByTip(window, "发送到当前终端");
                Assert.True(send.IsEnabled);
                Click(window, send);
                WaitUntil(() => pty!.WriterText.Length > 0, "macro bytes");
                Assert.Equal("ls -la\r", pty.WriterText);
            });
        }
        finally
        {
            window?.Close();
            pty?.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void MAC01_SendButtonDisabledWithoutTerminal()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.Macros.Add(new MacroConfig
                {
                    Id = "m1",
                    Name = "ls",
                    Text = "ls -la",
                    AppendEnter = true,
                });
                env.SeedConfig(config);
                window = CreateWindow(env);
                var vm = Vm(window);
                vm.SidebarTab = SidebarTab.Macros;
                RunJobs(window);

                var send = ButtonByTip(window, "发送到当前终端");
                Assert.False(send.IsEnabled);

                var terminal = new TerminalControl { Process = string.Empty };
                vm.AddTab(TestTab.Create(terminal, sessionId: "t", terminal: terminal));
                RunJobs(window);
                Assert.True(send.IsEnabled);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task HIST01_OpeningSessionRecordsAndPersistsHistory()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                var config = new ComtoolConfig();
                config.SshSessions.Add(new SshSession
                {
                    Host = "192.168.1.10",
                    Port = 22,
                    User = "root",
                });
                env.SeedConfig(config);
                window = CreateWindow(env);
                var vm = Vm(window);
                const string key = "root@192.168.1.10:22";
                var item = vm.Sections
                    .SelectMany(section => section.Items)
                    .Single(i => i.SessionId == key);

                window.SessionActivationOverrideForTests = _ => Task.CompletedTask;
                await window.ActivateSessionAsync(item);
                RunJobs(window);

                Assert.Single(vm.History);
                Assert.Equal(key, vm.History[0].Session.SessionId);
                var disk = ReadConfig(env);
                Assert.Single(disk.RecentSessions);
                Assert.Equal("Ssh", disk.RecentSessions[0].Kind);
                Assert.Equal(key, disk.RecentSessions[0].Id);

                // Reopening the same session must not duplicate the entry.
                await window.ActivateSessionAsync(item);
                RunJobs(window);
                Assert.Single(vm.History);
                Assert.Single(ReadConfig(env).RecentSessions);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void HIST01_HistoryIsMruCappedAndPrunedOnLoad()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                for (var i = 1; i <= 22; i++)
                {
                    config.SshSessions.Add(new SshSession
                    {
                        Host = $"10.0.0.{i}",
                        Port = 22,
                        User = "root",
                    });
                }

                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                for (var i = 1; i <= 22; i++)
                {
                    config.RecentSessions.Add(new RecentSessionConfig
                    {
                        Kind = "Ssh",
                        Id = $"root@10.0.0.{i}:22",
                        At = now - (long)i * 1000,
                    });
                }

                // A session that was deleted after being opened.
                config.RecentSessions.Add(new RecentSessionConfig
                {
                    Kind = "Ssh",
                    Id = "root@10.9.9.9:22",
                    At = now,
                });
                env.SeedConfig(config);

                window = CreateWindow(env);
                var vm = Vm(window);

                Assert.Equal(20, vm.History.Count);
                Assert.Equal("root@10.0.0.1:22", vm.History[0].Session.SessionId);
                Assert.Equal("just now", vm.History[0].TimeText);

                var disk = ReadConfig(env);
                Assert.Equal(20, disk.RecentSessions.Count);
                Assert.DoesNotContain(disk.RecentSessions, e => e.Id == "root@10.9.9.9:22");
                Assert.DoesNotContain(disk.RecentSessions, e => e.Id == "root@10.0.0.22:22");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void HIST01_HistoryClickReopensSession()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.SshSessions.Add(new SshSession
                {
                    Host = "192.168.1.10",
                    Port = 22,
                    User = "root",
                });
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                config.RecentSessions.Add(new RecentSessionConfig
                {
                    Kind = "Ssh",
                    Id = "root@192.168.1.10:22",
                    At = now - 60_000,
                });
                env.SeedConfig(config);
                window = CreateWindow(env);
                var vm = Vm(window);

                SessionItem? activated = null;
                window.SessionActivationOverrideForTests = item =>
                {
                    activated = item;
                    return Task.CompletedTask;
                };

                vm.SidebarTab = SidebarTab.History;
                RunJobs(window);

                Assert.Single(vm.History);
                Assert.Equal("1 min ago", vm.History[0].TimeText);

                var row = window.GetVisualDescendants()
                    .OfType<Control>()
                    .First(control => control.DataContext is HistoryEntry);
                Click(window, row);

                Assert.NotNull(activated);
                Assert.Equal("root@192.168.1.10:22", activated!.SessionId);
                Assert.Equal(SessionKind.Ssh, activated!.Kind);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void HIST01_ClearHistory()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.SshSessions.Add(new SshSession
                {
                    Host = "192.168.1.10",
                    Port = 22,
                    User = "root",
                });
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                config.RecentSessions.Add(new RecentSessionConfig
                {
                    Kind = "Ssh",
                    Id = "root@192.168.1.10:22",
                    At = now,
                });
                env.SeedConfig(config);
                window = CreateWindow(env);
                var vm = Vm(window);
                vm.SidebarTab = SidebarTab.History;
                RunJobs(window);

                Assert.Single(vm.History);
                Click(window, Sidebar(window).FindControl<Button>("SidebarClearHistoryButton")!);

                Assert.Empty(vm.History);
                Assert.Empty(ReadConfig(env).RecentSessions);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_ScanInvalidInputShowsError()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);
                vm.SidebarTab = SidebarTab.Tools;
                RunJobs(window);
                OpenScannerView(window, vm);

                // 起始地址不是 IP
                vm.ScanStartIp = "not-an-ip";
                vm.ScanEndIp = "";
                Click(window, sidebar.FindControl<Button>("ScanStartButton")!);
                Assert.NotNull(vm.ScanError);
                Assert.False(vm.IsScanning);
                Assert.Empty(vm.ScanRows);

                // 结束地址小于起始地址
                vm.ScanStartIp = "192.168.1.50";
                vm.ScanEndIp = "192.168.1.10";
                RunJobs(window);
                Click(window, sidebar.FindControl<Button>("ScanStartButton")!);
                Assert.NotNull(vm.ScanError);
                Assert.False(vm.IsScanning);

                // 无效端口
                vm.ScanStartIp = "127.0.0.1";
                vm.ScanEndIp = "";
                vm.ScanPorts = "99999";
                RunJobs(window);
                Click(window, sidebar.FindControl<Button>("ScanStartButton")!);
                Assert.NotNull(vm.ScanError);
                Assert.False(vm.IsScanning);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_ScanShowsResultsFromScanner()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);
                vm.SidebarTab = SidebarTab.Tools;
                RunJobs(window);
                OpenScannerView(window, vm);
                vm.ScanStartIp = "127.0.0.1";
                vm.ScanEndIp = "127.0.0.2";

                // DelayMs > 0 keeps the scan asynchronous so progress reports
                // are processed on the UI thread before the completion status.
                window.NetworkScannerForTests = new FakeScanner(new List<NetworkScanHost>
                {
                    new(new IPAddress(new byte[] { 127, 0, 0, 1 }), "loopback", new[] { 22 }, 1),
                    new(new IPAddress(new byte[] { 127, 0, 0, 2 }), null, Array.Empty<int>(), null),
                }) { DelayMs = 1 };

                Click(window, sidebar.FindControl<Button>("ScanStartButton")!);
                var finished = WaitUntilTry(() => vm.ScanStatus.Contains("完成"), 10);
                Assert.True(finished,
                    $"scan did not finish: status='{vm.ScanStatus}' " +
                    $"isScanning={vm.IsScanning} rows={vm.ScanRows.Count}");
                RunJobs(window);

                Assert.Equal(2, vm.ScanRows.Count);
                Assert.Equal("127.0.0.1", vm.ScanRows[0].Address);
                Assert.Equal("loopback", vm.ScanRows[0].HostName);
                Assert.Equal("1 ms", vm.ScanRows[0].Latency);
                // 默认端口 22,23 → 两列：22 开放（绿勾），23 关闭（灰叉）
                Assert.Equal(2, vm.ScanRows[0].Cells.Count);
                Assert.True(vm.ScanRows[0].Cells[0].IsOpen);
                Assert.False(vm.ScanRows[0].Cells[1].IsOpen);
                Assert.Equal("127.0.0.2", vm.ScanRows[1].Address);
                Assert.All(vm.ScanRows[1].Cells, c => Assert.False(c.IsOpen));
                Assert.Equal("完成：发现 2 台在线设备", vm.ScanStatus);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_StopCancelsScan()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);
                vm.SidebarTab = SidebarTab.Tools;
                RunJobs(window);
                OpenScannerView(window, vm);
                vm.ScanStartIp = "127.0.0.1";
                vm.ScanEndIp = "127.0.0.30";

                window.NetworkScannerForTests =
                    new FakeScanner(Array.Empty<NetworkScanHost>()) { DelayMs = 5000 };

                Click(window, sidebar.FindControl<Button>("ScanStartButton")!);
                WaitUntil(() => vm.IsScanning, "scan to start");
                Click(window, sidebar.FindControl<Button>("ScanStopButton")!);
                WaitUntil(() => !vm.IsScanning, "scan to stop");

                Assert.StartsWith("已停止", vm.ScanStatus);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_DeepScanRequiresRowSelection()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);
                vm.SidebarTab = SidebarTab.Tools;
                RunJobs(window);
                OpenScannerView(window, vm);
                vm.ScanStartIp = "127.0.0.1";
                vm.ScanEndIp = "";

                window.NetworkScannerForTests = new FakeScanner(new List<NetworkScanHost>
                {
                    new(new IPAddress(new byte[] { 127, 0, 0, 1 }), null, new[] { 22 }, 1),
                }) { DelayMs = 1 };

                Click(window, sidebar.FindControl<Button>("ScanStartButton")!);
                WaitUntilTry(() => vm.ScanStatus.Contains("完成"), 10);
                RunJobs(window);
                Assert.Single(vm.ScanRows);

                // No row selected → deep scan disabled.
                Assert.True(vm.SelectedScanAddress is null);
                Assert.False(vm.IsDeepScanEnabled);
                Assert.False(sidebar.FindControl<Button>("DeepScanButton")!.IsEnabled);

                // Selecting a row enables it.
                Click(window, ScanRowControl(window, "127.0.0.1"));
                Assert.Equal("127.0.0.1", vm.SelectedScanAddress);
                Assert.True(vm.IsDeepScanEnabled);
                Assert.True(sidebar.FindControl<Button>("DeepScanButton")!.IsEnabled);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_DeepScanShowsOpenPorts()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);
                vm.SidebarTab = SidebarTab.Tools;
                RunJobs(window);
                OpenScannerView(window, vm);
                vm.ScanStartIp = "127.0.0.1";
                vm.ScanEndIp = "";

                window.NetworkScannerForTests = new FakeScanner(new List<NetworkScanHost>
                {
                    new(new IPAddress(new byte[] { 127, 0, 0, 1 }), null, new[] { 22 }, 1),
                })
                {
                    DelayMs = 1,
                    DeepPorts = new[] { 80, 443 },
                };

                Click(window, sidebar.FindControl<Button>("ScanStartButton")!);
                WaitUntilTry(() => vm.ScanStatus.Contains("完成"), 10);
                RunJobs(window);
                Click(window, ScanRowControl(window, "127.0.0.1"));
                Assert.True(vm.IsDeepScanEnabled);

                Click(window, sidebar.FindControl<Button>("DeepScanButton")!);
                var done = WaitUntilTry(
                    () => !vm.IsDeepScanning && vm.DeepScanResult.Length > 0, 10);
                Assert.True(done,
                    $"deep scan did not finish: status='{vm.DeepScanStatus}' result='{vm.DeepScanResult}'");
                Assert.Contains("80", vm.DeepScanResult);
                Assert.Contains("443", vm.DeepScanResult);
                Assert.StartsWith("深度扫描完成：127.0.0.1", vm.DeepScanResult);
                Assert.False(vm.IsDeepScanning);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_DeepScanStopCancels()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow(env);
                var vm = Vm(window);
                var sidebar = Sidebar(window);
                vm.SidebarTab = SidebarTab.Tools;
                RunJobs(window);
                OpenScannerView(window, vm);
                vm.ScanStartIp = "127.0.0.1";
                vm.ScanEndIp = "";

                window.NetworkScannerForTests = new FakeScanner(new List<NetworkScanHost>
                {
                    new(new IPAddress(new byte[] { 127, 0, 0, 1 }), null, new[] { 22 }, 1),
                })
                {
                    DelayMs = 1,
                    DeepDelayMs = 5000,
                    DeepPorts = Array.Empty<int>(),
                };

                Click(window, sidebar.FindControl<Button>("ScanStartButton")!);
                WaitUntilTry(() => vm.ScanStatus.Contains("完成"), 10);
                RunJobs(window);
                Click(window, ScanRowControl(window, "127.0.0.1"));

                Click(window, sidebar.FindControl<Button>("DeepScanButton")!);
                WaitUntil(() => vm.IsDeepScanning, "deep scan to start");
                Click(window, sidebar.FindControl<Button>("ScanStopButton")!);
                WaitUntil(() => !vm.IsDeepScanning, "deep scan to stop");

                Assert.StartsWith("深度扫描已停止", vm.DeepScanResult);
                Assert.False(vm.IsBusy);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class FakeScanner : INetworkScanner
    {
        private readonly IReadOnlyList<NetworkScanHost> _hosts;

        public FakeScanner(IReadOnlyList<NetworkScanHost> hosts) => _hosts = hosts;

        public int DelayMs { get; init; }

        public int DeepDelayMs { get; init; }

        public IReadOnlyList<int> DeepPorts { get; init; } = Array.Empty<int>();

        public async Task<IReadOnlyList<int>> ScanHostPortsAsync(
            IPAddress host,
            int fromPort,
            int toPort,
            IProgress<int>? scannedProgress,
            CancellationToken cancellationToken)
        {
            if (DeepDelayMs > 0)
            {
                await Task.Delay(DeepDelayMs, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            scannedProgress?.Report(toPort - fromPort + 1);
            return DeepPorts;
        }

        public async Task ScanAsync(
            IReadOnlyList<IPAddress> targets,
            IReadOnlyList<int> ports,
            IProgress<NetworkScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DelayMs > 0)
                {
                    await Task.Delay(DelayMs, cancellationToken);
                }

                progress?.Report(new NetworkScanProgress(
                    i + 1,
                    targets.Count,
                    i < _hosts.Count ? _hosts[i] : null));
            }
        }
    }

    /// <summary>Clicks the 网络扫描仪 entry in the tools menu, opening the scanner view.</summary>
    private static void OpenScannerView(MainWindow window, MainViewModel vm)
    {
        var scannerItem = window.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.Tag is string s && s == "scanner");
        Click(window, scannerItem);
        Assert.Equal(ToolsView.Scanner, vm.ToolsView);
        RunJobs(window);
    }

    /// <summary>Finds the scan-result row control for the given address.</summary>
    private static Control ScanRowControl(MainWindow window, string address)
    {
        var row = window.GetVisualDescendants()
            .OfType<Control>()
            .First(c => c.DataContext is ScanGridRow r && r.Address == address);
        Assert.NotNull(row);
        return row;
    }

    private static MainWindow CreateWindow(TestEnvironment env)
    {
        var window = new MainWindow { DataContext = new MainViewModel() };
        window.Show();
        window.StopPortTimerForTests();
        RunJobs(window);
        return window;
    }

    private static MainViewModel Vm(MainWindow window) =>
        (MainViewModel)window.DataContext!;

    private static SessionSidebar Sidebar(MainWindow window) =>
        window.FindControl<SessionSidebar>("Sidebar")!;

    private static ComtoolConfig ReadConfig(TestEnvironment env) =>
        ComtoolConfig.Load(Path.Combine(env.AvaloniaConfigDir, "config.json"));

    private static Button ButtonByTip(MainWindow window, string tip)
    {
        var button = window.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => string.Equals(ToolTip.GetTip(b) as string, tip));
        Assert.NotNull(button);
        return button!;
    }

    private static void Click(MainWindow window, Control control)
    {
        var center = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);
        Assert.NotNull(center);
        window.MouseDown(center!.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
        RunJobs(window);
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}");
            }

            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// WaitUntil variant returning false on timeout so the caller can
    /// report the actual VM state in the failure message.
    /// </summary>
    private static bool WaitUntilTry(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        return true;
    }
}
