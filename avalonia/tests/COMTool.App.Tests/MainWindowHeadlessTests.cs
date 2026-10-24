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
                config.SessionSidebar.Visible = true;
                config.SessionSidebar.Width = 280;
                env.SeedConfig(config);

                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var sidebar = window.FindControl<SessionSidebar>("Sidebar")!;
                Assert.True(sidebar.IsVisible);
                Assert.Equal(280, sidebar.Bounds.Width, precision: 1);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI02_CollapsedSidebarStaysCollapsedOnLaunch()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.SessionSidebar.Visible = false;
                config.SessionSidebar.Width = 280;
                env.SeedConfig(config);

                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var body = window.FindControl<Grid>("BodyGrid")!;
                var sidebar = window.FindControl<SessionSidebar>("Sidebar")!;
                // The dock is manual-only now, so a collapsed state survives a
                // restart instead of being overridden on launch. The 40px
                // icon rail stays on screen so the panels can be reopened.
                Assert.True(sidebar.IsVisible);
                Assert.NotNull(sidebar.FindControl<Border>("SidebarRail"));
                Assert.Equal(40, body.ColumnDefinitions[0].ActualWidth, precision: 1);
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
    public void UI05_TabSwitchKeepsBothTerminalsLive()
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

                ClickTab(window, b.Tab);
                RunJobs(window);
                Assert.Same(b.Tab, viewModel.SelectedTab);
                Assert.True(b.Terminal.IsKeyboardFocusWithin);

                window.KeyTextInput("B");
                RunJobs(window);

                Assert.Contains("A", a.Pty.WriterText, StringComparison.Ordinal);
                Assert.DoesNotContain("B", a.Pty.WriterText, StringComparison.Ordinal);
                Assert.Contains("B", b.Pty.WriterText, StringComparison.Ordinal);
                Assert.DoesNotContain("A", b.Pty.WriterText, StringComparison.Ordinal);
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

                ClickTab(window, a.Tab);
                RunJobs(window);
                Assert.Same(a.Tab, viewModel.SelectedTab);

                // MouseDown/MouseUp pump dispatcher jobs, so they cannot model a
                // rapid switch. Raise both tab pointer presses in one dispatcher
                // turn and verify each selection lands before RunJobs is called.
                PressTab(window, b.Tab);
                Assert.Same(b.Tab, viewModel.SelectedTab);
                PressTab(window, a.Tab);
                Assert.Same(a.Tab, viewModel.SelectedTab);

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
        var border = FindTabBorder(window, tab);
        var center = border.TranslatePoint(
            new Point(5, border.Bounds.Height / 2),
            window);
        Assert.NotNull(center);

        window.MouseDown(center.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
    }

    private static void PressTab(MainWindow window, TerminalTab tab)
    {
        var border = FindTabBorder(window, tab);
        var position = border.TranslatePoint(
            new Point(5, border.Bounds.Height / 2),
            window);
        Assert.NotNull(position);

        border.RaiseEvent(new PointerPressedEventArgs(
            border,
            new Pointer(0, PointerType.Mouse, isPrimary: true),
            window,
            position.Value,
            0,
            new PointerPointProperties(
                RawInputModifiers.None,
                PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clickCount: 1));
    }

    private static Border FindTabBorder(MainWindow window, TerminalTab tab) =>
        window.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => ReferenceEquals(border.Tag, tab));

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
