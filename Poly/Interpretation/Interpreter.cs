using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Primitives;

using PrimReturn = Poly.Syntax.Primitives.Return;
using PrimValueKind = Poly.Interpretation.Analysis.Semantics.ValueRepresentationKind;

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
///     <item><see cref="Semantics.UseValueRepresentationAnalysis"/></item>
///     <item><see cref="Semantics.UseCallSiteCatalog"/></item>
///     <item><see cref="ConstantFolding.UseConstantFolding"/></item>
///     <item><see cref="Semantics.UseDefiniteAssignmentAnalysis"/></item>
///     <item><see cref="Semantics.UseLambdaReturnTypeResolution"/></item>
///     <item><see cref="Semantics.UseExceptionRegionAnalysis"/></item>
///     <item><see cref="Analysis.UsePrimitiveExpansion"/></item>
///   </list>
///
/// The <see cref="Analyzer"/> is built once and cached; subsequent calls reuse it.
/// </summary>
public static class Interpreter {
    private static readonly Analyzer _analyzer = new AnalyzerBuilder()
        .UseThisReferenceContext()
        .UseTypeAndMemberResolver()
        .UseVariableScopeValidator()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseControlFlowAnalysis()
        .UseValueRepresentationAnalysis()
        .UseCallSiteCatalog()
        .UseConstantFolding()
        .UseDefiniteAssignmentAnalysis()
        .UseLambdaReturnTypeResolution()
        .UseExceptionRegionAnalysis()
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

        // Use ValueRepresentationMetadata from analysis when available to
        // correctly distinguish heap handles from raw scalars (fixes INT-002).
        var rootKind = state.Program.RootValueKind;

        if (rootKind == PrimValueKind.StackScalar || rootKind == PrimValueKind.Bool)
            return InterpreterResult.FromValue(raw);

        if (rootKind == PrimValueKind.HeapRef) {
            if (handle >= 0 && handle < state.Heap.Count) {
                var heapObj = state.Heap.UnsafeGet(handle);
                return InterpreterResult.FromValue(heapObj);
            }
            return InterpreterResult.FromValue(raw);
        }

        // Fallback heuristic (void, unknown, or no metadata):
        // Only dereference when the handle looks like a valid heap index.
        // Handle 0 is never a valid heap entry (first alloc starts at 0 but
        // is freed on null; 0 and 1 are always bool/scalar results).
        // After ANA-FIX-003, the standard pipeline always provides
        // RootValueKind for expression roots, so this path is only hit
        // for void-program terminations or external caller paths.
        if (handle >= 2 && handle < state.Heap.Count) {
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
            // Fallback: expand directly if the pass didn't capture metadata.
            // In release builds, this indicates a missing pipeline registration.
#if DEBUG
            System.Diagnostics.Debug.WriteLine(
                "[Interpreter] Warning: PrimitiveExpansionMetadata not found; " +
                "ExpansionPass may not have run. Falling back to direct expansion.");
            var ac = new AnalysisContext(Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared);
            expansionCtx = new ExpansionContext(ac);
            primitives = node.ToPrimitives(expansionCtx).ToArray();
#else
            throw new InvalidOperationException(
                "PrimitiveExpansionMetadata not found on root node. " +
                "The analysis pipeline must include UsePrimitiveExpansion().");
#endif
        }

        var callSites = analysis.GetCallSiteCatalog() is { Count: > 0 } sites
            ? sites
            : null;

        // Extract pending functions from the expansion context
        pendingFunctions = expansionCtx?.Env.ExtractPendingFunctions();

        // ── Compile pending function bodies as standalone delegates ──
        Action<VmState>[]? functionTable = null;
        if (pendingFunctions is not null && pendingFunctions.Count > 0) {
            int maxIdx = pendingFunctions.Max(pf => pf.LambdaIndex);
            functionTable = new Action<VmState>[maxIdx + 1];
            // Pre-fill the table with all compiled delegates. The table reference
            // is captured by Constant(functionTable) in each function's LINQ
            // expression tree, so at RUNTIME the Call µop reads the CURRENT array
            // contents — even for functions compiled later in this loop.
            foreach (var pf in pendingFunctions) {
                var funcPrims = new List<PrimitiveNode>(pf.Body);
                if (funcPrims.Count == 0 || funcPrims[^1] is not PrimReturn)
                    funcPrims.Add(new PrimReturn());
                var funcProgram = ProgramCompiler.CompilePrimitives(
                    funcPrims, mode, traceExpressions, functionTable, callSites);
                functionTable[pf.LambdaIndex] = funcProgram.Delegate;
            }
        }

        // Read ValueRepresentationMetadata from the root node to inform
        // InterpretResult about the root value's representation (fixes INT-002).
        var rootKind = analysis.GetMetadata<ValueRepresentationMetadata>(node)?.Kind;

#if DEBUG
        // The standard analysis pipeline always stamps ValueRepresentationMetadata
        // on every expression root. If this fires, the analyzer was built without
        // UseValueRepresentationAnalysis (or metadata was stripped/corrupted).
        if (meta is not null && rootKind is null) {
            System.Diagnostics.Debug.WriteLine(
                "[Interpreter] Warning: standard pipeline ran but RootValueKind is null; " +
                "ValueRepresentationAnalysis may not be registered in the pipeline.");
        }
#endif

        var primsList = primitives.ToList();
        primsList.Add(new PrimReturn());
        var program = ProgramCompiler.CompilePrimitives(primsList, mode, traceExpressions, functionTable, callSites);

        // Build exception region table from analysis metadata (INT-018 Phase 1c, Strategy B).
        // Extract and compile handler bodies as independent functions, then wrap
        // the main delegate in Expression.TryCatch with dispatch.
        var exceptionRegions = analysis.GetMetadata<ExceptionRegionMetadata>(null);
        var regionTable = ExceptionTableBuilder.BuildTable(primitives, exceptionRegions);

        if (regionTable is not null) {
            // Extract handler primitive ranges and compile as standalone functions.
            var handlerRanges = ExceptionTableBuilder.ExtractHandlerRanges(primitives);
            var handlerDelegates = new List<Action<VmState>>();

            int closureFuncCount = functionTable?.Length ?? 0;
            foreach (var (startPc, endPc, kind, regionIdx, _) in handlerRanges) {
                var handlerPrims = new List<PrimitiveNode>();
                for (int pc = startPc; pc < endPc; pc++)
                    handlerPrims.Add(primitives[pc]);
                if (handlerPrims.Count == 0 || handlerPrims[^1] is not PrimReturn)
                    handlerPrims.Add(new PrimReturn());

                var handlerProgram = ProgramCompiler.CompilePrimitives(
                    handlerPrims, mode, traceExpressions, callSites: callSites);
                handlerDelegates.Add(handlerProgram.Delegate);
            }

            // Build combined function table: closure functions + handler functions.
            int totalFuncCount = closureFuncCount + handlerDelegates.Count;
            var combinedFunctions = new Action<VmState>[totalFuncCount];
            if (functionTable is not null) {
                for (int i = 0; i < closureFuncCount; i++)
                    combinedFunctions[i] = functionTable[i];
            }
            for (int i = 0; i < handlerDelegates.Count; i++)
                combinedFunctions[closureFuncCount + i] = handlerDelegates[i];

            // Update HandlerFuncIndex in region table entries by position.
            var updatedTable = regionTable;
            int handlerCount = Math.Min(handlerRanges.Count, updatedTable.Entries.Count);
            for (int h = 0; h < handlerCount; h++) {
                int handlerIndex = closureFuncCount + h;
                updatedTable = updatedTable.WithHandlerIndexAt(h, handlerIndex);
            }

            // Wrap the main delegate inside a try/catch that dispatches to handlers.
            var mainDelegate = program.Delegate;
            program = program with {
                Delegate = s => {
                    try {
                        mainDelegate(s);
                    }
                    catch (Exception ex) when (updatedTable is not null) {
                        ProgramCompiler.DispatchException(s, updatedTable, ex);
                    }
                },
                Functions = combinedFunctions,
                Regions = updatedTable
            };
        }

        program = program with {
            RootValueKind = rootKind,
            CallSites = callSites,
            Regions = regionTable ?? program.Regions
        };
        return functionTable is not null
            ? program with { Functions = functionTable }
            : program;
    }
}