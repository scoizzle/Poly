namespace Poly.DomainModeling.Constraints;

/// <summary>
/// Combines two constraints of the same type into the net (more restrictive) version of
/// that constraint type — the intersection. Used by the invariant model so combinations
/// are not just numeric ranges: range, length, enum, required, equality, and pattern all
/// merge to a net constraint. Returns null when the types differ, the merge is not
/// statically expressible, or the intersection is empty (unsatisfiable).
/// </summary>
public static class ConstraintMerge {
    /// <summary>Merges <paramref name="self"/> and <paramref name="other"/> (same type) into
    /// the net constraint, or null when unsatisfiable / not mergeable.</summary>
    public static Constraint? Merge(this Constraint self, Constraint other) {
        if (self.GetType() != other.GetType()) return null;
        return (self, other) switch {
            (RangeConstraint a, RangeConstraint b) => MergeRange(a, b),
            (LengthConstraint a, LengthConstraint b) => MergeLength(a, b),
            (EnumConstraint a, EnumConstraint b) => MergeEnum(a, b),
            (RequiredConstraint, RequiredConstraint) => new RequiredConstraint(),
            (EqualityConstraint a, EqualityConstraint b) =>
                Equals(a.ExpectedValue, b.ExpectedValue) ? a : null,
            (PatternConstraint a, PatternConstraint b) =>
                string.Equals(a.Pattern, b.Pattern, StringComparison.Ordinal) ? a : null,
            _ => null,
        };
    }

    /// <summary>Intersection of two ranges (the tighter lower bound and the tighter upper bound).</summary>
    public static RangeConstraint? MergeRange(RangeConstraint a, RangeConstraint b) {
        var min = (a.Minimum, b.Minimum) switch {
            (not null, not null) when Compare(a.Minimum, b.Minimum) >= 0 => a.Minimum,
            (not null, not null) => b.Minimum,
            (not null, _) => a.Minimum,
            (_, not null) => b.Minimum,
            _ => null,
        };
        var max = (a.Maximum, b.Maximum) switch {
            (not null, not null) when Compare(a.Maximum, b.Maximum) <= 0 => a.Maximum,
            (not null, not null) => b.Maximum,
            (not null, _) => a.Maximum,
            (_, not null) => b.Maximum,
            _ => null,
        };
        if (min is not null && max is not null && Compare(min, max) > 0)
            return null; // min > max — unsatisfiable
        return new RangeConstraint(min, max);
    }

    /// <summary>Intersection of two length bounds.</summary>
    public static LengthConstraint? MergeLength(LengthConstraint a, LengthConstraint b) {
        var min = Math.Max(a.MinLength, b.MinLength);
        var max = Math.Min(a.MaxLength, b.MaxLength);
        return min > max ? null : new LengthConstraint(min, max);
    }

    /// <summary>Intersection of two enum member sets.</summary>
    public static EnumConstraint? MergeEnum(EnumConstraint a, EnumConstraint b) {
        var members = a.Members
            .Where(am => b.Members.Any(bm => string.Equals(am.Name, bm.Name, StringComparison.Ordinal)))
            .ToList();
        return members.Count == 0 ? null : new EnumConstraint(members);
    }

    private static int Compare(object x, object y) =>
        Convert.ToDouble(x).CompareTo(Convert.ToDouble(y));
}