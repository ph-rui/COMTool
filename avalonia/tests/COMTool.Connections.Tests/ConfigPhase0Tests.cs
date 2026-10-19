using System.Text.Json;
using COMTool.Core.Config;

namespace COMTool.Connections.Tests;

public sealed class ConfigPhase0Tests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "comtool-phase0-tests",
        Guid.NewGuid().ToString("N"));

    public ConfigPhase0Tests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void ImportCopiesPythonConfigWithoutChangingSource()
    {
        var source = Path.Combine(_directory, "python", "config.json");
        var target = Path.Combine(_directory, "avalonia", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, """
        {
          "version": 3,
          "skin": "dark",
          "futureField": { "enabled": true },
          "serialSessions": [
            { "port": "COM2", "name": "COM2", "futureSessionField": 7 }
          ]
        }
        """);
        var sourceBytes = File.ReadAllBytes(source);

        var result = PythonConfigImporter.ImportIfMissing(source, target);

        Assert.True(result.Imported);
        Assert.Equal(sourceBytes, File.ReadAllBytes(source));
        Assert.Equal(sourceBytes, File.ReadAllBytes(target));
    }

    [Fact]
    public void ImportDoesNotOverwriteExistingBetaConfig()
    {
        var source = Path.Combine(_directory, "python.json");
        var target = Path.Combine(_directory, "avalonia.json");
        File.WriteAllText(source, """{"version":3,"skin":"dark"}""");
        File.WriteAllText(target, """{"version":3,"skin":"light"}""");

        var result = PythonConfigImporter.ImportIfMissing(source, target);

        Assert.Equal(ConfigImportStatus.TargetExists, result.Status);
        Assert.Contains("light", File.ReadAllText(target));
    }

    [Fact]
    public void ImportOverwriteCreatesTimestampedBackup()
    {
        var source = Path.Combine(_directory, "python.json");
        var target = Path.Combine(_directory, "avalonia.json");
        File.WriteAllText(source, """{"version":3,"skin":"dark"}""");
        File.WriteAllText(target, """{"version":3,"skin":"light"}""");

        var result = PythonConfigImporter.ImportIfMissing(source, target, overwrite: true);

        Assert.True(result.Imported);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Contains("dark", File.ReadAllText(target));
        Assert.Contains("light", File.ReadAllText(result.BackupPath!));
    }

    [Fact]
    public void MissingOrInvalidSourceLeavesTargetUntouched()
    {
        var target = Path.Combine(_directory, "avalonia.json");
        File.WriteAllText(target, """{"version":3,"skin":"light"}""");

        var missing = PythonConfigImporter.ImportIfMissing(
            Path.Combine(_directory, "missing.json"),
            target);
        Assert.Equal(ConfigImportStatus.SourceMissing, missing.Status);

        var invalidSource = Path.Combine(_directory, "invalid.json");
        File.WriteAllText(invalidSource, "{not json");
        var invalid = PythonConfigImporter.ImportIfMissing(invalidSource, target);
        Assert.Equal(ConfigImportStatus.SourceInvalid, invalid.Status);
        Assert.Contains("light", File.ReadAllText(target));
    }

    [Fact]
    public void UnsupportedVersionIsNotImported()
    {
        var source = Path.Combine(_directory, "python.json");
        var target = Path.Combine(_directory, "avalonia.json");
        File.WriteAllText(source, """{"version":2,"skin":"dark"}""");

        var result = PythonConfigImporter.ImportIfMissing(source, target);

        Assert.Equal(ConfigImportStatus.UnsupportedVersion, result.Status);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void SameSourceAndTargetIsRejected()
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, """{"version":3}""");

        var result = PythonConfigImporter.ImportIfMissing(path, path, overwrite: true);

        Assert.Equal(ConfigImportStatus.SamePath, result.Status);
    }

    [Fact]
    public void DpapiIsUnsupportedOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal("", SecretProtector.Protect("secret"));
        Assert.Equal("", SecretProtector.Unprotect("not-a-token"));
    }

    [Fact]
    public void DpapiRejectsInvalidTokenOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal("", SecretProtector.Unprotect("not a base64 token"));
    }

    [Fact]
    public void SshPasswordCacheKeepsOnlyInMemoryAndCanBeCleared()
    {
        var cache = new SshPasswordCache();
        cache.Set("user@host:22", "secret");

        Assert.True(cache.TryGet("user@host:22", out var password));
        Assert.Equal("secret", password);

        cache.Set("user@host:22", "");
        Assert.False(cache.TryGet("user@host:22", out _));
    }
}
