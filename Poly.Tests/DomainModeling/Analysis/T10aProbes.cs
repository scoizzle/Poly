using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>T-10a probe scaffolding. Deleted in the last commit.</summary>
public class T10aProbes {
    private const string Out = "/private/tmp/claude-501/-Users-scoizzle--t3-worktrees-Poly-t3-55090160-3a371982132ae91d/6d298fdc-b4e5-4d89-ae5b-227e4c7e0cc9/scratchpad/probe.txt";

    private static string Doc(string members) => $$"""
        domain T
        uses temporal
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
            ("g1 Due + n Days param", "Go: action (n: Number) { assign OutD to Due + n Days }", null, """{"n":2}""", false),
            ("g2 Due + A Days prop", "Go: action { assign OutD to Due + A Days }", null, null, false),
            ("l1 Today + 1", "Go: action { assign OutD to Today + 1 }", null, null, false),
            ("l2 Now + 1", "Go: action { assign OutT to Now + 1 }", null, null, false),
            ("l3 Today - 1", "Go: action { assign OutD to Today - 1 }", null, null, false),
            ("l4 (Due + 1) + 1", "Go: action { assign OutD to (Due + 1) + 1 }", null, null, false),
            ("l5 Due + 1 + 1", "Go: action { assign OutD to Due + 1 + 1 }", null, null, false),
            ("l6 Today + 3 Days", "Go: action { assign OutD to Today + 3 Days }", null, null, false),
            ("l7 Now + 3 Hours", "Go: action { assign OutT to Now + 3 Hours }", null, null, false),
            ("l8 part Due + 3 Days", "Go: action { assign OutD to part Due + 3 Days }", null, null, false),
            ("l9 (Due + 3 Days) + 2 Days", "Go: action { assign OutD to (Due + 3 Days) + 2 Days }", null, null, false),
            ("l10 Due2 + 1 == Due", "Go: policy { Due + 1 == Due2 }", null, null, true),
            ("l11 Now + 1 Hours > Stamp", "Go: policy { Now + 1 Hours > Stamp }", null, null, true),
            ("o11 Due + OptA null count", "Go: action { assign OutD to Due + OptA }", null, null, false),
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

        var defaults = new[] {
            ("df0 control default(Now)", "Stamp3: DateTime default(Now)"),
            ("df0b control default(1)", "Stamp3: Number default(1)"),
        };
        foreach (var (id, member) in defaults) {
            try {
                var r = GateParity.Check(Doc(member), SimStep.Create("Item", Create, "i"));
                lines.Add($"{id}\n    {r}");
            }
            catch (Exception e) {
                lines.Add($"{id}\n    THREW {e.GetType().Name}: {e.Message.Split('\n')[0]}");
            }
        }

        File.WriteAllLines(Out, lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }
}