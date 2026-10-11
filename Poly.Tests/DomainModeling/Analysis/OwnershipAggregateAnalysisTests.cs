using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// D2: a grandchild whose parent is a non-root aggregate member is not an orphan.
/// An entity with no incoming relationship still is.
/// </summary>
public class OwnershipAggregateAnalysisTests {
    private static (Domain Domain, AnalysisResult Analysis) Analyze(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(dsl).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var domain = result.Root!;
        return (domain, DomainModelAnalyzer.Analyze(domain));
    }

    private static bool Orphan(AnalysisResult analysis, string entityName) =>
        analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.AggregateOrphan
            && d.Message.Contains($"'{entityName}'", StringComparison.Ordinal));

    [Test]
    public async Task Grandchild_NonRootParent_NoOrphanWarning() {
        var (domain, analysis) = Analyze("""
            domain Test
            Customer: entity {
              Name: Text
              orders: many Order
            }
            Order: entity {
              Total: Number
              customer: Customer
              items: many OrderItem
            }
            OrderItem: entity {
              Sku: Text
              order: Order
            }
            """);

        await Assert.That(Orphan(analysis, "OrderItem")).IsFalse();
        await Assert.That(Orphan(analysis, "Order")).IsFalse();
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        var item = aggregate.Entities.First(e => e.Name == "OrderItem");
        await Assert.That(item.IsRoot).IsFalse();
        await Assert.That(item.AggregateParentName).IsEqualTo("Order");
        await Assert.That(item.ParentRelationshipName).IsEqualTo("items");
    }

    [Test]
    public async Task NoIncomingRelationship_StillOrphanWarning() {
        var (_, analysis) = Analyze("""
            domain Test
            Account: entity { Name: Text }
            Lead: entity {
              Name: Text
              account: Account
            }
            """);

        await Assert.That(Orphan(analysis, "Lead")).IsTrue();
        await Assert.That(Orphan(analysis, "Account")).IsFalse();
    }
}