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
    /// <summary>#559 (Decimal range maps to TEXT + CHECK, so a valid write fails <c>SaveChanges</c>; P-05). A
    /// <c>status=0</c> row is a request that could not be sent: there was no <c>Location</c>. #589's rows (a shadow-key
    /// create answered 200 with no <c>Location</c>) are gone: the create now answers 201 with the shadow id's route.</summary>
    public static IReadOnlyList<KnownPackDivergence> All { get; } = [
        new(559, "P-05", "sqlite", "crm", "Product.CreateValid", "status=500;location=none"),
        new(559, "P-05", "sqlite", "crm", "Product.ReadCreated", "status=0;location=none"),
        new(559, "P-05", "sqlite", "hotel", "RoomType.CreateValid", "status=500;location=none"),
        new(559, "P-05", "sqlite", "hotel", "RoomType.ReadCreated", "status=0;location=none"),
    ];
}