using Poly.Analysis;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Findings from domain analysis, registered as one <c>analysis-report</c> artifact per domain.
/// Each finding names the domain element it points at by id path (for example
/// <c>Hotel/Reservation/Confirm</c>).
/// </summary>
public sealed record AnalysisReport(IReadOnlyList<AnalysisFinding> Findings) {
    /// <summary>The findings in analysis order. Cannot be changed by a caller.</summary>
    public IReadOnlyList<AnalysisFinding> Findings { get; } = CopyOf(Findings);

    public bool Equals(AnalysisReport? other) =>
        other is not null && Findings.SequenceEqual(other.Findings);

    public override int GetHashCode() =>
        Findings.Aggregate(0, HashCode.Combine);

    private static IReadOnlyList<AnalysisFinding> CopyOf(IReadOnlyList<AnalysisFinding> findings) {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Any(f => f is null))
            throw new ArgumentException("Findings must not contain null.", nameof(findings));
        return findings.ToArray().AsReadOnly();
    }
}

/// <summary>
/// One analysis diagnostic, with the id path of the domain element it was reported on.
/// </summary>
/// <param name="Code">Diagnostic code, or null when the analyzer did not assign one.</param>
/// <param name="Severity">Error, warning, information, or hint.</param>
/// <param name="Message">Human-readable text.</param>
/// <param name="ElementPath">
/// Name path of the element the diagnostic points at (nearest named ancestor when the
/// reported node is unnamed), for example <c>Hotel/Reservation</c>.
/// </param>
public sealed record AnalysisFinding(
    string? Code,
    DiagnosticSeverity Severity,
    string Message,
    string ElementPath);
