namespace COMTool.Core.Config;

public static class TcpSessionRules
{
    public const int DefaultPort = 2345;
    public const string ClientMode = "client";
    public const string ServerMode = "server";
    public const string TcpProtocol = "tcp";
    public const string UdpProtocol = "udp";

    public static string NormalizeMode(string? mode) =>
        string.Equals((mode ?? "").Trim(), ServerMode, StringComparison.OrdinalIgnoreCase)
            ? ServerMode
            : ClientMode;

    public static string NormalizeProtocol(string? protocol) =>
        string.Equals((protocol ?? "").Trim(), UdpProtocol, StringComparison.OrdinalIgnoreCase)
            ? UdpProtocol
            : TcpProtocol;

    public static bool IsValidPort(int port) => port is > 0 and <= 65535;

    public static double NormalizeReconnectInterval(double interval) =>
        double.IsFinite(interval) && interval > 0
            ? Math.Clamp(interval, 0.1, 3600)
            : 1.0;

    public static string ClientTarget(string target)
    {
        if (!TcpTarget.TryParse(target, out var parsed) || parsed is null)
        {
            return "";
        }

        return $"{parsed.Host.ToLowerInvariant()}:{parsed.Port}";
    }

    public static string Key(string mode, string target, int port) =>
        Key(TcpProtocol, mode, target, port);

    public static string Key(string protocol, string mode, string target, int port)
    {
        protocol = NormalizeProtocol(protocol);
        mode = NormalizeMode(mode);
        if (!IsValidPort(port))
        {
            return "";
        }

        if (protocol == UdpProtocol)
        {
            var normalizedTarget = ClientTarget(target);
            return normalizedTarget.Length == 0
                ? ""
                : $"udp:{mode}:{port}:{normalizedTarget}";
        }

        if (mode == ServerMode)
        {
            return $"tcp:server:{port}";
        }

        var normalized = ClientTarget(target);
        return normalized.Length == 0 ? "" : $"tcp:client:{normalized}";
    }

    public static string DisplayName(TcpSession session)
    {
        var protocol = NormalizeProtocol(session.Protocol);
        if (protocol == UdpProtocol)
        {
            return NormalizeMode(session.Mode) == ServerMode
                ? $"UDP server :{session.Port}"
                : $"UDP {session.Target}";
        }

        return NormalizeMode(session.Mode) == ServerMode
            ? $"TCP server :{session.Port}"
            : session.Target;
    }

    public static string Detail(TcpSession session)
    {
        var protocol = NormalizeProtocol(session.Protocol);
        if (protocol == UdpProtocol)
        {
            return $"Local :{session.Port} -> {session.Target}";
        }

        return NormalizeMode(session.Mode) == ServerMode
            ? $"Listening on 0.0.0.0:{session.Port}"
            : "TCP client";
    }

    public static TcpSession? Clean(TcpSession session)
    {
        var protocol = NormalizeProtocol(session.Protocol);
        var mode = NormalizeMode(session.Mode);

        if (protocol == UdpProtocol)
        {
            if (!IsValidPort(session.Port)
                || !TcpTarget.TryParse(session.Target, out var udpTarget)
                || udpTarget is null)
            {
                return null;
            }

            var normalizedUdpTarget = udpTarget.ToString();
            return new TcpSession
            {
                Protocol = protocol,
                Key = Key(protocol, mode, normalizedUdpTarget, session.Port),
                Mode = mode,
                Target = normalizedUdpTarget,
                Port = session.Port,
                AutoReconnect = protocol == TcpProtocol
                    && mode == ClientMode
                    && session.AutoReconnect,
                ReconnectInterval = NormalizeReconnectInterval(session.ReconnectInterval),
                Updated = session.Updated,
                Extra = session.Extra,
            };
        }

        if (mode == ServerMode)
        {
            if (!IsValidPort(session.Port))
            {
                return null;
            }

            return new TcpSession
            {
                Protocol = protocol,
                Key = Key(protocol, mode, "", session.Port),
                Mode = mode,
                Target = "",
                Port = session.Port,
                AutoReconnect = protocol == TcpProtocol
                    && mode == ClientMode
                    && session.AutoReconnect,
                ReconnectInterval = NormalizeReconnectInterval(session.ReconnectInterval),
                Updated = session.Updated,
                Extra = session.Extra,
            };
        }

        if (!TcpTarget.TryParse(session.Target, out var target) || target is null)
        {
            return null;
        }

        var normalized = target.ToString();
        return new TcpSession
        {
            Protocol = protocol,
            Key = Key(protocol, mode, normalized, target.Port),
            Mode = mode,
            Target = normalized,
            Port = target.Port,
            AutoReconnect = protocol == TcpProtocol
                && mode == ClientMode
                && session.AutoReconnect,
            ReconnectInterval = NormalizeReconnectInterval(session.ReconnectInterval),
            Updated = session.Updated,
            Extra = session.Extra,
        };
    }
}

public sealed class TcpSessionStore
{
    public List<TcpSession> Sessions { get; private set; } = new();

    public void Load(ComtoolConfig config)
    {
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var loaded = new List<TcpSession>();
        foreach (var raw in config.TcpSessions ?? new List<TcpSession>())
        {
            if (raw is null)
            {
                continue;
            }

            var cleaned = TcpSessionRules.Clean(raw);
            if (cleaned is null || cleaned.Key.Length == 0)
            {
                continue;
            }

            if (byKey.TryGetValue(cleaned.Key, out var index))
            {
                loaded[index] = cleaned;
            }
            else
            {
                byKey[cleaned.Key] = loaded.Count;
                loaded.Add(cleaned);
            }
        }

        Sessions = loaded;
    }

    public void Save(ComtoolConfig config) =>
        config.TcpSessions = Sessions.Select(Clone).ToList();

    public TcpSession? Get(string key) =>
        Sessions.FirstOrDefault(item => item.Key == key) is { } found ? Clone(found) : null;

    public TcpSession? Upsert(string mode, string target, int port) =>
        Upsert(TcpSessionRules.TcpProtocol, mode, target, port);

    public TcpSession? Upsert(
        string protocol,
        string mode,
        string target,
        int port,
        bool autoReconnect = false,
        double reconnectInterval = 1.0)
    {
        protocol = TcpSessionRules.NormalizeProtocol(protocol);
        mode = TcpSessionRules.NormalizeMode(mode);

        string normalizedTarget;
        int normalizedPort;
        if (protocol == TcpSessionRules.UdpProtocol)
        {
            if (!TcpSessionRules.IsValidPort(port)
                || !TcpTarget.TryParse(target, out var parsed)
                || parsed is null)
            {
                return null;
            }

            normalizedTarget = parsed.ToString();
            normalizedPort = port;
        }
        else if (mode == TcpSessionRules.ServerMode)
        {
            if (!TcpSessionRules.IsValidPort(port))
            {
                return null;
            }

            normalizedTarget = "";
            normalizedPort = port;
        }
        else
        {
            if (!TcpTarget.TryParse(target, out var parsed) || parsed is null)
            {
                return null;
            }

            normalizedTarget = parsed.ToString();
            normalizedPort = parsed.Port;
        }

        var key = TcpSessionRules.Key(protocol, mode, normalizedTarget, normalizedPort);
        if (key.Length == 0)
        {
            return null;
        }

        var session = new TcpSession
        {
            Protocol = protocol,
            Key = key,
            Mode = mode,
            Target = normalizedTarget,
            Port = normalizedPort,
            AutoReconnect = protocol == TcpSessionRules.TcpProtocol
                && mode == TcpSessionRules.ClientMode
                && autoReconnect,
            ReconnectInterval = TcpSessionRules.NormalizeReconnectInterval(reconnectInterval),
            Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        var existingIndex = Sessions.FindIndex(item => item.Key == key);
        if (existingIndex >= 0)
        {
            Sessions[existingIndex] = session;
        }
        else
        {
            Sessions.Add(session);
        }

        return Clone(session);
    }

    public string? Remove(string key)
    {
        var index = Sessions.FindIndex(item => item.Key == key);
        if (index < 0)
        {
            return null;
        }

        var removed = Sessions[index].Key;
        Sessions.RemoveAt(index);
        return removed;
    }

    public IReadOnlyList<TcpSession> SortedSessions() =>
        Sessions
            .OrderBy(
                session => TcpSessionRules.DisplayName(session),
                NaturalStringComparer.Instance)
            .ThenBy(session => session.Key, StringComparer.Ordinal)
            .Select(Clone)
            .ToList();

    private static TcpSession Clone(TcpSession session) => new()
    {
        Protocol = session.Protocol,
        Key = session.Key,
        Mode = session.Mode,
        Target = session.Target,
        Port = session.Port,
        AutoReconnect = session.AutoReconnect,
        ReconnectInterval = session.ReconnectInterval,
        Updated = session.Updated,
        Extra = session.Extra,
    };
}
