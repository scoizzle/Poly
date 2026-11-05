using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #391 restored, with a producer: the C# reader keeps comments and the writer prints them, so a
/// body round-trips with its notes. The node is inert to analysis and the VM, and lowering never
/// emits one — <see cref="Comment"/> is a source comment, not a lowering-gap marker.
/// </summary>
public class CSharpCommentTests {
    private const string Source = """
        class C {
            void M() {
                // a line note
                int x = 1;
                /* a block note */
                x = 2;
                // trailing note
            }
        }
        """;

    private static Block Body(string text = Source) =>
        CSharpReader.Read(text).Types.OfType<TypeDefinitionNode>().First().Methods!
            .First(m => m.Name == "M").Body as Block
        ?? throw new InvalidOperationException("M has no block body");

    [Test]
    public async Task Read_KeepsBothCommentStyles_InSourceOrder() {
        var body = Body();
        var comments = body.Nodes.OfType<Comment>().ToList();

        await Assert.That(comments.Count).IsEqualTo(3);
        await Assert.That(comments[0].Text).IsEqualTo("a line note");
        await Assert.That(comments[0].IsBlock).IsFalse();
        await Assert.That(comments[1].Text).IsEqualTo("a block note");
        await Assert.That(comments[1].IsBlock).IsTrue();
        await Assert.That(comments[2].Text).IsEqualTo("trailing note");
    }

    /// <summary>A comment belongs to the statement that follows it, not to the block's end.</summary>
    [Test]
    public async Task Read_AttachesAComment_BeforeItsStatement() {
        var nodes = Body().Nodes.ToList();
        var comment = nodes.IndexOf(nodes.OfType<Comment>().First());
        var firstAssignment = nodes.IndexOf(nodes.OfType<Assignment>().First());

        await Assert.That(comment).IsLessThan(firstAssignment);
        await Assert.That(comment).IsEqualTo(0);
    }

    [Test]
    public async Task Write_PrintsBothStyles() {
        var written = CSharpLanguage.Write(Body());

        await Assert.That(written).Contains("// a line note");
        await Assert.That(written).Contains("/* a block note */");
        await Assert.That(written).Contains("// trailing note");
    }

    /// <summary>`Write(Read(x))` stays idempotent with comments in the body.</summary>
    [Test]
    public async Task RoundTrip_WriteReadWrite_IsStable() {
        var once = CSharpLanguage.Write(Body());
        var twice = CSharpLanguage.Write(Body("class C { void M() " + once + " }"));

        await Assert.That(twice).IsEqualTo(once);
    }

    /// <summary>A comment emits nothing and does not release the value before it.</summary>
    [Test]
    public async Task Vm_CommentIsInert_AndKeepsThePreviousValue() {
        var variable = new Variable("v");
        var block = new Block([
            new Assignment(variable, new Constant(7L)),
            new Comment("between"),
            variable,
        ], [variable]);

        using var exec = Interpreter.Execute(Interpreter.Compile(block));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);

        using var alone = Interpreter.Execute(Interpreter.Compile(new Comment("note")));
        await Assert.That(alone.Result.IsVoid).IsTrue();
    }

    /// <summary>Fail closed: a comment is not a value.</summary>
    [Test]
    public async Task Analysis_RejectsACommentAsAValue() {
        await Assert.That(() => Interpreter.Compile(new Add(new Comment("x"), new Constant(1L))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NodeFacts_RegistersTheCommentAsInert() {
        await Assert.That(NodeFacts.All.TryGetValue(typeof(Comment), out var fact)).IsTrue();
        await Assert.That(fact!.Effects).IsEqualTo(NodeEffect.None);
    }
}