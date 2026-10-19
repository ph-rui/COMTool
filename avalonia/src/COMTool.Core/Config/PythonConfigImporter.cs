using System.Text.Json;

namespace COMTool.Core.Config;

public enum ConfigImportStatus
{
    Imported,
    TargetExists,
    SourceMissing,
    SourceInvalid,
    UnsupportedVersion,
    SamePath,
}

public sealed record ConfigImportResult(
    ConfigImportStatus Status,
    string Source,
    string Target,
    string? BackupPath = null,
    string? Error = null)
{
    public bool Imported => Status == ConfigImportStatus.Imported;
}

/// <summary>
/// One-way import of the stable Python config into the Avalonia Beta directory.
/// The source is never modified. An existing Beta config is never replaced
/// unless the caller explicitly asks for an overwrite (which first creates a
/// timestamped backup).
/// </summary>
public static class PythonConfigImporter
{
    public static ConfigImportResult ImportIfMissing(
        string? source = null,
        string? target = null,
        bool overwrite = false)
    {
        source ??= ConfigPaths.PythonConfigFile();
        target ??= ConfigPaths.AvaloniaConfigFile();

        try
        {
            var sourcePath = Path.GetFullPath(source);
            var targetPath = Path.GetFullPath(target);
            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return new ConfigImportResult(
                    ConfigImportStatus.SamePath,
                    sourcePath,
                    targetPath,
                    Error: "source and target are the same file");
            }

            if (!File.Exists(sourcePath))
            {
                return new ConfigImportResult(
                    ConfigImportStatus.SourceMissing,
                    sourcePath,
                    targetPath);
            }

            var bytes = File.ReadAllBytes(sourcePath);
            using (var document = JsonDocument.Parse(bytes))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return new ConfigImportResult(
                        ConfigImportStatus.SourceInvalid,
                        sourcePath,
                        targetPath,
                        Error: "config root must be a JSON object");
                }

                if (document.RootElement.TryGetProperty("version", out var version)
                    && version.ValueKind == JsonValueKind.Number
                    && version.TryGetInt32(out var value)
                    && value != 3)
                {
                    return new ConfigImportResult(
                        ConfigImportStatus.UnsupportedVersion,
                        sourcePath,
                        targetPath,
                        Error: $"unsupported config version {value}");
                }
            }

            if (File.Exists(targetPath) && !overwrite)
            {
                return new ConfigImportResult(
                    ConfigImportStatus.TargetExists,
                    sourcePath,
                    targetPath);
            }

            string? backup = null;
            if (File.Exists(targetPath))
            {
                backup = $"{targetPath}.bak.{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json";
                File.Copy(targetPath, backup, overwrite: false);
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Copy the original bytes so unknown Python fields survive the
            // import untouched. The Beta then owns this separate file.
            File.WriteAllBytes(targetPath, bytes);
            return new ConfigImportResult(
                ConfigImportStatus.Imported,
                sourcePath,
                targetPath,
                backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ConfigImportResult(
                ConfigImportStatus.SourceInvalid,
                source,
                target,
                Error: ex.Message);
        }
    }
}
