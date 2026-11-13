using System.Diagnostics;

using Poly.Compile;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Packs.Product;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Issue #823 (X-12): constant folding evaluated the left operand of a logical node twice, so analysis time
/// doubled with each operand of a left-nested <c>and</c>/<c>or</c> chain. The scaling tests allow a quadratic
/// algorithm and forbid an exponential one; the semantics tests pin what a logical node folds and simplifies to.
/// </summary>
public class CompileCostTests {
    private static readonly int[] ChainSizes = [16, 32, 64, 128, 256];

    private static Node[] BoolParameters(int count) =>
        Enumerable.Range(0, count).Select(i => (Node)new Parameter("p" + i, TypeReference.To<bool>())).ToArray();

    private static Node[] IntParameters(int count) =>
        Enumerable.Range(0, count).Select(i => (Node)new Parameter("n" + i, TypeReference.To<int>())).ToArray();

    private static Node AndChain(int count) => BoolParameters(count).Aggregate((acc, p) => new And(acc, p));

    private static Node OrChain(int count) => BoolParameters(count).Aggregate((acc, p) => new Or(acc, p));

    private static Node MixedChain(int count) {
        var joinWithAnd = false;
        return BoolParameters(count).Aggregate((acc, p) => (joinWithAnd = !joinWithAnd) ? new Or(acc, p) : new And(acc, p));
    }

    private static Node ComparisonChain(int count) {
        var ps = IntParameters(count);
        return Enumerable.Range(1, count - 1).Select(i => (Node)new LessThan(ps[i - 1], ps[i])).Aggregate((acc, c) => new And(acc, c));
    }

    private static readonly (string Name, Func<int, Node> Build)[] AstShapes = [
        ("and", AndChain),
        ("or", OrChain),
        ("mixed", MixedChain),
        ("comparison", ComparisonChain),
    ];

    private static double BestOfThreeMs(Func<double> run) => Enumerable.Range(0, 3).Min(_ => run());

    private static double Elapsed(Action action) {
        var sw = Stopwatch.StartNew();
        action();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static async Task AssertScalesSubExponentially(string shape, Func<int, double> measure) {
        var times = new Dictionary<int, double>();
        foreach (var size in ChainSizes) {
            measure(size);
            times[size] = BestOfThreeMs(() => measure(size));
        }

        foreach (var (small, large) in ChainSizes.Zip(ChainSizes.Skip(1))) {
            await Assert.That(times[large]).IsLessThanOrEqualTo(6 * Math.Max(times[small], 2.0))
                .Because($"{shape}: doubling the chain from {small} to {large} operands took {times[small]:F2} ms -> {times[large]:F2} ms (#823)");
        }

        await Assert.That(times[ChainSizes[^1]]).IsLessThanOrEqualTo(5000.0)
            .Because($"{shape}: a {ChainSizes[^1]}-operand chain took {times[ChainSizes[^1]]:F0} ms (#823)");
    }

    [Test]
    public async Task Analyze_LeftNestedLogicalChainsOnBareAsts_GrowAtMostQuadratically() {
        foreach (var (name, build) in AstShapes) {
            await AssertScalesSubExponentially("ast " + name, size => {
                var node = build(size);
                return Elapsed(() => Interpreter.Analyze(node));
            });
        }
    }

    [Test]
    public async Task Compile_LeftNestedLogicalChainsOnBareAsts_GrowAtMostQuadratically() {
        foreach (var (name, build) in AstShapes) {
            await AssertScalesSubExponentially("compile " + name, size => {
                var node = build(size);
                return Elapsed(() => Interpreter.Compile(node, Interpreter.Analyze(node)));
            });
        }
    }

    private static string PolicyDsl(string expression) => $$"""
        domain Probe
        Thing: entity {
          Flag: Boolean
          Count: Number
          Ok: policy { {{expression}} }
        }
        """;

    private static readonly (string Name, Func<int, string> Build)[] DslShapes = [
        ("and", n => string.Join(" and ", Enumerable.Repeat("Flag", n))),
        ("or", n => string.Join(" or ", Enumerable.Repeat("Flag", n))),
        ("mixed", n => string.Join(" ", Enumerable.Range(0, n).Select(i => i == 0 ? "Flag" : (i % 2 == 1 ? "or Flag" : "and Flag")))),
        ("comparison", n => string.Join(" and ", Enumerable.Range(0, n).Select(i => $"Count > {i}"))),
    ];

    [Test]
    public async Task Analyze_LeftNestedLogicalChainsThroughDsl_GrowAtMostQuadratically() {
        var compiler = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring));
        foreach (var (name, build) in DslShapes) {
            await AssertScalesSubExponentially("dsl " + name, size => {
                var text = PolicyDsl(build(size));
                var sw = Stopwatch.StartNew();
                var analyzed = compiler.Analyze(text);
                PolyCompiler.Produce(analyzed.Session!, analyzed.Domain!, analyzed.Analysis!);
                return sw.Elapsed.TotalMilliseconds;
            });
        }
    }

    private static AnalysisResult Fold(Node ast) => Interpreter.Analyze(ast);

    private static Node Flag(string name = "x") => new Parameter(name, TypeReference.To<bool>());

    private static Node Wrap(object? value) => new Constant(value);

    [Test]
    public async Task FalseAnd_NonConstant_FoldsToFalse() {
        var ast = new And(Wrap(false), Flag());
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast) is false).IsTrue();
    }

    [Test]
    public async Task TrueOr_NonConstant_FoldsToTrue() {
        var ast = new Or(Wrap(true), Flag());
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast) is true).IsTrue();
    }

    [Test]
    public async Task TrueAnd_NonConstant_SimplifiesToTheOperand() {
        var x = Flag();
        var ast = new And(Wrap(true), x);
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsFalse();
        await Assert.That(result.GetNodeReplacement(ast)).IsSameReferenceAs(x);
    }

    [Test]
    public async Task FalseOr_NonConstant_SimplifiesToTheOperand() {
        var x = Flag();
        var ast = new Or(Wrap(false), x);
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsFalse();
        await Assert.That(result.GetNodeReplacement(ast)).IsSameReferenceAs(x);
    }

    [Test]
    public async Task NonConstantAndFalse_IsNotFoldedOrSimplified() {
        var ast = new And(Flag(), Wrap(false));
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsFalse();
        await Assert.That(result.GetNodeReplacement(ast)).IsNull();
    }

    [Test]
    public async Task NonConstantOrTrue_IsNotFoldedOrSimplified() {
        var ast = new Or(Flag(), Wrap(true));
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsFalse();
        await Assert.That(result.GetNodeReplacement(ast)).IsNull();
    }

    [Test]
    public async Task NestedAnd_FalseDeepInTheLeftSpine_FoldsTheWholeChain() {
        var ast = new And(new And(new And(Wrap(false), Flag("a")), Flag("b")), Flag("c"));
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast) is false).IsTrue();
    }

    [Test]
    public async Task NestedOr_TrueDeepInTheLeftSpine_FoldsTheWholeChain() {
        var ast = new Or(new Or(new Or(Wrap(true), Flag("a")), Flag("b")), Flag("c"));
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast) is true).IsTrue();
    }

    [Test]
    public async Task NestedMix_TrueAndXOrTrue_FoldsToTrueOnTheLeftOperandOnly() {
        var x = Flag();
        var inner = new And(Wrap(true), x);
        var ast = new Or(inner, Flag("y"));
        var result = Fold(ast);
        await Assert.That(result.GetNodeReplacement(inner)).IsSameReferenceAs(x);
        await Assert.That(result.IsConstant(ast)).IsFalse();
    }

    [Test]
    public async Task NestedMix_FalseAndXOrTrue_FoldsToTrue() {
        var ast = new Or(new And(Wrap(false), Flag()), Wrap(true));
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast) is true).IsTrue();
    }

    [Test]
    public async Task LogicalNodes_OverNonBooleanConstants_DoNotFold() {
        var and = new And(Wrap(1), Wrap(2));
        var or = new Or(Wrap(1), Wrap(2));
        await Assert.That(Fold(and).IsConstant(and)).IsFalse();
        await Assert.That(Fold(or).IsConstant(or)).IsFalse();
    }

    [Test]
    public async Task LambdaAnd_FoldsOnlyWhenTheParametersAreBound() {
        var p = new Parameter("p", TypeReference.To<bool>());
        var q = new Parameter("q", TypeReference.To<bool>());
        var body = new And(p, q);
        var ast = new Invoke(new Lambda([p, q], body), Wrap(true), Wrap(false));
        var result = Fold(ast);
        await Assert.That(result.IsConstant(body)).IsFalse();
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast) is false).IsTrue();
    }

    [Test]
    public async Task LambdaOr_FoldsOnlyWhenTheParametersAreBound() {
        var p = new Parameter("p", TypeReference.To<bool>());
        var q = new Parameter("q", TypeReference.To<bool>());
        var body = new Or(p, q);
        var ast = new Invoke(new Lambda([p, q], body), Wrap(false), Wrap(true));
        var result = Fold(ast);
        await Assert.That(result.IsConstant(body)).IsFalse();
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast) is true).IsTrue();
    }

    [Test]
    public async Task LambdaNestedChain_FoldsOnlyWhenTheParametersAreBound() {
        var p = new Parameter("p", TypeReference.To<bool>());
        var q = new Parameter("q", TypeReference.To<bool>());
        var r = new Parameter("r", TypeReference.To<bool>());
        var body = new And(new Or(p, q), r);
        var ast = new Invoke(new Lambda([p, q, r], body), Wrap(false), Wrap(true), Wrap(true));
        var result = Fold(ast);
        await Assert.That(result.IsConstant(body)).IsFalse();
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast) is true).IsTrue();
    }

    [Test]
    public async Task LambdaAnd_WithANonConstantArgument_DoesNotFold() {
        var p = new Parameter("p", TypeReference.To<bool>());
        var q = new Parameter("q", TypeReference.To<bool>());
        var outer = new Parameter("outer", TypeReference.To<bool>());
        var withFalseFirst = new Invoke(new Lambda([p, q], new And(p, q)), Wrap(false), outer);
        var withTrueFirst = new Invoke(new Lambda([p, q], new And(p, q)), Wrap(true), outer);
        await Assert.That(Fold(withFalseFirst).IsConstant(withFalseFirst)).IsFalse();
        await Assert.That(Fold(withTrueFirst).IsConstant(withTrueFirst)).IsFalse();
    }
}