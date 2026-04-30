using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Syntax.Analysis;

public class AnalyzerIncrementalInvalidationTests {
    [Test]
    public async Task Analyze_WithInvalidatedChild_AlsoInvalidatesAncestors() {
        var analyzer = CreateAnalyzer(new TestMetadataAnalyzer());
        var initialLeaf = new TestLeaf(1);
        var initialRoot = new TestParent(initialLeaf);

        var priorAnalysis = analyzer.Analyze(initialRoot);

        var updatedLeaf = initialLeaf with { Value = 2, Id = initialLeaf.Id };
        var updatedRoot = initialRoot with { Child = updatedLeaf, Id = initialRoot.Id };

        var result = analyzer.Analyze(updatedRoot, priorAnalysis, [updatedLeaf]);

        await Assert.That(result.GetMetadata<TestValueMetadata>(updatedRoot)?.Value).IsEqualTo(2);
    }

    [Test]
    public async Task Analyze_WithInvalidatedAncestor_AlsoInvalidatesDescendants() {
        var analyzer = CreateAnalyzer(new TestMetadataAnalyzer());
        var initialLeaf = new TestLeaf(1);
        var initialRoot = new TestParent(initialLeaf);

        var priorAnalysis = analyzer.Analyze(initialRoot);

        var updatedLeaf = initialLeaf with { Value = 2, Id = initialLeaf.Id };
        var updatedRoot = initialRoot with { Child = updatedLeaf, Id = initialRoot.Id };

        var result = analyzer.Analyze(updatedRoot, priorAnalysis, [updatedRoot]);

        await Assert.That(result.GetMetadata<TestValueMetadata>(updatedLeaf)?.Value).IsEqualTo(2);
    }

    [Test]
    public async Task Analyze_WhenPriorMissingTopologyMetadata_FallsBackToFreshFullAnalysis() {
        var analyzer = CreateAnalyzer(new ReuseSensitiveMetadataAnalyzer());
        var initialLeaf = new TestLeaf(1);
        var initialRoot = new TestParent(initialLeaf);

        var context = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);
        var staleMetadata = new NodeMetadataStore();
        staleMetadata.Set(initialRoot, new TestValueMetadata(999));
        staleMetadata.Set(initialLeaf, new TestValueMetadata(999));
        var priorAnalysisWithoutTopology = new AnalysisResult(context);

        var updatedLeaf = initialLeaf with { Value = 2, Id = initialLeaf.Id };
        var updatedRoot = initialRoot with { Child = updatedLeaf, Id = initialRoot.Id };

        var result = analyzer.Analyze(updatedRoot, priorAnalysisWithoutTopology, [updatedLeaf]);

        await Assert.That(result.GetMetadata<TestValueMetadata>(updatedRoot)?.Value).IsEqualTo(2);
        await Assert.That(result.GetMetadata<TestValueMetadata>(updatedLeaf)?.Value).IsEqualTo(2);
    }

    [Test]
    public async Task Analyze_WithCarryForwardDiagnostics_RemovesDiagnosticsForInvalidatedNodes() {
        var analyzer = CreateAnalyzer(new TestDiagnosticAnalyzer());

        var left = new TestLeaf(-1);
        var right = new TestLeaf(-1);
        var initialRoot = new TestBinaryParent(left, right);

        var priorAnalysis = analyzer.Analyze(initialRoot);

        var updatedRight = right with { Value = 1, Id = right.Id };
        var updatedRoot = initialRoot with { Right = updatedRight, Id = initialRoot.Id };

        var result = analyzer.Analyze(updatedRoot, priorAnalysis, [updatedRight]);

        await Assert.That(priorAnalysis.Diagnostics.Count).IsGreaterThan(0);
        await Assert.That(result.Diagnostics.Count).IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Node.Id).IsEqualTo(left.Id);
    }

    private static Analyzer CreateAnalyzer(params INodeAnalyzer[] testAnalyzers) {
        var builder = new AnalyzerBuilder(new NoopTypeDefinitionProvider())
            .UseIncrementalAnalysis();

        foreach (var analyzer in testAnalyzers) {
            builder.AddAnalyzer(analyzer);
        }

        return builder.Build();
    }

    private sealed class TestMetadataAnalyzer : INodeAnalyzer {
        public void Analyze(AnalysisContext context, Node node) {
            switch (node) {
                case TestLeaf leaf:
                    context.SetMetadata(node, new TestValueMetadata(leaf.Value));
                    break;
                case TestParent parent:
                    context.SetMetadata(node, new TestValueMetadata(((TestLeaf)parent.Child).Value));
                    break;
            }

            this.AnalyzeChildren(context, node);
        }
    }

    private sealed class ReuseSensitiveMetadataAnalyzer : INodeAnalyzer {
        public void Analyze(AnalysisContext context, Node node) {
            switch (node) {
                case TestLeaf leaf:
                    _ = context.GetOrAddMetadata(node, () => new TestValueMetadata(leaf.Value));
                    break;
                case TestParent parent:
                    _ = context.GetOrAddMetadata(node, () => new TestValueMetadata(((TestLeaf)parent.Child).Value));
                    break;
            }

            this.AnalyzeChildren(context, node);
        }
    }

    private sealed class TestDiagnosticAnalyzer : INodeAnalyzer {
        public void Analyze(AnalysisContext context, Node node) {
            if (node is TestLeaf leaf && leaf.Value < 0) {
                context.ReportError(leaf, "Negative leaf value", "NEG");
            }

            this.AnalyzeChildren(context, node);
        }
    }

    private sealed record TestLeaf(int Value) : Node;

    private sealed record TestParent(Node Child) : Node {
        public override IEnumerable<Node?> Children {
            get {
                yield return Child;
            }
        }
    }

    private sealed record TestBinaryParent(Node Left, Node Right) : Node {
        public override IEnumerable<Node?> Children {
            get {
                yield return Left;
                yield return Right;
            }
        }
    }

    private sealed record TestValueMetadata(int Value) : IAnalysisMetadata;

    private sealed class NoopTypeDefinitionProvider : ITypeDefinitionProvider {
        public ITypeDefinition? GetTypeDefinition(string name) => null;

        public ITypeDefinition? GetTypeDefinition(Type type) => null;
    }
}