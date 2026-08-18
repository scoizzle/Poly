using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Known fact-consumer passes must declare real Dependencies;
/// pipeline order must honor those edges (no silent undeclared catalog/structure/topology reads).
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

    private static void AssertDeclares(INodeAnalyzer pass, params string[] requiredDeps) {
        foreach (var dep in requiredDeps) {
            if (!pass.Dependencies.Contains(dep, StringComparer.Ordinal))
                throw new Exception(
                    $"Pass '{pass.PassName}' must declare dependency '{dep}'. " +
                    $"Actual: [{string.Join(", ", pass.Dependencies)}]");
        }
    }

    [Test]
    public async Task FactConsumerPasses_DeclareKnownDependencies() {
        AssertDeclares(new DomainCatalogPass());
        AssertDeclares(new RuntimeContractAnalyzer(), DomainCatalogPass.Id);
        AssertDeclares(new RequiredPropertiesPass(), DomainCatalogPass.Id);
        AssertDeclares(new PolicyConstraintAnalyzer(), DomainCatalogPass.Id);
        AssertDeclares(new ConstraintPropagationAnalyzer());
        AssertDeclares(new EffectFactsPass(), DomainCatalogPass.Id);
        AssertDeclares(
            new EffectAnalyzer(),
            DomainCatalogPass.Id,
            RequiredPropertiesPass.Id,
            ConstraintPropagationAnalyzer.Id);
        AssertDeclares(
            new CapabilityAnalyzer(),
            DomainCatalogPass.Id);
        AssertDeclares(new EntityStructureAnalyzer(), DomainCatalogPass.Id);
        AssertDeclares(new EffectTopologyPass()); // pure tree scan
        AssertDeclares(
            new OwnershipAggregatePass(),
            EffectTopologyPass.Id,
            EntityStructureAnalyzer.Id);
        AssertDeclares(new CrossReferencePass(), EffectTopologyPass.Id);
        AssertDeclares(
            new StoragePass(),
            EffectTopologyPass.Id,
            OwnershipAggregatePass.Id);

        // Lint consumers that still read analysis bags
        AssertDeclares(new RuleCoverageAnalyzer(), RequiredPropertiesPass.Id);
        AssertDeclares(
            new SubscriptionAnalyzer(),
            DomainCatalogPass.Id,
            CapabilityAnalyzer.Id);
        AssertDeclares(new ConstraintQualityAnalyzer(), DomainCatalogPass.Id);
        AssertDeclares(new AuthoringSuggestionAnalyzer(), DomainCatalogPass.Id);

        var catalogDeps = new DomainCatalogPass().Dependencies;
        await Assert.That(catalogDeps.Contains(RuntimeContractAnalyzer.Id)).IsFalse();
        await Assert.That(catalogDeps.Length).IsEqualTo(0);
    }

    [Test]
    public async Task DomainPipeline_PassOrder_HonorsDeclaredDependencies() {
        var domain = ParseDomain("""
            domain Test
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

        // Catalog / structure / topology consumers after their publishers
        await Assert.That(Index(StructuralDomainAnalyzer.Id)).IsLessThan(Index(DomainCatalogPass.Id));
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
        await Assert.That(Index(CapabilityAnalyzer.Id)).IsLessThan(Index(SubscriptionAnalyzer.Id));
    }

    [Test]
    public async Task AnalyzerBuilder_MissingDeclaredDependency_Throws() {
        // Lightweight guard: consumer registered without its dep fails closed at build time.
        await Assert.That(() =>
            new AnalyzerBuilder()
                .AddAnalyzer(new OwnershipAggregatePass())
                .Build()).ThrowsExactly<InvalidOperationException>();
    }
}