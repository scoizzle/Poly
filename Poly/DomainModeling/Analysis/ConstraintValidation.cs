using Poly.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.DomainModeling.Analysis;

/// <summary>Three-valued constraint check result (#367 D-1).</summary>
internal enum ConstraintVerdict { Satisfied, Violated, Undecided }

/// <summary>
/// Static helpers for checking whether a value satisfies a constraint.
/// Used at analysis time to validate literal assignments and parameter-propagation chains.
/// </summary>
internal static class ConstraintValidation {
    /// <summary>
    /// Evaluates <paramref name="constraint"/> against a literal. A constraint kind this
    /// validator does not model is <see cref="ConstraintVerdict.Undecided"/>, never
    /// <see cref="ConstraintVerdict.Satisfied"/>: callers must decide what to do with it.
    /// </summary>
    public static ConstraintVerdict Evaluate(Constraint constraint, object? value) {
        ArgumentNullException.ThrowIfNull(constraint);
        return ConstraintKinds.Of(constraint)?.Evaluate(constraint, value) ?? ConstraintVerdict.Undecided;
    }

    /// <summary>Reports the shared DMCS003 warning for a constraint kind analysis does not model.</summary>
    public static void ReportUndecided(AnalysisContext context, Node node, Constraint constraint, string where) =>
        context.ReportWarning(
            node,
            $"Constraint {Describe(constraint)} {where} is not modelled by analysis, so it was not checked.",
            DomainModelDiagnosticCodes.ConstraintUndecided);

    internal static ConstraintVerdict Of(bool satisfied) =>
        satisfied ? ConstraintVerdict.Satisfied : ConstraintVerdict.Violated;

    public static string Describe(Constraint constraint) {
        return ConstraintKinds.Of(constraint)?.Describe(constraint) ?? constraint.GetType().Name;
    }

    internal static string FormatRange(RangeConstraint r) =>
        r.Intervals is { } set
            ? string.Join(" ∪ ", set.Select(i => FormatInterval(i.Minimum, i.Maximum)))
            : FormatInterval(r.Minimum, r.Maximum);

    private static string FormatInterval(object? min, object? max) {
        var lo = min is not null ? FormatValue(min) : "−∞";
        var hi = max is not null ? FormatValue(max) : "+∞";
        return $"range({lo}, {hi})";
    }

    private static string FormatValue(object v) => v switch {
        double d => d == Math.Floor(d) ? d.ToString("F0") : d.ToString("G"),
        _ => v.ToString() ?? "?"
    };

    // ── Individual constraint checks ──────────────────────────────────────

    /// <summary>In any interval of a union (exact), or in the single interval of a plain range.</summary>
    internal static bool IsInRangeUnion(object? value, RangeConstraint range) =>
        range.Intervals is { } set
            ? set.Any(i => IsInRange(value, i.Minimum, i.Maximum))
            : IsInRange(value, range.Minimum, range.Maximum);

    internal static bool IsInRange(object? value, object? min, object? max) {
        if (value is null) return false; // null is never in range
        // Same ordering rule as the merge/join (ConstraintMerge.TryCompare); a value that cannot be
        // ordered against a bound is not in range (fail closed), never coerced or skipped.
        if (min is not null && ConstraintMerge.TryCompare(value, min) is not >= 0) return false;
        if (max is not null && ConstraintMerge.TryCompare(value, max) is not <= 0) return false;
        return true;
    }

    internal static bool IsValidLength(object? value, int minLength, int maxLength) {
        if (value is not string s) return false;
        return s.Length >= minLength && s.Length <= maxLength;
    }

    internal static bool IsValidPattern(object? value, string pattern) {
        if (value is not string s) return false;
        try {
            return Regex.IsMatch(s, pattern);
        }
        catch (RegexParseException) {
            return false;
        }
    }
}