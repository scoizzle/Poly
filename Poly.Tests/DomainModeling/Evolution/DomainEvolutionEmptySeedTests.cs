using Poly.Analysis;
using Poly.Ast;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;

namespace Poly.Tests.DomainModeling.Evolution;

/// <summary>
/// Proves that <see cref="DomainEvolution.ApplyAndAnalyze"/> from an empty seed runs the
/// analysis pipeline once, on the proposal, and that a non-empty seed still runs
/// seed-then-proposal.
/// </summary>
public class DomainEvolutionEmptySeedTests {
    [Test]
    public async Task Apply_EmptySeed_AnalyzesOnlyTheProposal() {
        var recorder = new RecordingAnalyzer();
        var session = SessionBuilder.CreateEmpty().LoadCore().AddAnalyzer(recorder).Build();

        var evolution = new DomainEvolution(new Domain("D", []));
        var result = evolution.ApplyAndAnalyze([new AddEntityChange("Order", [])], session: session);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(recorder.Roots.Count).IsEqualTo(1);
        await Assert.That(recorder.Roots[0]).IsSameReferenceAs(result.Root);
    }

    [Test]
    public async Task Apply_NonEmptySeed_StillAnalyzesSeedThenProposal() {
        var recorder = new RecordingAnalyzer();
        var session = SessionBuilder.CreateEmpty().LoadCore().AddAnalyzer(recorder).Build();

        var seed = DomainFactory.Create("D");
        var evolution = new DomainEvolution(seed);
        var result = evolution.ApplyAndAnalyze([new AddEntityChange("Order", [])], session: session);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(recorder.Roots.Count).IsEqualTo(2);
        await Assert.That(recorder.Roots[0]).IsSameReferenceAs(seed);
        await Assert.That(recorder.Roots[1]).IsSameReferenceAs(result.Root);
    }

    [Test]
    public async Task Apply_EmptySeed_InBatchTarget_ResolvesOnLiveOverlay() {
        var session = SessionBuilder.CreateEmpty().LoadCore().Build();

        var result = new DomainEvolution(new Domain("D", [])).ApplyAndAnalyze(
            [
                new AddEntityChange("Order", []),
                new AddStageChange("Order", "Draft"),
                new AddPolicyToStageChange("Order", "Draft", new Policy("Guard", DomainExpression.Literal(true)))
            ],
            session: session);

        await Assert.That(result.Succeeded).IsTrue();
        var order = result.Root.Types.OfType<Entity>().Single();
        var draft = order.Stages.Single(s => s.Name == "Draft");
        await Assert.That(draft.Policies.Select(p => p.Name)).Contains("Guard");
    }

    [Test]
    public async Task Apply_EmptySeed_MissingTarget_IsRolledBack() {
        var session = SessionBuilder.CreateEmpty().LoadCore().Build();

        var result = new DomainEvolution(new Domain("D", []))
            .ApplyAndAnalyze(
                [new AddPolicyToStageChange("Missing", "Draft", new Policy("Guard", DomainExpression.Literal(true)))],
                session: session);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.WasRolledBack).IsTrue();
        await Assert.That(result.MutationErrors.Single()).Contains("Entity 'Missing'");
        await Assert.That(result.Analysis.HasErrors).IsFalse();
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