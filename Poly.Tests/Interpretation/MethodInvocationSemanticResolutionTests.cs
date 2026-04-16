using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

public class MethodInvocationSemanticResolutionTests {
    [Test]
    public async Task AnalyzeNode_MethodInvocation_ResolvesCharOverloadFromArgumentType() {
        var methodInvocation = new Invoke(new MemberAccess(Wrap("hello"), "IndexOf"), Wrap('e'));

        var analysis = methodInvocation.AnalyzeNode();
        var resolvedMethod = analysis.GetResolvedMember(methodInvocation) as ITypeMethod;
        var resolvedType = analysis.GetResolvedType(methodInvocation);

        await Assert.That(resolvedMethod).IsNotNull();
        await Assert.That(resolvedMethod!.Parameters.Count()).IsEqualTo(1);
        await Assert.That(resolvedMethod.Parameters.First().ParameterTypeDefinition.FullName).IsEqualTo("System.Char");
        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.FullName).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task AnalyzeNode_MethodInvocation_PrefersExactOverAssignableOverload() {
        var methodInvocation = new Invoke(new MemberAccess(Wrap("hello"), "Equals"), Wrap("world"));

        var analysis = methodInvocation.AnalyzeNode();
        var resolvedMethod = analysis.GetResolvedMember(methodInvocation) as ITypeMethod;
        var resolvedType = analysis.GetResolvedType(methodInvocation);

        await Assert.That(resolvedMethod).IsNotNull();
        await Assert.That(resolvedMethod!.Parameters.Count()).IsEqualTo(1);
        await Assert.That(resolvedMethod.Parameters.First().ParameterTypeDefinition.FullName).IsEqualTo("System.String");
        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.FullName).IsEqualTo("System.Boolean");
    }

    [Test]
    public async Task AnalyzeNode_MethodInvocation_DoesNotResolveWhenNoOverloadMatches() {
        var methodInvocation = new Invoke(new MemberAccess(Wrap("hello"), "Substring"), Wrap(1.5));

        var analysis = methodInvocation.AnalyzeNode();

        await Assert.That(analysis.GetResolvedMember(methodInvocation)).IsNull();
        await Assert.That(analysis.GetResolvedType(methodInvocation)).IsNull();
    }
}