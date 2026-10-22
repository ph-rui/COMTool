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
    public void SftpPaneStacksTheRemotePaneOverTheLocalPane()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var pane = new SftpPane { ViewModel = NewPaneViewModel() };
            var window = new Window { Content = pane, Width = 420, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var remote = pane.RemoteListControl.TranslatePoint(new Point(0, 0), window);
            var local = pane.LocalListControl.TranslatePoint(new Point(0, 0), window);

            Assert.NotNull(remote);
            Assert.NotNull(local);
            Assert.True(
                remote!.Value.Y < local!.Value.Y,
                $"the remote pane is at {remote.Value.Y} and the local pane at {local.Value.Y}");
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
