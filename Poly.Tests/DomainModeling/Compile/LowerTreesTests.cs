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
    public async Task Emit_WritesOneFilePerEntityTreeThenTheScaffolding() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);

        var files = session.Emit(domain, analysis);

        await Assert.That(files.Select(f => f.FileName)).IsEquivalentTo(["Permit.cs", "Garage.cs", "Poly.Types.cs"]);
        await Assert.That(files[^1].FileName).IsEqualTo("Poly.Types.cs");
    }
}
