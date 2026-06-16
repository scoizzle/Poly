namespace Poly.Interpretation.VirtualMachine;

/// <summary>Sequential µop optimizer pipeline.  Each pass transforms the
/// µop array in place or produces a new one.  Passes are applied in order,
/// and the result is consumed by <see cref="ProgramCompiler.Compile"/>.</summary>
public static class UopOptimizer {
    /// <summary>Run all registered optimization passes on <paramref name="uops"/>
    /// and return the (possibly new) µop array.</summary>
    public static MicroOp[] Optimize(MicroOp[] uops, BytecodeSpec? spec = null,
        List<FunctionEntry>? functions = null) {
        var current = uops;
        // µop-level heuristic synthesis — always safe, no spec needed
        current = new UopHeuristicPass().Apply(current);
        // Loop CSE — needs loop boundary metadata from lowering
        if (spec is not null)
            current = new LoopCsePass(spec, functions).Apply(current);
        // Future passes go here
        return current;
    }
}