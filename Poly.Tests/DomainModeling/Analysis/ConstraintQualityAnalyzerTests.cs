using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>V-20: range bounds of different numeric types are ordered exactly and never throw.</summary>
public class ConstraintQualityAnalyzerTests {
    private static int UnsatisfiableRangeCount(object minimum, object maximum) {
        var domain = DomainTestFactory.Create("D", [
            new ValueType("V", [new Property("Value", new DomainTypeReference("Number"),
                [new RangeConstraint(minimum, maximum)])], [])]);
        return DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Count(d => d.Code == DomainModelDiagnosticCodes.ConstraintSatisfiability);
    }

    [Test]
    public async Task Analyze_MixedLongUlongRange_DoesNotThrow() {
        var domain = DomainTestFactory.Create("D", [
            new ValueType("V", [new Property("Value", new DomainTypeReference("Number"),
                [new RangeConstraint(0L, ulong.MaxValue)])], [])]);
        Exception? thrown = null;
        try { DomainModelAnalyzer.Analyze(domain); } catch (Exception e) { thrown = e; }
        await Assert.That(thrown?.ToString()).IsNull();
    }

    [Test]
    public async Task Analyze_MixedNumericBounds_ReportsOnlyMinimumAboveMaximum() {
        List<(object Minimum, object Maximum, bool Unsatisfiable)> rows = [
            (0L, ulong.MaxValue, false),
            (ulong.MaxValue, 0L, true),
            (ulong.MaxValue, ulong.MaxValue, false),
            (long.MinValue, long.MaxValue, false),
            (long.MaxValue, long.MinValue, true),
            (long.MaxValue, ulong.MaxValue, false),
            (ulong.MaxValue, long.MaxValue, true),
            (decimal.MaxValue, ulong.MaxValue, true),
            (ulong.MaxValue, decimal.MaxValue, false),
            (decimal.MaxValue, long.MaxValue, true),
            (long.MaxValue, decimal.MaxValue, false),
            (long.MinValue, decimal.MaxValue, false),
            (5L, 5m, false),
            (5m, 5L, false),
            (6L, 5d, true),
            (5d, 5L, false),
            (0.5d, 1L, false),
            (1L, 0.5d, true),
            (ulong.MaxValue, 2.5d, true),
            (9223372036854775808UL, long.MaxValue, true),
            (9223372036854775808UL, 9223372036854775807m, true),
            (long.MaxValue, 9223372036854775808UL, false),
            (9223372036854775808UL, 9223372036854775808m, false),
            // Double bounds are compared within double precision (no exact form for a fractional double):
            // 0.1d and 0.1m are the same double, so neither is reported. NaN orders below every number
            // as minimum, and above every number as maximum, so a NaN maximum is reported.
            (0.1d, 0.1m, false),
            (double.NaN, 1L, false),
            (1L, double.NaN, true),
        ];

        var mismatches = new List<string>();
        foreach (var (minimum, maximum, unsatisfiable) in rows) {
            var count = UnsatisfiableRangeCount(minimum, maximum);
            if (count != (unsatisfiable ? 1 : 0)) {
                mismatches.Add($"{minimum} ({minimum.GetType().Name}) .. {maximum} ({maximum.GetType().Name}) reported {count}");
            }
        }
        await Assert.That(mismatches).IsEmpty();
    }
}