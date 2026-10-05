using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// A transition nested in an <c>if</c> inside a stage's entry or exit block. Session Emit prints the
/// block as its own <c>OnEntry{Stage}</c>/<c>OnExit{Stage}</c> method; that C# must compile and run
/// the same as the simulator.
/// </summary>
public class EntryExitTransitionEmitTests {
    const string EntryDsl = """
        domain Gate
        Door: entity {
          Tag: Text
          Open: Boolean default(true)
          A: stage { Go: action { transition to B } }
          B: stage { entry { if (Open is true) { transition to C } } }
          C: stage { }
        }
        """;

    [Test]
    public async Task Emit_WhenEntryTransitionsInsideIf_Compiles() {
        var (_, _, asm) = EmitAndCompile(EntryDsl, "EmitNestedEntry");
        await Assert.That(asm).IsNotNull();
    }

    [Test]
    public async Task Invoke_WhenEntryTransitionsInsideIf_SimulateAndPrintedAgree() {
        var (domain, _, asm) = EmitAndCompile(EntryDsl, "EmitNestedEntryParity");
        var simulate = new SimulateSide(domain);
        var printed = new PrintedSide(domain, asm);
        foreach (var side in new ParitySide[] { simulate, printed }) {
            side.Create("Door", ("Tag", "t"));
            side.Invoke("Go");
        }
        await Assert.That(ParityScenario.Differences(simulate.Outcomes, printed.Outcomes)).IsEmpty();
        await Assert.That(simulate.Outcomes[1].State["Stage"]).IsEqualTo("C");
    }

    const string WatchedDsl = EntryDsl + """

        Watcher: entity {
          Seen: Text
          Tracks: Door
          W: stage { when Tracks C { assign Seen to "yes" } }
        }
        """;

    // A watcher on the stage the nested transition reaches is notified on both sides.
    [Test]
    public async Task Invoke_WhenEntryTransitionInsideIfReachesAWatchedStage_BothSidesNotify() {
        var (domain, _, asm) = EmitAndCompile(WatchedDsl, "EmitNestedWatched");
        var simulate = new SimulateSide(domain);
        var printed = new PrintedSide(domain, asm);
        foreach (var side in new ParitySide[] { simulate, printed }) {
            var door = side.Create("Door", ("Tag", "t"));
            var watcher = side.Create("Watcher", ("Seen", "no"), ("Tracks", door));
            side.Use(door);
            side.Invoke("Go");
            side.Use(watcher);
        }
        await Assert.That(ParityScenario.Differences(simulate.Outcomes, printed.Outcomes)).IsEmpty();
        await Assert.That(simulate.Outcomes[4].State["Seen"]).IsEqualTo("yes");
    }

    static (Domain Domain, AnalysisResult Analysis, System.Reflection.Assembly Assembly) EmitAndCompile(string poly, string assemblyName) {
        var session = DomainSession.ForSource(poly, seed: [], catalog: ExtensionCatalog.Core);
        var outcome = new DomainEvolution(new Domain("_", [])).Apply(new PolyDslParser(poly, session).Parse(), session: session);
        if (!outcome.Succeeded)
            throw new InvalidOperationException(string.Join("; ", outcome.Analysis.Diagnostics.Select(d => d.Message)));
        var domain = outcome.Root;
        session = session.WithDomain(domain);
        var analysis = session.Analyze(domain);
        var files = session.Emit(domain, analysis);
        // One compilation unit: each file's usings move to the top.
        var sources = files.Select(f => f.Source.Split('\n')).ToList();
        var usings = sources.SelectMany(l => l).Where(l => l.StartsWith("using ", StringComparison.Ordinal)).Distinct();
        var bodies = sources.Select(l => string.Join('\n', l.Where(x => !x.StartsWith("using ", StringComparison.Ordinal))));
        return (domain, analysis, ExportedCSharp.CompileAndLoad(string.Join('\n', usings.Concat(bodies)), assemblyName));
    }
}
