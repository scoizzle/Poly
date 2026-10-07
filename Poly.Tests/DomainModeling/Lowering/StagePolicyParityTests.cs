using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

// Stage policies and action-local policies have no DSL syntax, so these rows add them through the API.
public class StagePolicyParityTests {
    const string TicketDsl = """
        domain Desk
        Ticket: entity {
          Ready: Boolean default(false)
          Note: Text default("open")
          Touch: action { assign Note to "touched" }
          Open: stage {
            Close: action { transition to Closed }
          }
          Closed: stage { }
        }
        """;

    const string BasketDsl = """
        domain Shop
        Basket: entity {
          Items: Number default(0)
          Add: action { assign Items to Items + 1 }
        }
        """;

    static readonly DomainExpression ReadyIsTrue =
        DomainExpression.Equal(DomainExpression.Property("Ready"), DomainExpression.Literal(true));

    static ParityScenario Scenario(string dsl, string assemblyName, Func<EvolutionBuilder, EvolutionBuilder> edit) {
        var result = edit(new DomainEvolution(EvolvedDomain.FromDsl(dsl).Domain).Evolve()).Apply();
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics.Select(d => d.Message)));
        return ParityScenario.FromDomain(result.Root!, assemblyName);
    }

    static ParityScenario TicketWithStagePolicy(string assemblyName) =>
        Scenario(TicketDsl, assemblyName, b => b.AddPolicyToStage("Ticket", "Open", "IsReady", ReadyIsTrue));

    // The simulator reports a blocked guard in FailedGuards, not ErrorMessage: see ParityTests.KnownGap_RequireBlocksAction_*.
    static string BlockedMessageGap(string action, string policy) =>
        $"invoke {action}: failure message differs (simulate '', printed ''{action}' blocked by policy '{policy}'.')";

    [Test]
    public async Task Invoke_StageActionWhenStagePolicyFails_BlockedOnBothSides() {
        var (simulate, printed) = TicketWithStagePolicy("ParityStagePolicyBlocksStageAction")
            .Run(side => { side.Create("Ticket"); side.Invoke("Close"); });
        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEquivalentTo([BlockedMessageGap("Close", "IsReady")]);
        await Assert.That(simulate[1].Success).IsFalse();
        await Assert.That(simulate[1].State["Stage"]).IsEqualTo("Open");
    }

    [Test]
    public async Task Invoke_EntityActionWhenStagePolicyFails_BlockedOnBothSides() {
        var (simulate, printed) = TicketWithStagePolicy("ParityStagePolicyBlocksEntityAction")
            .Run(side => { side.Create("Ticket"); side.Invoke("Touch"); });
        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEquivalentTo([BlockedMessageGap("Touch", "IsReady")]);
        await Assert.That(simulate[1].Success).IsFalse();
        await Assert.That(simulate[1].State["Note"]).IsEqualTo("open");
    }

    [Test]
    public async Task Invoke_WhenStagePolicyHolds_AgreesAndTransitions() {
        var outcomes = await TicketWithStagePolicy("ParityStagePolicyHolds")
            .AssertAgree(side => { side.Create("Ticket", ("Ready", true)); side.Invoke("Close"); });
        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("Closed");
    }

    [Test]
    public async Task Invoke_EntityActionAfterLeavingPolicyStage_AgreesAndRuns() {
        var outcomes = await TicketWithStagePolicy("ParityStagePolicyLeftStage")
            .AssertAgree(side => {
                side.Create("Ticket", ("Ready", true));
                side.Invoke("Close");
                side.Invoke("Touch");
            });
        await Assert.That(outcomes[2].Success).IsTrue();
        await Assert.That(outcomes[2].State["Note"]).IsEqualTo("touched");
    }

    [Test]
    public async Task Invoke_ActionLocalPolicyThatIsNotAnEntityPolicy_BlocksOnBothSides() {
        var (simulate, printed) = Scenario(BasketDsl, "ParityActionLocalPolicy",
                b => b.AddPolicyToAction("Basket", "Add", "HasRoom",
                    DomainExpression.LessThan(DomainExpression.Property("Items"), DomainExpression.Literal(1L))))
            .Run(side => { side.Create("Basket"); side.Invoke("Add"); side.Invoke("Add"); });
        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEquivalentTo([BlockedMessageGap("Add", "HasRoom")]);
        await Assert.That(simulate[1].Success).IsTrue();
        await Assert.That(simulate[2].Success).IsFalse();
        await Assert.That(simulate[2].State["Items"]).IsEqualTo("1");
    }
}
