using System.Collections;
using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-03c (#683; #565, #567, #568): a failed action leaves no trace on either host. The module touches every entity at
/// the entry of each mutator (the action wrapper, a handler, <c>Attach*</c>, <c>Register*Subscriber</c>), so its action
/// scope restores the action's own writes, a callee's writes on another entity, a handler's writes on the subscriber,
/// and a child left in a peer's collection or registry.
/// </summary>
/// <remarks>
/// Every row runs one scenario twice on one compiled module. Each run asserts three things about the failing step,
/// which is the scenario's last: the two hosts agree on every step, including the whole instance graph
/// (<see cref="GraphState"/>); on each host the graph after the failed step equals the graph before it, rendered against
/// the table taken before it, so a child the step left behind shows as an orphan; and the second run is the scope-only
/// oracle, where the simulator runs the failing step with no executor transaction
/// (<see cref="ParitySide.InvokeScopeOnly"/>), so only the module's action scope can restore what it touched.
/// </remarks>
public class AtomicityParityTests {
    private static (string Cs, Assembly Assembly) Print(string dsl, string assemblyName) {
        var session = ParitySessions.Create(ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!evolved.Succeeded)
            throw new InvalidOperationException(string.Join("; ", evolved.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var cs = new Poly.Interpretation.CSharp.CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));
        return (cs, ExportedCSharp.CompileAndLoad(cs, assemblyName));
    }

    internal delegate void Steps(ParitySide side, Action<string, (string Name, object? Value)[]> fail);

    /// <summary>The three assertions, over the normal run and the scope-only run. Returns the normal run.</summary>
    internal static async Task<(IReadOnlyList<ParityOutcome> Simulate, IReadOnlyList<ParityOutcome> Printed)> AssertRollsBack(
        string dsl, string assemblyName, Steps steps) {
        var scenario = ParityScenario.FromDsl(dsl, assemblyName, recordGraph: true);
        (IReadOnlyList<ParityOutcome> Simulate, IReadOnlyList<ParityOutcome> Printed)? normal = null;
        foreach (var scopeOnly in new[] { false, true }) {
            var run = scenario.Run(side => steps(side, (action, args) => {
                if (scopeOnly)
                    side.InvokeScopeOnly(action, args);
                else
                    side.Invoke(action, args);
            }));
            var label = scopeOnly ? "scope-only" : "normal";
            var differences = string.Join("\n", ParityScenario.Differences(run.Simulate, run.Printed));
            await Assert.That(differences.Length == 0).IsTrue()
                .Because($"{label} run: the hosts agree on every step and graph:\n{differences}");
            foreach (var (host, outcomes) in new[] { ("simulate", run.Simulate), ("printed", run.Printed) }) {
                var failed = outcomes[^1];
                await Assert.That(failed.Success).IsFalse().Because($"{label} {host}: the last step is the failing one");
                await Assert.That(failed.GraphAfter == failed.GraphBefore).IsTrue()
                    .Because($"{label} {host}: the failed step '{failed.Step}' left no trace:\nbefore:\n{failed.GraphBefore}after:\n{failed.GraphAfter}");
            }
            await Assert.That(ActionScope.HasOpenScope).IsFalse();
            normal ??= run;
        }
        return normal!.Value;
    }

    // ── the row's two reds ──────────────────────────────────────────────

    private const string SettleDsl = """
        domain Library
        MemberStatus: enum { Active, Suspended }

        Member: entity {
          Status: MemberStatus default(Active)
          FineBalance: Number range(0, 100000) default(0)
          Settle: action (amount: Number) {
            assign Status to Suspended
            assign FineBalance to (FineBalance - amount)
          }
        }
        """;

    // #565, verbatim.
    [Test]
    public async Task MultiAssign_WhenLaterAssignBreaksRange_LeavesStateUnchanged() {
        var (simulate, _) = await AssertRollsBack(SettleDsl, "AtomicitySettle", (side, fail) => {
            side.Create("Member");
            fail("Settle", [("amount", 10L)]);
        });

        await Assert.That(simulate[^1].FailureKind).IsEqualTo("Range");
        await Assert.That(simulate[^1].State["Status"]).IsEqualTo("Active");
    }

    private const string PractDsl = """
        domain Min2

        Pract: entity {
          Name: Text length(1, 80)
          Booked: Number default(0) range(0, 2)
          appts: many Appt
          Book: action (key: Text) {
            create in appts { Key: key }
            assign Booked to Booked + 1
          }
        }

        Appt: entity {
          Key: Text
        }
        """;

    // #568, verbatim: the third Book fails and leaves two appointments.
    [Test]
    public async Task CreateIn_WhenLaterAssignFails_DoesNotAppendChild() {
        var (simulate, _) = await AssertRollsBack(PractDsl, "AtomicityPract", (side, fail) => {
            side.Create("Pract", ("Name", "Dr"));
            side.Invoke("Book", ("key", "k0"));
            side.Invoke("Book", ("key", "k1"));
            fail("Book", [("key", "k2")]);
        });

        await Assert.That(simulate[^1].State["appts"]).IsEqualTo("2");
        await Assert.That(simulate[^1].State["Booked"]).IsEqualTo("2");
    }

    // ── #567 ───────────────────────────────────────────────────────────

    // #567's one DSL, verbatim: the child create is validated last, after the two assigns.
    private const string AcctDsl = """
        domain AuditA
        Acct: entity {
          Balance: Decimal range(-5000, 100)
          Count: Number range(0, 1) default(0)
          entries: many owned Entry
          Open: stage {
            Dep: action (amount: Decimal) {
              assign Balance to Balance + amount
              assign Count to Count + 1
              create in entries { Amount: amount }
            }
          }
        }
        Entry: entity { Amount: Decimal range(0.01, 100) }
        """;

    [Test]
    public async Task Dep_WhenTheChildCreateFailsLast_LeavesBalanceAndCount() {
        var (simulate, _) = await AssertRollsBack(AcctDsl, "AtomicityDep", (side, fail) => {
            side.Create("Acct", ("Balance", 10m));
            fail("Dep", [("amount", -5m)]);
        });

        await Assert.That(simulate[^1].State["Balance"]).IsEqualTo("10");
        await Assert.That(simulate[^1].State["Count"]).IsEqualTo("0");
        await Assert.That(simulate[^1].State["entries"]).IsEqualTo("0");
    }

    // #567's bank cases are described in prose only; these DSLs are reconstructed from it.
    private const string BankDsl = """
        domain Bank
        Account: entity {
          Balance: Decimal range(0, 100000)
          accruals: many Accrual
          Open: stage {
            Withdraw: action (amount: Decimal) { assign Balance to Balance - amount }
            Deposit: action (amount: Decimal) { assign Balance to Balance + amount }
            Freeze: action { transition to Frozen }
            Accrue: action (amount: Decimal) {
              create in accruals { Amount: amount }
              assign Balance to Balance + amount
            }
          }
          Frozen: stage { }
        }
        Accrual: entity { Amount: Decimal }
        Transfer: entity {
          Amount: Decimal
          source: Account
          target: Account
          Pending: stage {
            Settle: action {
              invoke source.Withdraw(amount: Amount)
              invoke target.Deposit(amount: Amount)
              transition to Done
            }
          }
          Done: stage { }
        }
        Customer: entity {
          Name: Text
          accounts: many Account
          FreezeAll: action { for accounts as a invoke a.Freeze }
        }
        """;

    // Reconstructed: Settle to a frozen target debits the source on the generated C# today.
    [Test]
    public async Task Settle_WhenTheTargetIsFrozen_RestoresTheDebitedSource() {
        var (simulate, _) = await AssertRollsBack(BankDsl, "AtomicitySettleFrozen", (side, fail) => {
            var source = side.Create("Account", ("Balance", 1000m));
            var target = side.Create("Account", ("Balance", 0m));
            side.Use(target);
            side.Invoke("Freeze");
            side.Create("Transfer", ("Amount", 100m), ("source", source), ("target", target));
            fail("Settle", []);
        });

        await Assert.That(simulate[^1].FailureKind).IsEqualTo("Stage");
        await Assert.That(simulate[^1].State["Stage"]).IsEqualTo("Pending");
    }

    // Reconstructed: Settle with no target debits the source, then fails on the missing link.
    [Test]
    public async Task Settle_WhenTheTargetIsMissing_RestoresTheDebitedSource() {
        await AssertRollsBack(BankDsl, "AtomicitySettleNull", (side, fail) => {
            var source = side.Create("Account", ("Balance", 1000m));
            side.Create("Transfer", ("Amount", 100m), ("source", source));
            fail("Settle", []);
        });
    }

    // Reconstructed: FreezeAll over [Open, Frozen] freezes the first account, then fails on the second.
    [Test]
    public async Task FreezeAll_WhenTheSecondAccountIsFrozen_RestoresTheFirst() {
        await AssertRollsBack(BankDsl, "AtomicityFreezeAll", (side, fail) => {
            var open = side.Create("Account", ("Balance", 1m));
            var frozen = side.Create("Account", ("Balance", 1m));
            side.Use(frozen);
            side.Invoke("Freeze");
            side.Create("Customer", ("Name", "c"), ("accounts", new[] { open!, frozen! }));
            fail("FreezeAll", []);
        });
    }

    // Reconstructed: Accrue creates the accrual before the balance range check.
    [Test]
    public async Task Accrue_WhenTheBalanceBreaksItsRange_DropsTheAccrual() {
        var (simulate, _) = await AssertRollsBack(BankDsl, "AtomicityAccrue", (side, fail) => {
            side.Create("Account", ("Balance", 99990m));
            fail("Accrue", [("amount", 20m)]);
        });

        await Assert.That(simulate[^1].State["accruals"]).IsEqualTo("0");
    }

    // ── A2: callees on other entities ──────────────────────────────────

    private const string CalleeDsl = """
        domain Calls
        Kid: entity {
          N: Number default(0)
          Bump: action { assign N to N + 1 }
          Open: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        Holder: entity {
          Count: Number range(0, 5) default(0)
          child: Kid
          kids: many Kid
          Run: action (n: Number) {
            invoke child.Bump
            assign Count to n
          }
          FinishAll: action { for k in kids { invoke k.Finish } }
        }
        """;

    [Test]
    public async Task Invoke_WhenTheCalleeSucceedsAndTheCallerFailsAfter_RestoresTheCallee() {
        await AssertRollsBack(CalleeDsl, "AtomicityCallee", (side, fail) => {
            var kid = side.Create("Kid");
            side.Create("Holder", ("child", kid));
            fail("Run", [("n", 9L)]);
        });
    }

    [Test]
    public async Task ForInvoke_WhenTheSecondRecordFails_RestoresTheFirst() {
        await AssertRollsBack(CalleeDsl, "AtomicityForInvoke", (side, fail) => {
            var first = side.Create("Kid");
            var second = side.Create("Kid");
            side.Use(second);
            side.Invoke("Finish");
            side.Create("Holder", ("kids", new[] { first!, second! }));
            fail("FinishAll", []);
        });
    }

    // ── handlers ──────────────────────────────────────────────────────

    private const string HandlerDsl = """
        domain H
        Owner: entity {
          Count: Number default(0)
          Flag: Text default("")
          Seen: Number range(0, 10) default(0)
          items: many Item
          Never: policy { Count > 100 }
          Y: action require Never { assign Count to Count + 1 }
          when items Done { assign Flag to "first" }
          when items Done { invoke Y }
          when items Shut { assign Seen to Seen - 1 }
        }
        Item: entity {
          N: Number range(0, 0) default(0)
          Open: stage {
            Finish: action { transition to Done }
            FinishThenBreak: action (n: Number) {
              transition to Gone
              assign N to n
            }
            Close: action { transition to Shut }
          }
          Done: stage { }
          Gone: stage { }
          Shut: stage { }
        }
        Watcher: entity {
          Flag: Text default("")
          items: many Item
          when items Gone { assign Flag to "seen" }
        }
        """;

    // G3: the first handler's write on the subscriber is restored when the second handler fails.
    [Test]
    public async Task Handler_WhenALaterHandlerFails_RestoresTheEarlierHandlersWrite() {
        await AssertRollsBack(HandlerDsl, "AtomicityHandlerPair", (side, fail) => {
            var item = side.Create("Item");
            side.Create("Owner", ("items", new[] { item! }));
            side.Use(item);
            fail("Finish", []);
        });
    }

    // Amendment 4: the handler succeeds, then a later effect of the action that fired it fails.
    [Test]
    public async Task Handler_WhenItSucceedsAndALaterOuterEffectFails_RestoresTheHandlersWrite() {
        await AssertRollsBack(HandlerDsl, "AtomicityHandlerThenOuter", (side, fail) => {
            var item = side.Create("Item");
            side.Create("Watcher", ("items", new[] { item! }));
            side.Use(item);
            fail("FinishThenBreak", [("n", 1L)]);
        });
    }

    // Amendment 4: a handler's assign-range failure after the transition that fired it. L-04a: the handler RETURNS the
    // typed failure (it threw before), the notify site returns it to the wrapper, and the wrapper rolls the transition back.
    [Test]
    public async Task Handler_WhenItsAssignBreaksARangeAfterTheTransition_RestoresTheTransition() {
        var (simulate, printed) = await AssertRollsBack(HandlerDsl, "AtomicityHandlerRange", (side, fail) => {
            var item = side.Create("Item");
            side.Create("Owner", ("items", new[] { item! }));
            side.Use(item);
            fail("Close", []);
        });

        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.ExceptionType).IsNull();
            await Assert.That(outcome.FailureKind).IsEqualTo("Range");
            await Assert.That(outcome.Message!).Contains("'Seen' must be >= 0");
        }
    }

    // G5, the #818 repro: the nested wrapper of the failing callee rolls the frame back mid-cascade, then the cascade
    // unwinds. The child's _notifyDepth is in the graph, so it must come back to 0, not -1.
    private const string CascadeDsl = """
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

    [Test]
    public async Task NotifyDepth_WhenANestedWrapperRollsBackMidCascade_ComesBackToZero() {
        var (_, printed) = await AssertRollsBack(CascadeDsl, "AtomicityDepth", (side, fail) => {
            side.Create("Owner");
            side.Invoke("Add");
            fail("Run", []);
        });

        // A depth at its default (0) is not rendered; the -1 a relative exit leaves would be.
        await Assert.That(printed[^1].GraphBefore!).DoesNotContain("_notifyDepth");
        await Assert.That(printed[^1].GraphAfter!).DoesNotContain("_notifyDepth");
    }

    // A thrown failure: the subscription cascade overflows its depth guard and throws on both hosts. The throw unwinds
    // through every wrapper's finally, so the whole cascade is rolled back (the cascade-overflow seed, #599).
    private const string OverflowDsl = """
        domain Cycle

        Left: entity {
          Name: Text
          Hops: Number default(0)
          rights: many Right
          A: stage { when rights X, Y { assign Hops to Hops + 1 transition to B } }
          B: stage { when rights X, Y { assign Hops to Hops + 1 transition to A } }
        }

        Right: entity {
          Name: Text
          Hops: Number default(0)
          lefts: many Left
          X: stage {
            Poke: action { transition to Y }
            when lefts A, B { assign Hops to Hops + 1 transition to Y }
          }
          Y: stage { when lefts A, B { assign Hops to Hops + 1 transition to X } }
        }
        """;

    [Test]
    public async Task CascadeOverflow_WhenTheDepthGuardThrows_RollsTheWholeCascadeBack() {
        var (simulate, printed) = await AssertRollsBack(OverflowDsl, "AtomicityOverflow", (side, fail) => {
            var left = side.Create("Left", ("Name", "L"));
            var right = side.Create("Right", ("Name", "R"));
            side.Use(left);
            side.Link("rights", right);
            side.Use(right);
            side.Link("lefts", left);
            fail("Poke", []);
        });

        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.ExceptionType).IsEqualTo("InvalidOperationException");
            await Assert.That(outcome.Message!).Contains("exceeded max depth");
        }
    }

    // ── stage, peers and registries ───────────────────────────────────

    // A4: the source stage's exit, the target stage's entry and CurrentStage are all restored.
    private const string StageDsl = """
        domain St
        Item: entity {
          K: Number default(0)
          M: Number default(0)
          N: Number range(0, 0) default(0)
          A: stage {
            exit { assign K to 1 }
            Go: action (n: Number) {
              transition to B
              assign N to n
            }
          }
          B: stage { entry { assign M to 5 } }
        }
        """;

    [Test]
    public async Task Transition_WhenALaterAssignFails_RestoresExitEntryAndStage() {
        var (simulate, _) = await AssertRollsBack(StageDsl, "AtomicityStage", (side, fail) => {
            side.Create("Item");
            fail("Go", [("n", 1L)]);
        });

        await Assert.That(simulate[^1].State["Stage"]).IsEqualTo("A");
        await Assert.That(simulate[^1].State["K"]).IsEqualTo("0");
        await Assert.That(simulate[^1].State["M"]).IsEqualTo("0");
    }

    // G4 (the L-03a review's item 1): a create-in whose child names an existing peer. The child's factory attaches it to
    // the peer's collection; the owner's later failure must take it out again, or the peer keeps an orphan.
    private const string PeerDsl = """
        domain Peers
        Shelf: entity {
          Label: Text
          books: many Book
        }
        Book: entity {
          Code: Text
          shelf: Shelf
          owner: Owner
        }
        Owner: entity {
          Count: Number range(0, 5) default(0)
          books: many Book
          Add: action (s: Shelf, code: Text, n: Number) {
            create in books { shelf: s Code: code }
            assign Count to n
          }
          Make: action (s: Shelf, code: Text, n: Number) {
            create Book { shelf: s Code: code }
            assign Count to n
          }
        }
        """;

    [Test]
    public async Task CreateIn_WhenTheChildNamesAnExistingPeer_TakesTheChildOutOfThePeer() {
        await AssertRollsBack(PeerDsl, "AtomicityPeerCreateIn", (side, fail) => {
            var shelf = side.Create("Shelf", ("Label", "s"));
            side.Create("Owner");
            fail("Add", [("s", shelf), ("code", "c"), ("n", 9L)]);
        });
    }

    // A4: a by-type create whose child names an existing peer.
    [Test]
    public async Task CreateByType_WhenTheChildNamesAnExistingPeer_TakesTheChildOutOfThePeer() {
        await AssertRollsBack(PeerDsl, "AtomicityPeerCreate", (side, fail) => {
            var shelf = side.Create("Shelf", ("Label", "s"));
            side.Create("Owner");
            fail("Make", [("s", shelf), ("code", "c"), ("n", 9L)]);
        });
    }

    // A4: the new child subscribes to an existing entity, which records it in a subscriber registry; the later failure
    // must take the registry entry out again.
    private const string RegistryDsl = """
        domain Reg
        Thing: entity {
          Open: stage { Close: action { transition to Shut } }
          Shut: stage { }
        }
        Watcher: entity {
          Flag: Text default("")
          thing: Thing
          owner: Owner
          when thing Shut { assign Flag to "shut" }
        }
        Owner: entity {
          Count: Number range(0, 5) default(0)
          watchers: many Watcher
          Watch: action (t: Thing, n: Number) {
            create in watchers { thing: t }
            assign Count to n
          }
        }
        """;

    [Test]
    public async Task CreateIn_WhenTheChildSubscribesToAnExistingEntity_TakesItOutOfTheRegistry() {
        await AssertRollsBack(RegistryDsl, "AtomicityRegistry", (side, fail) => {
            var thing = side.Create("Thing");
            side.Create("Owner");
            fail("Watch", [("t", thing), ("n", 9L)]);
        });
    }

    // ── success characterisation: what a successful action leaves is unchanged by the slice ──

    // Literal graphs, recorded on master before the Touch emission: a successful multi-assign, create-in, transition
    // with a handler, invoke chain, for-invoke and by-type create with an attach.
    [Test]
    public async Task SuccessfulActions_LeaveTheRecordedGraphOnBothHosts() {
        var problems = new List<string>();
        Task Graph(string dsl, string name, Action<ParitySide> steps, string expected) {
            var (simulate, printed) = ParityScenario.FromDsl(dsl, name, recordGraph: true).Run(steps);
            var differences = string.Join("\n", ParityScenario.Differences(simulate, printed));
            if (differences.Length > 0)
                problems.Add($"{name}: the hosts differ:\n{differences}");
            if (!simulate[^1].Success)
                problems.Add($"{name}: the last step failed: {simulate[^1].Message}");
            if (simulate[^1].GraphParity != expected)
                problems.Add($"{name}: graph is\n{simulate[^1].GraphParity}");
            return Task.CompletedTask;
        }

        await Graph(SettleDsl, "AtomicityPinSettle", side => {
            side.Create("Member", ("FineBalance", 50L));
            side.Invoke("Settle", ("amount", 10L));
        }, "#0 {FineBalance=40; Status=\"Suspended\"}\n");
        await Graph(PractDsl, "AtomicityPinPract", side => {
            side.Create("Pract", ("Name", "Dr"));
            side.Invoke("Book", ("key", "k0"));
        }, "#0 {Booked=1; Name=\"Dr\"; _appts=[#1]}\n#1 {Key=\"k0\"}\n");
        await Graph(HandlerDsl, "AtomicityPinHandler", side => {
            var item = side.Create("Item");
            side.Create("Watcher", ("items", new[] { item! }));
            side.Use(item);
            side.Invoke("FinishThenBreak", ("n", 0L));
        }, "#0 {CurrentStage=\"Gone\"; _watcherItemsGoneSubscribers=[#1]}\n#1 {Flag=\"seen\"; _items=[#0]}\n");
        await Graph(CalleeDsl, "AtomicityPinCallee", side => {
            var kid = side.Create("Kid");
            side.Create("Holder", ("child", kid));
            side.Invoke("Run", ("n", 2L));
        }, "#0 {CurrentStage=\"Open\"; N=1}\n#1 {Child=#0; Count=2}\n");
        await Graph(BankDsl, "AtomicityPinFor", side => {
            var a = side.Create("Account", ("Balance", 1m));
            var b = side.Create("Account", ("Balance", 2m));
            side.Create("Customer", ("Name", "c"), ("accounts", new[] { a!, b! }));
            side.Invoke("FreezeAll");
        }, "#0 {Balance=1; CurrentStage=\"Frozen\"}\n#1 {Balance=2; CurrentStage=\"Frozen\"}\n#2 {Name=\"c\"; _accounts=[#0,#1]}\n");
        await Graph(PeerDsl, "AtomicityPinPeer", side => {
            var shelf = side.Create("Shelf", ("Label", "s"));
            side.Create("Owner");
            side.Invoke("Make", ("s", shelf), ("code", "c"), ("n", 1L));
        }, "#0 {Label=\"s\"; _books=[#2]}\n#1 {Count=1}\n#2 {Code=\"c\"; Shelf=#0}\n");

        await Assert.That(problems.Count == 0).IsTrue().Because(string.Join("\n", problems));
    }

    // ── rejections (amendment 10) ─────────────────────────────────────

    private static IReadOnlyList<string> ErrorCodes(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        return [.. result.Analysis.Diagnostics
            .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
            .Select(d => d.Code ?? "")];
    }

    // A cross-entity assign needs no rollback of its own: the DSL cannot write another entity's state, so a callee's
    // state only changes through the callee's own action, whose wrapper touches it. A path target does not parse
    // (positioned); an API-built one is the analyzer's EffectCrossEntityMutation (CrossEntityMutationAnalysisTests).
    [Test]
    public async Task CrossEntityAssign_IsRejected() {
        GrammarException? rejected = null;
        try {
            ErrorCodes("""
                domain X
                Kid: entity { N: Number default(0) }
                Holder: entity {
                  child: Kid
                  Run: action { assign child N to 1 }
                }
                """);
        }
        catch (GrammarException ex) {
            rejected = ex;
        }
        await Assert.That(rejected).IsNotNull();
        await Assert.That(rejected!.Message).Contains("line 5");
    }

    // An invoke on a many-navigation receiver is rejected: a fan-out goes through `for`, whose callee wrappers touch
    // each record.
    [Test]
    public async Task InvokeOnAManyNavigation_IsRejected() {
        var codes = ErrorCodes("""
            domain X
            Kid: entity { N: Number default(0) Bump: action { assign N to N + 1 } }
            Holder: entity {
              kids: many Kid
              Run: action { invoke kids.Bump }
            }
            """);
        await Assert.That(codes).Contains(DomainModelDiagnosticCodes.EffectInvokeShape);
    }

    // ── the pinned residual: unique-key reservations (G6, X-04) ───────

    private const string UniqueDsl = """
        domain U
        Tag: entity {
          Code: Text unique
          N: Number range(0, 0) default(0)
          Rename: action (code: Text, n: Number) {
            assign Code to code
            assign N to n
          }
        }
        """;

    // PINNED, removed by X-04 (the storage journal hook, `ActionScope.Begin(db)` and the port rolled back to its mark):
    // the wrapper opens its scope with no journal, so a rolled-back action restores the entity's Code but not the
    // storage port. The unique assign recorded the new key and released the old one before the later assign failed,
    // so after the rollback the new key stays reserved and the old one, which the entity holds again, is free.
    [Test]
    public async Task Unique_WhenTheActionRollsBack_LeavesTheNewKeyReserved_UntilX04() {
        var (_, assembly) = Print(UniqueDsl, "AtomicityUnique");
        var port = new RecordingStoragePort();
        var type = assembly.GetType("Tag")!;
        var tag = Unwrap(ExportedCSharp.InvokeCreate(type, ("code", "old"), ("db", port)));
        var rename = type.GetMethods(PrintedMembers.Any).Single(m => m.Name == "Rename");
        await Assert.That(port.Exists("Tag", "Code", "old")).IsTrue();

        var result = rename.Invoke(tag, [.. rename.GetParameters()
            .Select(p => p.ParameterType == typeof(IStoragePort) ? port : p.ParameterType == typeof(long) ? 1L : (object)"new")])!;

        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsFalse();
        await Assert.That(type.GetProperty("Code", PrintedMembers.Any)!.GetValue(tag)).IsEqualTo("old");
        await Assert.That(port.Exists("Tag", "Code", "new")).IsTrue().Because("X-04 releases it");
        await Assert.That(port.Exists("Tag", "Code", "old")).IsFalse().Because("X-04 records it again");
    }

    private static object Unwrap(object result) =>
        result.GetType().GetProperty("Value")!.GetValue(result)!;

    // ── L-04a: the initial stage's entry runs in Create, which returns its failure ────────────

    private static readonly string EntryRangeDsl = InitialEntryDomains.Dsl("assign Score to Cap");

    // The row's red test (#683 L-04): a range broken by the initial entry's assign is a returned, typed failure on both
    // hosts. It used to throw ConstraintFailureException out of the constructor, which the harness records without a
    // failure kind.
    [Test]
    public async Task Create_WhenInitialEntryEffectBreaksRange_ReturnsFailureNotException() {
        var scenario = ParityScenario.FromDsl(EntryRangeDsl, "AtomicityEntryRange", recordGraph: true);

        var (simulate, printed) = scenario.Run(side => side.Create("Kid"));

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.Success).IsFalse();
            await Assert.That(outcome.ExceptionType).IsNull();
            await Assert.That(outcome.FailureKind).IsEqualTo("Range");
            await Assert.That(outcome.FailurePath).IsEqualTo("Kid.Score");
            await Assert.That(outcome.Message!).Contains("'Score' must be <= 10");
            await Assert.That(outcome.GraphAfter == outcome.GraphBefore).IsTrue();
        }
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    // The same entry reached through the holder's create-in: the failure reaches Make with its path prefix, the action
    // rolls back, and the next action on the holder commits.
    [Test]
    public async Task Make_WhenTheKidsEntryAssignBreaksRange_RollsBackAndReturnsTheFailure() {
        var (simulate, printed) = await AssertRollsBack(EntryRangeDsl, "AtomicityEntryRangeMake", (side, fail) => {
            side.Create("Holder");
            fail("Make", []);
        });

        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.ExceptionType).IsNull();
            await Assert.That(outcome.FailureKind).IsEqualTo("Range");
            await Assert.That(outcome.FailurePath).IsEqualTo("Holder.Make.Kid.Score");
            await Assert.That(outcome.State["kids"]).IsEqualTo("0");
        }
    }

    // ── a bare Create{Nav} (L-03c nit 1) ──────────────────────────────

    private const string BareCreateNavDsl = """
        domain Bare
        Owner: entity {
          Count: Number default(0)
          items: many Item
          Never: policy { Count > 100 }
          Y: action require Never { assign Count to Count + 1 }
          Add: action { create in items { } }
          when items Open { invoke Y }
        }
        Item: entity {
          owner: Owner via items
          Open: stage { }
        }
        """;

    // PINNED, removed by L-04b (every public generated mutator opens a root scope when none is active): host code calling
    // `Owner.CreateItems()` with no action running gets the failed result of the child's initial-stage handler, but the
    // child was added to the owner's collection first and nothing rolls it back, so it stays.
    [Test]
    public async Task BareCreateNav_WhenTheChildsHandlerFails_LeavesTheChildInTheCollection_UntilL04b() {
        var (_, assembly) = Print(BareCreateNavDsl, "AtomicityBareCreateNav");
        var owner = ExportedCSharp.CreateEntity(assembly, "Owner");
        var create = owner.GetType().GetMethods(PrintedMembers.Any).SingleOrDefault(m => m.Name == "CreateItems")
            ?? throw new InvalidOperationException(string.Join(",", owner.GetType().GetMethods(PrintedMembers.Any).Select(m => m.Name)));

        var result = create.Invoke(owner, [])!;

        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsFalse();
        var items = (IList)owner.GetType().GetProperty("Items", PrintedMembers.Any)!.GetValue(owner)!;
        await Assert.That(items.Count).IsEqualTo(1).Because("L-04b: a bare Create{Nav} rolls the child back");
    }

    // ── the creation order the scope records (lock amendment 5) ───────

    private const string OrderDsl = """
        domain Order
        Leaf: entity { N: Number default(0) }
        Kid: entity {
          Start: stage { entry { create Leaf { } } }
        }
        """;

    // `ActionScope.Create` records the instance after `new` and before the entry runs, so a parent now PRECEDES the
    // children its entry creates (the child used to come first, because the constructor created it before `Create`
    // recorded the parent). Rollback order is the reverse of this list, so a child leaves before its parent.
    [Test]
    public async Task Create_InsideAnOpenScope_RecordsTheParentBeforeItsEntryCreatedChildren() {
        var (_, assembly) = Print(OrderDsl, "AtomicityCreateOrder");
        var token = ActionScope.Begin(null);
        IReadOnlyList<string> printed;
        try {
            ExportedCSharp.CreateEntity(assembly, "Kid");
            printed = [.. ActionScope.CreatedInstances.Select(c => c.Entity)];
            ActionScope.Commit(token);
        }
        finally {
            if (ActionScope.HasOpenScope)
                ActionScope.Rollback(token);
        }

        var (domain, _) = EvolvedDomain.FromDsl(OrderDsl);
        var exec = ModuleExec.For(domain);
        var simToken = ActionScope.Begin(null);
        IReadOnlyList<string> simulated;
        try {
            exec.Create("Kid", new Dictionary<string, object?>());
            simulated = [.. ActionScope.CreatedInstances.Select(c => c.Entity)];
            ActionScope.Commit(simToken);
        }
        finally {
            if (ActionScope.HasOpenScope)
                ActionScope.Rollback(simToken);
        }

        await Assert.That(string.Join(",", printed)).IsEqualTo("Kid,Leaf");
        await Assert.That(string.Join(",", simulated)).IsEqualTo("Kid,Leaf");
    }

    // ── a unique key a failed create reserved (X-04) ──────────────────

    private const string EntryUniqueDsl = """
        domain EntryUnique
        Kid: entity {
          Code: Text unique
          Cap: Number default(100)
          Score: Number range(0, 10) default(0)
          Start: stage { entry { assign Score to Cap } }
        }
        """;

    // The storage library records a unique key as the LAST step before `Success` (`db.Record`), after the initial entry.
    // The lock expected the unique guard to hold the key across a failed entry; it does not: the guard only checks that
    // the key is free, and the failing entry returns before the key is recorded, on master (where the constructor threw)
    // and now. So a create whose entry fails reserves nothing. What X-04 still owns is the key a create reserved when a
    // LATER step of its action fails (`Unique_WhenTheActionRollsBack_LeavesTheNewKeyReserved_UntilX04`). The control
    // keeps this from passing because the key is never recorded at all.
    [Test]
    public async Task Create_WhenTheEntryFails_ReservesNoUniqueKey() {
        var (_, assembly) = Print(EntryUniqueDsl, "AtomicityEntryUnique");
        var port = new RecordingStoragePort();
        var type = assembly.GetType("Kid")!;

        var failed = ExportedCSharp.InvokeCreate(type, ("code", "k"), ("db", port));
        var succeeded = ExportedCSharp.InvokeCreate(type, ("code", "ok"), ("cap", 5L), ("db", port));

        await Assert.That((bool)failed.GetType().GetProperty("IsSuccess")!.GetValue(failed)!).IsFalse();
        await Assert.That(port.Exists("Kid", "Code", "k")).IsFalse();
        await Assert.That((bool)succeeded.GetType().GetProperty("IsSuccess")!.GetValue(succeeded)!).IsTrue();
        await Assert.That(port.Exists("Kid", "Code", "ok")).IsTrue().Because("a successful create records its key");
    }

    // ── the factory records what it created (F3, amendment 8) ─────────

    private const string CreatedDsl = """
        domain C
        Item: entity { Code: Text }
        """;

    [Test]
    public async Task Create_InsideAnOpenScope_RecordsTheInstanceOnBothHosts() {
        var (_, assembly) = Print(CreatedDsl, "AtomicityCreated");
        var type = assembly.GetType("Item")!;
        var token = ActionScope.Begin(null);
        object printed;
        IReadOnlyList<(object Instance, string Entity)> recorded;
        try {
            printed = Unwrap(ExportedCSharp.InvokeCreate(type, ("code", "c")));
            recorded = ActionScope.CreatedInstances;
            ActionScope.Commit(token);
        }
        finally {
            if (ActionScope.HasOpenScope)
                ActionScope.Rollback(token);
        }
        await Assert.That(recorded.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(recorded[0].Instance, printed)).IsTrue();
        await Assert.That(recorded[0].Entity).IsEqualTo("Item");

        var (domain, _) = EvolvedDomain.FromDsl(CreatedDsl);
        var exec = ModuleExec.For(domain);
        var simToken = ActionScope.Begin(null);
        Poly.DomainModeling.Runtime.ModuleOutcome created;
        try {
            created = exec.Create("Item", new Dictionary<string, object?> { ["Code"] = "c" });
            recorded = ActionScope.CreatedInstances;
            ActionScope.Commit(simToken);
        }
        finally {
            if (ActionScope.HasOpenScope)
                ActionScope.Rollback(simToken);
        }
        await Assert.That(created.Succeeded).IsTrue();
        await Assert.That(recorded.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(recorded[0].Instance, created.Instance!.Bag)).IsTrue();
        await Assert.That(recorded[0].Entity).IsEqualTo("Item");
    }

    // ── the oracle itself: an orphan cannot hide (amendment 3) ────────

    [Test]
    public async Task GraphState_WhenAChildIsLeftInACollection_RendersAnOrphanOnBothReaders() {
        var (_, assembly) = Print(PractDsl, "AtomicityOrphan");
        var pract = ExportedCSharp.CreateEntity(assembly, "Pract", ("name", "Dr"));
        var table = GraphState.Table(GraphState.PrintedReader.Instance, [pract]);
        var before = GraphState.Render(GraphState.PrintedReader.Instance, table);
        var appts = (IList)pract.GetType().GetField("_appts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pract)!;
        appts.Add(ExportedCSharp.CreateEntity(assembly, "Appt", ("key", "k")));

        var after = GraphState.Render(GraphState.PrintedReader.Instance, table);

        await Assert.That(after).Contains(GraphState.Orphan);
        await Assert.That(after).IsNotEqualTo(before);

        var bag = new Dictionary<string, object?> { ["Name"] = "Dr", ["_appts"] = new List<object?>() };
        var bagTable = GraphState.Table(GraphState.BagReader.Instance, [bag]);
        var bagBefore = GraphState.Render(GraphState.BagReader.Instance, bagTable);
        ((List<object?>)bag["_appts"]!).Add(new Dictionary<string, object?> { ["Key"] = "k" });
        var bagAfter = GraphState.Render(GraphState.BagReader.Instance, bagTable);

        await Assert.That(bagAfter).Contains(GraphState.Orphan);
        await Assert.That(bagAfter).IsNotEqualTo(bagBefore);
    }
}