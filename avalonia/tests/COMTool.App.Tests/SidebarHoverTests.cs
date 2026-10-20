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
                Assert.True(sidebar.IsVisible);
                Assert.Equal(220, body.ColumnDefinitions[0].ActualWidth, precision: 1);
                Assert.False(hoverStrip.IsVisible);

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

                // Entering the left-edge hover strip reopens the panel and the
                // content column expands back to the saved width.
                window.ShowSidebarFromHoverForTests();
                RunJobs(window);

                Assert.True(sidebar.IsVisible);
                Assert.False(hoverStrip.IsVisible);
                Assert.Equal(220, body.ColumnDefinitions[0].ActualWidth, precision: 1);
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
