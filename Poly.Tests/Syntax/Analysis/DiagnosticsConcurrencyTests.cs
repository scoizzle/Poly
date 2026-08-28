namespace Poly.Tests.Syntax.Analysis;

public class DiagnosticsConcurrencyTests {
    private sealed class ReportPass : INodeAnalyzer {
        public string PassName { get; }
        private readonly string _code;
        public ReportPass(string name, string code) {
            PassName = name;
            _code = code;
        }
        public void Analyze(AnalysisContext context, Node node) =>
            context.ReportError(node, $"from {PassName}", _code);
    }

    [Test]
    public async Task ReportDiagnostic_FromManyThreads_AllSurviveUntilDistinct() {
        var node = new Constant(0);
        var context = AnalysisContext.CreateDefault();

        await Parallel.ForAsync(0, 32, (i, _) => {
            context.ReportError(node, $"msg-{i}", "CONCUR");
            return ValueTask.CompletedTask;
        });

        await Assert.That(context.Diagnostics.Count).IsEqualTo(32);
        await Assert.That(context.Diagnostics.Select(d => d.Message).Distinct().Count()).IsEqualTo(32);
    }

    [Test]
    public async Task IndependentPasses_SameNode_BothDiagnosticsPresent() {
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(new ReportPass("a", "A"))
            .AddAnalyzer(new ReportPass("b", "B"))
            .Build();
        var result = analyzer.Analyze(new Constant(0));
        await Assert.That(result.Diagnostics.Count).IsEqualTo(2);
        var codes = result.Diagnostics.Select(d => d.Code).ToHashSet();
        await Assert.That(codes.SetEquals(["A", "B"])).IsTrue();
    }
}