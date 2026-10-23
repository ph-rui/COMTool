using Avalonia.Threading;

namespace COMTool.App.Tools;

/// <summary>
/// Reports progress on the UI thread regardless of the ambient
/// synchronization context, which keeps headless tests deterministic.
/// </summary>
internal sealed class UiProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) =>
        Dispatcher.UIThread.Post(() => action(value));
}
