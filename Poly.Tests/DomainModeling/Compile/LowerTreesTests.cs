using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;

using Artifact = Poly.DomainModeling.Compile.Artifact;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// What <see cref="DomainSession.Lower"/> registers: one scaffolding tree and one tree per entity,
/// each pointing at a source-domain or source-entity artifact, plus one analysis report
/// and one catalog-reference-report.
/// </summary>
public sealed class LowerTreesTests {
    private const string Parking = """
        domain Parking

        Permit: entity {
          Plate: Text required
          Open: stage { }
        }

        Garage: entity {
          Name: Text required
        }
        """;

    public static IEnumerable<string> SampleDomains() => EmitGoldenTests.SampleDomains();

    private static IReadOnlyList<string> TypeNames(Artifact tree) =>
        ((IReadOnlyList<TypeDefinitionNode>)tree.Payload!).Select(t => t.Name).ToList();

    private static bool IsTree(Artifact artifact) =>
        artifact.Descriptor.Id.Type is "entity" or "scaffolding";

    [Test]
    public async Task Lower_RegistersAScaffoldingTreeAndOneTreePerEntity() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);

        session.Lower(domain, analysis);

        var catalog = session.ArtifactCatalog;
        await Assert.That(catalog.ToText()).IsEqualTo(
            "analysis-report|Parking|Analyze\n" +
            "catalog-reference-report|Parking|Lower\n" +
            "scaffolding|Parking|Lower\nsource-domain|Parking|Lower\n" +
            "entity|Parking/Garage|Lower\nsource-entity|Parking/Garage|Lower\n" +
            "entity|Parking/Permit|Lower\nsource-entity|Parking/Permit|Lower");
        await Assert.That(TypeNames(catalog.Find(ArtifactId.Create(["Parking", "Permit"], "entity"))!))
            .IsEquivalentTo(["Permit", "PermitStage"]);
        await Assert.That(TypeNames(catalog.Find(ArtifactId.Create(["Parking", "Garage"], "entity"))!))
            .IsEquivalentTo(["Garage"]);
        await Assert.That(TypeNames(catalog.Find(ArtifactId.Create(["Parking"], "scaffolding"))!))
            .Contains("DomainResult");
        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();
    }

    [Test]
    public async Task Lower_TreesTogetherAreTheWholeModule() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);

        var module = session.Lower(domain, analysis);

        var inTrees = session.ArtifactCatalog.Artifacts.Where(IsTree).SelectMany(TypeNames).Order(StringComparer.Ordinal);
        await Assert.That(inTrees.SequenceEqual(module.Select(t => t.Name).Order(StringComparer.Ordinal))).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(SampleDomains))]
    public async Task Lower_EveryTreeArtifact_PointsAtASourceElementThatExists(string relativePath) {
        var (session, domain, analysis) = EmitGoldenTests.AnalyzeSampleFile(relativePath);
        session.Lower(domain, analysis);
        var catalog = session.ArtifactCatalog;

        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();

        var trees = catalog.Artifacts.Where(IsTree).ToList();
        await Assert.That(trees.Count).IsGreaterThan(0);

        foreach (var tree in trees) {
            await Assert.That(tree.Descriptor.References.Count).IsEqualTo(1);
            var sourceId = tree.Descriptor.References[0];
            var source = catalog.Find(sourceId);
            await Assert.That(source).IsNotNull();

            if (tree.Descriptor.Id.Type == "entity") {
                await Assert.That(sourceId.Type).IsEqualTo("source-entity");
                var entity = domain.Types.OfType<Entity>().Single(e => e.Name == sourceId.Segments[^1]);
                await Assert.That(sourceId.Path).IsEqualTo($"{domain.Name}/{entity.Name}");
                await Assert.That(source!.Payload).IsSameReferenceAs(entity);
            }
            else {
                await Assert.That(sourceId.Type).IsEqualTo("source-domain");
                await Assert.That(sourceId.Path).IsEqualTo(domain.Name);
                await Assert.That(source!.Payload).IsSameReferenceAs(domain);
            }
        }
    }

    [Test]
    public async Task RemovingAnEntityFromTheDomain_MakesTheDanglingCheckReportItsTree() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);
        var module = session.Lower(domain, analysis);
        var withoutGarage = domain with {
            Types = [.. domain.Types.Where(t => t is not Entity { Name: "Garage" })],
        };

        var catalog = new ArtifactCatalog();
        DomainSession.RegisterSourceElements(catalog, withoutGarage);
        DomainSession.RegisterTrees(catalog, domain, module);

        var problems = catalog.FindDanglingOrWrongType();
        await Assert.That(problems).IsEquivalentTo([
            new ArtifactReferenceProblem(
                ArtifactId.Parse("Parking/Garage#entity"),
                ArtifactId.Parse("Parking/Garage#source-entity"),
                ArtifactReferenceProblemKind.WrongType),
        ]);
    }

    [Test]
    public async Task Lower_TreePayloadsCannotBeChangedByACaller() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);
        var module = session.Lower(domain, analysis);

        var tree = session.ArtifactCatalog.Find(ArtifactId.Create(["Parking", "Permit"], "entity"))!;

        var payload = (IList<TypeDefinitionNode>)tree.Payload!;
        await Assert.That(payload.IsReadOnly).IsTrue();
        await Assert.That(() => payload.Add(module[0])).Throws<NotSupportedException>();
        await Assert.That(() => payload.Clear()).Throws<NotSupportedException>();
        await Assert.That(ReferenceEquals(payload, module)).IsFalse();
    }

    [Test]
    public async Task Lower_DomainNameWithWhitespace_Throws() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);

        var ex = Assert.Throws<FormatException>(() => session.Lower(domain with { Name = "My Domain" }, analysis));

        await Assert.That(ex.Message).Contains("My Domain").And.Contains("no whitespace");
    }

    [Test]
    public async Task Emit_WritesOneFilePerEntityTreeThenTheScaffolding() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);

        var files = session.Emit(domain, analysis);

        await Assert.That(files.Select(f => f.FileName).SequenceEqual(["Permit.cs", "Garage.cs", "Poly.Types.cs"])).IsTrue();
    }

    [Test]
    public async Task Emit_FromManyThreadsOnOneSession_EachCallGetsTheFilesOfItsOwnDomain() {
        var (domainA, analysisA, session) = SliceCProducerLoopCatalogTests.Evolve("""
            domain Alpha

            Alpha: entity {
              Name: Text required
            }
            """);
        var (domainB, analysisB, _) = SliceCProducerLoopCatalogTests.Evolve("""
            domain Beta

            Beta: entity {
              Name: Text required
            }

            Gamma: entity {
              Name: Text required
            }
            """);

        var wrong = 0;
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, worker => {
            for (var i = 0; i < 150; i++) {
                var useA = (worker + i) % 2 == 0;
                var files = useA ? session.Emit(domainA, analysisA) : session.Emit(domainB, analysisB);
                string[] expected = useA ? ["Alpha.cs", "Poly.Types.cs"] : ["Beta.cs", "Gamma.cs", "Poly.Types.cs"];
                if (!files.Select(f => f.FileName).SequenceEqual(expected))
                    Interlocked.Increment(ref wrong);
            }
        });

        await Assert.That(wrong).IsEqualTo(0);
    }

    [Test]
    public async Task Analyze_EntityNamedLikeAnotherEntitysStageEnum_IsRefused() {
        // N4: Analyze rejects `{E}Stage` when E has stages before Lower can run.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SliceCProducerLoopCatalogTests.Evolve("""
                domain Clash

                Permit: entity {
                  Plate: Text required
                  Open: stage { }
                }

                PermitStage: entity {
                  Name: Text required
                }
                """));

        await Assert.That(ex.Message).Contains("PermitStage").And.Contains("stage enum");
    }

    [Test]
    public async Task Lower_EntityNamedLikeAnotherEntitysStageEnumName_IsRefusedEvenWithoutStages() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve("""
            domain Clash

            Permit: entity {
              Plate: Text required
            }

            PermitStage: entity {
              Name: Text required
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));

        await Assert.That(ex.Message).Contains("PermitStage").And.Contains("belongs to the trees");
    }

    [Test]
    public async Task Analyze_EntityNamedDomainResult_IsRefused() {
        // N4: Analyze rejects DomainResult before Lower can run.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SliceCProducerLoopCatalogTests.Evolve("""
                domain Clash

                DomainResult: entity {
                  Name: Text required
                }
                """));

        await Assert.That(ex.Message).Contains("DomainResult").And.Contains("scaffolding");
    }
}