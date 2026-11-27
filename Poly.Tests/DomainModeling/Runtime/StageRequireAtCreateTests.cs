using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Mcp.Tools;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// #576: a stage <c>require</c> is an invariant — "every action that can run in the stage must keep it
/// true" — so it is also checked when the instance is created. <c>Create</c> sets the initial values,
/// runs the initial stage's entry, then re-checks that stage's requirements on the one lowered factory
/// both hosts run: an instance whose own initial values falsify its stage's requirement is refused and
/// never registered.
/// </summary>
public sealed class StageRequireAtCreateTests {
    private const string BooleanStage = """
        domain D
        Case: entity {
          Cleared: Boolean default(false)
          NotCleared: policy { Cleared is false }
          Open: stage require NotCleared {
            Clear: action { assign Cleared to true }
            Close: action { transition to Closed }
          }
          Closed: stage { }
        }
        """;

    private const string NumericStage = """
        domain B
        X: entity {
          Points: Number range(0, 100) default(0)
          Small: policy { Points <= 5 }
          Review: stage require Small {
            Bump: action { assign Points to 50 }
            Ok: action { assign Points to 1 }
          }
        }
        """;

    [Test]
    public async Task Create_WhenInitialValuesFalsifyStageRequire_FailsAndRegistersNothing() {
        var executor = ExecutorFor(BooleanStage);

        var refused = executor.Create("Case", Values(("Cleared", true)));

        await Assert.That(refused.Succeeded).IsFalse();
        await Assert.That(refused.Failure!.Kind).IsEqualTo(DomainFailureKind.Policy);
        await Assert.That(refused.Failure.Message)
            .IsEqualTo("'Create' blocked by policy 'NotCleared'. It would leave stage 'Open' invalid.");
        await Assert.That(refused.Failure.ElementPath).IsEqualTo("Case.Create");
        await Assert.That(refused.Failure.Guard).IsEqualTo("NotCleared");
        // The create rolls back like any other failure: nothing was registered.
        await Assert.That(executor.Instances.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Create_WhenSatisfyingValues_SucceedsAndTheStageRuns() {
        var executor = ExecutorFor(BooleanStage);

        var created = executor.Create("Case", Values(("Cleared", false)));

        await Assert.That(created.Succeeded).IsTrue();
        var instance = created.Instance!;
        await Assert.That(StageOf(instance)).IsEqualTo("Open");
        var close = executor.Invoke(instance, "Close");
        await Assert.That(close.Succeeded).IsTrue();
        await Assert.That(StageOf(instance)).IsEqualTo("Closed");
    }

    [Test]
    public async Task Create_WhenNumericInitialValueFalsifiesStageRequire_Fails() {
        // #518's shape: 50 is inside the declared range (0..100), so only the stage requirement refuses it.
        var executor = ExecutorFor(NumericStage);

        var refused = executor.Create("X", Values(("Points", 50L)));
        await Assert.That(refused.Succeeded).IsFalse();
        await Assert.That(refused.Failure!.Kind).IsEqualTo(DomainFailureKind.Policy);
        await Assert.That(refused.Failure.Message)
            .IsEqualTo("'Create' blocked by policy 'Small'. It would leave stage 'Review' invalid.");
        await Assert.That(refused.Failure.Guard).IsEqualTo("Small");

        var accepted = executor.Create("X", Values(("Points", 1L)));
        await Assert.That(accepted.Succeeded).IsTrue();
    }

    [Test]
    public async Task Create_WhenInitialEntrySatisfiesTheRequire_Succeeds() {
        // The check runs AFTER the initial stage's entry, so a requirement the entry establishes holds:
        // the raw argument falsifies it, and the entry's assign is what Create is judged on.
        var executor = ExecutorFor("""
            domain E
            Note: entity {
              Memo: Text optional
              HasMemo: policy { Memo is not null }
              Open: stage require HasMemo {
                entry { assign Memo to "seeded" }
                Wipe: action { assign Memo to "kept" }
              }
            }
            """);

        var created = executor.Create("Note", Values());

        await Assert.That(created.Succeeded).IsTrue();
        await Assert.That(created.Instance!.Bag["Memo"]).IsEqualTo("seeded");
    }

    [Test]
    public async Task Create_IsJudgedByItsInitialStageOnly_Succeeds() {
        // Closed requires the opposite of Open; a create lands in Open, so Closed's requirement is
        // not the one Create is judged by.
        var executor = ExecutorFor("""
            domain S
            Ticket: entity {
              Cleared: Boolean default(false)
              NotCleared: policy { Cleared is false }
              ClearedNow: policy { Cleared is true }
              Open: stage require NotCleared {
                Close: action { transition to Closed }
              }
              Closed: stage require ClearedNow { }
            }
            """);

        var created = executor.Create("Ticket", Values(("Cleared", false)));

        await Assert.That(created.Succeeded).IsTrue();
        await Assert.That(StageOf(created.Instance!)).IsEqualTo("Open");
    }

    [Test]
    public async Task Create_WhenEntryTransitionsAway_IsJudgedByTheStageItLandsIn() {
        // The check dispatches on CurrentStage, so a create whose entry transitions out is judged by the
        // stage it landed in, not by the one it started in.
        var executor = ExecutorFor("""
            domain T
            Case: entity {
              Cleared: Boolean default(false)
              NotCleared: policy { Cleared is false }
              ClearedNow: policy { Cleared is true }
              Open: stage require NotCleared {
                entry { assign Cleared to true  transition to Closed }
              }
              Closed: stage require ClearedNow { }
            }
            """);

        var created = executor.Create("Case", Values());

        await Assert.That(created.Succeeded).IsTrue();
        await Assert.That(StageOf(created.Instance!)).IsEqualTo("Closed");
    }

    [Test]
    public async Task Create_WhenStageRequireReadsAnUnlinkedOptionalNavigation_IsRefusedCleanly() {
        // A stage requirement that reads a to-one hop cannot hold with no target: the create is refused
        // as a failure, never a throw out of the factory.
        var executor = ExecutorFor("""
            domain P
            Room: entity { Occupied: Boolean default(false) }
            Reservation: entity {
              room: Room optional
              RoomFree: policy { room Occupied is false }
              Booked: stage require RoomFree {
                CheckIn: action { transition to InHouse }
              }
              InHouse: stage { }
            }
            """);

        var refused = executor.Create("Reservation", Values());

        await Assert.That(refused.Succeeded).IsFalse();
        await Assert.That(refused.Failure!.Kind).IsEqualTo(DomainFailureKind.Policy);
        await Assert.That(refused.Failure.Message)
            .IsEqualTo("'Create' blocked by policy 'RoomFree'. It would leave stage 'Booked' invalid.");
        await Assert.That(refused.Failure.Guard).IsEqualTo("RoomFree");
    }

    [Test]
    public async Task Create_StageRequire_IsTheSameRefusalOnBothHosts() {
        // The check lives in the lowered Create, so the simulator and the printed C# refuse alike.
        var outcomes = await ParityScenario.FromDsl(BooleanStage, "ParityStageRequireAtCreate")
            .AssertAgree(side => {
                side.Create("Case", ("Cleared", true));
                side.Create("Case", ("Cleared", false));
            });

        await Assert.That(outcomes[0].Success).IsFalse();
        await Assert.That(outcomes[0].FailureKind).IsEqualTo("Policy");
        await Assert.That(outcomes[0].FailurePath).IsEqualTo("Case.Create");
        await Assert.That(outcomes[0].FailureGuard).IsEqualTo("NotCleared");
        await Assert.That(outcomes[0].Message)
            .IsEqualTo("'Create' blocked by policy 'NotCleared'. It would leave stage 'Open' invalid.");
        await Assert.That(outcomes[1].Success).IsTrue();
    }

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

    private static string? StageOf(ModuleInstance instance) =>
        instance.Bag.TryGetValue("CurrentStage", out var stage) && stage is not null
            ? Convert.ToString(stage, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    private static ModuleInstanceExecutor ExecutorFor(string poly) => new(Lower(poly));

    /// <summary>Analyzed domain with the product libraries loaded and the module cached.</summary>
    private static Domain Lower(string poly) {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        foreach (var id in ExtensionCatalog.ProductAuthoring)
            builder.Load(ExtensionCatalog.Core.Resolve(id));
        builder.Load(new PersistenceEmitLibrary());
        var session = builder.Build();
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        _ = RuntimeAnalysisCache.GetOrLower(result.Root!, session, result.Analysis);
        return result.Root!;
    }
}