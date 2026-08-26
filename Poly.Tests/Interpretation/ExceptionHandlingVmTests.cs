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
}