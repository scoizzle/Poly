using System.Linq.Expressions;

using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The ring's representation rule, proven on the emitted tree. A ring slot carries a
/// representation as well as an index, so an IEEE value lives in a <c>double</c> or
/// <c>float</c> local and only becomes a bit pattern where a word has to move — a frame
/// slot, a register, a capture cell, or a call result.
/// </summary>
public class IeeeRingSlotTests {
    private static AbiEmitter.BuiltVmProgram Build(Node program) =>
        AbiEmitter.Build(program, Interpreter.Analyze(program), CompilationMode.NoDebug);

    private static VmTree.Scan Scan(Node program) => VmTree.Read(Build(program).Expression);

    /// <summary>The shape that costs 1.8x: <c>zx*zx + zy*zy</c> on two doubles. Every
    /// ring slot is typed, so the two multiplies and the add read their operands
    /// directly — no <c>Int64BitsToDouble</c> between them.</summary>
    [Test, Timeout(10_000)]
    public async Task IeeeArithmetic_HasNoBitcastBetweenOperands(CancellationToken ct) {
        var zr = new Variable("zr");
        var zi = new Variable("zi");
        var program = new Block([
            new Assignment(zr, new Constant(0.5)),
            new Assignment(zi, new Constant(0.25)),
            new Add(new Multiply(zr, zr), new Multiply(zi, zi)),
        ], [zr, zi]);

        var scan = Scan(program);

        await Assert.That(scan.IeeeArithmetic.Count).IsEqualTo(3)
            .Because("two multiplies and one add, so the check is not vacuous");
        var offenders = scan.IeeeArithmeticWithBitcasts
            .Select(op => $"{op.NodeType}({op.Left.Type.Name}, {op.Right.Type.Name})")
            .ToList();
        await Assert.That(offenders).IsEmpty()
            .Because("an IEEE operand must stay a double between operations; these still "
                + "round-trip a word at the operation: " + string.Join(", ", offenders));
    }

    /// <summary>A double stored by one statement and read by the next crosses the word
    /// boundary exactly once each way: two stores and one read are three bitcasts, and
    /// nothing else in the program bitcasts.</summary>
    [Test, Timeout(10_000)]
    public async Task IeeeAcrossStatements_CrossesTheBoundaryOnce(CancellationToken ct) {
        var zero = new Variable("zero");
        var copy = new Variable("copy");
        var program = new Block([
            new Assignment(zero, new Constant(0.5)),
            new Assignment(copy, zero),
        ], [zero, copy]);

        var built = Build(program);

        await Assert.That(VmTree.BitcastCount(built.Expression)).IsEqualTo(3)
            .Because("two double stores (zero, copy) and one double read (zero)");
        await Assert.That(ResultOf(program)).IsEqualTo(0.5)
            .Because("the typed slot still round-trips its value through the word");
    }

    /// <summary>A double compared to a double compares the doubles: neither operand is
    /// itself a bitcast call.</summary>
    [Test, Timeout(10_000)]
    public async Task IeeeComparison_ComparesDoubles(CancellationToken ct) {
        var a = new Variable("a");
        var b = new Variable("b");
        var program = new Block([
            new Assignment(a, new Constant(1.5)),
            new Assignment(b, new Constant(2.5)),
            new LessThan(a, b),
        ], [a, b]);

        var comparison = Scan(program).Comparisons.Single(
            op => op.NodeType == ExpressionType.LessThan);

        await Assert.That(comparison.Left.Type).IsEqualTo(typeof(double));
        await Assert.That(comparison.Right.Type).IsEqualTo(typeof(double));
        await Assert.That(IsBitcastCall(comparison.Left)).IsFalse();
        await Assert.That(IsBitcastCall(comparison.Right)).IsFalse();
    }

    /// <summary>A <c>float</c> expression stays 32-bit: the multiply itself is a
    /// <c>float</c> operation and widens nowhere inside.</summary>
    [Test, Timeout(10_000)]
    public async Task FloatExpression_StaysNarrow(CancellationToken ct) {
        var f = new Variable("f");
        var program = new Block([
            new Assignment(f, new Constant(1.5f)),
            new Multiply(f, f),
        ], [f]);

        var multiply = Scan(program).IeeeArithmetic
            .Single(op => op.NodeType == ExpressionType.Multiply);

        await Assert.That(multiply.Type).IsEqualTo(typeof(float));
        await Assert.That(multiply.Left.Type).IsEqualTo(typeof(float));
        await Assert.That(multiply.Right.Type).IsEqualTo(typeof(float));
        await Assert.That(VmTree.IsBitcastCall(multiply.Left)).IsFalse();
        await Assert.That(VmTree.IsBitcastCall(multiply.Right)).IsFalse();
    }

    /// <summary>The collision the slot table exists for: ring depth rewinds after every
    /// statement, so index 0 holds a <c>double</c> in the first statement and a
    /// <c>long</c> in the second. One local per index cannot be assigned both, so the
    /// slot table declares one local per (index, representation).</summary>
    [Test, Timeout(10_000)]
    public async Task RingSlot_HoldingTwoRepresentations_DeclaresTwoLocals(CancellationToken ct) {
        var value = new Variable("value");
        var count = new Variable("count");
        var program = new Block([
            new Assignment(value, new Add(new Multiply(new Constant(1.5), new Constant(1.5)),
                new Constant(0.25))),
            new Assignment(count, new Constant(7L)),
        ], [value, count]);

        var atZero = Scan(program).RingLocalsAt(0);
        var names = atZero.Select(local => local.Name).ToList();

        await Assert.That(names).Contains("_d0");
        await Assert.That(names).Contains("_r0");
        await Assert.That(atZero.Select(local => local.Type).Distinct().Count()).IsEqualTo(2)
            .Because("the two locals must be differently typed, not one coerced local");
        await Assert.That(DirectVmAbiEmitterTests.ExecDirect(program)).IsEqualTo(7L)
            .Because("both statements still have to read the value they wrote");
    }

    /// <summary>A representation the analysis cannot name falls back to the word slot
    /// rather than failing — and a program with no IEEE value declares no typed local,
    /// which is what keeps the integer programs' local count unchanged.</summary>
    [Test, Timeout(10_000)]
    public async Task UnknownRepresentation_UsesTheWordSlot(CancellationToken ct) {
        var ctx = new AbiEmitter.Context();
        var unresolved = new Member(new Constant(1L), "Unresolved");

        await Assert.That(ctx.SlotTypeOf(unresolved)).IsEqualTo(typeof(long));
        await Assert.That(ctx.RingVar(2, ctx.SlotTypeOf(unresolved)).Type).IsEqualTo(typeof(long));

        var scan = Scan(new Add(new Constant(1L), new Constant(2L)));
        var typed = scan.Declared
            .Where(local => local.Name is "_d0" or "_f0" or "_d1" or "_f1")
            .ToList();
        await Assert.That(typed).IsEmpty()
            .Because("an integer program keeps the word slot, so no typed local is declared");
    }

    private static bool IsBitcastCall(Expression expression) =>
        VmTree.IsBitcastCall(expression);

    private static object? ResultOf(Node program) {
        using var execution = Interpreter.Execute(Interpreter.Compile(program));
        return execution.Result.Value;
    }
}