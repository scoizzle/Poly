using System.Text.RegularExpressions;

namespace Poly.Tests.Quality;

/// <summary>
/// One request-matrix probe that fails on master today: the open <paramref name="Issue"/>, the slice
/// <paramref name="Owner"/> that fixes it, the <paramref name="Provider"/> the oracle ran, the golden
/// <paramref name="Domain"/>, the <paramref name="Probe"/> and its coarse <paramref name="Observed"/> value
/// (<c>status=500;location=none</c>, never body text). The fixing slice deletes its rows.
/// </summary>
public sealed record KnownPackDivergence(
    int Issue, string Owner, string Provider, string Domain, string Probe, string Observed);

/// <summary>Every known pack divergence, sorted by <c>(Issue, Provider, Domain, Probe)</c>.</summary>
public static class KnownPackDivergences {
    public static IReadOnlyList<KnownPackDivergence> All { get; } = [];

    /// <summary>Slice id shape, identical to the Q-06 ledger's (<c>DomainParityLedgerTests.IsSliceId</c>).</summary>
    public static bool IsSliceId(string value) => Regex.IsMatch(value, @"\A[A-Z]{1,2}-[0-9]{2}\z");
}