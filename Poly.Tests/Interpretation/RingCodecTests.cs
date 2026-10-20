using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

/// <summary>Direct oracles for <see cref="RingCodec"/> — how a value is classified
/// into and converted to the VM's long ring slot.</summary>
public class RingCodecTests {
    [Test]
    public async Task KindFor_IntegerPrimitives_AreStackScalars() {
        await Assert.That(RingCodec.KindFor(typeof(long))).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.KindFor(typeof(int))).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.KindFor(typeof(short))).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.KindFor(typeof(byte))).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.KindFor(typeof(sbyte))).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.KindFor(typeof(ushort))).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.KindFor(typeof(uint))).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.KindFor(typeof(ulong))).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.KindFor(typeof(char))).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task KindFor_Bool_IsBool() {
        await Assert.That(RingCodec.KindFor(typeof(bool))).IsEqualTo(ValueRepresentationKind.Bool);
    }

    [Test]
    public async Task KindFor_FloatAndDouble_AreStackScalarsAndIeee() {
        await Assert.That(RingCodec.KindFor(typeof(float))).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.KindFor(typeof(double))).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.IsIeee(typeof(float))).IsTrue();
        await Assert.That(RingCodec.IsIeee(typeof(double))).IsTrue();
        await Assert.That(RingCodec.IsIeee(typeof(int))).IsFalse();
    }

    [Test]
    public async Task KindFor_EnumAndNullableEnum_AreStackScalars() {
        await Assert.That(RingCodec.KindFor(typeof(DayOfWeek))).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.KindFor(typeof(DayOfWeek?))).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task KindFor_NullableInt_IsStackScalar() {
        await Assert.That(RingCodec.KindFor(typeof(int?))).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task KindFor_HeapResidentValueTypes_AreHeapRefs() {
        await Assert.That(RingCodec.KindFor(typeof(decimal))).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(RingCodec.KindFor(typeof(DateTime))).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(RingCodec.KindFor(typeof(DateOnly))).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(RingCodec.KindFor(typeof(TimeOnly))).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(RingCodec.KindFor(typeof(TimeSpan))).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(RingCodec.KindFor(typeof(Guid))).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task KindFor_ReferenceTypes_AreHeapRefs() {
        await Assert.That(RingCodec.KindFor(typeof(string))).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(RingCodec.KindFor(typeof(object))).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(RingCodec.KindFor(typeof(int[]))).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task KindFor_Constant_MatchesStoredSlot() {
        await Assert.That(RingCodec.KindFor((object?)true)).IsEqualTo(ValueRepresentationKind.Bool);
        await Assert.That(RingCodec.KindFor((object?)1.5)).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(RingCodec.KindFor((object?)7L)).IsEqualTo(ValueRepresentationKind.StackScalar);
        // An enum constant is boxed by the emitter, not folded to an inline slot.
        await Assert.That(RingCodec.KindFor((object?)DayOfWeek.Monday)).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(RingCodec.KindFor((object?)null)).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task TryInlineConstant_ScalarsAndIeeeBits_AreFolded() {
        await Assert.That(RingCodec.TryInlineConstant(null, out long nul)).IsTrue();
        await Assert.That(nul).IsEqualTo(0L);

        await Assert.That(RingCodec.TryInlineConstant(true, out long t)).IsTrue();
        await Assert.That(t).IsEqualTo(1L);

        await Assert.That(RingCodec.TryInlineConstant(1.5, out long dbl)).IsTrue();
        await Assert.That(dbl).IsEqualTo(BitConverter.DoubleToInt64Bits(1.5));

        await Assert.That(RingCodec.TryInlineConstant(ulong.MaxValue, out long ul)).IsTrue();
        await Assert.That(ul).IsEqualTo(unchecked((long)ulong.MaxValue));

        await Assert.That(RingCodec.TryInlineConstant(DayOfWeek.Monday, out _)).IsFalse();
        await Assert.That(RingCodec.TryInlineConstant("x", out _)).IsFalse();
    }

    [Test]
    public async Task IsInvokeArgumentScalar_StringConcatAdd_StaysScalarBoxed() {
        var add = new Add(new Constant("a"), new Constant("b"));
        var analysis = Interpreter.Analyze(add);
        await Assert.That(analysis.GetValueRepresentation(add)).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(RingCodec.IsInvokeArgumentScalar(add, analysis)).IsTrue();
    }

    [Test]
    public async Task IsInvokeArgumentScalar_PlainReferenceConstant_IsFalse() {
        await Assert.That(RingCodec.IsInvokeArgumentScalar(new Constant("a"), null)).IsFalse();
    }
}