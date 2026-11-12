using Poly.DomainModeling.Analysis;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// V-05b S1 unit rows for the exact interval core (B4, F5). Every op is pinned at the edge that the
/// double interval could not represent: an exact result that does not fit a decimal opens its endpoint
/// instead of rounding (OQ-6), <c>0 · ±∞</c> is 0, an open operand keeps the same-side endpoint open,
/// and a strict bound on an integral axis is exact while a decimal axis stays inclusive.
/// </summary>
public class ExactIntervalTests {
    private static ValueRange Range(decimal? min, decimal? max) => new(min, max);

    // ── Mul: an overflowing bound opens its side (B4 sign rule) ──────────

    [Test]
    public async Task Mul_OverflowingBound_BecomesOpenEndpoint() {
        var product = Range(0m, decimal.MaxValue).Mul(Range(0m, 2m));

        await Assert.That(product).IsNotNull();
        await Assert.That(product!.Min).IsEqualTo(0m);
        await Assert.That(product.Max).IsNull();
    }

    [Test]
    public async Task Mul_NegativeOverflowingBound_BecomesOpenEndpoint() {
        var product = Range(-decimal.MaxValue, 0m).Mul(Range(0m, 2m));

        await Assert.That(product).IsNotNull();
        await Assert.That(product!.Min).IsNull();
        await Assert.That(product.Max).IsEqualTo(0m);
    }

    /// <summary>T10b: an operand that is exactly <c>[0, 0]</c> makes the product 0 even when the other
    /// side is open — <c>0 · ±∞ = 0</c>, never unknown.</summary>
    [Test]
    public async Task Mul_OpenOperandByZero_IsZero() {
        var product = Range(0m, 0m).Mul(Range(null, 5m));

        await Assert.That(product).IsNotNull();
        await Assert.That(product!.Min).IsEqualTo(0m);
        await Assert.That(product.Max).IsEqualTo(0m);
    }

    [Test]
    public async Task Mul_UnknownOperand_IsUnknown() {
        await Assert.That(Range(0m, 10m).Mul(null)).IsNull();
    }

    // ── Add / Sub: an open operand or a null try-result opens that side ──

    [Test]
    public async Task Add_OverflowingBound_BecomesOpenEndpoint() {
        var sum = Range(0m, decimal.MaxValue).Add(Range(0m, 2m));

        await Assert.That(sum).IsNotNull();
        await Assert.That(sum!.Min).IsEqualTo(0m);
        await Assert.That(sum.Max).IsNull();
    }

    [Test]
    public async Task Add_NegativeOverflowingBound_BecomesOpenEndpoint() {
        var sum = Range(-decimal.MaxValue, 0m).Add(Range(-2m, 0m));

        await Assert.That(sum).IsNotNull();
        await Assert.That(sum!.Min).IsNull();
        await Assert.That(sum.Max).IsEqualTo(0m);
    }

    /// <summary>T12: an open operand endpoint opens that same side of the result; it is not "unknown".</summary>
    [Test]
    public async Task Add_OpenOperand_YieldsOpenEndpointNotUnknown() {
        var sum = Range(null, 10m).Add(Range(1m, 2m));

        await Assert.That(sum).IsNotNull();
        await Assert.That(sum!.Min).IsNull();
        await Assert.That(sum.Max).IsEqualTo(12m);
    }

    [Test]
    public async Task Sub_ClosedOperands_AreExact() {
        var difference = Range(0m, 10m).Sub(Range(2m, 3m));

        await Assert.That(difference).IsNotNull();
        await Assert.That(difference!.Min).IsEqualTo(-3m);
        await Assert.That(difference.Max).IsEqualTo(8m);
    }

    [Test]
    public async Task Sub_OpenOperand_OpensTheFlippedSide() {
        var difference = Range(null, 10m).Sub(Range(2m, 3m));

        await Assert.That(difference).IsNotNull();
        await Assert.That(difference!.Min).IsNull();
        await Assert.That(difference.Max).IsEqualTo(8m);
    }

    [Test]
    public async Task Add_UnknownOperand_IsUnknown() {
        await Assert.That(Range(0m, 10m).Add(null)).IsNull();
        await Assert.That(Range(0m, 10m).Sub(null)).IsNull();
    }

    // ── Below / Above: strict bounds (F5) ────────────────────────────────

    [Test]
    public async Task StrictLessThan_Number_IsExactMinusOne() {
        await Assert.That(Range(null, null).Below(100m, integral: true)!.Max).IsEqualTo(99m);
        await Assert.That(Range(null, null).Below(99.5m, integral: true)!.Max).IsEqualTo(99m);
    }

    [Test]
    public async Task StrictLessThan_Decimal_StaysInclusive() {
        await Assert.That(Range(null, null).Below(99.5m, integral: false)!.Max).IsEqualTo(99.5m);
    }

    [Test]
    public async Task StrictGreaterThan_Number_IsExactPlusOne() {
        await Assert.That(Range(null, null).Above(100m, integral: true)!.Min).IsEqualTo(101m);
        await Assert.That(Range(null, null).Above(99.5m, integral: true)!.Min).IsEqualTo(100m);
    }

    [Test]
    public async Task StrictGreaterThan_Decimal_StaysInclusive() {
        await Assert.That(Range(null, null).Above(99.5m, integral: false)!.Min).IsEqualTo(99.5m);
    }

    [Test]
    public async Task Below_KeepsTheTighterExistingEndpoint() {
        var narrowed = Range(0m, 50m).Below(100m, integral: true);

        await Assert.That(narrowed).IsNotNull();
        await Assert.That(narrowed!.Min).IsEqualTo(0m);
        await Assert.That(narrowed.Max).IsEqualTo(50m);
    }

    [Test]
    public async Task Above_KeepsTheTighterExistingEndpoint() {
        var narrowed = Range(50m, 200m).Above(10m, integral: true);

        await Assert.That(narrowed).IsNotNull();
        await Assert.That(narrowed!.Min).IsEqualTo(50m);
        await Assert.That(narrowed.Max).IsEqualTo(200m);
    }

    [Test]
    public async Task Below_EmptyRange_IsNoRange() {
        await Assert.That(Range(5m, 10m).Below(5m, integral: true)).IsNull();
    }

    [Test]
    public async Task Above_EmptyRange_IsNoRange() {
        await Assert.That(Range(5m, 10m).Above(10m, integral: true)).IsNull();
    }

    // ── Join / TryMeet ──────────────────────────────────────────────────

    [Test]
    public async Task Join_TakesTheHull() {
        var joined = Range(0m, 10m).Join(Range(5m, 20m));

        await Assert.That(joined).IsNotNull();
        await Assert.That(joined!.Min).IsEqualTo(0m);
        await Assert.That(joined.Max).IsEqualTo(20m);
    }

    [Test]
    public async Task Join_OpenEndpoint_StaysOpen() {
        var joined = Range(null, 10m).Join(Range(5m, 20m));

        await Assert.That(joined).IsNotNull();
        await Assert.That(joined!.Min).IsNull();
        await Assert.That(joined.Max).IsEqualTo(20m);
    }

    [Test]
    public async Task Join_UnknownOperand_IsUnknown() {
        await Assert.That(Range(0m, 10m).Join(null)).IsNull();
    }

    [Test]
    public async Task TryMeet_OverlappingRanges_IsTheIntersection() {
        var met = Range(0m, 10m).TryMeet(Range(5m, 20m), out var meet);

        await Assert.That(met).IsTrue();
        await Assert.That(meet.Min).IsEqualTo(5m);
        await Assert.That(meet.Max).IsEqualTo(10m);
    }

    [Test]
    public async Task TryMeet_DisjointRanges_IsEmpty() {
        await Assert.That(Range(0m, 10m).TryMeet(Range(11m, 20m), out _)).IsFalse();
    }

    [Test]
    public async Task TryMeet_UnknownOperand_IsEmpty() {
        await Assert.That(Range(0m, 10m).TryMeet(null, out _)).IsFalse();
    }

    // ── The range itself ────────────────────────────────────────────────

    [Test]
    public async Task ToRangeConstraint_BothEndpointsOpen_IsNoConstraint() {
        await Assert.That(Range(null, null).ToRangeConstraint()).IsNull();
        await Assert.That(Range(0m, 10m).ToRangeConstraint()!.Minimum).IsEqualTo(0m);
    }
}