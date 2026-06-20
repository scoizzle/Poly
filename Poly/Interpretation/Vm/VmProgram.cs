namespace Poly.Interpretation.Vm;

using Poly.Interpretation.Vm.Instructions;
using Poly.Syntax;

public sealed record VmProgram(
    Action<VmState> Delegate,
    IReadOnlyList<Instruction> Instructions,
    IReadOnlyDictionary<NodeId, SourceRange> SourceRanges,
    IReadOnlyList<FunctionEntry> Functions,
    IReadOnlyList<object?>? Constants,
    IReadOnlyList<CallSiteDelegate>? CallSites,
    int MaxActiveLocalsDepth
);