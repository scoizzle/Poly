using System.Text.RegularExpressions;

using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Static helpers for checking whether a value satisfies a constraint.
/// Used at analysis time to validate literal assignments and parameter-propagation chains.
/// </summary>
internal static class ConstraintValidation {
    public static bool IsSatisfiedBy(Constraint constraint, object? value) {
        ArgumentNullException.ThrowIfNull(constraint);
        return constraint switch {
            RequiredConstraint => value is not null,
            RangeConstraint r => IsInRange(value, r.Minimum, r.Maximum),
            LengthConstraint l => IsValidLength(value, l.MinLength, l.MaxLength),
            PatternConstraint p => IsValidPattern(value, p.Pattern),
            EqualityConstraint eq => Equals(value, eq.ExpectedValue),
            UniqueConstraint => true, // cannot validate statically; requires instance set
            DefaultValueConstraint => true, // default-expression does not constrain assignment
            _ => true  // unknown constraint type — pass
        };
    }

    public static string Describe(Constraint constraint) {
        return constraint switch {
            RequiredConstraint => "required (non-null)",
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