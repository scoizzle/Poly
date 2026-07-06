using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Syntax.Analysis;

public class AnalyzerIncrementalInvalidationTests {
    private static Analyzer CreateAnalyzerWith(Func<AnalyzerBuilder, AnalyzerBuilder> configure) =>
        configure(new AnalyzerBuilder()).Build();

    private static Analyzer DefaultIncrementalAnalyzer<T>() where T : INodeAnalyzer, new() =>
        new AnalyzerBuilder().UseIncrementalAnalysis().AddAnalyzer(new T()).Build();

    [Test]
    public async Task Analyze_WithInvalidatedChild_AlsoInvalidatesAncestors() {
        var analyzer = DefaultIncrementalAnalyzer<TestMetadataAnalyzer>();
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
        var analyzer = DefaultIncrementalAnalyzer<TestMetadataAnalyzer>();
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
        var analyzer = CreateAnalyzerWith(b => b.UseIncrementalAnalysis().AddAnalyzer(new ReuseSensitiveMetadataAnalyzer()));
        var initialLeaf = new TestLeaf(1);
        var initialRoot = new TestParent(initialLeaf);

        var context = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);
        var staleMetadata = new NodeMetadataStore();
        staleMetadata.Set(initialRoot, new TestValueMetadata(999));
        staleMetadata.Set(initialLeaf, new TestValueMetadata(999));
        var priorAnalysisWithoutTopology = new AnalysisResult(context, AnalysisTelemetry.Empty);

        var updatedLeaf = initialLeaf with { Value = 2, Id = initialLeaf.Id };
        var updatedRoot = initialRoot with { Child = updatedLeaf, Id = initialRoot.Id };

        var result = analyzer.Analyze(updatedRoot, priorAnalysisWithoutTopology, [updatedLeaf]);

        await Assert.That(result.GetMetadata<TestValueMetadata>(updatedRoot)?.Value).IsEqualTo(2);
        await Assert.That(result.GetMetadata<TestValueMetadata>(updatedLeaf)?.Value).IsEqualTo(2);
    }

    [Test]
    public async Task Analyze_WithCarryForwardDiagnostics_RemovesDiagnosticsForInvalidatedNodes() {
        var analyzer = DefaultIncrementalAnalyzer<TestDiagnosticAnalyzer>();

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

    [Test]
    public async Task Analyze_WhenSameDiagnosticReportedTwice_DeduplicatesByNodeSeverityCodeAndMessage() {
        var analyzer = DefaultIncrementalAnalyzer<DuplicateDiagnosticAnalyzer>();
        var leaf = new TestLeaf(1);

        var result = analyzer.Analyze(leaf);

        await Assert.That(result.Diagnostics.Count).IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Code).IsEqualTo("DUP");
        await Assert.That(result.Diagnostics[0].Message).IsEqualTo("Duplicate diagnostic");
    }

    [Test]
    public async Task Analyze_WhenAnalyzerRevisitsSameNode_MetadataSkipPreventsDuplicateWork() {
        var analyzer = DefaultIncrementalAnalyzer<DuplicateVisitAnalyzer>();
        var leaf = new TestLeaf(1);

        var result = analyzer.Analyze(leaf);
        var metadata = result.GetMetadata<TestVisitCountMetadata>(leaf);

        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Analyze_WhenPassesAreNamed_TelemetryPreservesExecutionOrder() {
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(new NoopAnalyzer())
            .AddAnalyzer(new TestMetadataAnalyzer())
            .Build();

        var result = analyzer.Analyze(new TestLeaf(1));

        await Assert.That(result.Telemetry.Passes.Count).IsEqualTo(2);
        await Assert.That(result.Telemetry.Passes[0].PassName).IsEqualTo("TestNoop");
        await Assert.That(result.Telemetry.Passes[1].PassName).IsEqualTo("TestMetadata");
    }

    [Test]
    public async Task Analyze_IncrementalRun_CapturesIncrementalTelemetry() {
        var analyzer = DefaultIncrementalAnalyzer<TestMetadataAnalyzer>();
        var initialLeaf = new TestLeaf(1);
        var initialRoot = new TestParent(initialLeaf);
        var priorAnalysis = analyzer.Analyze(initialRoot);

        var updatedLeaf = initialLeaf with { Value = 2, Id = initialLeaf.Id };
        var updatedRoot = initialRoot with { Child = updatedLeaf, Id = initialRoot.Id };
        var result = analyzer.Analyze(updatedRoot, priorAnalysis, [updatedLeaf]);

        await Assert.That(result.Telemetry.Incremental).IsTrue();
        await Assert.That(result.Telemetry.InvalidatedNodeCount).IsEqualTo(1);
    }

    private sealed class NoopAnalyzer : INodeAnalyzer {
        public const string Id = "TestNoop";
        public string PassName => Id;
        public string[] Dependencies => [];
        public void Analyze(AnalysisContext context, Node node) {
            this.AnalyzeChildren(context, node);
        }
    }

    private sealed class TestMetadataAnalyzer : INodeAnalyzer {
        public const string Id = "TestMetadata";
        public string PassName => Id;
        public string[] Dependencies => [];
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
        public const string Id = "TestReuseSensitive";
        public string PassName => Id;
        public string[] Dependencies => [];
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
        public const string Id = "TestDiagnostic";
        public string PassName => Id;
        public string[] Dependencies => [];
        public void Analyze(AnalysisContext context, Node node) {
            if (node is TestLeaf leaf && leaf.Value < 0) {
                context.ReportError(leaf, "Negative leaf value", "NEG");
            }

            this.AnalyzeChildren(context, node);
        }
    }

    private sealed class DuplicateDiagnosticAnalyzer : INodeAnalyzer {
        public const string Id = "TestDuplicateDiagnostic";
        public string PassName => Id;
        public string[] Dependencies => [];
        public void Analyze(AnalysisContext context, Node node) {
            if (node is TestLeaf leaf) {
                context.ReportError(leaf, "Duplicate diagnostic", "DUP");
                context.ReportError(leaf, "Duplicate diagnostic", "DUP");
            }

            this.AnalyzeChildren(context, node);
        }
    }

    private sealed class DuplicateVisitAnalyzer : INodeAnalyzer {
        public const string Id = "TestDuplicateVisit";
        public string PassName => Id;
        public string[] Dependencies => [];
        public void Analyze(AnalysisContext context, Node node) {
            if (!context.TryBeginAnalyzerVisit<DuplicateVisitAnalyzer>(node)) {
                return;
            }

            if (node is TestLeaf leaf) {
                var visit = context.GetMetadata<TestVisitCountMetadata>(leaf)?.Count ?? 0;
                context.SetMetadata(leaf, new TestVisitCountMetadata(visit + 1));

                Analyze(context, leaf);
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

    private sealed record TestVisitCountMetadata(int Count) : IAnalysisMetadata;

    private sealed class NoopTypeDefinitionProvider : ITypeDefinitionProvider {
        public ITypeDefinition? GetTypeDefinition(string name) => null;

        public ITypeDefinition? GetTypeDefinition(Type type) => null;
    }
}