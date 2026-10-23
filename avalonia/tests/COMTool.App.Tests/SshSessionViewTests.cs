using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

public sealed class SshSessionViewTests
{
    [AvaloniaFact]
    public void SshSessionViewShowsOnlyTheTerminal()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var terminal = new TerminalControl { Process = string.Empty };
            var view = new SshSessionView { Terminal = terminal };

            var window = new Window { Content = view, Width = 800, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Same(terminal, view.TerminalHostControl.Content);
            Assert.Null(view.FindControl<Control>("SftpHost"));
            Assert.Null(view.FindControl<Control>("SftpSplitter"));

            // The remote files are not a drawer of the tab any more: the
            // terminal fills the whole view and the browser lives in the
            // sidebar.
            Assert.Equal(800, view.TerminalHostControl.Bounds.Width, 0.5);
            Assert.Equal(600, view.TerminalHostControl.Bounds.Height, 0.5);
        });
    }

    [AvaloniaFact]
    public void TerminalTabExposesTheTerminalAndTheSftpPane()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var terminal = new TerminalControl { Process = string.Empty };
            var view = new SshSessionView { Terminal = terminal };
            var pane = new SftpPaneViewModel(
                () => throw new InvalidOperationException("the test has no remote"));

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
