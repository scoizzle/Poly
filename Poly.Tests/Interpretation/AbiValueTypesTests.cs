using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>Direct oracles for <see cref="AbiValueTypes"/> — ring-inline vs heap-resident CLR types.</summary>
public class AbiValueTypesTests {
    [Test]
    public async Task IsLongRepresentable_IntegerPrimitives_AreTrue() {
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(long))).IsTrue();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(int))).IsTrue();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(short))).IsTrue();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(byte))).IsTrue();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(sbyte))).IsTrue();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(ushort))).IsTrue();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(uint))).IsTrue();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(ulong))).IsTrue();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(char))).IsTrue();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(bool))).IsTrue();
    }

    [Test]
    public async Task IsLongRepresentable_FloatAndDouble_AreTrue() {
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(float))).IsTrue();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(double))).IsTrue();
    }

    [Test]
    public async Task IsLongRepresentable_EnumAndNullableEnum_AreTrue() {
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(DayOfWeek))).IsTrue();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(DayOfWeek?))).IsTrue();
    }

    [Test]
    public async Task IsLongRepresentable_NullableInt_IsTrue() {
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(int?))).IsTrue();
    }

    [Test]
    public async Task IsLongRepresentable_HeapResidentValueTypes_AreFalse() {
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(decimal))).IsFalse();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(DateTime))).IsFalse();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(DateOnly))).IsFalse();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(TimeOnly))).IsFalse();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(TimeSpan))).IsFalse();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(Guid))).IsFalse();
    }

    [Test]
    public async Task IsLongRepresentable_ReferenceTypes_AreFalse() {
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(string))).IsFalse();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(object))).IsFalse();
        await Assert.That(AbiValueTypes.IsLongRepresentable(typeof(int[]))).IsFalse();
    }
}
