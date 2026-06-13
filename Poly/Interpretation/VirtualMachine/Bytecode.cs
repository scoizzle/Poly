using System.IO;

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

    /// <summary>Dump the entire µop listing to <paramref name="writer"/>
    /// in a human-readable format, including function table, exception
    /// regions, and each µop with PC index and source annotation.</summary>
    public void Dump(TextWriter writer) {
        var uops = MicroOps;

        // ── Function table ──
        var funcs = Functions;
        if (funcs.Count > 0) {
            writer.WriteLine(";; Functions");
            for (var i = 0; i < funcs.Count; i++) {
                var f = funcs[i];
                writer.WriteLine(
                    $";;   {i}: PC={f.PC}, args={f.ArgSlots}, ret={f.RetSlots}, locals={f.LocalCount}");
            }

            writer.WriteLine();
        }

        // ── Exception regions ──
        if (ExceptionRegions.Count > 0) {
            writer.WriteLine(";; Exception Regions");
            foreach (var er in ExceptionRegions) {
                writer.Write($";;   try [{er.TryStart}..{er.TryEnd}) → catch at {er.CatchStart}");
                if (er.FinallyStart is not null)
                    writer.Write($", finally at {er.FinallyStart}");
                writer.WriteLine();
            }

            writer.WriteLine();
        }

        // ── µop listing ──
        // Build PC → function index map for inline headers
        var funcAtPC = new Dictionary<int, int>();
        for (var i = 0; i < funcs.Count; i++)
            funcAtPC[funcs[i].PC] = i;

        writer.WriteLine(";; µops");
        for (var i = 0; i < uops.Count; i++) {
            if (funcAtPC.TryGetValue(i, out var fi))
                writer.WriteLine($";; --- function {fi} ---");

            var op = uops[i];
            if (op.SourceName is not null)
                writer.WriteLine($"{i,4}  {op}  ← {op.SourceName}");
            else
                writer.WriteLine($"{i,4}  {op}");
        }
    }

    /// <summary>Dump the µop listing to a string.</summary>
    public string DumpToString() {
        var sw = new StringWriter();
        Dump(sw);
        return sw.ToString();
    }
}