using System.Text;

namespace COMTool.Sftp;

/// <summary>
/// Computes the local cache location of a remote file when it is opened
/// through the built-in viewers or the system default program (r60).
/// The part of the remote path that is relative to the remote home
/// directory is kept, so repeated opens of the same file reuse one
/// location. Paths outside the home fall back to the bare file name.
/// </summary>
public static class SftpPathCache
{
    /// <summary>
    /// Maps a remote path into the local cache root.
    /// </summary>
    /// <param name="cacheRoot">The local directory the cache lives in.</param>
    /// <param name="remoteHome">The remote home directory of the session.</param>
    /// <param name="remotePath">The full path of the remote file.</param>
    public static string BuildCachePath(string cacheRoot, string remoteHome, string remotePath)
    {
        var normalized = SftpPath.Normalize(remotePath);
        var trimmedHome = SftpPath.Normalize(remoteHome).TrimEnd('/');
        var relative = string.Empty;

        if (trimmedHome.Length > 0 && normalized.StartsWith(trimmedHome + "/", StringComparison.Ordinal))
        {
            relative = normalized[(trimmedHome.Length + 1)..];
        }
        else if (trimmedHome.Length == 0 && normalized.Length > 1)
        {
            // The home is the root: keep everything below it.
            relative = normalized[1..];
        }

        var path = cacheRoot;
        if (relative.Length > 0)
        {
            foreach (var segment in relative.Split('/'))
            {
                var safe = SanitizeSegment(segment);
                if (safe.Length > 0)
                {
                    path = System.IO.Path.Combine(path, safe);
                }
            }
        }

        // Outside the home (or nothing usable): keep the file name alone.
        if (path == cacheRoot)
        {
            var name = SanitizeSegment(SftpPath.GetFileName(normalized));
            if (name.Length > 0)
            {
                path = System.IO.Path.Combine(path, name);
            }
        }

        return path;
    }

    /// <summary>
    /// Replaces the characters that are invalid in local file names with
    /// '_', so a remote name can never escape the cache directory.
    /// </summary>
    public static string SanitizeSegment(string segment)
    {
        var result = new StringBuilder(segment.Length);
        foreach (var character in segment)
        {
            result.Append(character switch
            {
                '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' => '_',
                _ => character,
            });
        }

        return result.ToString();
    }
}
