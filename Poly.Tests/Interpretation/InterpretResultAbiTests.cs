using Poly.Interpretation;
using Poly.Syntax;
using Poly.Syntax.Nodes;

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
}