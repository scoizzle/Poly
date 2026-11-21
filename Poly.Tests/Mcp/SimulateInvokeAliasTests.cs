namespace Poly.Tests.Mcp;

/// <summary>
/// #500: a simulate step's <c>as</c> alias is the instance handle later steps take. It resolves
/// for an <c>invoke</c>'s entity-typed argument exactly as it does for <c>instanceId</c> — and only
/// for an entity-typed argument, so a Text value that happens to spell an alias keeps its text.
/// </summary>
public class SimulateInvokeAliasTests {
    private const string Cart = """
        domain AliasCart
        Product: entity { Sku: Text }
        Line: entity { product: Product }
        Cart: entity {
          lines: many Line
          Add: action (p: Product) { create in lines { product: p } }
        }
        """;

    private const string Notes = """
        domain AliasScope
        Product: entity { Sku: Text }
        Note: entity {
          Body: Text
          Set: action (text: Text) { assign Body to text }
        }
        """;

    [Test]
    public async Task EntityTypedArgument_TakesAnEarlierCreatesAlias() {
        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open("AliasCart");
        await Assert.That(harness.Define(workspace, Cart).Success).IsTrue();

        var run = harness.Simulate(workspace, """
            [{"op":"create","entity":"Product","properties":{"Sku":"A"},"as":"p"},
             {"op":"create","entity":"Cart","properties":{},"as":"c"},
             {"op":"invoke","instanceId":"c","action":"Add","args":{"p":"p"}}]
            """);

        await Assert.That(run.Step(0).Ok).IsTrue().Because(run.Step(0).Summary);
        await Assert.That(run.Step(1).Ok).IsTrue().Because(run.Step(1).Summary);
        await Assert.That(run.Step(2).Ok).IsTrue().Because(run.Step(2).Summary);
    }

    /// <summary>The alias scope is the entity-typed parameter, not every string that matches an alias.</summary>
    [Test]
    public async Task TextArgument_ThatSpellsAnAlias_KeepsItsText() {
        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open("AliasScope");
        await Assert.That(harness.Define(workspace, Notes).Success).IsTrue();

        var run = harness.Simulate(workspace, """
            [{"op":"create","entity":"Product","properties":{"Sku":"A"},"as":"p"},
             {"op":"create","entity":"Note","properties":{"Body":"x"},"as":"n"},
             {"op":"invoke","instanceId":"n","action":"Set","args":{"text":"p"}},
             {"op":"get","instanceId":"n"}]
            """);

        await Assert.That(run.Step(2).Ok).IsTrue().Because(run.Step(2).Summary);
        await Assert.That(run.Step(3).Property("Body")).IsEqualTo("p");
    }

    /// <summary>An alias that was never created stays an instance id, so the missing instance fails closed.</summary>
    [Test]
    public async Task UnknownAlias_StillFailsAsAMissingInstance() {
        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open("AliasMissing");
        await Assert.That(harness.Define(workspace, Cart).Success).IsTrue();

        var run = harness.Simulate(workspace, """
            [{"op":"create","entity":"Cart","properties":{},"as":"c"},
             {"op":"invoke","instanceId":"c","action":"Add","args":{"p":"never-created"}}]
            """);

        await Assert.That(run.Step(1).Ok).IsFalse();
        await Assert.That(run.Step(1).Summary).Contains("not found");
    }
}