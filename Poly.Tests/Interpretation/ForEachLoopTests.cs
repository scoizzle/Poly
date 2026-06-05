using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Tests.TestHelpers;

using Expr = System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

public class ForEachLoopTests {
    private static readonly int[] Value = [1, 2, 3];

    [Test]
    public async Task AnalyzeNode_ForEachLoop_ResolvesLoopVariableTypeFromArrayElementType() {
        var loopVariable = new Variable("item");
        var node = new ForEachLoop(loopVariable, new Constant(Value), loopVariable);

        var analysis = node.AnalyzeNode();
        var resolvedType = analysis.GetResolvedType(loopVariable);

        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task ForEachLoop_UsesLoopVariableInsideBody() {
        var sum = new Variable("sum");
        var item = new Variable("item");
        var node = new Block(
            [
                new Assignment(sum, new Constant(0)),
                new ForEachLoop(
                    item,
                    new Constant(new[] { 1, 2, 3, 4 }),
                    new Assignment(sum, new Add(sum, item))),
                sum
            ],
            [sum]
        );

        var expr = node.BuildExpression();
        var result = Expr.Lambda<Func<int>>(expr).Compile()();

        await Assert.That(result).IsEqualTo(10);
    }

    [Test]
    public async Task ForEachLoop_EmptyCollection_DoesNotExecuteBody() {
        var sum = new Variable("sum");
        var item = new Variable("item");
        var node = new Block(
            [
                new Assignment(sum, new Constant(5)),
                new ForEachLoop(
                    item,
                    new Constant(Array.Empty<int>()),
                    new Assignment(sum, new Add(sum, item))),
                sum
            ],
            [sum]
        );

        var expr = node.BuildExpression();
        var result = Expr.Lambda<Func<int>>(expr).Compile()();

        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task ForEachLoop_ShadowsOuterVariable_WithoutLeakingValue() {
        var outerItem = new Variable("item");
        var loopItem = new Variable("item");
        var lastSeen = new Variable("lastSeen");
        var node = new Block(
            [
                new Assignment(outerItem, new Constant(100)),
                new Assignment(lastSeen, new Constant(0)),
                new ForEachLoop(
                    loopItem,
                    new Constant(new[] { 1, 2, 3 }),
                    new Assignment(lastSeen, loopItem)),
                new Add(outerItem, lastSeen)
            ],
            [outerItem, lastSeen]
        );

        var expr = node.BuildExpression();
        var result = Expr.Lambda<Func<int>>(expr).Compile()();

        await Assert.That(result).IsEqualTo(103);
    }

    [Test]
    public async Task ForEachLoop_LoopVariableDoesNotLeakOutsideLoopScope() {
        var loopItem = new Variable("item");
        var node = new Block(
            new ForEachLoop(loopItem, new Constant(new[] { 1 }), new Constant(0)),
            loopItem
        );

        var expr = node.BuildExpression();

        await Assert.That(() => Expr.Lambda<Func<int>>(expr).Compile())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ForEachLoop_Continue_SkipsRemainingBodyForCurrentIteration() {
        var sum = new Variable("sum");
        var item = new Variable("item");
        var node = new Block(
            [
                new Assignment(sum, new Constant(0)),
                new ForEachLoop(
                    item,
                    new Constant(new[] { 1, 2, 3, 4 }),
                    new Block(
                        new IfStatement(
                            new Equal(new Modulo(item, new Constant(2)), new Constant(0)),
                            new ContinueStatement()),
                        new Assignment(sum, new Add(sum, item)))),
                sum
            ],
            [sum]
        );

        var expr = node.BuildExpression();
        var result = Expr.Lambda<Func<int>>(expr).Compile()();

        await Assert.That(result).IsEqualTo(4);
    }

    [Test]
    public async Task ForEachLoop_Break_StopsIterating() {
        var sum = new Variable("sum");
        var item = new Variable("item");
        var node = new Block(
            [
                new Assignment(sum, new Constant(0)),
                new ForEachLoop(
                    item,
                    new Constant(new[] { 1, 2, 3, 4, 5 }),
                    new Block(
                        new IfStatement(
                            new GreaterThan(item, new Constant(3)),
                            new BreakStatement()),
                        new Assignment(sum, new Add(sum, item)))),
                sum
            ],
            [sum]
        );

        var expr = node.BuildExpression();
        var result = Expr.Lambda<Func<int>>(expr).Compile()();

        await Assert.That(result).IsEqualTo(6);
    }
}