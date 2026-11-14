using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>T-10c probe scaffolding. Deleted in the last commit.</summary>
public class T10cProbes {
    private const string Out = "/private/tmp/claude-501/-Users-scoizzle--t3-worktrees-Poly-t3-55090160-3a371982132ae91d/6d298fdc-b4e5-4d89-ae5b-227e4c7e0cc9/scratchpad/probe-t10c.txt";

    private static string Doc(string policy) => $$"""
        domain T
        uses temporal
        Item: entity {
          Due: Date
          Due2: Date optional
          OptDue: Date optional
          Stamp: DateTime
          OptStamp: DateTime optional
          OptStamp2: DateTime optional
          Opens: Time
          OptOpens: Time optional
          OptOpens2: Time optional
          Len: Duration
          OptLen: Duration optional
          OptLen2: Duration optional
          N: Number
          OptN: Number optional
          OptN2: Number optional
          {{policy}}
        }
        """;

    private const string Req = """{"Due":"2026-01-05","Stamp":"2026-01-05T00:00:00Z","Opens":"09:00:00","Len":"01:00:00","N":5""";

    private static string Props(string present, bool isPresent) {
        if (!isPresent) return Req + "}";
        return Req + "," + present + "}";
    }

    [Test]
    public async Task Run() {
        var present = new Dictionary<string, string> {
            ["OptDue"] = "\"OptDue\":\"2026-01-03\",\"Due2\":\"2026-01-04\"",
            ["OptStamp"] = "\"OptStamp\":\"2026-01-03T00:00:00Z\",\"OptStamp2\":\"2026-01-04T00:00:00Z\"",
            ["OptOpens"] = "\"OptOpens\":\"08:00:00\",\"OptOpens2\":\"07:00:00\"",
            ["OptLen"] = "\"OptLen\":\"00:30:00\"",
            ["OptN"] = "\"OptN\":3",
        };
        var cells = new List<(string Id, string Opt, string Expr)>();
        var ops = new[] { "<", "<=", ">", ">=", "==", "!=" };
        void Add(string optName, string optGroup, (string Tag, string Rhs)[] rhs) {
            foreach (var op in ops)
                foreach (var (tag, r) in rhs) {
                    cells.Add(($"{optName} {op} {tag}", optGroup, $"{optName} {op} {r}"));
                    if (tag == "required") cells.Add(($"{tag} {op} {optName}", optGroup, $"{r} {op} {optName}"));
                }
        }
        Add("OptDue", "OptDue", [("required", "Due"), ("optional", "Due2"), ("clock", "Today"), ("offset", "Due + 3 Days")]);
        Add("OptStamp", "OptStamp", [("required", "Stamp"), ("optional", "OptStamp2"), ("clock", "Now"), ("offset", "Stamp + 3 Hours")]);
        Add("OptOpens", "OptOpens", [("required", "Opens"), ("optional", "OptOpens2"), ("offset", "Opens + 2 Hours")]);
        Add("OptLen", "OptLen", [("required", "Len")]);
        Add("OptN", "OptN", [("required", "N")]);
        // guards
        cells.Add(("guard and", "OptDue", "OptDue is not null and OptDue > Due"));
        cells.Add(("guard and 2", "OptDue", "OptDue != null and OptDue > Due"));
        cells.Add(("null eq", "OptDue", "OptDue == null"));
        cells.Add(("null ne", "OptDue", "OptDue != null"));

        var lines = new List<string>();
        foreach (var (id, opt, expr) in cells) {
            var doc = Doc($"P: policy {{ {expr} }}");
            var sb = new System.Text.StringBuilder(id.PadRight(34));
            try {
                var res = new List<string>();
                string analysis = "?";
                foreach (var isPresent in new[] { false, true }) {
                    var r = GateParity.Check(doc, SimStep.Create("Item", Props(present[opt], isPresent), "i"), SimStep.Evaluate("Item", "P", "i"));
                    analysis = r.Analysis ? "A" : "R";
                    if (!r.Analysis) { res.Add(r.AnalysisSummary.Split(';')[0]); break; }
                    res.Add($"{(isPresent ? "present" : "absent")}: sim={(r.Simulate is true ? "ok" : "FAIL")} cs={(r.CSharp is true ? "ok" : "FAIL")} pc={(r.PolycCheck is true ? "ok" : "FAIL")}");
                }
                sb.Append(analysis).Append(" | ").Append(string.Join(" | ", res));
            }
            catch (Exception e) { sb.Append("THREW ").Append(e.GetType().Name).Append(": ").Append(e.Message.Split('\n')[0]); }
            lines.Add(sb.ToString());
        }
        File.WriteAllLines(Out, lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Values() {
        var lines = new List<string>();
        var kinds = new (string Opt, string Opt2, object A, object B)[] {
            ("OptDue", "Due2", new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4)),
            ("OptStamp", "OptStamp2", new DateTime(2026, 1, 3), new DateTime(2026, 1, 4)),
            ("OptOpens", "OptOpens2", new TimeOnly(8, 0), new TimeOnly(7, 0)),
            ("OptLen", "OptLen2", TimeSpan.FromHours(2), TimeSpan.FromHours(3)),
            ("OptN", "OptN2", 3L, 4L),
        };
        foreach (var (opt, opt2, a, b) in kinds)
            foreach (var op in new[] { "<", "<=", ">", ">=", "==", "!=" }) {
                var combos = new (string Name, bool L, bool R)[] { ("none", false, false), ("left", true, false), ("right", false, true), ("both", true, true) };
                var results = new List<string>();
                foreach (var (name, l, r) in combos) {
                    try {
                        var outs = await ParityScenario.FromDsl(Doc($"P: policy {{ {opt} {op} {opt2} }}"), "T10cV" + opt + op.GetHashCode() + name).AssertAgree(side => {
                            var props = new List<(string, object?)> { ("Due", new DateOnly(2026, 1, 5)), ("Stamp", new DateTime(2026, 1, 5)), ("Opens", new TimeOnly(9, 0)), ("Len", TimeSpan.FromHours(1)), ("N", 5L) };
                            if (l) props.Add((opt, a));
                            if (r) props.Add((opt2, b));
                            side.Create("Item", [.. props]);
                            side.EvaluatePolicy("P");
                        });
                        results.Add($"{name}={outs[1].State["P"]}");
                    }
                    catch (Exception e) { results.Add($"{name}=THREW {e.GetType().Name}: {e.Message.Split('\n')[0]}"); }
                }
                lines.Add($"{opt} {op} {opt2}: {string.Join(" ", results)}");
            }
        File.WriteAllLines(Out + ".values", lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }
}