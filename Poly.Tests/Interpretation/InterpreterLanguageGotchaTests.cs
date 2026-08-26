using Poly.Interpretation;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Interpreter-implementation lessons from CPython, Lua, JavaScript, Ruby, and C#,
/// mapped onto Syntax trees. Canonical meaning is the VM; LINQ is the same-tree checker
/// on pure expressions.
/// </summary>
public class InterpreterLanguageGotchaTests {
    [Test]
    public async Task And_FalseLeft_DoesNotEvaluateRight() {
        var seen = new Variable("seen");
        var node = new Block([
            new Assignment(seen, new Constant(false)),
            new And(new Constant(false), new Assignment(seen, new Constant(true))),
            seen
        ], [seen]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task And_TrueLeft_EvaluatesRight() {
        var seen = new Variable("seen");
        var node = new Block([
            new Assignment(seen, new Constant(false)),
            new And(new Constant(true), new Assignment(seen, new Constant(true))),
            seen
        ], [seen]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Or_TrueLeft_DoesNotEvaluateRight() {
        var seen = new Variable("seen");
        var node = new Block([
            new Assignment(seen, new Constant(false)),
            new Or(new Constant(true), new Assignment(seen, new Constant(true))),
            seen
        ], [seen]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Or_FalseLeft_EvaluatesRight() {
        var seen = new Variable("seen");
        var node = new Block([
            new Assignment(seen, new Constant(false)),
            new Or(new Constant(false), new Assignment(seen, new Constant(true))),
            seen
        ], [seen]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Conditional_True_DoesNotEvaluateFalseBranch() {
        var seen = new Variable("seen");
        var node = new Block([
            new Assignment(seen, new Constant(0L)),
            new Conditional(new Constant(true), new Constant(1L), new Assignment(seen, new Constant(99L))),
            seen
        ], [seen]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Conditional_False_DoesNotEvaluateTrueBranch() {
        var seen = new Variable("seen");
        var node = new Block([
            new Assignment(seen, new Constant(0L)),
            new Conditional(new Constant(false), new Assignment(seen, new Constant(99L)), new Constant(1L)),
            seen
        ], [seen]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task BitwiseAnd_EvaluatesBothOperands() {
        var left = new Variable("left");
        var right = new Variable("right");
        var node = new Block([
            new Assignment(left, new Constant(0L)),
            new Assignment(right, new Constant(0L)),
            new BitwiseAnd(
                new Assignment(left, new Constant(12L)),
                new Assignment(right, new Constant(10L))),
            new Add(left, right)
        ], [left, right]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(22L);
    }

    [Test]
    public async Task Equal_NaN_IsFalse() {
        var node = new Equal(new Constant(double.NaN), new Constant(double.NaN));
        await AssertLinqBool(node, false);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task NotEqual_NaN_IsTrue() {
        var node = new NotEqual(new Constant(double.NaN), new Constant(double.NaN));
        await AssertLinqBool(node, true);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_NaN_IsFalse() {
        var node = new LessThan(new Constant(double.NaN), new Constant(0.0));
        await AssertLinqBool(node, false);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Divide_DoubleByZero_IsInfinity() {
        var node = new Divide(new Constant(1.0), new Constant(0.0));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(double.IsPositiveInfinity(exec.GetValue<double>())).IsTrue();
    }

    [Test]
    public async Task Divide_IntByZero_Throws() {
        var node = new Divide(new Constant(1L), new Constant(0L));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(Interpreter.Compile(node));
        }).Throws<DivideByZeroException>();
    }

    [Test]
    public async Task Divide_NegativeInts_TruncatesTowardZero() {
        var node = new Divide(new Constant(-7L), new Constant(2L));
        await AssertLinqLong(node, -3L);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(-3L);
    }

    [Test]
    public async Task Add_LongOverflow_WrapsUnchecked() {
        var node = new Add(new Constant(long.MaxValue), new Constant(1L));
        var expected = unchecked(long.MaxValue + 1L);
        await AssertLinqLong(node, expected);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(expected);
    }

    [Test]
    public async Task Add_Double_PointOnePlusPointTwo_IsIeeeSum() {
        var node = new Add(new Constant(0.1), new Constant(0.2));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(0.1 + 0.2);
        await Assert.That(exec.GetValue<double>()).IsNotEqualTo(0.3);
    }

    [Test]
    public async Task Equal_StringValues_AreEqualByContent() {
        var node = new Equal(new Constant("hi"), new Constant("hi"));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Lambda_Capture_SeesLatestValueAtInvoke() {
        var captured = new Variable("captured");
        var lambda = new Lambda([], captured);
        var node = new Block([
            new Assignment(captured, new Constant(1L)),
            new Assignment(captured, new Constant(2L)),
            new Invoke(lambda)
        ], [captured]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task Invoke_VariableHoldingLambda_CallsThrough() {
        var fn = new Variable("fn");
        var lambda = new Lambda([], new Constant(41L));
        var node = new Block([
            new Assignment(fn, lambda),
            new Invoke(fn)
        ], [fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(41L);
    }

    [Test]
    public async Task Invoke_VariableHoldingLambda_PassesArguments() {
        var fn = new Variable("fn");
        var x = new Parameter("x", TypeReference.To<long>());
        var lambda = new Lambda([x], new Add(x, new Constant(1L)));
        var node = new Block([
            new Assignment(fn, lambda),
            new Invoke(fn, new Constant(41L))
        ], [fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(42L);
    }

    [Test]
    public async Task Invoke_StoredClosure_SnapshotsCapturesAtCreation() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var lambda = new Lambda([], captured);
        var node = new Block([
            new Assignment(captured, new Constant(1L)),
            new Assignment(fn, lambda),
            new Assignment(captured, new Constant(2L)),
            new Invoke(fn)
        ], [captured, fn]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.RawValue).IsEqualTo(1L);
    }

    [Test]
    public async Task Lambda_Capture_SeesLoopVariableFinalValue() {
        var i = new Variable("i");
        var lambda = new Lambda([], i);
        var node = new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(
                new LessThan(i, new Constant(3L)),
                new Assignment(i, new Add(i, new Constant(1L)))),
            new Invoke(lambda)
        ], [i]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task Invoke_Arguments_EvaluateLeftToRight() {
        var x = new Variable("x");
        var a = new Parameter("a", TypeReference.To<long>());
        var b = new Parameter("b", TypeReference.To<long>());
        var lambda = new Lambda([a, b], b);
        var node = new Block([
            new Assignment(x, new Constant(0L)),
            new Invoke(lambda, new Assignment(x, new Constant(1L)), x)
        ], [x]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Assignment_AsExpression_ReturnsAssignedValue() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(7L))
        ], [x]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task While_FalseCondition_DoesNotRunBody() {
        var seen = new Variable("seen");
        var node = new Block([
            new Assignment(seen, new Constant(0L)),
            new WhileLoop(new Constant(false), new Assignment(seen, new Constant(1L))),
            seen
        ], [seen]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task DoWhile_FalseCondition_RunsBodyOnce() {
        var seen = new Variable("seen");
        var node = new Block([
            new Assignment(seen, new Constant(0L)),
            new DoWhileLoop(new Assignment(seen, new Add(seen, new Constant(1L))), new Constant(false)),
            seen
        ], [seen]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task NestedWhile_Break_ExitsOnlyInner() {
        var outer = new Variable("outer");
        var inner = new Variable("inner");
        var node = new Block([
            new Assignment(outer, new Constant(0L)),
            new WhileLoop(
                new LessThan(outer, new Constant(2L)),
                new Block([
                    new Assignment(inner, new Constant(0L)),
                    new WhileLoop(
                        new Constant(true),
                        new Block([
                            new Assignment(inner, new Add(inner, new Constant(1L))),
                            new BreakStatement()
                        ])),
                    new Assignment(outer, new Add(outer, new Constant(1L)))
                ])),
            new Add(outer, inner)
        ], [outer, inner]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task NestedWhile_LabeledContinue_ContinuesOuter() {
        var i = new Variable("i");
        var node = new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(
                new LessThan(i, new Constant(3L)),
                new WhileLoop(
                    new Constant(true),
                    new Block([
                        new Assignment(i, new Add(i, new Constant(1L))),
                        new ContinueStatement("outer")
                    ])),
                "outer"),
            i
        ], [i]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node), s => s.MaxLoopIterations = 100);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    [Test]
    public async Task NestedWhile_LabeledBreak_ExitsOuter() {
        var outer = new Variable("outer");
        var node = new Block([
            new Assignment(outer, new Constant(0L)),
            new WhileLoop(
                new Constant(true),
                new WhileLoop(
                    new Constant(true),
                    new Block([
                        new Assignment(outer, new Constant(1L)),
                        new BreakStatement("outer")
                    ])),
                "outer"),
            outer
        ], [outer]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node), s => s.MaxLoopIterations = 100);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Switch_NoMatch_RunsDefaultOnly() {
        var taken = new Variable("taken");
        var node = new Block([
            new Assignment(taken, new Constant(0L)),
            new SwitchStatement(
                new Constant(9L),
                [new SwitchCase(new Constant(1L), new Assignment(taken, new Constant(1L)))],
                new Assignment(taken, new Constant(2L))),
            taken
        ], [taken]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task Switch_Match_DoesNotRunDefault() {
        var taken = new Variable("taken");
        var node = new Block([
            new Assignment(taken, new Constant(0L)),
            new SwitchStatement(
                new Constant(1L),
                [new SwitchCase(new Constant(1L), new Assignment(taken, new Constant(1L)))],
                new Assignment(taken, new Constant(2L))),
            taken
        ], [taken]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task TryFinally_RunsFinallyBeforeReturn() {
        var bag = new List<long>();
        var node = new TryCatchFinally(
            new Return(new Constant(7L)),
            CatchClauses: null,
            FinallyBlock: new Invoke(new Member(new Constant(bag), "Add"), new Constant(1L)));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
        await Assert.That(bag).IsEquivalentTo(new long[] { 1L });
    }

    [Test]
    public async Task TryThrow_FinallyRunsThenPropagates() {
        var bag = new List<long>();
        var node = new TryCatchFinally(
            new ThrowStatement(new New(TypeReference.To<InvalidOperationException>())),
            CatchClauses: null,
            FinallyBlock: new Invoke(new Member(new Constant(bag), "Add"), new Constant(1L)));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(Interpreter.Compile(node));
        }).Throws<InvalidOperationException>();
        await Assert.That(bag).IsEquivalentTo(new long[] { 1L });
    }

    [Test]
    public async Task FinallyThrow_ReplacesTryThrow() {
        var node = new TryCatchFinally(
            new ThrowStatement(new New(TypeReference.To<InvalidOperationException>())),
            CatchClauses: null,
            FinallyBlock: new ThrowStatement(new New(TypeReference.To<ArgumentException>())));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(Interpreter.Compile(node));
        }).Throws<ArgumentException>();
    }

    [Test]
    public async Task Catch_FirstMatchingClause_Wins() {
        var caught = new Variable("caught");
        var node = new Block([
            new Assignment(caught, new Constant(0L)),
            new TryCatchFinally(
                new ThrowStatement(new New(TypeReference.To<InvalidOperationException>())),
                CatchClauses: [
                    new CatchClause(
                        TypeReference.To<InvalidOperationException>(),
                        "ex",
                        new Assignment(caught, new Constant(1L))),
                    new CatchClause(
                        TypeReference.To<Exception>(),
                        "ex2",
                        new Assignment(caught, new Constant(2L)))
                ]),
            caught
        ], [caught]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Using_OnThrow_StillDisposes() {
        var resource = new TrackingDisposable();
        var node = new UsingStatement(
            new Constant(resource),
            new ThrowStatement(new New(TypeReference.To<InvalidOperationException>())));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(Interpreter.Compile(node));
        }).Throws<InvalidOperationException>();
        await Assert.That(resource.Disposed).IsTrue();
    }

    [Test]
    public async Task ForEach_MutatingList_ThrowsInvalidOperation() {
        var list = new List<long> { 1L, 2L };
        var item = new Variable("item");
        var node = new ForEachLoop(
            item,
            new Constant(list),
            new Invoke(new Member(new Constant(list), "Add"), new Constant(99L)));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(Interpreter.Compile(node));
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task IndexAccess_OutOfRange_Throws() {
        var arr = new Variable("arr", new Constant(new long[] { 10, 20 }));
        var node = new Block(arr, new IndexAccess(arr, new Constant(9L)));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(Interpreter.Compile(node));
        }).Throws<IndexOutOfRangeException>();
    }

    [Test]
    public async Task Member_OnNull_FailsLoud() {
        var node = new Member(new Constant(null), "Length");
        try {
            var program = Interpreter.Compile(node);
            await Assert.That(() => {
                using var exec = Interpreter.Execute(program);
            }).Throws<Exception>();
        }
        catch (InvalidOperationException) {
        }
    }

    [Test]
    public async Task ForLoop_FalseCondition_RunsInitNotBody() {
        var i = new Variable("i");
        var seen = new Variable("seen");
        var node = new Block([
            new Assignment(seen, new Constant(0L)),
            new ForLoop(
                new Assignment(i, new Constant(5L)),
                new LessThan(i, new Constant(0L)),
                new Assignment(i, new Add(i, new Constant(1L))),
                new Assignment(seen, new Constant(1L))),
            new Add(i, seen)
        ], [i, seen]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(5L);
    }

    [Test]
    public async Task ChainedComparison_IsNotPythonStyle_CompileRejects() {
        var node = new LessThan(
            new LessThan(new Constant(1L), new Constant(2L)),
            new Constant(3L));
        await Assert.That(() => Interpreter.Compile(node)).Throws<InvalidOperationException>();
    }

    private static async Task AssertLinqLong(Node node, long expected) {
        var compiled = Expression.Lambda<Func<long>>(node.BuildExpression()).Compile();
        await Assert.That(compiled()).IsEqualTo(expected);
    }

    private static async Task AssertLinqBool(Node node, bool expected) {
        var compiled = Expression.Lambda<Func<bool>>(node.BuildExpression()).Compile();
        await Assert.That(compiled()).IsEqualTo(expected);
    }

    private sealed class TrackingDisposable : IDisposable {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}