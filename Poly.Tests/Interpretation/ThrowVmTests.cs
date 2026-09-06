using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

public class ThrowVmTests {
    [Test]
    public async Task Throw_Unprotected_PropagatesClrException() {
        var throwStmt = new ThrowStatement(
            new New(TypeReference.To<Exception>()));
        await Assert.That(() => { Interpreter.Execute(throwStmt); }).ThrowsExactly<Exception>();
    }

    [Test]
    public async Task Throw_WithMessage_PropagatesCorrectMessage() {
        var throwStmt = new ThrowStatement(
            new New(TypeReference.To<InvalidOperationException>(), new Constant("test message")));
        bool caught = false;
        try { Interpreter.Execute(throwStmt); }
        catch (InvalidOperationException ioex) { caught = ioex.Message == "test message"; }
        await Assert.That(caught).IsTrue();
    }

    [Test]
    public async Task Throw_InVoidContext_Propagates() {
        var block = new Block(new ThrowStatement(new New(TypeReference.To<Exception>())));
        await Assert.That(() => { Interpreter.Execute(block); }).ThrowsExactly<Exception>();
    }

    [Test]
    public async Task Throw_AfterLocal_TerminatesExecution() {
        var varX = new Variable("x");
        var block = new Block([
            new Assignment(varX, new Constant(42)),
            new ThrowStatement(new New(TypeReference.To<Exception>())),
            new Return(varX)
        ], [varX]);
        await Assert.That(() => { Interpreter.Execute(block); }).ThrowsExactly<Exception>();
    }

    /// <summary>
    /// F1 product gap: DirectVmAbiEmitter discards non-New Throw operands and throws a fresh Exception().
    /// Desired oracle (same-instance propagate) is blocked until product changes — assert current loud discard.
    /// </summary>
    [Test]
    public async Task Throw_ConstantExceptionInstance_DiscardsOperand_ThrowsFreshException() {
        var expected = new InvalidOperationException("constant-ex");
        var throwStmt = new ThrowStatement(new Constant(expected));
        Exception? caught = null;
        try { Interpreter.Execute(throwStmt); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught).IsNotSameReferenceAs(expected);
        await Assert.That(caught!.GetType()).IsEqualTo(typeof(Exception));
    }

    [Test]
    public async Task ThrowExpression_ConstantExceptionInstance_DiscardsOperand_ThrowsFreshException() {
        var expected = new InvalidOperationException("expr-ex");
        var node = new ThrowExpression(new Constant(expected));
        Exception? caught = null;
        try { Interpreter.Execute(Interpreter.Compile(node)); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught).IsNotSameReferenceAs(expected);
        await Assert.That(caught!.GetType()).IsEqualTo(typeof(Exception));
    }

    [Test]
    public async Task Throw_VariableHoldingException_DiscardsOperand_ThrowsFreshException() {
        var expected = new InvalidOperationException("var-ex");
        var e = new Variable("e");
        var block = new Block([
            new Assignment(e, new Constant(expected)),
            new ThrowStatement(e)
        ], [e]);
        Exception? caught = null;
        try { Interpreter.Execute(Interpreter.Compile(block)); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNotNull();
        await Assert.That(caught).IsNotSameReferenceAs(expected);
        await Assert.That(caught!.GetType()).IsEqualTo(typeof(Exception));
    }
}
