using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Connections;
using COMTool.Core.Config;
using Xunit;

namespace COMTool.App.Tests;

/// <summary>
/// TERM-19：pty 与终端视口尺寸同步（top 等全屏程序只画 30 行的根因：附加时
/// 首个真实尺寸从未推给远端，pty 停在 CreateShellStream 的 100x30）与
/// 粘贴行尾规范（Windows 剪贴板 CRLF 经远端 ICRNL 后每条命令之间多一空行，
/// MobaXterm 规范成单个 \r）。
/// </summary>
public sealed class TerminalPtyFeatureTests
{
    private sealed class CapturingWriter : Stream
    {
        private readonly MemoryStream _ms = new();

        public byte[] Captured => _ms.ToArray();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _ms.Length;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            _ms.Write(buffer, offset, count);
    }

    [Fact]
    public void TERM19_PasteLineEndingsNormalizeToSingleCarriageReturn()
    {
        // Windows 剪贴板 CRLF：每条命令只发一个 \r（= 一次回车），不多打空行。
        Assert.Equal("a\rb\rc\r", MainWindow.NormalizePtyPasteText("a\r\nb\r\nc\r\n"));
        // Linux 剪贴板 LF：同样规范成 \r。
        Assert.Equal("a\rb\r", MainWindow.NormalizePtyPasteText("a\nb\n"));
        // 已是 \r 的不变；无行尾的不变。
        Assert.Equal("a\rb", MainWindow.NormalizePtyPasteText("a\rb"));
        Assert.Equal("echo hi", MainWindow.NormalizePtyPasteText("echo hi"));
        // 末尾的空行去掉，只保留最后一个命令的回车（对齐 MobaXterm 粘贴行为）。
        Assert.Equal(
            "echo hello\recho world\r",
            MainWindow.NormalizePtyPasteText("echo hello\r\necho world\r\n\r\n\r\n"));
        Assert.Equal("a\r", MainWindow.NormalizePtyPasteText("a\r\r\r"));
        Assert.Equal("", MainWindow.NormalizePtyPasteText("\r\n"));
    }

    [AvaloniaFact]
    public async Task TERM19_PasteNormalizedSendsSingleCrPerLineToPty()
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
                var viewModel = (MainViewModel)window.DataContext!;

                var terminal = window.CreateTerminal();
                var writer = new CapturingWriter();
                var pty = new PtyStreamConnection(
                    new MemoryStream(new byte[0]),
                    writer,
                    ownsStreams: false,
                    interceptZmodem: false);
                var tab = new TerminalTab
                {
                    Title = "paste",
                    SessionId = "paste",
                    Kind = SessionKind.Local,
                    Content = terminal,
                    Terminal = terminal,
                    IsAlive = true,
                    CloseAction = () => { },
                };
                viewModel.AddTab(tab);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                terminal.AttachConnection(pty);
                Dispatcher.UIThread.RunJobs();

                // 直接走 SendPasteTextAsync，避开 headless 剪贴板 async 死锁；
                // 与 PasteNormalizedAsync 走相同的 规范化+发送 路径。
                await MainWindow.SendPasteTextAsync(terminal, "l1\r\nl2\r\n");

                var sent = System.Text.Encoding.ASCII.GetString(writer.Captured);
                // 多行粘贴之间不得出现空行；末尾空行被剥除，只保留各命令的回车。
                Assert.Equal("l1\rl2\r", sent);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM19_SyncPtySizeMatchesViewportAfterAttach()
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
                var viewModel = (MainViewModel)window.DataContext!;

                var terminal = window.CreateTerminal();
                int lastCols = -1;
                int lastRows = -1;
                var pty = new PtyStreamConnection(
                    new MemoryStream(new byte[0]),
                    new CapturingWriter(),
                    resize: (c, r) =>
                    {
                        lastCols = c;
                        lastRows = r;
                    },
                    ownsStreams: false,
                    interceptZmodem: false);
                var tab = new TerminalTab
                {
                    Title = "sync",
                    SessionId = "sync",
                    Kind = SessionKind.Local,
                    Content = terminal,
                    Terminal = terminal,
                    IsAlive = true,
                    CloseAction = () => { },
                };
                viewModel.AddTab(tab);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                terminal.AttachConnection(pty);
                Dispatcher.UIThread.RunJobs();

                // 附加后显式同步：cols/rows 必须为正且与视口度量一致。
                MainWindow.SyncPtySizeToViewport(tab, terminal, pty);

                Assert.True(lastCols > 0 && lastRows > 0);
                var expectedCols = (int)((terminal.Bounds.Width -
                    Math.Max(0, terminal.GutterWidth)) / terminal.CharWidth);
                var expectedRows = (int)(terminal.Bounds.Height / terminal.CharHeight);
                Assert.Equal(expectedCols, lastCols);
                Assert.Equal(expectedRows, lastRows);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM20_AttachTerminalWrapsReaderWithLineEndingStream()
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
                var viewModel = (MainViewModel)window.DataContext!;

                var terminal = window.CreateTerminal();
                var pty = new PtyStreamConnection(
                    new MemoryStream(new byte[0]),
                    new CapturingWriter(),
                    ownsStreams: false,
                    interceptZmodem: false);
                var tab = new TerminalTab
                {
                    Title = "le",
                    SessionId = "le",
                    Kind = SessionKind.Local,
                    Content = terminal,
                    Terminal = terminal,
                    IsAlive = true,
                    CloseAction = () => { },
                };
                viewModel.AddTab(tab);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                // 走真实附加路径：必须在 AttachConnection 之前套上行尾变换流，
                // 库读循环每次取 ReaderStream 属性，替换即刻生效。
                window.AttachTerminal(tab, terminal, pty);
                Dispatcher.UIThread.RunJobs();

                Assert.True(
                    pty.ReaderStream is LineEndingStream,
                    "attach 后读流应为 LineEndingStream");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM21_ShiftRightClickOpensMobaXtermStyleTerminalMenu()
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
                var viewModel = (MainViewModel)window.DataContext!;

                var terminal = window.CreateTerminal();
                var pty = new PtyStreamConnection(
                    new MemoryStream(new byte[0]),
                    new CapturingWriter(),
                    ownsStreams: false,
                    interceptZmodem: false);
                var tab = new TerminalTab
                {
                    Title = "ctx",
                    SessionId = "ctx",
                    Kind = SessionKind.Local,
                    Content = terminal,
                    Terminal = terminal,
                    IsAlive = true,
                    CloseAction = () => { },
                };
                viewModel.AddTab(tab);
                window.AttachTerminal(tab, terminal, pty);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                // 普通右键：走原有复制/粘贴路径，不得弹菜单（Shift 门控）。
                // 派发目标必须是内层 TerminalView（真实命中目标），否则本测试退化为旧路径。
                var plainTarget = RaiseTerminalRightClick(terminal, window, Avalonia.Input.KeyModifiers.None);
                Assert.IsType<Iciclecreek.Terminal.TerminalView>(plainTarget);
                Assert.Null(window.LastTerminalMenuForTests);

                // Shift+右键：弹出 MobaXterm 22.1 式终端菜单（18 项 + 4 分隔线）。
                var shiftTarget = RaiseTerminalRightClick(terminal, window, Avalonia.Input.KeyModifiers.Shift);
                Assert.IsType<Iciclecreek.Terminal.TerminalView>(shiftTarget);
                var menu = window.LastTerminalMenuForTests;
                Assert.NotNull(menu);

                var items = menu!.Items.OfType<Avalonia.Controls.MenuItem>().ToList();
                Assert.Equal(18, items.Count);
                Assert.Equal(4, menu.Items.OfType<Avalonia.Controls.Separator>().Count());

                Assert.Equal(new[]
                {
                    "复制", "复制全部", "复制带格式文本（RTF）", "粘贴", "查找",
                    "字体设置", "终端显示", "语法高亮",
                    "执行宏", "录制宏", "录制终端输出到文件", "保存到文件", "打印",
                    "使用 Zmodem 接收文件", "使用 Zmodem 发送文件",
                    "更改终端设置…", "特殊命令", "事件日志",
                },
                items.Select(ItemLabel).ToList());

                // 启用态：无选区时“复制”置灰；“粘贴”/“更改终端设置…”恒可用；
                // 未实现项（查找/打印等）置灰。
                Assert.False(LabelItem(items, "复制").IsEnabled);
                Assert.True(LabelItem(items, "粘贴").IsEnabled);
                Assert.True(LabelItem(items, "更改终端设置…").IsEnabled);
                Assert.False(LabelItem(items, "查找").IsEnabled);
                Assert.False(LabelItem(items, "打印").IsEnabled);
                Assert.True(LabelItem(items, "使用 Zmodem 接收文件").IsEnabled);

                // 快捷键标注（MobaXterm 样式右侧灰字）：粘贴 = Shift+INSERT。
                var pasteGrid = (Avalonia.Controls.Grid)LabelItem(items, "粘贴").Header!;
                Assert.Equal(
                    "Shift+INSERT",
                    pasteGrid.Children.OfType<Avalonia.Controls.TextBlock>().Last().Text);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 对内层 TerminalView（库的真实命中目标）RaiseEvent，覆盖真实隧道路径：
    /// window → … → TerminalControl（我们的隧道 handler）→ TerminalView（目标，库类 handler）。
    /// 旧版直接在 TerminalControl 上 RaiseEvent 绕过了库的抢先逻辑，掩盖了实机 bug。
    /// 返回实际派发目标，调用方必须断言其为 TerminalView（防止视觉树未就绪时
    /// 静默回退到旧路径、让测试失去意义）。
    /// </summary>
    private static Avalonia.Controls.Control RaiseTerminalRightClick(
        Iciclecreek.Terminal.TerminalControl terminal,
        Avalonia.Controls.Window window,
        Avalonia.Input.KeyModifiers modifiers)
    {
        var view = terminal.GetVisualDescendants()
            .OfType<Iciclecreek.Terminal.TerminalView>()
            .FirstOrDefault();
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
            modifiers,
            clickCount: 1));
        return target;
    }

    private static Avalonia.Controls.MenuItem LabelItem(
        System.Collections.Generic.IReadOnlyList<Avalonia.Controls.MenuItem> items, string label) =>
        items.Single(mi => ItemLabel(mi) == label);

    private static string? ItemLabel(Avalonia.Controls.MenuItem item) => item.Header switch
    {
        string s => s,
        Avalonia.Controls.Grid g =>
            g.Children.OfType<Avalonia.Controls.TextBlock>().FirstOrDefault()?.Text,
        _ => null,
    };
}
