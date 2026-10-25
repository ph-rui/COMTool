using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;

namespace COMTool.App.Tests;

/// <summary>
/// MobaXterm's window snaps to a screen edge (Aero Snap) when dragged there,
/// which Windows only does for a window that keeps its native resize frame.
/// The main window draws its own title bar, so it asks for a border without a
/// caption on Windows and stays frameless elsewhere (the X11 E2E harness
/// clicks window-relative coordinates that a reparenting WM frame would shift).
/// </summary>
public sealed class WindowSnapTests
{
    [AvaloniaFact]
    public void UI16_WindowsKeepsTheNativeResizeFrameForAeroSnap()
    {
        Assert.Equal(
            WindowDecorations.BorderOnly,
            MainWindow.PreferredWindowDecorations(isWindows: true));
        Assert.Equal(
            WindowDecorations.None,
            MainWindow.PreferredWindowDecorations(isWindows: false));
    }

    [AvaloniaFact]
    public void UI16_LinuxWindowStaysFrameless()
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
