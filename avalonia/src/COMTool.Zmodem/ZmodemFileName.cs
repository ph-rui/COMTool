using System.Text;

namespace COMTool.Zmodem;

public sealed record ZmodemFileMeta(
    string Name,
    long? Size = null,
    long? ModifiedTime = null,
    long? Mode = null,
    long? Serial = null);

public static class ZmodemFileName
{
    private static readonly HashSet<string> WindowsReserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly HashSet<char> InvalidChars = new() { '<', '>', ':', '"', '|', '?', '*' };

    public static string DecodeName(ReadOnlySpan<byte> raw) =>
        Encoding.UTF8.GetString(raw);

    public static string SanitizeBaseName(string name)
    {
        name = name.Normalize(NormalizationForm.FormC);
        name = name.Replace('\\', '/').Split('/')[^1];

        var cleaned = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            var code = (int)character;
            if (code is >= 0xD800 and <= 0xDFFF)
            {
                cleaned.Append('_');
            }
            else if (code < 0x20 || code == 0x7F)
            {
                continue;
            }
            else if (InvalidChars.Contains(character) || character is '/' or '\\')
            {
                cleaned.Append('_');
            }
            else
            {
                cleaned.Append(character);
            }
        }

        name = cleaned.ToString().Trim(' ', '.');
        if (name.Length == 0 || name is "." or "..")
        {
            return "download";
        }

        var root = Path.GetFileNameWithoutExtension(name);
        if (WindowsReserved.Contains(root))
        {
            name += "_";
        }

        if (name.Length > 200)
        {
            var extension = Path.GetExtension(name);
            var keep = Math.Max(1, 200 - extension.Length);
            name = name[..keep] + extension;
        }

        return name.Length == 0 ? "download" : name;
    }

    public static string UniquePath(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var baseName = SanitizeBaseName(name);
        var candidate = Path.Combine(directory, baseName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var root = Path.GetFileNameWithoutExtension(baseName);
        var extension = Path.GetExtension(baseName);
        for (var index = 1; ; index++)
        {
            candidate = Path.Combine(directory, $"{root} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    public static ZmodemFileMeta ParseMetadata(byte[] payload)
    {
        var separator = Array.IndexOf(payload, (byte)0);
        var nameRaw = separator >= 0 ? payload.AsSpan(0, separator) : payload;
        var restRaw = separator >= 0 ? payload.AsSpan(separator + 1) : ReadOnlySpan<byte>.Empty;
        var name = SanitizeBaseName(DecodeName(nameRaw));
        var parts = Encoding.ASCII.GetString(restRaw)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return new ZmodemFileMeta(
            name,
            ParseInt(parts.ElementAtOrDefault(0), 10),
            ParseInt(parts.ElementAtOrDefault(1), 8),
            ParseInt(parts.ElementAtOrDefault(2), 8),
            ParseInt(parts.ElementAtOrDefault(3), 10));
    }

    private static long? ParseInt(string? value, int numberBase)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var parsed = Convert.ToInt64(value, numberBase);
            return parsed >= 0 ? parsed : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
