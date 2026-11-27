using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Mcp.Tools;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// #576 / #577 / #518: a stage <c>require</c> is documented as an invariant — "every action that can
/// run in the stage must keep it true" — so it is checked on the state AFTER an action as well as
/// before it, on the one lowered module both hosts run: an action in the stage may not falsify it,
/// and the failure rolls the body back.
/// </summary>
public class StageRequireInvariantTests {
    private const string BooleanStage = """
        domain D
        Case: entity {
          Cleared: Boolean default(false)
          NotCleared: policy { Cleared is false }
          Open: stage require NotCleared {
            Clear: action { assign Cleared to true }
            Close: action { transition to Closed }
          }
          Closed: stage {}
        }
        """;

    private const string NumericStage = """
        domain B
        X: entity {
          Points: Number range(0,100) default(0)
          Small: policy { Points <= 5 }
          Review: stage require Small {
            Bump: action { assign Points to 50 }
            Ok: action { assign Points to 1 }
          }
        }
        """;

    [Test]
    public async Task Invoke_FalsifyingStageRequire_FailsAndRollsBack() {
        // #577: 'Clear' would leave stage 'Open' violating 'NotCleared'; the instance must not be left
        // stranded in its stage with every action blocked.
        var executor = ExecutorFor(BooleanStage);
        var instance = executor.Create("Case", Values()).Instance!;

        var blocked = executor.Invoke(instance, "Clear");

        await Assert.That(blocked.Succeeded).IsFalse();
        await Assert.That(blocked.Error).Contains("policy 'NotCleared'");
        await Assert.That(blocked.Error).Contains("leave stage 'Open' invalid");
        await Assert.That(Stage(instance)).IsEqualTo("Open");
        await Assert.That(instance.Bag["Cleared"] is false).IsTrue();
        // Still usable: the stage's own actions were not stranded.
        var close = executor.Invoke(instance, "Close");
        await Assert.That(close.Succeeded).IsTrue();
        await Assert.That(Stage(instance)).IsEqualTo("Closed");
    }

    [Test]
    public async Task Invoke_FalsifyingNumericStageRequire_FailsAndRollsBack() {
        // #518: the analysed write is inside the range (0..100), so only the stage requirement refuses it.
        var executor = ExecutorFor(NumericStage);
        var instance = executor.Create("X", Values()).Instance!;

        var blocked = executor.Invoke(instance, "Bump");

        await Assert.That(blocked.Succeeded).IsFalse();
        await Assert.That(blocked.Error).Contains("policy 'Small'");
        await Assert.That(instance.Bag["Points"]).IsEqualTo(0L);

        var kept = executor.Invoke(instance, "Ok");
        await Assert.That(kept.Succeeded).IsTrue();
        await Assert.That(instance.Bag["Points"]).IsEqualTo(1L);
    }

    [Test]
    public async Task TransitionOutOfStage_LeavingRequirement_IsAllowed() {
        // The post-check is stage-guarded: 'Close' transitions away, so 'Open's requirement no longer
        // applies to the state it lands in.
        var executor = ExecutorFor("""
            domain D
            Case: entity {
              Cleared: Boolean default(false)
              NotCleared: policy { Cleared is false }
              Open: stage require NotCleared {
                Close: action { assign Cleared to true  transition to Closed }
              }
              Closed: stage {}
            }
            """);
        var instance = executor.Create("Case", Values()).Instance!;

        var close = executor.Invoke(instance, "Close");

        await Assert.That(close.Succeeded).IsTrue();
        await Assert.That(Stage(instance)).IsEqualTo("Closed");
    }

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

    private static string? Stage(ModuleInstance instance) =>
        instance.Bag.TryGetValue("CurrentStage", out var stage) && stage is not null
            ? Convert.ToString(stage, System.Globalization.CultureInfo.InvariantCulture)
            : null;

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

    private static ModuleInstanceExecutor ExecutorFor(string poly) => new(Lower(poly));
}