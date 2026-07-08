using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.Vm;

public sealed record VmProgram(
    Action<VmState> Delegate,
    int MaxActiveLocalsDepth,
    IReadOnlyList<Action<VmState>>? Functions = null,
    ValueRepresentationKind? RootValueKind = null,
    IReadOnlyList<CallSiteEntry>? CallSites = null,
    /// <summary>
    /// Nodes indexed by the step/PC assigned during lowering.
    /// Used by debuggers to resolve PC to symbolic AST node for
    /// stack traces, variable names, source locations, etc.
    /// </summary>
    IReadOnlyList<Node>? StepNodes = null,

    /// <summary>
    /// Optional richer debug info. Can include per-node or per-function
    /// variable layout (name -> frame offset) so debuggers can map
    /// raw stack values to names without re-running analysis.
    /// </summary>
    object? DebugInfo = null
);