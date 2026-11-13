using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
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

    // S-13a: Door states the explicit inverse of Watcher.Tracks; EntryDsl itself stays as the other tests use it.
    static readonly string WatchedDsl = EntryDsl.Replace("  Tag: Text\n", "  Tag: Text\n  watchers: many Watcher via Tracks\n") + """

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
        var outcome = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(new PolyDslParser(poly, session).Parse(), session: session);
        if (!outcome.Succeeded)
            throw new InvalidOperationException(string.Join("; ", outcome.Analysis.Diagnostics.Select(d => d.Message)));
        var domain = outcome.Root;
        session = session.WithDomain(domain);
        var analysis = session.Analyze(domain);
        var files = session.Emit(domain, analysis);
        return (domain, analysis, ExportedCSharp.CompileAndLoad(files, assemblyName));
    }
}