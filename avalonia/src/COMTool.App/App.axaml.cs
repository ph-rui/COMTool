using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;

namespace COMTool.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppLog.Initialize();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (_, _) => AppLog.Info("COMTool Avalonia exiting");
            Resources["UiFontFamily"] = new FontFamily(
                OperatingSystem.IsWindows() ? "Microsoft YaHei" : "DejaVu Sans");

            var viewModel = new MainViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
