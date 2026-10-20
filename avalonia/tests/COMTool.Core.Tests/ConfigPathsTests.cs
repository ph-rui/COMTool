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
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", "/tmp/comtool-test-av");
        Assert.Equal("/tmp/comtool-test-av", Normalize(ConfigPaths.AvaloniaConfigDirectory()));
        Assert.NotEqual("/tmp/comtool-test-av", Normalize(ConfigPaths.PythonConfigDirectory()));
    }

    [Fact]
    public void CP02_PythonOverrideWins()
    {
        using var env = new EnvScope("COMTOOL_PYTHON_CONFIG_DIR", "/tmp/comtool-test-py");
        Assert.Equal("/tmp/comtool-test-py", Normalize(ConfigPaths.PythonConfigDirectory()));
    }

    [Fact]
    public void CP03_EmptyOverrideFallsBack()
    {
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", "   ");
        Assert.EndsWith("comtool-avalonia", Normalize(ConfigPaths.AvaloniaConfigDirectory()));
    }

    [Fact]
    public void CP04_RelativeOverrideIsAbsolute()
    {
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", "relative-config");
        Assert.True(Path.IsPathRooted(ConfigPaths.AvaloniaConfigDirectory()));
    }

    [Fact]
    public void CP05_DefaultDirectoriesStaySeparate()
    {
        Assert.NotEqual(
            Normalize(ConfigPaths.AvaloniaConfigDirectory()),
            Normalize(ConfigPaths.PythonConfigDirectory()));
    }

    [Fact]
    public void CP06_OverrideWithSpacesIsPreserved()
    {
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", "/tmp/comtool test config");
        Assert.Equal("/tmp/comtool test config", Normalize(ConfigPaths.AvaloniaConfigDirectory()));
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
