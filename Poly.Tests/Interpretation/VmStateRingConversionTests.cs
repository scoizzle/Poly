using System.Runtime.CompilerServices;

using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Ring-slot conversion (<see cref="LocalType.Write(object?, Type, VmManagedObjectHeap)"/>) for the narrow integer kinds, and the frame word a root argument
/// gets from its parameter's declared type (issue 881): a scalar written where the declared type is a reference is a
/// handle to the boxed value, never the scalar's own inline word.
/// </summary>
public class VmStateRingConversionTests {
    [Test]
    public async Task ToRing_NarrowIntegers_WidenToLong() {
        using var heap = new VmManagedObjectHeap();
        await Assert.That(LocalType.Write((byte)200, typeof(byte), heap)).IsEqualTo(200L);
        await Assert.That(LocalType.Write((sbyte)-1, typeof(sbyte), heap)).IsEqualTo(-1L);
        await Assert.That(LocalType.Write((short)300, typeof(short), heap)).IsEqualTo(300L);
        await Assert.That(LocalType.Write((ushort)40000, typeof(ushort), heap)).IsEqualTo(40000L);
    }

    private static Parameter ObjectParameter(string name) => new(name, new ClrTypeReference(typeof(object)));

    private static Invoke Call(Type type, string method, Node argument) =>
        new(new Member(new ClrTypeReference(type), method), argument);

    [Test]
    [Arguments(7, "7")]
    [Arguments(7L, "7")]
    [Arguments(true, "True")]
    [Arguments("x", "x")]
    public async Task ObjectParameter_HoldingAValue_ReachesACallAsThatValue(object argument, string expected) {
        var a = ObjectParameter("a");
        using (var text = Interpreter.Execute(Interpreter.Compile(Call(typeof(Convert), nameof(Convert.ToString), a)), argument))
            await Assert.That(text.GetValue<string>()).IsEqualTo(expected);
        using (var hash = Interpreter.Execute(Interpreter.Compile(Call(typeof(RuntimeHelpers), nameof(RuntimeHelpers.GetHashCode), a)), argument))
            await Assert.That(hash.GetValue<int>()).IsNotEqualTo(0);
        using (var root = Interpreter.Execute(Interpreter.Compile(a), argument))
            await Assert.That(root.Result.Value).IsEqualTo(argument);
    }

    [Test]
    [Arguments(7, "7")]
    [Arguments(7L, "7")]
    [Arguments(true, "True")]
    [Arguments("x", "x")]
    public async Task CastToObject_OfATypedParameter_ReachesACallAsThatValue(object argument, string expected) {
        var x = new Parameter("x", new ClrTypeReference(argument.GetType()));
        var cast = new TypeCast(x, new ClrTypeReference(typeof(object)));
        using var text = Interpreter.Execute(Interpreter.Compile(Call(typeof(Convert), nameof(Convert.ToString), cast)), argument);
        await Assert.That(text.GetValue<string>()).IsEqualTo(expected);
    }

    /// <summary>The way back: <c>(T)o</c> of an object holding a <c>T</c> is that value, not the handle's number.</summary>
    [Test]
    [Arguments(7)]
    [Arguments(7L)]
    [Arguments(true)]
    [Arguments("x")]
    public async Task CastFromObject_IsTheValueItHolds(object argument) {
        var unbox = new TypeCast(ObjectParameter("o"), new ClrTypeReference(argument.GetType()));
        using var exec = Interpreter.Execute(Interpreter.Compile(new Equal(unbox, new Constant(argument))), argument);
        await Assert.That(exec.GetValue<bool>()).IsTrue();
    }

    /// <summary>A constant boxed and unboxed in one tree, and a date (a word that is not a number) boxed for a call.</summary>
    [Test]
    public async Task CastToObject_ConstantAndDate_RoundTrip() {
        var boxed = new TypeCast(new Constant(7), new ClrTypeReference(typeof(object)));
        using (var text = Interpreter.Execute(Interpreter.Compile(Call(typeof(Convert), nameof(Convert.ToString), boxed))))
            await Assert.That(text.GetValue<string>()).IsEqualTo("7");
        using (var back = Interpreter.Execute(Interpreter.Compile(new TypeCast(boxed, TypeReference.To<int>()))))
            await Assert.That(back.GetValue<int>()).IsEqualTo(7);
        var date = new Parameter("d", TypeReference.To<DateOnly>());
        var dateText = Call(typeof(Convert), nameof(Convert.ToString), new TypeCast(date, new ClrTypeReference(typeof(object))));
        using (var text = Interpreter.Execute(Interpreter.Compile(dateText), new DateOnly(2024, 2, 28)))
            await Assert.That(text.GetValue<string>()).IsEqualTo(Convert.ToString((object)new DateOnly(2024, 2, 28)));
    }

    /// <summary><c>(int?)x</c> of an <c>int</c> is a present optional holding the value.</summary>
    [Test]
    public async Task CastToNullable_OfAnInt_IsPresentWithTheValue() {
        var cast = new TypeCast(new Parameter("x", TypeReference.To<int>()), TypeReference.To<int?>());
        using (var root = Interpreter.Execute(Interpreter.Compile(cast), 7))
            await Assert.That(root.Result.Value).IsEqualTo((object)7);
        using var passed = Interpreter.Execute(Interpreter.Compile(Call(typeof(NullableIntProbe), nameof(NullableIntProbe.OrMinusOne), cast)), 7);
        await Assert.That(passed.GetValue<int>()).IsEqualTo(7);
    }
}

public static class NullableIntProbe {
    public static int OrMinusOne(int? value) => value ?? -1;
}