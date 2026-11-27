using System.Collections.Frozen;

using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Negate = Poly.DomainModeling.Ontology.Negate;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The interval of an operator applied to its operand intervals — one immutable row per node kind, so a
/// new node adds a row beside it and never a second evaluator (A4, B6). A node with no row is
/// <c>null</c> (unknown): the transfer never guesses an interval. A two-operand row sits in
/// <see cref="Rows"/>; a one-operand row (unary minus) sits in <see cref="UnaryRows"/>.
/// </summary>
internal static class IntervalTransfers {
    private static readonly FrozenDictionary<Type, Func<ValueRange?, ValueRange?, ValueRange?>> Rows =
        new Dictionary<Type, Func<ValueRange?, ValueRange?, ValueRange?>> {
            [typeof(Add)] = static (left, right) => Combine(left, right, static (l, r) => l.Add(r)),
            [typeof(Subtract)] = static (left, right) => Combine(left, right, static (l, r) => l.Sub(r)),
            [typeof(Multiply)] = static (left, right) => left is null || right is null
                // An unknown operand still multiplies by exactly [0, 0] to 0 (0 · ±∞ = 0).
                ? (IsZero(left) || IsZero(right) ? new ValueRange(0m, 0m) : null)
                : left.Mul(right),
            [typeof(Divide)] = static (left, right) => Quotient(left, right),
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<Type, Func<ValueRange?, ValueRange?>> UnaryRows =
        new Dictionary<Type, Func<ValueRange?, ValueRange?>> {
            [typeof(Negate)] = static range => range?.Negate(),
        }.ToFrozenDictionary();

    /// <summary>The interval of <paramref name="node"/> over <paramref name="operands"/>, or null when the
    /// node's transfer is not modelled.</summary>
    internal static ValueRange? Transfer(DomainExpression node, IReadOnlyList<ValueRange?> operands) {
        if (operands.Count == 1)
            return UnaryRows.TryGetValue(node.GetType(), out var unary) ? unary(operands[0]) : null;
        return operands.Count == 2 && Rows.TryGetValue(node.GetType(), out var row) ? row(operands[0], operands[1]) : null;
    }

    /// <summary>
    /// The interval of a built-in numeric call (#547), or null when its transfer is not modelled. Only the
    /// rows whose result is a sound over-approximation exist: <c>Abs</c> is never negative, and <c>Min</c>/
    /// <c>Max</c> take the hull of their operands. <c>Round</c> has no row: rounding a midpoint away from zero
    /// can leave the operand's interval, so its result is unknown rather than a bound that is wrong.
    /// </summary>
    internal static ValueRange? TransferCall(BuiltinNumericFunction function, IReadOnlyList<ValueRange?> operands) =>
        function switch {
            BuiltinNumericFunction.Abs when operands.Count == 1 => Abs(operands[0]),
            BuiltinNumericFunction.Min when operands.Count == 2 => Min(operands[0], operands[1]),
            BuiltinNumericFunction.Max when operands.Count == 2 => Max(operands[0], operands[1]),
            _ => null,
        };

    /// <summary>The interval of the absolute value: never negative, at most the farther endpoint. An open
    /// endpoint on the side that decides the magnitude leaves the result open, never bounded.</summary>
    private static ValueRange? Abs(ValueRange? operand) {
        if (operand is not { } range) return null;
        if (range.Min is { } min && min >= 0) return range;
        if (range.Max is { } max && max <= 0) {
            if (range.Min is null) return new ValueRange(0m, null).OrNull();
            return new ValueRange(Math.Abs(max), Math.Abs(range.Min.Value));
        }
        if (range.Min is null || range.Max is null) return new ValueRange(0m, null).OrNull();
        try {
            return new ValueRange(0m, Math.Max(Math.Abs(range.Min.Value), Math.Abs(range.Max.Value)));
        }
        catch (OverflowException) {
            // |decimal.MinValue| does not fit a decimal: the upper bound is open, not wrong.
            return new ValueRange(0m, null).OrNull();
        }
    }

    /// <summary>The interval of the smaller operand: the smaller of the lower bounds, the smaller of the upper
    /// bounds. A null endpoint is open, so it never tightens the side it answers.</summary>
    private static ValueRange? Min(ValueRange? left, ValueRange? right) {
        if (left is not { } l || right is not { } r) return null;
        var min = l.Min is { } lMin && r.Min is { } rMin ? Math.Min(lMin, rMin) : (decimal?)null;
        var max = l.Max is { } lMax && r.Max is { } rMax ? Math.Min(lMax, rMax) : l.Max ?? r.Max;
        return new ValueRange(min, max).OrNull();
    }

    /// <summary>The interval of the larger operand: the larger of the lower bounds, the larger of the upper
    /// bounds. An upper bound open on either side leaves the result's upper bound open.</summary>
    private static ValueRange? Max(ValueRange? left, ValueRange? right) {
        if (left is not { } l || right is not { } r) return null;
        var min = l.Min is { } lMin && r.Min is { } rMin ? Math.Max(lMin, rMin) : l.Min ?? r.Min;
        var max = l.Max is { } lMax && r.Max is { } rMax ? Math.Max(lMax, rMax) : (decimal?)null;
        return new ValueRange(min, max).OrNull();
    }

    private static ValueRange? Combine(
        ValueRange? left, ValueRange? right, Func<ValueRange, ValueRange?, ValueRange?> op) =>
        left is null ? null : op(left, right);

    private static bool IsZero(ValueRange? range) => range is { Min: 0m, Max: 0m };

    /// <summary>
    /// The interval of a quotient, or null when it has none: an unknown operand, an open endpoint (a
    /// corner is then uncomputable), or a divisor interval that includes 0 — a hull cannot exclude it,
    /// and division by zero is #502's question, not a range.
    /// <para>
    /// The four corner quotients bound an exact (<c>Decimal</c>) division. A <c>Number</c> division is
    /// C#'s integer division, which truncates toward zero; <c>trunc</c> is monotone, so widening the
    /// exact hull to <c>trunc</c> at a positive lower and a negative upper endpoint is the smallest hull
    /// that holds both answers. Truncating every corner instead would be narrower and unsound: then
    /// <c>5 / 2</c> reads <c>[2, 2]</c> and proves a 2.5 into <c>range(0, 2)</c>. A corner quotient that
    /// decimal rounds outward-widens by one unit in its last place, so an endpoint no exact quotient
    /// reaches is never the one a <c>proven</c> rests on.
    /// </para>
    /// </summary>
    private static ValueRange? Quotient(ValueRange? left, ValueRange? right) {
        if (left is not { Min: { } leftMin, Max: { } leftMax }
            || right is not { Min: { } rightMin, Max: { } rightMax })
            return null;
        if (rightMin <= 0m && rightMax >= 0m) return null;

        var lower = decimal.MaxValue;
        var upper = decimal.MinValue;
        foreach (var (dividend, divisor) in new[] {
                     (leftMin, rightMin), (leftMin, rightMax), (leftMax, rightMin), (leftMax, rightMax) }) {
            if (Corner(dividend, divisor) is not { } corner) return null;
            lower = Math.Min(lower, corner.Low);
            upper = Math.Max(upper, corner.High);
        }
        if (lower > 0m) lower = decimal.Truncate(lower);
        if (upper < 0m) upper = decimal.Truncate(upper);
        return new ValueRange(lower, upper);
    }

    /// <summary>One corner quotient, widened by one unit in its last place on each side when decimal
    /// rounded it; an exact quotient is its own interval. Null when the quotient is beyond decimal's
    /// range: that endpoint is unrepresentable, which is unknown, never a wrapping or a crash.</summary>
    private static (decimal Low, decimal High)? Corner(decimal dividend, decimal divisor) {
        if (QuotientMayNotFit(dividend, divisor)) return null;
        var quotient = dividend / divisor;
        if (IsExactQuotient(dividend, divisor, quotient)) return (quotient, quotient);
        var unit = UnitInLastPlace(quotient);
        return (
            quotient > decimal.MinValue ? quotient - unit : quotient,
            quotient < decimal.MaxValue ? quotient + unit : quotient);
    }

    /// <summary>True when the quotient may not fit <see cref="decimal"/>, which the row answers as
    /// unknown instead of letting the division throw. The screen is a <see cref="double"/> ratio compared
    /// against <c>1e28</c> — a representability test, never a bound: it sits about eight times below
    /// <see cref="decimal.MaxValue"/> (~7.9e28), so every quotient it lets through is representable and a
    /// true ratio above the limit cannot slip under it (double is accurate to ~1e-16 relative here).</summary>
    private static bool QuotientMayNotFit(decimal dividend, decimal divisor) {
        var ratio = Math.Abs((double)dividend / (double)divisor);
        return !double.IsFinite(ratio) || ratio >= 1e28;
    }

    /// <summary><c>quotient · divisor == dividend</c> exactly; false when the product itself is inexact,
    /// which keeps the answer on the widening side.</summary>
    private static bool IsExactQuotient(decimal dividend, decimal divisor, decimal quotient) =>
        ExactNumber.TryFrom(dividend) is { } d
        && ExactNumber.TryFrom(divisor) is { } v
        && ExactNumber.TryFrom(quotient) is { } q
        && q.TryMultiply(v) is { } product
        && product.Equals(d);

    /// <summary>One unit in the last place of <paramref name="value"/>'s decimal scale (<c>10^-scale</c>),
    /// which bounds decimal's division rounding at that scale.</summary>
    private static decimal UnitInLastPlace(decimal value) =>
        new(1, 0, 0, false, (byte)((decimal.GetBits(value)[3] >> 16) & 0xFF));
}