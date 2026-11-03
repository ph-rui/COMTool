using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Connections;
using COMTool.Core.Config;
using Xunit;

namespace COMTool.App.Tests;

/// <summary>
/// TERM-01：终端输出字符保真（r41 探针）。远端 zh_CN.UTF-8 下 ls 对含空格
/// 文件名加 '...' 单引号，实机发现引号在终端上丢失（MobaXterm 同流可见）。
/// XTerm.NET 解析层探针已证保留引号；此处走真实 AttachConnection 读路径
/// （含 ZmodemStream/无 Zmodem 两条）回归丢字符层，含 CJK 文本完整性。
/// </summary>
public sealed class TerminalQuoteFidelityTests
{
    private const string Payload =
        "devuser@winhost2:~/tftp_dir$ ls\r\n" +
        "doc    'i.MX95 frdm imx623 cms方案搭建.docx'  modules.tar.zst\r\n" +
        "devuser@winhost2:~/tftp_dir$ ";

    [AvaloniaFact]
    public async Task TERM01_QuotesSurviveAttachedReadPathWithZmodem()
    {
        await RunQuoteFidelity(interceptZmodem: true);
    }

    [AvaloniaFact]
    public async Task TERM01_QuotesSurviveAttachedReadPathWithoutZmodem()
    {
        await RunQuoteFidelity(interceptZmodem: false);
    }

    private static async Task RunQuoteFidelity(bool interceptZmodem)
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
                var pty = new PtyStreamConnection(
                    new MemoryStream(Encoding.UTF8.GetBytes(Payload)),
                    new MemoryStream(),
                    ownsStreams: false,
                    interceptZmodem: interceptZmodem);
                var tab = new TerminalTab
                {
                    Title = "t32",
                    SessionId = "t32",
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

                // 等读循环消费完 payload（第二行出现 docx 即视为落屏）
                var buf = terminal.Terminal.Buffer;
                var line = "";
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline)
                {
                    line = buf.GetLine(1)?.TranslateToString(true, 0, buf.Cols) ?? "";
                    if (line.Contains("docx", StringComparison.Ordinal))
                    {
                        break;
                    }

                    await Task.Delay(50);
                    Dispatcher.UIThread.RunJobs();
                }

                Assert.Contains("docx", line);
                // 开闭引号必须都在（ls 的 shell-escape 引号），且 CJK 文本完整
                Assert.Contains("'i.MX95", line);
                Assert.Contains("docx'", line);
                Assert.Contains("cms方案搭建", line);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
