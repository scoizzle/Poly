namespace Poly.Interpretation.Vm;

using Poly.Interpretation.Vm.Instructions;

public sealed record LoweringResult(
    List<Instruction> Instructions,
    int MaxActiveLocalsDepth = 32,
    IReadOnlyDictionary<NodeId, SourceRange>? SourceRanges = null
) {
    /// <summary>
    /// Heap-allocated constant values collected during UopGeneration.
    /// Pre-loaded into <c>VmState.Heap</c> before execution.  Each entry's
    /// index is the heap handle used by <c>LoadHeapConst</c> instructions.
    /// </summary>
    public IReadOnlyList<object?>? HeapConstants { get; init; }
}