using System.Linq.Expressions;

using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>The frame word round-trips through <see cref="RingCodec"/>. The kind
/// and the slot view use this same pair.</summary>
public class RingCodecRoundTripTests {
    private static T RoundTrip<T>(T value) {
        var heap = Expression.Parameter(typeof(VmManagedObjectHeap), "heap");
        var encoded = RingCodec.ToFrame(Expression.Constant(value, typeof(T)), typeof(T), heap);
        var decoded = RingCodec.FromFrame(encoded, typeof(T), typeof(T), heap);
        var read = Expression.Lambda<Func<VmManagedObjectHeap, T>>(decoded, heap).Compile();
        using var store = new VmManagedObjectHeap();
        return read(store);
    }

    [Test]
    public async Task Double_RoundTrip_KeepsTheValue() {
        await Assert.That(RoundTrip(0.5d)).IsEqualTo(0.5d);
    }

    [Test]
    public async Task Float_RoundTrip_KeepsTheValue() {
        await Assert.That(RoundTrip(1.5f)).IsEqualTo(1.5f);
    }

    [Test]
    public async Task Int_PromotedToDouble_IsNumeric() {
        var heap = Expression.Parameter(typeof(VmManagedObjectHeap), "heap");
        var decoded = RingCodec.FromFrame(Expression.Constant(3L), typeof(int), typeof(double), heap);
        var read = Expression.Lambda<Func<VmManagedObjectHeap, double>>(decoded, heap).Compile();
        using var store = new VmManagedObjectHeap();
        await Assert.That(read(store)).IsEqualTo(3d);
    }

    [Test]
    public async Task DoubleBits_AreNotReadAsAnInteger() {
        var bits = BitConverter.DoubleToInt64Bits(0.5d);
        var heap = Expression.Parameter(typeof(VmManagedObjectHeap), "heap");
        var decoded = RingCodec.FromFrame(Expression.Constant(bits), typeof(double), typeof(double), heap);
        var read = Expression.Lambda<Func<VmManagedObjectHeap, double>>(decoded, heap).Compile();
        using var store = new VmManagedObjectHeap();
        await Assert.That(read(store)).IsEqualTo(0.5d);
    }

    [Test]
    public async Task ULong_RoundTrip_KeepsTheHighBit() {
        await Assert.That(RoundTrip(ulong.MaxValue)).IsEqualTo(ulong.MaxValue);
    }

    [Test]
    public async Task Bool_RoundTrip_KeepsFalse() {
        await Assert.That(RoundTrip(false)).IsFalse();
    }

    [Test]
    public async Task Decimal_RoundTrip_KeepsTheValue() {
        await Assert.That(RoundTrip(1.5m)).IsEqualTo(1.5m);
    }

    [Test]
    public async Task NullableLong_RoundTrip_KeepsPresentAndAbsent() {
        await Assert.That(RoundTrip<long?>(41L)).IsEqualTo(41L);
        await Assert.That(RoundTrip<long?>(null)).IsNull();
    }

    [Test]
    public async Task Kind_IsTheLocalType() {
        await Assert.That(LocalType.Of(typeof(int)) is LocalType.Int32Local).IsTrue();
        await Assert.That(LocalType.Of(typeof(nint)) is LocalType.Int64Local).IsTrue();
        await Assert.That(LocalType.Of(typeof(double)) is LocalType.IEEELocal).IsTrue();
        await Assert.That(LocalType.Of(typeof(bool)) is LocalType.FlagLocal).IsTrue();
        await Assert.That(LocalType.Of(typeof(decimal)) is LocalType.StructLocal).IsTrue();
        await Assert.That(LocalType.Of(typeof(int?)) is LocalType.NullableLocal).IsTrue();
        await Assert.That(LocalType.Of(typeof(void)).Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Enum_RoundTrip_KeepsTheValue() {
        await Assert.That(RoundTrip(DayOfWeek.Friday)).IsEqualTo(DayOfWeek.Friday);
    }

    [Test]
    public async Task IntPtr_StaysAnInlineWord() {
        nint value = 42;
        await Assert.That(ValueRepresentationClassifier.KindFor(typeof(nint)))
            .IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.TryInlineConstant(value, out long raw)).IsTrue();
        await Assert.That(raw).IsEqualTo(42L);
        using var heap = new VmManagedObjectHeap();
        await Assert.That(RingCodec.ToRing(value, heap)).IsEqualTo(42L);
        await Assert.That(RoundTrip(value)).IsEqualTo(value);
    }

    [Test]
    public async Task UIntPtr_StaysAnInlineWord() {
        nuint value = nuint.MaxValue;
        await Assert.That(RingCodec.TryInlineConstant(value, out long raw)).IsTrue();
        await Assert.That(raw).IsEqualTo(unchecked((long)nuint.MaxValue));
        using var heap = new VmManagedObjectHeap();
        await Assert.That(RingCodec.ToRing(value, heap)).IsEqualTo(unchecked((long)nuint.MaxValue));
        await Assert.That(RoundTrip(value)).IsEqualTo(value);
    }
}