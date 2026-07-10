using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.Vm;

/// <summary>Compiled Poly VM program produced by <see cref="DirectVmAbiEmitter.Emit"/>.
/// Contains the executable delegate, function table, call site catalog, and debug
/// metadata for step-through and variable inspection.</summary>
/// <param name="Delegate">The compiled delegate that executes the program when
/// invoked with a <see cref="VmState"/>.</param>
/// <param name="MaxActiveLocalsDepth">Maximum number of simultaneously live
/// local variables across all frames. Used for ring register allocation.</param>
/// <param name="Functions">Optional list of compiled function delegates for
/// lambda/function call support. Indexed by function ID during call lowering.</param>
/// <param name="RootValueKind">Optional value representation classification
/// for the program's root result. Used by <see cref="Interpreter.InterpretResult"/>
/// to correctly return heap references vs. stack scalars.</param>
/// <param name="CallSites">Optional module-level call site catalog. Indexed by
/// stable call site index for portable method/constructor resolution.</param>
/// <param name="StepNodes">Nodes indexed by the step/PC assigned during lowering.
/// Used by debuggers to resolve PC to symbolic AST node for stack traces,
/// variable names, source locations, etc.</param>
/// <param name="DebugInfo">Optional richer debug info (e.g. per-function
/// variable name-to-frame-offset layout). Debuggers can map raw stack values
/// to names without re-running analysis.</param>
/// <param name="RegisterCount">Number of register file slots used by this
/// program. Default 8, grows on demand up to 32.</param>
public sealed record VmProgram(
    Action<VmState> Delegate,
    int MaxActiveLocalsDepth,
    IReadOnlyList<Action<VmState>>? Functions = null,
    ValueRepresentationKind? RootValueKind = null,
    IReadOnlyList<CallSiteEntry>? CallSites = null,
    IReadOnlyList<Node>? StepNodes = null,
    object? DebugInfo = null,
    int RegisterCount = 8
);