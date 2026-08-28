namespace Poly.Tests.Syntax.Analysis;

public class AnalyzerDiagnosticsTests {
    [Test]
    public async Task Analyze_WhenSameDiagnosticReportedTwice_DeduplicatesByNodeSeverityCodeAndMessage() {
        var analyzer = new AnalyzerBuilder().AddAnalyzer(new DuplicateDiagnosticAnalyzer()).Build();
        var leaf = new TestLeaf(1);

        var result = analyzer.Analyze(leaf);

        await Assert.That(result.Diagnostics.Count).IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Code).IsEqualTo("DUP");
        await Assert.That(result.Diagnostics[0].Message).IsEqualTo("Duplicate diagnostic");
    }

    [Test]
    public async Task Analyze_WhenPassesAreNamed_TelemetryRecordsEachPass() {
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(new NoopAnalyzer())
            .AddAnalyzer(new DuplicateDiagnosticAnalyzer())
            .Build();

        var result = analyzer.Analyze(new TestLeaf(1));

        var names = result.Telemetry.Passes.Select(p => p.PassName).ToHashSet();
        await Assert.That(result.Telemetry.Passes.Count).IsEqualTo(2);
        await Assert.That(names.SetEquals(["TestNoop", "TestDuplicateDiagnostic"])).IsTrue();
    }

    private sealed class NoopAnalyzer : INodeAnalyzer {
        public const string Id = "TestNoop";
        public string PassName => Id;
        public void Analyze(AnalysisContext context, Node node) =>
            this.AnalyzeChildren(context, node);
    }

    private sealed class DuplicateDiagnosticAnalyzer : INodeAnalyzer {
        public const string Id = "TestDuplicateDiagnostic";
        public string PassName => Id;
        public void Analyze(AnalysisContext context, Node node) {
            if (node is TestLeaf) {
                context.ReportError(node, "Duplicate diagnostic", "DUP");
                context.ReportError(node, "Duplicate diagnostic", "DUP");
            }

            this.AnalyzeChildren(context, node);
        }
    }

    private sealed record TestLeaf(int Value) : Node;
}