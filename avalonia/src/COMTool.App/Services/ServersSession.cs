using COMTool.App.ViewModels.Tools;

namespace COMTool.App.Services;

/// <summary>
/// 网络服务器的进程级共享 ViewModel（对齐 MobaXterm）：服务器宿主、日志、
/// 运行倒计时归应用所有，而不是对话框所有。关闭"网络服务器"窗口不停止
/// 正在运行的服务；重新打开窗口时运行状态、日志与倒计时自动恢复。服务
/// 统一在主窗口关闭（应用退出）时停止，见 MainWindow.OnClosed。
/// </summary>
public static class ServersSession
{
    private static ServersManagementViewModel? _shared;
    private static readonly object _gate = new object();

    public static ServersManagementViewModel? Shared
    {
        get
        {
            lock (_gate)
            {
                return _shared;
            }
        }
    }

    public static ServersManagementViewModel GetOrCreate(AppConfigService? config)
    {
        lock (_gate)
        {
            _shared ??= new ServersManagementViewModel(config);
            return _shared;
        }
    }
}
