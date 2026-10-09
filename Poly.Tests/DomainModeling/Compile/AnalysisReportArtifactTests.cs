using Poly.Analysis;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;

using Artifact = Poly.DomainModeling.Compile.Artifact;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// A4: Lower registers an analysis-report artifact; findings carry element id paths.
/// H2: Emit also registers a vm-analysis-report; Lower does not.
/// </summary>
public sealed class AnalysisReportArtifactTests {
    // Loan points at Patron with no inverse many → Loan is a non-root orphan (DMAGG001).
    private const string WarningsDomain = """
        domain Warehouse
        Patron: entity { Name: Text }
        Loan: entity {
          Amount: Number
          borrower: Patron
        }
        """;

    [Test]
    public async Task Lower_DomainWithWarnings_RegistersReportListingThem() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(WarningsDomain);
        await Assert.That(analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning)).IsTrue();

        session.Lower(domain, analysis);

        var artifact = session.ArtifactCatalog.Find(ArtifactId.Create(["Warehouse"], "analysis-report"));
        await Assert.That(artifact).IsNotNull();
        await Assert.That(artifact!.Descriptor.Producer).IsEqualTo("Analyze");
        var report = (AnalysisReport)artifact.Payload!;
        var warnings = report.Findings.Where(f => f.Severity == DiagnosticSeverity.Warning).ToList();
        await Assert.That(warnings.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(warnings.All(f => f.ElementPath.Length > 0)).IsTrue();
        await Assert.That(warnings.Any(f => f.ElementPath.StartsWith("Warehouse/", StringComparison.Ordinal)
            || f.ElementPath == "Warehouse")).IsTrue();
        await Assert.That(session.ArtifactCatalog.ToText()).Contains("analysis-report|Warehouse|Analyze");
    }

    [Test]
    public async Task Lower_DoesNotRegisterVmAnalysisReport() {
        const string dsl = """
            domain Spotless
            Item: entity {
              Name: Text required
              Open: stage { }
            }
            """;
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(dsl);
        session.Lower(domain, analysis);

        await Assert.That(session.ArtifactCatalog.Find(ArtifactId.Create(["Spotless"], "vm-analysis-report")))
            .IsNull();
        await Assert.That(session.ArtifactCatalog.ToText()).Contains("analysis-report|Spotless|Analyze");
        await Assert.That(session.ArtifactCatalog.ToText()).DoesNotContain("vm-analysis-report");
    }

    [Test]
    public async Task Emit_RegistersVmAnalysisReportListingErrors() {
        var (session, domain, analysis) = EmitGoldenTests.AnalyzeSampleFile("docs/probes/smoke/smoke.poly");
        var files = session.Emit(domain, analysis);

        await Assert.That(files.Count).IsGreaterThan(0);
        var artifact = session.ArtifactCatalog.Find(ArtifactId.Create([domain.Name], "vm-analysis-report"));
        await Assert.That(artifact).IsNotNull();
        await Assert.That(artifact!.Descriptor.Producer).IsEqualTo("Emit");
        var report = (AnalysisReport)artifact.Payload!;
        var errors = report.Findings.Where(f => f.Severity == DiagnosticSeverity.Error).ToList();
        await Assert.That(errors.Count).IsEqualTo(9);
        await Assert.That(session.ArtifactCatalog.ToText())
            .Contains($"vm-analysis-report|{domain.Name}|Emit");
        await Assert.That(session.ArtifactCatalog.Find(ArtifactId.Create([domain.Name], "analysis-report")))
            .IsNotNull();
    }

    [Test]
    public async Task Lower_CleanDomain_RegistersEmptyReport() {
        const string dsl = """
            domain Spotless
            Item: entity {
              Name: Text required
              Open: stage { }
            }
            """;
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(dsl);
        // Spotless may still carry hints (authoring suggestions); the report lists whatever Analyze produced.
        session.Lower(domain, analysis);

        var artifact = session.ArtifactCatalog.Find(ArtifactId.Create(["Spotless"], "analysis-report"))!;
        var report = (AnalysisReport)artifact.Payload!;
        await Assert.That(report.Findings.Count).IsEqualTo(analysis.Diagnostics.Count);
        await Assert.That(session.ArtifactCatalog.ToText()).Contains("analysis-report|Spotless|Analyze");
    }

    [Test]
    public async Task AnalysisReport_Findings_CallerCannotMutate() {
        var findings = new List<AnalysisFinding> {
            new("DMAGG001", DiagnosticSeverity.Warning, "orphan", "Warehouse/Bin")
        };
        var report = new AnalysisReport(findings);
        findings.Add(new("X", DiagnosticSeverity.Error, "late", "Warehouse"));

        await Assert.That(report.Findings.Count).IsEqualTo(1);
        var list = (IList<AnalysisFinding>)report.Findings;
        await Assert.That(list.IsReadOnly).IsTrue();
        await Assert.That(() => list.Add(new("Y", DiagnosticSeverity.Hint, "no", "Warehouse")))
            .Throws<NotSupportedException>();
        await Assert.That(() => list.Clear()).Throws<NotSupportedException>();
        await Assert.That(() => list[0] = new("Z", DiagnosticSeverity.Hint, "no", "Warehouse"))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task AnalysisReport_RejectsNullFindingInList() {
        AnalysisFinding[] withNull = [new("A", DiagnosticSeverity.Hint, "m", "D"), null!];
        await Assert.That(() => new AnalysisReport(withNull)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Finding_ElementPath_IsNearestNamedAncestor() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(WarningsDomain);
        session.Lower(domain, analysis);
        var report = (AnalysisReport)session.ArtifactCatalog
            .Find(ArtifactId.Create(["Warehouse"], "analysis-report"))!.Payload!;

        // DMAGG001 is reported on the entity itself → Warehouse/Bin.
        var orphan = report.Findings.FirstOrDefault(f => f.Code == DomainModelDiagnosticCodes.AggregateOrphan);
        await Assert.That(orphan).IsNotNull();
        await Assert.That(orphan!.ElementPath).IsEqualTo("Warehouse/Loan");
    }

    [Test]
    public async Task DomainElementPath_UnnamedNode_UsesNearestNamedAncestor() {
        var (domain, _, _) = SliceCProducerLoopCatalogTests.Evolve("""
            domain P
            E: entity {
              Open: stage {
                Go: action { assign Flag to true }
              }
              Flag: Boolean
            }
            """);
        var entity = domain.Types.OfType<Entity>().Single();
        var action = entity.Stages.Single().Actions.Single();
        var effect = action.Effects.Single();

        await Assert.That(DomainElementPath.Resolve(domain, effect)).IsEqualTo("P/E/Open/Go");
        await Assert.That(DomainElementPath.Resolve(domain, action)).IsEqualTo("P/E/Open/Go");
        await Assert.That(DomainElementPath.Resolve(domain, entity)).IsEqualTo("P/E");
        await Assert.That(DomainElementPath.Resolve(domain, domain)).IsEqualTo("P");
    }
}