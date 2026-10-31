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
        var stay = DomainEntityInstance.Create(booking, Bag(199.50m, 3L, 0m), domain);
        await Assert.That(stay.GetProperty<decimal>("NightlyRate")).IsEqualTo(199.50m);

        var past = Assert.Throws<ConstraintFailureException>(() =>
            DomainEntityInstance.Create(booking, Bag(10000.01m, 1L, 0m), domain));
        await Assert.That(past!.Message).IsNotEmpty();
    }

    [Test]
    public async Task Ingest_FractionalJson_StoresDecimalAndRejectsFractionalNumber() {
        var (sessionId, _) = McpTestTools.Store.Create("Stay");
        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, Stay);
        await Assert.That(applied.Success).IsTrue();

        var created = McpTestTools.Runtime.CreateInstance(sessionId, "Booking",
            """{"NightlyRate":199.50,"Nights":3,"Total":0}""");
        await Assert.That(created.Success).IsTrue();
        McpTestTools.Store.TryGet(sessionId, out var state);
        var stored = state!.InstanceMap.Values.Single();
        await Assert.That(stored.GetProperty<decimal>("NightlyRate")).IsEqualTo(199.50m);

        var (rejectId, _) = McpTestTools.Store.Create("Stay");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(rejectId, Stay).Success).IsTrue();
        var fraction = McpTestTools.Runtime.CreateInstance(rejectId, "Booking",
            """{"NightlyRate":199.50,"Nights":1.5,"Total":0}""");
        await Assert.That(fraction.Success).IsFalse();
        await Assert.That(fraction.Message).Contains("Decimal");
        McpTestTools.Store.TryGet(rejectId, out var after);
        await Assert.That(after!.InstanceMap.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Arithmetic_NightlyRateTimesNights_IsExactDecimal() {
        var (domain, booking) = Evolve();
        var stay = DomainEntityInstance.Create(booking, Bag(199.50m, 3L, 0m), domain);
        var priced = stay.InvokeAction("Price");
        await Assert.That(priced.Succeeded).IsTrue();
        await Assert.That(stay.GetProperty<decimal>("Total")).IsEqualTo(598.50m);
    }

    [Test]
    public async Task Add_DecimalParameter_AddsToDecimalProperty() {
        var (domain, booking) = Evolve();
        var stay = DomainEntityInstance.Create(booking, Bag(199.50m, 3L, 0m), domain);
        var added = stay.InvokeAction("Add", new Dictionary<string, object?> { ["amount"] = 10m });
        await Assert.That(added.Succeeded).IsTrue();
        await Assert.That(stay.GetProperty<decimal>("Total")).IsEqualTo(209.50m);
    }

    [Test]
    public async Task Assign_LongLiteral_StoresDecimalZero() {
        var (domain, booking) = Evolve();
        var stay = DomainEntityInstance.Create(booking, Bag(199.50m, 3L, 199.50m), domain);
        var cleared = stay.InvokeAction("Clear");
        await Assert.That(cleared.Succeeded).IsTrue();
        await Assert.That(stay.GetProperty<decimal>("Total")).IsEqualTo(0m);
    }

    private static Dictionary<string, object?> Bag(decimal rate, long nights, decimal total) =>
        new() {
            ["NightlyRate"] = rate,
            ["Nights"] = nights,
            ["Total"] = total,
        };

    private static (Domain Domain, Entity Booking) Evolve() {
        var changes = new PolyDslParser(Stay).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var booking = result.Root!.Types.OfType<Entity>().First(t => t.Name == "Booking");
        return (result.Root, booking);
    }
}