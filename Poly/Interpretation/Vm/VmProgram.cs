using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.Vm;

public sealed record VmProgram(
    Action<VmState> Delegate,
    int MaxActiveLocalsDepth,
    IReadOnlyList<Action<VmState>>? Functions = null,
    ValueRepresentationKind? RootValueKind = null,
    IReadOnlyList<CallSiteEntry>? CallSites = null,
    ExceptionRegionTable? Regions = null
);