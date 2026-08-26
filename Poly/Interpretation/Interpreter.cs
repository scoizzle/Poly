using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;

using PrimValueKind = Poly.Interpretation.Analysis.Semantics.ValueRepresentationKind;

namespace Poly.Interpretation;

/// <summary>
/// Language VM: analyze, compile, and execute <c>Poly.Ast</c> programs.
/// DomainModeling is a client that lowers into this language — not a VM concern.
///
/// Direct AST-to-VM-ABI lowering is the sole compilation path.
/// <see cref="Compile(Node, CompilationMode)"/> fails closed on analysis errors.
/// </summary>
public static class Interpreter {
    private static readonly Analyzer _analyzer = new AnalyzerBuilder()
        .UseTypeDefinitionNodeAnalyzer()
        .UseThisReferenceContext()
        .UseTypeAndMemberResolver()
        .UseVariableScopeValidator()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseConstantFolding()
        .UseControlFlowAnalysis()
        .UseDefiniteAssignmentAnalysis()
        .UseLambdaReturnTypeResolution()
        .UseValueRepresentationAnalysis()
        .UseCallSiteCatalog()
        .UseExceptionRegionAnalysis()
        .UseSyntaxTypeCompatibility()
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
    /// Analyze and compile a Syntax program. Analysis errors fail closed.
    /// </summary>
    public static VmProgram Compile(Node node, ITypeDefinitionProvider typeDefinitions, CompilationMode mode = CompilationMode.Normal) {
        var analysis = _analyzer.Analyze(node, typeDefinitions: typeDefinitions);
        FailLoudOnAnalysisErrors(analysis);
        return DirectVmAbiEmitter.Emit(node, analysis, mode);
    }

    /// <summary>Alias of <see cref="Compile(Node, ITypeDefinitionProvider, CompilationMode)"/>.</summary>
    public static VmProgram CompileChecked(Node node, ITypeDefinitionProvider typeDefinitions, CompilationMode mode = CompilationMode.Normal) =>
        Compile(node, typeDefinitions, mode);

    /// <summary>
    /// Analyze and compile a Syntax program. Analysis errors fail closed.
    /// This is the language-VM compile door.
    /// </summary>
    public static VmProgram Compile(Node node, CompilationMode mode = CompilationMode.Normal) {
        var analysis = _analyzer.Analyze(node);
        FailLoudOnAnalysisErrors(analysis);
        return DirectVmAbiEmitter.Emit(node, analysis, mode);
    }

    /// <summary>
    /// Illegal programs do not emit. Call <see cref="Analyze"/> to inspect diagnostics
    /// without compiling.
    /// </summary>
    private static void FailLoudOnAnalysisErrors(AnalysisResult analysis) {
        foreach (var d in analysis.Diagnostics) {
            if (d.Severity != DiagnosticSeverity.Error) continue;
            throw new InvalidOperationException($"VM compile rejected: {d.Message}");
        }
    }

    /// <summary>
    /// Compile a previously-analyzed node. Analysis errors on
    /// <paramref name="analysis"/> fail closed.
    /// </summary>
    public static VmProgram Compile(Node node, AnalysisResult analysis, CompilationMode mode = CompilationMode.Normal) {
        FailLoudOnAnalysisErrors(analysis);
        return DirectVmAbiEmitter.Emit(node, analysis, mode);
    }

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
        var clr = state.Program.RootClrType;

        if (rootKind == PrimValueKind.Void)
            return InterpreterResult.Void;

        if (rootKind == PrimValueKind.StackScalar || rootKind == PrimValueKind.Bool) {
            if (clr == typeof(double) || clr == typeof(float))
                return InterpreterResult.FromValue(BitConverter.Int64BitsToDouble(raw));
            return InterpreterResult.FromValue(raw);
        }

        if (rootKind == PrimValueKind.HeapRef) {
            if (raw == 0L)
                return InterpreterResult.FromValue(null);
            if (handle > 0 && handle < state.Heap.Count) {
                var heapObj = state.Heap.UnsafeGet(handle);
                if (heapObj is not null)
                    return InterpreterResult.FromValue(heapObj);
            }
            return InterpreterResult.FromValue(raw);
        }

        if (raw == 0L)
            return InterpreterResult.FromValue(raw);
        if (handle > 0 && handle < state.Heap.Count) {
            var heapObj = state.Heap.UnsafeGet(handle);
            if (heapObj is not null)
                return InterpreterResult.FromValue(heapObj);
        }

        return InterpreterResult.FromValue(raw);
    }

}