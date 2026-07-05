namespace Poly.Interpretation.Vm;

public sealed record VmProgram(
    Action<VmState> Delegate,
    int MaxActiveLocalsDepth,
    IReadOnlyList<Action<VmState>>? Functions = null
);