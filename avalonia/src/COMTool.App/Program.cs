using Avalonia;
using System;

namespace COMTool.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // r51: 锁定启动计时 T0（单文件自提取发生在 CLR 之前，不含在此时长内）
        Services.StartupClock.Start();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

        // VDI 卡顿 A/B 开关（2026-10-28）：
        // 设置环境变量 COMTOOL_SW_RENDER=1 后启动，强制 Win32 走 CPU framebuffer 渲染
        // （Win32RenderingMode.Software），完全绕开虚拟 GPU 的 AngleEgl(D3D11) 链路。
        // 未设置或 =0 时保持默认行为（RenderingMode 默认 { AngleEgl, Software } 按序回退）。
        // 用于在 SANGFOR VDI 上对比 D3D 硬件路径与纯 CPU 路径的流畅度。
        var sw = Environment.GetEnvironmentVariable("COMTOOL_SW_RENDER");
        if (sw is not null and not "" and not "0")
        {
            builder = builder.With(new Win32PlatformOptions
            {
                RenderingMode = new[] { Win32RenderingMode.Software },
            });
        }

        return builder;
    }
}
