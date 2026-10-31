using Poly.Analysis;
using Poly.Ast;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Evolution;

/// <summary>
/// Proves <see cref="DomainEvolution.Apply"/> mutates without running the analysis
/// pipeline, and that <see cref="DomainEvolution.ApplyAndAnalyze"/> still rejects
/// a proposal the pipeline would reject.
/// </summary>
public class DomainEvolutionApplyFacadeTests {
    [Test]
    public async Task Apply_DoesNotAnalyze() {
        var recorder = new RecordingAnalyzer();
        var session = SessionBuilder.CreateEmpty().LoadCore().AddAnalyzer(recorder).Build();
        var start = new Domain("D", []);

        var result = new DomainEvolution(start).Apply(
            [
                new AddEntityChange("Order", []),
                new AddEntityChange("Order", [])
            ],
            session: session);

        await Assert.That(recorder.Roots.Count).IsEqualTo(0);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.WasRolledBack).IsFalse();
        await Assert.That(result.Root).IsNotSameReferenceAs(start);
        var orders = result.Root.Types.OfType<Entity>().ToList();
        await Assert.That(orders.Count).IsEqualTo(2);
        await Assert.That(orders.All(entity => entity.Name == "Order")).IsTrue();
    }

    [Test]
    public async Task Apply_MutationError_RollsBackWithoutAnalysis() {
        var recorder = new RecordingAnalyzer();
        var session = SessionBuilder.CreateEmpty().LoadCore().AddAnalyzer(recorder).Build();
        var start = new Domain("D", []);

        var result = new DomainEvolution(start).Apply(
            [new AddPolicyToStageChange("Missing", "Draft", new Policy("Guard", DomainExpression.Literal(true)))],
            session: session);

        await Assert.That(recorder.Roots.Count).IsEqualTo(0);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
        await Assert.That(result.Root).IsSameReferenceAs(start);
        await Assert.That(result.MutationErrors.Single()).Contains("Entity 'Missing'");
    }

    [Test]
    public async Task ApplyAndAnalyze_DuplicateEntity_Rejects() {
        var recorder = new RecordingAnalyzer();
        var session = SessionBuilder.CreateEmpty().LoadCore().AddAnalyzer(recorder).Build();
        var start = new Domain("D", []);

        var result = new DomainEvolution(start).ApplyAndAnalyze(
            [
                new AddEntityChange("Order", []),
                new AddEntityChange("Order", [])
            ],
            session: session);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
        await Assert.That(result.Root).IsSameReferenceAs(start);
        await Assert.That(result.Analysis.HasErrors).IsTrue();
        await Assert.That(result.MutationErrors).IsEmpty();
        await Assert.That(recorder.Roots.Count).IsEqualTo(1);
        await Assert.That(recorder.Roots[0]).IsNotSameReferenceAs(start);
    }

    private sealed class RecordingAnalyzer : INodeAnalyzer {
        public const string Id = "TestRecording";
        public string PassName => Id;
        public List<Node> Roots { get; } = [];

        public void Analyze(AnalysisContext context, Node node) {
            Roots.Add(node);
        }
    }
}