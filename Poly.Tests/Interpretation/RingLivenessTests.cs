using System.Linq.Expressions;

using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

using static System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The ring is a pool of result locations, not an ambient cursor. A statement's
/// result is observable only when it is the block's value, so a block reuses the
/// same ring slots statement after statement instead of letting the high-water
/// accumulate. It did accumulate: <c>RingDepth</c> was never reset between
/// statements, so nqueens emitted 23 ring locals for a body whose deepest
/// statement needs 13.
/// </summary>
public class RingLivenessTests {
    private static AbiEmitter.BuiltVmProgram Build(Node node) =>
        AbiEmitter.Build(node, Interpreter.Analyze(node), CompilationMode.NoDebug);

    /// <summary>Ring locals (<c>_rN</c>) the compiled delegate declares: the pool
    /// high-water, not the number of statements.</summary>
    private static int RingLocalCount(AbiEmitter.BuiltVmProgram built) =>
        ((BlockExpression)built.Expression.Body).Variables
            .Count(v => v.Name is { } name
                && name.StartsWith("_r", StringComparison.Ordinal)
                && name.Length > 2 && char.IsDigit(name[2]));

    /// <summary>A statement whose emission needs two ring slots: the binary
    /// arithmetic spills its result, and the assignment stores it.</summary>
    private static Assignment DeepAssignment(Variable target) =>
        new(target, new Add(new Constant(1L), new Constant(2L)));

    [Test, Timeout(10_000)]
    public async Task ConsecutiveStatements_ReuseTheSameRingSlot(CancellationToken ct) {
        var a = new Variable("a");
        var b = new Variable("b");
        var built = Build(new Block([
            new Assignment(a, new Constant(1L)),
            new Assignment(b, new Constant(2L)),
            new Assignment(a, new Constant(3L)),
        ], [a, b]));

        // a and b are leases _r0 and _r1 for the whole block. The three assignments
        // share one value temp above them, so the pool declares three _r locals,
        // not one per statement.
        await Assert.That(RingLocalCount(built)).IsEqualTo(3)
            .Because("two variable leases plus one reused value temp");
    }

    [Test, Timeout(10_000)]
    public async Task RingHighWater_IsTheDeepestStatement_NotTheSum(CancellationToken ct) {
        var arr = new Variable("arr");
        var x = new Variable("x");
        var b = new Variable("b");
        // An index access is the reliably deep statement: receiver, index, and result.
        var makeArray = new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(4L)));
        var deep = new Assignment(x, new IndexAccess(arr, new Constant(0L)));
        var shallow = new Assignment(b, new Constant(1L));

        int deepAlone = RingLocalCount(Build(new Block([makeArray, deep], [arr, x])));
        int deepThenShallow = RingLocalCount(Build(new Block(
            [makeArray, deep, shallow], [arr, x, b])));

        await Assert.That(deepAlone).IsGreaterThanOrEqualTo(2);
        await Assert.That(deepThenShallow).IsEqualTo(deepAlone + 1)
            .Because("b is one more variable lease; the shallow assignment reuses the temp above the leases instead of adding its own slots");
    }

    [Test, Timeout(10_000)]
    public async Task BlockValue_SurvivesLaterStatementsThatAreReleased(CancellationToken ct) {
        var a = new Variable("a");
        var b = new Variable("b");
        var node = new Block([
            new Assignment(a, new Constant(1L)),
            new Assignment(b, new Constant(2L)),
            new Assignment(a, new Add(a, b)),
        ], [a, b]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(3L);
    }

    /// <summary>A comment emits nothing, so it must not release the result the
    /// preceding statement left: <c>[return 7, comment]</c> still yields 7.</summary>
    [Test, Timeout(10_000)]
    public async Task Comment_DoesNotReleaseThePendingResult(CancellationToken ct) {
        var node = new Invoke(new Lambda([], new Block([
            new IfStatement(new Constant(true), new Return(new Constant(7L))),
            new Comment("trailing"),
        ])));

        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }
}