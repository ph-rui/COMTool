using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class SessionConnectingFeedbackTests
{
    [AvaloniaFact]
    public void UI02_SessionClickShowsConnectingStateAndIgnoresReentry()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    SessionSidebar = { Visible = true, Width = 220 },
                    TelnetSessions =
                    {
                        new TelnetSession
                        {
                            Key = "telnet:192.168.1.12:23",
                            Host = "192.168.1.12",
                            Port = 23,
                            Alias = "192.168.1.12",
                        },
                    },
                });

                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                window.StopSidebarHoverTimerForTests();
                RunJobs(window);

                var viewModel = Assert.IsType<MainViewModel>(window.DataContext);
                var item = viewModel.Sections
                    .SelectMany(section => section.Items)
                    .Single(session => session.Kind == SessionKind.Telnet);
                var label = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text =>
                        text.Classes.Contains("SessionConnecting")
                        && ReferenceEquals(text.DataContext, item));

                Assert.False(item.IsConnecting);
                Assert.False(label.IsVisible);
                Assert.Equal(Color.Parse("#90a4ae"), ((SolidColorBrush)item.IconBrush).Color);

                var release = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var activations = 0;
                window.SessionActivationOverrideForTests = _ =>
                {
                    activations++;
                    return release.Task;
                };

                var activation = window.ActivateSessionForTests(item);
                Assert.True(item.IsConnecting);
                RunJobs(window);

                Assert.True(label.IsVisible);
                Assert.Equal(Color.Parse("#1565c0"), ((SolidColorBrush)item.IconBrush).Color);

                // A second click while the first attempt is pending must not
                // start another connection attempt.
                var reentry = window.ActivateSessionForTests(item);
                Assert.True(reentry.IsCompleted);
                Assert.Equal(1, activations);

                release.SetResult(true);
                while (!activation.IsCompleted)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(1);
                }

                RunJobs(window);
                Assert.False(item.IsConnecting);
                Assert.False(label.IsVisible);
                Assert.Equal(Color.Parse("#90a4ae"), ((SolidColorBrush)item.IconBrush).Color);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void RunJobs(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
