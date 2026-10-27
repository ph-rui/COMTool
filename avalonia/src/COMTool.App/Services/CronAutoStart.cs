using COMTool.Connections.Servers;
using COMTool.Core.Config;

namespace COMTool.App.Services;

/// <summary>
/// App-level owner of the auto-started Cron server ("COMTool 启动时启动
/// Cron 服务器"). Lives outside the servers dialog so the server keeps
/// running while the dialog is closed; the dialog adopts the same host
/// instance for its Cron row so start/stop stays in sync. Stopped on app
/// exit.
/// </summary>
public static class CronAutoStart
{
    public static CronServer? Host { get; private set; }

    public static bool IsRunning => Host?.IsRunning == true;

    /// <summary>Starts the shared Cron server when the config opts in. No-op otherwise.</summary>
    public static async Task EnsureAsync(CronServerSettings settings)
    {
        if (!settings.StartWithApp || Host is not null)
        {
            return;
        }

        var host = new CronServer(settings.CronExpression, settings.Command);
        host.LogMessage += message => AppLog.Info($"[cron] {message}");
        Host = host;
        await host.StartAsync();
        AppLog.Info($"Cron auto-start: expression='{settings.CronExpression}' command='{settings.Command}'");
    }

    /// <summary>Adopts a host created by the dialog as the shared instance.</summary>
    public static void Adopt(CronServer host)
    {
        if (Host is null)
        {
            host.LogMessage += message => AppLog.Info($"[cron] {message}");
            Host = host;
        }
    }

    /// <summary>Called after the dialog stopped/disposed the shared host.</summary>
    public static void Detach(CronServer host)
    {
        if (ReferenceEquals(Host, host))
        {
            Host = null;
        }
    }

    public static async Task StopAsync()
    {
        if (Host is null)
        {
            return;
        }

        var host = Host;
        Host = null;
        try
        {
            await host.StopAsync();
            await host.DisposeAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to stop auto-start cron server", ex);
        }
    }
}
