using System.Linq;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;
using Poly.Introspection;

namespace Poly.Tests.DomainModeling.Parsing;

public class PolyDslRoundTripTests {
    [Test]
    public async Task ParseThenPrint_RoundTrips_StructurallyIdentical() {
        var poly = """
            domain TestDomain

            Product: entity {
              SKU: Text required unique
              Name: Text required
              Draft: stage {
                Activate: action {
                  transition to Active
                }
              }
              Active: stage {}
            }
            """;

        // ── Parse ───────────────────────────────────────────────
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        await Assert.That(changes.Count).IsGreaterThan(0);

        // ── Apply via evolution ─────────────────────────────────
        var emptyDomain = new Domain("_", [], []);
        var applyResult = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(applyResult.Succeeded).IsTrue();

        // ── Print ───────────────────────────────────────────────
        var printer = new DomainDslPrinter();
        var printedPoly = printer.Print(applyResult.Root!);
        await Assert.That(printedPoly).IsNotNull();
        await Assert.That(printedPoly.Length).IsGreaterThan(0);

        // ── Re-parse ────────────────────────────────────────────
        var parser2 = new PolyDslParser(printedPoly);
        var changes2 = parser2.Parse();
        await Assert.That(changes2.Count).IsGreaterThan(0);

        // ── Re-apply ────────────────────────────────────────────
        var emptyDomain2 = new Domain("_", [], []);
        var applyResult2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(applyResult2.Succeeded).IsTrue();

        // ── Structural identity check ───────────────────────────
        var entities1 = applyResult.Root!.Types.OfType<Entity>().OrderBy(e => e.Name).ToList();
        var entities2 = applyResult2.Root!.Types.OfType<Entity>().OrderBy(e => e.Name).ToList();

        await Assert.That(entities2.Count).IsEqualTo(entities1.Count);
        var e1 = entities1[0];
        var e2 = entities2[0];
        await Assert.That(e2.Name).IsEqualTo(e1.Name);
        await Assert.That(e2.Properties.Count).IsEqualTo(e1.Properties.Count);
        await Assert.That(e2.Stages.Count).IsEqualTo(e1.Stages.Count);

        // ── Analysis clean ──────────────────────────────────────
        var analysis = DomainModelAnalyzer.Analyze(applyResult2.Root);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
    }

    [Test]
    public async Task Parse_MinimalEntity_ProducesChangeList() {
        var poly = """
            domain Minimal

            Person: entity {
              Name: Text required
              Age: Number range(0, 150)
            }
            """;

        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();

        await Assert.That(changes.Count).IsGreaterThan(0);

        // Apply and verify
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var person = result.Root!.Types.OfType<Entity>().Single(e => e.Name == "Person");
        await Assert.That(person.Properties.Count).IsEqualTo(2);
        await Assert.That(person.Properties.Any(p => p.Name == "Name")).IsTrue();
        await Assert.That(person.Properties.Any(p => p.Name == "Age")).IsTrue();
    }

    [Test]
    public async Task ParsePrintParse_Minimal_RoundTrips() {
        var poly = """
            domain Simple

            Task: entity {
              Title: Text required
            }
            """;

        // Parse
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();

        // Apply
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        // Print
        var printer = new DomainDslPrinter();
        var printed = printer.Print(result.Root!);

        // Re-parse
        var parser2 = new PolyDslParser(printed);
        var changes2 = parser2.Parse();
        await Assert.That(changes2.Count).IsGreaterThan(0);

        // Re-apply
        var emptyDomain2 = new Domain("_", [], []);
        var result2 = new DomainEvolution(emptyDomain2).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();
    }
}