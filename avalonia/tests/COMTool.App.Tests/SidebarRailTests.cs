using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.ViewModels.Tools;
using COMTool.App.Services;
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
                var titles = new[] { "Tools", "宏", "User sessions" };

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
    public void UI16_RailActiveClearedWhenDockCollapsed()
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
                var vm = Vm(window);
                var sidebar = Sidebar(window);
                var sessions = sidebar.FindControl<Button>("RailSessionsButton")!;
                var tools = sidebar.FindControl<Button>("RailToolsButton")!;
                var macros = sidebar.FindControl<Button>("RailMacrosButton")!;
                RunJobs(window);

                // 展开且停在会话页：会话图标高亮，其余不高亮。
                Assert.True(vm.IsSidebarVisible);
                Assert.True(sessions.Classes.Contains("active"));
                Assert.False(tools.Classes.Contains("active"));
                Assert.False(macros.Classes.Contains("active"));

                // 收起面板：rail 图标不应保持高亮（用户反馈：收起后图标像被点住）。
                Click(window, sessions);
                Assert.False(vm.IsSidebarVisible);
                Assert.False(sessions.Classes.Contains("active"));
                Assert.False(tools.Classes.Contains("active"));
                Assert.False(macros.Classes.Contains("active"));

                // 重新展开：当前页图标恢复高亮。
                Click(window, sessions);
                Assert.True(vm.IsSidebarVisible);
                Assert.True(sessions.Classes.Contains("active"));
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

                // 点选“网络服务器”：打开独立顶层窗（无 Owner，任务栏靠同进程 AUMID
                // 归组、悬停出缩略图预览、主窗最小化不带走），重复点选只激活同一实例
                ClickToolMenuItem(window, "net-server");
                var servers = Assert.IsType<ServersManagementWindow>(
                    window!.FindToolWindowForTests("net-server"));
                Assert.True(servers.IsVisible);
                Assert.Null(servers.Owner);
                ClickToolMenuItem(window, "net-server");
                Assert.Single(window.ToolWindowsForTests);
                Assert.Same(servers, window.FindToolWindowForTests("net-server"));

                // 点选“网络扫描仪”：再开一个独立窗口
                ClickToolMenuItem(window, "scanner");
                var scanner = Assert.IsType<NetworkScanWindow>(
                    window.FindToolWindowForTests("scanner"));
                Assert.True(scanner.IsVisible);
                Assert.Null(scanner.Owner);
                Assert.Equal(2, window.ToolWindowsForTests.Count);

                // 点选“端口扫描仪”：第三个窗口
                ClickToolMenuItem(window, "port-scanner");
                var portScanner = Assert.IsType<PortScanWindow>(
                    window.FindToolWindowForTests("port-scanner"));
                Assert.True(portScanner.IsVisible);
                Assert.Null(portScanner.Owner);
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
    public void UI16_MainWindowClosesDetachedTools()
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
                vm.SidebarTab = SidebarTab.Tools;
                RunJobs(window);

                ClickToolMenuItem(window, "scanner");
                var scanner = Assert.IsType<NetworkScanWindow>(
                    window.FindToolWindowForTests("scanner"));
                Assert.Null(scanner.Owner);

                window.Close();
                RunJobs(window);

                Assert.False(scanner.IsVisible);
                Assert.Empty(window.ToolWindowsForTests);
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
                    // 网络扫描只保留固定 7 服务列，不再探测/显示“其他端口”
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
                Assert.Equal("127.0.0.2", vm.Rows[1].Address);
                Assert.All(vm.Rows[1].Cells, c => Assert.False(c.IsOpen));
                Assert.Equal("完成：发现 2 台在线设备", vm.Status);

                // 10 列减为 9 列：IP/名称 + 七个服务状态列
                var header = window.FindControl<Grid>("ScanHeaderGrid")!;
                Assert.Equal(9, header.ColumnDefinitions.Count);
                Assert.DoesNotContain(
                    window.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "其他端口");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_ScanRowsAppearIncrementally()
    {
        using var env = new TestEnvironment();
        NetworkScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new NetworkScanWindow
                {
                    // 发现阶段先报 SSH(22) 已确认，名称留空；完成阶段才补名称并定格 7 列
                    ScannerForTests = new FakeScanner(new List<NetworkScanHost>
                    {
                        new(new IPAddress(new byte[] { 192, 168, 1, 10 }), "host10", new[] { 22 }, 1),
                    })
                    // DiscoverDelayMs=20ms 出 discovered；DelayMs=500ms 拉开与 completed 的间隔，
                    // 保证扫描中途能稳定观察到 "…" 未定格状态
                    { DelayMs = 500, DiscoverDelayMs = 20, DiscoveredOpenPorts = new[] { 22 } },
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;
                vm.OctetTo = "2"; // 缩到 2 个目标，加快测试

                Click(window, window.FindControl<Button>("ScanStartButton")!);

                // 扫描中途：行先出现（discovered）——名称空、SSH 已确认、其余 6 列 "…"
                var rowAppeared = WaitUntilTry(() => vm.Rows.Count == 1, 10);
                Assert.True(rowAppeared, "row not inserted on discovery");
                var row = vm.Rows[0];
                Assert.Equal("192.168.1.10", row.Address);
                Assert.Equal("", row.HostName);
                Assert.True(row.Cells[0].IsOpen);
                Assert.False(row.Cells[0].IsPending);
                Assert.All(row.Cells.Skip(1), c => Assert.True(c.IsPending));
                RunJobs(window);
                var ellipses = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(tb => tb.Text == "\uf141").ToList();
                Assert.Equal(6, ellipses.Count);

                // 扫描完成：名称回填、7 列全部定格
                var finished = WaitUntilTry(() => vm.Status.Contains("完成"), 10);
                Assert.True(finished, $"scan did not finish: status='{vm.Status}'");
                RunJobs(window);
                Assert.Equal("host10", row.HostName);
                Assert.All(row.Cells, c => Assert.False(c.IsPending));
                Assert.True(row.Cells[0].IsOpen);
                Assert.All(row.Cells.Skip(1), c => Assert.False(c.IsOpen));
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                    tb => tb.Text == "\uf141");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_ScanResultColumnsMatchMobaXtermWidths()
    {
        using var env = new TestEnvironment();
        NetworkScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new NetworkScanWindow
                {
                    ScannerForTests = new FakeScanner(new List<NetworkScanHost>
                    {
                        new(new IPAddress(new byte[] { 127, 0, 0, 1 }), "loopback", new[] { 22 }, 1),
                    }) { DelayMs = 1 },
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Click(window, window.FindControl<Button>("ScanStartButton")!);
                Assert.True(WaitUntilTry(() => window.ViewModel.Rows.Count == 1, 10));
                RunJobs(window);

                // 名称列 90 → 160：SSH 列中心随之到 ≈318，与 MobaXterm 实测
                // (≈317.5) 对齐，整块服务列不再偏左。
                var expected = new[] { 120d, 160d, 60d, 60d, 60d, 60d, 60d, 60d, 60d };
                var header = window.FindControl<Grid>("ScanHeaderGrid")!;
                Assert.Equal(9, header.ColumnDefinitions.Count);
                Assert.Equal(
                    expected,
                    header.ColumnDefinitions.Select(c => c.Width.Value).ToArray());

                // 表头与数据行必须共用同一组列宽
                var rowGrids = window.GetVisualDescendants()
                    .OfType<Grid>()
                    .Where(g => g.ColumnDefinitions.Count == 9 && !ReferenceEquals(g, header))
                    .ToList();
                Assert.NotEmpty(rowGrids);
                Assert.All(rowGrids, grid => Assert.Equal(
                    expected,
                    grid.ColumnDefinitions.Select(c => c.Width.Value).ToArray()));

                // “IP 地址/名称”表头居中（MobaXterm 实测）
                var headerTexts = header.Children.OfType<TextBlock>().ToList();
                Assert.Equal(2, headerTexts.Count);
                Assert.All(headerTexts,
                    text => Assert.Equal(TextAlignment.Center, text.TextAlignment));

                // Excel 式表头分栏：列 1..8 各有一条左缘分隔线
                var dividers = header.Children.OfType<Border>().Count(
                    border => border.BorderThickness.Left == 1
                              && border.BorderThickness.Right == 0);
                Assert.Equal(8, dividers);

                // 数据行 √/× 在 60px 单元格内必须水平居中，才能正对表头
                //（只在数据行里找，避免把“关闭”按钮的 ✕ 图标算进来）
                var glyphs = rowGrids
                    .SelectMany(grid => grid.GetVisualDescendants().OfType<TextBlock>())
                    .Where(text => text.Text == "\uf00c" || text.Text == "\uf00d")
                    .ToList();
                Assert.Equal(7, glyphs.Count); // 1 行 × 7 服务列
                Assert.All(glyphs,
                    glyph => Assert.Equal(TextAlignment.Center, glyph.TextAlignment));

                // IP 地址 / 名称 数据单元格也必须居中，与居中的表头对齐
                var cellTexts = rowGrids
                    .SelectMany(grid => grid.Children.OfType<TextBlock>())
                    .Where(text => Grid.GetColumn(text) is 0 or 1)
                    .ToList();
                Assert.Equal(2, cellTexts.Count); // 1 行 × 列 0/1
                Assert.All(cellTexts,
                    text => Assert.Equal(TextAlignment.Center, text.TextAlignment));
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_ScanRangeIsRememberedOnCloseAndRestored()
    {
        using var env = new TestEnvironment();
        NetworkScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = AppConfigService.Load();
                window = new NetworkScanWindow(config);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var vm = window.ViewModel;
                vm.Octet1 = "10";
                vm.Octet2 = "13";
                vm.Octet3 = "1";
                vm.OctetFrom = "20";
                vm.OctetTo = "40";

                // 关窗时把合法范围写入配置（即使没有扫描）
                window.Close();
                Dispatcher.UIThread.RunJobs();
                window = null;

                var saved = AppConfigService.Load().Config.NetworkScan;
                Assert.Equal("10", saved.Octet1);
                Assert.Equal("13", saved.Octet2);
                Assert.Equal("1", saved.Octet3);
                Assert.Equal("20", saved.From);
                Assert.Equal("40", saved.To);

                // 重新打开窗口恢复上次范围，而不是默认 192.168.1.1→254
                window = new NetworkScanWindow(AppConfigService.Load());
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("10", window.ViewModel.Octet1);
                Assert.Equal("13", window.ViewModel.Octet2);
                Assert.Equal("1", window.ViewModel.Octet3);
                Assert.Equal("20", window.ViewModel.OctetFrom);
                Assert.Equal("40", window.ViewModel.OctetTo);

                // 非法范围不覆盖已记住的值
                window.ViewModel.Octet1 = "300";
                window.Close();
                Dispatcher.UIThread.RunJobs();
                window = null;
                Assert.Equal("10", AppConfigService.Load().Config.NetworkScan.Octet1);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_ScanRangeIsRememberedOnStartScan()
    {
        using var env = new TestEnvironment();
        NetworkScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new NetworkScanWindow(AppConfigService.Load())
                {
                    ScannerForTests = new FakeScanner(new List<NetworkScanHost>()) { DelayMs = 1 },
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var vm = window.ViewModel;
                vm.Octet1 = "172";
                vm.Octet2 = "16";
                vm.Octet3 = "66";
                vm.OctetFrom = "1";
                vm.OctetTo = "10";

                Click(window, window.FindControl<Button>("ScanStartButton")!);
                Assert.True(WaitUntilTry(() => vm.Status.Contains("完成"), 10));

                // 点 Start scan 就开始记忆，无需等到关窗
                var saved = AppConfigService.Load().Config.NetworkScan;
                Assert.Equal("172", saved.Octet1);
                Assert.Equal("16", saved.Octet2);
                Assert.Equal("66", saved.Octet3);
                Assert.Equal("1", saved.From);
                Assert.Equal("10", saved.To);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_ScanNumericInputsAreCenteredAndEqualWidth()
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

                var names = new[]
                {
                    "ScanOctet1Box", "ScanOctet2Box", "ScanOctet3Box",
                    "ScanOctetFromBox", "ScanOctetToBox",
                };
                var boxes = names
                    .Select(name => window.FindControl<TextBox>(name)!)
                    .ToArray();
                Assert.All(boxes, box => Assert.Equal(TextAlignment.Center, box.TextAlignment));
                Assert.All(boxes, box => Assert.Equal(32, box.Width));
                Assert.All(boxes, box => Assert.Equal(24, box.Height));
                Assert.All(boxes, box => Assert.Equal(3, box.MaxLength));
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_ScanIpRowIsCompactAndShiftedRight()
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

                var fieldsPanel = window.FindControl<StackPanel>("ScanIpFieldsPanel")!;
                var label = window.FindControl<TextBlock>("ScanIpLabel")!;
                var firstBox = window.FindControl<TextBox>("ScanOctet1Box")!;
                var start = window.FindControl<Button>("ScanStartButton")!;

                // MobaXterm 的 IP 行不是贴着窗口左边缘：标签右移到“名称”
                // 列附近，五个输入框缩小到 32px，Start scan 仍留在原位置。
                Assert.Equal(110, fieldsPanel.Margin.Left);
                var labelX = label.TranslatePoint(new Point(0, 0), window)!.Value.X;
                var firstBoxX = firstBox.TranslatePoint(new Point(0, 0), window)!.Value.X;
                var startX = start.TranslatePoint(new Point(0, 0), window)!.Value.X;
                Assert.Equal(32, firstBox.Bounds.Width);
                Assert.InRange(labelX, 125, 145);
                Assert.InRange(firstBoxX, 220, 245);
                Assert.InRange(startX, 505, 530);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET05_ScanOctetAutoAdvancesAtThreeDigits()
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

                var first = window.FindControl<TextBox>("ScanOctet1Box")!;
                var second = window.FindControl<TextBox>("ScanOctet2Box")!;
                var third = window.FindControl<TextBox>("ScanOctet3Box")!;
                var from = window.FindControl<TextBox>("ScanOctetFromBox")!;
                var to = window.FindControl<TextBox>("ScanOctetToBox")!;

                // 初始化绑定 192/168/... 时没有焦点，不应误跳到下一格。
                first.Text = "192";
                RunJobs(window);
                Assert.False(first.IsFocused);
                Assert.False(second.IsFocused);

                // 聚焦时全选原值，直接输入可覆盖。
                first.Focus();
                RunJobs(window);
                Assert.True(first.IsFocused);
                Assert.Equal(0, first.SelectionStart);
                Assert.Equal(first.Text!.Length, first.SelectionEnd);

                // 输入满 3 位且有效才跳转。
                first.Text = "19";
                RunJobs(window);
                Assert.True(first.IsFocused);
                Assert.False(second.IsFocused);

                first.Text = "192";
                RunJobs(window);
                Assert.True(second.IsFocused);
                Assert.Equal("192", first.Text);
                Assert.Equal(0, second.SelectionStart);
                Assert.Equal(second.Text!.Length, second.SelectionEnd);

                // 999 超过 0–255，不跳转。
                second.Text = "999";
                RunJobs(window);
                Assert.True(second.IsFocused);
                Assert.False(third.IsFocused);

                // 下一格输入有效值后继续跳转，最后一段不再越界。
                second.Text = "168";
                RunJobs(window);
                Assert.True(third.IsFocused);
                third.Text = "1";
                RunJobs(window);
                Assert.True(third.IsFocused);
                third.Text = "001";
                RunJobs(window);
                Assert.True(from.IsFocused);

                to.Focus();
                RunJobs(window);
                to.Text = "25";
                RunJobs(window);
                to.Text = "254";
                RunJobs(window);
                Assert.True(to.IsFocused);
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
    public void NET05_StopButtonCancelsScan()
    {
        using var env = new TestEnvironment();
        NetworkScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new NetworkScanWindow
                {
                    ScannerForTests = new FakeScanner(new List<NetworkScanHost>
                    {
                        new(new IPAddress(new byte[] { 127, 0, 0, 1 }), "loopback", new[] { 80 }, 1),
                    }) { DelayMs = 50 },
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;

                Click(window, window.FindControl<Button>("ScanStartButton")!);
                WaitUntil(() => vm.Rows.Count == 1, "first scan row");
                var stop = window.FindControl<Button>("NetScanStopButton")!;
                Assert.True(stop.IsEnabled);

                Click(window, stop);
                WaitUntil(() => !vm.IsScanning, "scan to stop");

                // 停止后保留已发现结果，不回到“完成”
                Assert.Single(vm.Rows);
                Assert.StartsWith("已停止", vm.Status);
                Assert.False(stop.IsEnabled);
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

    [AvaloniaFact]
    public void PORT01_PortScanIpAndRangeAreRemembered()
    {
        using var env = new TestEnvironment();
        PortScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = AppConfigService.Load();
                window = new PortScanWindow(config);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var vm = window.ViewModel;
                vm.Ip = "192.0.2.251";
                vm.From = "1000";
                vm.To = "2000";

                // 关窗时把合法输入写入配置（即使没有扫描）
                window.Close();
                Dispatcher.UIThread.RunJobs();
                window = null;

                var saved = AppConfigService.Load().Config.PortScan;
                Assert.Equal("192.0.2.251", saved.Ip);
                Assert.Equal("1000", saved.From);
                Assert.Equal("2000", saved.To);

                // 重新打开窗口恢复上次输入，而不是默认 127.0.0.1 / 1→65535
                window = new PortScanWindow(AppConfigService.Load());
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("192.0.2.251", window.ViewModel.Ip);
                Assert.Equal("1000", window.ViewModel.From);
                Assert.Equal("2000", window.ViewModel.To);

                // 非法 IP 不覆盖已记住的值；非法端口范围同样不落盘
                window.ViewModel.Ip = "not-an-ip";
                window.ViewModel.From = "99999";
                window.ViewModel.To = "1";
                window.Close();
                Dispatcher.UIThread.RunJobs();
                window = null;

                var kept = AppConfigService.Load().Config.PortScan;
                Assert.Equal("192.0.2.251", kept.Ip);
                Assert.Equal("1000", kept.From);
                Assert.Equal("2000", kept.To);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void PORT01_PortScanIpIsRememberedOnStartScan()
    {
        using var env = new TestEnvironment();
        PortScanWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new PortScanWindow(AppConfigService.Load())
                {
                    ScannerForTests = new FakeScanner(Array.Empty<NetworkScanHost>())
                    {
                        DeepPorts = new[] { 22 },
                    },
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var vm = window.ViewModel;
                vm.Ip = "192.0.2.163";

                Click(window, window.FindControl<Button>("PortScanStartButton")!);
                Assert.True(WaitUntilTry(
                    () => !vm.IsScanning && vm.Result.Length > 0, 10),
                    $"port scan did not finish: status='{vm.Status}'");

                // 点 Start scan 就开始记忆，无需等到关窗
                var saved = AppConfigService.Load().Config.PortScan;
                Assert.Equal("192.0.2.163", saved.Ip);
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
                window = new ServersManagementWindow(new ServersManagementViewModel(null));
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
                Assert.Equal(ServerConfigKind.Nfs, vm.Find("nfs")!.Kind);
                Assert.Equal(ServerConfigKind.Vnc, vm.Find("vnc")!.Kind);
                Assert.False(vm.Rows.Any(r => r.IsSelected));

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
    public void SRV01_NumericInputsShareWidth()
    {
        // MobaXterm 22.1 经典微调框：数值左对齐（自定义 ClassicNum 模板 1:1），
        // 各数值框统一 60px 宽。
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new ServersManagementWindow(new ServersManagementViewModel(null));
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var names = new[]
                {
                    "HttpPortBox", "HttpSecondsBox", "TftpPortBox", "TftpSecondsBox",
                };
                var boxes = names
                    .Select(name => window.FindControl<NumericUpDown>(name)!)
                    .ToArray();
                Assert.All(boxes, box => Assert.Equal(TextAlignment.Start, box.TextAlignment));
                Assert.All(boxes, box => Assert.Equal(60, box.Width));
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SRV01_SpinnerArrowsChangeValue()
    {
        // 回归（2026-10-26 实机：上下箭头点击无响应）：ClassicNum 模板必须包含
        // ButtonSpinner#PART_Spinner，NumericUpDown.OnApplyTemplate 才能订阅 Spin 事件；
        // PART_IncreaseButton / PART_DecreaseButton 必须位于 ButtonSpinner 模板作用域内。
        // 同时数值区（PART_TextBox）宽度必须足够显示 4 位数。
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new ServersManagementWindow(new ServersManagementViewModel(null));
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var vm = window.ViewModel;
                var pairs = new (string Server, string BoxName)[]
                {
                    ("http", "HttpPortBox"),
                    ("http", "HttpSecondsBox"),
                    ("tftp", "TftpPortBox"),
                    ("tftp", "TftpSecondsBox"),
                };
                foreach (var (server, boxName) in pairs)
                {
                    // 面板默认隐藏（欢迎页），先选中服务器让控件可见可命中
                    vm.Select(vm.Find(server));
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();

                    var box = window.FindControl<NumericUpDown>(boxName)!;

                    var spinner = box.GetVisualDescendants()
                        .OfType<ButtonSpinner>().FirstOrDefault();
                    Assert.NotNull(spinner);
                    Assert.Equal("PART_Spinner", spinner!.Name);

                    var inc = box.GetVisualDescendants().OfType<RepeatButton>()
                        .FirstOrDefault(b => b.Name == "PART_IncreaseButton");
                    var dec = box.GetVisualDescendants().OfType<RepeatButton>()
                        .FirstOrDefault(b => b.Name == "PART_DecreaseButton");
                    Assert.NotNull(inc);
                    Assert.NotNull(dec);

                    var v0 = box.Value;
                    Click(window, inc!);
                    Assert.True((int)box.Value == (int)(v0 + 1), $"{boxName} 上箭头未生效");
                    Click(window, dec!);
                    Click(window, dec!);
                    Assert.True((int)box.Value == (int)(v0 - 1), $"{boxName} 下箭头未生效");
                    box.Value = v0;
                    Dispatcher.UIThread.RunJobs();

                    var tb = box.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                    Assert.NotNull(tb);
                    Assert.True(tb!.Bounds.Width >= 30,
                        $"{boxName} 数值区宽度 {tb.Bounds.Width:F0}px 不足以显示 4 位数");

                    // 回归（2026-10-27 实机"小键盘吞键"）：键盘焦点只属于编辑框。
                    // 点 ▲/▼ 不得抢走焦点（RepeatButton/ButtonSpinner/NumericUpDown
                    // 均 Focusable=False），否则后续输入落到按钮上被吞
                    // （对齐 MobaXterm 的 Win32 buddy up-down 行为）
                    Assert.False(inc!.Focusable, $"{boxName} PART_IncreaseButton 必须 Focusable=False");
                    Assert.False(dec!.Focusable, $"{boxName} PART_DecreaseButton 必须 Focusable=False");
                    Assert.False(spinner.Focusable, $"{boxName} ButtonSpinner 必须 Focusable=False");
                    Assert.False(box.Focusable, $"{boxName} NumericUpDown 必须 Focusable=False");
                    Assert.True(tb.Focusable, $"{boxName} PART_TextBox 必须可聚焦");

                    var fm = window.FocusManager;
                    Assert.NotNull(fm);
                    fm!.Focus(tb);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(ReferenceEquals(tb, fm.GetFocusedElement()),
                        $"{boxName}: 无法将焦点设到编辑框（当前焦点 {fm.GetFocusedElement()?.GetType().Name}）");
                    Click(window, inc!);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(ReferenceEquals(tb, fm.GetFocusedElement()),
                        $"{boxName}: 点上箭头后焦点被抢走（当前焦点 {fm.GetFocusedElement()?.GetType().Name}）");
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
    public void SRV01_SelectingRowShowsMatchingConfig()
    {
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new ServersManagementWindow(new ServersManagementViewModel(null));
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
                window = new ServersManagementWindow(new ServersManagementViewModel(null)) { ServerFactoryForTests = _ => fake };
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
                window = new ServersManagementWindow(new ServersManagementViewModel(null)) { ServerFactoryForTests = _ => fake };
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
    public void SRV01_ClosingWindowKeepsServersRunningAndRestoresOnReopen()
    {
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        ServersManagementWindow? reopened = null;
        var root = Path.Combine(Path.GetTempPath(), "comtool-srv-close",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            env.Run(() =>
            {
                var fake = new FakeServerHost();
                var vm = new ServersManagementViewModel(null);
                window = new ServersManagementWindow(vm) { ServerFactoryForTests = _ => fake };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.ViewModel.Http.RootDirectory = root;

                Click(window, ServerRowButton(window, "http", "ServerPlay"));
                WaitUntil(() => window.ViewModel.HttpRow.IsRunning, "HTTP server to start");
                Assert.Equal(1, fake.StartCount);

                // 对齐 MobaXterm：关窗不停服务
                window.Close();
                Dispatcher.UIThread.RunJobs();
                RunJobs(window);
                Assert.True(fake.StopCount == 0, "关窗不应停止服务器");
                Assert.True(fake.IsRunning, "关窗后服务器应保持运行");

                // 重开窗口（同一共享 VM）：运行状态与按钮可用态恢复，不重复启动
                reopened = new ServersManagementWindow(vm) { ServerFactoryForTests = _ => fake };
                reopened.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(reopened.ViewModel.HttpRow.IsRunning, "重开后运行状态应恢复");
                Assert.True(fake.StartCount == 1, "重开窗口不应重复启动服务器");
                Assert.False(ServerRowButton(reopened, "http", "ServerPlay").IsEnabled);
                Assert.True(ServerRowButton(reopened, "http", "ServerStop").IsEnabled);

                // 面板内点 ■ 正常停止
                Click(reopened, ServerRowButton(reopened, "http", "ServerStop"));
                WaitUntil(() => fake.StopCount == 1, "stop via reopened window");
                Assert.Equal(1, fake.StopCount);
            });
        }
        finally
        {
            window?.Close();
            reopened?.Close();
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

        /// <summary>When >0: report a discovered (partial) host before its completed report.</summary>
        public int DiscoverDelayMs { get; init; }

        /// <summary>Open ports carried by the discovered (partial) report.</summary>
        public IReadOnlyList<int> DiscoveredOpenPorts { get; init; } = Array.Empty<int>();

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
            IProgress<NetworkScanHost>? discovered,
            CancellationToken cancellationToken)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (i < _hosts.Count && DiscoverDelayMs > 0)
                {
                    await Task.Delay(DiscoverDelayMs, cancellationToken);
                    discovered?.Report(new NetworkScanHost(
                        _hosts[i].Address, null, DiscoveredOpenPorts, null));
                }

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
