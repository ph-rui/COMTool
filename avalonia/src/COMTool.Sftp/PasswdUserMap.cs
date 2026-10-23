using System.Globalization;

namespace COMTool.Sftp;

/// <summary>
/// Reads the user names of a Unix <c>/etc/passwd</c> file. The SFTP
/// listing only carries the numeric uid of an entry; the browser resolves
/// it once, and keeps the number when the file cannot be read.
/// </summary>
public static class PasswdUserMap
{
    /// <summary>
    /// Parses the lines of a passwd file into a map from uid to user name.
    /// Malformed lines are skipped, a repeated uid keeps its first name.
    /// </summary>
    public static IReadOnlyDictionary<long, string> Parse(string? content)
    {
        var names = new Dictionary<long, string>();
        if (string.IsNullOrEmpty(content))
        {
            return names;
        }

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split(':');
            if (fields.Length < 3 || fields[0].Length == 0)
            {
                continue;
            }

            if (long.TryParse(
                    fields[2],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var uid))
            {
                names.TryAdd(uid, fields[0]);
            }
        }

        return names;
    }
}
