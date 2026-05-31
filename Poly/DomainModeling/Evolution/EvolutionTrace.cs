using System;
using System.Collections.Generic;

namespace Poly.DomainModeling.Evolution;

/// <summary>
/// Captures what happened during an evolution operation (successful or rolled back).
/// Designed to be rich enough for LLM/MCP agents and future real-time UIs while remaining simple.
/// The RolledBack flag and diagnostics in the accompanying AnalysisResult tell the caller
/// whether the proposed changes were rejected.
/// </summary>
public sealed record EvolutionTrace(
    IReadOnlyList<EvolutionStep> Steps,
    IReadOnlyList<string> AffectedNodeIds,
    bool RolledBack,
    TimeSpan Duration,
    int ErrorCount,
    int WarningCount
);

public sealed record EvolutionStep(
    string ChangeDescription,
    IReadOnlyList<string> AffectedNodeIds
);