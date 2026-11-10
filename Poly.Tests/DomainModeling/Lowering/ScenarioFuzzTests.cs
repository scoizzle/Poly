using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #374 S2: seeded step scripts over the stage graph, run through <see cref="ParityScenario"/>. The
/// oracle is the interpreter against the Roslyn-compiled printed C#; the generator states no expectation.
/// A divergence names the script (seed + index) and the differing step and field.
/// </summary>
public class ScenarioFuzzTests {
    const string DeskDsl = """
        domain Desk
        Priority: enum { Low, High }
        Ticket: entity {
          Note: Text length(1, 12) default("open")
          Score: Number range(0, 10) default(5)
          Level: Priority default(Low)
          Flag: Boolean default(false)
          CanClose: policy { Flag is true }
          Open: stage {
            Bump: action {
              assign Score to Score + 1
            }
            Annotate: action (text: Text) {
              assign Note to text
            }
            Mark: action {
              assign Flag to true
            }
            Escalate: action (level: Priority) {
              assign Level to level
              transition to Review
            }
            Close: action
              require CanClose
            {
              assign Note to "closed"
              transition to Closed
            }
          }
          Review: stage {
            Approve: action {
              transition to Closed
            }
            Reject: action {
              transition to Open
            }
          }
          Closed: stage {
            Reopen: action {
              assign Flag to false
              transition to Open
            }
          }
        }
        """;

    // The lowered module is broken, not the interpreter's domain: the printed side loses the guard.
    static readonly string GuardDropped = DeskDsl.Replace("""
              require CanClose
        """, "");

    // ...or loses a transition.
    static readonly string TransitionDropped = DeskDsl.Replace("""
              assign Note to "closed"
              transition to Closed
        """, """
              assign Note to "closed"
        """);

    static readonly Lazy<ParityScenario> Clean = new(() => ParityScenario.FromDsl(DeskDsl, "ScenarioFuzzClean"));
    static readonly Lazy<ParityScenario> NoGuard = new(() => ParityScenario.FromDsls(DeskDsl, GuardDropped, "ScenarioFuzzNoGuard"));
    static readonly Lazy<ParityScenario> NoTransition = new(() => ParityScenario.FromDsls(DeskDsl, TransitionDropped, "ScenarioFuzzNoTransition"));
    static readonly Lazy<Domain> Facts = new(() => EvolvedDomain.FromDsl(DeskDsl).Domain);

    // One run of the default set on the clean scenario, shared by the tests that read it: the module a
    // domain lowers to is bound to that Domain once, so two tests must not drive one scenario at the same time.
    static readonly Lazy<IReadOnlyList<(ScenarioScript Script, IReadOnlyList<ParityOutcome> Simulate, IReadOnlyList<ParityOutcome> Printed)>> CleanRuns = new(() => [.. DefaultSet().Select(script => {
        var (simulate, printed) = Clean.Value.Run(side => ScenarioGenerator.Apply(side, script));
        return (script, simulate, printed);
    })]);

    static IReadOnlyList<ScenarioScript> DefaultSet() => ScenarioGenerator.Generate(Facts.Value, ScenarioGenerator.DefaultSeed);

    static List<string> Differences(ParityScenario scenario, ScenarioScript script) {
        var (simulate, printed) = scenario.Run(side => ScenarioGenerator.Apply(side, script));
        return ParityScenario.Differences(simulate, printed);
    }

    [Test]
    public async Task Sanity_ThePremutatedDslsReallyDiffer() {
        await Assert.That(GuardDropped).IsNotEqualTo(DeskDsl);
        await Assert.That(TransitionDropped).IsNotEqualTo(DeskDsl);
    }

    [Test]
    public async Task Generate_SameSeedTwice_ProducesIdenticalScripts() {
        var first = ScenarioGenerator.Render(DefaultSet());
        var second = ScenarioGenerator.Render(DefaultSet());
        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task Generate_DifferentSeeds_ProduceDifferentScripts() {
        var a = ScenarioGenerator.Render(ScenarioGenerator.Generate(Facts.Value, 1));
        var b = ScenarioGenerator.Render(ScenarioGenerator.Generate(Facts.Value, 2));
        await Assert.That(a).IsNotEqualTo(b);
    }

    [Test]
    public async Task Generate_IsBounded() {
        var set = DefaultSet();
        await Assert.That(set.Count).IsEqualTo(ScenarioGenerator.DefaultScripts);
        // creates (3) + at most DefaultSteps steps
        await Assert.That(set.Max(s => s.Steps.Count)).IsLessThanOrEqualTo(3 + ScenarioGenerator.DefaultSteps);
        Assert.Throws<ArgumentOutOfRangeException>(() => ScenarioGenerator.Generate(Facts.Value, 1, ScenarioGenerator.MaxScripts + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScenarioGenerator.Generate(Facts.Value, 1, 1, ScenarioGenerator.MaxSteps + 1));
    }

    [Test]
    public async Task Generate_ReadsTheStageGraph_NotAFixedList() {
        var actions = DefaultSet().SelectMany(s => s.Steps).Where(s => s.Kind == "invoke").Select(s => s.Name).Distinct().Order().ToList();
        await Assert.That(actions).IsEquivalentTo(["Annotate", "Approve", "Bump", "Close", "Escalate", "Mark", "Reject", "Reopen"]);
    }

    [Test]
    public async Task DefaultSeed_InterpreterAndPrintedCSharp_Agree() {
        var failures = CleanRuns.Value
            .Select(r => (r.Script, diffs: ParityScenario.Differences(r.Simulate, r.Printed)))
            .Where(r => r.diffs.Count > 0)
            .Select(r => $"{r.Script.Text}\n  " + string.Join("\n  ", r.diffs))
            .ToList();

        await Assert.That(failures).IsEmpty().Because(string.Join("\n", failures.Take(3)));
    }

    [Test]
    public async Task DefaultSeed_Run_IsNotVacuous() {
        var outcomes = CleanRuns.Value.SelectMany(r => r.Simulate).ToList();

        var succeeded = outcomes.Count(o => o.Success);
        var failed = outcomes.Count(o => !o.Success);
        var stages = outcomes.Select(o => o.State.TryGetValue("Stage", out var s) ? s : null).Where(s => s is not null).Distinct().ToList();

        // Both legal and illegal steps happen, and the stage graph is actually traversed.
        await Assert.That(succeeded).IsGreaterThan(30).Because($"{succeeded} steps succeeded");
        await Assert.That(failed).IsGreaterThan(10).Because($"{failed} steps failed");
        await Assert.That(stages.Count).IsGreaterThanOrEqualTo(2).Because($"stages seen: {string.Join(",", stages)}");
    }

    [Test]
    public async Task BrokenLoweredGuard_IsCaught_AndNamesTheStep() {
        var caught = DefaultSet()
            .Select(script => (script, diffs: Differences(NoGuard.Value, script)))
            .Where(r => r.diffs.Count > 0)
            .ToList();

        await Assert.That(caught).IsNotEmpty().Because("a lowered module without the CanClose guard must diverge");
        // The differing step is named, and it is a Close invocation.
        await Assert.That(caught.All(r => r.diffs.Any(d => d.StartsWith("invoke Close:", StringComparison.Ordinal)))).IsTrue()
            .Because(string.Join("\n", caught.Take(2).Select(r => r.script.Text + "\n  " + string.Join("\n  ", r.diffs))));
    }

    [Test]
    public async Task BrokenLoweredTransition_IsCaught_AsAStateDifference() {
        var caught = DefaultSet()
            .Select(script => (script, diffs: Differences(NoTransition.Value, script)))
            .Where(r => r.diffs.Count > 0)
            .ToList();

        await Assert.That(caught).IsNotEmpty().Because("a lowered module without the Close transition must diverge");
        await Assert.That(caught.Any(r => r.diffs.Any(d => d.Contains("'Stage' differs", StringComparison.Ordinal)))).IsTrue();
    }
}