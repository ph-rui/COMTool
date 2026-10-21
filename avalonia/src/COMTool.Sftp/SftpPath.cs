namespace COMTool.Sftp;

/// <summary>
/// Helpers for remote POSIX paths. The remote side always uses '/' and is
/// never mixed with local Windows paths.
/// </summary>
public static class SftpPath
{
    public const char Separator = '/';

    public const string Root = "/";

    /// <summary>
    /// Joins a base path and a child name with a single separator.
    /// </summary>
    public static string Combine(string basePath, string name)
    {
        var baseSegment = basePath ?? string.Empty;
        if (baseSegment.Length == 0)
        {
            baseSegment = Root;
        }

        if (!baseSegment.EndsWith(Separator))
        {
            baseSegment += Separator;
        }

        return baseSegment + (name ?? string.Empty);
    }

    /// <summary>
    /// Returns the parent directory. The root is its own parent.
    /// </summary>
    public static string GetParent(string path)
    {
        var normalized = Normalize(path);
        if (IsRoot(normalized))
        {
            return Root;
        }

        var index = normalized.LastIndexOf(Separator);
        return index <= 0 ? Root : normalized[..index];
    }

    /// <summary>
    /// Returns the last segment of a path, or an empty string for the root.
    /// </summary>
    public static string GetFileName(string path)
    {
        var normalized = Normalize(path);
        if (IsRoot(normalized))
        {
            return string.Empty;
        }

        var index = normalized.LastIndexOf(Separator);
        return index < 0 ? normalized : normalized[(index + 1)..];
    }

    public static bool IsRoot(string path)
    {
        var normalized = Normalize(path);
        return normalized.Length == 1 && normalized[0] == Separator;
    }

    /// <summary>
    /// Collapses repeated separators and resolves '.' and '..' segments.
    /// A '..' segment never escapes the root.
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Root;
        }

        var segments = new List<string>();
        foreach (var segment in path.Split(Separator))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0
            ? Root
            : Separator + string.Join(Separator, segments);
    }
}
