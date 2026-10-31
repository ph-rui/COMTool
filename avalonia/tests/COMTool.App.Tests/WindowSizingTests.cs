using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

/// <summary>
/// 启动恢复窗口尺寸时必须钳制到屏幕工作区：保存尺寸来自更大显示器时，
/// 窗口底部的 SFTP 底栏（远程监控）会落到任务栏后面/屏幕外，按钮点不到。
/// </summary>
public sealed class WindowSizingTests
{
    [AvaloniaFact]
    public void UI01_ClampWindowSizeKeepsFittingSize()
    {
        var result = MainWindow.ClampWindowSizeToWorkArea(
            new Size(900, 600),
            new Size(1920, 1040),
            new Size(500, 320));

        Assert.Equal(new Size(900, 600), result);
    }

    [AvaloniaFact]
    public void UI01_ClampWindowSizeShrinksOversizedToWorkArea()
    {
        var result = MainWindow.ClampWindowSizeToWorkArea(
            new Size(4000, 3000),
            new Size(1280, 760),
            new Size(500, 320));

        Assert.Equal(new Size(1280, 760), result);
    }

    [AvaloniaFact]
    public void UI01_ClampWindowSizeShrinksOnlyTheOverflowingAxis()
    {
        var result = MainWindow.ClampWindowSizeToWorkArea(
            new Size(1000, 3000),
            new Size(1280, 760),
            new Size(500, 320));

        Assert.Equal(new Size(1000, 760), result);
    }

    [AvaloniaFact]
    public void UI01_ClampWindowSizeNeverGoesBelowMinimum()
    {
        var result = MainWindow.ClampWindowSizeToWorkArea(
            new Size(900, 600),
            new Size(320, 200),
            new Size(500, 320));

        Assert.Equal(new Size(500, 320), result);
    }

    [AvaloniaFact]
    public void UI01_ClampWindowSizeIgnoresUnknownWorkArea()
    {
        var result = MainWindow.ClampWindowSizeToWorkArea(
            new Size(900, 600),
            new Size(0, 0),
            new Size(500, 320));

        Assert.Equal(new Size(900, 600), result);
    }

    [AvaloniaFact]
    public void UI01_WindowRestoreClampsOversizedSavedSizeToScreen()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    Window = new WindowConfig
                    {
                        Width = 4000,
                        Height = 3000,
                        Maximized = false,
                    },
                });
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var screen = window.Screens.Primary;
                Assert.NotNull(screen);
                var areaWidth = screen!.WorkingArea.Width / screen.Scaling;
                var areaHeight = screen.WorkingArea.Height / screen.Scaling;
                Assert.True(
                    window.Width <= areaWidth + 0.5,
                    $"window width {window.Width} exceeds work area {areaWidth}");
                Assert.True(
                    window.Height <= areaHeight + 0.5,
                    $"window height {window.Height} exceeds work area {areaHeight}");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI01_WindowRestoreKeepsSmallSavedSize()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    Window = new WindowConfig
                    {
                        Width = 900,
                        Height = 600,
                        Maximized = false,
                    },
                });
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(900, window.Width, 3);
                Assert.Equal(600, window.Height, 3);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
