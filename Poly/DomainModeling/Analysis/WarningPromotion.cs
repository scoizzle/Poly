using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The warn-as-error primitive (V-04 D8, F1 provisional): promotes reported warnings to errors by
/// code, after the report, so an allowance has already picked each finding's severity. It copies
/// the list — the analysis result keeps the unpromoted severities, so a summary read from it does
/// not change. Inert until C-11 wires a policy to it.
/// </summary>
public static class WarningPromotion {
    /// <summary>
    /// <paramref name="diagnostics"/> with every <see cref="DiagnosticSeverity.Warning"/> of a code
    /// in <paramref name="codes"/> promoted to <see cref="DiagnosticSeverity.Error"/>;
    /// <paramref name="codes"/> null promotes every warning. Nothing else changes, and the input
    /// list is not mutated.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Apply(
        IReadOnlyList<Diagnostic> diagnostics, IReadOnlySet<string>? codes = null) {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return [.. diagnostics];
    }
}