using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

public class ExceptionHandlingVmTests {
    [Test]
    public async Task TryCatch_TypedCatch_CatchesMatching() {
        var caught = new Variable("caught");
        var node = new Block([
            new Assignment(caught, new Constant(0L)),
            new TryCatchFinally(
                new ThrowStatement(new New(new ClrTypeReference(typeof(InvalidOperationException)))),
                CatchClauses: [
                    new CatchClause(
                        new ClrTypeReference(typeof(InvalidOperationException)),
                        "ex",
                        new Assignment(caught, new Constant(1L)))
                ]),
            caught
        ], [caught]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task TryCatch_TypedCatch_SkipsNonMatching() {
        var node = new TryCatchFinally(
            new ThrowStatement(new New(new ClrTypeReference(typeof(InvalidOperationException)))),
            CatchClauses: [
                new CatchClause(
                    new ClrTypeReference(typeof(ArgumentException)),
                    null,
                    new Constant(1L))
            ]);
        var program = Interpreter.Compile(node);
        await Assert.That(() => {
            using var exec = Interpreter.Execute(program);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TryFinally_RunsFinally() {
        var flag = new Variable("flag");
        var node = new Block([
            new Assignment(flag, new Constant(0L)),
            new TryCatchFinally(
                new Constant(1L),
                CatchClauses: null,
                FinallyBlock: new Assignment(flag, new Constant(2L))),
            flag
        ], [flag]);
        var program = Interpreter.Compile(node);
        using var exec = Interpreter.Execute(program);
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task TryCatchFinally_CatchThenFinally_BothRun() {
        var steps = new Variable("steps");
        var node = new Block([
            new Assignment(steps, new Constant(0L)),
            new TryCatchFinally(
                new ThrowStatement(new New(new ClrTypeReference(typeof(InvalidOperationException)))),
                CatchClauses: [
                    new CatchClause(
                        new ClrTypeReference(typeof(InvalidOperationException)),
                        "ex",
                        new Assignment(steps, new Add(steps, new Constant(1L))))
                ],
                FinallyBlock: new Assignment(steps, new Add(steps, new Constant(10L)))),
            steps
        ], [steps]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(11L);
    }

    [Test]
    public async Task TryCatchFinally_NoThrow_RunsFinallyOnly() {
        // Catch bodies are coerced to void in the emitter; try must also be void-typed.
        // False If → Empty else branch keeps try void while still exercising catch+finally shape.
        var steps = new Variable("steps");
        var node = new Block([
            new Assignment(steps, new Constant(0L)),
            new TryCatchFinally(
                new IfStatement(
                    new Constant(0L),
                    new ThrowStatement(new New(new ClrTypeReference(typeof(InvalidOperationException))))),
                CatchClauses: [
                    new CatchClause(
                        new ClrTypeReference(typeof(InvalidOperationException)),
                        null,
                        new Assignment(steps, new Constant(99L)))
                ],
                FinallyBlock: new Assignment(steps, new Add(steps, new Constant(10L)))),
            steps
        ], [steps]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(10L);
    }

    [Test]
    public async Task TryCatch_CatchAllUntyped_CatchesAny() {
        var caught = new Variable("caught");
        var node = new Block([
            new Assignment(caught, new Constant(0L)),
            new TryCatchFinally(
                new ThrowStatement(new New(new ClrTypeReference(typeof(InvalidOperationException)))),
                CatchClauses: [
                    new CatchClause(
                        ExceptionType: null,
                        VariableName: null,
                        Body: new Assignment(caught, new Constant(7L)))
                ]),
            caught
        ], [caught]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(7L);
    }

    /// <summary>F2: CatchClause.VariableName is bound; catch body reads message.</summary>
    [Test]
    public async Task TryCatch_CatchVariable_ReadMessage_BindsSameInstance() {
        var msg = new Variable("msg");
        var node = new Block([
            new Assignment(msg, new Constant("")),
            new TryCatchFinally(
                new ThrowStatement(new New(
                    new ClrTypeReference(typeof(InvalidOperationException)),
                    new Constant("boom-msg"))),
                CatchClauses: [
                    new CatchClause(
                        new ClrTypeReference(typeof(InvalidOperationException)),
                        "ex",
                        new Assignment(msg, new Member(new Variable("ex"), "Message")))
                ]),
            msg
        ], [msg]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.GetValue<string>()).IsEqualTo("boom-msg");
    }

    /// <summary>F18: Nested try — inner catch handles; outer does not run.</summary>
    [Test]
    public async Task NestedTry_InnerCatchHandles_OuterSkipped() {
        var steps = new Variable("steps");
        var node = new Block([
            new Assignment(steps, new Constant(0L)),
            new TryCatchFinally(
                new TryCatchFinally(
                    new ThrowStatement(new New(new ClrTypeReference(typeof(InvalidOperationException)))),
                    CatchClauses: [
                        new CatchClause(
                            new ClrTypeReference(typeof(InvalidOperationException)),
                            null,
                            new Assignment(steps, new Add(steps, new Constant(1L))))
                    ]),
                CatchClauses: [
                    new CatchClause(
                        new ClrTypeReference(typeof(InvalidOperationException)),
                        null,
                        new Assignment(steps, new Add(steps, new Constant(10L))))
                ]),
            steps
        ], [steps]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(1L);
    }

    /// <summary>F18: Throw inside catch is rethrown to outer catch.</summary>
    [Test]
    public async Task NestedTry_ThrowInsideCatch_CaughtByOuter() {
        var steps = new Variable("steps");
        var node = new Block([
            new Assignment(steps, new Constant(0L)),
            new TryCatchFinally(
                new TryCatchFinally(
                    new ThrowStatement(new New(new ClrTypeReference(typeof(InvalidOperationException)))),
                    CatchClauses: [
                        new CatchClause(
                            new ClrTypeReference(typeof(InvalidOperationException)),
                            null,
                            new ThrowStatement(new New(
                                new ClrTypeReference(typeof(ArgumentException)),
                                new Constant("from-inner-catch"))))
                    ]),
                CatchClauses: [
                    new CatchClause(
                        new ClrTypeReference(typeof(ArgumentException)),
                        null,
                        new Assignment(steps, new Constant(7L)))
                ]),
            steps
        ], [steps]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.GetValue<long>()).IsEqualTo(7L);
    }
}
