using Poly.Interpretation.CSharp;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// A5b: every printed file artifact references the tree it was printed from.
/// <see cref="AllowedWithoutTreeReference"/> is the only exception.
/// </summary>
public sealed class EmitFileReferenceTests {
    /// <summary>Printed file names allowed to have no tree reference. demo.http only.</summary>
    private static readonly string[] AllowedWithoutTreeReference = ["demo.http"];

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

    private const string Hosted = """
        domain Catalog
        uses temporal
        uses storage
        uses sqlite
        uses http

        Item: entity {
          Name: Text required
          Qty: Number
        }
        """;

    [Test]
    public async Task Emit_RegistersEachCsFile_WithAReferenceToItsTree() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);

        var files = session.Emit(domain, analysis);
        var catalog = session.ArtifactCatalog;

        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();
        foreach (var (fileName, source) in files) {
            var artifact = catalog.Artifacts.Single(a =>
                a.Descriptor.Id.Type == ContributedFile.Type && ContributedFile.FileName(a) == fileName);
            await Assert.That(artifact.Descriptor.Producer).IsEqualTo("Emit");
            await Assert.That(ContributedFile.Text(artifact)).IsEqualTo(source);
            var target = artifact.Descriptor.References.Single();
            var tree = catalog.Find(target);
            await Assert.That(tree).IsNotNull();
            await Assert.That(tree!.Descriptor.Id.Type).IsEqualTo(target.Type);
            if (fileName == "Poly.Types.cs") {
                await Assert.That(target.Type).IsEqualTo("scaffolding");
                await Assert.That(target.Path).IsEqualTo(domain.Name);
            }
            else {
                await Assert.That(target.Type).IsEqualTo("entity");
                await Assert.That(target.Segments[^1] + ".cs").IsEqualTo(fileName);
            }
            await Assert.That(tree.Payload is IReadOnlyList<TypeDefinitionNode>).IsTrue();
        }
    }

    [Test]
    public async Task Compile_EveryPrintedFile_ReferencesItsTree_ExceptDemoHttp() {
        await Assert.That(AllowedWithoutTreeReference).IsEquivalentTo(["demo.http"]);

        var result = new Compiler().Compile(Hosted, CompileMode.Entities, DbmsPack.Sqlite);
        await Assert.That(result.Success).IsTrue().Because(
            result.Errors is null ? "" : string.Join("; ", result.Errors));
        var catalog = result.Catalog!;
        var files = catalog.Artifacts.Where(a => a.Descriptor.Id.Type == ContributedFile.Type).ToList();
        await Assert.That(files.Select(ContributedFile.FileName).ToList()).Contains("Item.cs");
        await Assert.That(files.Select(ContributedFile.FileName).ToList()).Contains("Poly.Types.cs");
        await Assert.That(files.Select(ContributedFile.FileName).ToList()).Contains("Program.cs");
        await Assert.That(files.Select(ContributedFile.FileName).ToList()).Contains("demo.http");
        await Assert.That(files.Any(f => ContributedFile.FileName(f).EndsWith("DbContext.cs", StringComparison.Ordinal)))
            .IsTrue();

        foreach (var file in files) {
            var name = ContributedFile.FileName(file);
            if (AllowedWithoutTreeReference.Contains(name, StringComparer.Ordinal)) {
                await Assert.That(file.Descriptor.References).IsEmpty();
                continue;
            }

            var target = file.Descriptor.References.Single();
            var tree = catalog.Find(target);
            await Assert.That(tree).IsNotNull();
            await Assert.That(tree!.Descriptor.Id.Type).IsEqualTo(target.Type);
            await Assert.That(target.Type).IsEqualTo(ExpectedTreeType(name));
            if (tree.Payload is CompilationUnitNode unit)
                await Assert.That(ContributedFile.Text(file)).IsEqualTo(new CSharpGenerator().Generate(unit));
        }

        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();
    }

    [Test]
    public async Task Emit_FilePointingAtTheWrongTreeType_FailsTheDanglingCheck() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);
        session.Emit(domain, analysis);
        var catalog = session.ArtifactCatalog;
        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();

        // Path Parking exists (scaffolding, source-domain), but not as an entity tree.
        catalog.Register(ContributedFile.Create(
            domain,
            "extra.cs",
            "class Extra {}",
            "Test",
            [ArtifactId.Create([domain.Name], "entity")]));

        var problem = catalog.FindDanglingOrWrongType().Single();
        await Assert.That(problem.Kind).IsEqualTo(ArtifactReferenceProblemKind.WrongType);
        await Assert.That(problem.From).IsEqualTo(ArtifactId.Parse("Parking/extra.cs#file"));
        await Assert.That(problem.Target).IsEqualTo(ArtifactId.Parse("Parking#entity"));
    }

    private static string ExpectedTreeType(string fileName) => fileName switch {
        "Poly.Types.cs" => "scaffolding",
        "Program.cs" => GeneratedTree.Type,
        _ when fileName.EndsWith("DbContext.cs", StringComparison.Ordinal) => GeneratedTree.Type,
        _ => "entity",
    };
}