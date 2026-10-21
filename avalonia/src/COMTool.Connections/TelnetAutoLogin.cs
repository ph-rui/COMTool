using System.Text;
using System.Text.RegularExpressions;

namespace COMTool.Connections;

/// <summary>
/// Recognizes login prompts in a bounded tail of decoded Telnet output and
/// sends the configured credentials once. No prompt means no credential is
/// sent, so a silent or unexpected server is never fed a password blindly.
/// </summary>
internal sealed class TelnetAutoLogin
{
    private const int MaxTailLength = 8192;

    private static readonly Regex AnsiEscape = new(
        @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\)|[@-Z\\-_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UserPrompt = new(
        @"(?i)(?:^|[\r\n])\s*(?:>>?\s*)?(?:(?:please|enter|input|your)\s+)*(?:login(?:\s+as)?|login\s+(?:name|id)|user(?:\s*(?:name|id))?|account(?:\s*name)?|用户名|账号|登录名|登录|用户)\s*[:：][\s\x00]*\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PasswordPrompt = new(
        @"(?i)(?:^|[\r\n])\s*(?:>>?\s*)?(?:(?:please|enter|input|your)\s+)*(?:password|passwd|passphrase|passcode|pin|密码|口令)(?:\s+for\s+[^\r\n:]+)?\s*[:：][\s\x00]*\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _user;
    private readonly string _password;
    private readonly Action<string> _send;
    private readonly StringBuilder _tail = new();
    private bool _userSent;
    private bool _passwordSent;

    public TelnetAutoLogin(string user, string? password, Action<string> send)
    {
        _user = user ?? "";
        _password = password ?? "";
        _send = send;
    }

    public void Observe(ReadOnlySpan<byte> data)
    {
        if (_user.Length == 0 || data.Length == 0)
        {
            return;
        }

        _tail.Append(Encoding.UTF8.GetString(data));
        if (_tail.Length > MaxTailLength)
        {
            _tail.Remove(0, _tail.Length - MaxTailLength);
        }

        var text = AnsiEscape.Replace(_tail.ToString(), "");
        if (!_userSent && UserPrompt.IsMatch(text))
        {
            _userSent = true;
            _tail.Clear();
            _send(_user + "\r");
            return;
        }

        if (_userSent
            && !_passwordSent
            && _password.Length > 0
            && PasswordPrompt.IsMatch(text))
        {
            _passwordSent = true;
            _tail.Clear();
            _send(_password + "\r");
        }
    }
}
