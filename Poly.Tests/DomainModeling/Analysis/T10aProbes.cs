using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>T-10a probe scaffolding. Deleted in the last commit.</summary>
public class T10aProbes {
    private const string Out = "/private/tmp/claude-501/-Users-scoizzle--t3-worktrees-Poly-t3-55090160-3a371982132ae91d/6d298fdc-b4e5-4d89-ae5b-227e4c7e0cc9/scratchpad/probe.txt";

    private static string Doc(string members) => $$"""
        domain T
        uses temporal
        function Shift(d: Date, n: Number): Date = d + n
        function ShiftT(d: DateTime, n: Number): DateTime = d + n
        function ShiftD(d: Date, n: Decimal): Date = d + n
        Part: entity {
          Due: Date
          Stamp: DateTime
        }
        Item: entity {
          A: Number
          D: Decimal
          Due: Date
          Due2: Date
          Stamp: DateTime
          Stamp2: DateTime
          Len: Duration
          OptDue: Date optional
          OptStamp: DateTime optional
          OptA: Number optional
          OptD: Decimal optional
          OptLen: Duration optional
          OutD: Date optional
          OutT: DateTime optional
          Gap: Duration optional
          part: Part
          {{members}}
        }
        """;

    private const string Create = """{"A":1,"D":2.5,"Due":"2026-01-01","Due2":"2026-01-05","Stamp":"2026-01-01T00:00:00Z","Stamp2":"2026-01-02T00:00:00Z","Len":"01:00:00","OptDue":"2026-02-01","OptStamp":"2026-02-01T00:00:00Z","OptA":2,"OptD":1.5,"OptLen":"02:00:00"}""";

    [Test]
    public async Task Run() {
        var rows = new List<(string Id, string Members, string? Params, string? Args, bool Evaluate)> {
            ("fn1 Date + Number", "Go: policy { Shift(Due, A) > Due }", null, null, true),
            ("fn2 DateTime + Number", "Go: policy { ShiftT(Stamp, A) > Stamp }", null, null, true),
            ("fn4 Date + Decimal", "Go: policy { ShiftD(Due, D) > Due }", null, null, true),
        };

        var lines = new List<string>();
        foreach (var (id, members, _, args, evaluate) in rows) {
            SimStep[] steps = evaluate
                ? [SimStep.Create("Item", Create, "i"), SimStep.Evaluate("Item", "Go", "i")]
                : [SimStep.Create("Item", id.StartsWith("o11") ? Create.Replace("\"OptA\":2,", "") : Create, "i"), SimStep.Invoke("i", "Go", args)];
            try {
                var r = GateParity.Check(Doc(members), steps);
                lines.Add($"{id}\n    {r}");
            }
            catch (Exception e) {
                lines.Add($"{id}\n    THREW {e.GetType().Name}: {e.Message.Split('\n')[0]}");
            }
        }

        File.WriteAllLines(Out, lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task RoundTripCell() {
        var cell = Poly.Tests.DomainModeling.Parsing.RoundTripMatrixCells.All.First(c => c.Id == "duration/Days");
        var session = Poly.DomainModeling.Compile.DomainSession.ForExtensions([Poly.DomainModeling.Compile.ExtensionCatalog.TemporalId]);
        var built = cell.Build();
        var first = new Poly.DomainModeling.Language.DomainDslPrinter(session).Print(built);
        var changes = new Poly.DomainModeling.Language.PolyDslParser(first, session).Parse();
        var result = new Poly.DomainModeling.Evolution.DomainEvolution(new Poly.DomainModeling.Ontology.Domain("_", [])).Apply(changes, session: session);
        var second = new Poly.DomainModeling.Language.DomainDslPrinter(session).Print(result.Root);
        File.WriteAllLines(Out + ".rt", [first, "-----", second, "-----", Poly.Tests.DomainModeling.Parsing.DomainGraphCanon.Of(built), "-----", Poly.Tests.DomainModeling.Parsing.DomainGraphCanon.Of(result.Root)]);
        await Assert.That(first).IsNotNull();
    }
}