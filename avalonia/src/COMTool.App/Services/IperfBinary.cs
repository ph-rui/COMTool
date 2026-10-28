using Avalonia.Platform;

namespace COMTool.App.Services;

/// <summary>
/// 随包捆绑的 iperf3（1:1 对齐 MobaXterm 自带 iperf）：官方推荐构建
/// iperf-3.22-win64.zip（ar51an/iperf3-win-builds，Cygwin 构建，exe 依赖
/// cygwin1.dll，两个文件必须同目录）。首次启动 Iperf 服务器时把这两个资源
/// 释放到程序基目录下的 iperf3/ 子目录，之后直接复用；非 Windows 或释放
/// 失败返回 null，引擎回退 exe 目录 / PATH 查找。许可证见
/// avalonia/third_party/iperf3/LICENSE。
/// </summary>
internal static class IperfBinary
{
    private static readonly Uri[] Assets =
    {
        new Uri("avares://COMTool.App/Assets/iperf3/iperf3.exe"),
        new Uri("avares://COMTool.App/Assets/iperf3/cygwin1.dll"),
    };

    private static readonly object _gate = new object();
    private static string? _cached;

    public static string? Locate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        lock (_gate)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "iperf3");
                var exe = Path.Combine(dir, "iperf3.exe");
                if (!File.Exists(exe))
                {
                    Directory.CreateDirectory(dir);
                    foreach (var asset in Assets)
                    {
                        var target = Path.Combine(dir, asset.Segments[^1]);
                        if (!File.Exists(target))
                        {
                            using var stream = AssetLoader.Open(asset);
                            using var fs = File.Create(target);
                            stream.CopyTo(fs);
                        }
                    }
                }

                _cached = exe;
                return exe;
            }
            catch (Exception)
            {
                // 基目录不可写（如装到 Program Files）→ 交给引擎回退 PATH
                return null;
            }
        }
    }
}
