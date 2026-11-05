using System.Diagnostics;

namespace COMTool.App.Services;

/// <summary>
/// r51: 启动阶段计时。T0 在 Program.Main 第一行锁定（静态初始化延迟到首次访问）。
/// 单文件自提取发生在 CLR 启动之前、不在 T0 内：需用自检脚本的外层 wall time
/// 减去 T0→退出 的进程内总时长，才能反出自提取+CLR 初始化的耗时。
/// </summary>
public static class StartupClock
{
    private static readonly DateTime T0 = DateTime.UtcNow;
    private static readonly Stopwatch Sw = Stopwatch.StartNew();

    /// <summary>在 Program.Main 第一行调用，锁定 T0。</summary>
    public static void Start()
    {
    }

    public static double Ms => Sw.Elapsed.TotalMilliseconds;

    public static void Mark(string phase) =>
        AppLog.Info($"[Startup] {phase} t+{Ms:F0}ms");
}
