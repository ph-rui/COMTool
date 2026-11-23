using COMTool.App.ViewModels;
using COMTool.Core.Config;

namespace COMTool.App.Services;

/// <summary>
/// r67.9：删除会话确认框的文案组装（纯函数，便于单测）。
/// </summary>
/// <remarks>
/// 旧实现把「Remove this {kind} session from memory?」和会话标识拼成一个字符串塞进
/// 同一个 TextBlock，既与实际不符（会话写入 config.json，不是仅存于内存），也没有
/// 层级。这里拆成 问句 / 名称 / 端点 / 备注 四段，名称与端点重复时只保留一段；
/// 删除 SSH/Telnet 会话会同时清掉内存中的密码，存在密码时用备注行明示。
/// </remarks>
internal sealed record SessionRemovalPrompt(
    string Title,
    string Question,
    string Name,
    string Detail,
    string Note)
{
    internal const string DefaultTitle = "Remove session";

    internal const string PasswordNote = "The saved password will also be deleted.";

    internal static SessionRemovalPrompt Build(
        SessionKind kind,
        string sessionId,
        AppConfigService config)
    {
        string kindLabel;
        string name;
        string detail;
        var dropsPassword = false;

        switch (kind)
        {
            case SessionKind.Ssh:
            {
                kindLabel = "SSH";
                var session = config.SshSessions.Get(sessionId);
                name = session is null ? sessionId : SshSessionRules.DisplayName(session);
                detail = session is null ? "" : SshSessionRules.Detail(session);
                dropsPassword = true;
                break;
            }

            case SessionKind.Telnet:
            {
                kindLabel = "Telnet";
                var session = config.TelnetSessions.Get(sessionId);
                name = session is null ? sessionId : TelnetSessionRules.DisplayName(session);
                detail = session is null ? "" : TelnetSessionRules.Detail(session);
                dropsPassword = true;
                break;
            }

            case SessionKind.Tcp:
            {
                kindLabel = "TCP";
                var session = config.TcpSessions.Get(sessionId);
                name = session is null ? sessionId : TcpSessionRules.DisplayName(session);
                detail = session is null ? "" : TcpSessionRules.Detail(session);
                break;
            }

            default:
                kindLabel = "serial";
                name = sessionId;
                detail = "";
                break;
        }

        if (string.Equals(name, detail, StringComparison.Ordinal))
        {
            detail = "";
        }

        var note = dropsPassword && config.PasswordCache.Get(sessionId) is not null
            ? PasswordNote
            : "";

        return new SessionRemovalPrompt(
            DefaultTitle,
            $"Remove this {kindLabel} session?",
            name,
            detail,
            note);
    }
}
