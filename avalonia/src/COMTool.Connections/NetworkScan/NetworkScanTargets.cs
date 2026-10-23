using System.Net;

namespace COMTool.Connections.NetworkScan;

/// <summary>
/// Parsers for the MobaXterm-style tool dialogs:
///  - 网络扫描仪 "IP 地址范围" form: three fixed octets plus a last-octet
///    range (MobaXterm scans a single /24, e.g. 192.168.1.1 -&gt; 254).
///  - 端口扫描仪 "IP 地址" form: a single strict dotted-quad IPv4 address.
/// Only strict dotted-quad IPv4 is accepted; the legacy 2/3-part and 32-bit
/// forms ("192.168.1", "10.0.0.2000") are rejected.
/// </summary>
public static class NetworkScanTargets
{
    /// <summary>
    /// MobaXterm 网络扫描仪 "IP 地址范围" form: [o1] . [o2] . [o3] .
    /// [from] --&gt; [to]. The first three octets are fixed, only the last
    /// octet forms an inclusive range. Returns the expanded host list.
    /// </summary>
    public static bool TryParseSubnet(
        string? firstOctet,
        string? secondOctet,
        string? thirdOctet,
        string? fromOctet,
        string? toOctet,
        out IReadOnlyList<IPAddress> targets,
        out string? error)
    {
        targets = Array.Empty<IPAddress>();
        error = null;

        if (!TryParseOctet(firstOctet, out var o1, out error))
        {
            return false;
        }

        if (!TryParseOctet(secondOctet, out var o2, out error))
        {
            return false;
        }

        if (!TryParseOctet(thirdOctet, out var o3, out error))
        {
            return false;
        }

        if (!TryParseOctet(fromOctet, out var o4From, out error))
        {
            return false;
        }

        if (!TryParseOctet(toOctet, out var o4To, out error))
        {
            return false;
        }

        if (o4To < o4From)
        {
            error = "范围结束值不能小于起始值";
            return false;
        }

        var baseAddr = ((uint)o1 << 24) | ((uint)o2 << 16) | ((uint)o3 << 8);
        var count = o4To - o4From + 1;
        var list = new List<IPAddress>(count);
        for (var o4 = o4From; o4 <= o4To; o4++)
        {
            list.Add(MakeIp(baseAddr | (uint)o4));
        }

        targets = list;
        return true;
    }

    /// <summary>
    /// 端口扫描仪 "IP 地址" field: a single strict IPv4 address.
    /// </summary>
    public static bool TryParseIp(string? text, out IPAddress ip, out string? error)
    {
        ip = IPAddress.Any;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "IP 地址不能为空";
            return false;
        }

        if (!TryParseIpv4(text.Trim(), out var value, out _))
        {
            error = "IP 地址无效（仅支持 IPv4 点分十进制）";
            return false;
        }

        ip = MakeIp(value);
        return true;
    }

    private static bool TryParseOctet(string? text, out int value, out string? error)
    {
        value = 0;
        error = null;

        if (!int.TryParse((text ?? "").Trim(), System.Globalization.NumberStyles.None, null, out var o)
            || o is < 0 or > 255)
        {
            error = "IP 地址段无效（须为 0~255 的整数）";
            return false;
        }

        value = o;
        return true;
    }

    /// <summary>
    /// Strict dotted-quad parser. IPAddress.TryParse additionally accepts the
    /// legacy 2/3-part and 32-bit forms ("192.168.1", "10.0.0.2000"), which
    /// must be rejected here.
    /// </summary>
    private static bool TryParseIpv4(string text, out uint value, out string? error)
    {
        value = 0;
        error = null;

        var parts = text.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        var octets = new uint[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, null, out var o)
                || o is < 0 or > 255)
            {
                return false;
            }

            octets[i] = (uint)o;
        }

        value = (octets[0] << 24) | (octets[1] << 16) | (octets[2] << 8) | octets[3];
        return true;
    }

    private static IPAddress MakeIp(uint value) =>
        new(new byte[]
        {
            (byte)(value >> 24), (byte)(value >> 16),
            (byte)(value >> 8),  (byte)value,
        });
}
