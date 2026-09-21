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

    [Test]
    public async Task Build_IsRegistrationOrder() {
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(new StampPass("first", "a"))
            .AddAnalyzer(new StampPass("second", "b"))
            .AddAnalyzer(new StampPass("third", "c"))
            .Build();
        var names = analyzer.PassNames.ToArray();
        await Assert.That(string.Join(",", names)).IsEqualTo("first,second,third");
    }

    [Test]
    public async Task LaterPass_OverwritesSharedMetadata() {
        var node = new Constant(0);
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(new StampPass("first", "a"))
            .AddAnalyzer(new StampPass("second", "b"))
            .Build();
        var result = analyzer.Analyze(node);
        await Assert.That(result.GetMetadata<StampMetadata>(node)?.From).IsEqualTo("b");
    }

    [Test]
    public async Task AddAnalyzer_DuplicatePassName_Throws() {
        var builder = new AnalyzerBuilder().AddAnalyzer(new StampPass("same", "a"));
        await Assert.That(() => builder.AddAnalyzer(new StampPass("same", "b")))
            .ThrowsExactly<InvalidOperationException>()
            .WithMessageContaining("same");
    }

    [Test]
    public async Task AddAnalyzer_EmptyPassName_Throws() {
        await Assert.That(() =>
            new AnalyzerBuilder().AddAnalyzer(new StampPass(" ", "a")))
            .ThrowsExactly<ArgumentException>();
    }
}