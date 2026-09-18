using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Frozen domain pipeline is registration order. Known bag readers sit after writers.
/// </summary>
public class PassDependencyDeclarationTests {
    private static Domain ParseDomain(string poly) {
        var ctx = ExtensionCatalog.Core.Authoring;
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException("Domain evolution failed: " +
                string.Join("; ", result.Analysis.Diagnostics.Where(d =>
                    d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        return result.Root!;
    }

    [Test]
    public async Task DomainPipeline_RegistrationOrder_PlacesReadersAfterWriters() {
        var domain = ParseDomain("""
            domain Test
            uses persistence
            Customer: entity {
              Name: Text
              Active: stage {
                Submit: action { transition to Done }
              }
              Done: stage { }
            }
            """);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var order = analysis.Telemetry.Passes.Select(p => p.PassName).ToList();

        int Index(string id) {
            var i = order.IndexOf(id);
            if (i < 0)
                throw new Exception($"Pass '{id}' missing from pipeline telemetry. Passes: [{string.Join(", ", order)}]");
            return i;
        }

        await Assert.That(Index(DomainCatalogPass.Id)).IsLessThan(Index(ExpressionTypeAnalyzer.Id));
        await Assert.That(Index(DomainCatalogPass.Id)).IsLessThan(Index(CapabilityAnalyzer.Id));
        await Assert.That(Index(DomainCatalogPass.Id)).IsLessThan(Index(EntityStructureAnalyzer.Id));
        await Assert.That(Index(EffectTopologyPass.Id)).IsLessThan(Index(OwnershipAggregatePass.Id));
        await Assert.That(Index(EntityStructureAnalyzer.Id)).IsLessThan(Index(OwnershipAggregatePass.Id));
        await Assert.That(Index(OwnershipAggregatePass.Id)).IsLessThan(Index(StoragePass.Id));
        await Assert.That(Index(EffectTopologyPass.Id)).IsLessThan(Index(CrossReferencePass.Id));
        await Assert.That(Index(ConstraintPropagationAnalyzer.Id)).IsLessThan(Index(EffectAnalyzer.Id));
        await Assert.That(Index(RequiredPropertiesPass.Id)).IsLessThan(Index(EffectAnalyzer.Id));
        await Assert.That(Index(RequiredPropertiesPass.Id)).IsLessThan(Index(RuleCoverageAnalyzer.Id));
        await Assert.That(Index(EffectFactsPass.Id)).IsLessThan(Index(EffectAnalyzer.Id));
        await Assert.That(Index(EffectInvariantAnalyzer.Id)).IsLessThan(Index(EffectAnalyzer.Id));
        await Assert.That(Index(EffectInvariantAnalyzer.Id)).IsLessThan(Index(StoragePass.Id));
        await Assert.That(Index(CapabilityAnalyzer.Id)).IsLessThan(Index(SubscriptionAnalyzer.Id));
    }

    [Test]
    public async Task DomainPipeline_HasNoTemporalPass() {
        var domain = DomainFactory.Create("T");
        var analysis = DomainSession.Open(domain).Analyze(domain);
        var order = analysis.Telemetry.Passes.Select(p => p.PassName).ToList();
        await Assert.That(order.Contains("Temporal")).IsFalse();
        await Assert.That(Index(analysis, DomainCatalogPass.Id))
            .IsLessThan(Index(analysis, ExpressionTypeAnalyzer.Id));
    }

    [Test]
    public async Task DomainPipeline_HasNoStoragePass_WithoutPersistenceLibrary() {
        var domain = DomainFactory.Create("T");
        var analysis = DomainSession.Open(domain).Analyze(domain);
        var order = analysis.Telemetry.Passes.Select(p => p.PassName).ToList();
        await Assert.That(order.Contains(StoragePass.Id)).IsFalse();
    }

    private static int Index(AnalysisResult analysis, string id) {
        var order = analysis.Telemetry.Passes.Select(p => p.PassName).ToList();
        var i = order.IndexOf(id);
        if (i < 0)
            throw new Exception($"Pass '{id}' missing. Passes: [{string.Join(", ", order)}]");
        return i;
    }
}