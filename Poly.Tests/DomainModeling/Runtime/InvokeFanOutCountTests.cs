using Poly.DomainModeling.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// #1121 (B1): the counted inline limit is an invoke-chain <em>depth</em>, not the number of actions
/// reachable from the outermost compile. A shallow fan-out (one action invoking hundreds of others, or a
/// binary tree) runs today, so it must not be refused as "too deep".
/// </summary>
public class InvokeFanOutCountTests {
    /// <summary>A complete binary invoke tree: every internal action invokes two callees, so the
    /// longest chain is <paramref name="depth"/> actions while the tree holds 2^depth - 1 actions.</summary>
    private static string Tree(int depth) {
        var total = (1 << depth) - 1;
        var lines = new List<string>();
        for (var k = 1; k <= total; k++) {
            lines.Add(2 * k <= total
                ? $"  T{k}: action {{ invoke T{2 * k} invoke T{2 * k + 1} }}"
                : $"  T{k}: action {{ assign N to N + 1 }}");
        }
        return "domain Tree\nA: entity {\n  N: Number default(0)\n"
            + string.Join("\n", lines) + "\n}\n";
    }

    /// <summary>One action invoking <paramref name="fanOut"/> distinct callees, each of which assigns.</summary>
    private static string FanOut(int fanOut) {
        var lines = new List<string>
        {
            "  T1: action { "
                + string.Join(" ", Enumerable.Range(1, fanOut).Select(i => $"invoke W{i}")) + " }",
        };
        for (var i = 1; i <= fanOut; i++)
            lines.Add($"  W{i}: action {{ assign N to N + 1 }}");
        return "domain Fan\nA: entity {\n  N: Number default(0)\n"
            + string.Join("\n", lines) + "\n}\n";
    }

    private static async Task<ModuleInstance> Run(string dsl, string action = "T1") {
        var (domain, _) = EvolvedDomain.FromDsl(dsl);
        var executor = ModuleExec.For(domain);
        var created = executor.Create("A", new Dictionary<string, object?>());
        await Assert.That(created.Succeeded).IsTrue().Because(created.Error ?? "");
        var outcome = executor.Invoke(created.Instance!, action);
        await Assert.That(outcome.Error).IsNull();
        return created.Instance!;
    }

    /// <summary>511 actions, longest chain 9: a wide tree must never be refused by a depth limit.</summary>
    [Test]
    public async Task Invoke_ShallowTreeOf511Actions_RunsEveryLeaf() {
        var instance = await Run(Tree(9));
        await Assert.That(Convert.ToInt64(instance.Bag["N"])).IsEqualTo(256L);
    }

    /// <summary>One action invoking 300 others: depth 2, every callee executes.</summary>
    [Test]
    public async Task Invoke_OneActionInvoking300Others_RunsEveryCallee() {
        var instance = await Run(FanOut(300));
        await Assert.That(Convert.ToInt64(instance.Bag["N"])).IsEqualTo(300L);
    }

    /// <summary>1023 actions, longest chain 10: still shallow, so it runs (the stack budget alone may
    /// refuse a chain whose per-link depth is large, never a tree this shallow).</summary>
    [Test]
    public async Task Invoke_TreeOf1023Actions_Depth10_RunsEveryLeaf() {
        var instance = await Run(Tree(10));
        await Assert.That(Convert.ToInt64(instance.Bag["N"])).IsEqualTo(512L);
    }
}