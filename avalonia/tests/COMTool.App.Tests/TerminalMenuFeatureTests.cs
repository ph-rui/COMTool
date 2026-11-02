using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
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

    [AvaloniaFact]
    public async Task TERM24_FontDialogShowsConfiguredFontSizeAndOkKeepsDefault()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        var prevFamily = MainWindow.TerminalFontPrimary;
        MainWindow.TerminalFontPrimary = "Consolas";
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                // 打开字体对话框（模态，异步返回 Task）
                var task = window.ShowFontDialogForTestsAsync();
                WaitUntil(() => window.LastFontDialogForTests is not null, "font dialog opened");
                var dialog = window.LastFontDialogForTests!;
                RunJobs(window);

                // 等"设置→低优先级复验"校正链（Background + ContextIdle 重试 + 平台异步
                // resize 回环）收敛。headless 下平台回环异步且陈旧布局趟可能把旧尺寸推回，
                // 最终值会在 {初始值, 校正值} 间由重试次数决定；泵到稳定（带上限）。
                double prevH = -1;
                for (var i = 0; i < 10; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var h = dialog.ClientSize.Height;
                    if (Math.Abs(h - prevH) < 0.01 && i >= 2)
                    {
                        break;
                    }
                    prevH = h;
                }

                // 回归（实机 RDP 曾显示残缺文本 "1"）：模板应用后 Value 与显示
                // Text 都必须是 config 的 13.5，而不是截断/残留的默认值。
                var spin = dialog.GetVisualDescendants().OfType<NumericUpDown>().FirstOrDefault();
                Assert.NotNull(spin);
                Assert.Equal(13.5m, (decimal)spin!.Value);
                Assert.Equal("13.5", spin.Text);

                // 回归（实机显示 "1" 的真正根因）：12.1.4 Fluent 模板内 TextBox 仅 22px
                // 宽导致 "13.5" 被裁切。控件必须足够宽，使内部 TextBox 实际排布宽度
                // 足以容纳完整文本（14px 字号下 "13.5" 约需 32px+，留余量取 45px）。
                var tb = spin.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                Assert.NotNull(tb);
                Assert.True(tb!.Bounds.Width >= 45.0,
                    $"inner TextBox width {tb.Bounds.Width} too narrow to render '13.5'");

                // 点"确定"（不改值）：对话框关闭、config 字号保持 13.5 无副作用。
                var ok = dialog.GetVisualDescendants()
                    .OfType<Button>().FirstOrDefault(b => Equals(b.Content, "确定"));
                Assert.NotNull(ok);

                // 回归（实机"取消/确定显示不全"五次复验）：按钮必须完整落在客户区内
                // （固定 240 时被裁切）；且不能出现 320 版 134px 悬空或 SizeToContent
                // 在 Win32 量成 720 的巨幅窗。断言取不变量：无裁切 + 底部空白 ≤75px
                // + 客户区高 <400（headless 平台 resize 回环可能使窗口停在初始 230，
                // 此时空白 55px 仍属正常观感，故阈值放宽到 75；实机像素级贴合由
                // [FontDlg] post-layout 日志与 --selftest-fontdlg 核验）。
                var cancel = dialog.GetVisualDescendants()
                    .OfType<Button>().FirstOrDefault(b => Equals(b.Content, "取消"));
                Assert.NotNull(cancel);
                Point? okBottom = ok.TranslatePoint(new Point(0, ok.Bounds.Height), dialog);
                Point? cancelBottom = cancel.TranslatePoint(new Point(0, cancel.Bounds.Height), dialog);
                Assert.NotNull(okBottom);
                Assert.NotNull(cancelBottom);
                double clientH = dialog.ClientSize.Height;
                Assert.True(okBottom!.Value.Y <= clientH,
                    $"OK button bottom {okBottom.Value.Y:0.#} exceeds client height {clientH:0.#} (dialog too short, button clipped)");
                Assert.True(clientH - okBottom.Value.Y <= 75,
                    $"bottom gap {clientH - okBottom.Value.Y:0.#} too large (dialog should be compact)");
                Assert.True(clientH < 400,
                    $"client height {clientH:0.#} too large (SizeToContent 720px regression)");
                Assert.True(cancelBottom!.Value.Y <= clientH,
                    $"Cancel button bottom {cancelBottom.Value.Y:0.#} exceeds client height {clientH:0.#} (dialog too short, button clipped)");

                ok.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                RunJobs(window);
                await task;
                Assert.Null(window.LastFontDialogForTests);
                Assert.Equal(13.5, window.ConfigService!.Config.Terminal.FontSize, 3);
            });
        }
        finally
        {
            MainWindow.TerminalFontPrimary = prevFamily;
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

    // ═══════════════════════ r38：RTF 复制 / 事件日志 / 打印 ═══════════════════════

    [Fact]
    public void TERM26_RtfBuilder_PreservesTextColorsAndStructure()
    {
        using var emu = new COMTool.Terminal.TerminalEmulator(20, 3);
        var term = emu.Terminal;
        term.Colors.SetForeground(0x112233);
        term.Colors.SetBackground(0xAABBCC);
        term.Colors.SetColor(1, 0xCC0000); // ANSI 31（红）

        emu.Write("\u001b[31mRED\u001b[0m plain\n");
        emu.Select(0, 0, 19, 0);

        var rtf = COMTool.App.Services.TerminalRtf.BuildRtf(term, 13.5, "Consolas");
        Assert.NotNull(rtf);
        Assert.StartsWith("{\\rtf1\\ansi", rtf);
        Assert.Contains("RED", rtf);
        Assert.Contains(" plain", rtf);
        Assert.Contains(@"\par", rtf);
        Assert.Contains(@"\fs20", rtf);                       // 13.5px → 20 半点
        Assert.Contains(@"\red204\green0\blue0", rtf);        // ANSI 31 → 0xCC0000
        Assert.Contains(@"\red17\green34\blue51", rtf);       // 默认前景 0x112233
        Assert.Contains(@"\red170\green187\blue204", rtf);    // 默认背景 0xAABBCC

        // CJK 与特殊字符：你=\u20320、好=\u22909，\ { } 必须转义
        emu.Write("你好\\{}x");
        emu.Select(0, 1, 19, 1);
        var rtf2 = COMTool.App.Services.TerminalRtf.BuildRtf(term, 13.5, "Consolas");
        Assert.NotNull(rtf2);
        Assert.Contains(@"\u20320 ", rtf2);
        Assert.Contains(@"\u22909 ", rtf2);
        Assert.Contains(@"\\", rtf2);  // RTF 中字面反斜杠 = 2 个反斜杠字符
        Assert.Contains(@"\{", rtf2);
        Assert.Contains(@"\}", rtf2);

        // 无选区 → null
        term.Selection.ClearSelection();
        Assert.Null(COMTool.App.Services.TerminalRtf.BuildRtf(term, 13.5, "Consolas"));
    }

    [AvaloniaFact]
    public async Task TERM26_RtfCopy_MenuItemWritesRtfAndPlainToClipboard()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var (terminal, _, tab) = CreateTestTerminal(window, "rtf-menu");
                terminal.Terminal.Write("\u001b[31mRED\u001b[0m tail");
                RunJobs(window);

                // 无选区 → 菜单项置灰
                RaiseShiftRightClick(terminal, window);
                Assert.False(MenuLabelItem(window.LastTerminalMenuForTests!, "复制带格式文本（RTF）").IsEnabled);

                // 有选区 → 可用；点击后剪贴板同时含 RTF 与纯文本（headless 剪贴板为内存 stub，可回读）
                terminal.Terminal.Selection.SelectAll();
                RaiseShiftRightClick(terminal, window);
                var rtfItem = MenuLabelItem(window.LastTerminalMenuForTests!, "复制带格式文本（RTF）");
                Assert.True(rtfItem.IsEnabled);
                rtfItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
                RunJobs(window);
                RunJobs(window);
                Assert.Equal("已复制带格式文本（RTF）", tab.StatusMessage);

                var clip = TopLevel.GetTopLevel(terminal)?.Clipboard;
                Assert.NotNull(clip);
                var data = await clip!.TryGetDataAsync();
                Assert.NotNull(data);
                var rtf = await data!.TryGetValueAsync(DataFormat.CreateStringPlatformFormat("Rich Text Format"));
                Assert.NotNull(rtf);
                Assert.StartsWith("{\\rtf1\\ansi", rtf);
                Assert.Contains("RED", rtf);
                var plain = await clip.TryGetTextAsync();
                Assert.Contains("RED", plain);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task TERM27_EventLog_DialogShowsTailAndRefreshes()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var (terminal, _, _) = CreateTestTerminal(window, "evlog");

                // 菜单接线：事件日志恒可用
                RaiseShiftRightClick(terminal, window);
                Assert.True(MenuLabelItem(window.LastTerminalMenuForTests!, "事件日志").IsEnabled);

                var marker1 = "TERM27-M1-" + Guid.NewGuid().ToString("N");
                COMTool.App.Services.AppLog.Info(marker1);

                var task = window.ShowEventLogForTestsAsync();
                WaitUntil(() => window.LastEventLogDialogForTests is not null, "事件日志对话框");
                var dialog = window.LastEventLogDialogForTests!;
                RunJobs(window);
                var box = dialog.GetVisualDescendants().OfType<TextBox>().First();
                Assert.Contains(marker1, box.Text);

                // 新日志行 → 刷新按钮
                var marker2 = "TERM27-M2-" + Guid.NewGuid().ToString("N");
                COMTool.App.Services.AppLog.Info(marker2);
                var refresh = dialog.GetVisualDescendants().OfType<Button>()
                    .First(b => string.Equals(b.Content, "刷新"));
                refresh.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                RunJobs(window);
                Assert.Contains(marker2, box.Text);
                Assert.Contains(marker1, box.Text);

                // 关闭
                var closeBtn = dialog.GetVisualDescendants().OfType<Button>()
                    .First(b => string.Equals(b.Content, "关闭"));
                closeBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await task;
                Assert.Null(window.LastEventLogDialogForTests);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public async Task TERM28_Print_ScriptContentAndNonWindowsGate()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // 非 Windows 门控断言只在非 Windows 环境有意义
        }
        Assert.False(COMTool.App.Services.TerminalPrint.IsSupported);
        var (ok, msg) = await COMTool.App.Services.TerminalPrint.PrintTextAsync("hello");
        Assert.False(ok);
        Assert.Contains("Windows", msg);

        var script = COMTool.App.Services.TerminalPrint.BuildScript(@"C:\temp\my'print.txt");
        Assert.Contains("PrintDocument", script);
        Assert.Contains("PrintDialog", script);
        Assert.Contains("C:\\temp\\my''print.txt", script);
        Assert.DoesNotContain("__COMTOOL_PRINT_FILE__", script);
    }

    [AvaloniaFact]
    public async Task TERM28_Print_MenuClickNonWindowsShowsUnsupported()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var (terminal, _, tab) = CreateTestTerminal(window, "print1");
                terminal.Terminal.Write("printable text");
                RunJobs(window);

                RaiseShiftRightClick(terminal, window);
                var printItem = MenuLabelItem(window.LastTerminalMenuForTests!, "打印");
                Assert.True(printItem.IsEnabled);
                printItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
                RunJobs(window);
                RunJobs(window);
                Assert.Contains("Windows", tab.StatusMessage);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // ═══════════════════════ 辅助方法 ═══════════════════════

    private static Avalonia.Controls.Control RaiseShiftRightClick(
        Iciclecreek.Terminal.TerminalControl terminal, MainWindow window)
    {
        var view = terminal.GetVisualDescendants()
            .OfType<Iciclecreek.Terminal.TerminalView>().FirstOrDefault();
        var target = view ?? (Avalonia.Controls.Control)terminal;
        var position = target.TranslatePoint(new Point(10, 10), window) ?? new Point(10, 10);
        target.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(
            target,
            new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, isPrimary: true),
            window,
            position,
            0,
            new Avalonia.Input.PointerPointProperties(
                Avalonia.Input.RawInputModifiers.None,
                Avalonia.Input.PointerUpdateKind.RightButtonPressed),
            Avalonia.Input.KeyModifiers.Shift,
            clickCount: 1));
        return target;
    }

    private static Avalonia.Controls.MenuItem MenuLabelItem(
        Avalonia.Controls.ContextMenu menu, string label)
    {
        var items = menu.Items.OfType<Avalonia.Controls.MenuItem>().ToList();
        return items.Single(mi => MenuLabel(mi) == label);
    }

    private static string? MenuLabel(Avalonia.Controls.MenuItem item) => item.Header switch
    {
        string s => s,
        Avalonia.Controls.Grid g =>
            g.Children.OfType<Avalonia.Controls.TextBlock>().FirstOrDefault()?.Text,
        _ => item.Header?.ToString(),
    };

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

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}");
            }

            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }
}
