using System.Diagnostics;

using Poly.Compile;
using Poly.Interpretation;
using Poly.Packs.Product;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Probes;

/// <summary>Probe for #823 (X-12): VM compile time doubles with each operand of an and chain.</summary>
public class CompileCostProbes {
    internal static void Log(string line) {
        Console.WriteLine(line);
        if (Environment.GetEnvironmentVariable("X12_LOG") is { Length: > 0 } path)
            File.AppendAllText(path, line + Environment.NewLine);
    }

    private static Node[] BoolParameters(int count) =>
        Enumerable.Range(0, count).Select(i => (Node)new Parameter("p" + i, TypeReference.To<bool>())).ToArray();

    private static Node LeftNestedAnd(Node[] ps) => ps.Skip(1).Aggregate(ps[0], (acc, p) => new And(acc, p));

    private static Node RightNestedAnd(Node[] ps) => ps.Reverse().Skip(1).Aggregate(ps[^1], (acc, p) => new And(p, acc));

    private static Node NestedNot(Node[] ps) => ps.Aggregate((Node)ps[0], (acc, _) => new Not(acc));

    private static (double AnalyzeMs, double CompileMs) Time(Func<Node> build) {
        var node = build();
        var sw = Stopwatch.StartNew();
        var analysis = Interpreter.Analyze(node);
        var analyzeMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        Interpreter.Compile(node, analysis);
        return (analyzeMs, sw.Elapsed.TotalMilliseconds);
    }

    private static double Min(Func<Node> build, int runs = 3) {
        var best = double.MaxValue;
        for (var i = 0; i < runs; i++)
            best = Math.Min(best, Time(build) is var t ? t.AnalyzeMs + t.CompileMs : 0);
        return best;
    }

    [Test]
    public async Task CompileCost_PerPassTelemetry_PrintsAnalyzeBreakdown() {
        Interpreter.Analyze(LeftNestedAnd(BoolParameters(4)));
        foreach (var n in new[] { 12, 14, 16, 18 }) {
            var analysis = Interpreter.Analyze(LeftNestedAnd(BoolParameters(n)));
            var top = analysis.Telemetry.Passes.OrderByDescending(p => p.Elapsed).Take(4)
                .Select(p => $"{p.PassName}={p.Elapsed.TotalMilliseconds:F1}");
            Log($"PASSES n={n} total={analysis.Telemetry.TotalElapsed.TotalMilliseconds:F1} top: {string.Join(", ", top)}");
        }
        var chain = Interpreter.Analyze(LeftNestedAnd(BoolParameters(18)));
        var folding = chain.Telemetry.Passes.Single(p => p.PassName == "ConstantFolding").Elapsed;
        await Assert.That(folding.TotalMilliseconds).IsGreaterThanOrEqualTo(0.8 * chain.Telemetry.TotalElapsed.TotalMilliseconds)
            .Because("the whole cost of an 18-operand left-nested and chain is in the ConstantFolding pass (#823)");
    }

    private static string PolicyDsl(string expression) => $$"""
        domain Probe
        Thing: entity {
          Flag: Boolean
          Ok: policy { {{expression}} }
        }
        """;

    [Test]
    public async Task CompileCost_Dsl_AnalyzeVsProduce() {
        var compiler = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring));
        compiler.Analyze(PolicyDsl("Flag and Flag"));
        foreach (var n in new[] { 8, 12, 14, 16, 18 }) {
            var text = PolicyDsl(string.Join(" and ", Enumerable.Repeat("Flag", n)));
            var sw = Stopwatch.StartNew();
            var analyzed = compiler.Analyze(text);
            var analyzeMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var produced = PolyCompiler.Produce(analyzed.Session!, analyzed.Domain!, analyzed.Analysis!);
            var produceMs = sw.Elapsed.TotalMilliseconds;
            var rightText = PolicyDsl(string.Concat(Enumerable.Repeat("Flag and (", n - 1)) + "Flag" + new string(')', n - 1));
            sw.Restart();
            var rightAnalyzed = compiler.Analyze(rightText);
            var rightProduced = PolyCompiler.Produce(rightAnalyzed.Session!, rightAnalyzed.Domain!, rightAnalyzed.Analysis!);
            var rightMs = sw.Elapsed.TotalMilliseconds;
            Log($"DSLRIGHT n={n} ok={rightProduced.Succeeded} analyze+produce={rightMs:F1}");
            Log($"DSL n={n} analyzeOk={analyzed.Succeeded} analyze={analyzeMs:F1} produce={produceMs:F1} files=[{string.Join(",", produced.Files.Select(f => f.FileName))}] diags={produced.Diagnostics.Count}");
        }
        await Task.CompletedTask;
    }

    public sealed class Link {
        public Link? Next { get; set; }
        public bool Flag { get; set; }
    }

    private static Node[] Params<T>(int count) =>
        Enumerable.Range(0, count).Select(i => (Node)new Parameter("p" + i, TypeReference.To<T>())).ToArray();

    private static Node Fold(Node[] ps, Func<Node, Node, Node> join, bool leftNested) =>
        leftNested ? ps.Skip(1).Aggregate(ps[0], join) : ps.Reverse().Skip(1).Aggregate(ps[^1], (acc, p) => join(p, acc));

    private static IEnumerable<(string Name, Func<int, Node> Build)> Shapes() => [
        ("and-left", n => Fold(Params<bool>(n), (a, b) => new And(a, b), true)),
        ("and-right", n => Fold(Params<bool>(n), (a, b) => new And(a, b), false)),
        ("or-left", n => Fold(Params<bool>(n), (a, b) => new Or(a, b), true)),
        ("mixed-and-or-left", n => { var i = 0; return Fold(Params<bool>(n), (a, b) => ++i % 2 == 0 ? new And(a, b) : new Or(a, b), true); }),
        ("compare-and-left", n => { var ps = Params<int>(n); return Enumerable.Range(1, n - 1).Select(i => (Node)new LessThan(ps[i - 1], ps[i])).Aggregate((a, b) => new And(a, b)); }),
        ("add-int-left", n => Fold(Params<int>(n), (a, b) => new Add(a, b), true)),
        ("add-string-left", n => Fold(Params<string>(n), (a, b) => new Add(a, b), true)),
        ("conditional-nested-else", n => Fold(Params<bool>(n), (a, b) => new Conditional(a, Constant1(), b), false)),
        ("conditional-nested-condition", n => Fold(Params<bool>(n), (a, b) => new Conditional(a, b, new Constant(false)), true)),
        ("conditional-nested-then", n => Fold(Params<bool>(n), (a, b) => new Conditional(b, a, new Constant(false)), true)),
        ("coalesce-left", n => Fold(Params<int?>(n), (a, b) => new Coalesce(a, b), true)),
        ("coalesce-right", n => Fold(Params<int?>(n), (a, b) => new Coalesce(a, b), false)),
        ("member-chain", n => Enumerable.Range(0, n).Aggregate((Node)new Parameter("p", TypeReference.To<Link>()), (acc, _) => new Member(acc, "Next"))),
        ("not-chain", n => Enumerable.Range(0, n).Aggregate(Params<bool>(1)[0], (acc, _) => new Not(acc))),
    ];

    private static Node Constant1() => new Constant(true);

    [Test]
    public async Task CompileCost_Scope_GrowthPerOperand() {
        Time(() => LeftNestedAnd(BoolParameters(4)));
        foreach (var (name, build) in Shapes()) {
            var points = new List<string>();
            double prev = 0;
            foreach (var n in new[] { 12, 14, 16, 18 }) {
                try {
                    var t = Time(() => build(n));
                    var total = t.AnalyzeMs + t.CompileMs;
                    points.Add($"{n}:{total:F1}");
                    prev = total;
                }
                catch (Exception ex) {
                    points.Add($"{n}:THROWS {ex.GetType().Name}");
                }
            }
            Log($"SCOPE {name} total(ms) {string.Join(" ", points)}");
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task CompileCost_Table_PrintsTimings() {
        Time(() => LeftNestedAnd(BoolParameters(4)));
        foreach (var n in new[] { 8, 12, 14, 16, 18 }) {
            var l = Time(() => LeftNestedAnd(BoolParameters(n)));
            var r = Time(() => RightNestedAnd(BoolParameters(n)));
            Log($"TABLE n={n} left analyze={l.AnalyzeMs:F1} compile={l.CompileMs:F1} | right analyze={r.AnalyzeMs:F1} compile={r.CompileMs:F1}");
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task CompileCost_AndChain_GrowsLinearly() {
        Time(() => LeftNestedAnd(BoolParameters(4)));
        var small = Min(() => LeftNestedAnd(BoolParameters(8)));
        var large = Min(() => LeftNestedAnd(BoolParameters(16)));
        Log($"AndChain 8={small:F1} ms, 16={large:F1} ms");
        await Assert.That(large).IsLessThanOrEqualTo(3 * Math.Max(small, 1.0));
    }

    [Test]
    public async Task CompileCost_NestedNot_GrowsLinearly() {
        Time(() => NestedNot(BoolParameters(4)));
        var small = Min(() => NestedNot(BoolParameters(64)));
        var large = Min(() => NestedNot(BoolParameters(128)));
        Log($"NestedNot 64={small:F1} ms, 128={large:F1} ms");
        await Assert.That(large).IsLessThanOrEqualTo(6 * Math.Max(small, 1.0));
    }
}