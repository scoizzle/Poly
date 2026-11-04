using Poly.Analysis;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

public class AvailableExpressionTests {
    [Test]
    public async Task DuplicateMultiply_OneBlock_CollapsesToOneMultiply() {
        var x = new Variable("x");
        var y = new Variable("y");
        var left = new Multiply(x, y);
        var right = new Multiply(x, y);
        var sum = new Add(left, right);
        var block = new Block([
            new Assignment(x, new Constant(3L)),
            new Assignment(y, new Constant(4L)),
            sum,
        ], [x, y]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<Multiply>(analysis.GetEffectiveNode(sum), analysis)).IsEqualTo(1);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(24L);
    }

    [Test]
    public async Task KillBetweenCopies_StaysTwoMultiplies() {
        var x = new Variable("x");
        var y = new Variable("y");
        var a = new Variable("a");
        var b = new Variable("b");
        var first = new Multiply(x, y);
        var second = new Multiply(x, y);
        var block = new Block([
            new Assignment(x, new Constant(3L)),
            new Assignment(y, new Constant(4L)),
            new Assignment(a, first),
            new Assignment(x, new Constant(1L)),
            new Assignment(b, second),
            new Add(a, b),
        ], [x, y, a, b]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<Multiply>(analysis.GetEffectiveNode(block), analysis)).IsEqualTo(2);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(16L);
    }

    [Test]
    public async Task ShadowedNames_DoNotCollapse() {
        var outer = new Variable("x");
        var inner = new Variable("x");
        var innerSum = new Add(new Multiply(inner, inner), new Multiply(inner, inner));
        var block = new Block([
            new Assignment(outer, new Constant(2L)),
            new Add(new Multiply(outer, outer), new Multiply(outer, outer)),
            new Block([
                new Assignment(inner, new Constant(3L)),
                innerSum,
            ], [inner]),
        ], [outer]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<Multiply>(analysis.GetEffectiveNode(innerSum), analysis)).IsEqualTo(1);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(18L);
    }

    [Test]
    public async Task IdenticalCalls_StayTwoCalls() {
        var n = new Variable("n");
        var f = new Variable("f");
        var first = new Invoke(f);
        var second = new Invoke(f);
        var block = new Block([
            new Assignment(n, new Constant(0L)),
            new Assignment(f, new Lambda([], new Assignment(n, new Add(n, new Constant(1L))))),
            first,
            second,
            n,
        ], [n, f]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<Invoke>(analysis.GetEffectiveNode(block), analysis)).IsEqualTo(2);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(2L);
    }

    [Test]
    public async Task And_DoesNotHoistRightOnlyStore() {
        var x = new Variable("x");
        var y = new Variable("y");
        var flag = new Variable("flag");
        var right = new Add(new Multiply(x, y), new Multiply(x, y));
        var and = new And(flag, right);
        var block = new Block([
            new Assignment(flag, new Constant(false)),
            new Assignment(x, new Constant(3L)),
            new Assignment(y, new Constant(4L)),
            and,
        ], [flag, x, y]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(analysis.GetEffectiveNode(and)).IsTypeOf<And>();
        var effectiveRight = analysis.GetEffectiveNode(right);
        await Assert.That(effectiveRight).IsTypeOf<Block>();
        await Assert.That(Count<Multiply>(effectiveRight, analysis)).IsEqualTo(1);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task ValueBlock_InWhileCondition_RunsStatementsAndYieldsTruth() {
        var n = new Variable("n");
        var seen = new Variable("seen");
        var condition = new Block([
            new Assignment(seen, new Add(seen, new Constant(1L))),
            new LessThan(n, new Constant(2L)),
        ]);
        var block = new Block([
            new Assignment(n, new Constant(0L)),
            new Assignment(seen, new Constant(0L)),
            new WhileLoop(condition, new Assignment(n, new Add(n, new Constant(1L)))),
            seen,
        ], [n, seen]);

        using var exec = Interpreter.Execute(Interpreter.Compile(block));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task WhileTrueEdge_ReusesSquaresUntilBodyKillsThem() {
        var iter = new Variable("iter");
        var n = new Variable("n");
        var zx = new Variable("zx");
        var zy = new Variable("zy");
        var acc = new Variable("acc");
        var condX = new Multiply(zx, zx);
        var condY = new Multiply(zy, zy);
        var useX = new Multiply(zx, zx);
        var useY = new Multiply(zy, zy);
        var condition = new And(
            new LessThan(iter, n),
            new LessThanOrEqual(new Add(condX, condY), new Constant(100L)));
        var loop = new WhileLoop(condition, new Block([
            new Assignment(acc, new Add(acc, new Add(useX, useY))),
            new Assignment(zy, new Add(zy, new Constant(1L))),
            new Assignment(zx, new Add(zx, new Constant(1L))),
            new Assignment(iter, new Add(iter, new Constant(1L))),
        ]));
        var block = new Block([
            new Assignment(iter, new Constant(0L)),
            new Assignment(n, new Constant(2L)),
            new Assignment(zx, new Constant(1L)),
            new Assignment(zy, new Constant(1L)),
            new Assignment(acc, new Constant(0L)),
            loop,
            acc,
        ], [iter, n, zx, zy, acc]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<Multiply>(analysis.GetEffectiveNode(loop), analysis)).IsEqualTo(2);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(10L);
    }

    [Test]
    public async Task ZeroTripLoop_DoesNotEvaluateRightOfAnd() {
        var iter = new Variable("iter");
        var flag = new Variable("flag");
        var x = new Variable("x");
        var right = new Block([
            new Assignment(flag, new Constant(1L)),
            new LessThan(new Multiply(x, x), new Constant(100L)),
        ]);
        var block = new Block([
            new Assignment(iter, new Constant(5L)),
            new Assignment(flag, new Constant(0L)),
            new Assignment(x, new Constant(3L)),
            new WhileLoop(new And(new LessThan(iter, new Constant(5L)), right), new Assignment(iter, new Add(iter, new Constant(1L)))),
            flag,
        ], [iter, flag, x]);

        using var exec = Interpreter.Execute(Interpreter.Compile(block));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task OrTrueLeft_DoesNotReuseRightInBody() {
        var p = new Variable("p");
        var flag = new Variable("flag");
        var x = new Variable("x");
        var bodyUse = new Multiply(x, x);
        var block = new Block([
            new Assignment(p, new Constant(true)),
            new Assignment(flag, new Constant(0L)),
            new Assignment(x, new Constant(3L)),
            new IfStatement(
                new Or(p, new Block([
                    new Assignment(flag, new Constant(1L)),
                    new LessThan(new Multiply(x, x), new Constant(100L)),
                ])),
                new Assignment(flag, new Add(flag, bodyUse))),
            flag,
        ], [p, flag, x]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(analysis.GetEffectiveNode(bodyUse)).IsTypeOf<Multiply>();
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(9L);
    }

    [Test]
    public async Task IfThen_ReusesCondition_ElseDoesNot() {
        var p = new Variable("p");
        var x = new Variable("x");
        var cond = new Multiply(x, x);
        var thenUse = new Multiply(x, x);
        var elseUse = new Multiply(x, x);
        var acc = new Variable("acc");
        var ifStatement = new IfStatement(
            new And(p, new LessThan(cond, new Constant(100L))),
            new Assignment(acc, thenUse),
            new Assignment(acc, elseUse));
        var block = new Block([
            new Assignment(p, new Constant(false)),
            new Assignment(x, new Constant(3L)),
            new Assignment(acc, new Constant(0L)),
            ifStatement,
            acc,
        ], [p, x, acc]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(analysis.GetEffectiveNode(elseUse)).IsTypeOf<Multiply>();
        var effectiveIf = (IfStatement)analysis.GetEffectiveNode(ifStatement);
        await Assert.That(CountVariables(effectiveIf.ThenBranch, analysis, "te")).IsGreaterThan(0);
        await Assert.That(Count<Multiply>(effectiveIf.ThenBranch, analysis)).IsEqualTo(0);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(9L);
    }

    [Test]
    public async Task BareComparison_DoesNotSubstituteUnstoredTemp() {
        var x = new Variable("x");
        var acc = new Variable("acc");
        var loop = new WhileLoop(
            new LessThanOrEqual(new Multiply(x, x), new Constant(100L)),
            new Block([
                new Assignment(acc, new Add(acc, new Multiply(x, x))),
                new Assignment(x, new Add(x, new Constant(10L))),
            ]));
        var block = new Block([
            new Assignment(x, new Constant(3L)),
            new Assignment(acc, new Constant(0L)),
            loop,
            acc,
        ], [x, acc]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<Multiply>(analysis.GetEffectiveNode(loop), analysis)).IsEqualTo(2);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(9L);
    }

    [Test]
    public async Task OrLeft_DoesNotReuseInBody() {
        var q = new Variable("q");
        var x = new Variable("x");
        var acc = new Variable("acc");
        var bodyUse = new Multiply(x, x);
        var ifStatement = new IfStatement(
            new Or(new LessThan(new Multiply(x, x), new Constant(100L)), q),
            new Assignment(acc, bodyUse));
        var block = new Block([
            new Assignment(q, new Constant(false)),
            new Assignment(x, new Constant(3L)),
            new Assignment(acc, new Constant(0L)),
            ifStatement,
            acc,
        ], [q, x, acc]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(analysis.GetEffectiveNode(bodyUse)).IsTypeOf<Multiply>();
        await Assert.That(Count<Multiply>(analysis.GetEffectiveNode(ifStatement), analysis)).IsEqualTo(2);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(9L);
    }

    [Test]
    public async Task BodyAssignsBeforeUse_DoesNotReuse() {
        var i = new Variable("i");
        var x = new Variable("x");
        var acc = new Variable("acc");
        var loop = new WhileLoop(
            new And(new LessThan(i, new Constant(1L)), new LessThan(new Multiply(x, x), new Constant(100L))),
            new Block([
                new Assignment(x, new Constant(5L)),
                new Assignment(acc, new Multiply(x, x)),
                new Assignment(i, new Add(i, new Constant(1L))),
            ]));
        var block = new Block([
            new Assignment(i, new Constant(0L)),
            new Assignment(x, new Constant(3L)),
            new Assignment(acc, new Constant(0L)),
            loop,
            acc,
        ], [i, x, acc]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<Multiply>(analysis.GetEffectiveNode(loop), analysis)).IsEqualTo(2);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(25L);
    }

    [Test]
    public async Task Profit_NqueensSubtractShape_DoesNotTemp() {
        var row = new Variable("row");
        var arr = new Variable("arr");
        var body = new Block([
            new Assignment(row, new Constant(5L)),
            new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(10L))),
            new Assignment(new IndexAccess(arr, row), new Subtract(row, new Constant(1L))),
            new Assignment(new IndexAccess(arr, new Constant(1L)), new Subtract(row, new Constant(1L))),
            new Assignment(new IndexAccess(arr, new Constant(2L)), new Subtract(row, new Constant(1L))),
            new IndexAccess(arr, row),
        ], [row, arr]);

        var analysis = Interpreter.Analyze(body);
        await Assert.That(Count<Subtract>(body, analysis)).IsEqualTo(3);
        await Assert.That(CountVariables(body, analysis, "av")).IsEqualTo(0);
        await Assert.That(CountVariables(body, analysis, "te")).IsEqualTo(0);
        await Assert.That(CountVariables(body, analysis, "li")).IsEqualTo(0);
        using var exec = Interpreter.Execute(Interpreter.Compile(body, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(4L);
    }

    [Test]
    public async Task Profit_SieveShape_DoesNotTemp() {
        var i = new Variable("i");
        var bound = new Variable("bound");
        var flag = new Variable("flag");
        var acc = new Variable("acc");
        var loop = new WhileLoop(
            new LessThanOrEqual(new Multiply(i, i), bound),
            new Block([
                new IfStatement(flag, new Assignment(acc, new Add(acc, new Multiply(i, i)))),
                new Assignment(i, new Add(i, new Constant(1L))),
            ]));
        var block = new Block([
            new Assignment(i, new Constant(1L)),
            new Assignment(bound, new Constant(3L)),
            new Assignment(flag, new Constant(true)),
            new Assignment(acc, new Constant(0L)),
            loop,
            acc,
        ], [i, bound, flag, acc]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<Multiply>(analysis.GetEffectiveNode(loop), analysis)).IsEqualTo(2);
        await Assert.That(CountVariables(block, analysis, "av")).IsEqualTo(0);
        await Assert.That(CountVariables(block, analysis, "te")).IsEqualTo(0);
        await Assert.That(CountVariables(block, analysis, "li")).IsEqualTo(0);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(1L);
    }

    [Test]
    public async Task IndexRead_NoWrite_Reuses() {
        var a = new Variable("a");
        var i = new Variable("i");
        var sum = new Add(new IndexAccess(a, i), new IndexAccess(a, i));
        var block = new Block([
            new Assignment(a, new NewArray(TypeReference.To<long>(), new Constant(2L))),
            new Assignment(new IndexAccess(a, new Constant(0L)), new Constant(3L)),
            new Assignment(i, new Constant(0L)),
            sum,
        ], [a, i]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<IndexAccess>(sum, analysis)).IsEqualTo(1);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(6L);
    }

    [Test]
    public async Task IndexWrite_UnprovenKey_StaysTwo() {
        var a = new Variable("a");
        var i = new Variable("i");
        var j = new Variable("j");
        var acc = new Variable("acc");
        var first = new Assignment(acc, new IndexAccess(a, i));
        var second = new Assignment(acc, new Add(acc, new IndexAccess(a, i)));
        var block = new Block([
            new Assignment(a, new NewArray(TypeReference.To<long>(), new Constant(3L))),
            new Assignment(new IndexAccess(a, new Constant(0L)), new Constant(3L)),
            new Assignment(i, new Constant(0L)),
            new Assignment(j, new Constant(0L)),
            first,
            new Assignment(new IndexAccess(a, j), new Constant(1L)),
            second,
            acc,
        ], [a, i, j, acc]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<IndexAccess>(first, analysis) + Count<IndexAccess>(second, analysis)).IsEqualTo(2);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(4L);
    }

    [Test]
    public async Task IndexWrite_UnequalConstants_Reuses() {
        var a = new Variable("a");
        var acc = new Variable("acc");
        var first = new Assignment(acc, new IndexAccess(a, new Constant(1L)));
        var second = new Assignment(acc, new Add(acc, new IndexAccess(a, new Constant(1L))));
        var block = new Block([
            new Assignment(a, new NewArray(TypeReference.To<long>(), new Constant(3L))),
            new Assignment(new IndexAccess(a, new Constant(1L)), new Constant(3L)),
            first,
            new Assignment(new IndexAccess(a, new Constant(2L)), new Constant(1L)),
            second,
            acc,
        ], [a, acc]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<IndexAccess>(first, analysis) + Count<IndexAccess>(second, analysis)).IsEqualTo(1);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(6L);
    }

    [Test]
    public async Task CapturedArray_AfterCall_StaysTwo() {
        var a = new Variable("a");
        var i = new Variable("i");
        var acc = new Variable("acc");
        var xs = new Parameter("xs", TypeReference.To<long[]>());
        var first = new Assignment(acc, new IndexAccess(a, i));
        var second = new Assignment(acc, new Add(acc, new IndexAccess(a, i)));
        var block = new Block([
            new Assignment(a, new NewArray(TypeReference.To<long>(), new Constant(3L))),
            new Assignment(new IndexAccess(a, new Constant(0L)), new Constant(7L)),
            new Assignment(i, new Constant(0L)),
            first,
            new Invoke(new Lambda([xs], new Member(xs, "Length")), a),
            second,
            acc,
        ], [a, i, acc]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<IndexAccess>(first, analysis) + Count<IndexAccess>(second, analysis)).IsEqualTo(2);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(14L);
    }

    [Test]
    public async Task WhileBodyDuplicates_NotUnderAnd_DoesNotReadZeroTemp() {
        var x = new Variable("x");
        var y = new Variable("y");
        var loop = new WhileLoop(
            new LessThan(x, new Constant(10L)),
            new Block([
                new Assignment(y, new Add(new Multiply(x, x), new Multiply(x, x))),
                new Assignment(x, new Constant(10L)),
            ]));
        var block = new Block([
            new Assignment(x, new Constant(3L)),
            new Assignment(y, new Constant(0L)),
            loop,
            y,
        ], [x, y]);

        using var exec = Interpreter.Execute(Interpreter.Compile(block));
        await Assert.That(exec.RawValue).IsEqualTo(18L);
    }

    [Test]
    public async Task AndUnderNot_DoesNotReadUnstoredTemp() {
        var flag = new Variable("flag");
        var x = new Variable("x");
        var acc = new Variable("acc");
        var loop = new WhileLoop(
            new Not(new And(flag, new LessThan(new Multiply(x, x), new Constant(100L)))),
            new Block([
                new Assignment(acc, new Add(acc, new Multiply(x, x))),
                new Assignment(flag, new Constant(true)),
            ]));
        var block = new Block([
            new Assignment(flag, new Constant(false)),
            new Assignment(x, new Constant(3L)),
            new Assignment(acc, new Constant(0L)),
            loop,
            acc,
        ], [flag, x, acc]);

        using var exec = Interpreter.Execute(Interpreter.Compile(block));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(9L);
    }

    [Test]
    public async Task RootWhile_KeepsTrueEdgeRewrite() {
        var x = new Variable("x");
        var acc = new Variable("acc");
        var iter = new Variable("iter");
        var loop = new WhileLoop(
            new And(new LessThan(iter, new Constant(1L)), new LessThan(new Multiply(x, x), new Constant(100L))),
            new Assignment(acc, new Multiply(x, x)));
        var analysis = Interpreter.Analyze(loop);
        await Assert.That(CountVariables(analysis.GetEffectiveNode(loop), analysis, "te")).IsGreaterThan(0);
    }

    [Test]
    public async Task CallOnAndRight_KillsIndexNumber() {
        var a = new Variable("a");
        var i = new Variable("i");
        var acc = new Variable("acc");
        var flag = new Variable("flag");
        var first = new Assignment(acc, new IndexAccess(a, i));
        var second = new Assignment(acc, new Add(acc, new IndexAccess(a, i)));
        var block = new Block([
            new Assignment(a, new NewArray(TypeReference.To<long>(), new Constant(1L))),
            new Assignment(new IndexAccess(a, new Constant(0L)), new Constant(3L)),
            new Assignment(i, new Constant(0L)),
            new Assignment(flag, new Constant(true)),
            new Assignment(acc, new Constant(0L)),
            first,
            new And(flag, new Invoke(new Lambda([], new Block([
                new Assignment(new IndexAccess(a, new Constant(0L)), new Constant(1L)),
                new Constant(true),
            ])))),
            second,
            acc,
        ], [a, i, acc, flag]);

        var analysis = Interpreter.Analyze(block);
        await Assert.That(Count<IndexAccess>(first, analysis) + Count<IndexAccess>(second, analysis)).IsEqualTo(2);
        using var exec = Interpreter.Execute(Interpreter.Compile(block, analysis));
        await Assert.That(exec.RawValue).IsEqualTo(4L);
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

    private static int Count<T>(Node node, AnalysisResult analysis) where T : Node {
        var effective = analysis.GetEffectiveNode(node);
        var count = effective is T ? 1 : 0;
        foreach (var child in effective.Children) {
            if (child is not null)
                count += Count<T>(child, analysis);
        }
        return count;
    }
}