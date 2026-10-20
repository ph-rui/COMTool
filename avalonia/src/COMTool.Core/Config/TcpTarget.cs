using System.Globalization;

namespace COMTool.Core.Config;

/// <summary>
/// A TCP endpoint written the way the Python TCP/UDP connection accepts it.
/// The parser intentionally mirrors the Python behavior: an omitted port
/// means port 80, a full-width colon is accepted, and a trailing slash is
/// ignored.
/// </summary>
public sealed record TcpTarget(string Host, int Port)
{
    public const int DefaultPort = 80;

    public static bool TryParse(string? value, out TcpTarget? target)
    {
        target = null;
        var text = (value ?? "").Trim();
        if (text.Length == 0)
        {
            return false;
        }

        text = text.Replace('：', ':');

        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var current = text[..scheme];
            if (!string.Equals(current, "http", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(current, "https", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            text = text[(scheme + 3)..];
        }

        text = text.TrimEnd('/');
        if (text.Length == 0 || text.Contains('/'))
        {
            return false;
        }

        var colon = text.LastIndexOf(':');
        string host;
        int port;
        if (colon >= 0)
        {
            host = text[..colon].Trim();
            var portText = text[(colon + 1)..].Trim();
            if (portText.Length == 0)
            {
                port = DefaultPort;
            }
            else if (!int.TryParse(
                         portText,
                         NumberStyles.None,
                         CultureInfo.InvariantCulture,
                         out port))
            {
                return false;
            }
        }
        else
        {
            host = text;
            port = DefaultPort;
        }

        if (host.Length == 0
            || host.Contains(':')
            || host.Any(char.IsWhiteSpace)
            || port is <= 0 or > 65535)
        {
            return false;
        }

        target = new TcpTarget(host, port);
        return true;
    }

    public override string ToString() => $"{Host}:{Port}";
}
