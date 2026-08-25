using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

public class LanguageSurfaceTests {
    [Test]
    public async Task ExecutableKinds_CompileWithoutNotSupported() {
        Node[] programs = [
            new Constant(1L),
            new Add(new Constant(1L), new Constant(2L)),
            new Block(new Constant(1L)),
            new IfStatement(new Constant(true), new Constant(1L)),
            new WhileLoop(new Constant(false), new Constant(0L)),
            new Default(),
            new ThisReference(),
            new Comment("ok"),
            new TypeCast(new Constant(1), TypeReference.To<long>()),
            new TypeOf(TypeReference.To<int>()),
            SameLambda(),
        ];
        foreach (var node in programs) {
            var program = Interpreter.Compile(node);
            await Assert.That(program).IsNotNull();
        }
    }

    [Test]
    public async Task CompileRejectKinds_FailLoud() {
        Node[] programs = [
            new Await(new Constant(1L)),
            new ParameterReference(),
            new CompilationUnitNode([], null, [], null),
        ];
        foreach (var node in programs) {
            await Assert.That(() => Interpreter.Compile(node)).Throws<Exception>();
        }
    }

    private static Lambda SameLambda() {
        var x = new Parameter("x", TypeReference.To<long>());
        return new Lambda([x], x);
    }
}