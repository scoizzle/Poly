namespace Poly.Tests.Syntax.Analysis;

public class AnalyzerDependencyScheduleTests {
    private sealed record StampMetadata(string From) : IAnalysisMetadata;

    private sealed class StampPass : INodeAnalyzer {
        public string PassName { get; }
        private readonly string _stamp;
        public StampPass(string name, string stamp) {
            PassName = name;
            _stamp = stamp;
        }
        public void Analyze(AnalysisContext context, Node node) =>
            context.SetMetadata(node, new StampMetadata(_stamp));
    }

    private sealed class ReadPass : INodeAnalyzer {
        public string PassName { get; }
        public string[] Dependencies { get; }
        public string? Seen;
        public ReadPass(string name, params string[] deps) {
            PassName = name;
            Dependencies = deps;
        }
        public void Analyze(AnalysisContext context, Node node) =>
            Seen = context.GetMetadata<StampMetadata>(node)?.From;
    }

    [Test]
    public async Task DeclaredDependency_CompletesBeforeDependentPass() {
        var first = new StampPass("first", "a");
        var second = new ReadPass("second", "first");
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(first)
            .AddAnalyzer(second)
            .Build();
        analyzer.Analyze(new Constant(0));
        await Assert.That(second.Seen).IsEqualTo("a");
    }

    [Test]
    public async Task IndependentPasses_BothComplete() {
        var left = new StampPass("left", "L");
        var right = new StampPass("right", "R");
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(left)
            .AddAnalyzer(right)
            .Build();
        var result = analyzer.Analyze(new Constant(0));
        var names = result.Telemetry.Passes.Select(p => p.PassName).ToHashSet();
        await Assert.That(result.Telemetry.Passes.Count).IsEqualTo(2);
        await Assert.That(names.SetEquals(["left", "right"])).IsTrue();
    }

    [Test]
    public async Task Passes_RunInInsertOrder_LaterPassWinsSharedSlot() {
        var first = new StampPass("first", "a");
        var second = new StampPass("second", "b");
        var third = new ReadPass("third", "first");
        var node = new Constant(0);
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(first)
            .AddAnalyzer(second)
            .AddAnalyzer(third)
            .Build();
        var result = analyzer.Analyze(node);
        var names = result.Telemetry.Passes.Select(p => p.PassName).ToArray();
        await Assert.That(names[0]).IsEqualTo("first");
        await Assert.That(names[1]).IsEqualTo("third");
        await Assert.That(names[2]).IsEqualTo("second");
        await Assert.That(third.Seen).IsEqualTo("a");
        await Assert.That(result.GetMetadata<StampMetadata>(node)?.From).IsEqualTo("b");
    }
}