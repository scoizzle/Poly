using Poly.Analysis;
using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

public class LoopInvariantMotionTests {
    [Test]
    public async Task InvariantMultiply_LiftsAheadOfWhile() {
        var x = new Variable("x");
        var acc = new Variable("acc");
        var i = new Variable("i");
        var square = new Multiply(x, x);
        var loop = new WhileLoop(
            new LessThan(i, new Constant(2L)),
            new Block([
                new Assignment(acc, new Add(acc, square)),
                new Assignment(i, new Add(i, new Constant(1L))),
            ]));
        var block = new Block([
            new Assignment(x, new Constant(3L)),
            new Assignment(acc, new Constant(0L)),
            new Assignment(i, new Constant(0L)),
            loop,
            acc,
        ], [x, acc, i]);

        var analysis = Interpreter.Analyze(block);
        var effective = analysis.GetEffectiveNode(loop);
        await Assert.That(Count<Multiply>(LoopOf(effective, analysis), analysis)).IsEqualTo(0);
        await Assert.That(Count<Multiply>(effective, analysis)).IsEqualTo(1);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(18L);
    }

    [Test]
    public async Task AssignedBinding_StaysInside() {
        var x = new Variable("x");
        var acc = new Variable("acc");
        var i = new Variable("i");
        var loop = new WhileLoop(
            new LessThan(i, new Constant(2L)),
            new Block([
                new Assignment(acc, new Add(acc, new Multiply(x, x))),
                new Assignment(x, new Add(x, new Constant(1L))),
                new Assignment(i, new Add(i, new Constant(1L))),
            ]));
        var block = new Block([
            new Assignment(x, new Constant(3L)),
            new Assignment(acc, new Constant(0L)),
            new Assignment(i, new Constant(0L)),
            loop,
            acc,
        ], [x, acc, i]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<Multiply>(analysis.GetEffectiveNode(loop), analysis)).IsEqualTo(1);
        await Assert.That(CountVariables(analysis.GetEffectiveNode(loop), analysis, "li")).IsEqualTo(0);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(25L);
    }

    [Test]
    public async Task NestedLift_StaysInsideAssigningLoop() {
        var x = new Variable("x");
        var acc = new Variable("acc");
        var i = new Variable("i");
        var j = new Variable("j");
        var square = new Multiply(x, x);
        var inner = new WhileLoop(
            new LessThan(j, new Constant(2L)),
            new Block([
                new Assignment(acc, new Add(acc, square)),
                new Assignment(j, new Add(j, new Constant(1L))),
            ]));
        var outer = new WhileLoop(
            new LessThan(i, new Constant(2L)),
            new Block([
                new Assignment(x, new Add(x, new Constant(1L))),
                new Assignment(j, new Constant(0L)),
                inner,
                new Assignment(i, new Add(i, new Constant(1L))),
            ]));
        var block = new Block([
            new Assignment(x, new Constant(2L)),
            new Assignment(acc, new Constant(0L)),
            new Assignment(i, new Constant(0L)),
            new Assignment(j, new Constant(0L)),
            outer,
            acc,
        ], [x, acc, i, j]);

        var analysis = Interpreter.Analyze(block);
        var effectiveOuter = analysis.GetEffectiveNode(outer);
        // Nothing is lifted above the outer loop: the outer body assigns x.
        await Assert.That(effectiveOuter).IsTypeOf<WhileLoop>();
        // The multiply sits before the inner while, still inside the outer body.
        await Assert.That(Count<Multiply>(effectiveOuter, analysis)).IsEqualTo(1);
        var innerEffective = analysis.GetEffectiveNode(inner);
        await Assert.That(innerEffective).IsTypeOf<Block>();
        await Assert.That(Count<Multiply>(LoopOf(innerEffective, analysis), analysis)).IsEqualTo(0);
        // x: 3 then 4 -> (9 + 9) + (16 + 16) = 50.
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(50L);
    }

    [Test]
    public async Task IfAssignsBinding_DoesNotLift() {
        var x = new Variable("x");
        var acc = new Variable("acc");
        var i = new Variable("i");
        var p = new Variable("p");
        var loop = new WhileLoop(
            new LessThan(i, new Constant(2L)),
            new Block([
                new IfStatement(p, new Assignment(x, new Add(x, new Constant(1L)))),
                new Assignment(acc, new Add(acc, new Multiply(x, x))),
                new Assignment(i, new Add(i, new Constant(1L))),
            ]));
        var block = new Block([
            new Assignment(p, new Constant(true)),
            new Assignment(x, new Constant(3L)),
            new Assignment(acc, new Constant(0L)),
            new Assignment(i, new Constant(0L)),
            loop,
            acc,
        ], [p, x, acc, i]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<Multiply>(analysis.GetEffectiveNode(loop), analysis)).IsEqualTo(1);
        await Assert.That(CountVariables(analysis.GetEffectiveNode(loop), analysis, "li")).IsEqualTo(0);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(41L);
    }

    [Test]
    public async Task NegativeDivisor_IsNotLiftedAheadOfALoopThatDoesNotRun() {
        var x = new Variable("x");
        var acc = new Variable("acc");
        var i = new Variable("i");
        var loop = new WhileLoop(
            new LessThan(i, new Constant(0L)),
            new Assignment(acc, new Add(acc, new Divide(x, new Constant(-1L)))));
        var block = new Block([
            new Assignment(x, new Constant(long.MinValue)),
            new Assignment(acc, new Constant(0L)),
            new Assignment(i, new Constant(0L)),
            loop,
            acc,
        ], [x, acc, i]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<Divide>(analysis.GetEffectiveNode(loop), analysis)).IsEqualTo(1);
        await Assert.That(CountVariables(analysis.GetEffectiveNode(loop), analysis, "li")).IsEqualTo(0);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(0L);
    }

    /// <summary>The wrapper block holds the stores then the loop; the loop is the last node.</summary>
    private static Node LoopOf(Node effective, AnalysisResult analysis) {
        if (effective is Block block) {
            var last = analysis.GetEffectiveNode(block.Nodes[^1]);
            return last;
        }
        return effective;
    }

    private static int Count<T>(Node node, AnalysisResult analysis) where T : Node {
        var effective = analysis.GetEffectiveNode(node);
        var count = effective is T ? 1 : 0;
        foreach (var child in effective.Children) {
            if (child is not null)
                count += Count<T>(child, analysis);
        }
        return count;
    }

    private static int CountVariables(Node node, AnalysisResult analysis, string prefix) {
        var effective = analysis.GetEffectiveNode(node);
        var count = effective is Variable v && v.Name.StartsWith(prefix, StringComparison.Ordinal) ? 1 : 0;
        foreach (var child in effective.Children) {
            if (child is not null)
                count += CountVariables(child, analysis, prefix);
        }
        return count;
    }
}