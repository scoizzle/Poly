using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

public class InterpretResultAbiTests {
    [Test]
    public async Task ScalarReturn_ReturnsValue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Constant(42L)));
        await Assert.That(exec.Result.Kind).IsEqualTo(InterpreterResult.ResultKind.Value);
        await Assert.That(exec.Result.HasValue).IsTrue();
        await Assert.That(exec.GetValue<long>()).IsEqualTo(42L);
    }

    [Test]
    public async Task BoolReturn_ReturnsZeroOrOne() {
        using var t = Interpreter.Execute(Interpreter.Compile(new Constant(true)));
        await Assert.That(t.GetValue<long>()).IsEqualTo(1L);
        using var f = Interpreter.Execute(Interpreter.Compile(new Constant(false)));
        await Assert.That(f.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task HeapStringReturn_DereferencesObject() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Constant("hello")));
        await Assert.That(exec.Result.Kind).IsEqualTo(InterpreterResult.ResultKind.Value);
        await Assert.That(exec.GetValue<string>()).IsEqualTo("hello");
    }

    [Test]
    public async Task VoidProgram_HasNoValue() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Comment("note")));
        await Assert.That(exec.Result.IsVoid).IsTrue();
        await Assert.That(exec.Result.HasValue).IsFalse();
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(0L);
    }

    [Test]
    public async Task GetValue_LongBits_AsDouble_RoundTripsIeee() {
        var bits = BitConverter.DoubleToInt64Bits(1.5);
        using var exec = Interpreter.Execute(Interpreter.Compile(new Constant(1.5)));
        await Assert.That(exec.RawValue).IsEqualTo(bits);
        await Assert.That(exec.GetValue<double>()).IsEqualTo(1.5);
    }

    [Test]
    public async Task GetValue_NullPayload_ReturnsDefault() {
        var result = InterpreterResult.FromValue(null);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.GetValue<string>()).IsNull();
    }
}
