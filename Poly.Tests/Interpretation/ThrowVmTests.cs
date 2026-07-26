using Poly.Ast;
using Poly.Ast.Nodes;
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
        var varX = new Variable("x", new Constant(42));
        var block = new Block([new ThrowStatement(new New(TypeReference.To<Exception>())), new Return(varX)], [varX]);
        await Assert.That(() => { Interpreter.Execute(block); }).ThrowsExactly<Exception>();
    }
}