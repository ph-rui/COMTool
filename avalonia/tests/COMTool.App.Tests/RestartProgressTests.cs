using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

/// <summary>
/// Covers the R-restart flow around the status row: while a stopped session
/// reconnects the progress area shows an indeterminate bar, and a failed
/// attempt pops the "重连失败" dialog with 继续重启 / 不重连.
/// </summary>
public sealed class RestartProgressTests
{
    [AvaloniaFact]
    public void SSH11_RestartProgressShownWhileReconnecting()
    {
        var tab = new TerminalTab
        {
            Title = "t",
            SessionId = "s",
            Kind = SessionKind.Ssh,
            Content = new Border(),
        };

        // Idle: no progress area at all.
        Assert.False(tab.ShowStatusProgress);
        Assert.False(tab.StatusProgressIndeterminate);
        Assert.False(tab.ShowProgressTextVisible);

        // R pressed: the bar appears, indeterminate, labeled "Restarting...".
        tab.IsRestarting = true;
        Assert.True(tab.ShowStatusProgress);
        Assert.Equal("Restarting...", tab.StatusProgressLabel);
        Assert.True(tab.StatusProgressIndeterminate);
        Assert.False(tab.ShowProgressTextVisible);

        // Attempt finished: the area goes away again.
        tab.IsRestarting = false;
        Assert.False(tab.ShowStatusProgress);
        Assert.False(tab.StatusProgressIndeterminate);

        // ZMODEM keeps its own rules (regression: the rebind must not change
        // how the transfer progress renders).
        tab.IsZmodemActive = true;
        tab.ZmodemLabel = "Receiving file (42%)";
        tab.ZmodemProgress = 42;
        Assert.True(tab.ShowStatusProgress);
        Assert.Equal("Receiving file (42%)", tab.StatusProgressLabel);
        Assert.False(tab.StatusProgressIndeterminate);
        Assert.True(tab.ShowProgressTextVisible);

        tab.IsZmodemIndeterminate = true;
        Assert.True(tab.StatusProgressIndeterminate);
        Assert.False(tab.ShowProgressTextVisible);
    }

    [AvaloniaFact]
    public async Task SSH11_RestartAskDialogStopsOnGiveUp()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig());
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs();

                var result = window.ShowRestartAskAsync(window, "Connection refused");
                WaitUntil(() => window.LastRestartDialogForTests is not null, "ask dialog shown");
                var dialog = window.LastRestartDialogForTests!;
                Assert.Equal("重连失败", dialog.Title);
                var text = dialog.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .First()
                    .Text;
                Assert.Contains("Connection refused", text, StringComparison.Ordinal);
                Assert.Contains("是否继续重启", text, StringComparison.Ordinal);

                ClickButton(dialog, "不重连");
                WaitUntil(() => window.LastRestartDialogForTests is null, "give-up closed the dialog");
                WaitUntil(() => result.IsCompleted, "give-up resolved");
                Assert.False(await result);
            });
        }
        finally
        {
            window?.Close();
            RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SSH11_RestartAskDialogRetriesOnContinue()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig());
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs();

                var result = window.ShowRestartAskAsync(window, "boom");
                WaitUntil(() => window.LastRestartDialogForTests is not null, "ask dialog shown");
                var dialog = window.LastRestartDialogForTests!;

                ClickButton(dialog, "继续重启");
                WaitUntil(() => window.LastRestartDialogForTests is null, "continue closed the dialog");
                WaitUntil(() => result.IsCompleted, "continue resolved");
                Assert.True(await result);
            });
        }
        finally
        {
            window?.Close();
            RunJobs();
        }
    }

    private static void ClickButton(Window dialog, string label)
    {
        var button = dialog.GetVisualDescendants()
            .OfType<Button>()
            .Single(b => string.Equals(b.Content?.ToString(), label, StringComparison.Ordinal));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RunJobs();
    }

    private static void RunJobs()
    {
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitUntil(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"timed out waiting for {because}");
            }

            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }
}
