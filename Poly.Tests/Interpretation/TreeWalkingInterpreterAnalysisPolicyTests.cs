using Poly.Interpretation.Analysis;
using Poly.Interpretation.TreeWalking;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

public class TreeWalkingInterpreterAnalysisPolicyTests {
    private sealed class WarningAnalyzer(string message) : INodeAnalyzer {
        public void Analyze(AnalysisContext context, Node node) {
            context.ReportWarning(node, message, "TEST_WARNING");
            this.AnalyzeChildren(context, node);
        }
    }

    [Test]
    public async Task PrecomputedAnalysis_WarningsAsErrors_BlocksEvenInBalancedMode() {
        var ast = new Add(new Constant(1), new Constant(2));

        var warningAnalysis = new AnalyzerBuilder()
            .AddAnalyzer(new WarningAnalyzer("warning"))
            .Build()
            .Analyze(ast, AnalysisSettings.Default.With(new AnalysisDiagnosticConfiguration {
                TreatWarningsAsErrors = true
            }));

        var walker = new TreeWalkingInterpreter(
            warningAnalysis,
            null,
            InterpretationAnalysisSettings.ForMode(InterpretationAnalysisMode.Balanced));

        var ex = await Assert.That(() => walker.Evaluate(ast))
            .Throws<InvalidOperationException>();
        await Assert.That(ex!.Message).Contains("Analysis failed before interpretation could start");
    }

    [Test]
    public async Task StrictInterpreterSettings_OverrideLenientPrecomputedAnalysis() {
        var ast = new Add(new Constant(3), new Constant(4));

        var warningAnalysis = new AnalyzerBuilder()
            .AddAnalyzer(new WarningAnalyzer("warning"))
            .Build()
            .Analyze(ast, AnalysisSettings.Default.With(new AnalysisDiagnosticConfiguration {
                TreatWarningsAsErrors = false
            }));

        var walker = new TreeWalkingInterpreter(
            warningAnalysis,
            null,
            InterpretationAnalysisSettings.ForMode(InterpretationAnalysisMode.Strict));

        var ex = await Assert.That(() => walker.Evaluate(ast))
            .Throws<InvalidOperationException>();
        await Assert.That(ex!.Message).Contains("Analysis failed before interpretation could start");
    }

    [Test]
    public async Task ResumeWithRefinedAnalysis_StrictPolicyStillBlocksWarnings() {
        var ast = new Block([
            new SuspendNode(new Constant("checkpoint"), "RefineHere"),
            new Add(new Constant(5), new Constant(6))
        ]);

        var strictSettings = InterpretationAnalysisSettings.ForMode(InterpretationAnalysisMode.Strict);
        var walker = new TreeWalkingInterpreter(null, null, strictSettings);

        var first = walker.Evaluate(ast);
        await Assert.That(first.HasValue).IsTrue();
        await Assert.That(first.Value).IsTypeOf<SuspendedExecution>();

        var refinedWarningAnalysis = new AnalyzerBuilder()
            .AddAnalyzer(new WarningAnalyzer("warning"))
            .Build()
            .Analyze(ast, AnalysisSettings.Default.With(new AnalysisDiagnosticConfiguration {
                TreatWarningsAsErrors = false
            }));

        var ex = await Assert.That(() => walker.Resume(refinedWarningAnalysis))
            .Throws<InvalidOperationException>();
        await Assert.That(ex!.Message).Contains("Analysis failed before interpretation could start");
    }

    [Test]
    public async Task BalancedSettings_WithLenientPrecomputedWarningAnalysis_AllowsExecution() {
        var ast = new Add(new Constant(7), new Constant(8));

        var warningAnalysis = new AnalyzerBuilder()
            .AddAnalyzer(new WarningAnalyzer("warning"))
            .Build()
            .Analyze(ast, AnalysisSettings.Default.With(new AnalysisDiagnosticConfiguration {
                TreatWarningsAsErrors = false
            }));

        var walker = new TreeWalkingInterpreter(
            warningAnalysis,
            null,
            InterpretationAnalysisSettings.ForMode(InterpretationAnalysisMode.Balanced));

        var result = walker.Evaluate(ast);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(15);
    }
}