using System.Net;
using System.Net.Sockets;

namespace COMTool.Connections.NetworkScan;

/// <summary>
/// Best-effort NetBIOS (NBNS) name lookup for one IPv4 host. Used as a
/// fallback when reverse DNS has no PTR record — the usual case on small
/// LANs, where MobaXterm's scanner still shows the computer name.
///
/// Sends a node-status request (name "*", QTYPE NBSTAT) to UDP 137, exactly
/// like <c>nbtstat -A</c>, and reads the workstation name from the reply.
/// </summary>
public static class NetbiosNameResolver
{
    /// <summary>Default UDP 137 wait; short so a /24 scan is not slowed down.</summary>
    public const int DefaultTimeoutMs = 300;

    /// <summary>
    /// Returns the host's NetBIOS workstation name (lower case, as MobaXterm
    /// displays it) or null when the host does not answer NBNS.
    /// </summary>
    public static async Task<string?> QueryAsync(
        IPAddress host,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default,
        int port = 137)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.AddressFamily != AddressFamily.InterNetwork || timeoutMs <= 0)
        {
            return null;
        }

        var transactionId = (ushort)Random.Shared.Next(0, ushort.MaxValue + 1);
        var request = BuildNodeStatusQuery(transactionId);

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Connect(new IPEndPoint(host, port));
            await socket.SendAsync(request, SocketFlags.None, cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            var buffer = new byte[1024];
            var received = await socket.ReceiveAsync(buffer, SocketFlags.None, cts.Token);
            return ParseNodeStatusResponse(buffer.AsSpan(0, received), transactionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // No answer, ICMP unreachable, firewall drop: name stays unknown.
            return null;
        }
    }

    /// <summary>
    /// NBNS node-status query: header (12 bytes) + encoded "*" name (34 bytes)
    /// + QTYPE 0x0021 (NBSTAT) + QCLASS 0x0001 (IN).
    /// </summary>
    internal static byte[] BuildNodeStatusQuery(ushort transactionId)
    {
        var packet = new byte[50];
        WriteUInt16(packet, 0, transactionId);
        WriteUInt16(packet, 2, 0x0000); // query, no flags
        WriteUInt16(packet, 4, 1);      // QDCOUNT

        packet[12] = 0x20; // encoded name length (32)
        var raw = new byte[16]; // "*" followed by 15 zero bytes
        raw[0] = (byte)'*';
        EncodeName(raw).CopyTo(packet, 13);
        packet[45] = 0x00; // end of name
        WriteUInt16(packet, 46, 0x0021); // QTYPE NBSTAT
        WriteUInt16(packet, 48, 0x0001); // QCLASS IN
        return packet;
    }

    /// <summary>
    /// Picks the workstation name out of an NBSTAT response. Entries flagged
    /// as group names (workgroup / domain) are ignored.
    /// </summary>
    internal static string? ParseNodeStatusResponse(ReadOnlySpan<byte> response, ushort transactionId)
    {
        if (response.Length < 12
            || ReadUInt16(response, 0) != transactionId
            || (ReadUInt16(response, 2) & 0x8000) == 0   // not a response
            || (ReadUInt16(response, 2) & 0x000F) != 0)  // rcode != success
        {
            return null;
        }

        var questions = ReadUInt16(response, 4);
        var answers = ReadUInt16(response, 6);
        var offset = 12;

        for (var i = 0; i < questions; i++)
        {
            offset = SkipName(response, offset);
            if (offset < 0 || offset + 4 > response.Length)
            {
                return null;
            }

            offset += 4;
        }

        string? station = null;
        string? fileServer = null;
        for (var i = 0; i < answers; i++)
        {
            offset = SkipName(response, offset);
            if (offset < 0 || offset + 10 > response.Length)
            {
                return null;
            }

            var type = ReadUInt16(response, offset);
            var length = ReadUInt16(response, offset + 8);
            offset += 10;
            if (offset + length > response.Length)
            {
                return null;
            }

            if (type == 0x0021 && length > 0)
            {
                ReadNames(response.Slice(offset, length), ref station, ref fileServer);
            }

            offset += length;
        }

        return station ?? fileServer;
    }

    private static void ReadNames(ReadOnlySpan<byte> rdata, ref string? station, ref string? fileServer)
    {
        var count = rdata[0];
        for (var i = 0; i < count; i++)
        {
            var start = 1 + i * 18;
            if (start + 18 > rdata.Length)
            {
                return;
            }

            var suffix = rdata[start + 15];
            var flags = ReadUInt16(rdata, start + 16);
            if ((flags & 0x8000) != 0)
            {
                continue; // group name: workgroup / domain, not the host
            }

            var name = CleanName(rdata.Slice(start, 15));
            if (name.Length == 0)
            {
                continue;
            }

            if (suffix == 0x00)
            {
                station ??= name;
            }
            else if (suffix == 0x20)
            {
                fileServer ??= name;
            }
        }
    }

    private static string CleanName(ReadOnlySpan<byte> raw)
    {
        var length = raw.Length;
        while (length > 0 && (raw[length - 1] == 0 || raw[length - 1] == (byte)' '))
        {
            length--;
        }

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = char.ToLowerInvariant((char)raw[i]);
        }

        return new string(chars);
    }

    private static byte[] EncodeName(ReadOnlySpan<byte> raw16)
    {
        var encoded = new byte[raw16.Length * 2];
        for (var i = 0; i < raw16.Length; i++)
        {
            encoded[i * 2] = (byte)('A' + ((raw16[i] >> 4) & 0x0F));
            encoded[i * 2 + 1] = (byte)('A' + (raw16[i] & 0x0F));
        }

        return encoded;
    }

    /// <summary>Skips a (possibly compressed) domain name; -1 when truncated.</summary>
    private static int SkipName(ReadOnlySpan<byte> buffer, int offset)
    {
        if (offset < 0 || offset >= buffer.Length)
        {
            return -1;
        }

        if ((buffer[offset] & 0xC0) == 0xC0)
        {
            return offset + 2 <= buffer.Length ? offset + 2 : -1;
        }

        while (offset < buffer.Length && buffer[offset] != 0)
        {
            offset += buffer[offset] + 1;
        }

        return offset < buffer.Length ? offset + 1 : -1;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> buffer, int offset) =>
        (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }
}
