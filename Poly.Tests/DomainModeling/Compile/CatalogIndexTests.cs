using Poly.DomainModeling.Compile;

using Artifact = Poly.DomainModeling.Compile.Artifact;

namespace Poly.Tests.DomainModeling.Compile;

public sealed class CatalogIndexTests {
    private const string Library = """
        domain Library

        Patron: entity {
          Name: Text required
          fines: many Fine
          Active: stage {
            AssessByType: action { }
          }
        }

        Fine: entity {
          Amount: Number
        }
        """;

    private const string Parking = """
        domain Parking

        Permit: entity {
          Plate: Text required
        }
        """;

    private const string EntityNamedLikeDomain = """
        domain Patron

        Patron: entity {
          Name: Text required
        }
        """;

    private static (CatalogIndex Index, ArtifactCatalog Catalog) IndexOf(string poly) {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(poly);
        session.Lower(domain, analysis);
        return (new CatalogIndex(session.ArtifactCatalog), session.ArtifactCatalog);
    }

    [Test]
    public async Task Tree_EntityName_ReturnsEntityArtifact() {
        var (index, catalog) = IndexOf(Library);
        var expected = catalog.Find(ArtifactId.Create(["Library", "Patron"], "entity"));

        await Assert.That(index.Tree("Patron")).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task Tree_DomainNameWithNoEntityOfThatName_ReturnsScaffoldingArtifact() {
        var (index, catalog) = IndexOf(Library);
        var expected = catalog.Find(ArtifactId.Create(["Library"], "scaffolding"));

        await Assert.That(index.Tree("Library")).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task Tree_UnknownName_ReturnsNull() {
        var (index, _) = IndexOf(Library);

        await Assert.That(index.Tree("NoSuch")).IsNull();
    }

    [Test]
    public async Task Tree_EntityAndDomainShareAName_ReturnsEntityTree() {
        var (index, catalog) = IndexOf(EntityNamedLikeDomain);
        var entity = catalog.Find(ArtifactId.Create(["Patron", "Patron"], "entity"));
        var scaffolding = catalog.Find(ArtifactId.Create(["Patron"], "scaffolding"));

        await Assert.That(index.Tree("Patron")).IsSameReferenceAs(entity);
        await Assert.That(index.Tree("Patron")).IsNotSameReferenceAs(scaffolding);
    }

    [Test]
    public async Task Method_PresentOnTree_ReturnsMethodFromPayload() {
        var (index, _) = IndexOf(Library);
        var expected = MethodOn(index.Tree("Patron")!, "AssessByType");

        await Assert.That(index.Method("Patron", "AssessByType")).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task Method_AbsentOrUnknownTree_ReturnsNull() {
        var (index, _) = IndexOf(Library);

        await Assert.That(index.Method("Patron", "NoSuch")).IsNull();
        await Assert.That(index.Method("NoSuch", "AssessByType")).IsNull();
    }

    [Test]
    public async Task Navigation_Present_ReturnsPropertyFromPayload() {
        var (index, _) = IndexOf(Library);
        var expected = PropertyOn(index.Tree("Patron")!, "Fines");

        await Assert.That(index.Navigation("Patron", "Fines")).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task Navigation_AbsentOrUnknownTree_ReturnsNull() {
        var (index, _) = IndexOf(Library);

        await Assert.That(index.Navigation("Patron", "NoSuch")).IsNull();
        await Assert.That(index.Navigation("NoSuch", "Fines")).IsNull();
    }

    [Test]
    public async Task Index_ReadsOnlyTheCatalogItWasBuiltFrom() {
        var (indexA, catalogA) = IndexOf(Library);
        var (indexB, catalogB) = IndexOf(Parking);

        await Assert.That(indexA.Tree("Patron"))
            .IsSameReferenceAs(catalogA.Find(ArtifactId.Create(["Library", "Patron"], "entity")));
        await Assert.That(indexA.Tree("Permit")).IsNull();
        await Assert.That(indexA.Tree("Parking")).IsNull();

        await Assert.That(indexB.Tree("Permit"))
            .IsSameReferenceAs(catalogB.Find(ArtifactId.Create(["Parking", "Permit"], "entity")));
        await Assert.That(indexB.Tree("Parking"))
            .IsSameReferenceAs(catalogB.Find(ArtifactId.Create(["Parking"], "scaffolding")));
        await Assert.That(indexB.Tree("Patron")).IsNull();
        await Assert.That(indexB.Tree("Library")).IsNull();
        await Assert.That(indexB.Method("Patron", "AssessByType")).IsNull();
        await Assert.That(indexB.Navigation("Patron", "Fines")).IsNull();
    }

    private static MethodDefinitionNode MethodOn(Artifact tree, string name) =>
        ((IReadOnlyList<TypeDefinitionNode>)tree.Payload!)
            .SelectMany(t => t.Methods ?? [])
            .Single(m => m.Name == name);

    private static PropertyDefinitionNode PropertyOn(Artifact tree, string name) =>
        ((IReadOnlyList<TypeDefinitionNode>)tree.Payload!)
            .SelectMany(t => t.Properties ?? [])
            .Single(p => p.Name == name);
}
