using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class TitleBarTests
{
    [AvaloniaFact]
    public void UI01_TitleBarDoubleClickTogglesMaximizeOnce()
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
                RunJobs(window);

                var titleBar = window.FindControl<Border>("TitleBar");
                Assert.NotNull(titleBar);
                Assert.Equal(WindowState.Normal, window.WindowState);

                // The second press carries ClickCount=2. A pointer press alone
                // must not toggle the window state; the double-tap gesture
                // performs the single maximize/restore transition.
                var pointerPress = CreatePress(titleBar, window, clickCount: 2);
                titleBar.RaiseEvent(pointerPress);
                RunJobs(window);
                Assert.Equal(WindowState.Normal, window.WindowState);

                var firstDoubleTap = RaiseDoubleTap(titleBar, window, pointerPress);
                RunJobs(window);
                Assert.True(firstDoubleTap.Handled);
                Assert.Equal(WindowState.Maximized, window.WindowState);

                var restorePress = CreatePress(titleBar, window, clickCount: 2);
                titleBar.RaiseEvent(restorePress);
                RunJobs(window);
                Assert.Equal(WindowState.Maximized, window.WindowState);

                var secondDoubleTap = RaiseDoubleTap(titleBar, window, restorePress);
                RunJobs(window);
                Assert.True(secondDoubleTap.Handled);
                Assert.Equal(WindowState.Normal, window.WindowState);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static PointerPressedEventArgs CreatePress(
        Border titleBar,
        Window window,
        int clickCount)
    {
        var position = titleBar.TranslatePoint(new Point(10, 10), window)
            ?? new Point(10, 10);
        var pointer = new Avalonia.Input.Pointer(0, PointerType.Mouse, isPrimary: true);
        return new PointerPressedEventArgs(
            titleBar,
            pointer,
            window,
            position,
            0,
            new PointerPointProperties(
                RawInputModifiers.None,
                PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clickCount);
    }

    private static TappedEventArgs RaiseDoubleTap(
        Border titleBar,
        Window window,
        PointerPressedEventArgs press)
    {
        var args = new TappedEventArgs(InputElement.DoubleTappedEvent, press);
        titleBar.RaiseEvent(args);
        return args;
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
