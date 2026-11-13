using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.Runtime;
using Poly.Tests.TestHelpers;

using D = Poly.Tests.DomainModeling.Lowering.InitialEntryDomains;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-04a (#683, #825): the initial stage's entry runs in <c>Create</c>, which RETURNS the entry's failure on both hosts.
/// These are the entry forms S-18 may later reject (<c>invoke</c>, <c>create</c>, <c>for … invoke</c>); the plan keeps
/// them in one class so S-18 deletes tests only. Each failing form is run through both doors: a bare
/// <c>Create</c> of the <c>Kid</c> and the holder's <c>Make</c> action (a <c>create in</c>).
/// </summary>
/// <remarks>
/// Owner ruling 2026-11-13, "any failure is a rollback": a failed create leaves nothing behind. Inside an action the
/// action scope restores everything on the generated C#, so those rows are plain <see cref="AtomicityParityTests"/>
/// rows. A bare generated <c>Create</c> opens no scope yet, so a failing entry that already changed ANOTHER entity
/// (a callee's write, a child attached to an existing peer) leaves that change on the generated C# while the
/// simulator's executor transaction restores it. Those rows are marked "UntilL04b": the marker is the explicit
/// divergence the lock allows, tied to L-04b (a bare <c>Create</c> opens a root scope), which turns them into plain
/// parity rows and deletes the marker.
/// </remarks>
public class InitialEntryFailureTests {
    private static async Task AssertFailedWithNothingLeft(IReadOnlyList<ParityOutcome> simulate, IReadOnlyList<ParityOutcome> printed) {
        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        foreach (var (host, outcomes) in new[] { ("simulate", simulate), ("printed", printed) }) {
            var failed = outcomes[^1];
            await Assert.That(failed.Success).IsFalse().Because($"{host}: the last step is the failing create");
            await Assert.That(failed.ExceptionType).IsNull().Because($"{host}: the failure is returned, never thrown");
            await Assert.That(failed.FailureKind).IsNotNull().Because($"{host}: the failure is typed");
            await Assert.That(failed.GraphAfter == failed.GraphBefore).IsTrue()
                .Because($"{host}: the failed create left no trace:\nbefore:\n{failed.GraphBefore}after:\n{failed.GraphAfter}");
        }
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    private static (IReadOnlyList<ParityOutcome> Simulate, IReadOnlyList<ParityOutcome> Printed) RunBare(
        string dsl, string name, Action<ParitySide> steps) =>
        ParityScenario.FromDsl(dsl, name, recordGraph: true).Run(steps);

    // ── invoke on self ────────────────────────────────────────────────

    private const string InvokeSelf = "invoke Fails";

    [Test]
    public async Task BareCreate_WhenTheEntryInvokesAFailingActionOnSelf_ReturnsItsFailureWithKidFailsPath() {
        var (simulate, printed) = RunBare(D.Dsl(InvokeSelf), "InitialEntryInvokeSelfBare", side => side.Create("Kid"));

        await AssertFailedWithNothingLeft(simulate, printed);
        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.FailureKind).IsEqualTo("Policy");
            await Assert.That(outcome.FailureGuard).IsEqualTo("Never");
            await Assert.That(outcome.FailurePath).IsEqualTo("Kid.Fails");
        }
    }

    [Test]
    public async Task Make_WhenTheKidsEntryInvokesAFailingActionOnSelf_ReturnsItsFailureWithHolderMakeKidFailsPath() {
        var (simulate, printed) = await AtomicityParityTests.AssertRollsBack(
            D.Dsl(InvokeSelf), "InitialEntryInvokeSelfMake", (side, fail) => {
                side.Create("Holder");
                fail("Make", []);
            });

        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.ExceptionType).IsNull();
            await Assert.That(outcome.FailureKind).IsEqualTo("Policy");
            await Assert.That(outcome.FailureGuard).IsEqualTo("Never");
            await Assert.That(outcome.FailurePath).IsEqualTo("Holder.Make.Kid.Fails");
        }
        await Assert.That(simulate[^1].State["kids"]).IsEqualTo("0");
    }

    // ── invoke on another entity ──────────────────────────────────────

    // The callee (Holder.Bump) succeeds and changes the holder, then the entry's assign breaks a range.
    private const string InvokeOther = "invoke holder.Bump\nassign Score to Cap";

    [Test]
    public async Task Make_WhenTheEntryChangesTheHolderThenFails_RestoresTheHolder() {
        var (simulate, printed) = await AtomicityParityTests.AssertRollsBack(
            D.Dsl(InvokeOther), "InitialEntryInvokeOtherMake", (side, fail) => {
                side.Create("Holder");
                fail("Make", []);
            });

        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.ExceptionType).IsNull();
            await Assert.That(outcome.FailureKind).IsEqualTo("Range");
            await Assert.That(outcome.State["Total"]).IsEqualTo("0");
        }
    }

    // PINNED, removed by L-04b (a bare generated `Create` opens a root action scope): the callee's wrapper opened and
    // committed its own scope, and the bare factory has none to roll back, so after the failed create the holder keeps
    // Total = 1 on the generated C#. The simulator's executor transaction restores it. This is the one divergence the
    // owner's ruling allows L-04a, and the graph is the only thing that differs.
    [Test]
    public async Task BareCreate_WhenTheEntryChangesTheHolderThenFails_LeavesTheHolderChangedOnPrintedUntilL04b() {
        var (simulate, printed) = RunBare(D.Dsl(InvokeOther), "InitialEntryInvokeOtherBare", side => {
            var holder = side.Create("Holder");
            side.Create("Kid", ("holder", holder));
        });

        var differences = ParityScenario.DifferenceSet(simulate, printed);
        await Assert.That(differences.Select(d => (d.Index, d.Field)).ToArray())
            .IsEquivalentTo(new[] { (1, "graph") }).Because("only the graph differs, and only at the failing create");
        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.Success).IsFalse();
            await Assert.That(outcome.ExceptionType).IsNull();
            await Assert.That(outcome.FailureKind).IsEqualTo("Range");
        }
        await Assert.That(simulate[^1].GraphAfter == simulate[^1].GraphBefore).IsTrue().Because("the simulator's transaction restored the holder");
        await Assert.That(printed[^1].GraphAfter == printed[^1].GraphBefore).IsFalse()
            .Because("L-04b: the generated C# keeps the callee's write until a bare Create opens a root scope");
        await Assert.That(printed[^1].GraphAfter!).Contains("Total=1");
    }

    // ── create a child ────────────────────────────────────────────────

    // The child names the existing holder as its owner, so the leaf's factory attaches it to the holder's collection,
    // then the entry's assign fails.
    private const string CreateChild = "create Leaf { Amount: 1 owner: holder }\nassign Score to Cap";

    [Test]
    public async Task Make_WhenTheEntryCreatesAChildOfTheHolderThenFails_TakesTheChildOutOfTheHolder() {
        var (simulate, printed) = await AtomicityParityTests.AssertRollsBack(
            D.Dsl(CreateChild), "InitialEntryCreateChildMake", (side, fail) => {
                side.Create("Holder");
                fail("Make", []);
            });

        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.ExceptionType).IsNull();
            await Assert.That(outcome.State["leaves"]).IsEqualTo("0");
            await Assert.That(outcome.State["kids"]).IsEqualTo("0");
        }
    }

    // PINNED, removed by L-04b: the leaf stays attached to the holder on the generated C#.
    [Test]
    public async Task BareCreate_WhenTheEntryCreatesAChildOfThePeerThenFails_LeavesTheChildInThePeerOnPrintedUntilL04b() {
        var (simulate, printed) = RunBare(D.Dsl(CreateChild), "InitialEntryCreateChildBare", side => {
            var holder = side.Create("Holder");
            side.Create("Kid", ("holder", holder));
        });

        var differences = ParityScenario.DifferenceSet(simulate, printed);
        await Assert.That(differences.Select(d => (d.Index, d.Field)).ToArray())
            .IsEquivalentTo(new[] { (1, "graph") });
        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.Success).IsFalse();
            await Assert.That(outcome.ExceptionType).IsNull();
        }
        await Assert.That(simulate[^1].GraphAfter == simulate[^1].GraphBefore).IsTrue();
        await Assert.That(printed[^1].GraphAfter!).Contains(GraphState.Orphan)
            .Because("L-04b: the leaf the failed create attached to the holder is an orphan until a bare Create opens a root scope");
    }

    // A failing child create inside the entry is returned too (a child's own constraint, not the Kid's).
    [Test]
    public async Task BareCreate_WhenTheEntryCreatesAChildThatFails_ReturnsTheChildsFailure() {
        var (simulate, printed) = RunBare(D.Dsl("create Leaf { Amount: Cap }"), "InitialEntryCreateChildFails", side => side.Create("Kid"));

        await AssertFailedWithNothingLeft(simulate, printed);
        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.FailureKind).IsEqualTo("Range");
            await Assert.That(outcome.Message!).Contains("'Amount' must be <= 10");
        }
    }

    // ── for … invoke ─────────────────────────────────────────────────

    // The entry creates two ticks of its own, then pops each: the first pop succeeds and changes the first tick, the
    // second fails. The ticks are reachable only from the Kid being created, so nothing of the failed create is visible
    // from an existing entity and both doors are plain parity rows.
    private const string TickEntities = """
        Tick: entity {
          Ok: Boolean
          Allowed: policy { Ok is true }
          Open: stage { Pop: action require Allowed { transition to Spent } }
          Spent: stage { }
        }
        """;

    private const string ForInvokeEntry = "create in ticks { Ok: true }\ncreate in ticks { Ok: false }\nfor ticks as t invoke t.Pop";

    [Test]
    public async Task BareCreate_WhenTheSecondForInvokeFails_ReturnsTheFailureOnBothHosts() {
        var (simulate, printed) = RunBare(D.Dsl(ForInvokeEntry, "ticks: many Tick", entities: TickEntities),
            "InitialEntryForInvokeBare", side => side.Create("Kid"));

        await AssertFailedWithNothingLeft(simulate, printed);
        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.FailureKind).IsEqualTo("Policy");
            await Assert.That(outcome.FailureGuard).IsEqualTo("Allowed");
            await Assert.That(outcome.FailurePath).IsEqualTo("Tick.Pop");
        }
    }

    [Test]
    public async Task Make_WhenTheKidsSecondForInvokeFails_RollsBackAndReturnsTheFailure() {
        var (simulate, printed) = await AtomicityParityTests.AssertRollsBack(
            D.Dsl(ForInvokeEntry, "ticks: many Tick", entities: TickEntities), "InitialEntryForInvokeMake", (side, fail) => {
                side.Create("Holder");
                fail("Make", []);
            });

        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.ExceptionType).IsNull();
            await Assert.That(outcome.FailurePath).IsEqualTo("Holder.Make.Tick.Pop");
            await Assert.That(outcome.State["kids"]).IsEqualTo("0");
        }
    }

    // ── every entry form compiles with warnings as errors (CS0127 cannot come back) ──

    internal static readonly IReadOnlyDictionary<string, string> EntryForms = new Dictionary<string, string> {
        ["Assign"] = D.Dsl("assign Score to 5"),
        ["Transition"] = D.Dsl("transition to Next", "Next: stage { entry { assign Score to 7 } }"),
        ["InvokeSelf"] = D.Dsl(InvokeSelf),
        ["InvokeOther"] = D.Dsl(InvokeOther),
        ["Create"] = D.Dsl(CreateChild),
        ["ForInvoke"] = D.Dsl(ForInvokeEntry, "ticks: many Tick", entities: TickEntities),
    };

    public static IEnumerable<string> EntryFormNames() => EntryForms.Keys;

    [Test]
    [MethodDataSource(nameof(EntryFormNames))]
    public async Task EntryForm_PrintsCSharpThatCompilesWithWarningsAsErrors(string form) {
        var session = ParitySessions.Create(ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(EntryForms[form], session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        await Assert.That(evolved.Succeeded).IsTrue();
        var cs = new Poly.Interpretation.CSharp.CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));

        var diagnostics = ExportedCSharp.CompileWithWarningsAsErrors("EntryForm" + form, [("N.cs", cs)]);

        await Assert.That(string.Join("\n", diagnostics.Select(d => d.ToString()))).IsEqualTo("");
    }
}
