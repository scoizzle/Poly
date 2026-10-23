using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The two node-level surface behaviours that outlived the hand-kept inventory: a
/// <see cref="Comment"/> is a no-op statement and rejected as a value, and a
/// <see cref="ClrTypeReference"/> is not a VM value. Which producer honours which kind is
/// <see cref="NodeFacts"/>'s question, checked by <see cref="NodeFactsTotalityTests"/>.
/// </summary>
public class LanguageSurfaceTests {
    [Test]
    public async Task CommentAsExpression_IsCompileReject_StatementIsNoOp() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Comment("note")));
        await Assert.That(exec.Result.IsVoid).IsTrue();
        await Assert.That(() => Interpreter.Compile(new Add(new Comment("x"), new Constant(1L))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ClrTypeReference_AsValue_CompileRejects() {
        await Assert.That(() => Interpreter.Compile(new ClrTypeReference(typeof(string))))
            .Throws<InvalidOperationException>();
    }
}