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
    /// <summary>#589 (the synthetic <c>Id</c> is never serialised, so a create answers 200 with no <c>Location</c> and the
    /// row cannot be read or invoked; P-12). A <c>status=0</c> row is a request that could not be sent: there was no
    /// <c>Location</c>. #559's four rows are gone with its fix: crm <c>Product</c> has a natural key, so its create is a
    /// 201 with a <c>Location</c> and its read passes; hotel <c>RoomType</c> has no key, so its two probes are #589's.</summary>
    public static IReadOnlyList<KnownPackDivergence> All { get; } = [
        new(589, "P-12", "sqlite", "clinic", "Patient.ActionSuccess", "status=0;location=none"),
        new(589, "P-12", "sqlite", "clinic", "Patient.CreateValid", "status=200;location=none"),
        new(589, "P-12", "sqlite", "clinic", "Patient.ReadCreated", "status=0;location=none"),
        new(589, "P-12", "sqlite", "hotel", "RoomType.CreateValid", "status=200;location=none"),
        new(589, "P-12", "sqlite", "hotel", "RoomType.ReadCreated", "status=0;location=none"),
        new(589, "P-12", "sqlite", "mcp-library", "Patron.CreateValid", "status=200;location=none"),
        new(589, "P-12", "sqlite", "mcp-library", "Patron.ReadCreated", "status=0;location=none"),
        new(589, "P-12", "sqlite", "simulate-create-create-in", "Patron.ActionSuccess", "status=0;location=none"),
        new(589, "P-12", "sqlite", "simulate-create-create-in", "Patron.CreateValid", "status=200;location=none"),
        new(589, "P-12", "sqlite", "simulate-create-create-in", "Patron.ReadCreated", "status=0;location=none"),
        new(589, "P-12", "sqlite", "simulate-create-in", "Patron.ActionSuccess", "status=0;location=none"),
        new(589, "P-12", "sqlite", "simulate-create-in", "Patron.CreateValid", "status=200;location=none"),
        new(589, "P-12", "sqlite", "simulate-create-in", "Patron.ReadCreated", "status=0;location=none"),
        new(589, "P-12", "sqlite", "simulate-create-type", "Patron.ActionSuccess", "status=0;location=none"),
        new(589, "P-12", "sqlite", "simulate-create-type", "Patron.CreateValid", "status=200;location=none"),
        new(589, "P-12", "sqlite", "simulate-create-type", "Patron.ReadCreated", "status=0;location=none"),
    ];
}