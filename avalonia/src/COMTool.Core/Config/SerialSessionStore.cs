namespace COMTool.Core.Config;

public static class SerialSessionRules
{
    public static string NormalizePort(string? port) =>
        (port ?? "").Trim().ToLowerInvariant();

    public static SerialSession Clean(SerialSession session)
    {
        var port = (session.Port ?? "").Trim();
        if (port.Length == 0 && session.Serial is not null)
        {
            port = session.Serial.Port.Trim();
        }

        return new SerialSession
        {
            Port = port,
            Name = string.IsNullOrWhiteSpace(session.Name) ? port : session.Name,
            Serial = new SerialConnectionConfig
            {
                Port = port,
                Baudrate = session.Serial?.Baudrate ?? 115200,
                Bytesize = session.Serial?.Bytesize ?? 8,
                Parity = session.Serial?.Parity ?? "None",
                Stopbits = session.Serial?.Stopbits ?? "1",
                Flowcontrol = session.Serial?.Flowcontrol ?? "None",
                Rts = session.Serial?.Rts ?? false,
                Dtr = session.Serial?.Dtr ?? false,
                Extra = session.Serial?.Extra,
            },
            Updated = session.Updated,
            Extra = session.Extra,
        };
    }

    /// <summary>
    /// Compact sidebar summary, e.g. "115200 8N1" or
    /// "460800 8N1 · RTS/CTS". Flow control is omitted when it is None.
    /// </summary>
    public static string ConnectionSummary(SerialConnectionConfig? serial)
    {
        var baudrate = serial is { Baudrate: > 0 } ? serial.Baudrate : 115200;
        var bytesize = serial is { Bytesize: >= 5 and <= 8 } ? serial.Bytesize : 8;
        var parity = (serial?.Parity ?? "None").Trim().ToLowerInvariant() switch
        {
            "" or "none" => "N",
            "even" => "E",
            "odd" => "O",
            "mark" => "M",
            "space" => "S",
            _ => "?",
        };
        var stopbits = (serial?.Stopbits ?? "").Trim();
        if (stopbits.Length == 0)
        {
            stopbits = "1";
        }

        var summary = $"{baudrate} {bytesize}{parity}{stopbits}";
        var flowcontrol = (serial?.Flowcontrol ?? "None").Trim();
        return flowcontrol.Length == 0
            || string.Equals(flowcontrol, "None", StringComparison.OrdinalIgnoreCase)
            ? summary
            : $"{summary} · {flowcontrol}";
    }
}

public sealed class SerialSessionStore
{
    public List<SerialSession> Sessions { get; private set; } = new();

    public void Load(ComtoolConfig config)
    {
        var byPort = new Dictionary<string, int>();
        var loaded = new List<SerialSession>();
        foreach (var raw in config.SerialSessions)
        {
            if (raw is null)
            {
                continue;
            }

            var cleaned = SerialSessionRules.Clean(raw);
            if (cleaned.Port.Length == 0)
            {
                continue;
            }

            var key = SerialSessionRules.NormalizePort(cleaned.Port);
            if (byPort.TryGetValue(key, out var index))
            {
                loaded[index] = cleaned;
            }
            else
            {
                byPort[key] = loaded.Count;
                loaded.Add(cleaned);
            }
        }

        Sessions = loaded;
    }

    public void Save(ComtoolConfig config) =>
        config.SerialSessions = Sessions.Select(Clone).ToList();

    public SerialSession? Get(string port)
    {
        var key = SerialSessionRules.NormalizePort(port);
        var session = Sessions.FirstOrDefault(item =>
            SerialSessionRules.NormalizePort(item.Port) == key);
        return session is null ? null : Clone(session);
    }

    public SerialSession? Upsert(string port, SerialConnectionConfig serial, string? name = null)
    {
        port = port.Trim();
        if (port.Length == 0)
        {
            return null;
        }

        var session = new SerialSession
        {
            Port = port,
            Name = string.IsNullOrWhiteSpace(name) ? port : name,
            Serial = new SerialConnectionConfig
            {
                Port = port,
                Baudrate = serial.Baudrate,
                Bytesize = serial.Bytesize,
                Parity = serial.Parity,
                Stopbits = serial.Stopbits,
                Flowcontrol = serial.Flowcontrol,
                Rts = serial.Rts,
                Dtr = serial.Dtr,
                Extra = serial.Extra,
            },
            Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        var key = SerialSessionRules.NormalizePort(port);
        for (var i = 0; i < Sessions.Count; i++)
        {
            if (SerialSessionRules.NormalizePort(Sessions[i].Port) == key)
            {
                Sessions[i] = session;
                return Clone(session);
            }
        }

        Sessions.Add(session);
        return Clone(session);
    }

    public string? Remove(string port)
    {
        var key = SerialSessionRules.NormalizePort(port);
        var index = Sessions.FindIndex(item =>
            SerialSessionRules.NormalizePort(item.Port) == key);
        if (index < 0)
        {
            return null;
        }

        var removed = Sessions[index].Port;
        Sessions.RemoveAt(index);
        return removed;
    }

    public IReadOnlyList<string> Prune(IEnumerable<string> availablePorts)
    {
        var available = availablePorts
            .Select(SerialSessionRules.NormalizePort)
            .ToHashSet();
        var removed = new List<string>();
        var kept = new List<SerialSession>();
        foreach (var session in Sessions)
        {
            if (available.Contains(SerialSessionRules.NormalizePort(session.Port)))
            {
                kept.Add(session);
            }
            else
            {
                removed.Add(session.Port);
            }
        }

        Sessions = kept;
        return removed;
    }

    public IReadOnlyList<SerialSession> SortedSessions() =>
        Sessions
            .OrderBy(item => item.Port, NaturalStringComparer.Instance)
            .ThenBy(item => item.Name, NaturalStringComparer.Instance)
            .Select(Clone)
            .ToList();

    private static SerialSession Clone(SerialSession session) => new()
    {
        Port = session.Port,
        Name = session.Name,
        Serial = new SerialConnectionConfig
        {
            Port = session.Serial.Port,
            Baudrate = session.Serial.Baudrate,
            Bytesize = session.Serial.Bytesize,
            Parity = session.Serial.Parity,
            Stopbits = session.Serial.Stopbits,
            Flowcontrol = session.Serial.Flowcontrol,
            Rts = session.Serial.Rts,
            Dtr = session.Serial.Dtr,
            Extra = session.Serial.Extra,
        },
        Updated = session.Updated,
        Extra = session.Extra,
    };
}
