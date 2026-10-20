using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class TestEnvironment : IDisposable
{
    private const string AvaloniaConfigVariable = "COMTOOL_AVALONIA_CONFIG_DIR";
    private const string PythonConfigVariable = "COMTOOL_PYTHON_CONFIG_DIR";
    private readonly string? _previousAvaloniaConfigDir;
    private readonly string? _previousPythonConfigDir;
    private bool _disposed;
    private bool _keepForFailure;

    public TestEnvironment()
    {
        Root = Path.Combine(TestAppBuilder.Root, "tests", Guid.NewGuid().ToString("N"));
        AvaloniaConfigDir = Path.Combine(Root, "avalonia");
        PythonConfigDir = Path.Combine(Root, "python");
        Directory.CreateDirectory(AvaloniaConfigDir);
        Directory.CreateDirectory(PythonConfigDir);

        _previousAvaloniaConfigDir = Environment.GetEnvironmentVariable(AvaloniaConfigVariable);
        _previousPythonConfigDir = Environment.GetEnvironmentVariable(PythonConfigVariable);
        Environment.SetEnvironmentVariable(AvaloniaConfigVariable, AvaloniaConfigDir);
        Environment.SetEnvironmentVariable(PythonConfigVariable, PythonConfigDir);
    }

    public string Root { get; }

    public string AvaloniaConfigDir { get; }

    public string PythonConfigDir { get; }

    public void SeedConfig(ComtoolConfig config)
    {
        Directory.CreateDirectory(AvaloniaConfigDir);
        config.Version = 3;
        config.Save(Path.Combine(AvaloniaConfigDir, "config.json"));
    }

    public void Run(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            KeepForFailure();
            throw;
        }
    }

    public void KeepForFailure()
    {
        _keepForFailure = true;
        TestAppBuilder.MarkFailureRetained();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Environment.SetEnvironmentVariable(AvaloniaConfigVariable, _previousAvaloniaConfigDir);
        Environment.SetEnvironmentVariable(PythonConfigVariable, _previousPythonConfigDir);

        if (_keepForFailure)
        {
            Console.WriteLine($"[COMTool.App.Tests] kept test root: {Root}");
            return;
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[COMTool.App.Tests] failed to delete {Root}: {ex.Message}");
        }
    }
}
