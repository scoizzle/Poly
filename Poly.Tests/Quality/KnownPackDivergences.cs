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
    /// <summary>#559 (Decimal range maps to TEXT + CHECK, so a valid write fails <c>SaveChanges</c>; P-05) and #589 (the
    /// synthetic <c>Id</c> is never serialised, so a create answers 200 with no <c>Location</c> and the row cannot be
    /// read or invoked; P-12). A <c>status=0</c> row is a request that could not be sent: there was no <c>Location</c>.</summary>
    public static IReadOnlyList<KnownPackDivergence> All { get; } = [
        new(559, "P-05", "sqlite", "crm", "Product.CreateValid", "status=500;location=none;json=none"),
        new(559, "P-05", "sqlite", "crm", "Product.ReadCreated", "status=0;location=none;json=none"),
        new(589, "P-12", "sqlite", "clinic", "Patient.ActionSuccess", "status=0;location=none;json=none"),
        new(589, "P-12", "sqlite", "clinic", "Patient.CreateValid", "status=200;location=none;json=object"),
        new(589, "P-12", "sqlite", "clinic", "Patient.ReadCreated", "status=0;location=none;json=none"),
        new(589, "P-12", "sqlite", "mcp-library", "Patron.CreateValid", "status=200;location=none;json=object"),
        new(589, "P-12", "sqlite", "mcp-library", "Patron.ReadCreated", "status=0;location=none;json=none"),
        new(589, "P-12", "sqlite", "simulate-create-create-in", "Patron.ActionSuccess", "status=0;location=none;json=none"),
        new(589, "P-12", "sqlite", "simulate-create-create-in", "Patron.CreateValid", "status=200;location=none;json=object"),
        new(589, "P-12", "sqlite", "simulate-create-create-in", "Patron.ReadCreated", "status=0;location=none;json=none"),
        new(589, "P-12", "sqlite", "simulate-create-in", "Patron.ActionSuccess", "status=0;location=none;json=none"),
        new(589, "P-12", "sqlite", "simulate-create-in", "Patron.CreateValid", "status=200;location=none;json=object"),
        new(589, "P-12", "sqlite", "simulate-create-in", "Patron.ReadCreated", "status=0;location=none;json=none"),
        new(589, "P-12", "sqlite", "simulate-create-type", "Patron.ActionSuccess", "status=0;location=none;json=none"),
        new(589, "P-12", "sqlite", "simulate-create-type", "Patron.CreateValid", "status=200;location=none;json=object"),
        new(589, "P-12", "sqlite", "simulate-create-type", "Patron.ReadCreated", "status=0;location=none;json=none"),
    ];

    /// <summary>Slice id shape, identical to the Q-06 ledger's (<c>DomainParityLedgerTests.IsSliceId</c>).</summary>
    public static bool IsSliceId(string value) => Regex.IsMatch(value, @"\A[A-Z]{1,2}-[0-9]{2}\z");
}