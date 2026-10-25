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
}
