using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// Equality of an optional heap value (#330 R2, R5). A nullable whose underlying value is
/// itself a heap value (<c>decimal?</c>, <c>DateTime?</c>, <c>Guid?</c>, <c>TimeSpan?</c>)
/// against the same type compares through the heap path — <c>object.Equals</c> on the
/// boxed values, where absent equals absent. Against an inline number the value is
/// unboxed and the number converts to the underlying type, so a present zero equals
/// zero and a missing value equals nothing. Lifting through the long word rounds decimal
/// and has no conversion at all for the others.
/// </summary>
public class AbiEmitterOptionalHeapNullableCompareTests {
    private static long Run(Node body, params object?[] args) {
        using var exec = Interpreter.Execute(Interpreter.Compile(body), s => s.SetArgs(args));
        return exec.RawValue;
    }

    [Test]
    public async Task OptionalDecimal_Equal_DifferentValues_IsFalse() {
        var left = new Parameter("left", new OptionalTypeReference(TypeReference.To<decimal>()));
        var right = new Parameter("right", new OptionalTypeReference(TypeReference.To<decimal>()));
        await Assert.That(Run(new Equal(left, right), 1.5m, 1.9m)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(left, right), 1.5m, 1.5m)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(left, right))).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalDateTime_Equal_UsesValues() {
        var left = new Parameter("left", new OptionalTypeReference(TypeReference.To<DateTime>()));
        var right = new Parameter("right", new OptionalTypeReference(TypeReference.To<DateTime>()));
        var day = new DateTime(2026, 1, 1);
        await Assert.That(Run(new Equal(left, right), day, day)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(left, right), day, day.AddDays(1))).IsEqualTo(0L);
        await Assert.That(Run(new Equal(left, right))).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalGuid_Equal_UsesValues() {
        var left = new Parameter("left", new OptionalTypeReference(TypeReference.To<Guid>()));
        var right = new Parameter("right", new OptionalTypeReference(TypeReference.To<Guid>()));
        var value = Guid.NewGuid();
        await Assert.That(Run(new Equal(left, right), value, value)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(left, right), value, Guid.NewGuid())).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalTimeSpan_Equal_UsesValues() {
        var left = new Parameter("left", new OptionalTypeReference(TypeReference.To<TimeSpan>()));
        var right = new Parameter("right", new OptionalTypeReference(TypeReference.To<TimeSpan>()));
        var value = TimeSpan.FromMinutes(5);
        await Assert.That(Run(new Equal(left, right), value, value)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(left, right), value, TimeSpan.FromMinutes(6))).IsEqualTo(0L);
    }

    private static Parameter OptionalDecimal() =>
        new("value", new OptionalTypeReference(TypeReference.To<decimal>()));

    /// <summary>Absent is not zero: the inline literal is a value, not a heap handle,
    /// so it can never match an empty slot.</summary>
    [Test]
    public async Task OptionalDecimal_Equal_IntLiteral_AbsentIsNotZero() {
        var value = OptionalDecimal();
        await Assert.That(Run(new Equal(value, new Constant(0)))).IsEqualTo(0L);
        await Assert.That(Run(new NotEqual(value, new Constant(0)))).IsEqualTo(1L);
    }

    /// <summary>A present decimal zero equals the inline int zero — same value, no
    /// handle read on the inline side.</summary>
    [Test]
    public async Task OptionalDecimal_Equal_IntLiteral_PresentZeroIsZero() {
        var value = OptionalDecimal();
        await Assert.That(Run(new Equal(value, new Constant(0)), 0m)).IsEqualTo(1L);
        await Assert.That(Run(new NotEqual(value, new Constant(0)), 0m)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(new Constant(0), value), 0m)).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalDecimal_Equal_IntLiteral_UsesDecimalValue() {
        var value = OptionalDecimal();
        await Assert.That(Run(new Equal(value, new Constant(5)), 5.0m)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(value, new Constant(5)), 4.5m)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(value, new Constant(6)), 5.0m)).IsEqualTo(0L);
        // A large literal must not be dereferenced as a handle: it is just a number.
        await Assert.That(Run(new Equal(value, new Constant(999_999)), 999_999m)).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalDecimal_Equal_DecimalLiteral_UsesDecimalValue() {
        var value = OptionalDecimal();
        await Assert.That(Run(new Equal(value, new Constant(5.0m)), 5.0m)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(value, new Constant(5.0m)), 4.5m)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(value, new Constant(5.0m)))).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalDecimal_Equal_OptionalInt_ConvertsToDecimal() {
        var value = OptionalDecimal();
        var count = new Parameter("count", new OptionalTypeReference(TypeReference.To<int>()));
        await Assert.That(Run(new Equal(value, count), 5.0m, 5)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(value, count), 5.0m, 6)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(value, count), 5.0m)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(value, count), null, 5)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(value, count))).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalDecimal_Equal_Null_SeparatesAbsentFromPresent() {
        var value = OptionalDecimal();
        await Assert.That(Run(new Equal(value, new Constant(null)))).IsEqualTo(1L);
        await Assert.That(Run(new NotEqual(value, new Constant(null)))).IsEqualTo(0L);
        await Assert.That(Run(new Equal(value, new Constant(null)), 0m)).IsEqualTo(0L);
        await Assert.That(Run(new NotEqual(value, new Constant(null)), 0m)).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalDecimal_Equal_RequiredDecimal_UsesValues() {
        var optional = OptionalDecimal();
        var required = new Parameter("required", TypeReference.To<decimal>());
        await Assert.That(Run(new Equal(optional, required), 5.0m, 5.0m)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(optional, required), 5.0m, 4.0m)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(optional, required), null, 5.0m)).IsEqualTo(0L);
        await Assert.That(Run(new Equal(required, optional), 5.0m, 5.0m)).IsEqualTo(1L);
    }

    /// <summary>The same shape without the optional wrapper: a required decimal is a
    /// heap handle too, so an inline number must still compare by value.</summary>
    [Test]
    public async Task Decimal_Equal_IntLiteral_UsesDecimalValue() {
        var value = new Parameter("value", TypeReference.To<decimal>());
        await Assert.That(Run(new Equal(value, new Constant(5)), 5m)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(new Constant(5), value), 5m)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(value, new Constant(5)), 4.5m)).IsEqualTo(0L);
        await Assert.That(Run(new NotEqual(value, new Constant(5)), 4.5m)).IsEqualTo(1L);
    }
}
