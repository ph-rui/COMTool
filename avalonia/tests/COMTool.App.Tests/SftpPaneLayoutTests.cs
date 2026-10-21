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

    [AvaloniaFact]
    public void SftpPaneShowsTheRemotePaneOnTheLeftAndTheLocalPaneOnTheRight()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var pane = new SftpPane { ViewModel = NewPaneViewModel() };
            var window = new Window { Content = pane, Width = 900, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var remote = pane.RemoteListControl.TranslatePoint(new Point(0, 0), window);
            var local = pane.LocalListControl.TranslatePoint(new Point(0, 0), window);

            Assert.NotNull(remote);
            Assert.NotNull(local);
            Assert.True(
                remote!.Value.X < local!.Value.X,
                $"the remote pane is at {remote.Value.X} and the local pane at {local.Value.X}");
        });
    }

    [AvaloniaFact]
    public void DrawerIsExpandedByDefault()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var pane = NewPaneViewModel();
            var view = new SshSessionView
            {
                Terminal = new TerminalControl { Process = string.Empty },
                SftpPane = pane,
            };

            var window = new Window { Content = view, Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(pane.IsExpanded);
            Assert.Equal(260, view.DrawerHeight);
            Assert.True(view.SftpHostControl.IsVisible);
        });
    }

    [AvaloniaFact]
    public void ToggleButtonCollapsesAndRestoresTheDrawer()
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
                Assert.True(button.IsVisible);
                Assert.True(pane.IsExpanded);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunJobs(window);

                Assert.False(pane.IsExpanded);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunJobs(window);

                Assert.True(pane.IsExpanded);
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
