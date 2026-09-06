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

    [Test]
    public async Task BreakStatement_ResultKind_IsNotBreak() {
        var node = new WhileLoop(new Constant(true), new BreakStatement());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.Kind).IsNotEqualTo(InterpreterResult.ResultKind.Break);
        await Assert.That(exec.Result.Kind is InterpreterResult.ResultKind.Void or InterpreterResult.ResultKind.Value).IsTrue();
    }

    [Test]
    public async Task ContinueStatement_CompletingLoop_ResultKind_IsNotContinue() {
        var i = new Variable("i");
        var node = new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(
                new LessThan(i, new Constant(1L)),
                new Block([
                    new Assignment(i, new Add(i, new Constant(1L))),
                    new ContinueStatement()
                ])),
            i
        ], [i]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.Kind).IsNotEqualTo(InterpreterResult.ResultKind.Continue);
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task ThrowStatement_PropagatesClrException_NotResultKindThrow() {
        var node = new ThrowStatement(new New(TypeReference.To<InvalidOperationException>(), new Constant("x")));
        await Assert.That(() => {
            using var exec = Interpreter.Execute(Interpreter.Compile(node));
            _ = exec.Result.Kind; // would be Throw if dead API were used
        }).Throws<InvalidOperationException>();
    }
}
