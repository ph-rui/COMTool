using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using COMTool.Sftp.Testing;
using Iciclecreek.Terminal;

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

    private static SshSessionView? SessionView(MainWindow window)
    {
        var viewModel = (MainViewModel)window.DataContext!;
        return viewModel.SelectedTab?.Content as SshSessionView;
    }

    private static TerminalTab AddTab(MainWindow window, SessionKind kind, SftpPaneViewModel? pane)
    {
        var viewModel = (MainViewModel)window.DataContext!;
        var tab = new TerminalTab
        {
            Title = kind.ToString(),
            SessionId = $"{kind}:test",
            Kind = kind,
            Content = new SshSessionView { SftpPane = pane },
            SftpPane = pane,
        };
        viewModel.AddTab(tab);
        RunJobs(window);
        return tab;
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

            // The list sits under the toolbar and header row.
            var toolbar = pane.UpButtonControl;
            var list = pane.RemoteListControl;
            var toolbarBottom = toolbar.TranslatePoint(new Point(0, toolbar.Bounds.Height), window);
            var listTop = list.TranslatePoint(new Point(0, 0), window);

            Assert.NotNull(toolbarBottom);
            Assert.NotNull(listTop);
            Assert.True(
                listTop!.Value.Y > toolbarBottom!.Value.Y,
                $"the list top is at {listTop.Value.Y} but the toolbar bottom at {toolbarBottom.Value.Y}");
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
            Assert.Equal("返回上级目录", ToolTip.GetTip(pane.UpButtonControl));
            Assert.Equal("刷新当前目录", ToolTip.GetTip(pane.RefreshButtonControl));
            Assert.Equal("新建文件夹", ToolTip.GetTip(pane.NewFolderButtonControl));
            Assert.Equal("重命名", ToolTip.GetTip(pane.RenameButtonControl));
            Assert.Equal("删除", ToolTip.GetTip(pane.DeleteButtonControl));
            Assert.Equal("修改权限", ToolTip.GetTip(pane.ChmodButtonControl));
            Assert.Equal("上传到当前文件夹", ToolTip.GetTip(pane.UploadButtonControl));
            Assert.Equal("下载到本地", ToolTip.GetTip(pane.DownloadButtonControl));
            Assert.Equal("跟随符号链接", ToolTip.GetTip(pane.FollowLinksButtonControl));
            Assert.Equal("显示隐藏文件", ToolTip.GetTip(pane.ShowHiddenButtonControl));

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
    public void PanelIsFoldedAwayByDefault()
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

                var view = SessionView(window)!;

                Assert.False(pane.IsExpanded);
                Assert.False(view.SftpHostControl.IsVisible);
                Assert.Equal(0, view.PanelWidth);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void ToggleButtonOpensAndFoldsThePanel()
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

                var button = window.FindControl<Button>("SftpToggleButton")!;
                var view = SessionView(window)!;
                Assert.True(button.IsVisible);
                Assert.False(pane.IsExpanded);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunJobs(window);

                Assert.True(pane.IsExpanded);
                Assert.True(view.SftpHostControl.IsVisible);
                Assert.Equal(420, view.PanelWidth);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunJobs(window);

                Assert.False(pane.IsExpanded);
                Assert.False(view.SftpHostControl.IsVisible);
                Assert.Equal(0, view.PanelWidth);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task OpeningThePanelLoadsTheRemoteSide()
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

                // A folded panel does not touch the network.
                await Task.Delay(200);
                Assert.Equal(0, calls);

                var button = window.FindControl<Button>("SftpToggleButton")!;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                await WaitUntil(() => calls == 1 && pane.RemotePath == "/remote", "load the remote side");

                // Folding the panel and opening it again keeps the one
                // remote side that is already there.
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunJobs(window);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                var tab = AddTab(window, SessionKind.Ssh, pane);
                var view = SessionView(window)!;
                var sftpPaneView = (SftpPane)view.SftpHostControl.Content!;

                Assert.False(sftpPaneView.RetryButtonControl.IsVisible);

                var button = window.FindControl<Button>("SftpToggleButton")!;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                await WaitUntil(() => pane.HasRemoteError, "report the failure of the remote side");

                Assert.Contains("SFTP unavailable", pane.StatusMessage);
                Assert.True(sftpPaneView.RetryButtonControl.IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void ToggleButtonSitsLeftOfTheEncodingCombo()
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

                var button = window.FindControl<Button>("SftpToggleButton")!;
                var combo = window.FindControl<ComboBox>("EncodingCombo")!;

                var buttonRight = button.TranslatePoint(new Point(button.Bounds.Width, 0), window);
                var comboLeft = combo.TranslatePoint(new Point(0, 0), window);

                Assert.NotNull(buttonRight);
                Assert.NotNull(comboLeft);
                Assert.True(
                    buttonRight!.Value.X < comboLeft!.Value.X,
                    $"the button ends at {buttonRight.Value.X} and the combo starts at {comboLeft.Value.X}");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void ToggleButtonIsHiddenForNonSshTabs()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();

                var button = window.FindControl<Button>("SftpToggleButton")!;

                AddTab(window, SessionKind.Serial, null);
                Assert.False(button.IsVisible);

                AddTab(window, SessionKind.Tcp, null);
                Assert.False(button.IsVisible);

                AddTab(window, SessionKind.Local, null);
                Assert.False(button.IsVisible);

                AddTab(window, SessionKind.Ssh, NewPaneViewModel());
                Assert.True(button.IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
