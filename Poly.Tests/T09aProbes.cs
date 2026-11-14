using Poly.Tests.TestHelpers;

namespace Poly.Tests.Scratch;

/// <summary>T-09a probes: scaffolding, deleted in the last commit.</summary>
public class T09aProbes {
    private const string OutDir = "/private/tmp/claude-501/-Users-scoizzle--t3-worktrees-Poly-t3-6253440c-f801197d7d69f3ef/5f6ad075-6ad2-4157-9d47-2c167ab7e5f1/scratchpad/";
    private static int _n;

    private static string Doc(string members, string body, string extra = "") => $$"""
        domain Prb
        uses temporal
        PrbMood: enum { Happy, Sad }
        PrbOther: enum { Calm, Angry }
        PrbInv: entity {
          Qty: Number default(3)
          Dec: Decimal default(2.5)
          Opt: Number optional
          OptDec: Decimal optional
          Tag: Text default("t")
          Flag: Boolean default(true)
          When: Date
          Stamp: DateTime
        {{members}}
          Run: action { {{body}} }
        }
        {{extra}}
        """;

    private static readonly SimStep[] Steps = [
        SimStep.Create("PrbInv", """{"When":"2026-01-02","Stamp":"2026-01-02T00:00:00"}""", "o"),
        SimStep.Invoke("o", "Run"),
    ];

    private static string Cell(string dsl) {
        var verdict = StrictGateCensus.Verdict(dsl);
        if (verdict != "ok") {
            var first = Poly.Tests.Docs.GuideBlocks.Analyze(dsl).Diagnostics.FirstOrDefault(d => d.IsError)?.Message;
            var hit = Poly.Tests.Docs.GuideBlocks.Analyze(dsl).Diagnostics.FirstOrDefault(d => d.IsError);
            return $"analysis={verdict} {Trim(first)} @{hit}";
        }
        var gate = GateParity.Check(dsl, Steps);
        var text = $"sim={gate.Simulate}/{Trim(gate.SimulateSummary)} cs={gate.CSharp}/{Trim(gate.CSharpSummary)} polyc={gate.PolycCheck}/{Trim(gate.PolycSummary)}";
        if (gate.Simulate is true && gate.CSharp is true) {
            try {
                var scenario = ParityScenario.FromDsl(dsl, "Prb" + Interlocked.Increment(ref _n));
                var (s, p) = scenario.Run(side => {
                    foreach (var flag in new[] { true, false }) {
                        side.Create("PrbInv", ("When", new DateOnly(2026, 1, 2)), ("Stamp", new DateTime(2026, 1, 2)), ("Flag", flag));
                        side.Invoke("Run");
                    }
                });
                static string St(IReadOnlyList<ParityOutcome> o) => string.Join(",", o.Where(x => x.Step.StartsWith("invoke", StringComparison.Ordinal))
                    .Select(x => x.Success ? (x.State.TryGetValue("T", out var t) ? t : "?") : "FAIL:" + Trim(x.Message)));
                text += $" | simT={St(s)} printedT={St(p)} agree={St(s) == St(p)}";
            }
            catch (Exception e) {
                text += " | scenario threw " + Trim(e.Message);
            }
        }
        return text;
    }

    private static string Trim(string? s) {
        var line = (s ?? "").Split('\n')[0].Trim();
        return line.Length > 110 ? line[..110] : line;
    }

    private static void Write(string name, IEnumerable<string> lines) => File.WriteAllLines(OutDir + name, lines);

    private static string Assign(string t, string expr) => $"assign T to {expr}";

    [Test]
    public async Task P2_P3_P5_P6_P7() {
        var lines = new List<string>();
        void Probe(string label, string members, string body, string extra = "") =>
            lines.Add($"{label} :: {Cell(Doc(members, body, extra))}");

        // P2: Date with DateTime arms
        Probe("P2 Date,DateTime -> T DateTime", "  T: DateTime optional", "assign T to match { Flag -> When, else -> Stamp }");
        Probe("P2 DateTime,Date -> T DateTime", "  T: DateTime optional", "assign T to match { Flag -> Stamp, else -> When }");
        Probe("P2 Date,DateTime -> T Date", "  T: Date optional", "assign T to match { Flag -> When, else -> Stamp }");
        Probe("P2 DateTime,Date -> T Date", "  T: Date optional", "assign T to match { Flag -> Stamp, else -> When }");
        Probe("P2 targetless Date,DateTime == Stamp", "  T: Number default(0)", "if ((match { Flag -> When, else -> Stamp }) == Stamp) { assign T to 1 }");
        // P3: enum member arms with Text
        Probe("P3 Happy,\"x\" -> T Text", "  T: Text default(\"\")", "assign T to match { Flag -> Happy, else -> \"x\" }");
        Probe("P3 \"x\",Happy -> T Text", "  T: Text default(\"\")", "assign T to match { Flag -> \"x\", else -> Happy }");
        Probe("P3 Happy,Tag -> T Text", "  T: Text default(\"\")", "assign T to match { Flag -> Happy, else -> Tag }");
        Probe("P3 Happy,\"Sad\" -> T PrbMood", "  T: PrbMood default(Happy)", "assign T to match { Flag -> Happy, else -> \"Sad\" }");
        Probe("P3 Happy,Tag -> T PrbMood", "  T: PrbMood default(Happy)", "assign T to match { Flag -> Happy, else -> Tag }");
        Probe("P3 targetless Happy,Tag == Tag", "  T: Number default(0)", "if ((match { Flag -> Happy, else -> Tag }) == Tag) { assign T to 1 }");
        Probe("P3 targetless Happy,\"x\" == Tag", "  T: Number default(0)", "if ((match { Flag -> Happy, else -> \"x\" }) == Tag) { assign T to 1 }");
        Probe("P3 Happy,Sad -> T PrbMood (control)", "  T: PrbMood default(Happy)", "assign T to match { Flag -> Happy, else -> Sad }");
        Probe("P3 Happy,Calm -> T PrbMood (two enums)", "  T: PrbMood default(Happy)", "assign T to match { Flag -> Happy, else -> Calm }");
        // P5: nested match with a Decimal inner arm
        Probe("P5 nested inner Decimal/Qty -> T Decimal", "  T: Decimal default(0)", "assign T to match { Flag -> match { Qty > 1 -> 1.5, else -> Qty }, else -> 2 }");
        Probe("P5 nested inner Qty/1.5 outer Qty -> T Decimal", "  T: Decimal default(0)", "assign T to match { Qty > 5 -> Qty, else -> match { Flag -> 1.5, else -> Qty } }");
        Probe("P5 nested Number first then Decimal -> T Number", "  T: Number default(0)", "assign T to match { Flag -> Qty, else -> match { Qty > 1 -> 1.5, else -> 2 } }");
        // P6: call argument / guard operand
        Probe("P6 function arg", "  T: Decimal default(0)", "assign T to Twice(match { Flag -> 1.5, else -> Qty })", "function Twice(v: Decimal): Decimal = v + v");
        Probe("P6 function arg Number param", "  T: Number default(0)", "assign T to Id(match { Flag -> 1.5, else -> Qty })", "function Id(v: Number): Number = v");
        Probe("P6 guard operand", "  T: Number default(0)", "if ((match { Flag -> 1.5, else -> Qty }) > 1) { assign T to 1 }");
        Probe("P6 targetless *2 -> Decimal", "  T: Decimal default(0)", "assign T to (match { Flag -> 1.5, else -> Qty }) * 2");
        Probe("P6 invoke argument", "  T: Decimal default(0)\n  Take: action (x: Decimal) { assign T to x }", "invoke Take(x: match { Flag -> 1.5, else -> Qty })");
        // P7: optional Number arm with Decimal arm
        Probe("P7 Opt,1.5 -> T Decimal optional", "  T: Decimal optional", "assign T to match { Flag -> Opt, else -> 1.5 }");
        Probe("P7 Opt,Dec -> T Decimal optional", "  T: Decimal optional", "assign T to match { Flag -> Opt, else -> Dec }");
        Probe("P7 OptDec,Qty -> T Decimal optional", "  T: Decimal optional", "assign T to match { Flag -> OptDec, else -> Qty }");
        Probe("P7 Opt,Qty -> T Number optional (control)", "  T: Number optional", "assign T to match { Flag -> Opt, else -> Qty }");
        Probe("P7 null,Qty,1.5 -> T Decimal optional", "  T: Decimal optional", "assign T to match { Qty > 9 -> null, Qty > 2 -> Qty, else -> 1.5 }");
        // (a) null arm in an expression position
        Probe("A null,1 == null", "  T: Number default(0)", "if ((match { Flag -> null, else -> 1 }) == null) { assign T to 1 }");
        Probe("A (null,1) + 1 -> T Number", "  T: Number default(0)", "assign T to (match { Flag -> null, else -> 1 }) + 1");
        Probe("A (null,1) + 1 -> T Number optional", "  T: Number optional", "assign T to (match { Flag -> null, else -> 1 }) + 1");
        Probe("A (null,Qty) > 1", "  T: Number default(0)", "if ((match { Flag -> null, else -> Qty }) > 1) { assign T to 1 }");
        Probe("A function arg null,1", "  T: Number default(0)", "assign T to Id(match { Flag -> null, else -> 1 })", "function Id(v: Number optional): Number = 1");
        // (b) division in a match arm
        Probe("B 7/2,1.5 -> T Decimal", "  T: Decimal default(0)", "assign T to match { Flag -> 7 / 2, else -> 1.5 }");
        Write("P2-P7.txt", lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task P9_OptionalArithmetic() {
        var lines = new List<string>();
        void Probe(string label, string members, string body, string extra = "") =>
            lines.Add($"{label} :: {Cell(Doc(members, body, extra))}");
        Probe("P9 Opt + 1 -> T Number optional", "  T: Number optional", "assign T to Opt + 1");
        Probe("P9 Opt + 1 -> T Number", "  T: Number default(0)", "assign T to Opt + 1");
        Probe("P9 Opt > 1", "  T: Number default(0)", "if (Opt > 1) { assign T to 1 }");
        Probe("P9 Opt == 1", "  T: Number default(0)", "if (Opt == 1) { assign T to 1 }");
        Probe("P9 (Opt,1) + 1 -> T Number optional", "  T: Number optional", "assign T to (match { Flag -> Opt, else -> 1 }) + 1");
        Probe("P9 (Opt,1) > 1", "  T: Number default(0)", "if ((match { Flag -> Opt, else -> 1 }) > 1) { assign T to 1 }");
        Probe("P9 (null,Qty) == 3", "  T: Number default(0)", "if ((match { Flag -> null, else -> Qty }) == 3) { assign T to 1 }");
        Probe("P9 (null,Qty) != null", "  T: Number default(0)", "if ((match { Flag -> null, else -> Qty }) != null) { assign T to 1 }");
        Probe("P9 (null,Qty) < 5", "  T: Number default(0)", "if ((match { Flag -> null, else -> Qty }) < 5) { assign T to 1 }");
        Probe("P9 (null,1) -> T Number optional", "  T: Number optional", "assign T to match { Flag -> null, else -> 1 }");
        Probe("P9 (Opt,Qty) -> T Number optional", "  T: Number optional", "assign T to match { Flag -> Opt, else -> Qty }");
        Probe("P9 (Qty,Opt) -> T Number optional", "  T: Number optional", "assign T to match { Flag -> Qty, else -> Opt }");
        Write("P9.txt", lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task P10_DefaultMatch() {
        var lines = new List<string>();
        void Probe(string label, string members, string body, string extra = "") =>
            lines.Add($"{label} :: {Cell(Doc(members, body, extra))}");
        Probe("P10 default match Number", "  D2: Number default(match { true -> 1, else -> 2 })", "assign Qty to Qty");
        Probe("P10 default match Decimal same", "  D2: Decimal default(match { true -> 1.5, else -> 2.5 })", "assign Qty to Qty");
        Probe("P10 default match Decimal mixed", "  D2: Decimal default(match { true -> 1.5, else -> 2 })", "assign Qty to Qty");
        Probe("P10 default plain Decimal", "  D2: Decimal default(1.5)", "assign Qty to Qty");
        Write("P10.txt", lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }

    private static string Expected(string name) =>
        (string)typeof(Poly.Tests.DomainModeling.Analysis.AnalyzerClosingRoundPinTests)
            .GetNestedType("FormGridExpected", System.Reflection.BindingFlags.NonPublic)!
            .GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(null)!;

    [Test]
    public async Task P8_Grids() {
        var lines = new List<string>();
        lines.Add("== FormGrid");
        lines.Add(Poly.Tests.DomainModeling.Analysis.AnalyzerClosingRoundPinTests.GridDifferences(
            Poly.Tests.DomainModeling.Analysis.AnalyzerClosingRoundPinTests.FormGridRows(),
            Poly.Tests.DomainModeling.Analysis.AnalyzerClosingRoundPinTests.MasterPlusFlips("FormGrid", Expected("FormGrid"))));
        lines.Add("== FormGridF3");
        lines.Add(Poly.Tests.DomainModeling.Analysis.AnalyzerClosingRoundPinTests.GridDifferences(
            Poly.Tests.DomainModeling.Analysis.AnalyzerClosingRoundPinTests.FormGridF3Rows(),
            Poly.Tests.DomainModeling.Analysis.AnalyzerClosingRoundPinTests.MasterPlusFlips("FormGridF3", Expected("FormGridF3"))));
        lines.Add("== Dead");
        var expectedDead = (string)typeof(Poly.Tests.DomainModeling.Analysis.StrictGateCensusTests)
            .GetNestedType("Expected", System.Reflection.BindingFlags.NonPublic)!
            .GetField("Dead", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(null)!;
        lines.Add(Poly.Tests.DomainModeling.Analysis.AnalyzerClosingRoundPinTests.GridDifferences(
            StrictGateCensus.DeadRows(),
            Poly.Tests.DomainModeling.Analysis.AnalyzerClosingRoundPinTests.MasterPlusFlips("Dead", expectedDead)));
        Write("P8.txt", lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task P0_Decls() {
        var lines = new List<string>();
        foreach (var decl in new[] { "A: Int32 default(2)", "A: Int32", "A: Float default(1.5)", "A: Float", "A: Double", "A: Int", "A: Int64 default(2)",
            "A: Date", "A: DateTime", "A: Number optional", "A: Decimal default(2.5)", "A: PrbMood default(Happy)", "A: Boolean default(true)", "A: Text default(\"t\")" })
            lines.Add($"{decl} :: {Cell($"domain Prb\nuses temporal\nPrbMood: enum {{ Happy, Sad }}\nPrbInv: entity {{\n  {decl}\n  Z: Number default(0)\n}}")}");
        Write("P0.txt", lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task P4_NumberMixes() {
        var lines = new List<string>();
        (string Expr, string Kind)[] kinds = [("Qty", "Number"), ("Dec", "Decimal"), ("1", "lit1"), ("1.5", "lit1.5")];
        foreach (var (a, ak) in kinds)
            foreach (var (b, bk) in kinds) {
                if (ak == bk) continue;
                var arms = $"match {{ Flag -> {a}, else -> {b} }}";
                foreach (var t in new[] { "Number", "Decimal" })
                    lines.Add($"{ak},{bk} -> assign {t} :: {Cell(Doc($"  T: {t} default(0)", $"assign T to {arms}"))}");
                foreach (var t in new[] { "Number", "Decimal" })
                    lines.Add($"{ak},{bk} -> ({arms})*2 into {t} :: {Cell(Doc($"  T: {t} default(0)", $"assign T to ({arms}) * 2"))}");
            }
        Write("P4.txt", lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }
}