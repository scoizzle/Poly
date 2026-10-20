using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.Vm;

/// <summary>Compiled Poly VM program produced by <see cref="DirectVmAbiEmitter.Emit"/>.
/// Contains the executable delegate and debug metadata for step-through and
/// variable inspection.</summary>
/// <param name="Delegate">The compiled delegate that executes the program when
/// invoked with a <see cref="VmState"/>.</param>
/// <param name="MaxRingDepth">Maximum ring depth reached while compiling the
/// program. Ring values live in CLR locals in the compiled delegate; this value
/// is retained for tests and diagnostics.</param>
/// <param name="RootValueKind">Optional value representation classification
/// for the program's root result. Used by <see cref="Interpreter.InterpretResult"/>
/// to correctly return heap references vs. stack scalars.</param>
/// <param name="RootClrType">The CLR type of the program's root result, when
/// analysis resolved it.</param>
/// <param name="StepNodes">Nodes indexed by the step/PC assigned during lowering.
/// Used by debuggers to resolve PC to symbolic AST node for stack traces,
/// variable names, source locations, etc.</param>
/// <param name="DebugInfo">Optional richer debug info (e.g. per-function
/// variable name-to-frame-offset layout). Debuggers can map raw stack values
/// to names without re-running analysis.</param>
/// <param name="RootParameterClrTypes">The CLR types of the root parameters, in
/// slot order, used by <see cref="VmState.SetArgs"/> to decide nullable and
/// scalar representation.</param>
public sealed record VmProgram(
    Action<VmState> Delegate,
    int MaxRingDepth,
    ValueRepresentationKind? RootValueKind = null,
    Type? RootClrType = null,
    IReadOnlyList<Node>? StepNodes = null,
    object? DebugInfo = null,
    IReadOnlyList<Type?>? RootParameterClrTypes = null
);