namespace COMTool.Connections.RemoteMonitoring;

/// <summary>
/// 远程监控指标开关，与 MobaXterm 状态栏右键菜单 11 项一一对应
/// （spec 2026-10-29-remote-monitoring 第 1 节）。默认 7 项开启，
/// 与 MobaXterm 22.1 默认勾选一致。
/// </summary>
public sealed class MonitorOptions
{
    public bool ShowHost { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowNetUp { get; set; } = true;
    public bool ShowNetDown { get; set; } = true;
    public bool ShowClients { get; set; }
    public bool ShowProcesses { get; set; }
    public bool ShowFileDescriptors { get; set; }
    public bool ShowUptime { get; set; } = true;
    public bool ShowUsers { get; set; }
    public bool ShowPartitions { get; set; } = true;

    public static MonitorOptions Default { get; } = new MonitorOptions();
}
