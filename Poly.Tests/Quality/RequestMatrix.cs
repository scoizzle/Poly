using System.Collections.Concurrent;
using System.Text.Json.Nodes;

using Poly.Tests.TestHelpers;

namespace Poly.Tests.Quality;

/// <summary>An action to invoke on a created row; <paramref name="Rejected"/> is null when no argument can be rejected.</summary>
public sealed record ActionProbe(string Name, JsonObject? Valid, JsonObject? Rejected);

/// <summary>One entity's slice of the matrix: its route, the unique key property when it has one, a valid body (required
/// properties only), an invalid body and an optional action.</summary>
public sealed record EntityMatrix(
    string Entity, string Route, string? KeyProperty, JsonObject Valid, JsonObject Invalid, ActionProbe? Action = null);

/// <summary>The request matrix for one golden domain.</summary>
public sealed record DomainMatrix(string Domain, IReadOnlyList<EntityMatrix> Entities);

/// <summary>What one probe observed. <paramref name="Observed"/> is the coarse value a ledger row pins.</summary>
public sealed record ProbeObservation(string Probe, bool Passed, string Observed);

/// <summary>One domain's matrix run on one provider. <paramref name="Fault"/> is set when the domain could not run
/// (the phase and message); a fault belongs to its own domain and fails only that domain's row.</summary>
public sealed record DomainObservation(string Provider, string Domain, string? Fault, IReadOnlyList<ProbeObservation> Probes);

public static class RequestMatrix {
    public static readonly string[] Probes =
        ["List", "CreateValid", "ReadCreated", "CreateInvalid", "ActionSuccess", "ActionRejected"];

    private static JsonObject J(string json) => JsonNode.Parse(json)!.AsObject();

    private static EntityMatrix E(string entity, string route, string? key, string valid, string invalid, ActionProbe? action = null) =>
        new(entity, route, key, J(valid), J(invalid), action);

    private static ActionProbe A(string name, string? valid = null, string? rejected = null) =>
        new(name, valid is null ? null : J(valid), rejected is null ? null : J(rejected));

    /// <summary>Hand-written per-domain rows (data, not logic): the generic sample generator has not landed and a
    /// pattern such as <c>^WH-[0-9]{3}$</c> is not generically satisfiable. Valid bodies carry required properties only.</summary>
    public static IReadOnlyList<DomainMatrix> All { get; } = [
        new("warehouse", [E("Warehouse", "/api/warehouses", "Code",
            """{"Code":"WH-001","Name":"Main hub","Zip":"12345","Capacity":10}""",
            """{"Code":"WH-001","Name":"Main hub","Zip":"12345","Capacity":0}""",
            A("AdjustCapacity", """{"delta":1}""", """{"delta":100000}"""))]),
        new("orders", [E("Customer", "/api/customers", "Email",
            """{"Email":"a@example.com","Name":"Alice"}""",
            """{"Email":"a@example.com","Name":"A"}""",
            A("ShipAll"))]),
        new("clinic", [E("Doctor", "/api/doctors", "LicenseNo",
            """{"Name":"Doc Who","LicenseNo":"MD-123456"}""",
            """{"Name":"Doc Who","LicenseNo":"nope"}""",
            A("StartShift"))]),
        new("mcp-library", [
            E("Patron", "/api/patrons", null, """{"Name":"Ann"}""", """{}"""),
            E("Book", "/api/books", "Isbn", """{"Title":"Dune","Isbn":"978-0441013593"}""", """{"Title":"Dune"}"""),
        ]),
        new("university", [E("Instructor", "/api/instructors", "Email",
            """{"Name":"Ada","Email":"ada@example.edu"}""",
            """{"Name":"Ada","Email":"nope"}""",
            A("GoOnLeave"))]),
        new("crm", [E("Product", "/api/products", "Sku",
            """{"Sku":"ABC-1","Name":"Widget","ListPrice":10.5}""",
            """{"Sku":"A","Name":"Widget","ListPrice":10.5}""")]),
        new("hotel", [
            E("RoomType", "/api/roomtypes", null,
                """{"Name":"Standard","NightlyRate":100.5,"MaxOccupancy":2}""",
                """{"Name":"Standard","NightlyRate":100.5,"MaxOccupancy":0}"""),
            E("Guest", "/api/guests", "Email",
                """{"Name":"Gus","Email":"gus@example.com","Phone":"5551234567"}""",
                """{"Name":"Gus","Email":"gus@example.com","Phone":"1"}"""),
        ]),
        new("simulate-create-type", [E("Patron", "/api/patrons", null, """{"Name":"Pat"}""", """{}""", A("AssessByType"))]),
        new("simulate-create-in", [E("Patron", "/api/patrons", null, """{"Name":"Pat"}""", """{}""", A("AssessByRel"))]),
        new("simulate-create-create-in", [E("Patron", "/api/patrons", null, """{"Name":"Pat"}""", """{}""", A("AssessByType"))]),
    ];

    public static DomainMatrix For(string domain) => All.Single(m => m.Domain == domain);

    /// <summary>Every probe name the matrix defines for a domain, as <c>Entity.Probe</c>.</summary>
    public static IReadOnlyList<string> ProbeNames(DomainMatrix matrix) => [..
        matrix.Entities.SelectMany(e => Probes
            .Where(p => e.Action is not null || !p.StartsWith("Action", StringComparison.Ordinal))
            .Where(p => p != "ActionRejected" || e.Action?.Rejected is not null)
            .Select(p => $"{e.Entity}.{p}"))];

    public static Task<IReadOnlyList<ProbeObservation>> RunAsync(HttpSide side, DomainMatrix matrix) =>
        Task.FromResult<IReadOnlyList<ProbeObservation>>([]);
}

/// <summary>One memoised observation per <c>(provider, domain)</c>, so every test reads the same boot.</summary>
public static class MatrixObservations {
    private static readonly ConcurrentDictionary<string, Lazy<Task<DomainObservation>>> Runs = new();

    public static Task<DomainObservation> ObserveAsync(string provider, GoldenDomain golden) =>
        Runs.GetOrAdd($"{provider}/{golden.Name}", _ => new Lazy<Task<DomainObservation>>(() => RunCoreAsync(provider, golden))).Value;

    private static async Task<DomainObservation> RunCoreAsync(string provider, GoldenDomain golden) {
        var phase = "compile";
        try {
            var fixture = GoldenPackDomains.FromGolden(golden);
            _ = await PackOracle.CompileAsync(fixture);
            phase = "boot";
            await using var oracle = await PackOracle.BootAsync(fixture, provider);
            phase = "probe";
            var probes = await RequestMatrix.RunAsync(HttpSide.On(oracle), RequestMatrix.For(golden.Name));
            return new DomainObservation(provider, golden.Name, null, probes);
        }
        catch (Exception ex) {
            var message = ex.Message.ReplaceLineEndings(" ");
            return new DomainObservation(provider, golden.Name,
                $"{phase}: {ex.GetType().Name}: {(message.Length > 300 ? message[..300] : message)}", []);
        }
    }
}

/// <summary>The one comparison every matrix test uses: the live observation against a domain's ledger rows.</summary>
public static class PackDivergenceSet {
    public static IReadOnlyList<ParityDrift> Compare(
        IReadOnlyList<KnownPackDivergence> expected, DomainObservation live, IReadOnlyList<string> probeNames) {
        if (live.Fault is not null)
            return [new ParityDrift(ParityDriftKind.Widened, $"{live.Domain}: the domain did not run ({live.Fault})")];

        var drifts = new List<ParityDrift>();
        var byProbe = live.Probes.ToDictionary(p => p.Probe, StringComparer.Ordinal);
        foreach (var name in probeNames)
            if (!byProbe.ContainsKey(name))
                drifts.Add(new(ParityDriftKind.Deleted, $"{live.Domain} {name}: the probe did not run"));

        foreach (var row in expected) {
            if (!byProbe.TryGetValue(row.Probe, out var got)) {
                if (probeNames.Contains(row.Probe))
                    continue;
                drifts.Add(new(ParityDriftKind.Deleted, $"#{row.Issue} {live.Domain} {row.Probe}: the probe is not in the matrix"));
            }
            else if (got.Passed)
                drifts.Add(new(ParityDriftKind.Stale, $"#{row.Issue} {live.Domain} {row.Probe}: now passes; delete the row"));
            else if (!string.Equals(got.Observed, row.Observed, StringComparison.Ordinal))
                drifts.Add(new(ParityDriftKind.ValueChanged, $"#{row.Issue} {live.Domain} {row.Probe}: '{row.Observed}' is now '{got.Observed}'"));
        }

        var listed = expected.Select(r => r.Probe).ToHashSet(StringComparer.Ordinal);
        foreach (var got in live.Probes.Where(p => !p.Passed && !listed.Contains(p.Probe)))
            drifts.Add(new(ParityDriftKind.Widened, $"{live.Domain} {got.Probe}: '{got.Observed}' is unlisted"));
        return drifts;
    }
}