using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #630: a create-time entry chain runs while the child is still unlinked, so a <c>transition</c> in the
/// entry raised its stage notification into an empty registry and the parent's <c>when</c> never heard it.
/// The create-in wrapper defers those notifications, links the child, and replays them in entry order.
/// </summary>
public class CreateEntryNotificationParityTests {
    private const string EntryChainDsl = """
        domain Ordering
        Item: entity {
          Name: Text
          Auto: Number default(0)
          Start: stage { entry { assign Auto to Auto + 1 transition to Ready } }
          Ready: stage { entry { assign Auto to Auto + 10 } }
        }
        Box: entity {
          Label: Text
          Starts: Number default(0)
          Seen: Number default(0)
          Trace: Text default("")
          items: many Item
          Add: action (n: Text) { create in items { Name: n } }
          when items Start { assign Starts to Starts + 1 assign Trace to Trace + "S" }
          when items Ready { assign Seen to Seen + 1 assign Trace to Trace + "R" }
        }
        """;

    /// <summary>
    /// The parent's handlers see both stages the child entered, in the order the entry raised them: the
    /// initial stage (<c>Start</c>) then the entry chain's hop (<c>Ready</c>).
    /// </summary>
    [Test]
    public async Task CreateIn_WhenTheInitialEntryTransitions_NotifiesTheParentOfEachEntryInOrder() {
        var outcomes = await ParityScenario.FromDsl(EntryChainDsl, "ParityCreateEntryNotifiesParent")
            .AssertAgree(side => {
                side.Create("Box", ("Label", "b"));
                side.Invoke("Add", ("n", "x"));
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Starts"]).IsEqualTo("1");
        await Assert.That(outcomes[1].State["Seen"]).IsEqualTo("1")
            .Because("the entry chain's transition to Ready is a stage entry the linked parent must hear");
        await Assert.That(outcomes[1].State["Trace"]).IsEqualTo("SR")
            .Because("Start was entered first, then Ready: the replay keeps the entry's order");
    }
}