using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using COMTool.Sftp;
using COMTool.Sftp.Testing;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

/// <summary>
/// An SSH tab shows a session view instead of the terminal itself. Selecting
/// such a tab must still put the keyboard into its terminal: the terminal of
/// the tab is named on the tab, and the view that wraps it is not focusable.
/// </summary>
public sealed class SshTabFocusTests
{
    [AvaloniaFact]
    public void UI09_SelectingAnSshTabFocusesItsTerminal()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        SshTab? a = null;
        SshTab? b = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var viewModel = (MainViewModel)window.DataContext!;
                a = OpenSshTab(window, "ssh-a");
                b = OpenSshTab(window, "ssh-b");

                b.Terminal.Focus();
                RunJobs(window);
                Assert.True(b.Terminal.IsKeyboardFocusWithin);

                ClickTab(window, a.Tab);
                RunJobs(window);

                Assert.Same(a.Tab, viewModel.SelectedTab);
                Assert.True(a.Terminal.IsKeyboardFocusWithin);

                window.KeyTextInput("A");
                RunJobs(window);

                Assert.Contains("A", a.Pty.WriterText, StringComparison.Ordinal);
                Assert.DoesNotContain("A", b.Pty.WriterText, StringComparison.Ordinal);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            a?.Dispose();
            b?.Dispose();
        }
    }

    [AvaloniaFact]
    public void UI10_SwitchingAwayFromAnSshTabKeepsItsTerminalAlive()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        SshTab? a = null;
        SshTab? b = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                a = OpenSshTab(window, "ssh-a");
                b = OpenSshTab(window, "ssh-b");

                ClickTab(window, a.Tab);
                RunJobs(window);
                ClickTab(window, b.Tab);
                RunJobs(window);
                ClickTab(window, a.Tab);
                RunJobs(window);

                Assert.True(a.Terminal.IsKeyboardFocusWithin);
                Assert.False(a.Pty.IsClosed);
                Assert.False(b.Pty.IsClosed);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            a?.Dispose();
            b?.Dispose();
        }
    }

    private static SshTab OpenSshTab(MainWindow window, string title)    {
        var viewModel = (MainViewModel)window.DataContext!;
        var pty = new InMemoryPty();
        var terminal = new TerminalControl { Process = string.Empty };
        var pane = new SftpPaneViewModel(
            () => new InMemoryFileSystem("/home/test"),
            new SystemLocalFileSystem());
        var sessionView = new SshSessionView
        {
            Terminal = terminal,
            SftpPane = pane,
        };
        var tab = new TerminalTab
        {
            Title = title,
            SessionId = title,
            Kind = SessionKind.Ssh,
            Content = sessionView,
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
        return new SshTab(tab, terminal, pty);
    }

    private static void ClickTab(MainWindow window, TerminalTab tab)
    {
        var border = window.GetVisualDescendants()
            .OfType<Border>()
            .Single(candidate => ReferenceEquals(candidate.Tag, tab));
        var center = border.TranslatePoint(
            new Point(5, border.Bounds.Height / 2),
            window);
        Assert.NotNull(center);

        window.MouseDown(center.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private sealed record SshTab(TerminalTab Tab, TerminalControl Terminal, InMemoryPty Pty)
        : IDisposable
    {
        public void Dispose() => Pty.Dispose();
    }
}
