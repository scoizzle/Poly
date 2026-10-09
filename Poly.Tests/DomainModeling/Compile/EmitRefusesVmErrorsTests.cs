using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// H2: a hand-built tree whose method body is <c>Not(new Constant(42))</c> makes
/// <see cref="DomainSession.ThrowIfVmHasErrors"/> throw the first VM error.
/// Emit itself stays fail-open on the listed sample domains (see <see cref="VmAnalyzerReportTests"/>).
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
}
