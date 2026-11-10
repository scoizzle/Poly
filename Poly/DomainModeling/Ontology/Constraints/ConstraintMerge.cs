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
        return ConstraintKinds.Of(self)?.Merge(self, other) ?? ConstraintMergeResult.Unmergeable;
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
        return ConstraintKinds.Of(a)?.Identical(a, b) ?? false;
    }

    /// <summary>
    /// The default expressions a <c>default(...)</c> may hold. Compared structurally, ignoring
    /// node identity and position, so a library-contributed clock (<c>Today</c>) or date
    /// arithmetic (<c>Today - 1 Days</c>) compares like any other expression.
    /// </summary>
    internal static bool SameDefault(DomainExpression a, DomainExpression b) =>
        DomainExpressionStructuralComparer.Equal(a, b);

    /// <summary>
    /// The net constraint, or <c>null</c> when the intersection is empty or unmergeable.
    /// This is the convenience for callers where the difference does not matter (an
    /// implication test, a projection that drops a constraint). Use
    /// <see cref="MergeResult"/> when "cannot decide" must not read as "empty".
    /// </summary>
    public static Constraint? Merge(this Constraint self, Constraint other) =>
        self.MergeResult(other).Net;

    /// <summary>
    /// The JOIN (least upper bound) of two constraints of the same type: the strongest single
    /// constraint that holds whenever EITHER input holds. This is the operation for merging the
    /// facts of two control-flow arms. It is NOT <see cref="MergeResult"/>, which is the
    /// intersection (meet): the meet keeps a narrowing true on only one arm and calls two
    /// disjoint ranges Empty, both wrong for a merge point. <c>null</c> means "no fact survives"
    /// (the axis is dropped, top): different types, a kind with no join rule, or inputs that
    /// share no representable common fact. Dropping is always the sound direction.
    /// Rules: Range/Length = hull (null bound = unbounded); Required/Optional = survive (both
    /// arms have it); Equality/Pattern/Unique/Default and every other kind = survive only when
    /// <see cref="Identical"/>.
    /// </summary>
    public static Constraint? Join(this Constraint self, Constraint other) {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(other);
        if (self.GetType() != other.GetType())
            return null;
        return ConstraintKinds.Of(self)?.Join(self, other);
    }

    /// <summary>
    /// Join of two ranges: the normalised union of their intervals (#367 A-4), with the hull on
    /// <see cref="RangeConstraint.Minimum"/>/<see cref="RangeConstraint.Maximum"/>. A bound missing
    /// on either side is unbounded in the hull; a hull unbounded on both sides, or bounds that
    /// cannot be ordered, drop the whole axis (null) instead of throwing.
    /// </summary>
    internal static RangeConstraint? JoinRange(RangeConstraint a, RangeConstraint b) {
        var merged = NormaliseIntervals([.. IntervalsOf(a), .. IntervalsOf(b)]);
        if (merged is null) return null;
        var hull = FromIntervals(merged);
        return hull.Minimum is null && hull.Maximum is null ? null : hull;
    }

    private static List<RangeInterval> IntervalsOf(RangeConstraint r) =>
        r.Intervals is { } set ? [.. set] : [new RangeInterval(r.Minimum, r.Maximum)];

    private static RangeConstraint FromIntervals(List<RangeInterval> normalised) {
        var min = normalised[0].Minimum;
        var max = normalised[^1].Maximum;
        return normalised.Count == 1
            ? new RangeConstraint(min, max)
            : new RangeConstraint(min, max) { Intervals = new RangeIntervals(normalised) };
    }

    /// <summary>
    /// Sorts by lower bound and merges overlapping or touching intervals. Null when two bounds
    /// cannot be ordered. A null minimum sorts first; a null maximum absorbs everything after it.
    /// </summary>
    private static List<RangeInterval>? NormaliseIntervals(List<RangeInterval> items) {
        var failed = false;
        items.Sort((x, y) => {
            if (x.Minimum is null) return y.Minimum is null ? 0 : -1;
            if (y.Minimum is null) return 1;
            if (TryCompare(x.Minimum, y.Minimum) is { } c) return c;
            failed = true;
            return 0;
        });
        if (failed) return null;
        var result = new List<RangeInterval>();
        var current = items[0];
        for (var i = 1; i < items.Count; i++) {
            var next = items[i];
            bool overlaps;
            if (current.Maximum is null || next.Minimum is null) overlaps = true;
            else if (TryCompare(next.Minimum, current.Maximum) is { } c) overlaps = c <= 0;
            else return null;
            if (!overlaps) {
                result.Add(current);
                current = next;
                continue;
            }
            object? max;
            if (current.Maximum is null || next.Maximum is null) max = null;
            else if (TryCompare(current.Maximum, next.Maximum) is { } c) max = c >= 0 ? current.Maximum : next.Maximum;
            else return null;
            current = new RangeInterval(current.Minimum, max);
        }
        result.Add(current);
        return result;
    }

    /// <summary>Intersection of two ranges (the tighter lower bound and the tighter upper bound).</summary>
    public static RangeConstraint? MergeRange(RangeConstraint a, RangeConstraint b) {
        var result = MergeRangeResult(a, b);
        if (result.Kind == ConstraintMergeKind.Unmergeable)
            throw new InvalidOperationException(
                "Range bounds are not comparable; use MergeResult to get an Unmergeable outcome.");
        return result.Net as RangeConstraint;
    }

    internal static ConstraintMergeResult MergeRangeResult(RangeConstraint a, RangeConstraint b) {
        if (a.Intervals is null && b.Intervals is null)
            return MergeSingleInterval(a, b);
        // Meet distributes over the union: (u Ai) n (u Bj) = u (Ai n Bj). Exact emptiness: no
        // surviving pair means no value satisfies both.
        var survivors = new List<RangeInterval>();
        foreach (var x in IntervalsOf(a)) {
            foreach (var y in IntervalsOf(b)) {
                var pair = MergeSingleInterval(new RangeConstraint(x.Minimum, x.Maximum), new RangeConstraint(y.Minimum, y.Maximum));
                if (pair.Kind == ConstraintMergeKind.Unmergeable) return ConstraintMergeResult.Unmergeable;
                if (pair.Net is RangeConstraint r)
                    survivors.Add(new RangeInterval(r.Minimum, r.Maximum));
            }
        }
        if (survivors.Count == 0) return ConstraintMergeResult.Empty;
        return NormaliseIntervals(survivors) is { } normalised
            ? ConstraintMergeResult.Of(FromIntervals(normalised))
            : ConstraintMergeResult.Unmergeable;
    }

    private static ConstraintMergeResult MergeSingleInterval(RangeConstraint a, RangeConstraint b) {
        object? min = a.Minimum;
        if (b.Minimum is not null) {
            if (min is null) min = b.Minimum;
            else if (TryCompare(min, b.Minimum) is not { } c) return ConstraintMergeResult.Unmergeable;
            else if (c < 0) min = b.Minimum;
        }
        object? max = a.Maximum;
        if (b.Maximum is not null) {
            if (max is null) max = b.Maximum;
            else if (TryCompare(max, b.Maximum) is not { } c) return ConstraintMergeResult.Unmergeable;
            else if (c > 0) max = b.Maximum;
        }
        if (min is not null && max is not null) {
            if (TryCompare(min, max) is not { } c) return ConstraintMergeResult.Unmergeable;
            if (c > 0) return ConstraintMergeResult.Empty; // min > max — unsatisfiable
        }
        return ConstraintMergeResult.Of(new RangeConstraint(min, max));
    }

    /// <summary>Intersection of two length bounds.</summary>
    public static LengthConstraint? MergeLength(LengthConstraint a, LengthConstraint b) {
        var min = Math.Max(a.MinLength, b.MinLength);
        var max = Math.Min(a.MaxLength, b.MaxLength);
        return min > max ? null : new LengthConstraint(min, max);
    }

    /// <summary>
    /// Orders two range bounds, or <c>null</c> when they cannot be ordered. Same-type bounds use
    /// that type's own ordering (decimal, long, DateTime... never rounded through double).
    /// Two different numeric types fall back to double, as before; a bound of any
    /// non-numeric type against a different type is not comparable (cannot decide).
    /// </summary>
    internal static int? TryCompare(object x, object y) {
        if (x.GetType() == y.GetType())
            return x is IComparable c ? c.CompareTo(y) : null;
        if (IsNumeric(x) && IsNumeric(y))
            return Convert.ToDouble(x).CompareTo(Convert.ToDouble(y));
        return null;
    }

    private static bool IsNumeric(object v) => v is sbyte or byte or short or ushort or int
        or uint or long or ulong or float or double or decimal;
}