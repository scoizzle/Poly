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
/// </remarks>
public sealed record ValueRange(decimal? Min, decimal? Max) {
    /// <summary>The hull of this range and <paramref name="other"/>, or null when either side is unknown.</summary>
    internal ValueRange? Join(ValueRange? other) => null;

    /// <summary>The intersection with <paramref name="other"/>; false when it is empty or either side is
    /// unknown.</summary>
    internal bool TryMeet(ValueRange? other, out ValueRange meet) {
        meet = this;
        return false;
    }

    /// <summary>This range restricted to values strictly below <paramref name="bound"/>. On an integral
    /// axis the strict bound is exact (<c>ceil(v) − 1</c>); on a decimal axis the endpoint stays
    /// inclusive, which over-approximates soundly (F5).</summary>
    internal ValueRange? Below(decimal bound, bool integral) => null;

    /// <summary>This range restricted to values strictly above <paramref name="bound"/>. On an integral
    /// axis the strict bound is exact (<c>floor(v) + 1</c>); on a decimal axis the endpoint stays
    /// inclusive (F5).</summary>
    internal ValueRange? Above(decimal bound, bool integral) => null;

    /// <summary>The interval of the sum, or null when either operand is unknown or the result is open on
    /// both sides (B4).</summary>
    internal ValueRange? Add(ValueRange? other) => null;

    /// <summary>The interval of the difference, or null when either operand is unknown.</summary>
    internal ValueRange? Sub(ValueRange? other) => null;

    /// <summary>The interval of the product, or null when either operand is unknown (B4).</summary>
    internal ValueRange? Mul(ValueRange? other) => null;

    /// <summary>The same interval as a <see cref="RangeConstraint"/>, or null when both endpoints are open
    /// (which carries no information, never a <c>range(−∞, +∞)</c>).</summary>
    internal RangeConstraint? ToRangeConstraint() =>
        Min is null && Max is null ? null : new RangeConstraint(Min, Max);
}
