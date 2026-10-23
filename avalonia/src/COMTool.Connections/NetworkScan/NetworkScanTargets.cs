using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace COMTool.Connections.NetworkScan;

/// <summary>
/// Parses the user-entered scan target. Accepted forms (comma / semicolon /
/// whitespace separated, any mix):
///   - a single IPv4 address:      192.168.1.5
///   - a CIDR block:               192.168.1.0/24
///   - a last-octet range:         192.168.1.10-50
///   - a full-IP range:            192.168.1.10-192.168.1.50
/// Only strict dotted-quad IPv4 is accepted; the legacy 2/3-part and 32-bit
/// forms ("192.168.1", "10.0.0.2000") are rejected. The total host count is
/// capped at <see cref="MaxHosts"/>.
/// </summary>
public static class NetworkScanTargets
{
    public const int MaxHosts = 1024;

    public static bool TryParse(
        string? text,
        out IReadOnlyList<IPAddress> targets,
        out string? error)
    {
        targets = Array.Empty<IPAddress>();
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "目标不能为空";
            return false;
        }

        var found = new List<IPAddress>();
        var seen = new HashSet<uint>();
        foreach (var rawToken in text.Split(
                     new[] { ',', ';', ' ', '\t' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim();
            if (!TryParseToken(token, found, seen, out error))
            {
                return false;
            }
        }

        if (found.Count == 0)
        {
            error = "没有有效的目标地址";
            return false;
        }

        targets = found;
        return true;
    }

    /// <summary>
    /// Detects the first usable local IPv4 subnet (e.g. "192.168.1.0/24") to
    /// use as the default scan target. Falls back to loopback when no
    /// suitable interface is present.
    /// </summary>
    public static string DetectLocalCidr()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    var bytes = ua.Address.GetAddressBytes();
                    if (bytes[0] == 127 || (bytes[0] == 169 && bytes[1] == 254))
                    {
                        continue;
                    }

                    var maskBytes = ua.IPv4Mask?.GetAddressBytes();
                    if (maskBytes is null)
                    {
                        continue;
                    }

                    var ip = ToUInt32(bytes);
                    var mask = ToUInt32(maskBytes);
                    var prefix = PopCount(mask);
                    var network = ip & mask;
                    return $"{MakeIp(network)}/{prefix}";
                }
            }
        }
        catch
        {
            // Enumeration can fail on restricted platforms; use the fallback.
        }

        return "127.0.0.1";
    }

    private static bool TryParseToken(string token, List<IPAddress> found, HashSet<uint> seen, out string? error)
    {
        error = null;

        // CIDR form.
        var slash = token.IndexOf('/');
        if (slash >= 0)
        {
            var basePart = token[..slash];
            var prefixPart = token[(slash + 1)..];
            if (!TryParseIpv4(basePart, out var baseAddr, out error)
                || !int.TryParse(prefixPart, out var prefix)
                || prefix is < 0 or > 32)
            {
                error ??= $"无效的 CIDR 表示：{token}";
                return false;
            }

            var hostCount = 1u << (32 - prefix);
            if (found.Count + hostCount > MaxHosts)
            {
                error = $"目标主机数超过上限 {MaxHosts}";
                return false;
            }

            var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            var network = baseAddr & mask;
            for (var i = 0u; i < hostCount; i++)
            {
                AddHost(network + i, found, seen);
            }

            return true;
        }

        // Range form.
        var dash = token.IndexOf('-');
        if (dash >= 0)
        {
            var startPart = token[..dash];
            var endPart = token[(dash + 1)..];
            if (!TryParseIpv4(startPart, out var start, out error))
            {
                error ??= $"无效的范围起始地址：{token}";
                return false;
            }

            uint end;
            if (int.TryParse(endPart, out var lastOctet) && lastOctet is >= 0 and <= 255)
            {
                // Short form: "192.168.1.10-50".
                end = (start & 0xFFFFFF00) | (uint)lastOctet;
            }
            else if (!TryParseIpv4(endPart, out end, out _))
            {
                error = $"无效的范围结束地址：{token}";
                return false;
            }

            if (end < start)
            {
                error = $"范围结束地址小于起始地址：{token}";
                return false;
            }

            var count = (long)end - start + 1;
            if (count > MaxHosts - found.Count)
            {
                error = $"目标主机数超过上限 {MaxHosts}";
                return false;
            }

            for (var addr = start; addr <= end; addr++)
            {
                AddHost(addr, found, seen);
            }

            return true;
        }

        // Single address form.
        if (!TryParseIpv4(token, out var single, out _))
        {
            error = $"无效的地址：{token}（仅支持 IPv4）";
            return false;
        }

        AddHost(single, found, seen);
        return true;
    }

    private static void AddHost(uint addr, List<IPAddress> found, HashSet<uint> seen)
    {
        if (!seen.Add(addr))
        {
            return;
        }

        found.Add(MakeIp(addr));
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

    private static uint ToUInt32(byte[] bytes) =>
        (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);

    private static IPAddress MakeIp(uint value) =>
        new(new byte[]
        {
            (byte)(value >> 24), (byte)(value >> 16),
            (byte)(value >> 8),  (byte)value,
        });

    private static int PopCount(uint value)
    {
        var count = 0;
        while (value != 0)
        {
            count += (int)(value & 1u);
            value >>= 1;
        }

        return count;
    }
}
