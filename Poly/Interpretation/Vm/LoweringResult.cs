namespace Poly.Interpretation.Vm;

using Poly.Interpretation.Vm.Instructions;

public sealed record LoweringResult(
    List<Instruction> Instructions,
    int MaxActiveLocalsDepth = 32,
    IReadOnlyDictionary<NodeId, SourceRange>? SourceRanges = null
);