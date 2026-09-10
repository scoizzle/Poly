namespace Poly.DomainModeling.Evolution;

/// <summary>
/// Captures what happened during an evolution operation (successful or rejected).
/// Designed to be rich enough for LLM/MCP agents and future real-time UIs while remaining simple.
/// Steps carry the ordered natural-language descriptions of applied changes.
/// The RolledBack flag, mutation errors on <see cref="EvolutionResult"/>, and
/// analysis diagnostics tell the caller whether the proposal was rejected
/// (no actual rollback occurs — the model is immutable).
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