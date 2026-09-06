using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

/// <summary>F21: VariableAnalysisMetadata, shadow warning, captured/escaped sets.</summary>
public class VariableScopeTests {
    private static AnalysisResult Analyze(Node node) =>
        new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .Build()
            .Analyze(node);

    [Test]
    public async Task Root_HasVariableAnalysisMetadata() {
        var x = new Variable("x");
        var node = new Block([new Assignment(x, new Constant(1L)), x], [x]);
        var result = Analyze(node);
        var meta = result.GetMetadata<VariableAnalysisMetadata>(node);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.BlockScopes.ContainsKey(node)).IsTrue();
    }

    [Test]
    public async Task ShadowedInnerVariable_ReportsWarning() {
        var outer = new Variable("x");
        var inner = new Variable("x");
        var node = new Block([
            new Assignment(outer, new Constant(1L)),
            new Block([new Assignment(inner, new Constant(2L)), inner], [inner]),
            outer
        ], [outer]);
        var result = Analyze(node);
        await Assert.That(result.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning
            && d.Message.Contains("shadows", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task CapturedVariable_MarkedInMetadata() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(captured, new Constant(1L)),
            new Assignment(fn, new Lambda([], captured)),
            new Invoke(fn)
        ], [captured, fn]);
        var result = Analyze(node);
        var meta = result.GetMetadata<VariableAnalysisMetadata>(node);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.CapturedVariables.Contains(captured)).IsTrue();
    }
}
