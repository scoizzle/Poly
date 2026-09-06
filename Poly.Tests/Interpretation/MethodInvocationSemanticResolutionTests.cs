using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

public class MethodInvocationSemanticResolutionTests {
    [Test]
    public async Task AnalyzeNode_MethodInvocation_ResolvesCharOverloadFromArgumentType() {
        var methodInvocation = new Invoke(new Member(Wrap("hello"), "IndexOf"), Wrap('e'));

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
        var methodInvocation = new Invoke(new Member(Wrap("hello"), "Equals"), Wrap("world"));

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
        var methodInvocation = new Invoke(new Member(Wrap("hello"), "Substring"), Wrap(1.5));

        var analysis = methodInvocation.AnalyzeNode();

        await Assert.That(analysis.GetResolvedMember(methodInvocation)).IsNull();
        await Assert.That(analysis.GetResolvedType(methodInvocation)).IsNull();
    }

    [Test]
    public async Task CompileExecute_IndexOfChar_ReturnsVmIndex() {
        var node = new Invoke(new Member(Wrap("hello"), "IndexOf"), Wrap('e'));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task CompileExecute_EqualsStringOverload_ReturnsVmBool() {
        // String-arg CLR overload sibling to IndexOf(char); IndexOf(string) currently returns -1 in VM (product).
        var node = new Invoke(new Member(Wrap("hello"), "Equals"), Wrap("hello"));
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task Analyze_SubstringDouble_NoMatch_ResolvedMemberNull_AndCompileCurrentlyAccepts() {
        var methodInvocation = new Invoke(new Member(Wrap("hello"), "Substring"), Wrap(1.5));
        var analysis = Interpreter.Analyze(methodInvocation);
        await Assert.That(analysis.GetResolvedMember(methodInvocation)).IsNull();
        // PRODUCT HOOK (F12): desired Compile-reject; current tree accepts. Characterization only.
        var program = Interpreter.Compile(methodInvocation);
        await Assert.That(program).IsNotNull();
    }
}

