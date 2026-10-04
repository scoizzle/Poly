using Poly.DomainModeling.Compile;

using Artifact = Poly.DomainModeling.Compile.Artifact;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>What <see cref="DomainSession.Lower"/> registers: one scaffolding tree and one tree per entity.</summary>
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

    private static IReadOnlyList<string> TypeNames(Artifact tree) =>
        ((IReadOnlyList<TypeDefinitionNode>)tree.Payload!).Select(t => t.Name).ToList();

    [Test]
    public async Task Lower_RegistersAScaffoldingTreeAndOneTreePerEntity() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);

        session.Lower(domain, analysis);

        var catalog = session.ArtifactCatalog;
        await Assert.That(catalog.ToText()).IsEqualTo(
            "scaffolding|Parking|Lower\nentity|Parking/Garage|Lower\nentity|Parking/Permit|Lower");
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

        var inTrees = session.ArtifactCatalog.Artifacts.SelectMany(TypeNames).Order(StringComparer.Ordinal);
        await Assert.That(inTrees.SequenceEqual(module.Select(t => t.Name).Order(StringComparer.Ordinal))).IsTrue();
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

        await Assert.That(() => session.Lower(domain with { Name = "My Domain" }, analysis)).Throws<FormatException>();
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
    public async Task Lower_EntityNamedLikeAnotherEntitysStageEnum_IsRefused() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve("""
            domain Clash

            Permit: entity {
              Plate: Text required
              Open: stage { }
            }

            PermitStage: entity {
              Name: Text required
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));

        await Assert.That(ex.Message).Contains("PermitStage");
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

        await Assert.That(ex.Message).Contains("PermitStage");
    }

    [Test]
    public async Task Lower_EntityNamedDomainResult_IsRefusedInsteadOfEmittingAnEmptyScaffolding() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve("""
            domain Clash

            DomainResult: entity {
              Name: Text required
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));

        await Assert.That(ex.Message).Contains("DomainResult");
    }
}
