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

        await Assert.That(RingLocalCount(built)).IsEqualTo(1);
    }

    [Test, Timeout(10_000)]
    public async Task RingHighWater_IsTheDeepestStatement_NotTheSum(CancellationToken ct) {
        var arr = new Variable("arr");
        var alias = new Variable("alias");
        var x = new Variable("x");
        var b = new Variable("b");
        // An index access on a heap array is the reliably deep statement: receiver,
        // index, and result. The array escapes through `alias`, so the escape scan
        // keeps it on the heap — a promoted buffer is read straight from its frame
        // window and needs one ring slot, which measures a different thing.
        var makeArray = new Assignment(arr, new NewArray(TypeReference.To<long>(), new Constant(4L)));
        var escape = new Assignment(alias, arr);
        var deep = new Assignment(x, new IndexAccess(alias, new Constant(0L)));
        var shallow = new Assignment(b, new Constant(1L));

        int deepAlone = RingLocalCount(Build(new Block([makeArray, escape, deep], [arr, alias, x])));
        int deepThenShallow = RingLocalCount(Build(new Block(
            [makeArray, escape, deep, shallow], [arr, alias, x, b])));

        await Assert.That(deepAlone).IsGreaterThanOrEqualTo(2);
        await Assert.That(deepThenShallow).IsEqualTo(deepAlone);
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