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
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

public sealed class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void UI02_SidebarExpandsOnLaunch()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.SessionSidebar.Visible = false;
                config.SessionSidebar.Width = 240;
                env.SeedConfig(config);

                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var sidebar = window.FindControl<SessionSidebar>("Sidebar")!;
                Assert.True(sidebar.IsVisible);
                Assert.True(sidebar.Bounds.Width >= 200);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI05_TabSwitchReparentOrder()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        TestTerminal? a = null;
        TestTerminal? b = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var viewModel = (MainViewModel)window.DataContext!;
                a = OpenTab(window, "a");
                b = OpenTab(window, "b");

                ClickTab(window, a.Tab);
                RunJobs(window);
                ClickTab(window, b.Tab);
                RunJobs(window);
                ClickTab(window, a.Tab);
                RunJobs(window);

                Assert.Same(a.Tab, viewModel.SelectedTab);
                Assert.True(a.Tab.IsSelected);
                Assert.True(a.Terminal.IsKeyboardFocusWithin);

                window.KeyTextInput("X");
                RunJobs(window);

                Assert.Contains("X", a.Pty.WriterText, StringComparison.Ordinal);
                Assert.False(b.Pty.IsClosed);
                Assert.False(a.Pty.IsClosed);
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
    public void UI05_TabSwitchFocusesIncomingTerminal()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        TestTerminal? a = null;
        TestTerminal? b = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var viewModel = (MainViewModel)window.DataContext!;
                a = OpenTab(window, "a");
                b = OpenTab(window, "b");

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
    public void UI05_RapidSwitchFocus()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        TestTerminal? a = null;
        TestTerminal? b = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var viewModel = (MainViewModel)window.DataContext!;
                a = OpenTab(window, "a");
                b = OpenTab(window, "b");

                ClickTab(window, b.Tab);
                ClickTab(window, a.Tab);
                RunJobs(window);

                Assert.Same(a.Tab, viewModel.SelectedTab);
                Assert.True(a.Terminal.IsKeyboardFocusWithin);

                window.KeyTextInput("R");
                RunJobs(window);

                Assert.Contains("R", a.Pty.WriterText, StringComparison.Ordinal);
                Assert.DoesNotContain("R", b.Pty.WriterText, StringComparison.Ordinal);
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

    private static MainWindow CreateWindow()
    {
        var window = new MainWindow { DataContext = new MainViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static TestTerminal OpenTab(MainWindow window, string title)
    {
        var viewModel = (MainViewModel)window.DataContext!;
        var pty = new InMemoryPty();
        var terminal = new TerminalControl { Process = string.Empty };
        var tab = TestTab.Create(terminal, title: title, sessionId: title);
        viewModel.AddTab(tab);
        RunJobs(window);

        terminal.ApplyTemplate();
        terminal.AttachConnection(pty);
        RunJobs(window);
        return new TestTerminal(tab, terminal, pty);
    }

    private static void ClickTab(MainWindow window, TerminalTab tab)
    {
        var border = window.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => ReferenceEquals(border.Tag, tab));
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

    private sealed record TestTerminal(TerminalTab Tab, TerminalControl Terminal, InMemoryPty Pty)
        : IDisposable
    {
        public void Dispose() => Pty.Dispose();
    }
}
