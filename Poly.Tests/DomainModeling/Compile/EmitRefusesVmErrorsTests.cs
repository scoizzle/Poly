using Poly.DomainModeling.Analysis;
using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// H2: a hand-built tree whose method body is <c>Not(new Constant(42))</c> makes
/// <see cref="DomainSession.ThrowIfVmHasErrors"/> throw the first VM error, and
/// <see cref="DomainSession.Emit"/> refuses to produce files when the lowered module
/// carries a VM error (injected through the <see cref="RuntimeAnalysisCache.ReplaceModule"/>
/// test hook).
/// </summary>
public sealed class EmitRefusesVmErrorsTests {
    [Test]
    public async Task ThrowIfVmHasErrors_NotOnInteger_ThrowsFirstVmError() {
        var type = new TypeDefinitionNode(
            "Broken",
            Methods: [
                new MethodDefinitionNode(
                    "Bad",
                    new PrimitiveTypeReference(PrimitiveType.Boolean),
                    Body: new Not(new Constant(42)))
            ]);

        var analysis = Interpreter.Analyzer.Analyze(new CompilationUnitNode([], null, [type], null));
        await Assert.That(analysis.HasErrors).IsTrue();
        var first = analysis.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message;

        var ex = Assert.Throws<InvalidOperationException>(() => DomainSession.ThrowIfVmHasErrors(analysis));
        await Assert.That(ex!.Message).IsEqualTo(first);
    }

    [Test]
    public async Task ThrowIfVmHasErrors_NoErrors_DoesNotThrow() {
        var type = new TypeDefinitionNode(
            "Ok",
            Methods: [
                new MethodDefinitionNode(
                    "Flag",
                    new PrimitiveTypeReference(PrimitiveType.Boolean),
                    Body: new Not(new Constant(false)))
            ]);

        var analysis = Interpreter.Analyzer.Analyze(new CompilationUnitNode([], null, [type], null));
        await Assert.That(analysis.HasErrors).IsFalse();

        DomainSession.ThrowIfVmHasErrors(analysis);
    }

    [Test]
    public async Task Emit_OnVmError_ThrowsAfterRegisteringReport() {
        var (session, domain, analysis) =
            EmitGoldenTests.AnalyzeSampleFile("docs/probes/smoke/smoke.poly");

        // First Emit caches the lowered module and succeeds.
        var files = session.Emit(domain, analysis);
        await Assert.That(files.Count).IsGreaterThan(0);

        // Replace the cached module with one whose method body is Not(Constant(42)),
        // reusing the same Domain/Session/Analysis references so the cache is not reset.
        var broken = new TypeDefinitionNode(
            "Broken",
            Methods: [
                new MethodDefinitionNode(
                    "Bad",
                    new PrimitiveTypeReference(PrimitiveType.Boolean),
                    Body: new Not(new Constant(42)))
            ]);
        RuntimeAnalysisCache.ReplaceModule(domain, [broken]);

        var firstVmError = Interpreter.Analyzer.Analyze(
                new CompilationUnitNode([], null, [broken], null))
            .Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message;

        var ex = Assert.Throws<InvalidOperationException>(() => session.Emit(domain, analysis));
        await Assert.That(ex!.Message).IsEqualTo(firstVmError);

        // The report is registered on the session before the throw.
        var report = session.ArtifactCatalog.Find(ArtifactId.Create([domain.Name], "vm-analysis-report"));
        await Assert.That(report).IsNotNull();
        await Assert.That(((AnalysisReport)report!.Payload!).Findings
            .Any(f => f.Severity == DiagnosticSeverity.Error)).IsTrue();

        // A refused Emit keeps a complete catalog: the reference report is registered
        // on the refusal path as well as after a successful print.
        var catalogReport = session.ArtifactCatalog.Find(
            ArtifactId.Create([domain.Name], "catalog-reference-report"));
        await Assert.That(catalogReport).IsNotNull();
    }
}