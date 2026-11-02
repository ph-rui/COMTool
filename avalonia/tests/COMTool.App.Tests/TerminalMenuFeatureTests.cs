using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using Xunit;

namespace COMTool.App.Tests;

/// <summary>
/// TERM-23/24/25：终端菜单三项新接线（查找 / 字体设置 / 录制输出到文件）。
/// </summary>
public sealed class TerminalMenuFeatureTests
{
    // ═══════════════════════ TERM-23：查找 ═══════════════════════

    [AvaloniaFact]
    public void TERM23_SearchBarOpensWithKnownTextAndFinds()
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
                window.StopPortTimerForTests();
                RunJobs(window);

                // 创建带已知内容的终端
                var (terminal, pty, tab) = CreateTestTerminal(window, "ssh-test");
                // 直接写入 XTerm 缓冲（绕过 pty 读取任务）
                terminal.Terminal.Write("hello world\r\nfoo bar baz\r\nhello again\r\n");
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                // 通过菜单项点击打开查找条
                window.ShowSearchBarForTests();
                Assert.True(window.IsSearchBarVisibleForTests);

                // 输入查找词
                var searchBox = window.FindControl<TextBox>("SearchBox")!;
                searchBox.Text = "hello";
                Dispatcher.UIThread.RunJobs();

                // 应该找到 2 个 "hello"（大小写不敏感），首次定位到第 1 个
                Assert.Equal("1/2", window.SearchCountTextForTests);

                // 下一个 → 第 2 个
                window.FindControl<Button>("SearchNextButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("2/2", window.SearchCountTextForTests);

                // 再下一个 → 循环回第 1 个
                window.FindControl<Button>("SearchNextButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("1/2", window.SearchCountTextForTests);

                // 上一个 → 循环回第 2 个
                window.FindControl<Button>("SearchPrevButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("2/2", window.SearchCountTextForTests);

                // 清空输入 → 清除搜索
                searchBox.Text = "";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("", window.SearchCountTextForTests);

                // 关闭查找条
                window.FindControl<Button>("SearchCloseButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.False(window.IsSearchBarVisibleForTests);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM23_SearchNotFoundShowsZero()
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
                window.StopPortTimerForTests();
                RunJobs(window);

                var (terminal, pty, tab) = CreateTestTerminal(window, "ssh-test2");
                terminal.Terminal.Write("alpha beta\r\n");
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                window.ShowSearchBarForTests();
                var searchBox = window.FindControl<TextBox>("SearchBox")!;
                searchBox.Text = "zzz_not_present";
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("0/0", window.SearchCountTextForTests);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM23_CtrlShiftFOpensSearchBar()
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
                window.StopPortTimerForTests();
                RunJobs(window);

                var (terminal, pty, tab) = CreateTestTerminal(window, "ssh-key");
                terminal.Terminal.Write("key test\r\n");
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.False(window.IsSearchBarVisibleForTests);

                // 模拟 Ctrl+Shift+F（在终端上派发，经隧道路径到达 window 的 OnWindowKeyDown）
                terminal.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.F,
                    KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
                    PhysicalKey = PhysicalKey.F,
                });
                Dispatcher.UIThread.RunJobs();

                Assert.True(window.IsSearchBarVisibleForTests);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // ═══════════════════════ TERM-24：字体设置 ═══════════════════════

    [Fact]
    public void TERM24_BuildTerminalFontFamilyAppendsFallbacks()
    {
        var ff = MainWindow.BuildTerminalFontFamily("Consolas");
        Assert.Contains("Consolas", ff.ToString());
        Assert.Contains("Microsoft YaHei UI", ff.ToString());
        Assert.Contains("NotoSansMonoCJKsc-Regular.otf", ff.ToString());
    }

    [Fact]
    public void TERM24_BuildTerminalFontFamilyEmptyDefaultsToConsolas()
    {
        var ff = MainWindow.BuildTerminalFontFamily("");
        Assert.Contains("Consolas", ff.ToString());

        var ff2 = MainWindow.BuildTerminalFontFamily(null!);
        Assert.Contains("Consolas", ff2.ToString());
    }

    [Fact]
    public void TERM24_BuildTerminalFontFamilyCjk()
    {
        var ff = MainWindow.BuildTerminalFontFamily("Noto Sans Mono CJK SC");
        Assert.Contains("NotoSansMonoCJKsc-Regular.otf", ff.ToString());
    }

    [Fact]
    public void TERM24_ConfigRoundTripsFontFamily()
    {
        var config = new ComtoolConfig
        {
            Terminal = new TerminalConfig
            {
                FontFamily = "Cascadia Mono",
                FontSize = 16.0,
            },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        });
        var loaded = System.Text.Json.JsonSerializer.Deserialize<ComtoolConfig>(json);

        Assert.Equal("Cascadia Mono", loaded!.Terminal.FontFamily);
        Assert.Equal(16.0, loaded.Terminal.FontSize, 3);
    }

    [Fact]
    public void TERM24_ConfigMissingFontFamilyDefaultsToConsolas()
    {
        // 模拟旧配置无 fontFamily 键
        var json = @"{ ""terminal"": { ""fontSize"": 13.5 } }";
        var loaded = System.Text.Json.JsonSerializer.Deserialize<ComtoolConfig>(json);
        Assert.Equal("Consolas", loaded!.Terminal.FontFamily);
    }

    [AvaloniaFact]
    public void TERM24_ApplyTerminalFontUpdatesAllTerminals()
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
                window.StopPortTimerForTests();
                RunJobs(window);

                var vm = (MainViewModel)window.DataContext!;

                // 创建两个终端
                var (t1, pty1, tab1) = CreateTestTerminal(window, "term1");
                var (t2, pty2, tab2) = CreateTestTerminal(window, "term2");

                // 应用新字体
                window.ApplyTerminalFont("Courier New", 20.0);
                RunJobs(window);

                Assert.Equal(20.0, t1.FontSize, 1);
                Assert.Equal(20.0, t2.FontSize, 1);
                Assert.Contains("Courier New", t1.FontFamily.ToString());
                Assert.Contains("Courier New", t2.FontFamily.ToString());

                // 全局首选族已更新
                Assert.Equal("Courier New", MainWindow.TerminalFontPrimary);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // ═══════════════════════ TERM-25：录制终端输出到文件 ═══════════════════════

    [AvaloniaFact]
    public void TERM25_RecordCapturesOutputToTempFile()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        string tempFile = Path.Combine(Path.GetTempPath(), $"comtool_test_rec_{Guid.NewGuid():N}.log");
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var (terminal, pty, tab) = CreateTestTerminal(window, "rec-test");

                // 启动录制
                window.StartTerminalOutputRecording(terminal, tempFile, "rec-test");
                Assert.True(window.IsOutputRecordingForTests);

                // 通过 pty 发送数据
                pty.Send("Hello Recording!\r\n");

                // 等待读取任务处理（轮询最多 3 秒）
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (DateTime.UtcNow < deadline)
                {
                    Dispatcher.UIThread.RunJobs();
                    if (File.Exists(tempFile) && new FileInfo(tempFile).Length > 0)
                        break;
                    System.Threading.Thread.Sleep(50);
                }

                // 停止录制
                window.StopOutputRecordingForTests();
                Assert.False(window.IsOutputRecordingForTests);

                // 验证文件内容
                Assert.True(File.Exists(tempFile));
                var content = File.ReadAllText(tempFile);
                Assert.Contains("Hello Recording!", content);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [AvaloniaFact]
    public void TERM25_CloseTabAutoStopsRecording()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        string tempFile = Path.Combine(Path.GetTempPath(), $"comtool_test_rec2_{Guid.NewGuid():N}.log");
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var (terminal, pty, tab) = CreateTestTerminal(window, "rec-close");

                // 启动录制
                window.StartTerminalOutputRecording(terminal, tempFile, "rec-close");
                Assert.True(window.IsOutputRecordingForTests);

                // 关闭 tab → 自动停止录制
                window.CloseTabForTests(tab);
                RunJobs(window);

                Assert.False(window.IsOutputRecordingForTests);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ═══════════════════════ 辅助方法 ═══════════════════════

    private static (Iciclecreek.Terminal.TerminalControl Terminal, InMemoryPty Pty, TerminalTab Tab)
        CreateTestTerminal(MainWindow window, string id)
    {
        var vm = (MainViewModel)window.DataContext!;
        var terminal = window.CreateTerminal();
        var pty = new InMemoryPty();
        var tab = new TerminalTab
        {
            Title = id,
            SessionId = id,
            Kind = SessionKind.Ssh,
            Content = terminal,
            Terminal = terminal,
            IsAlive = true,
            IsTransportOpen = () => true,
            CloseAction = () => { },
        };
        vm.AddTab(tab);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        terminal.AttachConnection(pty);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (terminal, pty, tab);
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
