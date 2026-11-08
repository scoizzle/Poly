namespace Poly.DomainModeling.Ontology.Constraints;

/// <summary>The outcome of intersecting two constraints of the same type.</summary>
public enum ConstraintMergeKind {
    /// <summary>A constraint that represents the intersection.</summary>
    Net,

    /// <summary>
    /// The intersection exists but is not representable as one constraint of this kind
    /// (different types, or a kind with no merge rule). Not bottom.
    /// </summary>
    Unmergeable,

    /// <summary>No value satisfies both constraints. Bottom.</summary>
    Empty,
}

/// <summary>
/// The three-way result of <see cref="ConstraintMerge.MergeResult"/>. The distinction matters:
/// "cannot decide" must never read as "empty", or a legal model is rejected as unsatisfiable.
/// </summary>
public readonly record struct ConstraintMergeResult(ConstraintMergeKind Kind, Constraint? Net) {
    public static ConstraintMergeResult Of(Constraint net) =>
        new(ConstraintMergeKind.Net, net);

    public static readonly ConstraintMergeResult Unmergeable =
        new(ConstraintMergeKind.Unmergeable, null);

    public static readonly ConstraintMergeResult Empty =
        new(ConstraintMergeKind.Empty, null);

    public bool IsNet => Kind == ConstraintMergeKind.Net;

    public bool IsEmpty => Kind == ConstraintMergeKind.Empty;
}

/// <summary>
/// Combines two constraints of the same type into the net (more restrictive) version of
/// that constraint type — the intersection. Used by the invariant model so combinations
/// are not just numeric ranges: range, length, required, optional, equality, and
/// pattern all merge to a net constraint.
/// </summary>
public static class ConstraintMerge {
    /// <summary>
    /// Intersects <paramref name="self"/> and <paramref name="other"/> (same type) into a
    /// three-way result. <see cref="ConstraintMergeKind.Net"/> carries the net constraint;
    /// <see cref="ConstraintMergeKind.Empty"/> means no value satisfies both;
    /// <see cref="ConstraintMergeKind.Unmergeable"/> means the intersection is not one
    /// constraint of this kind (different types, or a kind with no merge rule).
    /// </summary>
    public static ConstraintMergeResult MergeResult(this Constraint self, Constraint other) {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(other);
        if (self.GetType() != other.GetType())
            return ConstraintMergeResult.Unmergeable;
        return (self, other) switch {
            (RangeConstraint a, RangeConstraint b) =>
                MergeRange(a, b) is { } range
                    ? ConstraintMergeResult.Of(range)
                    : ConstraintMergeResult.Empty,
            (LengthConstraint a, LengthConstraint b) =>
                MergeLength(a, b) is { } length
                    ? ConstraintMergeResult.Of(length)
                    : ConstraintMergeResult.Empty,
            (RequiredConstraint, RequiredConstraint) =>
                ConstraintMergeResult.Of(new RequiredConstraint()),
            (OptionalConstraint, OptionalConstraint) =>
                ConstraintMergeResult.Of(new OptionalConstraint()),
            (EqualityConstraint a, EqualityConstraint b) =>
                Equals(a.ExpectedValue, b.ExpectedValue)
                    ? ConstraintMergeResult.Of(a)
                    : ConstraintMergeResult.Empty,
            (PatternConstraint a, PatternConstraint b) =>
                string.Equals(a.Pattern, b.Pattern, StringComparison.Ordinal)
                    ? ConstraintMergeResult.Of(a)
                    : ConstraintMergeResult.Empty,
            (UniqueConstraint a, UniqueConstraint) => ConstraintMergeResult.Of(a),
            (DefaultValueConstraint a, DefaultValueConstraint b) =>
                SameDefault(a.Expression, b.Expression)
                    ? ConstraintMergeResult.Of(a)
                    : ConstraintMergeResult.Empty,
            _ => ConstraintMergeResult.Unmergeable,
        };
    }

    /// <summary>
    /// True when the two constraints say the same thing, ignoring node identity and source
    /// position (both participate in record equality). Used by the idempotent merge arms and
    /// by the evolution layer so adding the same constraint twice is a no-op.
    /// </summary>
    public static bool Identical(Constraint a, Constraint b) {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.GetType() != b.GetType())
            return false;
        return (a, b) switch {
            (RangeConstraint x, RangeConstraint y) =>
                Equals(x.Minimum, y.Minimum) && Equals(x.Maximum, y.Maximum),
            (LengthConstraint x, LengthConstraint y) =>
                x.MinLength == y.MinLength && x.MaxLength == y.MaxLength,
            (RequiredConstraint, RequiredConstraint) => true,
            (OptionalConstraint, OptionalConstraint) => true,
            (EqualityConstraint x, EqualityConstraint y) => Equals(x.ExpectedValue, y.ExpectedValue),
            (PatternConstraint x, PatternConstraint y) =>
                string.Equals(x.Pattern, y.Pattern, StringComparison.Ordinal),
            (UniqueConstraint, UniqueConstraint) => true,
            (DefaultValueConstraint x, DefaultValueConstraint y) =>
                SameDefault(x.Expression, y.Expression),
            _ => false,
        };
    }

    /// <summary>
    /// The default expressions a <c>default(...)</c> may hold: a literal or a runtime default
    /// name. Compared by value/name, never by record equality.
    /// </summary>
    private static bool SameDefault(DomainExpression a, DomainExpression b) => (a, b) switch {
        (Literal x, Literal y) => Equals(x.Value, y.Value),
        (PropertyAccess x, PropertyAccess y) =>
            string.Equals(x.Name, y.Name, StringComparison.Ordinal),
        (ParameterAccess x, ParameterAccess y) =>
            string.Equals(x.Name, y.Name, StringComparison.Ordinal),
        (UnresolvedDuration x, UnresolvedDuration y) =>
            x.Amount == y.Amount && string.Equals(x.Unit, y.Unit, StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>
    /// The net constraint, or <c>null</c> when the intersection is empty or unmergeable.
    /// This is the convenience for callers where the difference does not matter (an
    /// implication test, a projection that drops a constraint). Use
    /// <see cref="MergeResult"/> when "cannot decide" must not read as "empty".
    /// </summary>
    public static Constraint? Merge(this Constraint self, Constraint other) =>
        self.MergeResult(other).Net;

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

    private static int Compare(object x, object y) =>
        Convert.ToDouble(x).CompareTo(Convert.ToDouble(y));
}