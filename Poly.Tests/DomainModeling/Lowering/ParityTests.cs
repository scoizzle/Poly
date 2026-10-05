using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Runtime;

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

    const string TrackingDsl = """
        domain Watch
        Paper: entity {
          Title: Text
          A: stage {
            Advance: action { transition to B }
          }
          B: stage { }
        }
        Tr: entity {
          Tracks: Paper
          P: stage {
            when Tracks B {
              transition to Q
            }
          }
          Q: stage { }
        }
        """;

    // A stage-scoped subscription whose handler transitions the subscriber.
    [Test]
    public async Task Invoke_WhenTrackedPeerTransitions_SubscriberTransitionsToo() {
        var outcomes = await ParityScenario.FromDsl(TrackingDsl, "ParityTracking")
            .AssertAgree(side => {
                var paper = side.Create("Paper", ("Title", "p"));
                var tr = side.Create("Tr", ("Tracks", paper));
                side.Use(paper);
                side.Invoke("Advance");
                side.Use(tr);
            });
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("P");
        await Assert.That(outcomes[4].State["Stage"]).IsEqualTo("Q");
    }

    [Test]
    public async Task Invoke_WhenTwoSubscribersTrackOnePeer_BothTransition() {
        var outcomes = await ParityScenario.FromDsl(TrackingDsl, "ParityTrackingTwo")
            .AssertAgree(side => {
                var paper = side.Create("Paper", ("Title", "p"));
                var first = side.Create("Tr", ("Tracks", paper));
                var second = side.Create("Tr", ("Tracks", paper));
                side.Use(paper);
                side.Invoke("Advance");
                side.Use(first);
                side.Use(second);
            });
        await Assert.That(outcomes[5].State["Stage"]).IsEqualTo("Q");
        await Assert.That(outcomes[6].State["Stage"]).IsEqualTo("Q");
    }

    const string EqualityDsl = """
        domain Shop
        Order: entity {
          Status: Text
          Level: Number
          Code: Text default("Active")
        }
        """;

    const string EntryAssignedDsl = """
        domain Shop
        Job: entity {
          Tag: Text
          Mark: Text
          Open: stage { entry { assign Mark to "x" } }
        }
        """;

    // The DSL cannot author equals(...), so add the constraint to the parsed model.
    static ParityScenario EqualityScenario(string dsl, string assemblyName, params (string Property, object Expected)[] equalities) {
        var domain = EvolvedDomain.FromDsl(dsl).Domain;
        var entity = domain.Types.OfType<Entity>().Single();
        entity = entity with {
            Properties = [.. entity.Properties.Select(p => p with {
                Constraints = [.. p.Constraints, .. equalities.Where(e => e.Property == p.Name).Select(e => new EqualityConstraint(e.Expected))]
            })]
        };
        return ParityScenario.FromDomain(domain with { Types = [.. domain.Types.Select(t => t is Entity ? entity : t)] }, assemblyName);
    }

    // Status must be "Active"; Level must be 5 (an int, while the property holds a long).
    static ParityScenario OrderScenario(string assemblyName) =>
        EqualityScenario(EqualityDsl, assemblyName, ("Status", "Active"), ("Level", 5));

    [Test]
    public async Task Create_WhenEqualityHolds_Agrees() {
        var outcomes = await OrderScenario("ParityEqualityHolds")
            .AssertAgree(side => side.Create("Order", ("Status", "Active"), ("Level", 5L)));
        await Assert.That(outcomes[0].Success).IsTrue();
    }

    const string CreateThrowsVersusFails = "create Order: exception type differs (simulate 'InvalidOperationException', printed '')";

    // Create failure still differs in how it is reported (simulate throws, printed returns a failure): see
    // KnownGap_CreateOutOfRange_*. Everything else, including the message, must agree.
    [Test]
    [Arguments("Closed", 5L, "'Status' must equal Active.")]
    [Arguments("active", 5L, "'Status' must equal Active.")]
    [Arguments("Active", 6L, "'Level' must equal 5.")]
    public async Task Create_WhenEqualityViolated_FailsWithTheSameMessage(string status, long level, string message) {
        var (simulate, printed) = OrderScenario("ParityEqualityViolated")
            .Run(side => side.Create("Order", ("Status", status), ("Level", level)));
        await Assert.That(string.Join("\n", ParityScenario.Differences(simulate, printed))).IsEqualTo(CreateThrowsVersusFails);
        await Assert.That(simulate[0].Message).IsEqualTo(message);
    }

    [Test]
    public async Task Create_WhenDefaultViolatesEquality_FailsWithTheSameMessage() {
        // G1: a default that violates == is an analysis Error; Export and Create both refuse it.
        var domain = EvolvedDomain.FromDsl(EqualityDsl).Domain;
        var entity = domain.Types.OfType<Entity>().Single();
        entity = entity with {
            Properties = [.. entity.Properties.Select(p => p.Name == "Code"
                ? p with { Constraints = [.. p.Constraints, new EqualityConstraint("Other")] }
                : p)]
        };
        domain = domain with { Types = [.. domain.Types.Select(t => t is Entity ? entity : t)] };
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.HasErrors).IsTrue();
        var first = analysis.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message;
        await Assert.That(first).Contains("Code");

        var exportEx = Assert.Throws<InvalidOperationException>(
            () => new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(exportEx!.Message).IsEqualTo(first);

        var createEx = Assert.Throws<InvalidOperationException>(
            () => DomainEntityInstance.Create(entity, new Dictionary<string, object?> {
                ["Status"] = "s", ["Level"] = 1L
            }, domain));
        await Assert.That(createEx!.Message).IsEqualTo(first);
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

    // Owner: C2b (the compiled Create skips properties the first stage's entry assigns). The simulator checks
    // constraints before entry effects run, so equals() on such a property rejects a create the printed code accepts.
    // required() on the same property fails the same way today.
    [Test]
    public async Task KnownGap_EqualityOnEntryAssignedProperty_SimulateRejectsCreate() {
        var (simulate, printed) = EqualityScenario(EntryAssignedDsl, "ParityGapEntryAssigned", ("Mark", "x"))
            .Run(side => side.Create("Job", ("Tag", "t")));
        await Assert.That(printed[0].Success).IsTrue();
        await Assert.That(simulate[0].Message).IsEqualTo("'Mark' must equal x.");
    }

    // Owner: none named in the plan; closest is C2b (the compiled Create). The simulator's first-stage entry skips
    // transition effects (ApplyInitialStageEntryEffects), so the instance stays in the first stage; the printed
    // constructor performs the transition. Both sides compile and run: this is a behaviour gap, not a compile failure.
    [Test]
    public async Task KnownGap_FirstStageEntryTransition_SimulateStaysInFirstStage() {
        var dsl = await File.ReadAllTextAsync(Path.Combine(FindRepoRoot(), "docs/probes/dogfood/entry-transition-in-first-stage.poly"));
        var (simulate, printed) = ParityScenario.FromDsl(dsl, "ParityGapEntryTransition")
            .Run(side => side.Create("Z", ("Tag", "t")));
        await Assert.That(ParityScenario.Differences(simulate, printed))
            .IsEquivalentTo(["create Z: 'Stage' differs (simulate 'A', printed 'B')"]);
    }

    // Owner: C5b (multi-hop leaves the store). A subscriber's handler transition notifies its own subscribers in
    // simulate (the store recurses); the printed handler does not, so W stays in P in printed code.
    [Test]
    public async Task KnownGap_SubscriberTransitionCascade_PrintedStopsAfterOneHop() {
        const string cascade = """

            W: entity {
              Tracks: Tr
              P: stage { when Tracks Q { transition to R } }
              R: stage { }
            }
            """;
        var (simulate, printed) = ParityScenario.FromDsl(TrackingDsl + cascade, "ParityGapCascade")
            .Run(side => {
                var paper = side.Create("Paper", ("Title", "p"));
                var tr = side.Create("Tr", ("Tracks", paper));
                var w = side.Create("W", ("Tracks", tr));
                side.Use(paper);
                side.Invoke("Advance");
                side.Use(w);
            });
        await Assert.That(ParityScenario.Differences(simulate, printed))
            .IsEquivalentTo(["use W: 'Stage' differs (simulate 'R', printed 'P')"]);
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
