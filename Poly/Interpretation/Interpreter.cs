using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Analysis;

using PrimValueKind = Poly.Interpretation.Analysis.Semantics.ValueRepresentationKind;

namespace Poly.Interpretation;

/// <summary>
/// Standard analysis pipeline for VM execution.
///
/// Bundles interpretation-level analysis passes. The primary compilation path
/// is now direct AST-to-VM-ABI lowering (no primitive expansion).
/// Many passes remain useful for semantics, metadata, and diagnostics.
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
        // Direct AST-to-ABI lowering is the primary path.
        .Build();

    /// <summary>
    /// Gets the cached <see cref="Analyzer"/> instance for the standard VM pipeline.
    /// </summary>
    public static Analyzer Analyzer => _analyzer;

    /// <summary>
    /// Analyze <paramref name="node"/> through the standard pipeline.
    /// The analysis pipeline runs before compilation to produce metadata.
    /// Direct AST lowering is the primary execution path.
    /// </summary>
    public static AnalysisResult Analyze(Node node) =>
        _analyzer.Analyze(node);

    /// <summary>
    /// Analyze and compile using a custom type definition provider.
    /// Enables custom type definitions to be used during analysis.
    /// to be used during analysis without modifying the standard provider stack.
    /// </summary>
    public static VmProgram Compile(Node node, ITypeDefinitionProvider typeDefinitions, CompilationMode mode = CompilationMode.Normal) {
        var analysis = _analyzer.Analyze(node, typeDefinitions: typeDefinitions);
        return DirectVmAbiEmitter.Emit(node, analysis, mode);
    }

    /// <summary>
    /// Analyze and compile using the primary direct AST-to-VM-ABI lowering path.
    /// This is the supported way to produce a runnable <see cref="VmProgram"/>.
    /// </summary>
    public static VmProgram Compile(Node node, CompilationMode mode = CompilationMode.Normal) {
        var analysis = _analyzer.Analyze(node);
        return DirectVmAbiEmitter.Emit(node, analysis, mode);
    }

    /// <summary>
    /// Compile a previously-analyzed node using the direct AST-to-ABI emitter.
    /// </summary>
    public static VmProgram Compile(Node node, AnalysisResult analysis, CompilationMode mode = CompilationMode.Normal) =>
        DirectVmAbiEmitter.Emit(node, analysis, mode);

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
        // Registers array must be large enough for SP-based ring save indexing.
        state.Registers ??= new long[256];
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
        state.Status = InterpreterStatus.Resuming;
        state.Registers ??= new long[256];
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

}