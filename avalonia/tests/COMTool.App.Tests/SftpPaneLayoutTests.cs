using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using COMTool.Sftp.Testing;

namespace COMTool.App.Tests;

public sealed class SftpPaneLayoutTests
{
    private static SftpPaneViewModel NewPaneViewModel()
    {
        return new SftpPaneViewModel(
            () => throw new InvalidOperationException("the test has no remote"),
            new InMemoryFileSystem("/local"));
    }

    private static MainWindow CreateWindow()
    {
        var window = new MainWindow { DataContext = new MainViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static MainViewModel Vm(MainWindow window) => (MainViewModel)window.DataContext!;

    private static SessionSidebar Sidebar(MainWindow window) =>
        window.FindControl<SessionSidebar>("Sidebar")!;

    private static SshBrowserPanel Browser(MainWindow window) =>
        Sidebar(window).FindControl<SshBrowserPanel>("SshBrowserPanel")!;

    private static TerminalTab AddTab(MainWindow window, SessionKind kind, SftpPaneViewModel? pane)
    {
        var viewModel = Vm(window);
        var tab = new TerminalTab
        {
            Title = kind.ToString(),
            SessionId = $"{kind}:test",
            Kind = kind,
            Content = new SshSessionView(),
            SftpPane = pane,
        };
        viewModel.AddTab(tab);
        // The rail globe only exists while an SSH transport is live. The
        // test tab reports connected (no IsTransportOpen callback), exactly
        // like a connected shell.
        window.UpdateConnectionStatesForTests();
        RunJobs(window);
        return tab;
    }

    private static Button RailGlobe(MainWindow window) =>
        Sidebar(window).FindControl<Button>("RailSshBrowserButton")!;

    private static void ClickRailGlobe(MainWindow window)
    {
        var globe = RailGlobe(window);
        Assert.True(globe.IsVisible, "the SSH-browser rail icon should be visible");
        globe.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RunJobs(window);
    }

    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Fail($"The panel did not {what} in time.");
    }

    [AvaloniaFact]
    public void SftpPaneShowsASingleRemoteList()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var remote = new InMemoryFileSystem("/remote");
            remote.AddFile("/remote/a.txt", "a");
            var viewModel = new SftpPaneViewModel(() => remote, new InMemoryFileSystem("/local"));
            var pane = new SftpPane { ViewModel = viewModel };
            var window = new Window { Content = pane, Width = 420, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // The MobaXterm-style panel has one list only: the remote
            // directory. There is no local list or local path box.
            Assert.Null(pane.FindControl<Control>("LocalList"));
            Assert.Null(pane.FindControl<Control>("LocalPathBox"));
            Assert.Null(pane.FindControl<Control>("LocalPaneGrid"));
            Assert.NotNull(pane.FindControl<Control>("RemoteList"));
            Assert.NotNull(pane.FindControl<Control>("RemotePathBox"));

            // The toolbar sits at the top, the ".." row follows the column
            // headers, and the list takes the rest of the panel.
            var toolbar = pane.RefreshButtonControl;
            var parent = pane.ParentRowButtonControl;
            var list = pane.RemoteListControl;
            var toolbarBottom = toolbar.TranslatePoint(new Point(0, toolbar.Bounds.Height), window);
            var parentTop = parent.TranslatePoint(new Point(0, 0), window);
            var listTop = list.TranslatePoint(new Point(0, 0), window);

            Assert.NotNull(toolbarBottom);
            Assert.NotNull(parentTop);
            Assert.NotNull(listTop);
            Assert.True(
                parentTop!.Value.Y > toolbarBottom!.Value.Y,
                $"the parent row top is at {parentTop.Value.Y} but the toolbar bottom at {toolbarBottom.Value.Y}");
            Assert.True(
                listTop!.Value.Y > parentTop.Value.Y,
                $"the list top is at {listTop.Value.Y} but the parent row top at {parentTop.Value.Y}");
        });
    }

    [AvaloniaFact]
    public void ToolbarIsIconOnlyWithChineseTooltips()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var pane = new SftpPane { ViewModel = NewPaneViewModel() };
            var window = new Window { Content = pane, Width = 420, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Every toolbar button is an icon (its content is not text) and
            // carries the MobaXterm-style Chinese tooltip.
            Assert.Equal("刷新当前目录", ToolTip.GetTip(pane.RefreshButtonControl));
            Assert.Equal("新建文件夹", ToolTip.GetTip(pane.NewFolderButtonControl));
            Assert.Equal("重命名", ToolTip.GetTip(pane.RenameButtonControl));
            Assert.Equal("删除", ToolTip.GetTip(pane.DeleteButtonControl));
            Assert.Equal("修改权限", ToolTip.GetTip(pane.ChmodButtonControl));
            Assert.Equal("上传到当前文件夹", ToolTip.GetTip(pane.UploadButtonControl));
            Assert.Equal("下载到本地", ToolTip.GetTip(pane.DownloadButtonControl));
            Assert.Equal("跟随符号链接", ToolTip.GetTip(pane.FollowLinksButtonControl));
            Assert.Equal("显示隐藏文件", ToolTip.GetTip(pane.ShowHiddenButtonControl));
            Assert.Equal("返回上级目录", ToolTip.GetTip(pane.ParentRowButtonControl));

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [AvaloniaFact]
    public async Task HeaderMatchesMobaXtermAndShowsTheOwner()
    {
        using var env = new TestEnvironment();
        await env.RunAsync(async () =>
        {
            var remote = new InMemoryFileSystem("/remote") { Owner = "zlgmcu" };
            remote.AddFile("/remote/a.txt", "a");
            var viewModel = new SftpPaneViewModel(() => remote, new InMemoryFileSystem("/local"));
            await viewModel.LoadAsync(CancellationToken.None);

            var pane = new SftpPane { ViewModel = viewModel };
            var window = new Window { Content = pane, Width = 460, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // 名称 / 大小 (KB) / 最后修改 / 权限 / 所有者, as in MobaXterm.
            Assert.Equal("所有者", pane.OwnerHeaderControl.Text);
            Assert.Contains(
                "权限",
                pane.PermissionsHeaderButtonControl.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(text => text.Text));
            Assert.Contains(
                "大小 (KB)",
                pane.SizeHeaderButtonControl.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(text => text.Text));

            // The owner of a row is the name that the file system reports.
            Assert.Contains(
                "zlgmcu",
                pane.RemoteListControl.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(text => text.Text));

            // The path row carries the orange globe of the rail.
            var globe = pane.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(text => text.Text == "\uf57d");
            Assert.NotNull(globe);
            Assert.Equal(Color.Parse("#ff9800"), ((ISolidColorBrush)globe!.Foreground!).Color);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [AvaloniaFact]
    public async Task HeaderRowOrdersTheListAndShowsTheDirection()
    {
        using var env = new TestEnvironment();
        await env.RunAsync(async () =>
        {
            var remote = new InMemoryFileSystem("/remote");
            remote.AddDirectory("/remote/dir");
            remote.AddFile("/remote/small.txt", "a");
            remote.AddFile("/remote/large.txt", new string('x', 4096));
            var viewModel = new SftpPaneViewModel(() => remote, new InMemoryFileSystem("/local"));
            await viewModel.LoadAsync(CancellationToken.None);

            var pane = new SftpPane { ViewModel = viewModel };
            var win = new Window { Content = pane, Width = 420, Height = 600 };
            win.Show();
            Dispatcher.UIThread.RunJobs();

            // Click the size header: directories stay first, files follow
            // by size. Click again: the order turns around.
            pane.SizeHeaderButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                new[] { "dir", "small.txt", "large.txt" },
                viewModel.RemoteEntries.Select(entry => entry.Name).ToArray());
            Assert.Equal("\u25b2", pane.SizeSortGlyphControl.Text);
            Assert.Equal(string.Empty, pane.NameSortGlyphControl.Text);

            pane.SizeHeaderButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                new[] { "dir", "large.txt", "small.txt" },
                viewModel.RemoteEntries.Select(entry => entry.Name).ToArray());
            Assert.Equal("\u25bc", pane.SizeSortGlyphControl.Text);

            win.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [AvaloniaFact]
    public async Task ParentRowGoesToTheParentDirectory()
    {
        using var env = new TestEnvironment();
        await env.RunAsync(async () =>
        {
            var remote = new InMemoryFileSystem("/home/user");
            remote.AddFile("/home/user/a.txt", "a");
            var viewModel = new SftpPaneViewModel(() => remote, new InMemoryFileSystem("/local"));
            await viewModel.LoadAsync(CancellationToken.None);

            var pane = new SftpPane { ViewModel = viewModel };
            var win = new Window { Content = pane, Width = 460, Height = 600 };
            win.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("/home/user", viewModel.RemotePath);

            pane.ParentRowButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => viewModel.RemotePath == "/home", "go to the parent");

            win.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [AvaloniaFact]
    public async Task PathMenuOffersTheParentAndTheHome()
    {
        using var env = new TestEnvironment();
        await env.RunAsync(async () =>
        {
            var remote = new InMemoryFileSystem("/home/user");
            remote.AddDirectory("/home/user/sub");
            var viewModel = new SftpPaneViewModel(() => remote, new InMemoryFileSystem("/local"));
            await viewModel.LoadAsync(CancellationToken.None);
            await viewModel.EnterRemoteAsync(viewModel.RemoteEntries.Single(entry => entry.Name == "sub"));
            Assert.Equal("/home/user/sub", viewModel.RemotePath);

            var pane = new SftpPane { ViewModel = viewModel };
            var win = new Window { Content = pane, Width = 460, Height = 600 };
            win.Show();
            Dispatcher.UIThread.RunJobs();

            var flyout = Assert.IsType<MenuFlyout>(pane.PathMenuButtonControl.Flyout);
            var items = flyout.Items.OfType<MenuItem>().ToList();
            Assert.Equal(new object?[] { "上级目录", "主目录" }, items.Select(item => item.Header));

            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => viewModel.RemotePath == "/home/user", "go to the parent from the path menu");

            await viewModel.EnterRemoteAsync(viewModel.RemoteEntries.Single(entry => entry.Name == "sub"));
            Assert.Equal("/home/user/sub", viewModel.RemotePath);

            items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => viewModel.RemotePath == "/home/user", "go home from the path menu");

            win.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [AvaloniaFact]
    public void BrowserIsClosedByDefault()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var pane = NewPaneViewModel();
                AddTab(window, SessionKind.Ssh, pane);

                Assert.Equal(SidebarTab.Sessions, Vm(window).SidebarTab);
                Assert.False(pane.IsExpanded);
                Assert.False(Browser(window).IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void RailGlobeTogglesTheBrowserInTheSidebar()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var pane = NewPaneViewModel();
                AddTab(window, SessionKind.Ssh, pane);

                var globe = RailGlobe(window);
                var body = window.FindControl<Grid>("BodyGrid")!;
                var content = window.FindControl<Grid>("ContentArea")!;
                var splitter = window.FindControl<GridSplitter>("SidebarSplitter")!;
                Assert.True(globe.IsVisible);
                Assert.False(pane.IsExpanded);

                ClickRailGlobe(window);

                // The browser replaces the sessions panel, the rail stays
                // and the terminal gets the full right side.
                Assert.Equal(SidebarTab.SshBrowser, Vm(window).SidebarTab);
                Assert.True(pane.IsExpanded);
                Assert.True(Browser(window).IsVisible);
                Assert.Same(pane, Browser(window).SftpPaneControl?.ViewModel);
                Assert.Same(pane, Vm(window).SshBrowserPane);
                Assert.True(body.ColumnDefinitions[0].ActualWidth >= 440);
                Assert.Equal(0, content.Margin.Left);
                Assert.True(splitter.IsVisible);

                var tabView = (SshSessionView)Vm(window).SelectedTab!.Content!;
                Assert.Null(tabView.FindControl<Control>("SftpHost"));

                // Clicking the globe again retracts the panel. The 24px
                // rail stays on screen and the terminal keeps its former
                // absolute left edge (24 + 8 = 32).
                ClickRailGlobe(window);

                Assert.Equal(SidebarTab.SshBrowser, Vm(window).SidebarTab);
                Assert.True(Sidebar(window).IsVisible);
                Assert.True(RailGlobe(window).IsVisible);
                Assert.Equal(24, body.ColumnDefinitions[0].ActualWidth, precision: 1);
                Assert.Equal(8, content.Margin.Left);
                Assert.False(splitter.IsVisible);

                // The same icon opens the browser again.
                ClickRailGlobe(window);

                Assert.Equal(SidebarTab.SshBrowser, Vm(window).SidebarTab);
                Assert.True(body.ColumnDefinitions[0].ActualWidth >= 440);
                Assert.Equal(0, content.Margin.Left);
                Assert.True(splitter.IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task OpeningTheBrowserLoadsTheRemoteSide()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        var calls = 0;
        var remote = new InMemoryFileSystem("/remote");
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var pane = new SftpPaneViewModel(
                    () =>
                    {
                        calls++;
                        return remote;
                    },
                    new InMemoryFileSystem("/local"));
                AddTab(window, SessionKind.Ssh, pane);

                // A closed browser does not touch the network.
                await Task.Delay(200);
                Assert.Equal(0, calls);

                ClickRailGlobe(window);

                await WaitUntil(() => calls == 1 && pane.RemotePath == "/remote", "load the remote side");

                // Retracting the panel and opening it again keeps the one
                // remote side that is already there.
                ClickRailGlobe(window);
                ClickRailGlobe(window);
                await Task.Delay(300);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(1, calls);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task ARemoteThatCannotBeReachedOffersTheRetryButton()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var pane = NewPaneViewModel();
                AddTab(window, SessionKind.Ssh, pane);

                ClickRailGlobe(window);

                await WaitUntil(() => pane.HasRemoteError, "report the failure of the remote side");
                RunJobs(window);

                var view = Browser(window).SftpPaneControl;
                Assert.NotNull(view);
                Assert.True(view!.RetryButtonControl.IsVisible);
                Assert.Contains("SFTP unavailable", pane.StatusMessage);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SelectingANonSshTabLeavesTheBrowser()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var pane = NewPaneViewModel();
                var ssh = AddTab(window, SessionKind.Ssh, pane);

                ClickRailGlobe(window);
                Assert.Equal(SidebarTab.SshBrowser, Vm(window).SidebarTab);

                var serial = AddTab(window, SessionKind.Serial, null);
                Vm(window).SelectedTab = serial;
                RunJobs(window);

                Assert.Equal(SidebarTab.Sessions, Vm(window).SidebarTab);
                Assert.False(Browser(window).IsVisible);
                Assert.False(pane.IsExpanded);
                Assert.Same(ssh, Vm(window).Tabs[0]);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SwitchingSshTabsMovesTheBrowserToTheSelectedSession()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();

                var remoteA = new InMemoryFileSystem("/home/a");
                remoteA.AddFile("/home/a/a.txt", "a");
                var remoteB = new InMemoryFileSystem("/home/b");
                remoteB.AddFile("/home/b/b.txt", "b");
                var paneA = new SftpPaneViewModel(() => remoteA, new InMemoryFileSystem("/local"));
                var paneB = new SftpPaneViewModel(() => remoteB, new InMemoryFileSystem("/local"));
                var tabA = AddTab(window, SessionKind.Ssh, paneA);
                var tabB = AddTab(window, SessionKind.Ssh, paneB);
                Vm(window).SelectedTab = tabA;

                ClickRailGlobe(window);
                Assert.Same(paneA, Vm(window).SshBrowserPane);
                Assert.Same(paneA, Browser(window).SftpPaneControl?.ViewModel);

                Vm(window).SelectedTab = tabB;
                RunJobs(window);

                Assert.Equal(SidebarTab.SshBrowser, Vm(window).SidebarTab);
                Assert.False(paneA.IsExpanded);
                Assert.True(paneB.IsExpanded);
                Assert.Same(paneB, Vm(window).SshBrowserPane);
                Assert.Same(paneB, Browser(window).SftpPaneControl?.ViewModel);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TabStripHasNoSftpChip()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                AddTab(window, SessionKind.Ssh, NewPaneViewModel());

                // MobaXterm has no SFTP chip next to the encoding combo:
                // the browser is entered from the orange globe of the rail.
                Assert.Null(window.FindControl<Button>("SftpToggleButton"));
                Assert.NotNull(window.FindControl<ComboBox>("EncodingCombo"));
                Assert.True(RailGlobe(window).IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
