namespace COMTool.Core.Config;

public static class SshSessionRules
{
    public const int DefaultPort = 22;

    public static string NormalizeHost(string? host) =>
        (host ?? "").Trim().ToLowerInvariant();

    public static string NormalizeUser(string? user) =>
        (user ?? "").Trim();

    public static int NormalizePort(int port) =>
        port is <= 0 or > 65535 ? DefaultPort : port;

    public static string Key(string host, int port, string user) =>
        $"{NormalizeUser(user)}@{NormalizeHost(host)}:{NormalizePort(port)}";

    public static string DisplayName(SshSession session)
    {
        var alias = session.Alias.Trim();
        return alias.Length > 0 ? alias : session.Host.Trim();
    }

    public static string Detail(SshSession session) =>
        $"{session.User}@{session.Host}:{session.Port}";

    public static SshSession Clean(SshSession session)
    {
        var host = (session.Host ?? "").Trim();
        var port = NormalizePort(session.Port);
        return new SshSession
        {
            Key = Key(host, port, session.User),
            Host = host,
            Port = port,
            User = NormalizeUser(session.User),
            Alias = (session.Alias ?? "").Trim(),
            SshKey = (session.SshKey ?? "").Trim(),
            EncryptedPassword = (session.EncryptedPassword ?? "").Trim(),
            Updated = session.Updated,
            Extra = session.Extra,
        };
    }
}

public sealed class SshSessionStore
{
    public List<SshSession> Sessions { get; private set; } = new();

    public void Load(ComtoolConfig config)
    {
        var byKey = new Dictionary<string, int>();
        var loaded = new List<SshSession>();
        foreach (var raw in config.SshSessions)
        {
            if (raw is null)
            {
                continue;
            }

            var cleaned = SshSessionRules.Clean(raw);
            if (cleaned.Host.Length == 0)
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
        config.SshSessions = Sessions.Select(Clone).ToList();

    public SshSession? Get(string key) =>
        Sessions.FirstOrDefault(item => item.Key == key) is { } found ? Clone(found) : null;

    public SshSession? Upsert(
        string host,
        int port,
        string user,
        string alias = "",
        string sshKey = "",
        string? encryptedPassword = null)
    {
        host = host.Trim();
        if (host.Length == 0)
        {
            return null;
        }

        port = SshSessionRules.NormalizePort(port);
        user = SshSessionRules.NormalizeUser(user);
        var key = SshSessionRules.Key(host, port, user);
        var existingIndex = Sessions.FindIndex(item => item.Key == key);
        var existingPassword = existingIndex >= 0 ? Sessions[existingIndex].EncryptedPassword : "";
        var password = encryptedPassword is null ? existingPassword : encryptedPassword.Trim();

        var session = new SshSession
        {
            Key = key,
            Host = host,
            Port = port,
            User = user,
            Alias = (alias ?? "").Trim(),
            SshKey = (sshKey ?? "").Trim(),
            EncryptedPassword = password,
            Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

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

    public IReadOnlyList<SshSession> SortedSessions() =>
        Sessions
            .OrderBy(item => SshSessionRules.DisplayName(item).ToLowerInvariant())
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(Clone)
            .ToList();

    private static SshSession Clone(SshSession session) => new()
    {
        Key = session.Key,
        Host = session.Host,
        Port = session.Port,
        User = session.User,
        Alias = session.Alias,
        SshKey = session.SshKey,
        EncryptedPassword = session.EncryptedPassword,
        Updated = session.Updated,
        Extra = session.Extra,
    };
}
