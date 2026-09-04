using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling.Lowering;

public class ClockLoweringTests {
    [Test]
    public async Task Assign_DateToNow_LowersToFromDateTime_NotLiteral() {
        var (domain, analysis) = Evolve("""
            domain Clocks
            Item: entity {
              Due: Date
              Touch: action { assign Due to Now }
            }
            """);
        var item = domain.Types.OfType<Entity>().First(e => e.Name == "Item");
        var action = item.Actions.First(a => a.Name == "Touch");
        var pass = new EffectLoweringPass(item, new LoweringContext(
            new Parameter("entity", new TypeReference(item.Name)),
            Analysis: analysis,
            Domain: domain));
        var lowered = pass.LowerActionBody(action.Effects);
        await Assert.That(lowered).IsNotNull();
        var assign = Flatten(lowered!).OfType<Assignment>()
            .First(a => a.Destination is Member { MemberName: "Due" });
        await Assert.That(assign.Value is Constant).IsFalse();
        var members = Flatten(assign.Value).OfType<Member>().Select(m => m.MemberName).ToList();
        await Assert.That(members.Contains("FromDateTime") || members.Contains("UtcNow")).IsTrue();
    }

    [Test]
    public async Task Assign_DateToNow_WithTemporal_LowersToFromDateTime_NotLiteral() {
        var (domain, analysis) = Evolve("""
            domain Clocks
            uses temporal
            Item: entity {
              Due: Date
              Touch: action { assign Due to Now }
            }
            """, ExtensionCatalog.Core.Language);
        var item = domain.Types.OfType<Entity>().First(e => e.Name == "Item");
        var action = item.Actions.First(a => a.Name == "Touch");
        var pass = new EffectLoweringPass(item, new LoweringContext(
            new Parameter("entity", new TypeReference(item.Name)),
            Analysis: analysis,
            Domain: domain));
        var lowered = pass.LowerActionBody(action.Effects);
        var assign = Flatten(lowered!).OfType<Assignment>()
            .First(a => a.Destination is Member { MemberName: "Due" });
        await Assert.That(assign.Value is Constant).IsFalse();
        var members = Flatten(assign.Value).OfType<Member>().Select(m => m.MemberName).ToList();
        await Assert.That(members.Contains("FromDateTime") || members.Contains("UtcNow")).IsTrue();
    }

    [Test]
    public async Task CreateIn_DateNow_StoresDateOnly() {
        var (domain, _) = Evolve("""
            domain Parking
            Permit: entity {
              Plate: Text required
              Issued: Date
            }
            Lot: entity {
              permits: many Permit
              Issue: action (plate: Text) {
                create in permits { Plate: plate; Issued: Now }
              }
            }
            """);
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotE, domain: domain);
        store.Add(lot);

        var result = lot.InvokeAction("Issue", new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsTrue();
        var child = lot.CreatedChildren.Single();
        await Assert.That(child.GetProperty<object>("Issued")).IsTypeOf<DateOnly>();
        await Assert.That(child.GetProperty<string>("Plate")).IsEqualTo("ABC123");
    }

    [Test]
    public async Task Assign_TextToGuid_StoresStringNotLiteralInTree() {
        var (domain, analysis) = Evolve("""
            domain Ids
            Item: entity {
              ExternalId: Text
              Stamp: action { assign ExternalId to Guid }
            }
            """);
        var item = domain.Types.OfType<Entity>().First(e => e.Name == "Item");
        var action = item.Actions.First(a => a.Name == "Stamp");
        var pass = new EffectLoweringPass(item, new LoweringContext(
            new Parameter("entity", new TypeReference(item.Name)),
            Analysis: analysis,
            Domain: domain));
        var lowered = pass.LowerActionBody(action.Effects);
        var assign = Flatten(lowered!).OfType<Assignment>()
            .First(a => a.Destination is Member { MemberName: "ExternalId" });
        await Assert.That(assign.Value is Constant).IsFalse();

        var instance = DomainEntityInstance.Create(item, domain: domain);
        var result = instance.InvokeAction("Stamp");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(instance.GetProperty<object>("ExternalId")).IsTypeOf<string>();
        await Assert.That(Guid.TryParse(instance.GetProperty<string>("ExternalId"), out _)).IsTrue();
    }

    private static (Domain Domain, AnalysisResult Analysis) Evolve(
        string poly, DomainSession? session = null) {
        var changes = session is null
            ? new PolyDslParser(poly).Parse()
            : new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }

    private static IEnumerable<Node> Flatten(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var n in Flatten(child))
                yield return n;
        }
    }
}
