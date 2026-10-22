using System.Collections;
using System.Linq.Expressions;

using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Characterises the scaffolding the four loop emitters share: one iteration guard
/// first, one jump to the break label, one continue label, and the scope that makes
/// break/continue resolve — decided in one place rather than four. Beside each shape
/// sits the meaning that must stay outside it: <c>do</c>'s test-after-body,
/// <c>for</c>'s continue landing on the increment, <c>foreach</c>'s per-iteration
/// binding and enumerator dispose.
/// </summary>
public class LoopScaffoldShapeTests {
    private static AbiEmitter.BuiltVmProgram Build(Node node) =>
        AbiEmitter.Build(node, Interpreter.Analyze(node), CompilationMode.Normal);

    private static VmTree.LoopShape OnlyLoop(Node node) {
        var built = Build(node);
        VmTree.AssertInvariants(built);
        var scan = VmTree.Read(built.Expression);
        if (scan.Loops.Count != 1)
            throw new InvalidOperationException($"expected one loop, found {scan.Loops.Count}");
        return scan.Loops[0];
    }

    private static long Exec(Node node) {
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        return exec.GetValue<long>();
    }

    private static Block WhileToThree(Variable i) => new([
        new Assignment(i, new Constant(0L)),
        new WhileLoop(new LessThan(i, new Constant(3L)), new Assignment(i, new Add(i, new Constant(1L)))),
        i
    ], [i]);

    [Test, Timeout(10_000)]
    public async Task While_HasOneGuardAndOneScope(CancellationToken ct) {
        var i = new Variable("i");
        var shape = OnlyLoop(WhileToThree(i));

        await Assert.That(shape.BreakTarget!.Name).IsEqualTo("wl_break");
        await Assert.That(shape.Steps.Count).IsEqualTo(4);
        await Assert.That(shape.GuardIndex).IsEqualTo(0);
        await Assert.That(shape.BreakTestIndex).IsEqualTo(1);
        await Assert.That(shape.ContinueLabelIndex).IsEqualTo(3);
        await Assert.That(shape.InTryFinally).IsFalse();

        // The scope is pushed before the body compiles, so a `continue` in the body is
        // the loop's own...
        var loop = new WhileLoop(new LessThan(i, new Constant(3L)), new ContinueStatement());
        var inside = OnlyLoop(new Block([new Assignment(i, new Constant(0L)), loop], [i]));
        await Assert.That(inside.Steps[2]).IsAssignableTo<GotoExpression>();
        await Assert.That(((GotoExpression)inside.Steps[2]).Target).IsSameReferenceAs(inside.ContinueTarget);

        // ...and popped after it, so one after the loop is rejected rather than resolving
        // to a scope this emitter forgot to close.
        var outside = new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(new LessThan(i, new Constant(3L)), new Assignment(i, new Add(i, new Constant(1L)))),
            new ContinueStatement()
        ], [i]);
        await Assert.That(() => Interpreter.Compile(outside))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("outside an enclosing loop");
    }

    [Test, Timeout(10_000)]
    public async Task For_ContinueLandsOnIncrement(CancellationToken ct) {
        var i = new Variable("i");
        var shape = OnlyLoop(new Block([
            new Assignment(i, new Constant(0L)),
            new ForLoop(
                new Assignment(i, new Constant(0L)),
                new LessThan(i, new Constant(3L)),
                new Assignment(i, new Add(i, new Constant(1L))),
                new ContinueStatement()),
            i
        ], [i]));

        await Assert.That(shape.BreakTarget!.Name).IsEqualTo("for_break");
        await Assert.That(shape.GuardIndex).IsEqualTo(0);
        await Assert.That(shape.BreakTestIndex).IsEqualTo(1);
        // The increment is the step after the continue label, not before the body: a
        // `continue` that skipped it would spin until the iteration guard fired.
        await Assert.That(shape.ContinueLabelIndex).IsEqualTo(shape.Steps.Count - 2);
        await Assert.That(Exec(new Block([
            new Assignment(i, new Constant(0L)),
            new ForLoop(
                new Assignment(i, new Constant(0L)),
                new LessThan(i, new Constant(3L)),
                new Assignment(i, new Add(i, new Constant(1L))),
                new ContinueStatement()),
            i
        ], [i]))).IsEqualTo(3L);
    }

    [Test, Timeout(10_000)]
    public async Task Do_ContinueLandsOnTheTest(CancellationToken ct) {
        var i = new Variable("i");
        var shape = OnlyLoop(new Block([
            new Assignment(i, new Constant(0L)),
            new DoWhileLoop(
                new Block([
                    new Assignment(i, new Add(i, new Constant(1L))),
                    new IfStatement(new Equal(i, new Constant(2L)), new ContinueStatement())
                ]),
                new LessThan(i, new Constant(3L))),
            i
        ], [i]));

        await Assert.That(shape.BreakTarget!.Name).IsEqualTo("dwl_break");
        await Assert.That(shape.GuardIndex).IsEqualTo(0);
        // The body runs before the first test, and the continue label is the step the
        // test follows — so `continue` re-tests instead of re-running the body.
        await Assert.That(shape.BreakTestIndex).IsEqualTo(shape.Steps.Count - 1);
        await Assert.That(shape.ContinueLabelIndex).IsEqualTo(shape.BreakTestIndex - 1);
    }

    [Test, Timeout(10_000)]
    public async Task ForEach_DisposesEnumerator(CancellationToken ct) {
        var sum = new Variable("sum");
        var item = new Variable("item");
        var shape = OnlyLoop(new Block([
            new Assignment(sum, new Constant(0L)),
            new ForEachLoop(item, new Constant(new long[] { 1L, 2L, 3L }), new Assignment(sum, new Add(sum, item))),
            sum
        ], [sum]));

        await Assert.That(shape.BreakTarget!.Name).IsEqualTo("foreach_break");
        await Assert.That(shape.InTryFinally).IsTrue();
        await Assert.That(shape.GuardIndex).IsEqualTo(0);
        // MoveNext is the test; the binding is the step between it and the body.
        await Assert.That(shape.BreakTestIndex).IsEqualTo(1);
        await Assert.That(shape.Steps[2] is BinaryExpression { NodeType: ExpressionType.Assign }).IsTrue();
        await Assert.That(shape.ContinueLabelIndex).IsEqualTo(shape.Steps.Count - 1);

        // One dispose for the loop, not one per iteration: the enumerator's lifetime is
        // the loop's, so it cannot have moved into the scaffold's per-iteration steps.
        var coll = new DisposableEnumerable();
        var node = new Block([
            new Assignment(sum, new Constant(0L)),
            new ForEachLoop(item, new Constant(coll), new Assignment(sum, new Add(sum, item))),
            sum
        ], [sum]);
        await Assert.That(Exec(node)).IsEqualTo(6L);
        await Assert.That(coll.DisposeCount).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task AllFour_ShareOneScaffoldShape(CancellationToken ct) {
        var i = new Variable("i");
        var sum = new Variable("sum");
        var item = new Variable("item");
        var loops = new (string Name, Node Node)[] {
            ("while", WhileToThree(i)),
            ("for", new Block([
                new Assignment(i, new Constant(0L)),
                new ForLoop(
                    new Assignment(i, new Constant(0L)),
                    new LessThan(i, new Constant(3L)),
                    new Assignment(i, new Add(i, new Constant(1L))),
                    new Assignment(i, new Add(i, new Constant(1L)))),
                i
            ], [i])),
            ("do", new Block([
                new Assignment(i, new Constant(0L)),
                new DoWhileLoop(new Assignment(i, new Add(i, new Constant(1L))), new LessThan(i, new Constant(3L))),
                i
            ], [i])),
            ("foreach", new Block([
                new Assignment(sum, new Constant(0L)),
                new ForEachLoop(item, new Constant(new long[] { 1L, 2L }), new Assignment(sum, new Add(sum, item))),
                sum
            ], [sum])),
        };

        foreach (var (name, node) in loops) {
            var shape = OnlyLoop(node);
            var where = $"{name}: guard={shape.GuardIndex} test={shape.BreakTestIndex} " +
                $"continue={shape.ContinueLabelIndex} steps={shape.Steps.Count}";
            await Assert.That(shape.GuardIndex).IsEqualTo(0).Because(where);
            await Assert.That(shape.GuardCount).IsEqualTo(1).Because(where);
            await Assert.That(shape.BreakTestCount).IsEqualTo(1).Because(where);
            await Assert.That(shape.ContinueLabelCount).IsEqualTo(1).Because(where);
            await Assert.That(shape.BreakTestIndex == 1 || shape.BreakTestIndex == shape.Steps.Count - 1)
                .IsTrue().Because(where);
            await Assert.That(shape.ContinueLabelIndex).IsGreaterThanOrEqualTo(0).Because(where);
            await Assert.That(shape.ContinueTarget).IsNotNull().Because(where);
            await Assert.That(shape.BreakTestIndex).IsNotEqualTo(shape.ContinueLabelIndex).Because(where);
        }

        // The scaffold names its labels the same way for every loop.
        await Assert.That(OnlyLoop(WhileToThree(i)).BreakTarget!.Name).IsEqualTo("wl_break");
        await Assert.That(OnlyLoop(loops[1].Node).BreakTarget!.Name).IsEqualTo("for_break");
        await Assert.That(OnlyLoop(loops[2].Node).BreakTarget!.Name).IsEqualTo("dwl_break");
        await Assert.That(OnlyLoop(loops[3].Node).BreakTarget!.Name).IsEqualTo("foreach_break");
    }

    private sealed class DisposableEnumerable : IEnumerable<long> {
        public int DisposeCount { get; private set; }

        public IEnumerator<long> GetEnumerator() => new Enum(this);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enum : IEnumerator<long> {
            private readonly DisposableEnumerable _owner;
            private int _index = -1;
            private static readonly long[] Items = [1, 2, 3];

            public Enum(DisposableEnumerable owner) => _owner = owner;

            public long Current => Items[_index];

            object IEnumerator.Current => Current;

            public bool MoveNext() => ++_index < Items.Length;

            public void Reset() => _index = -1;

            public void Dispose() => _owner.DisposeCount++;
        }
    }
}