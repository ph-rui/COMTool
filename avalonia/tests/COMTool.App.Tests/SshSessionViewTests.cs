using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Sftp.Testing;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

public sealed class SshSessionViewTests
{
    private static SftpPaneViewModel NewPane()
    {
        return new SftpPaneViewModel(
            () => throw new InvalidOperationException("the test has no remote"),
            new InMemoryFileSystem("/local"));
    }

    [AvaloniaFact]
    public void SshSessionViewStacksTheTerminalOverTheDrawer()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var pane = NewPane();
            var terminal = new TerminalControl { Process = string.Empty };
            var view = new SshSessionView { Terminal = terminal, SftpPane = pane };

            var window = new Window { Content = view, Width = 800, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Same(terminal, view.TerminalHostControl.Content);
            Assert.Equal(0, Grid.GetRow(view.TerminalHostControl));
            Assert.Equal(1, Grid.GetRow(view.SftpSplitterControl));
            Assert.Equal(2, Grid.GetRow(view.SftpHostControl));
            Assert.Equal(260, view.DrawerHeight);
            Assert.True(view.SftpHostControl.IsVisible);
            Assert.True(view.SftpSplitterControl.IsVisible);
        });
    }

    [AvaloniaFact]
    public void SshSessionViewHidesTheDrawerWhenCollapsed()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var pane = NewPane();
            var terminal = new TerminalControl { Process = string.Empty };
            var view = new SshSessionView { Terminal = terminal, SftpPane = pane };

            var window = new Window { Content = view, Width = 800, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            pane.IsExpanded = false;
            Dispatcher.UIThread.RunJobs();

            Assert.False(view.SftpHostControl.IsVisible);
            Assert.False(view.SftpSplitterControl.IsVisible);
            Assert.Equal(0, view.DrawerHeight);

            // The drawer comes back with the height it had.
            pane.IsExpanded = true;
            pane.PaneHeight = 400;
            Dispatcher.UIThread.RunJobs();

            Assert.True(view.SftpHostControl.IsVisible);
            Assert.Equal(400, view.DrawerHeight);
        });
    }

    [AvaloniaFact]
    public void TerminalTabExposesTheTerminalAndTheSftpPane()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var pane = NewPane();
            var terminal = new TerminalControl { Process = string.Empty };
            var view = new SshSessionView { Terminal = terminal, SftpPane = pane };

            var tab = new TerminalTab
            {
                Title = "router",
                SessionId = "ssh:root@192.168.1.12:22",
                Kind = SessionKind.Ssh,
                Content = view,
                Terminal = terminal,
                SftpPane = pane,
            };

            Assert.Same(terminal, tab.Terminal);
            Assert.Same(pane, tab.SftpPane);
            Assert.True(tab.IsSshTab);

            var telnet = new TerminalTab
            {
                Title = "switch",
                SessionId = "telnet:root@192.168.1.12:23",
                Kind = SessionKind.Telnet,
                Content = terminal,
            };

            Assert.Null(telnet.Terminal);
            Assert.Null(telnet.SftpPane);
            Assert.False(telnet.IsSshTab);
        });
    }
}
