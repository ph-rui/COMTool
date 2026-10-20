using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class SidebarHoverTests
{
    [AvaloniaFact]
    public void UI02_SidebarAutoHidesOnPointerMoveAndContentReflows()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var config = new ComtoolConfig();
                config.SessionSidebar.Visible = true;
                config.SessionSidebar.Width = 220;
                env.SeedConfig(config);

                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                window.StopSidebarHoverTimerForTests();
                RunJobs(window);

                var body = window.FindControl<Grid>("BodyGrid")!;
                var sidebar = window.FindControl<SessionSidebar>("Sidebar")!;
                var hoverStrip = window.FindControl<Border>("SessionHoverStrip")!;
                var listFrame = sidebar.FindControl<Border>("SessionListFrame")!;
                Assert.True(sidebar.IsVisible);
                Assert.Equal(220, body.ColumnDefinitions[0].ActualWidth, precision: 1);
                Assert.False(hoverStrip.IsVisible);

                var frameOrigin = listFrame.TranslatePoint(new Point(0, 0), window);
                Assert.NotNull(frameOrigin);
                var frameLeft = frameOrigin!.Value.X;
                var frameRight = frameLeft + listFrame.Bounds.Width;
                Assert.True(frameRight > frameLeft);

                // Title-bar pointer movement must not retract the panel.
                window.MouseMove(new Point(600, 17));
                window.CheckSidebarAutoHideForTests();
                RunJobs(window);
                Assert.True(sidebar.IsVisible);
                Assert.Equal(220, body.ColumnDefinitions[0].ActualWidth, precision: 1);

                // Move straight into the content area without first visiting
                // the sidebar. The panel must still retract on the first move.
                window.MouseMove(new Point(700, 300));
                window.CheckSidebarAutoHideForTests();
                RunJobs(window);

                Assert.False(sidebar.IsVisible);
                Assert.True(hoverStrip.IsVisible);
                Assert.Equal(0, body.ColumnDefinitions[0].ActualWidth, precision: 1);

                // Moving to the left of the list frame's red edge reopens the
                // panel automatically, even outside the old fixed 10px strip.
                window.MouseMove(new Point(frameLeft - 1, 300));
                RunJobs(window);

                Assert.True(sidebar.IsVisible);
                Assert.False(hoverStrip.IsVisible);
                Assert.Equal(220, body.ColumnDefinitions[0].ActualWidth, precision: 1);

                // Anywhere between the frame's red and green edges stays open.
                window.MouseMove(new Point(frameLeft + 1, 300));
                window.CheckSidebarAutoHideForTests();
                RunJobs(window);
                Assert.True(sidebar.IsVisible);
                Assert.Equal(220, body.ColumnDefinitions[0].ActualWidth, precision: 1);

                window.MouseMove(new Point(100, 300));
                window.CheckSidebarAutoHideForTests();
                RunJobs(window);
                Assert.True(sidebar.IsVisible);

                // Crossing the list frame's green edge retracts it.
                window.MouseMove(new Point(frameRight + 1, 300));
                window.CheckSidebarAutoHideForTests();
                RunJobs(window);
                Assert.False(sidebar.IsVisible);
                Assert.True(hoverStrip.IsVisible);
                Assert.Equal(0, body.ColumnDefinitions[0].ActualWidth, precision: 1);

                // The trigger follows the current frame; moving left of the
                // red edge reopens it again.
                window.MouseMove(new Point(frameLeft - 1, 300));
                RunJobs(window);
                Assert.True(sidebar.IsVisible);
                Assert.Equal(220, body.ColumnDefinitions[0].ActualWidth, precision: 1);

                // The boundaries follow the list frame when the sidebar is
                // resized. A point beyond the old green edge but inside the
                // widened frame must keep the panel open.
                window.SetSidebarWidthForTests(260);
                RunJobs(window);
                Assert.Equal(260, body.ColumnDefinitions[0].ActualWidth, precision: 1);
                var widenedOrigin = listFrame.TranslatePoint(new Point(0, 0), window);
                Assert.NotNull(widenedOrigin);
                var widenedRight = widenedOrigin!.Value.X + listFrame.Bounds.Width;
                Assert.True(widenedRight > frameRight + 10);

                window.MouseMove(new Point(frameRight + 10, 300));
                window.CheckSidebarAutoHideForTests();
                RunJobs(window);
                Assert.True(sidebar.IsVisible);

                window.MouseMove(new Point(widenedRight + 1, 300));
                window.CheckSidebarAutoHideForTests();
                RunJobs(window);
                Assert.False(sidebar.IsVisible);

                // Moving far into the content area still retracts it after the
                // widened panel is reopened.
                window.MouseMove(new Point(frameLeft - 1, 300));
                RunJobs(window);
                Assert.True(sidebar.IsVisible);
                window.MouseMove(new Point(700, 300));
                window.CheckSidebarAutoHideForTests();
                RunJobs(window);
                Assert.False(sidebar.IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
