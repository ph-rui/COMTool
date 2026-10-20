namespace COMTool.TestGate.Tests;

internal sealed class TempDirectory : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "comtool-testgate-tests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(_path);

    public string Write(string name, string content)
    {
        var file = Path.Combine(_path, name);
        File.WriteAllText(file, content);
        return file;
    }

    public void Dispose()
    {
        if (Directory.Exists(_path))
        {
            Directory.Delete(_path, recursive: true);
        }
    }
}
