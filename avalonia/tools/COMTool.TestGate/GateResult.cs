namespace COMTool.TestGate;

public sealed class GateResult
{
    public GateResult(IReadOnlyList<string> errors)
    {
        Errors = errors.ToArray();
    }

    public IReadOnlyList<string> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;
}
