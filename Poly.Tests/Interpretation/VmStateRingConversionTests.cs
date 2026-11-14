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

    private static T Root<T>(object? argument) {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Parameter("p", new ClrTypeReference(typeof(T)))), argument);
        return exec.GetValue<T>()!;
    }

    /// <summary>
    /// The one rule at the door: a root argument whose runtime type is not the parameter's converts where C# converts it
    /// implicitly at a call site (the platform's conversion answer, <c>ResolveConversionFrom</c>), and is refused
    /// otherwise. Before T-21a's fix round an <c>int</c> for a <c>double</c> parameter was read as IEEE bits.
    /// </summary>
    [Test]
    public async Task RootArgument_OfAnotherType_ConvertsWhereCSharpConvertsImplicitly() {
        await Assert.That(Root<long>(7)).IsEqualTo((long)7);
        await Assert.That(Root<double>(7)).IsEqualTo((double)7);
        await Assert.That(Root<double>(1.5f)).IsEqualTo((double)1.5f);
        await Assert.That(Root<decimal>(7)).IsEqualTo((decimal)7);
        await Assert.That(Root<int?>(7)).IsEqualTo((int?)7);
        // The pairs Convert.ChangeType cannot do but C# converts implicitly: char to IEEE, and to or from a native integer.
        await Assert.That(Root<float>('a')).IsEqualTo((float)'a');
        await Assert.That(Root<double>('a')).IsEqualTo((double)'a');
        await Assert.That(Root<nint>(7)).IsEqualTo((nint)7);
        await Assert.That(Root<long>((nint)7)).IsEqualTo((long)(nint)7);
        await Assert.That(Root<double>((nint)7)).IsEqualTo((double)(nint)7);
    }

    public static string Shown(object? value) => value is null ? "null" : $"{value.GetType().Name}:{value}";

    /// <summary>
    /// An untyped parameter is <c>object</c> in the analysis as well as at the door: an operator on it gets C#'s verdict
    /// for an <c>object</c> operand (<c>x == 42</c> is refused at the node), it returns and passes as the value it holds,
    /// and two of them compare by reference. Before, the analysis left it unresolved and the emitter read its handle as
    /// a number, so <c>x == 42</c> given 42 answered false.
    /// </summary>
    [Test]
    public async Task UntypedParameter_IsObject_InTheAnalysisAsAtTheDoor() {
        var refused = Interpreter.Analyze(new Equal(new Parameter("x"), new Constant(42)))
            .Diagnostics.Where(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).Select(static d => d.Message).ToArray();
        await Assert.That(refused.Length).IsEqualTo(1);
        await Assert.That(refused[0]).Contains($"'{nameof(Object)}'").And.Contains($"'{nameof(Int32)}'");
        foreach (var value in new object?[] { 42, "s", null }) {
            using var root = Interpreter.Execute(Interpreter.Compile(new Parameter("x")), value);
            await Assert.That(root.Result.Value).IsEqualTo(value);
            using var passed = Interpreter.Execute(Interpreter.Compile(Call(typeof(VmStateRingConversionTests), nameof(Shown), new Parameter("x"))), value);
            await Assert.That(passed.GetValue<string>()).IsEqualTo(Shown(value));
        }
        object same = "s";
        object left = same, right = same;
        using var equal = Interpreter.Execute(Interpreter.Compile(new Equal(new Parameter("x"), new Parameter("y"))), same, same);
        await Assert.That(equal.GetValue<bool>()).IsEqualTo(left == right);
    }

    [Test]
    [Arguments(typeof(int), 7L)]
    [Arguments(typeof(double), "1.5")]
    [Arguments(typeof(bool), 7)]
    [Arguments(typeof(decimal), 1.5)]
    [Arguments(typeof(DayOfWeek), 1)]
    public async Task RootArgument_WithNoImplicitConversion_IsRefusedNamingBothTypes(Type declared, object given) {
        var program = Interpreter.Compile(new Parameter("p", new ClrTypeReference(declared)));
        var ex = Assert.Throws<InvalidOperationException>(() => {
            using var exec = Interpreter.Execute(program, given);
        });
        await Assert.That(ex!.Message).Contains($"'{given.GetType().Name}'");
        await Assert.That(ex.Message).Contains($"'{declared.Name}'");
    }

    [Test]
    public async Task RootArgument_NullForANonNullableValueType_IsRefused() {
        var program = Interpreter.Compile(new Parameter("p", TypeReference.To<int>()));
        var ex = Assert.Throws<InvalidOperationException>(() => {
            using var exec = Interpreter.Execute(program, (object?)null);
        });
        await Assert.That(ex!.Message).Contains("null").And.Contains($"'{nameof(Int32)}'");
    }

    private static T? Coalesced<T>(T zero, T other) where T : struct => (T?)zero ?? other;

    private static async Task PresentZero<T>(T zero, T other) where T : struct {
        var cast = new TypeCast(new Parameter("x", new ClrTypeReference(typeof(T))), new ClrTypeReference(typeof(T?)));
        var fallback = new TypeCast(new Constant(other), new ClrTypeReference(typeof(T?)));
        using (var coalesce = Interpreter.Execute(Interpreter.Compile(new Coalesce(cast, fallback)), zero))
            await Assert.That(coalesce.GetValue<T>()).IsEqualTo(Coalesced(zero, other)!.Value);
        var isNull = new TypeCast(new Parameter("x", new ClrTypeReference(typeof(T))), new ClrTypeReference(typeof(T?)));
        using var compared = Interpreter.Execute(Interpreter.Compile(new Equal(isNull, new Constant(null))), zero);
        T? present = zero;
        await Assert.That(compared.GetValue<bool>()).IsEqualTo(!present.HasValue);
    }

    /// <summary><c>(T?)x</c> of a zero is a present zero, not absent: <c>??</c> keeps it and <c>== null</c> is false.
    /// The fallback is a <c>T?</c>: <c>bool? ?? bool</c> is refused by the emitter's coalesce today (row T-21b).</summary>
    [Test]
    public async Task CastToNullable_OfZero_IsPresent() {
        await PresentZero(0, 1);
        await PresentZero(0L, 1L);
        await PresentZero(false, true);
        await PresentZero(0m, 1m);
    }

    private static string Text(Node value, params object?[] args) {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Invoke(new Member(new ClrTypeReference(typeof(Convert)), nameof(Convert.ToString)), value)), args);
        return exec.GetValue<string>()!;
    }

    private static string RoundTrip<T>(T value) {
        var boxed = new TypeCast(new Parameter("x", new ClrTypeReference(typeof(T))), new ClrTypeReference(typeof(object)));
        return Text(new TypeCast(new TypeCast(boxed, new ClrTypeReference(typeof(T))), new ClrTypeReference(typeof(object))), value);
    }

    /// <summary>Box and unbox for the representations the int rows do not cover: an IEEE word, a decimal handle, an
    /// enum word, a struct handle and a temporal word. The expected text is the platform's own for the boxed value.</summary>
    [Test]
    public async Task CastToObjectAndBack_KeepsTheValue_ForEveryRepresentation() {
        await Assert.That(RoundTrip(1.5)).IsEqualTo(Convert.ToString((object)1.5));
        await Assert.That(RoundTrip(1.5m)).IsEqualTo(Convert.ToString((object)1.5m));
        await Assert.That(RoundTrip(DayOfWeek.Tuesday)).IsEqualTo(Convert.ToString((object)DayOfWeek.Tuesday));
        await Assert.That(RoundTrip(new CastPoint(3, 4))).IsEqualTo(Convert.ToString((object)new CastPoint(3, 4)));
        var when = new DateTime(2024, 2, 28, 10, 30, 0, DateTimeKind.Utc);
        await Assert.That(RoundTrip(when)).IsEqualTo(Convert.ToString((object)when));
    }
}

public readonly record struct CastPoint(int X, int Y);

public static class NullableIntProbe {
    public static int OrMinusOne(int? value) => value ?? -1;
}