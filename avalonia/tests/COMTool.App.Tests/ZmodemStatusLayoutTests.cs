using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class ZmodemStatusLayoutTests
{
    [AvaloniaFact]
    public void UI12_ZmodemProgressTextAndLayoutStayReadable()
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

                var viewModel = (MainViewModel)window.DataContext!;
                var tab = TestTab.Create(title: "z", sessionId: "z");
                viewModel.AddTab(tab);
                tab.IsZmodemActive = true;
                tab.ZmodemLabel = "fastdepth-160.dvm";
                tab.ZmodemProgress = 13;
                window.UpdateLayout();

                var label = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(t => t.Text == "fastdepth-160.dvm");
                var bar = window.GetVisualDescendants()
                    .OfType<ProgressBar>()
                    .Single(p => p.IsVisible);

                Assert.False(bar.IsIndeterminate);
                Assert.True(bar.ShowProgressText);
                Assert.Equal("{1:0}%", bar.ProgressTextFormat);
                Assert.Equal(13, bar.Percentage, precision: 3);
                Assert.True(
                    label.Bounds.Width + 0.5 >= label.TextLayout.Width,
                    $"label clipped: bounds={label.Bounds.Width}, text={label.TextLayout.Width}");
                Assert.True(
                    bar.Bounds.Left + 0.5 >= label.Bounds.Right,
                    $"label overlapped: label right={label.Bounds.Right}, bar left={bar.Bounds.Left}");
                Assert.Equal(150, bar.Bounds.Width, precision: 1);

                var progressTexts = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(text => text.Text == "13%")
                    .ToList();
                Assert.Single(progressTexts);
                Assert.Contains(progressTexts[0], bar.GetVisualDescendants());

                tab.IsZmodemIndeterminate = true;
                window.UpdateLayout();
                Assert.False(bar.ShowProgressText);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
