using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// An inclusive numeric interval for one abstract numeric value. A <c>null</c> endpoint is open: it
/// stands for −∞ (<see cref="Min"/>) or +∞ (<see cref="Max"/>).
/// </summary>
/// <remarks>
/// The endpoints are <see cref="decimal"/>, so every <c>long</c> and every <c>decimal</c> bound is exact
/// (OQ-6, #643). An operation whose exact result does not fit is widened to an open endpoint instead of
/// being rounded or wrapped (B4). The operations are internal (B6): only the range itself is public.
/// A range whose two endpoints are both open carries no information and is reported as <c>null</c>,
/// never as a <c>range(−∞, +∞)</c>.
/// </remarks>
public sealed record ValueRange(decimal? Min, decimal? Max) {
    /// <summary>The hull of this range and <paramref name="other"/>, or null when either side is unknown.</summary>
    internal ValueRange? Join(ValueRange? other) {
        if (other is not { } right) return null;
        var min = Min is { } leftMin && right.Min is { } rightMin ? Math.Min(leftMin, rightMin) : (decimal?)null;
        var max = Max is { } leftMax && right.Max is { } rightMax ? Math.Max(leftMax, rightMax) : (decimal?)null;
        return new ValueRange(min, max).OrNull();
    }

    /// <summary>The intersection with <paramref name="other"/>; false when it is empty or either side is
    /// unknown. The meet is verbatim: two unbounded ranges meet in the unbounded range.</summary>
    internal bool TryMeet(ValueRange? other, out ValueRange meet) {
        meet = this;
        if (other is not { } right) return false;
        var min = Min is { } leftMin && right.Min is { } rightMin ? Math.Max(leftMin, rightMin) : Min ?? right.Min;
        var max = Max is { } leftMax && right.Max is { } rightMax ? Math.Min(leftMax, rightMax) : Max ?? right.Max;
        if (min is { } lo && max is { } hi && lo > hi) return false;
        meet = new ValueRange(min, max);
        return true;
    }

    /// <summary>This range restricted to values strictly below <paramref name="bound"/>. On an integral
    /// axis the strict bound is exact (<c>ceil(v) − 1</c>); on a decimal axis the endpoint stays
    /// inclusive, which over-approximates soundly (F5). Refinement only ever tightens the endpoint.</summary>
    internal ValueRange? Below(decimal bound, bool integral) {
        decimal candidate;
        if (integral) {
            if (bound <= decimal.MinValue) return null; // no integral value is strictly below the floor
            candidate = Math.Ceiling(bound) - 1m;
        }
        else {
            candidate = bound;
        }
        var max = Max is { } existing ? Math.Min(existing, candidate) : candidate;
        if (Min is { } min && min > max) return null; // empty: no admissible value is below the bound
        return this with { Max = max };
    }

    /// <summary>This range restricted to values strictly above <paramref name="bound"/>. On an integral
    /// axis the strict bound is exact (<c>floor(v) + 1</c>); on a decimal axis the endpoint stays
    /// inclusive (F5). Refinement only ever tightens the endpoint.</summary>
    internal ValueRange? Above(decimal bound, bool integral) {
        decimal candidate;
        if (integral) {
            if (bound >= decimal.MaxValue) return null; // no integral value is strictly above the ceiling
            candidate = Math.Floor(bound) + 1m;
        }
        else {
            candidate = bound;
        }
        var min = Min is { } existing ? Math.Max(existing, candidate) : candidate;
        if (Max is { } max && min > max) return null; // empty: no admissible value is above the bound
        return this with { Min = min };
    }

    /// <summary>The interval of the sum, or null when either operand is unknown or the result is open on
    /// both sides (B4). An open operand endpoint, or an exact result that does not fit, opens that side.</summary>
    internal ValueRange? Add(ValueRange? other) {
        if (other is not { } right) return null;
        var min = Sum(Min, right.Min, subtract: false);
        var max = Sum(Max, right.Max, subtract: false);
        return new ValueRange(min, max).OrNull();
    }

    /// <summary>The interval of the difference, or null when either operand is unknown. Subtract flips the
    /// sides: the result's lower bound takes the other range's upper bound.</summary>
    internal ValueRange? Sub(ValueRange? other) {
        if (other is not { } right) return null;
        var min = Sum(Min, right.Max, subtract: true);
        var max = Sum(Max, right.Min, subtract: true);
        return new ValueRange(min, max).OrNull();
    }

    /// <summary>
    /// The interval of the product (B4). An operand that is exactly <c>[0, 0]</c> makes the result
    /// <c>[0, 0]</c> — <c>0 · ±∞ = 0</c>. Otherwise all four operand endpoints must exist; a product that
    /// does not fit a decimal keeps its sign, so a positive one opens the upper side (its lower candidate
    /// is 0) and a negative one opens the lower side (its upper candidate is 0).
    /// </summary>
    internal ValueRange? Mul(ValueRange? other) {
        if (other is not { } right) return null;
        if (IsZero || right.IsZero) return new ValueRange(0m, 0m);
        if (Min is not { } leftMin || Max is not { } leftMax
            || right.Min is not { } rightMin || right.Max is not { } rightMax)
            return null;

        var lowerCandidates = new List<decimal>(4);
        var upperCandidates = new List<decimal>(4);
        bool lowerOpen = false, upperOpen = false;
        foreach (var (left, rightEnd) in new[] {
                     (leftMin, rightMin), (leftMin, rightMax), (leftMax, rightMin), (leftMax, rightMax) }) {
            if (Product(left, rightEnd) is { } product) {
                lowerCandidates.Add(product);
                upperCandidates.Add(product);
                continue;
            }
            // An inexact product keeps its sign; the other side of it is 0, which is still admissible.
            if (left >= 0m == rightEnd >= 0m) {
                upperOpen = true;
                lowerCandidates.Add(0m);
            }
            else {
                lowerOpen = true;
                upperCandidates.Add(0m);
            }
        }
        var min = lowerOpen ? (decimal?)null : lowerCandidates.Min();
        var max = upperOpen ? (decimal?)null : upperCandidates.Max();
        return new ValueRange(min, max).OrNull();
    }

    /// <summary>The same interval as a <see cref="RangeConstraint"/>, or null when both endpoints are open
    /// (which carries no information, never a <c>range(−∞, +∞)</c>).</summary>
    internal RangeConstraint? ToRangeConstraint() =>
        Min is null && Max is null ? null : new RangeConstraint(Min, Max);

    /// <summary>True when the range is exactly the single value 0.</summary>
    private bool IsZero => Min == 0m && Max == 0m;

    /// <summary>No range at all: both endpoints open carries no information, so callers see unknown.</summary>
    private ValueRange? OrNull() => Min is null && Max is null ? null : this;

    /// <summary>One result endpoint of an exact sum or difference: absent when an operand endpoint is
    /// open or the exact result does not fit a decimal.</summary>
    private static decimal? Sum(decimal? left, decimal? right, bool subtract) {
        if (ExactNumber.TryFrom(left) is not { } l || ExactNumber.TryFrom(right) is not { } r) return null;
        var result = subtract ? l.TrySubtract(r) : l.TryAdd(r);
        return result?.ToDecimal();
    }

    /// <summary>One exact product, or null when it does not fit a decimal.</summary>
    private static decimal? Product(decimal left, decimal right) =>
        ExactNumber.TryFrom(left) is { } l && ExactNumber.TryFrom(right) is { } r
            ? l.TryMultiply(r)?.ToDecimal()
            : null;
}