using COMTool.App.ViewModels;

namespace COMTool.App.Services;

/// <summary>
/// 一条「特殊命令」的定义（MobaXterm「Special Command」子菜单项）。
/// SshSignal：SSH 通道信号请求名（RFC 4254 §6.5，如 "INT"/"TERM"/"KILL"/"HUP"）；
/// CtrlByte：串口/TCP/Telnet 下等价的控制字节（如 0x03=Ctrl+C）；
/// IsBreak：硬件断流/SSH break 请求。
/// </summary>
public sealed record SpecialCommandDef(
    string Label,
    string? SshSignal = null,
    byte? CtrlByte = null,
    bool IsBreak = false);

/// <summary>
/// 特殊命令表 + 按会话类型的可用性矩阵（r39 设计定稿）：
/// - SSH：协议信号/break，全表可用；
/// - 串口：硬件 Break + 有控制字节等价物者（^C/^\/^Z）；
/// - TCP/Telnet：仅控制字节等价物；
/// - Local：无可靠信号通道，全置灰。
/// 「忽略消息」（MobaXterm IGNORE message）公开资料无确切语义，r40.2 用户决定不实现，已从菜单移除。
/// </summary>
public static class SpecialCommands
{
    /// <summary>子菜单顶层 5 项（MobaXterm 顺序：Break/SIGINT/SIGTERM/SIGKILL/SIGHUP）。</summary>
    public static readonly IReadOnlyList<SpecialCommandDef> TopLevel =
    [
        new("Break", SshSignal: "BREAK", IsBreak: true),
        new("SIGINT（中断）", SshSignal: "INT", CtrlByte: 0x03),
        new("SIGTERM（终止）", SshSignal: "TERM"),
        new("SIGKILL（强制结束）", SshSignal: "KILL"),
        new("SIGHUP（挂断）", SshSignal: "HUP"),
    ];

    /// <summary>「更多信号」子菜单（RFC 4254 信号集，顶层 4 个信号之外的其余项）。</summary>
    public static readonly IReadOnlyList<SpecialCommandDef> More =
    [
        new("SIGABRT（中止）", SshSignal: "ABRT"),
        new("SIGALRM（闹钟）", SshSignal: "ALRM"),
        new("SIGCONT（继续）", SshSignal: "CONT"),
        new("SIGSTOP（停止）", SshSignal: "STOP"),
        new("SIGTSTP（暂停）", SshSignal: "TSTP", CtrlByte: 0x1A),
        new("SIGTTIN（终端输入）", SshSignal: "TTIN"),
        new("SIGTTOU（终端输出）", SshSignal: "TTOU"),
        new("SIGURG（紧急）", SshSignal: "URG"),
        new("SIGUSR1（用户 1）", SshSignal: "USR1"),
        new("SIGUSR2（用户 2）", SshSignal: "USR2"),
        new("SIGVTALRM（虚拟闹钟）", SshSignal: "VTALRM"),
        new("SIGXCPU（CPU 超限）", SshSignal: "XCPU"),
        new("SIGXFSZ（文件大小超限）", SshSignal: "XFSZ"),
        new("SIGQUIT（注销）", SshSignal: "QUIT", CtrlByte: 0x1C),
    ];

    /// <summary>该命令对给定会话类型是否可用（决定菜单项置灰与否）。</summary>
    public static bool IsAvailable(SpecialCommandDef def, SessionKind kind) => kind switch
    {
        SessionKind.Ssh => def.SshSignal is not null,
        SessionKind.Serial => def.IsBreak || def.CtrlByte is not null,
        SessionKind.Tcp or SessionKind.Telnet => def.CtrlByte is not null,
        _ => false,
    };
}
