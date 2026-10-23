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
    public void UI16_NetScanDefaultsMatchMobaXterm()
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

                // 默认值 1:1 MobaXterm：192 . 168 . 1 . 1 --> 254
                Assert.Equal("192", vm.ScanOctet1);
                Assert.Equal("168", vm.ScanOctet2);
                Assert.Equal("1", vm.ScanOctet3);
                Assert.Equal("1", vm.ScanOctetFrom);
                Assert.Equal("254", vm.ScanOctetTo);
                Assert.True(
                    NetworkScanTargets.TryParseSubnet(vm.ScanOctet1, vm.ScanOctet2, vm.ScanOctet3,
                        vm.ScanOctetFrom, vm.ScanOctetTo, out var targets, out var error),
                    $"default range not parseable: {error}");
                Assert.Equal(254, targets.Count);
                Assert.Equal("192.168.1.1", targets[0].ToString());
                Assert.Equal("192.168.1.254", targets[253].ToString());
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI16_ToolsMenuMatchesMobaXtermDropdown()
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

                // 工具页默认显示菜单（1:1 MobaXterm "工具"下拉）：
                // 三组 系统/文本/网络 + 19 项，按 MobaXterm 顺序与图标
                Assert.True(sidebar.FindControl<Border>("ToolsMenuView")!.IsVisible);
                Assert.False(sidebar.FindControl<Grid>("NetworkScannerView")!.IsVisible);
                Assert.False(sidebar.FindControl<Grid>("PortScannerView")!.IsVisible);
                Assert.Equal(22, vm.ToolMenu.Count);
                Assert.Equal("系统", vm.ToolMenu[0].Name);
                Assert.True(vm.ToolMenu[0].IsHeader);
                Assert.Equal("文本", vm.ToolMenu[9].Name);
                Assert.True(vm.ToolMenu[9].IsHeader);
                Assert.Equal("网络", vm.ToolMenu[13].Name);
                Assert.True(vm.ToolMenu[13].IsHeader);
                Assert.Equal(new[]
                {
                    "MobaPkg 包管理器 (实验性)",
                    "带 Dwm 的 X11 标签页",
                    "带 Fvwm 的 X11 窗口",
                    "带 Twm 的 X11 窗口",
                    "列出硬件设备",
                    "列出运行中的进程",
                    "命令提示符 (管理员)",
                    "Windows Powershell (管理员)",
                    "MobaTextEditor",
                    "MobaDiff",
                    "Ascii table",
                    "网络服务器",
                    "MobaSSHtunnel (端口转发)",
                    "MobaKeyGen (SSH 密钥生成器)",
                    "列出开放的网络端口",
                    "网络唤醒",
                    "网络扫描仪",
                    "端口扫描仪",
                    "网络数据包捕获",
                },
                    vm.ToolMenu.Where(t => !t.IsHeader).Select(t => t.Name).ToArray());

                // 仅 网络扫描仪/端口扫描仪 可用；每一项都有图标
                Assert.Equal(
                    new[] { "scanner", "port-scanner" },
                    vm.ToolMenu.Where(t => !t.IsHeader && t.IsEnabled).Select(t => t.Id).ToArray());
                Assert.Equal(17, vm.ToolMenu.Count(t => !t.IsHeader && !t.IsEnabled));
                Assert.All(vm.ToolMenu.Where(t => !t.IsHeader),
                    t => Assert.False(string.IsNullOrEmpty(t.Glyph)));

                // 点选"网络扫描仪"进入网络扫描页（IP 地址范围五段框）
                OpenScannerView(window, vm);
                Assert.Equal(ToolsView.NetworkScanner, vm.ToolsView);
                Assert.True(sidebar.FindControl<TextBox>("ScanOctet1Box")!.IsVisible);
                Assert.True(sidebar.FindControl<TextBox>("ScanOctetFromBox")!.IsVisible);
                Assert.True(sidebar.FindControl<Button>("ScanStartButton")!.IsEnabled);
                Assert.True(sidebar.FindControl<Button>("NetScanCloseButton")!.IsVisible);

                // 返回工具菜单，再打开"端口扫描仪"
                Click(window, sidebar.FindControl<Button>("SidebarBackToolsButton")!);
                Assert.Equal(ToolsView.Menu, vm.ToolsView);
                RunJobs(window);
                OpenPortScannerView(window, vm);
                Assert.Equal(ToolsView.PortScanner, vm.ToolsView);
                Assert.True(sidebar.FindControl<TextBox>("PortScanIpBox")!.IsVisible);
                Assert.True(sidebar.FindControl<Button>("PortScanStartButton")!.IsVisible);
                // 默认"仅扫描已知端口"，范围框禁用
                Assert.False(vm.PortScanRangeMode);
                Assert.False(sidebar.FindControl<TextBox>("PortScanFromBox")!.IsEnabled);

                // 关闭返回工具菜单
                Click(window, sidebar.FindControl<Button>("PortScanCloseButton")!);
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

                // 段值非法：非数字
                vm.ScanOctet1 = "abc";
                vm.ScanError = null;
                Click(window, sidebar.FindControl<Button>("ScanStartButton")!);
                Assert.NotNull(vm.ScanError);
                Assert.False(vm.IsScanning);
                Assert.Empty(vm.ScanRows);

                // 段值超范围
                vm.ScanOctet1 = "256";
                vm.ScanError = null;
                RunJobs(window);
                Click(window, sidebar.FindControl<Button>("ScanStartButton")!);
                Assert.NotNull(vm.ScanError);
                Assert.False(vm.IsScanning);

                // 末段范围：结束小于起始
                vm.ScanOctet1 = "192";
                vm.ScanOctetFrom = "200";
                vm.ScanOctetTo = "10";
                vm.ScanError = null;
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
    public void NET05_ScanShowsServiceGridResults()
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

                // 网络扫描 1:1 MobaXterm：固定 7 服务列 + “其他端口”列
                window.NetworkScannerForTests = new FakeScanner(new List<NetworkScanHost>
                {
                    new(new IPAddress(new byte[] { 127, 0, 0, 1 }), "loopback", new[] { 22, 80, 443 }, 1),
                    new(new IPAddress(new byte[] { 127, 0, 0, 2 }), null, Array.Empty<int>(), 2),
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
                // 7 个固定服务列：SSH(22) 与 HTTP(80) 开放，其余关闭
                Assert.Equal(7, vm.ScanRows[0].Cells.Count);
                Assert.True(vm.ScanRows[0].Cells[0].IsOpen);   // SSH
                Assert.False(vm.ScanRows[0].Cells[1].IsOpen);  // RDP
                Assert.False(vm.ScanRows[0].Cells[2].IsOpen);  // VNC
                Assert.False(vm.ScanRows[0].Cells[3].IsOpen);  // FTP
                Assert.False(vm.ScanRows[0].Cells[4].IsOpen);  // Telnet
                Assert.False(vm.ScanRows[0].Cells[5].IsOpen);  // Rlogin
                Assert.True(vm.ScanRows[0].Cells[6].IsOpen);   // HTTP
                Assert.Equal("443", vm.ScanRows[0].OtherPorts);
                Assert.Equal("127.0.0.2", vm.ScanRows[1].Address);
                Assert.All(vm.ScanRows[1].Cells, c => Assert.False(c.IsOpen));
                Assert.Equal("", vm.ScanRows[1].OtherPorts);
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
    public void NET05_NetScanCloseCancelsScan()
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

                window.NetworkScannerForTests =
                    new FakeScanner(Array.Empty<NetworkScanHost>()) { DelayMs = 5000 };

                Click(window, sidebar.FindControl<Button>("ScanStartButton")!);
                WaitUntil(() => vm.IsScanning, "scan to start");
                // MobaXterm：关闭对话框即终止扫描并返回工具菜单
                Click(window, sidebar.FindControl<Button>("NetScanCloseButton")!);
                WaitUntil(() => !vm.IsScanning, "scan to stop");

                Assert.StartsWith("已停止", vm.ScanStatus);
                Assert.Equal(ToolsView.Menu, vm.ToolsView);
                Assert.False(vm.IsBusy);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void PORT01_PortScanKnownModeFormatsListeningLines()
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
                OpenPortScannerView(window, vm);
                vm.PortScanIp = "127.0.0.1";

                // 默认“仅扫描已知端口”，范围框禁用
                Assert.False(vm.PortScanRangeMode);
                Assert.False(sidebar.FindControl<TextBox>("PortScanFromBox")!.IsEnabled);
                Assert.False(sidebar.FindControl<TextBox>("PortScanToBox")!.IsEnabled);

                window.NetworkScannerForTests = new FakeScanner(Array.Empty<NetworkScanHost>())
                {
                    DeepDelayMs = 1,
                    DeepPorts = new[] { 22, 80 },
                };

                Click(window, sidebar.FindControl<Button>("PortScanStartButton")!);
                var done = WaitUntilTry(
                    () => !vm.IsPortScanning && vm.PortScanResult.Length > 0, 10);
                Assert.True(done,
                    $"port scan did not finish: status='{vm.PortScanStatus}' result='{vm.PortScanResult}'");
                RunJobs(window);

                // MobaXterm 行格式：- Port #22 (ssh): listening，升序
                Assert.Equal(
                    "- Port #22 (ssh): listening\n- Port #80 (http): listening",
                    vm.PortScanResult);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void PORT01_PortScanRangeModeScansDeep()
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
                OpenPortScannerView(window, vm);
                vm.PortScanIp = "127.0.0.1";

                // 切到“扫描端口范围”1→65535（即深度扫描模式），范围框启用
                sidebar.FindControl<RadioButton>("PortScanRangeRadio")!.IsChecked = true;
                RunJobs(window);
                Assert.True(vm.PortScanRangeMode);
                Assert.True(sidebar.FindControl<TextBox>("PortScanFromBox")!.IsEnabled);
                Assert.True(sidebar.FindControl<TextBox>("PortScanToBox")!.IsEnabled);
                vm.PortScanFrom = "1";
                vm.PortScanTo = "65535";

                window.NetworkScannerForTests = new FakeScanner(Array.Empty<NetworkScanHost>())
                {
                    DeepDelayMs = 1,
                    DeepPorts = new[] { 80, 443 },
                };

                Click(window, sidebar.FindControl<Button>("PortScanStartButton")!);
                var done = WaitUntilTry(
                    () => !vm.IsPortScanning && vm.PortScanResult.Length > 0, 10);
                Assert.True(done,
                    $"port scan did not finish: status='{vm.PortScanStatus}' result='{vm.PortScanResult}'");
                RunJobs(window);

                Assert.Equal(
                    "- Port #80 (http): listening\n- Port #443 (https): listening",
                    vm.PortScanResult);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void PORT01_PortScanStopAndInvalidInput()
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
                OpenPortScannerView(window, vm);
                vm.PortScanIp = "127.0.0.1";

                // 1) 范围模式（1→65535 深度扫描）用“停止”按钮取消
                sidebar.FindControl<RadioButton>("PortScanRangeRadio")!.IsChecked = true;
                RunJobs(window);
                vm.PortScanFrom = "1";
                vm.PortScanTo = "65535";
                window.NetworkScannerForTests = new FakeScanner(Array.Empty<NetworkScanHost>())
                {
                    DeepDelayMs = 5000,
                    DeepPorts = Array.Empty<int>(),
                };

                Click(window, sidebar.FindControl<Button>("PortScanStartButton")!);
                WaitUntil(() => vm.IsPortScanning, "port scan to start");
                Click(window, sidebar.FindControl<Button>("PortScanStopButton")!);
                WaitUntil(() => !vm.IsPortScanning, "port scan to stop");

                Assert.Equal("扫描已停止", vm.PortScanResult);
                Assert.False(vm.IsBusy);

                // 2) 非法 IP 地址
                vm.PortScanIp = "not-an-ip";
                vm.PortScanError = null;
                RunJobs(window);
                Click(window, sidebar.FindControl<Button>("PortScanStartButton")!);
                Assert.NotNull(vm.PortScanError);
                Assert.False(vm.IsPortScanning);

                // 3) 起始端口大于结束端口
                vm.PortScanIp = "127.0.0.1";
                vm.PortScanFrom = "100";
                vm.PortScanTo = "10";
                vm.PortScanError = null;
                RunJobs(window);
                Click(window, sidebar.FindControl<Button>("PortScanStartButton")!);
                Assert.NotNull(vm.PortScanError);
                Assert.False(vm.IsPortScanning);
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

        public async Task<IReadOnlyList<int>> ScanHostPortsAsync(
            IPAddress host,
            IReadOnlyList<int> ports,
            IProgress<int>? scannedProgress,
            CancellationToken cancellationToken)
        {
            if (DeepDelayMs > 0)
            {
                await Task.Delay(DeepDelayMs, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var open = ports.Where(p => DeepPorts.Contains(p)).OrderBy(p => p).ToList();
            scannedProgress?.Report(ports.Count);
            return open;
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
        ScrollMenuToBottom(window);
        var scannerItem = window.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.Tag is string s && s == "scanner");
        Click(window, scannerItem);
        Assert.Equal(ToolsView.NetworkScanner, vm.ToolsView);
        RunJobs(window);
    }

    /// <summary>Clicks the 端口扫描仪 entry in the tools menu, opening the port scanner view.</summary>
    private static void OpenPortScannerView(MainWindow window, MainViewModel vm)
    {
        ScrollMenuToBottom(window);
        var item = window.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.Tag is string s && s == "port-scanner");
        Click(window, item);
        Assert.Equal(ToolsView.PortScanner, vm.ToolsView);
        RunJobs(window);
    }

    /// <summary>
    /// The 1:1 menu (22 rows) is taller than the panel viewport, so the
    /// scanner entries live below the fold: scroll the menu to the bottom
    /// before clicking them (real users scroll the same way).
    /// </summary>
    private static void ScrollMenuToBottom(MainWindow window)
    {
        var scroller = Sidebar(window).FindControl<ScrollViewer>("ToolMenuScroll")!;
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        RunJobs(window);
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
