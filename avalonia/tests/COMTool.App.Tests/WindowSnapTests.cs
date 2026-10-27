using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;

namespace COMTool.App.Tests;

/// <summary>
/// MobaXterm's window snaps to a screen edge (Aero Snap) when dragged there,
/// which Windows only does for a window carrying the native resize style
/// (WS_THICKFRAME). The main window draws its own title bar and stays
/// frameless (WindowDecorations.None) on every platform - Avalonia then
/// answers WM_NCCALCSIZE with the full rect, so DWM has no non-client area to
/// draw a frame into. On Windows the app adds WS_THICKFRAME to the style bit
/// mask after creation: snapping is granted without any visible border (the
/// old BorderOnly decoration left a blue/white DWM strip above the title bar
/// on Windows 10). The X11 E2E harness keeps its window-relative coordinates
/// because no WM frame is introduced on other platforms.
/// </summary>
public sealed class WindowSnapTests
{
    [AvaloniaFact]
    public void UI16_SnapFrameBitIsWindowsOnly()
    {
        // Only Windows wants the snap frame bit; other platforms stay plain.
        Assert.True(MainWindow.WantsSnapFrame(isWindows: true));
        Assert.False(MainWindow.WantsSnapFrame(isWindows: false));

        // The bit is ORed onto the existing style, keeping all other bits.
        // 0x94000000 = WS_VISIBLE|WS_CLIPSIBLINGS|WS_POPUP (frameless look),
        // 0x94400000 = same + WS_THICKFRAME (snap-eligible, still no border
        // because the client area covers the whole window).
        Assert.Equal(0x94040000L, MainWindow.WithSnapFrame(0x94000000L));

        // Applying it twice must be idempotent.
        Assert.Equal(
            MainWindow.WithSnapFrame(0x94040000L),
            MainWindow.WithSnapFrame(MainWindow.WithSnapFrame(0x94040000L)));
    }

    [AvaloniaFact]
    public void UI16_WindowStaysFramelessOnEveryPlatform()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
