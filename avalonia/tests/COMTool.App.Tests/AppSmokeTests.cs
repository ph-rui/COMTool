using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class AppSmokeTests
{
    [AvaloniaFact]
    public void APP01_MainWindowShows()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.True(window.IsVisible);
                Assert.NotNull(window.FindControl<SessionSidebar>("Sidebar"));

                var expectedConfigPath = Path.Combine(env.AvaloniaConfigDir, "config.json");
                var log = File.ReadAllText(AppLog.Path);
                Assert.Contains(expectedConfigPath, log, StringComparison.Ordinal);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
