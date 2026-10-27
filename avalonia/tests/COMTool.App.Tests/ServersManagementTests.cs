using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.Services;
using COMTool.App.ViewModels.Tools;
using COMTool.App.Views;

namespace COMTool.App.Tests;

/// <summary>
/// SRV-01 (2026-10-26 round): all nine MobaXterm rows are selectable, the
/// NFS/VNC placeholders report "not implemented" in the server output without
/// starting, and the new per-server settings persist in config.json.
/// </summary>
public sealed class ServersManagementTests
{
    [AvaloniaFact]
    public void SRV01_AllNineRowsSelectableWithExclusivePanels()
    {
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new ServersManagementWindow();
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;

                var ids = new[] { "tftp", "ftp", "http", "ssh", "telnet", "nfs", "vnc", "cron", "iperf" };
                foreach (var id in ids)
                {
                    Click(window, ServerRowButton(window, id, "ServerGear"));
                    Assert.True(vm.IsSelectedFor(id), $"{id} panel should be visible");
                    Assert.False(vm.IsWelcome);
                    Assert.True(vm.Find(id)!.IsSelected);
                }

                // Only the last selected row keeps its highlight.
                Assert.True(vm.Find("iperf")!.IsSelected);
                Assert.False(vm.Find("tftp")!.IsSelected);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SRV01_NfsAndVncReportNotImplementedWithoutStarting()
    {
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new ServersManagementWindow();
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;

                Click(window, ServerRowButton(window, "nfs", "ServerPlay"));
                WaitUntil(() => vm.Find("nfs")!.Log.Any(l => l.Contains("暂未实现")),
                    "NFS placeholder message");
                Assert.False(vm.Find("nfs")!.IsRunning);

                Click(window, ServerRowButton(window, "vnc", "ServerPlay"));
                WaitUntil(() => vm.Find("vnc")!.Log.Any(l => l.Contains("暂未实现")),
                    "VNC placeholder message");
                Assert.False(vm.Find("vnc")!.IsRunning);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SRV01_NewServerSettingsPersistToConfig()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var service = AppConfigService.Load();
            var vm = new ServersManagementViewModel(service);

            vm.FtpPort = 2121;
            vm.FtpUser = "alice";
            vm.FtpPassword = "s3cret";
            vm.FtpAnonymous = false;
            vm.TelnetPassword = "telnet-pw";
            vm.CronExpression = "*/2 * * * *";
            vm.CronStartWithApp = true;
            vm.VncHttpPort = 5801;
            vm.IperfPort = 5202;
            vm.SshRoot = "/tmp/comtool-ssh-root";
            vm.TelnetPort = 2323;
            vm.SaveSettings();

            var reloaded = AppConfigService.Load().Config.Servers;
            Assert.Equal(2121, reloaded.Ftp.Port);
            Assert.Equal("alice", reloaded.Ftp.Username);
            Assert.Equal("s3cret", reloaded.Ftp.Password);
            Assert.False(reloaded.Ftp.AllowAnonymous);
            Assert.Equal("telnet-pw", reloaded.Telnet.Password);
            Assert.Equal("*/2 * * * *", reloaded.Cron.CronExpression);
            Assert.True(reloaded.Cron.StartWithApp);
            Assert.Equal(5801, reloaded.Vnc.HttpPort);
            Assert.Equal(5202, reloaded.Iperf.Port);
            Assert.Equal("/tmp/comtool-ssh-root", reloaded.Ssh.RootDirectory);
            Assert.Equal(2323, reloaded.Telnet.Port);
        });
    }

    /// <summary>
    /// 22.1 截图 1:1 核对：面板标题（含 "(实验性)"、大写 IPERF）、
    /// 新字段默认值、动态描述（SSH 用户名 / VNC URL）。
    /// </summary>
    [AvaloniaFact]
    public void SRV01_PanelsMatchMobaXtermTitlesAndFields()
    {
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new ServersManagementWindow();
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var vm = window.ViewModel;

                // 默认值与 MobaXterm 22.1 截图一致
                Assert.Equal(Environment.UserName, vm.FtpUser);
                Assert.True(vm.FtpAnonymous);
                Assert.False(vm.FtpUtf8);
                Assert.True(vm.FtpPrompt);
                Assert.True(vm.TelnetPrompt);
                Assert.Equal(5800, vm.VncHttpPort);
                Assert.False(vm.CronStartWithApp);

                // 面板标题 1:1（含后缀与大写差异）
                var titles = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(t => t.Text)
                    .ToList();
                Assert.Contains(titles, t => t == "TFTP 服务器设置");
                Assert.Contains(titles, t => t == "NFS 服务器设置 (实验性)");
                Assert.Contains(titles, t => t == "SSH/SFTP 服务器设置");
                Assert.Contains(titles, t => t == "IPERF 服务器设置");

                // 动态描述文字
                Assert.Contains(Environment.UserName, vm.SshDescription);
                Assert.Contains("http://", vm.VncDescription);
                Assert.StartsWith("http://", vm.VncUrl);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 布局回归（2026-10-26 部署面板错位事故）：TFTP 面板内各控件必须落在
    /// MobaXterm 22.1 实测坐标上。窗口坐标基准：分组框左上角 (305,43)，
    /// 1px 边框后内部原点 (306,44)。根目录行 rel y=46、监听端口行 rel y=113、
    /// 服务器输出框 rel y=182；复选框标签必须渲染出非零宽度。
    /// </summary>
    [AvaloniaFact]
    public void SRV01_TftpPanelControlsAtMobaXtermCoordinates()
    {
        using var env = new TestEnvironment();
        ServersManagementWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new ServersManagementWindow();
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.ViewModel.Select(window.ViewModel.Find("tftp"));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var rootBox = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(c => c.Name == "TftpRootBox");
                var portBox = window.GetVisualDescendants().OfType<NumericUpDown>().FirstOrDefault(c => c.Name == "TftpPortBox");
                var logBox = window.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(c => c.Name == "TftpLogBox");
                Assert.NotNull(rootBox);
                Assert.NotNull(portBox);
                Assert.NotNull(logBox);

                var rootTop = rootBox!.TranslatePoint(new Point(0, 0), window)!.Value.Y;
                var portTop = portBox!.TranslatePoint(new Point(0, 0), window)!.Value.Y;
                var logTop = logBox!.TranslatePoint(new Point(0, 0), window)!.Value.Y;

                Assert.InRange(rootTop, 86, 94);   // 44 + 46 = 90
                Assert.InRange(portTop, 153, 161); // 44 + 113 = 157
                Assert.InRange(logTop, 222, 230);  // 44 + 182 = 226

                // 根目录输入框要拿到标签列(97)右侧的完整宽度，不能塌成窄条；
                // 1:1 宽度 ~298px（MobaXterm 实测 299px：浏览按钮 17px + 面板右 margin 20）
                Assert.True(rootBox.Bounds.Width >= 293 && rootBox.Bounds.Width <= 303,
                    $"TftpRootBox width {rootBox.Bounds.Width:F0}px (expected ~298)");

                // 回归（实机"根目录显示不全"）：Fluent 主题默认 TextBox Padding 上下各约 5-6px，
                // 22px 框内文字带只剩 ~9px，12px 字体字形上半部被裁；ClassicBox 必须零垂直
                // 内边距 + 垂直居中
                Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, rootBox.VerticalContentAlignment);
                Assert.True(rootBox.Padding.Top == 0 && rootBox.Padding.Bottom == 0,
                    $"TftpRootBox vertical padding {rootBox.Padding} should be 0");

                // ClassicCheck 模板里的 ContentPresenter 标签必须可见
                var label = window.GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Text == "下载成功后显示信息");
                Assert.NotNull(label);
                Assert.True(label!.Bounds.Width > 30,
                    $"checkbox label width {label.Bounds.Width:F0}px (expected > 30)");

                // "服务器输出"标签不得被输出框顶边压住（回归：边框线穿过文字）
                var outLabel = window.GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Text == "服务器输出" && t.Bounds.Height > 0);
                Assert.NotNull(outLabel);
                var outLabelBottom = outLabel!
                    .TranslatePoint(new Point(0, outLabel.Bounds.Height), window)!.Value.Y;
                Assert.True(outLabelBottom < logTop,
                    $"output label bottom {outLabelBottom:F0} overlaps log box top {logTop:F0}");

                // 数值框必须显示出数值（回归：Fluent 模板窄宽下数值区仅 2px 不可见）
                var spinText = portBox!.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                Assert.NotNull(spinText);
                Assert.Equal("69", spinText!.Text);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Click(Window window, Control control)
    {
        var center = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);
        Assert.NotNull(center);
        window.MouseDown(center!.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
        if (window.IsVisible)
        {
            RunJobs(window);
        }
        else
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void RunJobs(Window window)
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

    private static Button ServerRowButton(ServersManagementWindow window, string id,
        string cssClass)
    {
        var button = window.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Tag is string tag && tag == id && b.Classes.Contains(cssClass));
        Assert.NotNull(button);
        return button!;
    }
}
