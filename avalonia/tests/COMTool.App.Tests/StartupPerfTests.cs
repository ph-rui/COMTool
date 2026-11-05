using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

/// <summary>
/// r51: startup performance. The serial port scan (GetPortNames + SetupAPI
/// friendly names) used to run synchronously on the UI thread every 2 seconds
/// (DispatcherTimer in MainWindow.OnLoaded), periodically blocking the UI on
/// machines with USB serial adapters (user-reported "运行卡卡的"). It must now
/// run on a worker thread and return to the UI thread immediately.
/// </summary>
public sealed class StartupPerfTests
{
    [AvaloniaFact]
    public void PERF01_PortScanDoesNotBlockUiThread()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig());
        env.Run(() =>
        {
            var window = new MainWindow { DataContext = new MainViewModel() };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                if (window.ConfigService is null)
                {
                    throw new InvalidOperationException("ConfigService 未就绪（OnLoaded 未运行）");
                }

                // 注入 300ms「慢扫描」：同步实现在调用内阻塞满 300ms，
                // 异步实现立即返回、稍后在 UI 线程应用结果。
                window.PortScanOverrideForTests = () =>
                {
                    Thread.Sleep(300);
                    return new SerialPortScanResult(true, new[] { new SerialPortInfo("COM99", "TEST-PORT") });
                };

                var sw = Stopwatch.StartNew();
                window.RefreshPorts();
                sw.Stop();
                Assert.True(sw.ElapsedMilliseconds < 100);

                // 泵 dispatcher 直到后台扫描结果落地（上限 3s）。
                // 注意：RunJobs(int) 的重载是 DispatcherPriority 而非超时，必须用无参版本。
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (DateTime.UtcNow < deadline &&
                       !window.ConfigService.Ports.Any(p => p.Device == "COM99"))
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(10);
                }
                Assert.True(window.ConfigService.Ports.Any(p => p.Device == "COM99"));
            }
            finally
            {
                window.PortScanOverrideForTests = null;
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }
}
