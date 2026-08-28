using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Tests.TestHelpers;

using Expr = System.Linq.Expressions.Expression;

namespace Poly.Tests.Interpretation;

public class NewNodeTests {
    [Test]
    public async Task AnalyzeNode_New_ResolvesParameterlessConstructor() {
        var node = new New(TypeReference.To<Widget>());

        var analysis = node.AnalyzeNode();
        var resolvedConstructor = analysis.GetResolvedMember(node) as ITypeConstructor;
        var resolvedType = analysis.GetResolvedType(node);

        await Assert.That(resolvedConstructor).IsNotNull();
        await Assert.That(resolvedConstructor!.Parameters).IsEmpty();
        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.GetRuntimeType()).IsEqualTo(typeof(Widget));
    }

    [Test]
    public async Task AnalyzeNode_New_ResolvesBestMatchingOverload() {
        var node = new New(TypeReference.To<Widget>(), Wrap("alpha"));

        var analysis = node.AnalyzeNode();
        var resolvedConstructor = analysis.GetResolvedMember(node) as ITypeConstructor;

        await Assert.That(resolvedConstructor).IsNotNull();
        await Assert.That(resolvedConstructor!.Parameters.Count()).IsEqualTo(2);
        await Assert.That(resolvedConstructor.Parameters.First().ParameterTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(string));
        await Assert.That(resolvedConstructor.Parameters.Last().IsOptional).IsTrue();
    }

    [Test]
    public async Task New_ParameterlessConstructor_CompilesAndCreatesInstance() {
        var node = new New(TypeReference.To<Widget>());

        var expression = node.BuildExpression();
        var compiled = Expr.Lambda<Func<Widget>>(expression).Compile();
        var result = compiled();

        await Assert.That(result.Name).IsEqualTo("default");
        await Assert.That(result.Count).IsEqualTo(-1);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        var vmResult = exec.GetValue<Widget>();
        await Assert.That(vmResult).IsNotNull();
        await Assert.That(vmResult!.Name).IsEqualTo("default");
        await Assert.That(vmResult.Count).IsEqualTo(-1);
    }

    [Test]
    public async Task New_WithOptionalParameter_UsesDefaultValueWhenOmitted() {
        var node = new New(TypeReference.To<Widget>(), Wrap("alpha"));

        var expression = node.BuildExpression();
        var compiled = Expr.Lambda<Func<Widget>>(expression).Compile();
        var result = compiled();

        await Assert.That(result.Name).IsEqualTo("alpha");
        await Assert.That(result.Count).IsEqualTo(0);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        var vmResult = exec.GetValue<Widget>();
        await Assert.That(vmResult).IsNotNull();
        await Assert.That(vmResult!.Name).IsEqualTo("alpha");
        await Assert.That(vmResult.Count).IsEqualTo(0);
    }

    [Test]
    public async Task New_WithExplicitArguments_BindsAllConstructorParameters() {
        var node = new New(TypeReference.To<Widget>(), Wrap("beta"), Wrap(42));

        var expression = node.BuildExpression();
        var compiled = Expr.Lambda<Func<Widget>>(expression).Compile();
        var result = compiled();

        await Assert.That(result.Name).IsEqualTo("beta");
        await Assert.That(result.Count).IsEqualTo(42);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        var vmResult = exec.GetValue<Widget>();
        await Assert.That(vmResult).IsNotNull();
        await Assert.That(vmResult!.Name).IsEqualTo("beta");
        await Assert.That(vmResult.Count).IsEqualTo(42);
    }

    [Test]
    public async Task New_AllOptionalCtor_ZeroArgs_AppliesDefaults() {
        var node = new New(TypeReference.To<AllOptional>());
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        var vmResult = exec.GetValue<AllOptional>();
        await Assert.That(vmResult).IsNotNull();
        await Assert.That(vmResult!.N).IsEqualTo(5);
    }

    [Test]
    public async Task New_NoMatchingConstructor_CompileRejected() {
        await Assert.That(() => Interpreter.Compile(new New(TypeReference.To<NeedsArg>())))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("constructor");
    }

    [Test]
    public async Task New_ToString_UsesSourceLikeSyntax() {
        var node = new New(TypeReference.To<Widget>(), Wrap("gamma"), Wrap(7));

        await Assert.That(node.ToString()).Contains("new");
        await Assert.That(node.ToString().Contains("System.String")).IsFalse();
        await Assert.That(node.ToString()).Contains("Poly.Tests.Interpretation.NewNodeTests+Widget");
    }

    private sealed class Widget {
        public Widget() {
            Name = "default";
            Count = -1;
        }

        public Widget(string name, int count = 0) {
            Name = name;
            Count = count;
        }

        public string Name { get; }

        public int Count { get; }
    }

    private sealed class AllOptional {
        public AllOptional(int n = 5) => N = n;
        public int N { get; }
    }

    private sealed class NeedsArg {
        public NeedsArg(int x) => X = x;
        public int X { get; }
    }
}