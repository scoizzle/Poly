using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

public class InvokeCallStackScopeTests {
    private static EvolutionResult Parse(string dsl) {
        var parser = new PolyDslParser(dsl);
        var changes = parser.Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).ApplyAndAnalyze(changes);
    }

    [Test]
    public async Task RepeatInvoke_SameCallee_YieldsOnePostconditionPerCall() {
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              B: action { assign Qty to Qty + 10 }
              A: action {
                invoke B
                invoke B
              }
            }
            """);
        var entity = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Item");
        var a = entity.Actions.Single(x => x.Name == "A");
        var posts = result.Analysis.GetActionInvariants(a)!.StageContexts().Single().Postconditions;

        await Assert.That(posts.Count).IsEqualTo(2);
        // The first call sees Qty in [0, 90] and yields [10, 100]. The callee's post-state flows into the
        // second call, which therefore sees [10, 100] and yields [20, 100] (max still bounded by the declared range check).
        await Assert.That(posts[0].ValueRange!.Min).IsEqualTo(10m);
        await Assert.That(posts[0].ValueRange!.Max).IsEqualTo(100m);
        await Assert.That(posts[1].ValueRange!.Min).IsEqualTo(20m);
        await Assert.That(posts[1].ValueRange!.Max).IsEqualTo(100m);
        // Each call reports its own call-chain violation (previously only the first was reported).
        var callChain = result.Analysis.Diagnostics.Count(d => d.Message.Contains("Call-chain postcondition (A"));
        await Assert.That(callChain).IsEqualTo(2);
    }

    /// <summary>
    /// #535, #1101: an invoke cycle is a DMEFF021 error, so the gated result carries the
    /// original (empty) root. The proposal still has to be analyzable — the point of these two tests
    /// is that a recursive body terminates the analyzer and still publishes its invariants — so they
    /// analyze the ungated proposal and assert the gate's error beside it.
    /// </summary>
    private static (Domain Root, AnalysisResult Analysis, EvolutionResult Gated) ParseRecursive(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        var evolution = new DomainEvolution(DomainTestFactory.Create("_", [], []));
        var gated = evolution.ApplyAndAnalyze(changes);
        var root = evolution.Apply(changes).Root;
        return (root, DomainModelAnalyzer.Analyze(root), gated);
    }

    [Test]
    public async Task SelfInvoke_StillTerminates() {
        var (root, analysis, gated) = ParseRecursive("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              Loop: action {
                assign Qty to Qty + 1
                invoke Loop
              }
            }
            """);
        await Assert.That(gated.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.InvokeCycle)).IsTrue();
        var entity = root.Types.OfType<Entity>().Single(e => e.Name == "Item");
        var loop = entity.Actions.Single(x => x.Name == "Loop");
        await Assert.That(analysis.GetActionInvariants(loop)).IsNotNull();
    }

    [Test]
    public async Task MutualInvoke_StillTerminates() {
        var (root, analysis, gated) = ParseRecursive("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              P: action {
                assign Qty to Qty + 1
                invoke Q
              }
              Q: action {
                assign Qty to Qty + 2
                invoke P
              }
            }
            """);
        await Assert.That(gated.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.InvokeCycle)).IsTrue();
        var entity = root.Types.OfType<Entity>().Single(e => e.Name == "Item");
        await Assert.That(analysis.GetActionInvariants(entity.Actions.Single(x => x.Name == "P"))).IsNotNull();
        await Assert.That(analysis.GetActionInvariants(entity.Actions.Single(x => x.Name == "Q"))).IsNotNull();
    }
}