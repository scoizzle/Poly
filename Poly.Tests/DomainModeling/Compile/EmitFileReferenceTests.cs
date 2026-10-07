using Poly.DomainModeling.Compile;

using Artifact = Poly.DomainModeling.Compile.Artifact;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// A5b: every printed C# file artifact points at the tree it was printed from.
/// <c>demo.http</c> is the only printed type allowed to have no tree.
/// </summary>
public sealed class EmitFileReferenceTests {
    // Test constant. The product list must match this, and only this.
    private static readonly string[] TypesAllowedWithoutTreeReference = ["http-file"];

    private static readonly string[] TreeTypes = ["entity", "scaffolding", "host-tree"];

    [Test]
    public async Task TypesAllowedWithoutTreeReference_IsHttpFileOnly() {
        await Assert.That(PrintedArtifactRules.TypesAllowedWithoutTreeReference.ToArray())
            .IsEquivalentTo(TypesAllowedWithoutTreeReference);
        await Assert.That(PrintedArtifactRules.TreeReferenceTypes.ToArray()).IsEquivalentTo(TreeTypes);
    }

    [Test]
    public async Task Emit_EachPrintedFile_ReferencesItsTree() {
        var (session, domain, analysis) = EmitGoldenTests.AnalyzeSampleFile(
            "docs/probes/dogfood/simulate-create-type.poly");
        session.Emit(domain, analysis);
        var catalog = session.ArtifactCatalog;

        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();

        var files = catalog.Artifacts.Where(a => a.Descriptor.Id.Type == ContributedFile.Type).ToList();
        await Assert.That(files.Count).IsGreaterThan(0);
        foreach (var file in files) {
            await Assert.That(file.Descriptor.References.Count).IsEqualTo(1);
            var target = file.Descriptor.References[0];
            await Assert.That(TreeTypes.Contains(target.Type)).IsTrue();
            await Assert.That(catalog.Find(target)).IsNotNull();
            var expectedTree = file.Descriptor.Id.Segments[^1] == "Poly.Types.cs"
                ? "scaffolding"
                : "entity";
            await Assert.That(target.Type).IsEqualTo(expectedTree);
        }
    }

    [Test]
    public async Task FileReference_WrongTreeType_IsWrongType() {
        var catalog = new ArtifactCatalog();
        catalog.DeclareType(HostTree.Type, mayPointAt: []);
        catalog.DeclareType("entity", mayPointAt: []);
        catalog.DeclareType(ContributedFile.Type, mayPointAt: PrintedArtifactRules.TreeReferenceTypes);
        catalog.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Create(["Hotel", "Room"], HostTree.Type), "Test"),
            Payload: null));
        catalog.Register(new Artifact(
            new ArtifactDescriptor(
                ArtifactId.Create(["Hotel", "Room.cs"], ContributedFile.Type),
                "Test",
                [ArtifactId.Create(["Hotel", "Room"], "entity")]),
            Payload: "class Room {}"));

        var problems = catalog.FindDanglingOrWrongType();
        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0].Kind).IsEqualTo(ArtifactReferenceProblemKind.WrongType);
        await Assert.That(problems[0].Target.Type).IsEqualTo("entity");
    }
}
