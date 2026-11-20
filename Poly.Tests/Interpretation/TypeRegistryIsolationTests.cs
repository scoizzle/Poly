using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The AST type table belongs to one analysis. Nothing answers a type query for
/// an analysis except that analysis's own layer, even when every run shares
/// one analyzer instance (#998 T12b).
/// </summary>
public class TypeRegistryIsolationTests {
    private static Analyzer SharedPipeline() =>
        new AnalyzerBuilder().AddAnalyzer(new TypeDefinitionNodeAnalyzer()).Build();

    [Test]
    public async Task TwoAnalyses_DoNotShareTypeRegistries() {
        var pipeline = SharedPipeline();
        var first = pipeline.Analyze(new TypeDefinitionNode("Alpha"));
        var second = pipeline.Analyze(new TypeDefinitionNode("Beta"));

        await Assert.That(first.TypeSystem.Resolve("Alpha")).IsNotNull();
        await Assert.That(first.TypeSystem.Resolve("Beta")).IsNull();
        await Assert.That(second.TypeSystem.Resolve("Beta")).IsNotNull();
        await Assert.That(second.TypeSystem.Resolve("Alpha")).IsNull();
    }

    [Test]
    public async Task ParallelAnalyses_EachSeeTheirOwnTypes() {
        const int count = 8;
        var pipeline = SharedPipeline();
        var results = new AnalysisResult[count];

        Parallel.For(0, count, index =>
            results[index] = pipeline.Analyze(new TypeDefinitionNode($"Type{index}")));

        for (var index = 0; index < count; index++) {
            var own = results[index].TypeSystem;
            await Assert.That(own.Resolve($"Type{index}")).IsNotNull();
            await Assert.That(own.Resolve($"Type{(index + 1) % count}")).IsNull();
        }
    }

    [Test]
    public async Task TypeSystemAnswersAreStableWithinOneAnalysis() {
        var analysis = SharedPipeline().Analyze(new TypeDefinitionNode("Stable"));
        var provider = analysis.TypeSystem;

        var first = provider.Resolve("Stable");
        var second = provider.Resolve("Stable");

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsSameReferenceAs(first!);
        await Assert.That(provider.Resolve("Missing")).IsNull();
        await Assert.That(provider.Resolve("Missing")).IsNull();
    }
}