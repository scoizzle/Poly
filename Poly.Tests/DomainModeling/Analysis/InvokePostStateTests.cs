using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>#367 B-3 (direct self-invoke): the callee's post-environment is merged back into the
/// caller, so effects after an <c>invoke</c> see what the callee did. A failed invoke returns the
/// caller's failure (fail-fast) and the action's transaction rolls back, so only the callee's
/// success state is ever observed by later effects.</summary>
public class InvokePostStateTests {
    private static EvolutionResult Parse(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).ApplyAndAnalyze(changes);
    }

    private static IReadOnlyList<EffectPostcondition> Posts(EvolutionResult result, string actionName) {
        var entity = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Item");
        var action = entity.Actions.Single(a => a.Name == actionName);
        return result.Analysis.GetActionInvariants(action)!.StageContexts().Single().Postconditions;
    }

    [Test]
    public async Task ChainedInvoke_SecondCallSeesFirstCallsOutput() {
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
        var posts = Posts(result, "A");

        await Assert.That(posts.Count).IsEqualTo(2);
        // First call: Qty is the declared [0, 90] -> Qty + 10 = [10, 100].
        await Assert.That(posts[0].ValueRange!.Min).IsEqualTo(10d);
        await Assert.That(posts[0].ValueRange!.Max).IsEqualTo(100d);
        // After the first call Qty is [10, 90] (declared range met with [10, 100]), so the second
        // call computes [20, 100].
        await Assert.That(posts[1].ValueRange!.Min).IsEqualTo(20d);
        await Assert.That(posts[1].ValueRange!.Max).IsEqualTo(100d);
    }

    [Test]
    public async Task InvokeThenAssign_CallerSeesCalleeSuccessState() {
        // B can fail (Qty + 10 may exceed range(0, 90)); on the path where it succeeds Qty is [10, 90].
        // The caller's later effects only run on that path, never on the failing one.
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              Total: Number range(0, 200)
              B: action { assign Qty to Qty + 10 }
              A: action {
                invoke B
                assign Total to Qty + 0
              }
            }
            """);
        var total = Posts(result, "A").Last(p => p.TargetProperty == "Total").ValueRange!;

        await Assert.That(total.Min).IsEqualTo(10d);
        await Assert.That(total.Max).IsEqualTo(90d);
    }

    [Test]
    public async Task ConditionalCallee_CallerSeesJoinNotTheAssignedArm() {
        // B assigns Qty only on one path; the other path leaves it untouched. The caller must see
        // the join of the two, not the assigned value.
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              Total: Number range(0, 200)
              B: action { if (Qty <= 4) { assign Qty to 50 } }
              A: action {
                invoke B
                assign Total to Qty + 0
              }
            }
            """);
        var total = Posts(result, "A").Last(p => p.TargetProperty == "Total").ValueRange!;

        await Assert.That(total.Min).IsEqualTo(0d);
        await Assert.That(total.Max).IsEqualTo(90d);
    }

    [Test]
    public async Task NestedInvoke_CalleeOfCalleeIsVisibleToOutermostCaller() {
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              Total: Number range(0, 200)
              C: action { assign Qty to 40 }
              B: action { invoke C }
              A: action {
                invoke B
                assign Total to Qty + 0
              }
            }
            """);
        var total = Posts(result, "A").Last(p => p.TargetProperty == "Total").ValueRange!;

        await Assert.That(total.Min).IsEqualTo(40d);
        await Assert.That(total.Max).IsEqualTo(40d);
    }

    [Test]
    public async Task InvokeNotAssigningProperty_LeavesItUnchanged() {
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              Other: Number range(5, 6)
              Total: Number range(0, 200)
              B: action { assign Qty to 40 }
              A: action {
                invoke B
                assign Total to Other + 0
              }
            }
            """);
        var total = Posts(result, "A").Last(p => p.TargetProperty == "Total").ValueRange!;

        await Assert.That(total.Min).IsEqualTo(5d);
        await Assert.That(total.Max).IsEqualTo(6d);
    }
}