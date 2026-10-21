using System.Buffers.Binary;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class NewSessionMenuTests
{
    [AvaloniaFact]
    public void UI06_NewSessionMenuItemsHaveMobaStyleIcons()
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

                AssertSessionMenuIcons(window.FindControl<Button>("EmptyAddSessionButton")!);
                AssertSessionMenuIcons(window.FindControl<Button>("ToolbarAddSessionButton")!);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI01_TitleBarUsesSquareTerminalIcon()
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

                var image = Assert.IsType<Image>(window.FindControl<Image>("TitleBarLogo"));
                Assert.NotNull(image.Source);
                Assert.Equal(35, image.Width);
                Assert.Equal(35, image.Height);

                using var png = AssetLoader.Open(
                    new Uri("avares://COMTool.App/Assets/logo.png"));
                using var pngReader = new BinaryReader(png);
                Assert.Equal(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
                    pngReader.ReadBytes(8));
                Assert.Equal(13, BinaryPrimitives.ReadInt32BigEndian(pngReader.ReadBytes(4)));
                Assert.Equal("IHDR", Encoding.ASCII.GetString(pngReader.ReadBytes(4)));
                var width = BinaryPrimitives.ReadInt32BigEndian(pngReader.ReadBytes(4));
                var height = BinaryPrimitives.ReadInt32BigEndian(pngReader.ReadBytes(4));
                Assert.Equal(512, width);
                Assert.Equal(width, height);

                using var ico = AssetLoader.Open(
                    new Uri("avares://COMTool.App/Assets/logo.ico"));
                using var icoReader = new BinaryReader(ico);
                Assert.Equal(0, icoReader.ReadUInt16());
                Assert.Equal(1, icoReader.ReadUInt16());
                var count = icoReader.ReadUInt16();
                var sizes = new List<int>();
                for (var i = 0; i < count; i++)
                {
                    var iconWidth = icoReader.ReadByte();
                    icoReader.ReadByte();
                    icoReader.ReadByte();
                    icoReader.ReadByte();
                    icoReader.ReadUInt16();
                    icoReader.ReadUInt16();
                    icoReader.ReadUInt32();
                    icoReader.ReadUInt32();
                    sizes.Add(iconWidth == 0 ? 256 : iconWidth);
                }

                Assert.Contains(16, sizes);
                Assert.Contains(24, sizes);
                Assert.Contains(256, sizes);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI06_NewSessionMenuReplacesPluginEntries()
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

                AssertSessionMenu(window.FindControl<Button>("EmptyAddSessionButton")!);
                AssertSessionMenu(window.FindControl<Button>("ToolbarAddSessionButton")!);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI06_NewSessionSettingsPreselectsRequestedTransport()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig());
        var service = AppConfigService.Load();

        env.Run(() =>
        {
            var cases = new (SessionKind Kind, int Index, string Panel)[]
            {
                (SessionKind.Serial, 0, "SerialPanel"),
                (SessionKind.Ssh, 1, "SshPanel"),
                (SessionKind.Telnet, 2, "TelnetPanel"),
                (SessionKind.Tcp, 3, "TcpPanel"),
            };

            foreach (var (kind, index, panelName) in cases)
            {
                ConnectionSettingsWindow? window = null;
                try
                {
                    Func<string, TerminalTab?, Task> open = (_, _) => Task.CompletedTask;
                    window = new ConnectionSettingsWindow(
                        service,
                        null,
                        open,
                        open,
                        open,
                        open,
                        _ => Task.CompletedTask,
                        kind);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();

                    Assert.Equal(
                        index,
                        window.FindControl<ComboBox>("ConnectionTypeCombo")!.SelectedIndex);
                    Assert.True(window.FindControl<Grid>(panelName)!.IsVisible);
                    Assert.Equal("New session", window.Title);
                }
                finally
                {
                    window?.Close();
                    Dispatcher.UIThread.RunJobs();
                }
            }
        });
    }

    [AvaloniaFact]
    public void UI06_LocalTerminalEntryAddsLocalTab()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var viewModel = new MainViewModel();
                window = new MainWindow { DataContext = viewModel };

                var button = window.FindControl<Button>("EmptyAddSessionButton")!;
                var local = ((MenuFlyout)button.Flyout!).Items
                    .OfType<MenuItem>()
                    .Single(item => item.Tag?.ToString() == "Local");

                local.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                var tab = Assert.Single(viewModel.Tabs);
                Assert.Equal(SessionKind.Local, tab.Kind);
                Assert.StartsWith("terminal:", tab.SessionId, StringComparison.Ordinal);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI07_EmptyStatePointsToNewSession()
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

                var hint = window.FindControl<TextBlock>("EmptyStateAddHint")!;
                Assert.Equal("or click + to add a session", hint.Text);
                Assert.Equal(
                    "New session",
                    ToolTip.GetTip(window.FindControl<Button>("EmptyAddSessionButton")!)?.ToString());
                Assert.Equal(
                    "New session",
                    ToolTip.GetTip(window.FindControl<Button>("ToolbarAddSessionButton")!)?.ToString());
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void AssertSessionMenu(Button button)
    {
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        var items = flyout.Items.OfType<MenuItem>().ToList();

        Assert.Equal(
            new[] { "Serial", "SSH", "Telnet", "TCP", "Local terminal" },
            items.Select(item => item.Header?.ToString()).ToArray());
        Assert.Equal(
            new[] { "Serial", "SSH", "Telnet", "TCP", "Local" },
            items.Select(item => item.Tag?.ToString()).ToArray());
        Assert.DoesNotContain(
            items,
            item => item.Header?.ToString()?.Contains("plugin", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static void AssertSessionMenuIcons(Button button)
    {
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        var expected = new Dictionary<string, (string Glyph, Color Color)>
        {
            ["Serial"] = ("\uf1e6", Color.Parse("#43a047")),
            ["SSH"] = ("\uf233", Color.Parse("#1976d2")),
            ["Telnet"] = ("\uf120", Color.Parse("#00897b")),
            ["TCP"] = ("\uf0ac", Color.Parse("#f57c00")),
            ["Local"] = ("\uf108", Color.Parse("#607d8b")),
        };

        foreach (var item in flyout.Items.OfType<MenuItem>())
        {
            var tag = item.Tag?.ToString();
            Assert.True(expected.TryGetValue(tag!, out var icon), $"missing expected icon for {tag}");
            var glyph = Assert.IsType<TextBlock>(item.Icon);
            Assert.Equal(icon.Glyph, glyph.Text);
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground);
            Assert.Equal(icon.Color, brush.Color);
        }
    }
}
