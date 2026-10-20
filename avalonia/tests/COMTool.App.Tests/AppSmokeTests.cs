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
            env.SeedConfig(new ComtoolConfig());
            window = new MainWindow { DataContext = new MainViewModel() };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.IsVisible);
            Assert.NotNull(window.FindControl<SessionSidebar>("Sidebar"));
            Assert.StartsWith(TestAppBuilder.Root, env.AvaloniaConfigDir, StringComparison.Ordinal);
            Assert.StartsWith(TestAppBuilder.Root, env.PythonConfigDir, StringComparison.Ordinal);
            Assert.StartsWith(TestAppBuilder.Root, AppLog.Path, StringComparison.Ordinal);
        }
        catch
        {
            env.KeepForFailure();
            throw;
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
