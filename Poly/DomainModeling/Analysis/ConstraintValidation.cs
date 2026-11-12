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

    /// <summary>The flow-side default for a kind that has no decision: never Satisfied (fail closed).</summary>
    internal static ConstraintVerdict Undecided(Constraint constraint, AbstractValue value) =>
        ConstraintVerdict.Undecided;

    /// <summary>
    /// Decides whether a non-literal <paramref name="value"/> can satisfy <paramref name="constraint"/>.
    /// An unregistered kind is Undecided (fail closed), exactly like <see cref="Evaluate"/>.
    /// </summary>
    internal static ConstraintVerdict Decide(Constraint constraint, AbstractValue value) {
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(value);
        return ConstraintKinds.Of(constraint)?.Decide(constraint, value) ?? ConstraintVerdict.Undecided;
    }

    /// <summary>
    /// Whether a non-literal write must be verified against <paramref name="constraint"/>. An unregistered
    /// kind is relevant (fail closed): a kind nobody models warns instead of being skipped.
    /// </summary>
    internal static bool IsWriteRelevant(Constraint constraint) {
        ArgumentNullException.ThrowIfNull(constraint);
        return ConstraintKinds.Of(constraint)?.WriteRelevant ?? true;
    }

    /// <summary>Reports the shared DMCS003 warning for a constraint kind analysis does not model.</summary>
    public static void ReportUndecided(AnalysisContext context, Node node, Constraint constraint, string where) =>
        context.ReportWarning(
            node,
            $"Constraint {Describe(constraint)} {where} is not modelled by analysis, so it was not checked.",
            DomainModelDiagnosticCodes.ConstraintUndecided);

    /// <summary>
    /// Decides "<paramref name="self"/>'s value <paramref name="relation"/> the other value" from the
    /// two properties' ranges (hulls; null range or null bound = unbounded). Computes which orderings
    /// are possible: none allowed by the relation → Violated; every possible ordering allowed →
    /// Satisfied; anything else, including bounds that cannot be ordered, → Undecided (fail closed).
    /// </summary>
    internal static ConstraintVerdict DecideRelation(
        RelationalConstraint relation, RangeConstraint? self, RangeConstraint? other) {
        ArgumentNullException.ThrowIfNull(relation);
        // a < b is possible unless a's smallest value is >= b's largest; unknown/unorderable = possible.
        static bool Possible(object? low, object? high, bool strict) =>
            low is null || high is null || ConstraintMerge.TryCompare(low, high) is not { } c
                || (strict ? c < 0 : c <= 0);
        var possible = RelationalOrdering.None;
        if (Possible(self?.Minimum, other?.Maximum, strict: true)) possible |= RelationalOrdering.Less;
        if (Possible(other?.Minimum, self?.Maximum, strict: true)) possible |= RelationalOrdering.Greater;
        if (Possible(self?.Minimum, other?.Maximum, strict: false)
            && Possible(other?.Minimum, self?.Maximum, strict: false)) possible |= RelationalOrdering.Equal;
        if ((possible & relation.Orderings) == RelationalOrdering.None) return ConstraintVerdict.Violated;
        return (possible & ~relation.Orderings) == RelationalOrdering.None
            ? ConstraintVerdict.Satisfied
            : ConstraintVerdict.Undecided;
    }

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
        return PatternSemantics.TryMatch(s, pattern);
    }
}