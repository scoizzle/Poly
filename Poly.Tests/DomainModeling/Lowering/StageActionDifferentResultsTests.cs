using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Same-named stage actions are distinct per stage, so one may return an entity
/// while another is void. The merged method lowers each branch by its own result.
/// </summary>
public class StageActionDifferentResultsTests {
    private const string Dsl = """
        domain Tickets
        Ticket: entity {
          Memo: Text
          Draft: stage {
            Cancel: action -> Receipt { create Receipt { Note: "done" } }
            Activate: action { transition to Active }
          }
          Active: stage { Cancel: action { transition to Closed } }
          Closed: stage { }
        }
        Receipt: entity { Note: Text }
        """;

    [Test]
    public async Task Cancel_DifferentResultsPerStage_LowersAndAgreesOnSimulateAndPrintedCsharp() {
        var outcomes = await ParityScenario.FromDsl(Dsl, "StageDifferentResults")
            .AssertAgree(side => {
                side.Create("Ticket", ("Memo", "m"));
                side.Invoke("Cancel");
                side.Create("Ticket", ("Memo", "m2"));
                side.Invoke("Activate");
                side.Invoke("Cancel");
            });
        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[4].Success).IsTrue();
        await Assert.That(outcomes[4].State["Stage"]).IsEqualTo("Closed");
    }

    [Test]
    public async Task Cancel_FromDraft_YieldsReceipt_FromActive_YieldsNoValue() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl);
        var exec = ModuleExec.For(domain);
        var draft = exec.Create("Ticket", new Dictionary<string, object?> { ["Memo"] = "m" });
        await Assert.That(draft.Succeeded).IsTrue();
        var cancelled = exec.Invoke(draft.Instance!, "Cancel");
        await Assert.That(cancelled.Succeeded).IsTrue();
        await Assert.That(cancelled.Instance?.EntityName).IsEqualTo("Receipt");

        var active = exec.Create("Ticket", new Dictionary<string, object?> { ["Memo"] = "m2" });
        await Assert.That(exec.Invoke(active.Instance!, "Activate").Succeeded).IsTrue();
        var closed = exec.Invoke(active.Instance!, "Cancel");
        await Assert.That(closed.Succeeded).IsTrue();
        await Assert.That(closed.Instance).IsNull();
        await Assert.That(ModuleReads.Stage(active.Instance!)).IsEqualTo("Closed");
    }
}