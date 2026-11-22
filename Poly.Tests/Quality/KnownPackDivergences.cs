namespace Poly.Tests.Quality;

/// <summary>
/// One request-matrix probe that fails on master today: the open <paramref name="Issue"/>, the slice
/// <paramref name="Owner"/> that fixes it, the <paramref name="Provider"/> the oracle ran, the golden
/// <paramref name="Domain"/>, the <paramref name="Probe"/> and its coarse <paramref name="Observed"/> value
/// (<c>status=500;location=none</c>, never body text; <c>status=0</c> is a request never sent, <c>status=err</c> a transport failure). The fixing slice deletes its rows.
/// </summary>
public sealed record KnownPackDivergence(
    int Issue, string Owner, string Provider, string Domain, string Probe, string Observed);

/// <summary>Every known pack divergence, sorted by <c>(Issue, Provider, Domain, Probe)</c>.</summary>
public static class KnownPackDivergences {
    /// <summary>No known pack divergence: #559's four rows went with its fix (#1092) and #589's with its fix (#1059).
    /// A <c>status=0</c> row would be a request that could not be sent (no <c>Location</c>).</summary>
    public static IReadOnlyList<KnownPackDivergence> All { get; } = [
    ];
}