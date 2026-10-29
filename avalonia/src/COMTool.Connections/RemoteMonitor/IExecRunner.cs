namespace COMTool.Connections.RemoteMonitoring;

/// <summary>
/// 在远端主机执行命令并返回标准输出的传输抽象。
/// 远程监控轮询通过该接口读取 /proc 指标，与交互式 shell 通道相互独立。
/// </summary>
public interface IExecRunner
{
    /// <summary>
    /// 执行一条远端命令并返回其标准输出。命令失败、传输异常或超时
    /// （timeoutMs 毫秒）时抛出异常。
    /// </summary>
    Task<string> RunCommandAsync(string command, int timeoutMs = 5000);
}
