namespace COMTool.Core.Config;

public static class TelnetSessionRules
{
    public const int DefaultPort = 23;
    public const string Transport = "telnet";

    public static string NormalizeHost(string? host) =>
        (host ?? "").Trim().ToLowerInvariant();

    public static string NormalizeUser(string? user) =>
        (user ?? "").Trim();

    public static int NormalizePort(int port) =>
        port is <= 0 or > 65535 ? DefaultPort : port;

    public static string Key(string host, int port, string user) =>
        $"{Transport}:{NormalizeUser(user)}@{NormalizeHost(host)}:{NormalizePort(port)}";

    public static string DisplayName(TelnetSession session)
    {
        var alias = (session.Alias ?? "").Trim();
        return alias.Length > 0 ? alias : (session.Host ?? "").Trim();
    }

    public static string Detail(TelnetSession session)
    {
        var host = (session.Host ?? "").Trim();
        var port = NormalizePort(session.Port);
        var user = NormalizeUser(session.User);
        return user.Length == 0 ? $"{host}:{port}" : $"{user}@{host}:{port}";
    }

    public static TelnetSession Clean(TelnetSession session)
    {
        var host = (session.Host ?? "").Trim();
        var port = NormalizePort(session.Port);
        return new TelnetSession
        {
            Transport = Transport,
            Key = Key(host, port, session.User),
            Host = host,
            Port = port,
            User = NormalizeUser(session.User),
            Alias = (session.Alias ?? "").Trim(),
            EncryptedPassword = (session.EncryptedPassword ?? "").Trim(),
            Updated = session.Updated,
            Extra = session.Extra,
        };
    }

    /// <summary>
    /// Creates a config suitable for sharing. Credentials and arbitrary extra
    /// fields are deliberately excluded so a shared session can never carry a
    /// DPAPI token or an unknown secret.
    /// </summary>
    public static TelnetSession ForShare(TelnetSession session)
    {
        var host = (session.Host ?? "").Trim();
        var port = NormalizePort(session.Port);
        return new TelnetSession
        {
            Transport = Transport,
            Key = Key(host, port, session.User),
            Host = host,
            Port = port,
            User = NormalizeUser(session.User),
            Alias = (session.Alias ?? "").Trim(),
            EncryptedPassword = "",
            Updated = session.Updated,
        };
    }
}

public sealed class TelnetSessionStore
{
    public List<TelnetSession> Sessions { get; private set; } = new();

    public void Load(ComtoolConfig config)
    {
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var loaded = new List<TelnetSession>();
        foreach (var raw in config.TelnetSessions ?? new List<TelnetSession>())
        {
            if (raw is null)
            {
                continue;
            }

            var cleaned = TelnetSessionRules.Clean(raw);
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
        config.TelnetSessions = Sessions.Select(Clone).ToList();

    public TelnetSession? Get(string key) =>
        Sessions.FirstOrDefault(item => item.Key == key) is { } found ? Clone(found) : null;

    public TelnetSession? Upsert(
        string host,
        int port,
        string user,
        string alias = "",
        string? encryptedPassword = null)
    {
        host = (host ?? "").Trim();
        if (host.Length == 0)
        {
            return null;
        }

        port = TelnetSessionRules.NormalizePort(port);
        user = TelnetSessionRules.NormalizeUser(user);
        var key = TelnetSessionRules.Key(host, port, user);
        var existingIndex = Sessions.FindIndex(item => item.Key == key);
        var existingPassword = existingIndex >= 0 ? Sessions[existingIndex].EncryptedPassword : "";
        var password = encryptedPassword is null ? existingPassword : encryptedPassword.Trim();

        var session = new TelnetSession
        {
            Transport = TelnetSessionRules.Transport,
            Key = key,
            Host = host,
            Port = port,
            User = user,
            Alias = (alias ?? "").Trim(),
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

    public IReadOnlyList<TelnetSession> SortedSessions() =>
        Sessions
            .OrderBy(
                session => TelnetSessionRules.DisplayName(session),
                NaturalStringComparer.Instance)
            .ThenBy(session => session.Key, StringComparer.Ordinal)
            .Select(Clone)
            .ToList();

    private static TelnetSession Clone(TelnetSession session) => new()
    {
        Transport = TelnetSessionRules.Transport,
        Key = session.Key,
        Host = session.Host,
        Port = session.Port,
        User = session.User,
        Alias = session.Alias,
        EncryptedPassword = session.EncryptedPassword,
        Updated = session.Updated,
        Extra = session.Extra,
    };
}
