using System.Buffers.Binary;
using System.IO.Compression;
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
                Assert.Equal(28, image.Width);
                Assert.Equal(28, image.Height);
                Assert.Equal(6, image.Margin.Left);
                Assert.Equal(3, image.Margin.Top);
                Assert.Equal(0, image.Margin.Right);
                Assert.Equal(4, image.Margin.Bottom);

                using var png = AssetLoader.Open(
                    new Uri("avares://COMTool.App/Assets/logo.png"));
                using var pngCopy = new MemoryStream();
                png.CopyTo(pngCopy);
                var pngBytes = pngCopy.ToArray();
                using var pngReader = new BinaryReader(new MemoryStream(pngBytes));
                Assert.Equal(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
                    pngReader.ReadBytes(8));
                Assert.Equal(13, BinaryPrimitives.ReadInt32BigEndian(pngReader.ReadBytes(4)));
                Assert.Equal("IHDR", Encoding.ASCII.GetString(pngReader.ReadBytes(4)));
                var width = BinaryPrimitives.ReadInt32BigEndian(pngReader.ReadBytes(4));
                var height = BinaryPrimitives.ReadInt32BigEndian(pngReader.ReadBytes(4));
                Assert.Equal(512, width);
                Assert.Equal(width, height);
                AssertNoBrightOuterFringe(pngBytes);

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

    private static void AssertNoBrightOuterFringe(byte[] png)
    {
        var (width, height, pixels) = DecodeRgbaPng(png);
        var stride = width * 4;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 4;
                if (pixels[offset + 3] < 16 || !IsBoundary(pixels, width, height, x, y))
                {
                    continue;
                }

                var max = Math.Max(
                    pixels[offset],
                    Math.Max(pixels[offset + 1], pixels[offset + 2]));
                Assert.True(
                    max < 150,
                    $"bright opaque edge pixel at {x},{y}: {pixels[offset]},{pixels[offset + 1]},{pixels[offset + 2]}");
            }
        }
    }

    private static bool IsBoundary(byte[] pixels, int width, int height, int x, int y)
    {
        if (x < 2 || y < 2 || x >= width - 2 || y >= height - 2)
        {
            return true;
        }

        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                var offset = ((y + dy) * width + x + dx) * 4;
                if (pixels[offset + 3] < 16)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (int Width, int Height, byte[] Pixels) DecodeRgbaPng(byte[] png)
    {
        Assert.Equal(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            png[..8]);
        var position = 8;
        var width = 0;
        var height = 0;
        using var compressed = new MemoryStream();
        while (position + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position, 4));
            position += 4;
            var type = Encoding.ASCII.GetString(png, position, 4);
            position += 4;
            var data = png.AsSpan(position, length);
            position += length + 4;
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                Assert.Equal(8, data[8]);
                Assert.Equal(6, data[9]);
                Assert.Equal(0, data[10]);
                Assert.Equal(0, data[11]);
                Assert.Equal(0, data[12]);
            }
            else if (type == "IDAT")
            {
                compressed.Write(data);
            }
            else if (type == "IEND")
            {
                break;
            }
        }

        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var filtered = raw.ToArray();
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1);
            var filter = filtered[row];
            for (var x = 0; x < stride; x++)
            {
                var value = (int)filtered[row + 1 + x];
                var left = x >= 4 ? pixels[y * stride + x - 4] : 0;
                var up = y > 0 ? pixels[(y - 1) * stride + x] : 0;
                var upLeft = y > 0 && x >= 4 ? pixels[(y - 1) * stride + x - 4] : 0;
                value = filter switch
                {
                    0 => value,
                    1 => value + left,
                    2 => value + up,
                    3 => value + ((left + up) / 2),
                    4 => value + Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException($"unsupported PNG filter {filter}"),
                };
                pixels[y * stride + x] = (byte)value;
            }
        }

        return (width, height, pixels);
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upLeftDistance = Math.Abs(estimate - upLeft);
        return leftDistance <= upDistance && leftDistance <= upLeftDistance
            ? left
            : upDistance <= upLeftDistance
                ? up
                : upLeft;
    }

}
