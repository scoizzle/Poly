namespace Poly.Data.Modeling;

public sealed record DomainMutationStepTrace(
    string CommandType,
    IReadOnlyList<NodeId> AffectedNodeIds
);

public sealed record DomainMutationTrace(
    IReadOnlyList<DomainMutationStepTrace> Steps,
    IReadOnlyList<NodeId> AffectedNodeIds,
    int AppliedStepCount,
    bool RolledBack,
    bool Succeeded,
    TimeSpan Duration,
    int ErrorCount,
    int WarningCount
);

public sealed record DomainMutationExecutionResult(
    AnalysisResult Analysis,
    DomainMutationTrace Trace
);