using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Primitives;

using PrimReturn = Poly.Syntax.Primitives.Return;

namespace Poly.Interpretation;

/// <summary>
/// Standard analysis pipeline for VM execution.
///
/// Bundles all interpretation-level analysis passes required for the
/// AST → primitives → compiled delegate path:
///   <list type="bullet">
///     <item><see cref="Semantics.UseTypeAndMemberResolver"/></item>
///     <item><see cref="Semantics.UseVariableScopeValidator"/></item>
///     <item><see cref="Semantics.UseSideEffectAnalysis"/></item>
///     <item><see cref="Semantics.UseThisReferenceContext"/></item>
///     <item><see cref="Semantics.UseJumpTargetResolution"/></item>
///     <item><see cref="ControlFlow.UseControlFlowAnalysis"/></item>
///     <item><see cref="ConstantFolding.UseConstantFolding"/></item>
///     <item><see cref="Semantics.UseDefiniteAssignmentAnalysis"/></item>
///     <item><see cref="Semantics.UseLambdaReturnTypeResolution"/></item>
///     <item><see cref="Analysis.UsePrimitiveExpansion"/></item>
///   </list>
///
/// The <see cref="Analyzer"/> is built once and cached; subsequent calls reuse it.
/// </summary>
public static class Interpreter {
    private static readonly Analyzer _analyzer = new AnalyzerBuilder()
        .UseTypeAndMemberResolver()
        .UseVariableScopeValidator()
        .UseSideEffectAnalysis()
        .UseThisReferenceContext()
        .UseJumpTargetResolution()
        .UseControlFlowAnalysis()
        .UseConstantFolding()
        .UseDefiniteAssignmentAnalysis()
        .UseLambdaReturnTypeResolution()
        .UsePrimitiveExpansion()
        .Build();

    /// <summary>
    /// Gets the cached <see cref="Analyzer"/> instance for the standard VM pipeline.
    /// </summary>
    public static Analyzer Analyzer => _analyzer;

    /// <summary>
    /// Analyze <paramref name="node"/> through the standard VM pipeline and return
    /// the <see cref="AnalysisResult"/>.  Primitive expansion metadata is guaranteed
    /// to be present for the root node on the returned result.
    /// </summary>
    public static AnalysisResult Analyze(Node node) =>
        _analyzer.Analyze(node);

    /// <summary>
    /// Analyze <paramref name="node"/> through the standard VM pipeline and compile
    /// the expanded primitives into a <see cref="VmProgram"/>.
    /// </summary>
    public static VmProgram Compile(Node node, CompilationMode mode = CompilationMode.Normal, TextWriter? traceExpressions = null) {
        var analysis = _analyzer.Analyze(node);
        return CompileCore(node, analysis, mode, traceExpressions);
    }

    /// <summary>
    /// Compile a previously-analyzed <paramref name="node"/> (that was analyzed with
    /// the standard pipeline) into a <see cref="VmProgram"/>.  Unlike <see cref="Compile(Node, CompilationMode)"/>,
    /// this does not re-run the analysis passes.
    /// </summary>
    public static VmProgram Compile(Node node, AnalysisResult analysis, CompilationMode mode = CompilationMode.Normal, TextWriter? traceExpressions = null) =>
        CompileCore(node, analysis, mode, traceExpressions);

    /// <summary>
    /// Analyze, compile, and execute <paramref name="node"/>, returning the
    /// top-of-stack value.
    /// </summary>
    public static long Execute(Node node) {
        using var result = Execute(Compile(node, CompilationMode.Normal));
        return result.RawValue;
    }

    /// <summary>
    /// Execute a pre-compiled <paramref name="program"/>, constructing a
    /// <see cref="VmState"/> internally and returning an <see cref="ExecutionResult"/>
    /// that owns the state.  The result carries both the <see cref="InterpreterResult"/>
    /// and the <see cref="VmState"/> for inspection or resumption.
    /// </summary>
    public static ExecutionResult Execute(VmProgram program, params IEnumerable<object?> args) =>
        Execute(program, s => s.SetArgs(args));

    /// <summary>
    /// Execute a pre-compiled <paramref name="program"/> with state configuration
    /// before the compiled delegate runs.  The <paramref name="configure"/> callback
    /// can set state properties (e.g. <c>Trace</c>, <c>MaxLoopIterations</c>) and
    /// call <c>state.SetArgs(...)</c> to seed arguments.
    /// </summary>
    public static ExecutionResult Execute(VmProgram program, Action<VmState> configure) {
        var state = new VmState(program);
        configure(state);
        state.Status = InterpreterStatus.Running;
        state.Registers ??= new long[state.Program.MaxActiveLocalsDepth];
        state.Program.Delegate(state);
        return new ExecutionResult(state, InterpretResult(state));
    }

    // ── Shared implementation ─────────────────────────────────────

    /// <summary>
    /// Execute a pre-configured <see cref="VmState"/> inline (no new state
    /// created). Internal — tests and resumption scenarios only.
    /// </summary>
    internal static void Execute(VmState state) {
        state.Program.Delegate(state);
    }

    /// <summary>
    /// Execute or resume on an existing <see cref="VmState"/> with new arguments.
    /// Internal — calling code should generally prefer the state-owning
    /// <see cref="ExecutionResult"/> API via the <c>Execute</c> overloads.
    /// </summary>
    internal static InterpreterResult Resume(VmState state, params IEnumerable<object?> args) {
        state.Status = InterpreterStatus.Running;
        state.Registers ??= new long[state.Program.MaxActiveLocalsDepth];
        state.SetArgs(args);
        state.Program.Delegate(state);
        return InterpretResult(state);
    }

    private static InterpreterResult InterpretResult(VmState state) {
        if (state.Status == InterpreterStatus.Suspended)
            return InterpreterResult.Suspend();

        int sp = state.Stack.StackPointer;
        if (sp <= 0)
            return InterpreterResult.Void;

        long raw = state.Stack.RawSlots[sp - 1];
        int handle = (int)raw;

        // If the result is a heap handle, dereference to give callers
        // the actual CLR object rather than an opaque handle.
        // 0 and 1 are excluded as they're almost always boolean results.
        if (handle > 1 && handle < state.Heap.Count) {
            var heapObj = state.Heap.UnsafeGet(handle);
            return InterpreterResult.FromValue(heapObj);
        }

        return InterpreterResult.FromValue(raw);
    }

    private static VmProgram CompileCore(Node node, AnalysisResult analysis, CompilationMode mode, TextWriter? traceExpressions = null) {
        var meta = analysis.GetMetadata<PrimitiveExpansionMetadata>(node);
        IReadOnlyList<PrimitiveNode> primitives;
        List<PendingFunction>? pendingFunctions = null;
        ExpansionContext? expansionCtx = null;
        if (meta is not null) {
            primitives = meta.Primitives;

            // The expansion pass stored the ExpansionContext as metadata so we
            // can extract pending function bodies compiled from child lambdas.
            expansionCtx = analysis.GetMetadata<ExpansionContext>(null);
        }
        else {
            // Fallback: expand directly if the pass didn't capture metadata
            // (e.g. when the root node was replaced during analysis).
            System.Diagnostics.Debug.WriteLine(
                "[Interpreter] Warning: PrimitiveExpansionMetadata not found; " +
                "ExpansionPass may not have run. Falling back to direct expansion.");
            var ac = new AnalysisContext(Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared);
            expansionCtx = new ExpansionContext(ac);
            primitives = node.ToPrimitives(expansionCtx).ToArray();
        }

        // Extract pending functions from the expansion context
        pendingFunctions = expansionCtx?.Env.ExtractPendingFunctions();

        // ── Compile pending function bodies as standalone delegates ──
        Action<VmState>[]? functionTable = null;
        if (pendingFunctions is not null && pendingFunctions.Count > 0) {
            int maxIdx = pendingFunctions.Max(pf => pf.LambdaIndex);
            functionTable = new Action<VmState>[maxIdx + 1];
            foreach (var pf in pendingFunctions) {
                // Each function body uses 0-based slot indices relative to FrameBase.
                // FrameBase is set by the caller before invoking this delegate.
                // Slot 0..ParamCount-1 are parameter slots (args passed by caller).
                // LoadUpvalue/StoreUpvalue use state.ClosureHandle (set by caller).
                var funcPrims = new List<PrimitiveNode>(pf.Body);
                // Ensure the function ends with a Return
                if (funcPrims.Count == 0 || funcPrims[^1] is not PrimReturn)
                    funcPrims.Add(new PrimReturn());
                var funcProgram = ProgramCompiler.CompilePrimitives(funcPrims, mode, traceExpressions);
                functionTable[pf.LambdaIndex] = funcProgram.Delegate;
            }
        }

        var primsList = primitives.ToList();
        primsList.Add(new PrimReturn());
        var program = ProgramCompiler.CompilePrimitives(primsList, mode, traceExpressions, functionTable);
        return functionTable is not null
            ? program with { Functions = functionTable }
            : program;
    }
}