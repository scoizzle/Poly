using Poly.Syntax.Analysis;

namespace Poly.Interpretation.VirtualMachine;

internal sealed record FunctionEntry(int PC, int ArgSlots, int RetSlots, int LocalCount = 0) {
    public Node? SourceNode;
}
internal sealed record ExceptionRegion(int TryStart, int TryEnd, int CatchStart, int? FinallyStart);

internal sealed class Bytecode {
    public IReadOnlyList<MicroOp> MicroOps { get; }
    public Action<VmState>? CompiledLoop { get; internal set; }
    public IReadOnlyList<FunctionEntry> Functions { get; }
    public IReadOnlyList<object?> Constants { get; }
    public IReadOnlyList<CallSiteDelegate> CallSites { get; }
    public IReadOnlyList<string> CallSiteTargets { get; }
    public IReadOnlyList<ExceptionRegion> ExceptionRegions { get; }
    public IReadOnlyList<LoopBodyEntry> LoopBodies { get; }
    public Type? ResultType { get; }
    public AnalysisResult? AnalysisResult { get; }
    /// <summary>PC range for each AST node, built during lowering.
    /// Used by the debugger for step-over range computation.</summary>
    public Dictionary<NodeId, (int StartPC, int EndPC)>? NodeRanges { get; }
    public int CodeLength => MicroOps.Count;

    public Bytecode(
        List<MicroOp> microOps,
        List<FunctionEntry>? functions = null,
        List<object?>? constants = null,
        List<CallSiteDelegate>? callSites = null,
        List<string>? callSiteTargets = null,
        List<ExceptionRegion>? exceptionRegions = null,
        Type? resultType = null,
        AnalysisResult? analysisResult = null,
        List<LoopBodyEntry>? loopBodies = null,
        Dictionary<NodeId, (int StartPC, int EndPC)>? nodeRanges = null) {
        MicroOps = microOps;
        Functions = functions ?? [];
        Constants = constants ?? [];
        CallSites = callSites ?? [];
        CallSiteTargets = callSiteTargets ?? [];
        ExceptionRegions = exceptionRegions ?? [];
        ResultType = resultType;
        AnalysisResult = analysisResult;
        LoopBodies = loopBodies ?? [];
        NodeRanges = nodeRanges;
    }

    /// <summary>Compile the µop list on first call and cache the resulting
    /// delegate.  Subsequent calls are O(1).</summary>
    public Action<VmState> EnsureCompiled() {
        if (CompiledLoop is not null) return CompiledLoop;
        CompiledLoop = ProgramCompiler.Compile(MicroOps);
        return CompiledLoop;
    }
}