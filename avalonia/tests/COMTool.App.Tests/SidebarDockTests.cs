using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

/// <summary>
/// The sessions panel is a fixed MobaXterm-style dock: it never appears or
/// disappears because of pointer movement, only the title-bar sessions button
/// toggles it, and a collapsed dock leaves no floating handle on the left edge.
/// </summary>
public sealed class SidebarDockTests
{
    [AvaloniaFact]
    public void UI02_SidebarStaysDockedWhenPointerMovesIntoContent()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = ShowWindow(env, visible: true, width: 260);

                var body = window.FindControl<Grid>("BodyGrid")!;
                var sidebar = window.FindControl<SessionSidebar>("Sidebar")!;
                Assert.True(sidebar.IsVisible);
                Assert.Equal(260, body.ColumnDefinitions[0].ActualWidth, precision: 1);

                // Hover-reveal affordances are gone: no edge strip, no handle.
                Assert.Null(window.FindControl<Border>("SessionHoverStrip"));
                Assert.Null(window.FindControl<Button>("SessionHandle"));

                // Pointer movement into the content, back to the left edge and
                // into the list must never change the dock.
                foreach (var point in new[]
                         {
                             new Point(700, 300),
                             new Point(25, 300),
                             new Point(100, 300),
                             new Point(240, 300),
                             new Point(600, 17),
                         })
                {
                    window.MouseMove(point);
                    RunJobs(window);
                    Assert.True(sidebar.IsVisible, $"sidebar collapsed at {point}");
                    Assert.Equal(260, body.ColumnDefinitions[0].ActualWidth, precision: 1);
                }
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI02_TitleBarButtonTogglesDockedSidebar()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = ShowWindow(env, visible: true, width: 260);

                var body = window.FindControl<Grid>("BodyGrid")!;
                var sidebar = window.FindControl<SessionSidebar>("Sidebar")!;
                var toggle = window.FindControl<Button>("SessionsButton")!;

                Click(window, toggle);

                // The panel collapses to the icon rail. The rail itself
                // stays on screen so a rail icon can reopen the panel.
                Assert.True(sidebar.IsVisible);
                Assert.NotNull(sidebar.FindControl<Border>("SidebarRail"));
                Assert.Equal(24, body.ColumnDefinitions[0].ActualWidth, precision: 1);
                Assert.False(
                    window.FindControl<GridSplitter>("SidebarSplitter")!.IsVisible);
                Assert.False(ReadConfig(env).SessionSidebar.Visible);

                // The dock is still closed: no auto reopening on pointer entry.
                window.MouseMove(new Point(25, 300));
                RunJobs(window);
                Assert.Equal(24, body.ColumnDefinitions[0].ActualWidth, precision: 1);

                Click(window, toggle);
                Assert.True(sidebar.IsVisible);
                Assert.Equal(260, body.ColumnDefinitions[0].ActualWidth, precision: 1);
                Assert.True(
                    window.FindControl<GridSplitter>("SidebarSplitter")!.IsVisible);
                Assert.True(ReadConfig(env).SessionSidebar.Visible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI02_CollapsedDockPersistsAcrossRestart()
    {
        using var env = new TestEnvironment();
        MainWindow? first = null;
        MainWindow? second = null;
        try
        {
            env.Run(() =>
            {
                first = ShowWindow(env, visible: true, width: 260);
                Click(first, first.FindControl<Button>("SessionsButton")!);
                Assert.Equal(
                    24,
                    first.FindControl<Grid>("BodyGrid")!.ColumnDefinitions[0].ActualWidth,
                    precision: 1);
            });

            first!.Close();
            first = null;
            Dispatcher.UIThread.RunJobs();

            env.Run(() =>
            {
                // A fresh window restores the user's explicit choice instead of
                // silently forcing the dock open like the auto-hide build did.
                second = ShowWindow(env, visible: false, width: 260, seed: false);
                var body = second.FindControl<Grid>("BodyGrid")!;
                var sidebar = second.FindControl<SessionSidebar>("Sidebar")!;
                Assert.True(sidebar.IsVisible);
                Assert.NotNull(sidebar.FindControl<Border>("SidebarRail"));
                Assert.Equal(24, body.ColumnDefinitions[0].ActualWidth, precision: 1);
            });
        }
        finally
        {
            first?.Close();
            second?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI02_NewSessionMenuKeepsDockedSidebarOpen()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = ShowWindow(env, visible: true, width: 260);

                var sidebar = window.FindControl<SessionSidebar>("Sidebar")!;
                var add = sidebar.FindControl<Button>("SidebarAddSessionButton")!;
                var flyout = Assert.IsType<MenuFlyout>(add.Flyout);
                flyout.ShowAt(add);
                RunJobs(window);
                Assert.True(sidebar.HasOpenNewSessionMenu);

                window.MouseMove(new Point(700, 300));
                RunJobs(window);
                Assert.True(sidebar.IsVisible);
                Assert.True(sidebar.HasOpenNewSessionMenu);

                flyout.Hide();
                RunJobs(window);
                Assert.False(sidebar.HasOpenNewSessionMenu);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI02_SidebarWidthSurvivesRestart()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = ShowWindow(env, visible: true, width: 260);
                var body = window.FindControl<Grid>("BodyGrid")!;
                Assert.Equal(260, body.ColumnDefinitions[0].ActualWidth, precision: 1);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static MainWindow ShowWindow(TestEnvironment env, bool visible, int width, bool seed = true)
    {
        if (seed)
        {
            var config = new ComtoolConfig();
            config.SessionSidebar.Visible = visible;
            config.SessionSidebar.Width = width;
            env.SeedConfig(config);
        }

        var window = new MainWindow { DataContext = new MainViewModel() };
        window.Show();
        window.StopPortTimerForTests();
        RunJobs(window);
        return window;
    }

    private static ComtoolConfig ReadConfig(TestEnvironment env) =>
        ComtoolConfig.Load(Path.Combine(env.AvaloniaConfigDir, "config.json"));

    private static void Click(MainWindow window, Control control)
    {
        var center = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);
        Assert.NotNull(center);
        window.MouseDown(center!.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
        RunJobs(window);
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
