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
using COMTool.App.ViewModels.Tools;
using COMTool.App.Views;
using COMTool.Connections.NetworkScan;
using COMTool.Connections.Servers;
using COMTool.Core.Config;
using COMTool.Sftp;
using COMTool.Sftp.Testing;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

/// <summary>
/// The sidebar icon rail (UI-16) and its three new panels: the network
/// scanner (NET-01), macros (MAC-01) and the SSH-browser icon that follows
/// SSH connections.
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

                // MobaXterm 左侧固定三格；SSH 浏览器是连接后才出现的第四格。
                var rail = new[]
                {
                    sidebar.FindControl<Button>("RailToolsButton")!,
                    sidebar.FindControl<Button>("RailMacrosButton")!,
                    sidebar.FindControl<Button>("RailSessionsButton")!,
                };
                var tabs = new[]
                {
                    SidebarTab.Tools,
                    SidebarTab.Macros,
                    SidebarTab.Sessions,
                };
                var panels = new[] { "ToolsPanel", "MacrosPanel", "SessionsPanel" };
                var titles = new[] { "Tools", "Macros", "User sessions" };

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
    public void UI16_SshBrowserIconAppearsOnlyWithAConnectedSshSession()
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
                var browser = sidebar.FindControl<Button>("RailSshBrowserButton")!;

                // 没有 SSH 连接时，MobaXterm 左栏只有三格。
                Assert.False(browser.IsVisible);

                var (tab, pane, connection) = AddConnectedSshTab(window);
                pty = connection;
                window.UpdateConnectionStatesForTests();
                RunJobs(window);

                // SSH 登录成功后出现第四格（橙色地球）。
                Assert.True(browser.IsVisible);
                Assert.Same(tab, vm.SelectedTab);
                Assert.False(pane.IsExpanded);

                // 点图标 = 左侧栏切换成当前 SSH 会话的 SFTP 浏览器。
                Click(window, browser);
                Assert.Equal(SidebarTab.SshBrowser, vm.SidebarTab);
                Assert.True(pane.IsExpanded);

                var panel = sidebar.FindControl<SshBrowserPanel>("SshBrowserPanel")!;
                Assert.True(panel.IsVisible);
                Assert.Same(pane, panel.SftpPaneControl?.ViewModel);
                Assert.Same(pane, vm.SshBrowserPane);

                // 连接断开后第四格消失，左栏回到会话列表。
                tab.IsAlive = false;
                window.UpdateConnectionStatesForTests();
                RunJobs(window);
                Assert.False(browser.IsVisible);
                Assert.Equal(SidebarTab.Sessions, vm.SidebarTab);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            pty?.Dispose();
        }
    }

    [AvaloniaFact]
    public void UI16_RailStaysVisibleWhenTheDockCollapses()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    SessionSidebar = new SessionSidebarConfig { Visible = true, Width = 260 },
                });
                window = CreateWindow(env);
                var sidebar = Sidebar(window);
                var body = window.FindControl<Grid>("BodyGrid")!;
                var toggle = window.FindControl<Button>("SessionsButton")!;

                Assert.NotNull(sidebar.FindControl<Border>("SidebarRail"));
                Click(window, toggle);

                // Only the panel is retracted; the rail stays at 40px.
                Assert.True(sidebar.IsVisible);
                Assert.NotNull(sidebar.FindControl<Border>("SidebarRail"));
                Assert.Equal(40, body.ColumnDefinitions[0].ActualWidth, precision: 1);

                Click(window, toggle);
                Assert.True(sidebar.IsVisible);
                Assert.Equal(260, body.ColumnDefinitions[0].ActualWidth, precision: 1);
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
    public void UI16_RailIconTogglesItsPanel()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    SessionSidebar = new SessionSidebarConfig { Visible = true, Width = 260 },
                });
                window = CreateWindow(env);
                var sidebar = Sidebar(window);
                var body = window.FindControl<Grid>("BodyGrid")!;
                var content = window.FindControl<Grid>("ContentArea")!;
                var splitter = window.FindControl<GridSplitter>("SidebarSplitter")!;
                var tools = sidebar.FindControl<Button>("RailToolsButton")!;
                var sessions = sidebar.FindControl<Button>("RailSessionsButton")!;

                Assert.Equal(260, body.ColumnDefinitions[0].ActualWidth, precision: 1);
                Assert.True(splitter.IsVisible);

                // Another icon switches to its panel without collapsing.
                Click(window, tools);
                Assert.Equal(SidebarTab.Tools, Vm(window).SidebarTab);
                Assert.True(sidebar.FindControl<Grid>("ToolsPanel")!.IsVisible);
                Assert.Equal(260, body.ColumnDefinitions[0].ActualWidth, precision: 1);

                // The same icon again retracts the panel; the rail and the
                // active tab stay, so a third click reopens the same panel.
                Click(window, tools);
                Assert.Equal(SidebarTab.Tools, Vm(window).SidebarTab);
                Assert.True(sidebar.IsVisible);
                Assert.NotNull(sidebar.FindControl<Border>("SidebarRail"));
                Assert.Equal(40, body.ColumnDefinitions[0].ActualWidth, precision: 1);
                Assert.Equal(8, content.Margin.Left);
                Assert.False(splitter.IsVisible);

                Click(window, tools);
                Assert.Equal(SidebarTab.Tools, Vm(window).SidebarTab);
                Assert.Equal(260, body.ColumnDefinitions[0].ActualWidth, precision: 1);
                Assert.Equal(32, content.Margin.Left);
                Assert.True(splitter.IsVisible);

                // The sessions icon toggles the same way.
                Click(window, sessions);
                Assert.Equal(SidebarTab.Sessions, Vm(window).SidebarTab);
                Assert.True(sidebar.FindControl<Grid>("SessionsPanel")!.IsVisible);
                Click(window, sessions);
                Assert.Equal(40, body.ColumnDefinitions[0].ActualWidth, precision: 1);
                Click(window, sessions);
                Assert.Equal(260, body.ColumnDefinitions[0].ActualWidth, precision: 1);
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
        NetworkScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new NetworkScanWindow();
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;

                // 默认值 1:1 MobaXterm：192 . 168 . 1 . 1 --> 254
                Assert.Equal("192", vm.Octet1);
                Assert.Equal("168", vm.Octet2);
                Assert.Equal("1", vm.Octet3);
                Assert.Equal("1", vm.OctetFrom);
                Assert.Equal("254", vm.OctetTo);
                Assert.True(
                    NetworkScanTargets.TryParseSubnet(vm.Octet1, vm.Octet2, vm.Octet3,
                        vm.OctetFrom, vm.OctetTo, out var targets, out var error),
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

                // 工具页 = 三个独立窗口工具，1:1 MobaXterm 22.1 工具菜单
                Assert.True(sidebar.FindControl<Border>("ToolsMenuView")!.IsVisible);
                Assert.Equal(3, vm.ToolMenu.Count);
                Assert.All(vm.ToolMenu, item => Assert.True(item.IsEnabled));
                Assert.Equal(
                    new[] { "net-server", "scanner", "port-scanner" },
                    vm.ToolMenu.Select(item => item.Id).ToArray());
                Assert.Equal(
                    new[] { "网络服务器", "网络扫描仪", "端口扫描仪" },
                    vm.ToolMenu.Select(item => item.Name).ToArray());
                Assert.Equal(
                    new[] { "\uf013", "\uf7c0", "\uf1eb" },
                    vm.ToolMenu.Select(item => item.Glyph).ToArray());
                Assert.Equal(
                    new[] { "#00897b", "#5e35b1", "#2196f3" },
                    vm.ToolMenu.Select(item => item.GlyphColor).ToArray());

                // 点选“网络服务器”：打开独立窗口，重复点选只激活同一实例
                ClickToolMenuItem(window, "net-server");
                var servers = Assert.IsType<ServersManagementWindow>(
                    window!.FindToolWindowForTests("net-server"));
                Assert.True(servers.IsVisible);
                ClickToolMenuItem(window, "net-server");
                Assert.Single(window.ToolWindowsForTests);
                Assert.Same(servers, window.FindToolWindowForTests("net-server"));

                // 点选“网络扫描仪”：再开一个独立窗口
                ClickToolMenuItem(window, "scanner");
                var scanner = Assert.IsType<NetworkScanWindow>(
                    window.FindToolWindowForTests("scanner"));
                Assert.True(scanner.IsVisible);
                Assert.Equal(2, window.ToolWindowsForTests.Count);

                // 点选“端口扫描仪”：第三个窗口
                ClickToolMenuItem(window, "port-scanner");
                var portScanner = Assert.IsType<PortScanWindow>(
                    window.FindToolWindowForTests("port-scanner"));
                Assert.True(portScanner.IsVisible);
                Assert.Equal(3, window.ToolWindowsForTests.Count);

                // 关闭窗口后从注册表移除，再次点选可重新打开
                portScanner.Close();
                RunJobs(window);
                Assert.Null(window.FindToolWindowForTests("port-scanner"));
                ClickToolMenuItem(window, "port-scanner");
                Assert.NotNull(window.FindToolWindowForTests("port-scanner"));
                Assert.Equal(3, window.ToolWindowsForTests.Count);

                servers.Close();
                scanner.Close();
                window.FindToolWindowForTests("port-scanner")!.Close();
                RunJobs(window);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI16_ToolsMenuUsesReadableSizesAndWiderPanel()
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

                // The left panel is wide enough for the MobaXterm tool rows.
                var body = window.FindControl<Grid>("BodyGrid")!;
                Assert.Equal(260, body.ColumnDefinitions[0].ActualWidth, precision: 1);

                // The rail is flush with the window edge: no rounded corner
                // that would expose a light line at the left.
                var rail = sidebar.FindControl<Border>("SidebarRail")!;
                Assert.Equal(new CornerRadius(0), rail.CornerRadius);
                Assert.Equal(40, rail.Bounds.Width, precision: 1);

                var button = sidebar.GetVisualDescendants()
                    .OfType<Button>()
                    .First(candidate => candidate.Classes.Contains("ToolMenuItemButton"));
                Assert.True(button.FontSize >= 13, $"tool font is {button.FontSize}");
                var glyph = button.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .First(text => text.Text == "\uf013");
                Assert.True(glyph.FontSize >= 14, $"tool icon is {glyph.FontSize}");
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
    public void NET05_ScanInvalidInputShowsError()
    {
        using var env = new TestEnvironment();
        NetworkScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new NetworkScanWindow();
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;
                var start = window.FindControl<Button>("ScanStartButton")!;

                // 段值非法：非数字
                vm.Octet1 = "abc";
                vm.Error = null;
                Click(window, start);
                Assert.NotNull(vm.Error);
                Assert.False(vm.IsScanning);
                Assert.Empty(vm.Rows);

                // 段值超范围
                vm.Octet1 = "256";
                vm.Error = null;
                RunJobs(window);
                Click(window, start);
                Assert.NotNull(vm.Error);
                Assert.False(vm.IsScanning);

                // 末段范围：结束小于起始
                vm.Octet1 = "192";
                vm.OctetFrom = "200";
                vm.OctetTo = "10";
                vm.Error = null;
                RunJobs(window);
                Click(window, start);
                Assert.NotNull(vm.Error);
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
        NetworkScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new NetworkScanWindow
                {
                    // 网络扫描 1:1 MobaXterm：固定 7 服务列 + “其他端口”列
                    ScannerForTests = new FakeScanner(new List<NetworkScanHost>
                    {
                        new(new IPAddress(new byte[] { 127, 0, 0, 1 }), "loopback", new[] { 22, 80, 443 }, 1),
                        new(new IPAddress(new byte[] { 127, 0, 0, 2 }), null, Array.Empty<int>(), 2),
                    }) { DelayMs = 1 },
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;

                Click(window, window.FindControl<Button>("ScanStartButton")!);
                var finished = WaitUntilTry(() => vm.Status.Contains("完成"), 10);
                Assert.True(finished,
                    $"scan did not finish: status='{vm.Status}' " +
                    $"isScanning={vm.IsScanning} rows={vm.Rows.Count}");
                RunJobs(window);

                Assert.Equal(2, vm.Rows.Count);
                Assert.Equal("127.0.0.1", vm.Rows[0].Address);
                Assert.Equal("loopback", vm.Rows[0].HostName);
                // 7 个固定服务列：SSH(22) 与 HTTP(80) 开放，其余关闭
                Assert.Equal(7, vm.Rows[0].Cells.Count);
                Assert.True(vm.Rows[0].Cells[0].IsOpen);   // SSH
                Assert.False(vm.Rows[0].Cells[1].IsOpen);  // RDP
                Assert.False(vm.Rows[0].Cells[2].IsOpen);  // VNC
                Assert.False(vm.Rows[0].Cells[3].IsOpen);  // FTP
                Assert.False(vm.Rows[0].Cells[4].IsOpen);  // Telnet
                Assert.False(vm.Rows[0].Cells[5].IsOpen);  // Rlogin
                Assert.True(vm.Rows[0].Cells[6].IsOpen);   // HTTP
                Assert.Equal("443", vm.Rows[0].OtherPorts);
                Assert.Equal("127.0.0.2", vm.Rows[1].Address);
                Assert.All(vm.Rows[1].Cells, c => Assert.False(c.IsOpen));
                Assert.Equal("", vm.Rows[1].OtherPorts);
                Assert.Equal("完成：发现 2 台在线设备", vm.Status);
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
        NetworkScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new NetworkScanWindow
                {
                    ScannerForTests =
                        new FakeScanner(Array.Empty<NetworkScanHost>()) { DelayMs = 5000 },
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;

                Click(window, window.FindControl<Button>("ScanStartButton")!);
                WaitUntil(() => vm.IsScanning, "scan to start");
                // MobaXterm：关闭对话框即终止扫描
                Click(window, window.FindControl<Button>("NetScanCloseButton")!);
                WaitUntil(() => !vm.IsScanning, "scan to stop");

                Assert.StartsWith("已停止", vm.Status);
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
        PortScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new PortScanWindow
                {
                    ScannerForTests = new FakeScanner(Array.Empty<NetworkScanHost>())
                    {
                        DeepDelayMs = 1,
                        DeepPorts = new[] { 22, 80 },
                    },
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;
                vm.Ip = "127.0.0.1";

                // 默认“仅扫描已知端口”，范围框禁用
                Assert.False(vm.RangeMode);
                Assert.False(window.FindControl<TextBox>("PortScanFromBox")!.IsEnabled);
                Assert.False(window.FindControl<TextBox>("PortScanToBox")!.IsEnabled);

                Click(window, window.FindControl<Button>("PortScanStartButton")!);
                var done = WaitUntilTry(
                    () => !vm.IsScanning && vm.Result.Length > 0, 10);
                Assert.True(done,
                    $"port scan did not finish: status='{vm.Status}' result='{vm.Result}'");
                RunJobs(window);

                // MobaXterm 行格式：- Port #22 (ssh): listening，升序
                Assert.Equal(
                    "- Port #22 (ssh): listening\n- Port #80 (http): listening",
                    vm.Result);
                // 完成后显示终止状态（迟到的进度上报不会覆盖它）
                Assert.Equal("扫描完成：发现 2 个开放端口", vm.Status);
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
    public void PORT01_PortScanRangeModeScansDeep()
    {
        using var env = new TestEnvironment();
        PortScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new PortScanWindow
                {
                    ScannerForTests = new FakeScanner(Array.Empty<NetworkScanHost>())
                    {
                        DeepDelayMs = 1,
                        DeepPorts = new[] { 80, 443 },
                    },
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;
                vm.Ip = "127.0.0.1";

                // 切到“扫描端口范围”1→65535（即深度扫描模式），范围框启用
                window.FindControl<RadioButton>("PortScanRangeRadio")!.IsChecked = true;
                RunJobs(window);
                Assert.True(vm.RangeMode);
                Assert.True(window.FindControl<TextBox>("PortScanFromBox")!.IsEnabled);
                Assert.True(window.FindControl<TextBox>("PortScanToBox")!.IsEnabled);
                vm.From = "1";
                vm.To = "65535";

                Click(window, window.FindControl<Button>("PortScanStartButton")!);
                var done = WaitUntilTry(
                    () => !vm.IsScanning && vm.Result.Length > 0, 10);
                Assert.True(done,
                    $"port scan did not finish: status='{vm.Status}' result='{vm.Result}'");
                RunJobs(window);

                Assert.Equal(
                    "- Port #80 (http): listening\n- Port #443 (https): listening",
                    vm.Result);
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
        PortScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new PortScanWindow
                {
                    ScannerForTests = new FakeScanner(Array.Empty<NetworkScanHost>())
                    {
                        DeepDelayMs = 5000,
                        DeepPorts = Array.Empty<int>(),
                    },
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;
                vm.Ip = "127.0.0.1";

                // 1) 范围模式（1→65535 深度扫描）用“停止”按钮取消
                window.FindControl<RadioButton>("PortScanRangeRadio")!.IsChecked = true;
                RunJobs(window);
                vm.From = "1";
                vm.To = "65535";

                Click(window, window.FindControl<Button>("PortScanStartButton")!);
                WaitUntil(() => vm.IsScanning, "port scan to start");
                Click(window, window.FindControl<Button>("PortScanStopButton")!);
                WaitUntil(() => !vm.IsScanning, "port scan to stop");

                Assert.Equal("扫描已停止", vm.Result);

                // 2) 非法 IP 地址
                vm.Ip = "not-an-ip";
                vm.Error = null;
                RunJobs(window);
                Click(window, window.FindControl<Button>("PortScanStartButton")!);
                Assert.NotNull(vm.Error);
                Assert.False(vm.IsScanning);

                // 3) 起始端口大于结束端口
                vm.Ip = "127.0.0.1";
                vm.From = "100";
                vm.To = "10";
                vm.Error = null;
                RunJobs(window);
                Click(window, window.FindControl<Button>("PortScanStartButton")!);
                Assert.NotNull(vm.Error);
                Assert.False(vm.IsScanning);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // --- Servers management (SRV-01) ---------------------------------------

    [AvaloniaFact]
    public void SRV01_ServersManagementShowsNineRows()
    {
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new ServersManagementWindow();
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;

                Assert.Equal(9, vm.Rows.Count);
                Assert.Equal(
                    new[] { "tftp", "ftp", "http", "ssh", "telnet", "nfs", "vnc", "cron", "iperf" },
                    vm.Rows.Select(r => r.Id));
                Assert.Equal(
                    new[]
                    {
                        "TFTP 服务器", "FTP 服务器", "HTTP 服务器", "SSH/SFTP 服务器",
                        "Telnet 服务器", "NFS 服务器", "VNC 服务器", "Cron 服务器",
                        "Iperf 服务器",
                    },
                    vm.Rows.Select(r => r.Name));
                Assert.True(vm.Find("http")!.IsImplemented);
                Assert.True(vm.Find("tftp")!.IsImplemented);
                Assert.Equal(7, vm.Rows.Count(r => !r.IsImplemented));
                Assert.Equal("暂不支持（框架已预留）", vm.Find("ftp")!.ToolTipText);

                // 初始显示欢迎页，底部有“确定”
                Assert.True(vm.IsWelcome);
                Assert.NotNull(window.FindControl<Button>("ServersOkButton"));
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SRV01_SelectingRowShowsMatchingConfig()
    {
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new ServersManagementWindow();
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;

                Click(window, ServerRowButton(window, "tftp", "ServerGear"));
                Assert.True(vm.IsTftpSelected);
                Assert.False(vm.IsWelcome);
                Assert.Equal(69, vm.TftpPort);
                Assert.True(vm.TftpRuntimeEnabled);
                Assert.Equal(3600, vm.TftpRuntimeSeconds);

                Click(window, ServerRowButton(window, "http", "ServerGear"));
                Assert.True(vm.IsHttpSelected);
                Assert.False(vm.IsTftpSelected);
                Assert.Equal(80, vm.HttpPort);
                Assert.True(vm.HttpAllowListing);
                Assert.Equal(3600, vm.HttpRuntimeSeconds);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SRV01_HttpConfigStartsWithStopAndLog()
    {
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        var root = Path.Combine(Path.GetTempPath(), "comtool-srv-http",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            env.Run(() =>
            {
                var fake = new FakeServerHost();
                window = new ServersManagementWindow { ServerFactoryForTests = _ => fake };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;
                vm.Http.RootDirectory = root;

                Click(window, ServerRowButton(window, "http", "ServerGear"));
                Assert.True(vm.IsHttpSelected);

                Click(window, ServerRowButton(window, "http", "ServerPlay"));
                WaitUntil(() => vm.HttpRow.IsRunning, "HTTP server to start");
                Assert.Equal(1, fake.StartCount);
                WaitUntil(() => vm.HttpLog.Any(line => line.Contains("HTTP server started")),
                    "HTTP start log line");
                Assert.NotNull(vm.HttpRow.RemainingSeconds);
                Assert.StartsWith("(", vm.HttpRow.CountdownText);

                Click(window, ServerRowButton(window, "http", "ServerStop"));
                WaitUntil(() => !vm.HttpRow.IsRunning, "HTTP server to stop");
                Assert.Equal(1, fake.StopCount);
                WaitUntil(() => vm.HttpLog.Any(line => line.Contains("HTTP server stopped")),
                    "HTTP stop log line");
                Assert.Null(vm.HttpRow.RemainingSeconds);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    [AvaloniaFact]
    public void SRV01_StartFailureIsLogged()
    {
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        var root = Path.Combine(Path.GetTempPath(), "comtool-srv-fail",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            env.Run(() =>
            {
                var fake = new FakeServerHost
                {
                    StartException = new InvalidOperationException("port already in use"),
                };
                window = new ServersManagementWindow { ServerFactoryForTests = _ => fake };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;
                vm.Http.RootDirectory = root;

                Click(window, ServerRowButton(window, "http", "ServerPlay"));
                WaitUntil(() => vm.HttpLog.Any(line => line.Contains("port already in use")),
                    "bind failure log line");
                Assert.False(vm.HttpRow.IsRunning);
                Assert.Null(vm.HttpRow.RemainingSeconds);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    [AvaloniaFact]
    public void SRV01_ClosingWindowStopsServers()
    {
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        var root = Path.Combine(Path.GetTempPath(), "comtool-srv-close",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            env.Run(() =>
            {
                var fake = new FakeServerHost();
                window = new ServersManagementWindow { ServerFactoryForTests = _ => fake };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.ViewModel.Http.RootDirectory = root;

                Click(window, ServerRowButton(window, "http", "ServerPlay"));
                WaitUntil(() => window.ViewModel.HttpRow.IsRunning, "HTTP server to start");

                window.Close();
                WaitUntil(() => fake.StopCount == 1, "stop on window close");
                RunJobs(window);
                Assert.Equal(1, fake.StopCount);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    private static Button ServerRowButton(ServersManagementWindow window, string id,
        string cssClass)
    {
        var button = window.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Tag is string tag && tag == id && b.Classes.Contains(cssClass));
        Assert.NotNull(button);
        return button!;
    }

    private sealed class FakeServerHost : IServerHost
    {
        public string Name => "HTTP server";

        public bool IsRunning { get; private set; }

        public int Port => 0;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Exception? StartException { get; init; }

        public event Action<string>? LogMessage;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            if (StartException is not null)
            {
                throw StartException;
            }

            IsRunning = true;
            LogMessage?.Invoke("HTTP server started");
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            IsRunning = false;
            LogMessage?.Invoke("HTTP server stopped");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
    /// <summary>Clicks a tool row in the Tools tab (opens or activates its window).</summary>
    private static void ClickToolMenuItem(MainWindow window, string id)
    {
        var item = window.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.Tag is string s && s == id);
        Click(window, item);
        RunJobs(window);
    }

    /// <summary>
    /// Adds an SSH tab whose transport reports itself as connected, without
    /// going near a network. The SFTP pane stays usable through the in-memory
    /// file system.
    /// </summary>
    private static (TerminalTab Tab, SftpPaneViewModel Pane, InMemoryPty Pty) AddConnectedSshTab(
        MainWindow window)
    {
        var viewModel = Vm(window);
        var pty = new InMemoryPty();
        var terminal = new TerminalControl { Process = string.Empty };
        var pane = new SftpPaneViewModel(
            () => new InMemoryFileSystem("/home/test"),
            new SystemLocalFileSystem());
        var tab = new TerminalTab
        {
            Title = "ssh",
            SessionId = "ssh",
            Kind = SessionKind.Ssh,
            Content = new SshSessionView { Terminal = terminal },
            Terminal = terminal,
            SftpPane = pane,
            IsTransportOpen = () => true,
            CloseAction = () => { },
        };
        viewModel.AddTab(tab);
        RunJobs(window);
        terminal.ApplyTemplate();
        terminal.AttachConnection(pty);
        RunJobs(window);
        return (tab, pane, pty);
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

    /// <summary>Clicks a control inside any standalone tool window.</summary>
    private static void Click(Window window, Control control)
    {
        var center = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);
        Assert.NotNull(center);
        window.MouseDown(center!.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
        if (window.IsVisible)
        {
            RunJobs(window);
        }
        else
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void RunJobs(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
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
