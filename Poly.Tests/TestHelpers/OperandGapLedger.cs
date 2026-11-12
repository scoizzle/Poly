namespace Poly.Tests.TestHelpers;

/// <summary>
/// One known operand-agreement gap: the lowered-tree row (<see cref="Kind"/>, <see cref="Case"/>) whose
/// analysis does not do what the row expects, the exact outcome it produces today
/// (<see cref="Observed"/>), and the issue and slice (<see cref="Owner"/>, a slice id) that fix it.
/// The fixing slice deletes the entry; a stale entry is a failure, never a narrowing.
/// </summary>
public sealed record KnownOperandGap(int Issue, string Owner, string Kind, string Case, string Observed);

/// <summary>Every known operand-agreement gap, sorted by <c>(Kind, Case)</c>.</summary>
public static class KnownOperandGaps {
    public static IReadOnlyList<KnownOperandGap> All { get; } = [];
}

/// <summary>
/// Adapts operand-gap rows to <see cref="ParityDifference"/> so <see cref="ParityDifferenceSet.Compare"/>
/// is the one comparison: <c>Step</c> is the kind, <c>Field</c> is <c>Kind.Case</c> (a case name is only
/// unique within its kind), <c>Simulate</c> is the observed outcome.
/// </summary>
public static class OperandGapRatchet {
    public static IReadOnlyList<ParityDrift> Drift(
        IEnumerable<KnownOperandGap> ledger, IEnumerable<(string Kind, string Case, string Observed)> live) =>
        ParityDifferenceSet.Compare(
            [.. ledger.Select(gap => Row(gap.Kind, gap.Case, gap.Observed))],
            [.. live.Select(gap => Row(gap.Kind, gap.Case, gap.Observed))]);

    static ParityDifference Row(string kind, string caseName, string observed) =>
        new(0, kind, $"{kind}.{caseName}", observed, "");
}