namespace COMTool.Sftp;

/// <summary>
/// The kind of a remote file, used to decide how the built-in viewer
/// (or the system) opens it.
/// </summary>
public enum RemoteFileKind
{
    Image,
    Text,
    Other,
}

/// <summary>
/// Classifies a file by its extension, so the SFTP browser can open it
/// with the appropriate viewer (built-in image, built-in text, or the
/// system default program). Mirrors the behaviour of MobaXterm's
/// "Open" action on a remote file.
/// </summary>
public static class RemoteFileClassifier
{
    private static readonly string[] ImageExtensions =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".svg", ".ico",
    };

    private static readonly string[] TextExtensions =
    {
        ".txt", ".log", ".md", ".c", ".h", ".cpp", ".hpp", ".cs", ".py",
        ".sh", ".bash", ".zsh", ".json", ".xml", ".yaml", ".yml", ".ini",
        ".conf", ".cfg", ".properties", ".csv", ".tsv", ".html", ".css",
        ".js", ".ts", ".java", ".go", ".rs", ".rb", ".pl", ".php", ".sql",
        ".toml", ".tf",
    };

    // Whole names that carry no extension but are text.
    private static readonly string[] TextNames =
    {
        "makefile", "gitignore", ".gitignore", ".env", "cmakelists.txt", "license", "readme",
    };

    /// <summary>
    /// Returns the kind of the file. A file without a recognised
    /// extension is <see cref="RemoteFileKind.Other"/>.
    /// </summary>
    public static RemoteFileKind Classify(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        var extension = System.IO.Path.GetExtension(lower);
        // A dotfile such as ".gitignore": the whole name acts as the
        // extension, so treat it as extensionless and match the name.
        if (extension == lower)
        {
            extension = string.Empty;
        }

        if (extension.Length == 0)
        {
            // A name like "Makefile" or ".gitignore" — check the whole name
            // against the text list as a last resort.
            if (TextNames.Contains(lower))
            {
                return RemoteFileKind.Text;
            }

            return RemoteFileKind.Other;
        }

        if (ImageExtensions.Contains(extension))
        {
            return RemoteFileKind.Image;
        }

        if (TextExtensions.Contains(extension))
        {
            return RemoteFileKind.Text;
        }

        return RemoteFileKind.Other;
    }
}
