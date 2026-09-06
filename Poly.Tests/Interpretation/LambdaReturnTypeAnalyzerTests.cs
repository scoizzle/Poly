using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>F10: Invoke(Lambda) / stored Invoke(Variable) resolved type is the body type.</summary>
public class LambdaReturnTypeAnalyzerTests {
    private static AnalysisResult Analyze(Node node) =>
        new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseLambdaReturnTypeResolution()
            .Build()
            .Analyze(node);

    [Test]
    public async Task Invoke_InlineLambda_ResolvedTypeIsBodyType() {
        var invoke = new Invoke(new Lambda([], new Constant(true)));
        var result = Analyze(invoke);
        var invokeType = result.GetResolvedType(invoke);
        await Assert.That(invokeType).IsNotNull();
        await Assert.That(invokeType!.GetRuntimeType()).IsEqualTo(typeof(bool));
        // Lambda node itself stays heap/function — not overwritten with body bool.
        var lambda = (Lambda)invoke.Delegate;
        var lambdaType = result.GetResolvedType(lambda);
        if (lambdaType is not null) {
            var rt = lambdaType.GetRuntimeType();
            await Assert.That(rt == typeof(bool)).IsFalse();
        }
    }

    [Test]
    public async Task Invoke_StoredLambdaVariable_ResolvedTypeIsBodyType() {
        var fn = new Variable("fn");
        var invoke = new Invoke(fn);
        var node = new Block([
            new Assignment(fn, new Lambda([], new Constant(42L))),
            invoke
        ], [fn]);
        var result = Analyze(node);
        var invokeType = result.GetResolvedType(invoke);
        await Assert.That(invokeType).IsNotNull();
        await Assert.That(invokeType!.GetRuntimeType()).IsEqualTo(typeof(long));
    }
}
