using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// Equality of an optional heap value (#330 R2). A nullable whose underlying value is
/// itself a heap value (<c>decimal?</c>, <c>DateTime?</c>, <c>Guid?</c>, <c>TimeSpan?</c>)
/// compares through the heap path — <c>object.Equals</c> on the boxed values, where
/// absent equals absent. Lifting it through the long word rounds decimal and has no
/// conversion at all for the others.
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
}
