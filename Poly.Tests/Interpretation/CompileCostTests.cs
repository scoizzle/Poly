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
    private const int ChainLength = 256;
    private const double BoundMs = 30000;

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

    private static Node BuildAst(string shape, int count) => shape switch {
        "and" => AndChain(count),
        "or" => OrChain(count),
        "mixed" => MixedChain(count),
        "comparison" => ComparisonChain(count),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
    };

    private static double Elapsed(Action action) {
        var sw = Stopwatch.StartNew();
        action();
        return sw.Elapsed.TotalMilliseconds;
    }

    // Wall clock is used only as an absolute bound sized for a shared CI runner about 7x slower than a dev machine: a quadratic chain of
    // 256 operands takes tens of milliseconds, an exponential one never finishes. Task.Run lets [Timeout] fail a
    // regression instead of hanging the suite.
    private static async Task AssertFinishesWithinBound(string label, Func<double> measure, CancellationToken cancellationToken) {
        var ms = await Task.Run(measure, cancellationToken);
        await Assert.That(ms).IsLessThanOrEqualTo(BoundMs)
            .Because($"{label}: a {ChainLength}-operand chain took {ms:F0} ms (#823)");
    }

    [Test, Timeout(120_000)]
    [Arguments("and")]
    [Arguments("or")]
    [Arguments("mixed")]
    [Arguments("comparison")]
    public async Task Analyze_LeftNestedChainOnBareAst_FinishesWithinTheBound(string shape, CancellationToken cancellationToken) {
        var node = BuildAst(shape, ChainLength);
        await AssertFinishesWithinBound("analyze " + shape, () => Elapsed(() => Interpreter.Analyze(node)), cancellationToken);
    }

    [Test, Timeout(120_000)]
    [Arguments("and")]
    [Arguments("or")]
    [Arguments("mixed")]
    [Arguments("comparison")]
    public async Task Compile_LeftNestedChainOnBareAst_FinishesWithinTheBound(string shape, CancellationToken cancellationToken) {
        var node = BuildAst(shape, ChainLength);
        await AssertFinishesWithinBound("compile " + shape, () => Elapsed(() => Interpreter.Compile(node, Interpreter.Analyze(node))), cancellationToken);
    }

    private static string PolicyDsl(string expression) => $$"""
        domain Probe
        Thing: entity {
          Flag: Boolean
          Count: Number
          Ok: policy { {{expression}} }
        }
        """;

    // Each operator switch needs a parenthesis, and the parser rejects nesting near 256; 100 operands is still far beyond an exponential chain.
    private const int ParenthesisedChainLength = 100;

    private static string DslExpression(string shape, int count) => shape switch {
        "and" => string.Join(" and ", Enumerable.Repeat("Flag", count)),
        "or" => string.Join(" or ", Enumerable.Repeat("Flag", count)),
        "mixed" => Enumerable.Range(1, Math.Min(count, ParenthesisedChainLength) - 1).Aggregate("Flag", (acc, i) => $"({acc} {(i % 2 == 1 ? "or" : "and")} Flag)"),
        "comparison" => string.Join(" and ", Enumerable.Range(0, count).Select(i => $"Count > {i}")),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
    };

    [Test, Timeout(120_000)]
    [Arguments("and")]
    [Arguments("or")]
    [Arguments("mixed")]
    [Arguments("comparison")]
    public async Task AnalyzeAndProduce_LeftNestedChainThroughDsl_FinishesWithinTheBound(string shape, CancellationToken cancellationToken) {
        var compiler = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring));
        var text = PolicyDsl(DslExpression(shape, ChainLength));
        await AssertFinishesWithinBound("dsl " + shape, () => {
            var sw = Stopwatch.StartNew();
            var analyzed = compiler.Analyze(text);
            PolyCompiler.Produce(analyzed.Session!, analyzed.Domain!, analyzed.Analysis!);
            return sw.Elapsed.TotalMilliseconds;
        }, cancellationToken);
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

    [Test]
    public async Task LambdaAnd_FalseBoundLeftWithAnUnboundOuterParameter_FoldsToFalse() {
        var p = new Parameter("p", TypeReference.To<bool>());
        var outer = Flag("outer");
        var ast = new Invoke(new Lambda([p], new And(p, outer)), Wrap(false));
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsTrue();
        await Assert.That(result.GetConstantValue(ast) is false).IsTrue();
    }

    [Test]
    public async Task NonConstantAndTrue_IsNotFoldedOrSimplified() {
        var ast = new And(Flag(), Wrap(true));
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsFalse();
        await Assert.That(result.GetNodeReplacement(ast)).IsNull();
    }

    [Test]
    public async Task NonConstantOrFalse_IsNotFoldedOrSimplified() {
        var ast = new Or(Flag(), Wrap(false));
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsFalse();
        await Assert.That(result.GetNodeReplacement(ast)).IsNull();
    }

    [Test]
    public async Task ZeroAnd_NonConstant_IsNotFoldedOrSimplified() {
        var ast = new And(Wrap(0), Flag());
        var result = Fold(ast);
        await Assert.That(result.IsConstant(ast)).IsFalse();
        await Assert.That(result.GetNodeReplacement(ast)).IsNull();
    }
}