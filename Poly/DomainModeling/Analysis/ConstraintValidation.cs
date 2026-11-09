using Poly.Analysis;
using Poly.DomainModeling.Ontology;

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
        return constraint switch {
            RequiredConstraint => Of(value is not null),
            OptionalConstraint => ConstraintVerdict.Satisfied, // optional is the opt-out of requiredness; any value (incl. null) is legal
            RangeConstraint r => Of(IsInRange(value, r.Minimum, r.Maximum)),
            LengthConstraint l => Of(IsValidLength(value, l.MinLength, l.MaxLength)),
            PatternConstraint p => Of(IsValidPattern(value, p.Pattern)),
            EqualityConstraint eq => Of(Equals(value, eq.ExpectedValue)),
            UniqueConstraint => ConstraintVerdict.Satisfied, // cannot validate statically; requires instance set
            DefaultValueConstraint => ConstraintVerdict.Satisfied, // default-expression does not constrain assignment
            _ => ConstraintVerdict.Undecided
        };
    }

    /// <summary>Reports the shared DMCS003 warning for a constraint kind analysis does not model.</summary>
    public static void ReportUndecided(AnalysisContext context, Node node, Constraint constraint, string where) =>
        context.ReportWarning(
            node,
            $"Constraint {Describe(constraint)} {where} is not modelled by analysis, so it was not checked.",
            DomainModelDiagnosticCodes.ConstraintUndecided);

    private static ConstraintVerdict Of(bool satisfied) =>
        satisfied ? ConstraintVerdict.Satisfied : ConstraintVerdict.Violated;

    public static string Describe(Constraint constraint) {
        return constraint switch {
            RequiredConstraint => "required (non-null)",
            OptionalConstraint => "optional",
            RangeConstraint r => FormatRange(r),
            LengthConstraint l => $"length({l.MinLength}, {l.MaxLength})",
            PatternConstraint p => $"pattern({p.Pattern})",
            EqualityConstraint eq => $"== {eq.ExpectedValue}",
            UniqueConstraint => "unique",
            DefaultValueConstraint => "default(...)",
            _ => constraint.GetType().Name
        };
    }

    private static string FormatRange(RangeConstraint r) {
        var lo = r.Minimum is not null ? FormatValue(r.Minimum) : "−∞";
        var hi = r.Maximum is not null ? FormatValue(r.Maximum) : "+∞";
        return $"range({lo}, {hi})";
    }

    private static string FormatValue(object v) => v switch {
        double d => d == Math.Floor(d) ? d.ToString("F0") : d.ToString("G"),
        _ => v.ToString() ?? "?"
    };

    // ── Individual constraint checks ──────────────────────────────────────

    private static bool IsInRange(object? value, object? min, object? max) {
        if (value is null) return false; // null is never in range
        var d = ToDouble(value);
        if (d is null) return false;

        if (min is not null && ToDouble(min) is double lo && d.Value < lo) return false;
        if (max is not null && ToDouble(max) is double hi && d.Value > hi) return false;
        return true;
    }

    private static bool IsValidLength(object? value, int minLength, int maxLength) {
        if (value is not string s) return false;
        return s.Length >= minLength && s.Length <= maxLength;
    }

    private static bool IsValidPattern(object? value, string pattern) {
        if (value is not string s) return false;
        try {
            return Regex.IsMatch(s, pattern);
        }
        catch (RegexParseException) {
            return false;
        }
    }

    private static double? ToDouble(object? value) {
        try { return Convert.ToDouble(value); }
        catch { return null; }
    }
}