using Poly.DomainModeling.Lowering;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class ParityTests {
    const string CreateFailDsl = """
        domain Lab
        Widget: entity {
          Score: Number range(1, 10) default(5)
          Bump: Number default(100)
          Draft: stage {
            entry { assign Score to Bump }
          }
        }
        """;

    const string ActionFailDsl = """
        domain Shop
        Basket: entity {
          Items: Number range(0, 3) default(3)
          AddItem: action {
            assign Items to Items + 1
          }
        }
        """;

    const string StageDsl = """
        domain Desk
        Ticket: entity {
          Note: Text default("open")
          Open: stage {
            Close: action {
              assign Note to "closed"
              transition to Closed
            }
          }
          Closed: stage { }
        }
        """;

    const string PolicyDsl = """
        domain Library
        Patron: entity {
          Name: Text required
          Email: Text unique
          MaxItems: Number range(0, 20) required
          loans: many Loan
          HasOverdueLoans: policy { any loans where Status is "Overdue" }
        }
        Loan: entity {
          Status: Text
        }
        """;

    const string LinkDsl = """
        domain Shop
        Kid: entity {
          Name: Text
        }
        Parent: entity {
          Name: Text
          kids: many Kid
          AddKid: action { create in kids { Name: "x" } }
        }
        """;

    const string RangeDsl = """
        domain Lab
        Widget: entity {
          Score: Number range(1, 10) default(5)
        }
        """;

    const string GuardDsl = """
        domain Board
        Task: entity {
          Ready: Boolean default(false)
          CanPromote: policy { Ready is true }
          Promote: action
            require CanPromote
          {
            assign Ready to true
          }
        }
        """;

    [Test]
    public async Task Create_WhenEntryEffectBreaksRange_FailsTheSameWay() {
        var outcomes = await ParityScenario.FromDsl(CreateFailDsl, "ParityCreateFail")
            .AssertAgree(side => side.Create("Widget"));
        await Assert.That(outcomes[0].Success).IsFalse();
        await Assert.That(outcomes[0].Message).Contains("must be <= 10");
    }

    [Test]
    public async Task Invoke_WhenAssignBreaksRange_FailsTheSameWay() {
        var outcomes = await ParityScenario.FromDsl(ActionFailDsl, "ParityActionFail")
            .AssertAgree(side => {
                side.Create("Basket");
                side.Invoke("AddItem");
            });
        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].State["Items"]).IsEqualTo("3");
    }

    [Test]
    public async Task Invoke_WhenActionTransitions_AgreesOnStageAndProperties() {
        var outcomes = await ParityScenario.FromDsl(StageDsl, "ParityStage")
            .AssertAgree(side => {
                side.Create("Ticket");
                side.Invoke("Close");
            });
        await Assert.That(outcomes[0].State["Stage"]).IsEqualTo("Open");
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("Closed");
        await Assert.That(outcomes[1].State["Note"]).IsEqualTo("closed");
    }

    [Test]
    public async Task EvaluatePolicy_HasOverdueLoans_AgreesForLinkedLoans() {
        var outcomes = await ParityScenario.FromDsl(PolicyDsl, "ParityPolicy")
            .AssertAgree(side => {
                var overdue = side.Create("Loan", ("Status", "Overdue"));
                var active = side.Create("Loan", ("Status", "Active"));
                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-overdue@lib.test"), ("MaxItems", 5L),
                    ("loans", new[] { overdue, active }));
                side.EvaluatePolicy("HasOverdueLoans");

                var returned = side.Create("Loan", ("Status", "Returned"));
                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-clear@lib.test"), ("MaxItems", 5L),
                    ("loans", new[] { returned }));
                side.EvaluatePolicy("HasOverdueLoans");
            });
        await Assert.That(outcomes[3].State["HasOverdueLoans"]).IsEqualTo("True");
        await Assert.That(outcomes[6].State["HasOverdueLoans"]).IsEqualTo("False");
    }

    [Test]
    public async Task Invoke_CreateInAction_AgreesOnLinkedChildren() {
        var outcomes = await ParityScenario.FromDsl(LinkDsl, "ParityLink")
            .AssertAgree(side => {
                side.Create("Parent", ("Name", "Pat"));
                side.Invoke("AddKid");
            });
        await Assert.That(outcomes[0].State["kids"]).IsEqualTo("0");
        await Assert.That(outcomes[1].State["kids"]).IsEqualTo("1");
    }

    [Test]
    public async Task Create_ForSecondEntity_RecordsThatEntitysState() {
        var outcomes = await ParityScenario.FromDsl(ActionFailDsl, "ParitySecondCreate")
            .AssertAgree(side => {
                side.Create("Basket", ("Items", 1L));
                side.Create("Basket", ("Items", 2L));
            });
        await Assert.That(outcomes[0].State["Items"]).IsEqualTo("1");
        await Assert.That(outcomes[1].State["Items"]).IsEqualTo("2");
    }

    [Test]
    public async Task AssertAgree_WhenStepsNameSomethingMissing_ThrowsInsteadOfAgreeing() {
        var scenario = ParityScenario.FromDsl(StageDsl, "ParityHarnessFault");
        Task Agree(Action<ParitySide> steps) => scenario.AssertAgree(steps);
        await Assert.That(() => Agree(side => { side.Create("Ticket"); side.Invoke("NoSuchAction"); })).Throws<ArgumentException>();
        await Assert.That(() => Agree(side => side.Create("NoSuchEntity"))).Throws<ArgumentException>();
        await Assert.That(() => Agree(side => side.Invoke("Close"))).Throws<ArgumentException>();
    }

    // Known gaps: the sides differ today. Each row pins the exact differences so a fix turns it red;
    // then replace it with an AssertAgree row.

    // Owner: C2b (create-time checks run from the compiled Create). Simulate throws; the printed Create returns a failure.
    [Test]
    public async Task KnownGap_CreateOutOfRange_SimulateThrowsAndPrintedReturnsFailure() {
        var (simulate, printed) = ParityScenario.FromDsl(RangeDsl, "ParityGapCreate")
            .Run(side => side.Create("Widget", ("Score", 99L)));
        await Assert.That(ParityScenario.Differences(simulate, printed))
            .IsEquivalentTo(["create Widget: exception type differs (simulate 'InvalidOperationException', printed '')"]);
    }

    // Owner: none in the plan; the simulator's guard mapping (MapModuleRequireFailure) goes with C8d (delete DEI).
    // Simulate leaves ErrorMessage empty and reports the guard in FailedGuards; the printed method returns the message.
    [Test]
    public async Task KnownGap_RequireBlocksAction_SimulateHasNoFailureMessage() {
        var (simulate, printed) = ParityScenario.FromDsl(GuardDsl, "ParityGapGuard")
            .Run(side => { side.Create("Task"); side.Invoke("Promote"); });
        await Assert.That(ParityScenario.Differences(simulate, printed))
            .IsEquivalentTo(["invoke Promote: failure message differs (simulate '', printed ''Promote' blocked by policy 'CanPromote'.')"]);
    }

    // Each Differences row differs from the baseline in exactly one field, so deleting that field's compare turns it red.
    static ParityOutcome Step(bool success = true, string? message = null, string? exceptionType = null, string? stage = "Open") =>
        new("invoke Close", success, message, exceptionType, new Dictionary<string, string?> { ["Stage"] = stage });

    static async Task AssertDifference(ParityOutcome simulate, ParityOutcome printed, string expected) =>
        await Assert.That(ParityScenario.Differences([simulate], [printed])).IsEquivalentTo([expected]);

    [Test]
    public async Task Differences_WhenIdentical_IsEmpty() =>
        await Assert.That(ParityScenario.Differences([Step()], [Step()])).IsEmpty();

    [Test]
    public async Task Differences_WhenStepCountDiffers_Reports() {
        var differences = ParityScenario.Differences([Step(), Step()], [Step()]);
        await Assert.That(differences).Contains("step count differs (simulate 2, printed 1)");
    }

    [Test]
    public Task Differences_WhenSuccessDiffers_Reports() =>
        AssertDifference(Step(), Step(success: false), "invoke Close: success differs (simulate 'True', printed 'False')");

    [Test]
    public Task Differences_WhenFailureMessageDiffers_Reports() =>
        AssertDifference(Step(message: "a"), Step(message: "b"), "invoke Close: failure message differs (simulate 'a', printed 'b')");

    [Test]
    public Task Differences_WhenExceptionTypeDiffers_Reports() =>
        AssertDifference(Step(exceptionType: "X"), Step(exceptionType: "Y"), "invoke Close: exception type differs (simulate 'X', printed 'Y')");

    [Test]
    public Task Differences_WhenStateValueDiffers_Reports() =>
        AssertDifference(Step(stage: "Open"), Step(stage: "Closed"), "invoke Close: 'Stage' differs (simulate 'Open', printed 'Closed')");

    [Test]
    public async Task Differences_WhenStateMemberIsMissingOnOneSide_Reports() {
        var withoutState = Step() with { State = new Dictionary<string, string?>() };
        await AssertDifference(Step(), withoutState, "invoke Close: 'Stage' differs (simulate 'Open', printed '(absent)')");
    }

    // Probes that are not expected to print compilable C#.
    static readonly string[] InvalidProbes = ["nested-invoke-type-mismatch.poly"];

    public static IEnumerable<string> PrintableProbes() {
        var root = FindRepoRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "docs/probes"), "*.poly", SearchOption.AllDirectories)
            .Where(path => !InvalidProbes.Contains(Path.GetFileName(path)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
    }

    [Test]
    public async Task PrintableProbes_FindsTheSamples() =>
        await Assert.That(PrintableProbes().Count()).IsGreaterThanOrEqualTo(10);

    // Every other probe must print C# that compiles; a probe that cannot yet is a B1 gap and is listed in InvalidProbes by name.
    [Test]
    [MethodDataSource(nameof(PrintableProbes))]
    public async Task PrintedCSharpCompiles(string relativePath) =>
        await Assert.That(await Print(relativePath, "Print_" + Path.GetFileNameWithoutExtension(relativePath))).IsNotNull();

    [Test]
    public async Task PrintedCSharp_ForTypeMismatchProbe_IsRejected() =>
        await Assert.That(async () => await Print("docs/probes/dogfood/nested-invoke-type-mismatch.poly", "Print_Mismatch"))
            .Throws<InvalidOperationException>();

    // CompileAndLoad throws with the compiler errors when the printed code does not compile.
    static async Task<System.Reflection.Assembly> Print(string relativePath, string assemblyName) {
        var poly = await File.ReadAllTextAsync(Path.Combine(FindRepoRoot(), relativePath));
        var (domain, analysis) = EvolvedDomain.FromDsl(poly);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        return ExportedCSharp.CompileAndLoad(cs, assemblyName);
    }

    static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "Poly.sln"))
                || File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}
