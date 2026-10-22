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
    public void SshSessionViewPutsThePanelLeftOfTheTerminal()
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
            Assert.Equal(0, Grid.GetColumn(view.SftpHostControl));
            Assert.Equal(1, Grid.GetColumn(view.SftpSplitterControl));
            Assert.Equal(2, Grid.GetColumn(view.TerminalHostControl));
        });
    }

    [AvaloniaFact]
    public void SshSessionViewStartsWithThePanelFoldedAway()
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
            window.UpdateLayout();

            Assert.False(pane.IsExpanded);
            Assert.Equal(0, view.PanelWidth);
            Assert.False(view.SftpHostControl.IsVisible);
            Assert.False(view.SftpSplitterControl.IsVisible);

            // The terminal takes the whole width while the panel is folded
            // away.
            Assert.Equal(800, view.TerminalHostControl.Bounds.Width, 0.5);
        });
    }

    [AvaloniaFact]
    public void SshSessionViewGivesThePanelItsWidthAndKeepsItWhenItIsFoldedAway()
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
            window.UpdateLayout();

            pane.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(420, view.PanelWidth);
            Assert.True(view.SftpHostControl.IsVisible);
            Assert.True(view.SftpSplitterControl.IsVisible);
            Assert.True(
                view.TerminalHostControl.Bounds.Width < 800,
                $"the terminal still takes {view.TerminalHostControl.Bounds.Width}");

            pane.IsExpanded = false;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(0, view.PanelWidth);
            Assert.False(view.SftpHostControl.IsVisible);
            Assert.Equal(800, view.TerminalHostControl.Bounds.Width, 0.5);

            // The panel comes back with the width it had.
            pane.PaneWidth = 480;
            pane.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(480, view.PanelWidth);
            Assert.True(view.SftpHostControl.IsVisible);
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
