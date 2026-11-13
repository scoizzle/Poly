using System.Collections;
using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation.CSharp;
using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-03h (#818): a subscription handler returns the failed result of anything it invokes, the notify dispatcher
/// returns the first failure, and the effect that fired the notification (a transition, a create-in) returns it to
/// its action. One lowered module, both hosts. Handler throw arms (assign range/enum/length guards, create failures)
/// are L-04's and keep throwing; the one newly-throwing case (a create-in inside a handler whose child's handler
/// fails) is pinned below.
/// </summary>
public class ActionScopeNestedFailureTests {
    // A handler passes the failure through with no segment of its own (fork F3, option B).
    private const string ExpectedPath = "Owner.Run.Item.Finish.Owner.Y";

    private const string Dsl = """
        domain P
        Owner: entity {
          Count: Number default(0)
          Flag: Text default("")
          items: many Item
          Never: policy { Count > 100 }
          Y: action require Never { assign Count to Count + 1 }
          Add: action { create in items { } }
          Run: action { for i in items { invoke i.Finish } }
          when items Done { invoke Y }
          when items Done { assign Flag to "second" }
        }
        Item: entity {
          Open: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        """;

    private static (string Cs, Assembly Assembly) Print(string dsl, string assemblyName) {
        var session = ParitySessions.Create(ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!evolved.Succeeded)
            throw new InvalidOperationException(string.Join("; ", evolved.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var cs = new CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));
        return (cs, ExportedCSharp.CompileAndLoad(cs, assemblyName));
    }

    private static string PrintOnly(string dsl) {
        var session = ParitySessions.Create(ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!evolved.Succeeded)
            throw new InvalidOperationException(string.Join("; ", evolved.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return new CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));
    }

    private static string StrictDiagnostics(string cs, string name) =>
        string.Join("\n", ExportedCSharp.CompileWithWarningsAsErrors(name, [("N.cs", cs)]).Select(d => d.ToString()));

    private static object? Read(object instance, string property) =>
        instance.GetType().GetProperty(property, PrintedMembers.Any)!.GetValue(instance);

    private static object Call(object instance, string method) =>
        instance.GetType().GetMethod(method, PrintedMembers.Any)!.Invoke(instance, [])!;

    private static bool IsSuccess(object result) =>
        (bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!;

    // R1
    [Test]
    public async Task OuterAction_WhenAHandlerInvokesAFailingAction_ReturnsTheFailure() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl);
        var exec = ModuleExec.For(domain);
        var owner = exec.Create("Owner", new Dictionary<string, object?>()).Instance!;
        await Assert.That(exec.Invoke(owner, "Add").Succeeded).IsTrue();
        var child = ModuleReads.Related(exec, "items", owner).Single();

        var run = exec.Invoke(owner, "Run");

        await Assert.That(run.Succeeded).IsFalse();
        await Assert.That(run.Failure!.Kind).IsEqualTo(DomainFailureKind.Policy);
        await Assert.That(run.Failure.Guard).IsEqualTo("Never");
        await Assert.That(run.Failure.Message).Contains("Never");
        await Assert.That(run.Failure.ElementPath).IsEqualTo(ExpectedPath);
        await Assert.That(ModuleReads.Stage(child)).IsEqualTo("Open");
        await Assert.That(owner.Bag["Count"]).IsEqualTo(0L);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    // R2
    [Test]
    public async Task Printed_WhenAHandlerInvokesAFailingAction_CompilesWithWarningsAsErrors() {
        var cs = PrintOnly(Dsl);

        await Assert.That(StrictDiagnostics(cs, "NestedFailureStrict")).IsEqualTo("");
    }

    // R3
    [Test]
    public async Task Parity_WhenAHandlerInvokesAFailingAction_BothHostsReturnTheSameFailure() {
        var outcomes = await ParityScenario.FromDsl(Dsl, "NestedFailureParity")
            .AssertAgree(side => {
                var owner = side.Create("Owner");
                side.Use(owner);
                side.Invoke("Add");
                side.Invoke("Run");
                side.Use(owner);
            });

        await Assert.That(outcomes[3].Success).IsFalse();
        await Assert.That(outcomes[3].FailureKind).IsEqualTo("Policy");
        await Assert.That(outcomes[3].FailurePath).IsEqualTo(ExpectedPath);
        await Assert.That(outcomes[3].FailureGuard).IsEqualTo("Never");
        await Assert.That(outcomes[4].State["Count"]).IsEqualTo("0");
    }

    // R4: the dispatcher stops at the first failing handler, so the second handler never ran.
    // Printed host, state read straight off the entity: the simulator restores everything on a
    // failure, so "the rest did not run" is only observable here.
    [Test]
    public async Task Printed_NotifyDispatcher_ReturnsTheFirstFailureAndSkipsTheRestOfTheHandlers() {
        var (cs, assembly) = Print(Dsl, "NestedFailureDispatcher");
        var owner = ExportedCSharp.CreateEntity(assembly, "Owner");
        await Assert.That(IsSuccess(Call(owner, "Add"))).IsTrue();
        var child = ((IEnumerable)Read(owner, "Items")!).Cast<object>().Single();

        var finish = Call(child, "Finish");

        await Assert.That(IsSuccess(finish)).IsFalse();
        await Assert.That(Read(owner, "Flag")).IsEqualTo("");
        await Assert.That(cs).Contains("DomainResult NotifyDoneSubscribers(");
        await Assert.That(cs).DoesNotContain("void NotifyDoneSubscribers(");
    }

    // R5: a create-in whose child lands in a watched initial stage with a failing handler.
    private const string CreateInDsl = """
        domain P
        Owner: entity {
          Count: Number default(0)
          items: many Item
          Never: policy { Count > 100 }
          Y: action require Never { assign Count to Count + 1 }
          Add: action { create in items { } }
          when items Open { invoke Y }
        }
        Item: entity {
          Open: stage { }
        }
        """;

    // Create{Nav} passes the handler's failure through with no prefix; the action's FailCreate adds Owner.Add.
    private const string CreateInPath = "Owner.Add.Owner.Y";

    [Test]
    public async Task CreateIn_WhenTheChildsInitialStageHandlerFails_TheActionReturnsTheFailure() {
        var (domain, _) = EvolvedDomain.FromDsl(CreateInDsl);
        var exec = ModuleExec.For(domain);
        var owner = exec.Create("Owner", new Dictionary<string, object?>()).Instance!;

        var add = exec.Invoke(owner, "Add");

        await Assert.That(add.Succeeded).IsFalse();
        await Assert.That(add.Failure!.Kind).IsEqualTo(DomainFailureKind.Policy);
        await Assert.That(add.Failure.Guard).IsEqualTo("Never");
        await Assert.That(add.Failure.ElementPath).IsEqualTo(CreateInPath);
        await Assert.That(ModuleReads.Related(exec, "items", owner).Count).IsEqualTo(0);

        var (cs, assembly) = Print(CreateInDsl, "NestedFailureCreateIn");
        var printedOwner = ExportedCSharp.CreateEntity(assembly, "Owner");
        await Assert.That(IsSuccess(Call(printedOwner, "Add"))).IsFalse();
        // Today's behaviour of the generated C#, which L-03c's rollback changes: the failed child is not undone. It
        // was already added to the owner's collection before its initial-stage handler ran.
        await Assert.That(((ICollection)Read(printedOwner, "Items")!).Count).IsEqualTo(1);
        await Assert.That(StrictDiagnostics(cs, "NestedFailureCreateInStrict")).IsEqualTo("");
    }

    [Test]
    public async Task Parity_CreateInWhenTheChildsInitialStageHandlerFails_BothHostsReturnTheSameFailure() {
        var (simulate, printed) = ParityScenario.FromDsl(CreateInDsl, "NestedFailureCreateInParity")
            .Run(side => {
                var owner = side.Create("Owner");
                side.Use(owner);
                side.Invoke("Add");
            });

        foreach (var outcome in new[] { simulate[2], printed[2] }) {
            await Assert.That(outcome.Success).IsFalse();
            await Assert.That(outcome.FailureKind).IsEqualTo("Policy");
            await Assert.That(outcome.FailurePath).IsEqualTo(CreateInPath);
            await Assert.That(outcome.FailureGuard).IsEqualTo("Never");
        }
        // The one difference is the state the failed child leaves behind: the simulator restores it, the generated
        // C# does not (L-03c's rollback closes this).
        await Assert.That(ParityScenario.Differences(simulate, printed))
            .IsEquivalentTo(new[] { "invoke Add: 'items' differs (simulate '0', printed '1')" });
    }

    private static int NotifyDepth(object instance) =>
        (int)instance.GetType().GetField("_notifyDepth", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(instance)!;

    // F2: the cascade depth is balanced after a failure return and after a handler that throws.
    [Test]
    public async Task Printed_NotifyDepth_IsZeroAfterAFailingHandlerAndAfterAThrowingHandler() {
        var (_, assembly) = Print(Dsl, "NestedFailureDepth");
        var owner = ExportedCSharp.CreateEntity(assembly, "Owner");
        Call(owner, "Add");
        var child = ((IEnumerable)Read(owner, "Items")!).Cast<object>().Single();
        await Assert.That(IsSuccess(Call(child, "Finish"))).IsFalse();
        await Assert.That(NotifyDepth(child)).IsEqualTo(0);

        const string throwing = """
            domain P
            S: entity {
              Count: Number default(0)
              jobs: many Job
              kids: many Kid
              Never: policy { Count > 100 }
              Y: action require Never { assign Count to Count + 1 }
              when jobs Done { create in kids { } }
              when kids Open { invoke Y }
            }
            Job: entity {
              Open: stage { Finish: action { transition to Done } }
              Done: stage { }
            }
            Kid: entity {
              Open: stage { }
            }
            """;
        var (_, throwingAssembly) = Print(throwing, "NestedFailureDepthThrow");
        var job = Poly.Tests.DomainModeling.Compile.WhenAnySimulatePrintAgreeTests.CreateEntity(throwingAssembly, "Job");
        var holder = Poly.Tests.DomainModeling.Compile.WhenAnySimulatePrintAgreeTests.CreateEntity(throwingAssembly, "S");
        Poly.Tests.DomainModeling.Compile.WhenAnySimulatePrintAgreeTests.Attach(holder, "Jobs", job);
        var threw = false;
        try { Call(job, "Finish"); }
        catch (TargetInvocationException e) when (e.InnerException?.GetType().Name == "ConstraintFailureException") { threw = true; }
        await Assert.That(threw).IsTrue();
        await Assert.That(NotifyDepth(job)).IsEqualTo(0);
    }

    // F3: a handler's own transition enters a stage another entity watches. The notify site inside the handler
    // returns the second handler's failure, and the handler adds no path segment.
    private const string HandlerTransitionDsl = """
        domain P
        Boss: entity {
          Count: Number default(0)
          owners: many Owner
          Never: policy { Count > 100 }
          Y: action require Never { assign Count to Count + 1 }
          when owners Busy { invoke Y }
        }
        Owner: entity {
          items: many Item
          Idle: stage { }
          Busy: stage { }
          when items Done { transition to Busy }
        }
        Item: entity {
          Open: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        """;

    [Test]
    public async Task Parity_AHandlersOwnTransitionIntoAWatchedStage_ReturnsTheSecondHandlersFailure() {
        var (simulate, printed) = ParityScenario.FromDsl(HandlerTransitionDsl, "NestedFailureHandlerTransition")
            .Run(side => {
                var item = side.Create("Item");
                var owner = side.Create("Owner", ("items", new[] { item! }));
                side.Create("Boss", ("owners", new[] { owner! }));
                side.Use(item);
                side.Invoke("Finish");
            });

        foreach (var outcome in new[] { simulate[4], printed[4] }) {
            await Assert.That(outcome.Success).IsFalse();
            await Assert.That(outcome.FailureKind).IsEqualTo("Policy");
            await Assert.That(outcome.FailurePath).IsEqualTo("Item.Finish.Boss.Y");
            await Assert.That(outcome.FailureGuard).IsEqualTo("Never");
        }
        // The one difference is the child's stage: the simulator restores it, the generated C# does not (L-03c).
        await Assert.That(ParityScenario.Differences(simulate, printed))
            .IsEquivalentTo(new[] { "invoke Finish: 'Stage' differs (simulate 'Open', printed 'Done')" });
    }

    // A target property named Notify must not collide with the notify local of Create{Nav}.
    [Test]
    public async Task CreateIn_WhenTheTargetHasAPropertyNamedNotify_CompilesWithWarningsAsErrors() {
        const string dsl = """
            domain P
            Owner: entity {
              Count: Number default(0)
              items: many Item
              Never: policy { Count > 100 }
              Y: action require Never { assign Count to Count + 1 }
              Add: action { create in items { Notify: "n" } }
              when items Open { invoke Y }
            }
            Item: entity {
              Notify: Text
              Open: stage { }
            }
            """;
        var cs = PrintOnly(dsl);

        await Assert.That(StrictDiagnostics(cs, "NestedFailureNotifyName")).IsEqualTo("");
    }

    // Amendment 7: inside a handler a create-in whose child's handler fails used to be dropped. A handler has no
    // result channel for create failures yet (L-04), so the returned failure reaches FailCreate and throws.
    [Test]
    public async Task HandlerCreateIn_WhenTheChildsHandlerFails_ThrowsOnBothHostsUntilL04() {
        const string dsl = """
            domain P
            S: entity {
              Count: Number default(0)
              jobs: many Job
              kids: many Kid
              Never: policy { Count > 100 }
              Y: action require Never { assign Count to Count + 1 }
              when jobs Done { create in kids { } }
              when kids Open { invoke Y }
            }
            Job: entity {
              Open: stage { Finish: action { transition to Done } }
              Done: stage { }
            }
            Kid: entity {
              Open: stage { }
            }
            """;

        var outcomes = await ParityScenario.FromDsl(dsl, "NestedFailureHandlerCreate")
            .AssertAgree(side => {
                var job = side.Create("Job");
                side.Create("S", ("jobs", new[] { job! }));
                side.Use(job);
                side.Invoke("Finish");
            });

        await Assert.That(outcomes[3].Success).IsFalse();
        await Assert.That(outcomes[3].ExceptionType).IsEqualTo("ConstraintFailureException");
        await Assert.That(outcomes[3].Message).Contains("Never");
    }

    // R6: every handler form returns a result and compiles with warnings as errors: each / any / all (previousStage),
    // peer `as`, stage-scoped and entity-level handlers; invoke, for-invoke, general for, a transition cascade.
    private const string EveryFormDsl = """
        domain W
        Owner: entity {
          Hits: Number default(0)
          Tag: Text default("t")
          items: many Item
          Never: policy { Hits > 100 }
          Y: action require Never { assign Hits to Hits + 1 }
          Add: action { create in items { } }
          Run: action { for i in items { invoke i.Finish } }
          when items Done { invoke Y }
          when any items Done { assign Tag to "any" }
          when all items Done { assign Hits to Hits + 2 }
          when items Done as peer { for x in items { invoke x.Reset } }
          when items Open { transition to Busy }
          Idle: stage {
            when items Done { invoke Y }
          }
          Busy: stage { }
        }
        Item: entity {
          Open: stage { Finish: action { transition to Done } }
          Done: stage { Reset: action { transition to Open } }
        }
        """;

    [Test]
    public async Task EveryHandlerForm_ReturnsAResultAndCompilesWithWarningsAsErrors() {
        var cs = PrintOnly(EveryFormDsl);

        await Assert.That(StrictDiagnostics(cs, "NestedFailureEveryForm")).IsEqualTo("");
        await Assert.That(cs).Contains("DomainResult WhenEachItemDone");
        await Assert.That(cs).DoesNotContain("void WhenEach");
        await Assert.That(cs).DoesNotContain("void WhenAny");
        await Assert.That(cs).DoesNotContain("void WhenAll");
        await Assert.That(cs).Contains("DomainResult WhenAnyItemDone");
        await Assert.That(cs).Contains("DomainResult WhenAllItemDone");
    }

    // R7: shape. Handlers and dispatchers return DomainResult; each absence is paired with a positive.
    [Test]
    public async Task Shape_HandlersAndDispatchersReturnDomainResult() {
        var cs = PrintOnly(Dsl);

        await Assert.That(cs).Contains("internal DomainResult WhenEachItemDone_CallY(");
        await Assert.That(cs).DoesNotContain("void WhenEachItemDone_CallY(");
        await Assert.That(cs).Contains("DomainResult NotifyDoneSubscribers(");
        await Assert.That(cs).DoesNotContain("void NotifyDoneSubscribers(");
    }

    // R8: the depth guard still throws (L-05 owns it).
    [Test]
    public async Task NotifyCascadeDepthGuard_StillThrows() {
        var cs = PrintOnly(Dsl);

        await Assert.That(cs).Contains("private void EnterNotifyCascade(");
        await Assert.That(cs).Contains("throw new System.InvalidOperationException(");
    }
}