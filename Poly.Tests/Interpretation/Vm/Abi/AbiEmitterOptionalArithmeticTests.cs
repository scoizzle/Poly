using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// Arithmetic with the nullable operand on the right (#330 R3). Type resolution ranks a
/// nullable by its underlying type and lifts the result when either operand is nullable,
/// so <c>1 + age</c> answers <c>long?</c> exactly like <c>age + 1</c>. A bitwise operator
/// is lifted the same way.
/// </summary>
public class AbiEmitterOptionalArithmeticTests {
    private static ExecutionResult Execute(Node body, params object?[] args) =>
        Interpreter.Execute(Interpreter.Compile(body), s => s.SetArgs(args));

    private static long RunValue(Node body, params object?[] args) {
        using var exec = Execute(body, args);
        return exec.GetValue<long>();
    }

    private static Parameter OptionalAge() =>
        new("age", new OptionalTypeReference(TypeReference.To<long>()));

    [Test]
    public async Task OptionalScalar_ValuePlusNullable_Right_UsesValue() {
        var age = OptionalAge();
        await Assert.That(RunValue(new Add(new Constant(1L), age), 41L)).IsEqualTo(42L);
    }

    [Test]
    public async Task OptionalScalar_ValuePlusNullable_Right_Absent_IsNull() {
        var age = OptionalAge();
        using var exec = Execute(new Add(new Constant(1L), age));
        await Assert.That(exec.Result.Value).IsNull();
    }

    [Test]
    public async Task OptionalScalar_ValueMinusNullable_Right_UsesValue() {
        var age = OptionalAge();
        await Assert.That(RunValue(new Subtract(new Constant(43L), age), 41L)).IsEqualTo(2L);
    }

    [Test]
    public async Task OptionalScalar_ValueTimesNullable_Right_UsesValue() {
        var age = OptionalAge();
        await Assert.That(RunValue(new Multiply(new Constant(2L), age), 21L)).IsEqualTo(42L);
    }

    [Test]
    public async Task OptionalScalar_BitwiseOr_NullableRight_UsesValue() {
        var age = OptionalAge();
        await Assert.That(RunValue(new BitwiseOr(new Constant(1L), age), 2L)).IsEqualTo(3L);
    }

    [Test]
    public async Task OptionalScalar_BitwiseOr_NullableRight_Absent_IsNull() {
        var age = OptionalAge();
        using var exec = Execute(new BitwiseOr(new Constant(1L), age));
        await Assert.That(exec.Result.Value).IsNull();
    }

    private static Parameter OptionalByte() =>
        new("b", new OptionalTypeReference(TypeReference.To<byte>()));

    private static Parameter OptionalShort() =>
        new("s", new OptionalTypeReference(TypeReference.To<short>()));

    /// <summary>C# promotes a smaller integral to <c>int</c> before the operator runs, and
    /// the nullable lift keeps the promotion: <c>byte? + byte?</c> is <c>int?</c>, so the
    /// sum is 400 and not the byte 400 truncates to.</summary>
    [Test, Timeout(10_000)]
    public async Task OptionalByte_BytePlusByte_OverflowsAsInt(CancellationToken ct) {
        _ = ct;
        var a = new Parameter("a", new OptionalTypeReference(TypeReference.To<byte>()));
        using var exec = Execute(new Add(a, OptionalByte()), (byte)200, (byte)200);
        await Assert.That(exec.Result.Value).IsEqualTo(400);
        await Assert.That(exec.Result.Value?.GetType()).IsEqualTo(typeof(int));
    }

    /// <summary>The same promotion at 16 bits: 30000 * 2 does not fit a short.</summary>
    [Test, Timeout(10_000)]
    public async Task OptionalShort_ShortTimesShort_ExceedsInt16(CancellationToken ct) {
        _ = ct;
        var a = new Parameter("a", new OptionalTypeReference(TypeReference.To<short>()));
        using var exec = Execute(new Multiply(a, OptionalShort()), (short)30000, (short)2);
        await Assert.That(exec.Result.Value).IsEqualTo(60000);
        await Assert.That(exec.Result.Value?.GetType()).IsEqualTo(typeof(int));
    }

    /// <summary>The promotion does not narrow a wider operand: <c>long + byte?</c> is
    /// <c>long?</c>.</summary>
    [Test, Timeout(10_000)]
    public async Task OptionalByte_LongPlusNullable_StaysLong(CancellationToken ct) {
        _ = ct;
        using var exec = Execute(new Add(new Constant(1L), OptionalByte()), (byte)200);
        await Assert.That(exec.Result.Value).IsEqualTo(201L);
        await Assert.That(exec.Result.Value?.GetType()).IsEqualTo(typeof(long));
    }

    [Test]
    public async Task OptionalULong_MaxDividedByTwo_UsesUnsignedValue() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<ulong>()));
        using var exec = Execute(new Divide(n, new Constant(2UL)), ulong.MaxValue);
        await Assert.That(exec.Result.Value).IsEqualTo(ulong.MaxValue / 2);
    }

    [Test]
    public async Task OptionalULong_MaxShiftedRight_UsesLogicalShift() {
        var n = new Parameter("n", new OptionalTypeReference(TypeReference.To<ulong>()));
        using var exec = Execute(new ShiftRight(n, new Constant(1)), ulong.MaxValue);
        await Assert.That(exec.Result.Value).IsEqualTo(ulong.MaxValue >> 1);
    }

    [Test]
    public async Task OptionalDecimal_Arithmetic_UsesTheValue() {
        var amount = new Parameter("amount", new OptionalTypeReference(TypeReference.To<decimal>()));
        using var present = Execute(new Add(amount, new Constant(1m)), 5m);
        await Assert.That(present.GetValue<decimal>()).IsEqualTo(6m);
        using var absent = Execute(new Add(amount, new Constant(1m)));
        await Assert.That(absent.Result.Value).IsNull();
    }
}