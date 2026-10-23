namespace Poly.Tests.Interpretation;

public class AnyChildTests {
    private sealed class NoOpAnalyzer : INodeAnalyzer {
        public void Analyze(AnalysisContext context, Node node) { }

        public string PassName => nameof(NoOpAnalyzer);
    }

    private sealed record AnyChildTestMetadata : IAnalysisMetadata;

    [Test]
    public async Task AnyChild_DirectChild_ReturnsTrue() {
        var root = new Block(new ThrowStatement(new Variable("ex")));

        await Assert.That(Finds<ThrowStatement>(root)).IsTrue();
    }

    [Test]
    public async Task AnyChild_DeepDescendant_ReturnsTrue() {
        var root = new Block(
            new IfStatement(
                new Variable("c"),
                new Block(new Block(new ThrowStatement(new Variable("ex"))))));

        await Assert.That(Finds<ThrowStatement>(root)).IsTrue();
    }

    [Test]
    public async Task AnyChild_NoMatch_ReturnsFalse() {
        var root = new Block(new IfStatement(new Variable("c"), new Block(new Constant(1L))));

        await Assert.That(Finds<ThrowStatement>(root)).IsFalse();
    }

    [Test]
    public async Task AnyChild_RootItself_IsNotMatched() {
        var root = new ThrowStatement(new Variable("ex"));

        await Assert.That(Finds<ThrowStatement>(root)).IsFalse();
    }

    [Test]
    public async Task AnyChild_LambdaNode_IsMatchedButItsBodyIsABarrier() {
        var root = new Assignment(new Variable("f"), new Lambda([], new ThrowStatement(new Variable("ex"))));

        await Assert.That(Finds<Lambda>(root)).IsTrue();
        await Assert.That(Finds<ThrowStatement>(root)).IsFalse();
    }

    [Test]
    public async Task AnyChild_TypeDeclarationNode_IsMatchedButItsBodyIsABarrier() {
        var typeDef = new TypeDefinitionNode(
            "C",
            Methods: [
                new MethodDefinitionNode(
                    "M",
                    TypeReference.To<object>(),
                    Body: new Block(new ThrowStatement(new Variable("ex"))))
            ]);
        var root = new CompilationUnitNode([], null, [typeDef], null);

        await Assert.That(Finds<TypeDefinitionNode>(root)).IsTrue();
        await Assert.That(Finds<ThrowStatement>(root)).IsFalse();
    }

    private static bool Finds<TNode>(Node root) where TNode : Node =>
        new NoOpAnalyzer().AnyChild<AnyChildTestMetadata>(
            AnalysisContext.CreateDefault(),
            root,
            static (_, node) => node is TNode);
}