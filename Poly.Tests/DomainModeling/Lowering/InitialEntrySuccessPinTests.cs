using Poly.Tests.TestHelpers;

using D = Poly.Tests.DomainModeling.Lowering.InitialEntryDomains;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-04a characterisation (#683): what a SUCCESSFUL create leaves, recorded on master before the entry moves out of the
/// constructor, so the move cannot silently change a success. Each row runs one scenario on both hosts and compares the
/// whole instance graph (<see cref="GraphState"/>) with a literal recorded on master. The entry forms that master's
/// generated C# cannot compile (an <c>invoke</c> lowers into the void constructor: CS0127, #825) are pinned on the
/// simulator only here; their parity rows are in <see cref="InitialEntryFailureTests"/> and are red until L-04a lands.
/// </summary>
public class InitialEntrySuccessPinTests {
    private static async Task Pin(string dsl, string name, Action<ParitySide> steps, string expected) {
        var scenario = ParityScenario.FromDsl(dsl, name, recordGraph: true);
        var (simulate, printedOutcomes) = scenario.Run(steps);
        var differences = string.Join("\n", ParityScenario.Differences(simulate, printedOutcomes));
        await Assert.That(differences).IsEqualTo("");
        await Assert.That(simulate[^1].Success).IsTrue().Because($"the last step succeeded: {simulate[^1].Message}");
        await Assert.That(simulate[^1].GraphParity).IsEqualTo(expected);
    }

    [Test]
    public async Task TopLevelCreate_WhenTheEntryAssigns_LeavesTheRecordedGraph() =>
        await Pin(D.Dsl("assign Score to 5"), "PinEntryAssignBare", side => side.Create("Kid"),
            "#0 {Cap=100; CurrentStage=\"Start\"; Score=5}\n");

    [Test]
    public async Task CreateInFromAnAction_WhenTheEntryAssigns_LeavesTheRecordedGraph() =>
        await Pin(D.Dsl("assign Score to 5"), "PinEntryAssignMake", side => {
            side.Create("Holder");
            side.Invoke("Make");
        }, "#0 {_kids=[#1]}\n#1 {Cap=100; CurrentStage=\"Start\"; Holder=#0; Score=5}\n");

    private const string TransitionKid = "Next: stage { entry { assign Score to 7 } }";

    [Test]
    public async Task TopLevelCreate_WhenTheEntryTransitions_LeavesTheRecordedGraph() =>
        await Pin(D.Dsl("transition to Next", TransitionKid), "PinEntryTransition", side => side.Create("Kid"),
            "#0 {Cap=100; CurrentStage=\"Next\"; Score=7}\n");

    [Test]
    public async Task TopLevelCreate_WhenTheEntryCreatesAChild_LeavesTheRecordedGraph() =>
        await Pin(D.Dsl("create Leaf { Amount: 3 }"), "PinEntryCreate", side => side.Create("Kid"),
            "#0 {Cap=100; CurrentStage=\"Start\"}\n");

    // The simulator only: master's generated C# does not compile for an entry `invoke` (CS0127, #825), so there is no
    // second host to compare against until L-04a lands; the parity row is InitialEntryFailureTests.
    [Test]
    public async Task TopLevelCreate_WhenTheEntryInvokesAnActionOnSelf_LeavesTheRecordedState_Simulator() {
        var (domain, _) = EvolvedDomain.FromDsl(D.Dsl("invoke Works"));
        var exec = ModuleExec.For(domain);

        var created = exec.Create("Kid", new Dictionary<string, object?>());

        await Assert.That(created.Succeeded).IsTrue();
        await Assert.That(created.Instance!.Bag["Count"]).IsEqualTo(1L);
        await Assert.That(ModuleReads.Stage(created.Instance)).IsEqualTo("Start");
    }

    // `create in` from a subscription handler: the child's entry runs, and the child lands in the owner's collection.
    private const string HandlerCreateDsl = """
        domain HandlerCreate
        S: entity {
          jobs: many Job
          kids: many Kid
          when jobs Done { create in kids { } }
        }
        Job: entity {
          s: S via jobs
          Open: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        Kid: entity {
          s: S via kids
          Score: Number range(0, 10) default(0)
          Start: stage { entry { assign Score to 5 } }
        }
        """;

    [Test]
    public async Task CreateInFromAHandler_WhenTheEntryAssigns_LeavesTheRecordedGraph() =>
        await Pin(HandlerCreateDsl, "PinEntryHandlerCreate", side => {
            var job = side.Create("Job");
            side.Create("S", ("jobs", new[] { job! }));
            side.Use(job);
            side.Invoke("Finish");
        }, "#0 {CurrentStage=\"Done\"; _sJobsDoneSubscribers=[#1]}\n#1 {_jobs=[#0]; _kids=[#2]}\n#2 {CurrentStage=\"Start\"; S=#1; Score=5}\n");

    private const string BuddyEntities = """
        Buddy: entity {
          Open: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        """;

    // InitializeSubscriptions order: the registry entry exists after the create, so the handler fires on a later transition.
    [Test]
    public async Task TopLevelCreate_WhenTheKidSubscribes_RegistersItselfWithTheBuddy() =>
        await Pin(D.Dsl("assign Score to 5", "buddies: many Buddy\nwhen buddies Done { assign Cap to 1 }", entities: BuddyEntities),
            "PinEntrySubscribes", side => {
                var buddy = side.Create("Buddy");
                side.Create("Kid", ("buddies", new[] { buddy! }));
                side.Use(buddy);
                side.Invoke("Finish");
            }, "#0 {CurrentStage=\"Done\"; _kidBuddiesDoneSubscribers=[#1]}\n#1 {Cap=1; CurrentStage=\"Start\"; Score=5; _buddies=[#0]}\n");
}