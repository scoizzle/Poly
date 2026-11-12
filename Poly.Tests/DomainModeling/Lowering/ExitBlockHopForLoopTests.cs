using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// A hop inside a <c>for</c> in an exit block lowers through a fresh pass; the exit must
/// not re-inline into itself without end. Kept in its own class: on the unfixed base the
/// lowering overflows the stack and aborts the process.
/// </summary>
public class ExitBlockHopForLoopTests {
    [Test]
    public async Task Invoke_ExitHopInFor_DoesNotRecurse() {
        const string dsl = """
            domain H9
            Item: entity { Tag: Text optional }
            Z: entity {
              Count: Number default(0)
              items: many Item
              A: stage {
                exit { for i in items { transition to B } }
                Go: action { transition to B }
              }
              B: stage { entry { assign Count to Count + 1 } }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "ExitHopInFor")
            .AssertAgree(side => {
                var item = side.Create("Item");
                side.Create("Z", ("items", new List<object?> { item }));
                side.Invoke("Go");
            });
        await Assert.That(outcomes[2].State["Stage"]).IsEqualTo("B");
        await Assert.That(outcomes[2].State["Count"]).IsEqualTo("1");
    }
}