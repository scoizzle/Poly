using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>
/// S1b. A store into a small integral variable wraps the way an unchecked C# cast
/// does. Today those variables are <c>long</c> locals, so <c>(short)(s + 1)</c> at
/// <c>short.MaxValue</c> keeps 32768. The register must also become a 32-bit local.
/// </summary>
public class IntegerFamilyNarrowingTests {
    private static ExecutionResult Run(Node program) =>
        Interpreter.Execute(Interpreter.Compile(program));

    private static VmTree.Scan Scan(Node program) =>
        VmTree.Read(AbiEmitter.Build(program, Interpreter.Analyze(program), CompilationMode.NoDebug).Expression);

    /// <summary><c>short s = 32767; s = (short)(s + 1);</c> is -32768, so <c>s &lt; 0</c>.</summary>
    [Test]
    public async Task ShortStore_WrapsAtInt16Boundary() {
        var s = new Variable("s");
        var program = new Block([
            new Assignment(s, new TypeCast(new Constant(short.MaxValue), TypeReference.To<short>())),
            new Assignment(s, new TypeCast(new Add(s, new Constant(1)), TypeReference.To<short>())),
            new LessThan(s, new Constant(0)),
        ], [s]);

        using var exec = Run(program);
        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task ShortVariable_RegisterIsInt32() {
        var s = new Variable("s");
        var program = new Block([
            new Assignment(s, new TypeCast(new Constant((short)1), TypeReference.To<short>())),
            s,
        ], [s]);

        var register = Scan(program).Registers.Single();
        await Assert.That(register.Name).IsEqualTo("_i0")
            .Because("a short variable is the pool's 32-bit lease _i0, not an _ireg0 local");
        await Assert.That(register.Type).IsEqualTo(typeof(int));
    }

    /// <summary><c>ushort.MaxValue + 1</c> stored as <c>ushort</c> is 0.</summary>
    [Test]
    public async Task UShortStore_WrapsAtUInt16Boundary() {
        await Assert.That(WrappedEqualsZero<ushort>(ushort.MaxValue)).IsTrue();
    }

    /// <summary><c>byte.MaxValue + 1</c> stored as <c>byte</c> is 0.</summary>
    [Test]
    public async Task ByteStore_WrapsAtByteBoundary() {
        await Assert.That(WrappedEqualsZero<byte>(byte.MaxValue)).IsTrue();
    }

    /// <summary><c>sbyte.MaxValue + 1</c> stored as <c>sbyte</c> is negative.</summary>
    [Test]
    public async Task SByteStore_WrapsAtSByteBoundary() {
        var s = new Variable("s");
        var program = new Block([
            new Assignment(s, new TypeCast(new Constant(sbyte.MaxValue), TypeReference.To<sbyte>())),
            new Assignment(s, new TypeCast(new Add(s, new Constant(1)), TypeReference.To<sbyte>())),
            new LessThan(s, new Constant(0)),
        ], [s]);

        using var exec = Run(program);
        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    /// <summary><c>char.MaxValue + 1</c> stored as <c>char</c> is <c>'\0'</c>.</summary>
    [Test]
    public async Task CharStore_WrapsAtCharBoundary() {
        await Assert.That(WrappedEqualsZero<char>(char.MaxValue)).IsTrue();
    }

    /// <summary>An int-backed enum uses the 32-bit local. A long-backed enum stays a word.</summary>
    [Test]
    public async Task EnumRegister_FollowsUnderlyingWidth() {
        var day = new Variable("day");
        var dayProgram = new Block([
            new Assignment(day, new Constant(DayOfWeek.Monday)),
            day,
        ], [day]);
        var dayRegister = Scan(dayProgram).Registers.Single();
        await Assert.That(dayRegister.Type).IsEqualTo(typeof(int));

        var wide = new Variable("wide");
        var wideProgram = new Block([
            new Assignment(wide, new Constant(Wide.A)),
            wide,
        ], [wide]);
        var wideRegister = Scan(wideProgram).Registers.Single();
        await Assert.That(wideRegister.Type).IsEqualTo(typeof(long));
    }

    private static bool WrappedEqualsZero<T>(T max) where T : struct {
        var v = new Variable("v");
        var program = new Block([
            new Assignment(v, new TypeCast(new Constant(max), TypeReference.To<T>())),
            new Assignment(v, new TypeCast(new Add(v, new Constant(1)), TypeReference.To<T>())),
            new Equal(v, new Constant(default(T))),
        ], [v]);
        using var exec = Run(program);
        return exec.GetValue<bool>();
    }

    private enum Wide : long { A = 1 }
}