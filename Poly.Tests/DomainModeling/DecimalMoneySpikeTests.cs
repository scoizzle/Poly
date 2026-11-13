using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// C8-num gate: one Decimal money property is created, range-checked, and
/// multiplied by a Number. CREATE, INGEST, and arithmetic share this domain.
/// </summary>
public class DecimalMoneySpikeTests {
    private const string Stay = """
        domain Stay

        Booking: entity {
          NightlyRate: Decimal range(0, 10000)
          Nights: Number
          Total: Decimal

          Price: action {
            assign Total to NightlyRate * Nights
          }

          Clear: action {
            assign Total to 0
          }

          Add: action (amount: Decimal) {
            assign Total to NightlyRate + amount
          }
        }
        """;

    [Test]
    public async Task Create_DecimalRate_StoresValueAndRejectsPastRange() {
        var (domain, booking) = Evolve();
        var exec = ModuleExec.For(domain);
        var stay = exec.Create((booking).Name, Bag(199.50m, 3L, 0m)).Instance!;
        await Assert.That((decimal)stay.Bag["NightlyRate"]!).IsEqualTo(199.50m);

        var past = exec.Create(booking.Name, Bag(10000.01m, 1L, 0m));
        await Assert.That(past.Succeeded).IsFalse();
        await Assert.That(past.Error).IsNotNull().And.IsNotEmpty();
    }

    [Test]
    public async Task Ingest_FractionalJson_StoresDecimalAndRejectsFractionalNumber() {
        var (sessionId, _) = McpTestTools.Store.Create("Stay");
        var applied = McpTestTools.Surface.Define(sessionId, Stay);
        await Assert.That(applied.Success).IsTrue();

        var created = McpTestTools.Surface.Create(sessionId, "Booking",
            """{"NightlyRate":199.50,"Nights":3,"Total":0}""");
        await Assert.That(created.Ok).IsTrue();
        McpTestTools.Store.TryGet(sessionId, out var state);
        var stored = state!.InstanceMap.Values.Single();
        await Assert.That((decimal)stored["NightlyRate"]!).IsEqualTo(199.50m);

        var (rejectId, _) = McpTestTools.Store.Create("Stay");
        await Assert.That(McpTestTools.Surface.Define(rejectId, Stay).Success).IsTrue();
        var fraction = McpTestTools.Surface.Create(rejectId, "Booking",
            """{"NightlyRate":199.50,"Nights":1.5,"Total":0}""");
        await Assert.That(fraction.Ok).IsFalse();
        await Assert.That(fraction.Summary).Contains("Decimal");
        McpTestTools.Store.TryGet(rejectId, out var after);
        await Assert.That(after!.InstanceMap.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Arithmetic_NightlyRateTimesNights_IsExactDecimal() {
        var (domain, booking) = Evolve();
        var exec = ModuleExec.For(domain);
        var stay = exec.Create((booking).Name, Bag(199.50m, 3L, 0m)).Instance!;
        var priced = exec.Invoke(stay, "Price");
        await Assert.That(priced.Succeeded).IsTrue();
        await Assert.That((decimal)stay.Bag["Total"]!).IsEqualTo(598.50m);
    }

    [Test]
    public async Task Add_DecimalParameter_AddsToDecimalProperty() {
        var (domain, booking) = Evolve();
        var exec = ModuleExec.For(domain);
        var stay = exec.Create((booking).Name, Bag(199.50m, 3L, 0m)).Instance!;
        var added = exec.Invoke(stay, "Add", new Dictionary<string, object?> { ["amount"] = 10m });
        await Assert.That(added.Succeeded).IsTrue();
        await Assert.That((decimal)stay.Bag["Total"]!).IsEqualTo(209.50m);
    }

    [Test]
    public async Task Assign_LongLiteral_StoresDecimalZero() {
        var (domain, booking) = Evolve();
        var exec = ModuleExec.For(domain);
        var stay = exec.Create((booking).Name, Bag(199.50m, 3L, 199.50m)).Instance!;
        var cleared = exec.Invoke(stay, "Clear");
        await Assert.That(cleared.Succeeded).IsTrue();
        await Assert.That((decimal)stay.Bag["Total"]!).IsEqualTo(0m);
    }

    private static Dictionary<string, object?> Bag(decimal rate, long nights, decimal total) =>
        new() {
            ["NightlyRate"] = rate,
            ["Nights"] = nights,
            ["Total"] = total,
        };

    private static (Domain Domain, Entity Booking) Evolve() {
        var changes = new PolyDslParser(Stay).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var booking = result.Root!.Types.OfType<Entity>().First(t => t.Name == "Booking");
        return (result.Root, booking);
    }
}