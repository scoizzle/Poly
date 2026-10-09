using Poly.Analysis;
using Poly.DomainModeling.Compile;

using Artifact = Poly.DomainModeling.Compile.Artifact;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// H3: dangling and wrong-type catalog references become AnalysisFinding Errors
/// on a separate catalog-reference-report.
/// </summary>
public sealed class CatalogReferenceDiagnosticTests {
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

    [Test]
    public async Task CatalogReferenceFindings_DanglingAndWrongType_AreBothErrors() {
        var catalog = new ArtifactCatalog();
        catalog.DeclareType("method", mayPointAt: ["type"]);
        catalog.DeclareType("type", mayPointAt: []);
        catalog.DeclareType("module", mayPointAt: []);
        catalog.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Parse("A#method"), "Test", [ArtifactId.Parse("Missing#type")]),
            null));
        catalog.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Parse("B#method"), "Test", [ArtifactId.Parse("Room#type")]),
            null));
        catalog.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Parse("Room#module"), "Test"),
            null));

        var problems = catalog.FindDanglingOrWrongType();
        await Assert.That(problems.Select(p => p.Kind).ToArray()).IsEquivalentTo([
            ArtifactReferenceProblemKind.Dangling,
            ArtifactReferenceProblemKind.WrongType,
        ]);

        var findings = DomainSession.CatalogReferenceFindings(catalog);
        await Assert.That(findings.Count).IsEqualTo(2);
        await Assert.That(findings[0].Code).IsEqualTo("Dangling");
        await Assert.That(findings[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(findings[0].ElementPath).IsEqualTo("A");
        await Assert.That(findings[0].Message).Contains("A#method").And.Contains("Missing#type");
        await Assert.That(findings[1].Code).IsEqualTo("WrongType");
        await Assert.That(findings[1].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(findings[1].ElementPath).IsEqualTo("B");
        await Assert.That(findings[1].Message).Contains("B#method").And.Contains("Room#type");
    }

    [Test]
    public async Task Lower_Parking_RegistersEmptyCatalogReferenceReport() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);
        session.Lower(domain, analysis);
        var catalog = session.ArtifactCatalog;

        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();
        var artifact = catalog.Find(ArtifactId.Create(["Parking"], "catalog-reference-report"));
        await Assert.That(artifact).IsNotNull();
        await Assert.That(artifact!.Descriptor.Producer).IsEqualTo("Lower");
        var report = (AnalysisReport)artifact.Payload!;
        await Assert.That(report.Findings).IsEmpty();
        await Assert.That(catalog.Find(ArtifactId.Create(["Parking"], "analysis-report"))).IsNotNull();
        await Assert.That(catalog.Find(ArtifactId.Create(["Parking"], "vm-analysis-report"))).IsNull();
    }

    [Test]
    public async Task Emit_Parking_RegistersEmptyCatalogReferenceReport() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);
        session.Emit(domain, analysis);
        var catalog = session.ArtifactCatalog;

        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();
        var artifact = catalog.Find(ArtifactId.Create(["Parking"], "catalog-reference-report"));
        await Assert.That(artifact).IsNotNull();
        await Assert.That(artifact!.Descriptor.Producer).IsEqualTo("Emit");
        var report = (AnalysisReport)artifact.Payload!;
        await Assert.That(report.Findings).IsEmpty();
        await Assert.That(catalog.Find(ArtifactId.Create(["Parking"], "analysis-report"))).IsNotNull();
        await Assert.That(catalog.Find(ArtifactId.Create(["Parking"], "vm-analysis-report"))).IsNotNull();
    }

    [Test]
    public async Task EmitThenRegisterWrongTypeFile_CatalogReferenceFindings_IsOneError() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Parking);
        session.Emit(domain, analysis);
        var catalog = session.ArtifactCatalog;
        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();

        catalog.Register(ContributedFile.Create(
            domain,
            "extra.cs",
            "class Extra {}",
            "Test",
            [ArtifactId.Create([domain.Name], "entity")]));

        var problem = catalog.FindDanglingOrWrongType().Single();
        await Assert.That(problem.Kind).IsEqualTo(ArtifactReferenceProblemKind.WrongType);
        await Assert.That(problem.From).IsEqualTo(ArtifactId.Parse("Parking/extra.cs#file"));

        var findings = DomainSession.CatalogReferenceFindings(catalog);
        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Code).IsEqualTo("WrongType");
        await Assert.That(findings[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(findings[0].ElementPath).IsEqualTo("Parking/extra.cs");
        await Assert.That(findings[0].Message).Contains("Parking/extra.cs#file").And.Contains("Parking#entity");
    }
}
