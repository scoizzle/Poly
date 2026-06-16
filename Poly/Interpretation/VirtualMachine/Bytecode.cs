using System.IO;

using Poly.Syntax.Analysis;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>Speculative payload carried on <see cref="Bytecode"/> for
/// debugging, tooling, or future optimizations.  Null when the consumer
/// does not need these fields — they are populated during lowering for
/// potential use but are not required for correct VM execution.</summary>
public sealed record BytecodeSpec(
    AnalysisResult? AnalysisResult,
    IReadOnlyList<string> CallSiteTargets,
    IReadOnlyList<LoopBodyEntry> LoopBodies
);

public sealed record FunctionEntry(int PC, int ArgSlots, int LocalCount = 0);
public sealed record ExceptionRegion(int TryStart, int TryEnd, int CatchStart, int? FinallyStart);

public sealed class Bytecode {
    private IReadOnlyList<MicroOp> _microOps;
    public IReadOnlyList<MicroOp> MicroOps => _microOps;
    public Action<VmState>? CompiledLoop { get; internal set; }
    public IReadOnlyList<FunctionEntry> Functions { get; }
    public IReadOnlyList<object?> Constants { get; }
    public IReadOnlyList<CallSiteDelegate> CallSites { get; }
    public IReadOnlyList<ExceptionRegion> ExceptionRegions { get; }
    public Type? ResultType { get; }
    public BytecodeSpec? Spec { get; }
    /// <summary>PC range for each AST node, built during lowering.
    /// Used by the debugger for step-over range computation.</summary>
    public Dictionary<NodeId, (int StartPC, int EndPC)>? NodeRanges { get; }
    /// <summary>Replace the µop list after optimization.
    /// Preserves identity — callers holding a reference to the
    /// same <c>Bytecode</c> instance see the new list.</summary>
    internal void ReplaceOps(IReadOnlyList<MicroOp> ops) {
        _microOps = ops;
        CompiledLoop = null;
    }

    public int CodeLength => _microOps.Count;

    public Bytecode(
        List<MicroOp> microOps,
        List<FunctionEntry>? functions = null,
        List<object?>? constants = null,
        List<CallSiteDelegate>? callSites = null,
        List<ExceptionRegion>? exceptionRegions = null,
        Type? resultType = null,
        BytecodeSpec? spec = null,
        Dictionary<NodeId, (int StartPC, int EndPC)>? nodeRanges = null) {
        _microOps = microOps;
        Functions = functions ?? [];
        Constants = constants ?? [];
        CallSites = callSites ?? [];
        ExceptionRegions = exceptionRegions ?? [];
        ResultType = resultType;
        Spec = spec;
        NodeRanges = nodeRanges;
    }

    /// <summary>Compile the µop list on first call and cache the resulting
    /// delegate.  Subsequent calls are O(1).  Runs µop-level optimization
    /// before compilation, storing the optimized ops back into this
    /// instance so dumps and debuggers see the post-optimization stream.</summary>
    public Action<VmState> EnsureCompiled() {
        if (CompiledLoop is not null) return CompiledLoop;
        var optimized = UopOptimizer.Optimize([.. _microOps], Spec,
            Functions is List<FunctionEntry> fnList ? fnList : null);
        if (optimized.Length != _microOps.Count
            || optimized.Zip(_microOps, (a, b) => a == b).Any(x => !x))
            ReplaceOps(optimized);
        CompiledLoop = ProgramCompiler.Compile(_microOps);
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
                    $";;   {i}: PC={f.PC}, args={f.ArgSlots}, locals={f.LocalCount}");
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