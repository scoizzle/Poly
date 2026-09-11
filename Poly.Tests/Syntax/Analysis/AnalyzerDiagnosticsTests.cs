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

    [Test]
    public async Task ReportError_SetsHasErrors() {
        var context = AnalysisContext.CreateDefault();
        context.ReportError(new Constant(0), "boom", "X");
        await Assert.That(context.HasErrors).IsTrue();
        await Assert.That(context.Diagnostics.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ReportWarning_DoesNotSetHasErrors() {
        var context = AnalysisContext.CreateDefault();
        context.ReportWarning(new Constant(0), "hmm", "W");
        await Assert.That(context.HasErrors).IsFalse();
        await Assert.That(context.Diagnostics.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Analyze_WhenStopOnStructuralErrors_AndErrorReported_SkipsLaterPasses() {
        var later = new CountingAnalyzer("later");
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(new ReportPass("first", "A"))
            .AddAnalyzer(later)
            .Build(AnalysisOptions.StopOnStructuralErrors);

        var result = analyzer.Analyze(new Constant(0));

        await Assert.That(later.Calls).IsEqualTo(0);
        await Assert.That(result.Telemetry.Passes.Count).IsEqualTo(1);
        await Assert.That(result.HasErrors).IsTrue();
    }

    [Test]
    public async Task Analyze_WhenFailFast_AndErrorReported_SkipsLaterPasses() {
        var later = new CountingAnalyzer("later");
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(new ReportPass("first", "A"))
            .AddAnalyzer(later)
            .Build(new AnalysisOptions { Mode = AnalysisMode.FailFast });

        var result = analyzer.Analyze(new Constant(0));

        await Assert.That(later.Calls).IsEqualTo(0);
        await Assert.That(result.Telemetry.Passes.Count).IsEqualTo(1);
        await Assert.That(result.HasErrors).IsTrue();
    }

    [Test]
    public async Task Analyze_WhenFullMode_AndErrorReported_RunsLaterPasses() {
        var later = new CountingAnalyzer("later");
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(new ReportPass("first", "A"))
            .AddAnalyzer(later)
            .Build();

        var result = analyzer.Analyze(new Constant(0));

        await Assert.That(later.Calls).IsEqualTo(1);
        await Assert.That(result.Telemetry.Passes.Count).IsEqualTo(2);
        await Assert.That(result.HasErrors).IsTrue();
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

    private sealed class CountingAnalyzer : INodeAnalyzer {
        public string PassName { get; }
        public int Calls;
        public CountingAnalyzer(string name) => PassName = name;
        public void Analyze(AnalysisContext context, Node node) => Calls++;
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