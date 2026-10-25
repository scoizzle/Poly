using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// A StackScalar slot compared with the null constant stays on the word path
/// (<c>age != null</c> on an optional value parameter). Before #330 the null
/// constant's HeapRef classification won, so the scalar word was dereferenced
/// as a heap handle.
/// </summary>
public class AbiEmitterNullScalarTests {
    [Test]
    public async Task NotEqual_StackScalarVsNull_PresentWord_IsTrue() {
        var age = new Parameter("age", TypeReference.To<long>());
        var program = Interpreter.Compile(new NotEqual(age, new Constant(null)));
        using var exec = Interpreter.Execute(program, s => s.SetArgs(41L));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task NotEqual_StackScalarVsNull_OmittedWord_IsFalse() {
        var age = new Parameter("age", TypeReference.To<long>());
        var program = Interpreter.Compile(new NotEqual(age, new Constant(null)));
        using var exec = Interpreter.Execute(program, s => s.SetArgs());
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task Equal_StackScalarVsNull_PresentWord_IsFalse() {
        var age = new Parameter("age", TypeReference.To<long>());
        var program = Interpreter.Compile(new Equal(age, new Constant(null)));
        using var exec = Interpreter.Execute(program, s => s.SetArgs(41L));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task NotEqual_NullVsStackScalar_PresentWord_IsTrue() {
        var age = new Parameter("age", TypeReference.To<long>());
        var program = Interpreter.Compile(new NotEqual(new Constant(null), age));
        using var exec = Interpreter.Execute(program, s => s.SetArgs(41L));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }
}
