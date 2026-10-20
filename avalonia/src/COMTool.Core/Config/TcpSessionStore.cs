namespace COMTool.Core.Config;

public static class TcpSessionRules
{
    public const int DefaultPort = 2345;
    public const string ClientMode = "client";
    public const string ServerMode = "server";

    public static string NormalizeMode(string? mode) =>
        string.Equals((mode ?? "").Trim(), ServerMode, StringComparison.OrdinalIgnoreCase)
            ? ServerMode
            : ClientMode;

    public static bool IsValidPort(int port) => port is > 0 and <= 65535;

    public static string ClientTarget(string target)
    {
        if (!TcpTarget.TryParse(target, out var parsed) || parsed is null)
        {
            return "";
        }

        return $"{parsed.Host.ToLowerInvariant()}:{parsed.Port}";
    }

    public static string Key(string mode, string target, int port)
    {
        mode = NormalizeMode(mode);
        if (mode == ServerMode)
        {
            return IsValidPort(port) ? $"tcp:server:{port}" : "";
        }

        var normalized = ClientTarget(target);
        return normalized.Length == 0 ? "" : $"tcp:client:{normalized}";
    }

    public static string DisplayName(TcpSession session) =>
        NormalizeMode(session.Mode) == ServerMode
            ? $"TCP server :{session.Port}"
            : session.Target;

    public static string Detail(TcpSession session) =>
        NormalizeMode(session.Mode) == ServerMode
            ? $"Listening on 0.0.0.0:{session.Port}"
            : "TCP client";

    public static TcpSession? Clean(TcpSession session)
    {
        var mode = NormalizeMode(session.Mode);
        if (mode == ServerMode)
        {
            if (!IsValidPort(session.Port))
            {
                return null;
            }

            return new TcpSession
            {
                Key = Key(mode, "", session.Port),
                Mode = mode,
                Target = "",
                Port = session.Port,
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
            Key = Key(mode, normalized, target.Port),
            Mode = mode,
            Target = normalized,
            Port = target.Port,
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
        foreach (var raw in config.TcpSessions)
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

    public TcpSession? Upsert(string mode, string target, int port)
    {
        mode = TcpSessionRules.NormalizeMode(mode);
        if (mode == TcpSessionRules.ServerMode)
        {
            if (!TcpSessionRules.IsValidPort(port))
            {
                return null;
            }

            target = "";
        }
        else
        {
            if (!TcpTarget.TryParse(target, out var parsed) || parsed is null)
            {
                return null;
            }

            target = parsed.ToString();
            port = parsed.Port;
        }

        var key = TcpSessionRules.Key(mode, target, port);
        if (key.Length == 0)
        {
            return null;
        }

        var session = new TcpSession
        {
            Key = key,
            Mode = mode,
            Target = target,
            Port = port,
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
        Key = session.Key,
        Mode = session.Mode,
        Target = session.Target,
        Port = session.Port,
        Updated = session.Updated,
        Extra = session.Extra,
    };
}
