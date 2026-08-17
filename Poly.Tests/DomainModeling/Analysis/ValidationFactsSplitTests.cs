using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology.Effects;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// fact emitters vs validate packs: RequiredProperties / create-in facts
/// are published by dedicated passes; PolicyConstraint and Effect remain diagnostic packs.
/// </summary>
public class ValidationFactsSplitTests {
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
    public async Task RequiredProperties_PublishedByFactPass_NotValidatePack() {
        var domain = ParseDomain("""
            domain Test
            Customer: entity {
              Name: Text required
              Email: Text
              HasName: policy { Name exists }
              Active: stage {
                Submit: action { transition to Done }
              }
              Done: stage { }
            }
            """);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var customer = domain.Types.OfType<Entity>().First(e => e.Name == "Customer");
        var required = analysis.GetMetadata<RequiredPropertiesMetadata>(customer);

        await Assert.That(required).IsNotNull();
        await Assert.That(required!.RequiredProperties.Any(p => p.Name == "Name")).IsTrue();

        var passNames = analysis.Telemetry.Passes.Select(p => p.PassName).ToList();
        await Assert.That(passNames).Contains(RequiredPropertiesPass.Id);
        await Assert.That(passNames).Contains(PolicyConstraintAnalyzer.Id);
        // Independent packs (both after Semantic); order between them is not a contract.
    }

    [Test]
    public async Task CreateIn_ResolvedTarget_PublishedByEffectFactsPass() {
        var domain = ParseDomain("""
            domain Test
            Order: entity {
              Place: action {
                create in lines { }
              }
              lines: many Line
            }
            Line: entity {
              Qty: Number
            }
            """);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var order = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var place = order.Actions.First(a => a.Name == "Place");
        var createIn = place.Effects.OfType<CreateEntityInRelationshipEffect>().First();

        var resolved = analysis.GetMetadata<ResolvedRelationshipTargetMetadata>(createIn);
        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.Relationship.Name).IsEqualTo("lines");
        await Assert.That(resolved.TargetEntity.Name).IsEqualTo("Line");

        var passNames = analysis.Telemetry.Passes.Select(p => p.PassName).ToList();
        await Assert.That(passNames).Contains(EffectFactsPass.Id);
        await Assert.That(passNames).Contains(EffectAnalyzer.Id);
        await Assert.That(passNames.IndexOf(EffectFactsPass.Id))
            .IsLessThan(passNames.IndexOf(EffectAnalyzer.Id));
    }

    [Test]
    public async Task EffectAnalyzer_DoesNotWriteResolvedTargetMetadata() {
        // Boundary check: stripping EffectFactsPass leaves create-in without fact bag
        // while validate pack still runs if registered alone with deps.
        var builder = new AnalyzerBuilder()
            .AddAnalyzer(new StructuralDomainAnalyzer())
            .AddAnalyzer(new DomainCatalogPass())
            .AddAnalyzer(new RequiredPropertiesPass())
            .AddAnalyzer(new ConstraintPropagationAnalyzer())
            .AddAnalyzer(new EffectAnalyzer()); // lint only — no EffectFactsPass
        var analyzer = builder.Build();

        var domain = ParseDomain("""
            domain Test
            Order: entity {
              Place: action {
                create in lines { }
              }
              lines: many Line
            }
            Line: entity {
              Qty: Number
            }
            """);

        var analysis = analyzer.Analyze(domain);
        var order = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var place = order.Actions.First(a => a.Name == "Place");
        var createIn = place.Effects.OfType<CreateEntityInRelationshipEffect>().First();

        await Assert.That(analysis.GetMetadata<ResolvedRelationshipTargetMetadata>(createIn)).IsNull();
    }
}