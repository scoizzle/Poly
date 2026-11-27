namespace Poly.Tests.Mcp;

/// <summary>
/// #622: a policy-guard refusal reads the module's own words on both hosts. The tool used to drop
/// the module's message and compose <c>Action 'X' blocked by guards: P</c>, which the generated C#
/// never says, so a consumer could not match one wording across simulator and generated code.
/// </summary>
public class PolicyGuardRefusalWordingTests {
    const string Dsl = """
        domain Mini

        Cart: entity {
          Count: Number default(0)
          NotEmpty: policy { Count > 0 }
          Checkout: action require NotEmpty {
            assign Count to Count
          }
        }
        """;

    [Test]
    public async Task InvokeAction_RefusedByPolicy_ReportsTheModulesMessage() {
        var sessionId = McpTestTools.Surface.Open("R04GuardWording");
        await Assert.That(McpTestTools.Surface.Define(sessionId, Dsl).Success).IsTrue();

        var created = McpTestTools.Surface.Create(sessionId, "Cart");
        await Assert.That(created.Ok).IsTrue().Because(created.Summary);

        var refused = McpTestTools.Surface.Invoke(sessionId, created.InstanceId, "Checkout");
        await Assert.That(refused.Ok).IsFalse();

        var result = refused.Result;
        await Assert.That(result.ErrorMessage).IsEqualTo("'Checkout' blocked by policy 'NotEmpty'.");
        await Assert.That(result.FailedGuards).IsEquivalentTo(["NotEmpty"]);
        await Assert.That(refused.Summary).IsEqualTo("'Checkout' blocked by policy 'NotEmpty'.")
            .Because("the tool reports the module's failure rather than composing a second wording");
    }
}