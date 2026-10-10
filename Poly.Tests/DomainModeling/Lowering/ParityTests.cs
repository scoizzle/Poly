using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Runtime;
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

    const string EnumDsl = """
        domain Shop
        PatronStatus: enum { Active, Suspended }
        Patron: entity {
          Status: PatronStatus default(Active)
          Open: stage {
            Suspend: action {
              assign Status to Suspended
              transition to Closed
            }
          }
          Closed: stage { }
          IsActive: policy { Status is "Active" }
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

    const string QuantifierDsl = """
        domain Library
        Patron: entity {
          Name: Text required
          Email: Text unique
          MaxItems: Number range(0, 20) required
          loans: many Loan
          AllActiveLoans: policy { all loans where Status is "Active" }
          NoneLostLoans: policy { none loans where Status is "Lost" }
          HasOverdueCount: policy { count loans where Status is "Overdue" > 0 }
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

    const string RequiredDsl = """
        domain Lab
        Widget: entity {
          Name: Text required
        }
        """;

    const string LengthDsl = """
        domain Lab
        Widget: entity {
          Name: Text length(3, 10)
        }
        """;

    const string PatternDsl = """
        domain Lab
        Widget: entity {
          Code: Text pattern("^[A-Z]{2}$")
        }
        """;

    const string UniqueDsl = """
        domain Lab
        Widget: entity {
          Code: Text unique
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

    const string UnboundAdapterDsl = """
        domain Shop
        Order: entity {
          Total: Number default(0)
          Pay: action (request: ChargeRequest) {
            assign Total to Total
          }
        }

        Stripe: contract external stripe v1 {
          ChargeRequest: value {
            Amount: Number
            Currency: Text
          }
          Charge: outbound operation ChargeRequest
        }

        ChargeOrder: bind Stripe Charge to Pay request
        """;

    [Test]
    public async Task Invoke_WhenUnboundContractEndpoint_FailsTheSameWay() {
        var outcomes = await ParityScenario.FromDsl(UnboundAdapterDsl, "ParityUnboundAdapter")
            .AssertAgree(side => {
                side.Create("Order");
                side.Invoke("Pay", ("request", new Dictionary<string, object?> {
                    ["Amount"] = 10L,
                    ["Currency"] = "USD"
                }));
            });
        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).IsEqualTo(
            "Contract endpoint 'Stripe.Charge' has no in-process adapter.");
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
    public async Task Invoke_WhenAssigningEnumMember_AgreesOnStatusStageAndPolicy() {
        var outcomes = await ParityScenario.FromDsl(EnumDsl, "ParityEnum")
            .AssertAgree(side => {
                side.Create("Patron");
                side.EvaluatePolicy("IsActive");
                side.Invoke("Suspend");
                side.EvaluatePolicy("IsActive");
            });
        await Assert.That(outcomes[0].Success).IsTrue();
        await Assert.That(outcomes[0].State["Status"]).IsEqualTo("Active");
        await Assert.That(outcomes[0].State["Stage"]).IsEqualTo("Open");
        await Assert.That(outcomes[1].State["IsActive"]).IsEqualTo("True");
        await Assert.That(outcomes[2].Success).IsTrue();
        await Assert.That(outcomes[2].Message).IsNull();
        await Assert.That(outcomes[2].State["Status"]).IsEqualTo("Suspended");
        await Assert.That(outcomes[2].State["Stage"]).IsEqualTo("Closed");
        await Assert.That(outcomes[3].State["IsActive"]).IsEqualTo("False");
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
    public async Task EvaluatePolicy_All_AgreesForLinkedLoans() {
        var outcomes = await ParityScenario.FromDsl(QuantifierDsl, "ParityAll")
            .AssertAgree(side => {
                var a1 = side.Create("Loan", ("Status", "Active"));
                var a2 = side.Create("Loan", ("Status", "Active"));
                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-all@lib.test"), ("MaxItems", 5L),
                    ("loans", new[] { a1, a2 }));
                side.EvaluatePolicy("AllActiveLoans");

                var active = side.Create("Loan", ("Status", "Active"));
                var overdue = side.Create("Loan", ("Status", "Overdue"));
                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-mixed@lib.test"), ("MaxItems", 5L),
                    ("loans", new[] { active, overdue }));
                side.EvaluatePolicy("AllActiveLoans");

                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-empty@lib.test"), ("MaxItems", 5L));
                side.EvaluatePolicy("AllActiveLoans");
            });
        await Assert.That(outcomes[3].State["AllActiveLoans"]).IsEqualTo("True");
        await Assert.That(outcomes[7].State["AllActiveLoans"]).IsEqualTo("False");
        await Assert.That(outcomes[9].State["AllActiveLoans"]).IsEqualTo("False");
    }

    [Test]
    public async Task EvaluatePolicy_None_AgreesForLinkedLoans() {
        var outcomes = await ParityScenario.FromDsl(QuantifierDsl, "ParityNone")
            .AssertAgree(side => {
                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-empty@lib.test"), ("MaxItems", 5L));
                side.EvaluatePolicy("NoneLostLoans");

                var a1 = side.Create("Loan", ("Status", "Active"));
                var a2 = side.Create("Loan", ("Status", "Active"));
                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-active@lib.test"), ("MaxItems", 5L),
                    ("loans", new[] { a1, a2 }));
                side.EvaluatePolicy("NoneLostLoans");

                var lost = side.Create("Loan", ("Status", "Lost"));
                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-lost@lib.test"), ("MaxItems", 5L),
                    ("loans", new[] { lost }));
                side.EvaluatePolicy("NoneLostLoans");
            });
        await Assert.That(outcomes[1].State["NoneLostLoans"]).IsEqualTo("True");
        await Assert.That(outcomes[5].State["NoneLostLoans"]).IsEqualTo("True");
        await Assert.That(outcomes[8].State["NoneLostLoans"]).IsEqualTo("False");
    }

    [Test]
    public async Task EvaluatePolicy_FilteredCount_AgreesForLinkedLoans() {
        var outcomes = await ParityScenario.FromDsl(QuantifierDsl, "ParityFilteredCount")
            .AssertAgree(side => {
                var overdue = side.Create("Loan", ("Status", "Overdue"));
                var active = side.Create("Loan", ("Status", "Active"));
                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-overdue@lib.test"), ("MaxItems", 5L),
                    ("loans", new[] { overdue, active }));
                side.EvaluatePolicy("HasOverdueCount");

                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-empty@lib.test"), ("MaxItems", 5L));
                side.EvaluatePolicy("HasOverdueCount");
            });
        await Assert.That(outcomes[3].State["HasOverdueCount"]).IsEqualTo("True");
        await Assert.That(outcomes[5].State["HasOverdueCount"]).IsEqualTo("False");
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

    // Injected so Level's expected value is int 5 against a long property (create-time widening).
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

    [Test]
    [Arguments("Closed", 5L, "'Status' must equal Active.")]
    [Arguments("active", 5L, "'Status' must equal Active.")]
    [Arguments("Active", 6L, "'Level' must equal 5.")]
    public async Task Create_WhenEqualityViolated_FailsWithTheSameMessage(string status, long level, string message) {
        var outcomes = await OrderScenario("ParityEqualityViolated")
            .AssertAgree(side => side.Create("Order", ("Status", status), ("Level", level)));
        await Assert.That(outcomes[0].Success).IsFalse();
        await Assert.That(outcomes[0].Message).IsEqualTo(message);
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
                ["Status"] = "s",
                ["Level"] = 1L
            }, domain));
        await Assert.That(createEx!.Message).IsEqualTo(first);
    }

    // The DSL cannot author a stage policy. Same fixture as
    // DomainSessionTests.Lower_StagePolicy_IsMethodAndActionGuard.
    static Domain StagePolicyDomain(bool ready) {
        var result = new DomainEvolution(DomainFactory.Create("D")).Evolve()
            .AddEntity("Item")
            .AddStage("Item", "Open")
            .AddPolicyToStage("Item", "Open", "Ready", DomainExpression.Literal(ready))
            .AddActionToStage("Item", "Open", "Go")
            .Apply();
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return result.Root;
    }

    // Action-local policy via AddPolicyToAction, not on entity.Policies.
    static Domain ActionLocalPolicyDomain() {
        var result = new DomainEvolution(DomainFactory.Create("D")).Evolve()
            .AddEntity("Item")
            .AddStage("Item", "Open")
            .AddActionToStage("Item", "Open", "Go")
            .AddPolicyToAction("Item", "Go", "Ok", DomainExpression.Literal(false))
            .Apply();
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return result.Root;
    }

    static string PrintedCSharp(Domain domain) {
        var analysis = DomainModelAnalyzer.Analyze(domain);
        return new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
    }

    [Test]
    public async Task Invoke_WhenStagePolicyIsTrue_Agrees() {
        var outcomes = await ParityScenario.FromDomain(StagePolicyDomain(true), "ParityStagePolicyTrue")
            .AssertAgree(side => {
                side.Create("Item");
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].Success).IsTrue();
    }

    // Both sides fail. Simulate ErrorMessage is empty (ActionInvocationResult.Blocked);
    // printed returns the Failure string — same C8d split as KnownGap_RequireBlocksAction.
    [Test]
    public async Task Invoke_WhenStagePolicyIsFalse_BothSidesFail() {
        var domain = StagePolicyDomain(false);
        var source = PrintedCSharp(domain);
        await Assert.That(source).Contains("bool Ready(");
        await Assert.That(source).Contains("blocked by policy 'Ready'");

        var (simulate, printed) = ParityScenario.FromDomain(domain, "ParityStagePolicyFalse")
            .Run(side => {
                side.Create("Item");
                side.Invoke("Go");
            });
        await Assert.That(simulate[1].Success).IsFalse();
        await Assert.That(printed[1].Success).IsFalse();
        await Assert.That(string.Join("\n", ParityScenario.Differences(simulate, printed)))
            .IsEqualTo("invoke Go: failure message differs (simulate '', printed ''Go' blocked by policy 'Ready'.')");
    }

    [Test]
    public async Task Invoke_WhenActionLocalPolicyIsFalse_BothSidesFail() {
        var domain = ActionLocalPolicyDomain();
        var item = domain.Types.OfType<Entity>().Single(e => e.Name == "Item");
        await Assert.That(item.Policies).IsEmpty();
        await Assert.That(item.Stages.Single().Policies).IsEmpty();
        await Assert.That(item.Stages.Single().Actions.Single().Policies.Single().Name).IsEqualTo("Ok");

        var source = PrintedCSharp(domain);
        await Assert.That(source).Contains("bool Ok(");
        await Assert.That(source).Contains("blocked by policy 'Ok'");

        var (simulate, printed) = ParityScenario.FromDomain(domain, "ParityActionLocalPolicyFalse")
            .Run(side => {
                side.Create("Item");
                side.Invoke("Go");
            });
        await Assert.That(simulate[1].Success).IsFalse();
        await Assert.That(printed[1].Success).IsFalse();
        await Assert.That(string.Join("\n", ParityScenario.Differences(simulate, printed)))
            .IsEqualTo("invoke Go: failure message differs (simulate '', printed ''Go' blocked by policy 'Ok'.')");
    }

    // Known gaps: the sides differ today. Each row pins the exact differences so a fix turns it red;
    // then replace it with an AssertAgree row.

    [Test]
    public async Task Create_WhenOutOfRange_FailsWithTheSameMessage() {
        var outcomes = await ParityScenario.FromDsl(RangeDsl, "ParityCreateRange")
            .AssertAgree(side => side.Create("Widget", ("Score", 99L)));
        await Assert.That(outcomes[0].Success).IsFalse();
        await Assert.That(outcomes[0].Message).IsEqualTo("'Score' must be <= 10.");
    }

    [Test]
    public async Task Create_WhenRequiredOmitted_FailsWithTheSameMessage() {
        var outcomes = await ParityScenario.FromDsl(RequiredDsl, "ParityCreateRequired")
            .AssertAgree(side => side.Create("Widget"));
        await Assert.That(outcomes[0].Success).IsFalse();
        await Assert.That(outcomes[0].Message).IsEqualTo("'Name' is required.");
    }

    [Test]
    public async Task Create_WhenLengthTooShort_FailsWithTheSameMessage() {
        var outcomes = await ParityScenario.FromDsl(LengthDsl, "ParityCreateLength")
            .AssertAgree(side => side.Create("Widget", ("Name", "ab")));
        await Assert.That(outcomes[0].Success).IsFalse();
        await Assert.That(outcomes[0].Message).IsEqualTo("'Name' must be at least 3 characters.");
    }

    [Test]
    public async Task Create_WhenPatternMisses_FailsWithTheSameMessage() {
        var outcomes = await ParityScenario.FromDsl(PatternDsl, "ParityCreatePattern")
            .AssertAgree(side => side.Create("Widget", ("Code", "a1")));
        await Assert.That(outcomes[0].Success).IsFalse();
        await Assert.That(outcomes[0].Message).IsEqualTo("'Code' does not match the required pattern.");
    }

    // Unique after created = new is outside CreateFactoryCheckPrefix. Simulate TryAdd
    // throws IOE; printed EnsureUnique is still the Success stub, so both Creates succeed.
    [Test]
    public async Task KnownGap_CreateDuplicateUnique_SimulateThrowsAndPrintedSucceeds() {
        var (simulate, printed) = ParityScenario.FromDsl(UniqueDsl, "ParityGapCreateDuplicateUnique")
            .Run(side => {
                side.Create("Widget", ("Code", "A"));
                side.Create("Widget", ("Code", "A"));
            });
        await Assert.That(ParityScenario.Differences(simulate, printed))
            .IsEquivalentTo([
                "create Widget: success differs (simulate 'False', printed 'True')",
                "create Widget: failure message differs (simulate 'Unique constraint violated: 'Code' value is already used on another 'Widget'.', printed '')",
                "create Widget: exception type differs (simulate 'InvalidOperationException', printed '')",
                "create Widget: 'Code' differs (simulate '(absent)', printed 'A')",
                "create Widget: 'Stage' differs (simulate '(absent)', printed '')"
            ]);
    }

    [Test]
    public async Task Create_WhenDefaultOmitted_LandsOnBothSides() {
        var outcomes = await ParityScenario.FromDsl(RangeDsl, "ParityCreateDefault")
            .AssertAgree(side => side.Create("Widget"));
        await Assert.That(outcomes[0].Success).IsTrue();
        await Assert.That(outcomes[0].State["Score"]).IsEqualTo("5");
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

    [Test]
    public async Task Create_WhenEqualityOnEntryAssignedProperty_Succeeds() {
        var outcomes = await EqualityScenario(EntryAssignedDsl, "ParityEntryAssigned", ("Mark", "x"))
            .AssertAgree(side => side.Create("Job", ("Tag", "t")));
        await Assert.That(outcomes[0].Success).IsTrue();
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