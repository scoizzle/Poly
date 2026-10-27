using System.Linq.Expressions;

using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The variable-storage representation rule, proven on the emitted tree. A register is
/// declared at the representation of the variable it backs — <c>double</c> for an IEEE
/// local, the <c>long</c> word otherwise — so reading a <c>double</c> variable is the
/// value itself and only a word boundary (a frame slot, a capture cell, a CLR call)
/// converts. IS7a typed the ring; the hot state of an FP program lives here.
/// </summary>
public class TypedVariableStorageTests {
    private static AbiEmitter.BuiltVmProgram Build(Node program) =>
        AbiEmitter.Build(program, Interpreter.Analyze(program), CompilationMode.NoDebug);

    private static VmTree.Scan Scan(Node program) => VmTree.Read(Build(program).Expression);

    /// <summary>The shape that costs 1.9x: mandelbrot's <c>zr = zr*zr - zi*zi</c>. The
    /// operands are variables, so the ring being typed is not enough — the registers have
    /// to hold the doubles, or every read of <c>zr</c> round-trips the word.</summary>
    [Test, Timeout(10_000)]
    public async Task DoubleVariableArithmetic_HasNoBitcast(CancellationToken ct) {
        var zr = new Variable("zr");
        var zi = new Variable("zi");
        var program = new Block([
            new Assignment(zr, new Constant(0.5)),
            new Assignment(zi, new Constant(0.25)),
            new Assignment(zr, new Subtract(new Multiply(zr, zr), new Multiply(zi, zi))),
        ], [zr, zi]);

        var scan = Scan(program);

        var registers = scan.Registers;
        await Assert.That(registers.Any(local => local.Name == "_d0" && local.Type == typeof(double)))
            .IsTrue().Because("zr is a double variable, so its pool lease holds the double (_d0, not _dreg0): "
                + string.Join(", ", registers.Select(local => $"{local.Name}:{local.Type.Name}")));

        var offenders = scan.RegisterBitcasts
            .Select(call => $"{call.Method.Name}({call.Arguments[0]})")
            .ToList();
        await Assert.That(offenders).IsEmpty()
            .Because("a double variable must not cross the word boundary per access: "
                + string.Join(", ", offenders));

        var ieee = scan.IeeeArithmeticWithBitcasts
            .Select(op => $"{op.NodeType}({op.Left.Type.Name}, {op.Right.Type.Name})")
            .ToList();
        await Assert.That(ieee).IsEmpty()
            .Because("every operand of the arithmetic is a typed variable read: "
                + string.Join(", ", ieee));
    }

    /// <summary>A double assigned by one statement and read by the next keeps the same
    /// register and type: the read is a copy, not a bitcast round trip.</summary>
    [Test, Timeout(10_000)]
    public async Task DoubleVariable_AcrossStatements_KeepsItsRegister(CancellationToken ct) {
        var z = new Variable("z");
        var program = new Block([
            new Assignment(z, new Constant(0.5)),
            new Assignment(z, new Add(new Multiply(z, new Constant(2.0)), new Constant(0.25))),
            z,
        ], [z]);

        var built = Build(program);
        var scan = VmTree.Read(built.Expression);

        var register = scan.Registers.Single(local => local.Name == "_d0");
        await Assert.That(register.Type).IsEqualTo(typeof(double));
        await Assert.That(scan.Registers.Count(local => local.Name == "_d0")).IsEqualTo(1)
            .Because("both statements write the one _d0 lease the declaration bound");
        await Assert.That(scan.RegisterBitcasts).IsEmpty()
            .Because("the second statement reads and writes the double lease directly");
        await Assert.That(ResultOf(program)).IsEqualTo(1.25)
            .Because("the value still round-trips: z = z * 2 + 0.25");
    }

    /// <summary>A double crossing into a CLR call is the value the callee takes, and the
    /// only conversion is the call boundary the ABI already charged.</summary>
    [Test, Timeout(10_000)]
    public async Task DoubleVariable_ToClrCall_ConvertsOnce(CancellationToken ct) {
        var host = new DoubleHost();
        var z = new Variable("z");
        var program = new Block([
            new Assignment(z, new Constant(2.5)),
            new Invoke(new Member(new Constant(host), nameof(DoubleHost.Echo)), z),
        ], [z]);

        var built = Build(program);
        var scan = VmTree.Read(built.Expression);

        await Assert.That(scan.RegisterBitcasts).IsEmpty()
            .Because("the call takes the double lease as it stands; the lease is not "
                + "read through a bitcast and written back");

        var echo = CallArguments(built.Expression, nameof(DoubleHost.Echo));
        await Assert.That(echo.Count).IsEqualTo(1);
        await Assert.That(VmTree.BitcastCount(echo[0])).IsEqualTo(1)
            .Because("the call argument is the word boundary: the double converts once here, "
                + "not once per access and not twice");
        await Assert.That(ResultOf(program)).IsEqualTo(2.5);
    }

    /// <summary>A captured double lives in a capture cell, which is a <c>long[1]</c> word:
    /// the cell converts once per access, and the register keeps the cell's handle rather
    /// than being retyped under it.</summary>
    [Test, Timeout(10_000)]
    public async Task DoubleVariable_Captured_ConvertsAtTheCell(CancellationToken ct) {
        var z = new Variable("z");
        var fn = new Variable("fn");
        var program = new Block([
            new Assignment(z, new Constant(0.5)),
            new Assignment(fn, new Lambda([], new Add(z, new Constant(1.0)))),
            new Invoke(fn),
        ], [z, fn]);

        var scan = Scan(program);

        var register = scan.Registers.Single(local => local.Name == "_r0");
        await Assert.That(register.Type).IsEqualTo(typeof(long))
            .Because("a cell-backed local's lease holds the cell handle, not the value; the lease is _r0, not _reg0");
        await Assert.That(scan.Registers.Any(local => local.Name == "_d0")).IsFalse()
            .Because("retyping the lease to _d0 would silently coerce the cell handle");
        await Assert.That(scan.RegisterBitcasts).IsEmpty()
            .Because("the conversion belongs to the cell, which is the word boundary");

        await Assert.That(ResultOf(program)).IsEqualTo(1.5)
            .Because("the captured double still reads back through the cell");
    }

    /// <summary>The collision the register table exists for: a register index is released
    /// when its scope ends and reused by the next scope's variable, which may have a
    /// different representation. The two must not alias one local — an
    /// <c>Expression.Assign</c> to a wrongly typed parameter throws, and a silent coercion
    /// would be worse.</summary>
    [Test, Timeout(10_000)]
    public async Task RegisterIndex_ReusedAcrossRepresentations_DoesNotAlias(CancellationToken ct) {
        var d = new Variable("d");
        var w = new Variable("w");
        var program = new Block([
            new Block([new Assignment(d, new Constant(1.5)), d], [d]),
            new Block([new Assignment(w, new Constant(7L)), w], [w]),
        ]);

        var scan = Scan(program);

        var doubleAtZero = scan.Registers.SingleOrDefault(local => local.Name == "_d0");
        var wordAtZero = scan.Registers.SingleOrDefault(local => local.Name == "_r0");
        await Assert.That(doubleAtZero).IsNotNull()
            .Because("the first block's double takes pool index 0 as _d0");
        await Assert.That(wordAtZero).IsNotNull()
            .Because("the second block's long reuses index 0 as _r0");
        await Assert.That(doubleAtZero!.Type).IsEqualTo(typeof(double));
        await Assert.That(wordAtZero!.Type).IsEqualTo(typeof(long));
        await Assert.That(ReferenceEquals(doubleAtZero, wordAtZero)).IsFalse()
            .Because("one ParameterExpression cannot be assigned both a double and a word");
        await Assert.That(DirectVmAbiEmitterTests.ExecDirect(program)).IsEqualTo(7L)
            .Because("both scopes still read the value they wrote");
    }

    /// <summary>An integer variable keeps the word: no register is retyped, and nothing
    /// converts on the way in or out.</summary>
    [Test, Timeout(10_000)]
    public async Task IntegerVariable_StaysAWord(CancellationToken ct) {
        var n = new Variable("n");
        var program = new Block([
            new Assignment(n, new Constant(7L)),
            new Assignment(n, new Add(n, new Constant(1L))),
            n,
        ], [n]);

        var scan = Scan(program);

        var register = scan.Registers.Single();
        await Assert.That(register.Name).IsEqualTo("_r0")
            .Because("an integer variable is the pool's long lease _r0, not a _reg0 local");
        await Assert.That(register.Type).IsEqualTo(typeof(long));
        await Assert.That(scan.RegisterBitcasts).IsEmpty();
        await Assert.That(DirectVmAbiEmitterTests.ExecDirect(program)).IsEqualTo(8L);
    }

    private static IReadOnlyList<Expression> CallArguments(Expression body, string method) {
        var finder = new ArgumentFinder(method);
        finder.Visit(body);
        return finder.Arguments;
    }

    private sealed class ArgumentFinder(string method) : ExpressionVisitor {
        public List<Expression> Arguments { get; } = [];

        protected override Expression VisitMethodCall(MethodCallExpression node) {
            if (node.Method.Name == method)
                Arguments.AddRange(node.Arguments);
            return base.VisitMethodCall(node);
        }
    }

    /// <summary>A double crossing into CLR: the callee wants the value.</summary>
    private sealed class DoubleHost {
        public double Echo(double value) => value;
    }

    private static object? ResultOf(Node program) {
        using var execution = Interpreter.Execute(Interpreter.Compile(program));
        return execution.Result.Value;
    }
}