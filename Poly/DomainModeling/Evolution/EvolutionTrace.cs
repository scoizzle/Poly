namespace Poly.DomainModeling.Evolution;

/// <summary>
/// Captures what happened during an evolution operation (successful or rejected).
/// Designed to be rich enough for LLM/MCP agents and future real-time UIs while remaining simple.
/// Steps carry the ordered natural-language descriptions (also emitted as Information diagnostics).
/// The RolledBack flag and diagnostics in the accompanying AnalysisResult tell the caller
/// whether the proposed changes were rejected (no actual rollback occurs — the model is immutable).
/// </summary>
public sealed record EvolutionTrace(
    IReadOnlyList<EvolutionStep> Steps,
    bool RolledBack,
    TimeSpan Duration,
    int ErrorCount,
    int WarningCount
);

public sealed record EvolutionStep(
    string ChangeDescription
);