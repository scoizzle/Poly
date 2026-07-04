namespace Poly.Interpretation.Vm;

using Poly.Syntax;

public sealed record VmProgram(
    Action<VmState> Delegate,
    IReadOnlyDictionary<NodeId, SourceRange> SourceRanges,
    IReadOnlyList<FunctionEntry> Functions,
    IReadOnlyList<object?>? Constants,
    int MaxActiveLocalsDepth
);