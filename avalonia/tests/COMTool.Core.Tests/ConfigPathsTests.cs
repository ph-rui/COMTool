using COMTool.Core.Config;

namespace COMTool.Core.Tests;

[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection
{
}

[Collection("Environment")]
public sealed class ConfigPathsTests
{
    [Fact]
    public void CP01_AvaloniaOverrideWins()
    {
        var expected = Path.Combine(Path.GetTempPath(), "comtool-test-av");
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", expected);
        Assert.Equal(Normalize(expected), Normalize(ConfigPaths.AvaloniaConfigDirectory()));
        Assert.NotEqual(Normalize(expected), Normalize(ConfigPaths.PythonConfigDirectory()));
    }

    [Fact]
    public void CP02_PythonOverrideWins()
    {
        var expected = Path.Combine(Path.GetTempPath(), "comtool-test-py");
        using var env = new EnvScope("COMTOOL_PYTHON_CONFIG_DIR", expected);
        Assert.Equal(Normalize(expected), Normalize(ConfigPaths.PythonConfigDirectory()));
    }

    [Fact]
    public void CP03_EmptyOverrideFallsBack()
    {
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", "   ");
        Assert.EndsWith(
            "comtool-avalonia",
            Normalize(ConfigPaths.AvaloniaConfigDirectory()),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CP04_RelativeOverrideIsAbsolute()
    {
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", "relative-config");
        Assert.Equal(
            Normalize(Path.GetFullPath("relative-config")),
            Normalize(ConfigPaths.AvaloniaConfigDirectory()));
    }

    [Fact]
    public void CP05_DefaultDirectoriesStaySeparate()
    {
        using var avaloniaEnv = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", null);
        using var pythonEnv = new EnvScope("COMTOOL_PYTHON_CONFIG_DIR", null);

        Assert.NotEqual(
            Normalize(ConfigPaths.AvaloniaConfigDirectory()),
            Normalize(ConfigPaths.PythonConfigDirectory()));
    }

    [Theory]
    [InlineData("comtool test config")]
    [InlineData("comtool 测试 配置")]
    public void CP06_OverrideWithSpacesAndUnicodeIsPreserved(string directoryName)
    {
        var expected = Path.Combine(Path.GetTempPath(), directoryName);
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", expected);
        Assert.Equal(Normalize(expected), Normalize(ConfigPaths.AvaloniaConfigDirectory()));
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private sealed class EnvScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previous);
        }
    }
}
