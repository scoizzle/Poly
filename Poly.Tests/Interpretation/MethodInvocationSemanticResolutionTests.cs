using System.Text;
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
    public async Task Analyze_SubstringDouble_NoMatch_ResolvedMemberNull_AndCompileRejects() {
        var methodInvocation = new Invoke(new Member(Wrap("hello"), "Substring"), Wrap(1.5));
        var analysis = Interpreter.Analyze(methodInvocation);
        await Assert.That(analysis.GetResolvedMember(methodInvocation)).IsNull();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("no matching member", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(() => Interpreter.Compile(methodInvocation))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("no matching member");
    }

    [Test]
    public async Task Analyze_MakeRelativeUri_StringBuilder_NoMatch_ResolvedMemberNull_AndCompileRejects() {
        // F25: TypeCode.Object==Object must not treat Uri.MakeRelativeUri(StringBuilder) as a match.
        var uri = Wrap(new Uri("https://example.com/a"));
        var methodInvocation = new Invoke(new Member(uri, "MakeRelativeUri"), Wrap(new StringBuilder("x")));
        var analysis = Interpreter.Analyze(methodInvocation);
        await Assert.That(analysis.GetResolvedMember(methodInvocation)).IsNull();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("no matching member", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(() => Interpreter.Compile(methodInvocation))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("no matching member");
    }

    [Test]
    public async Task Analyze_DateTimeAddDays_Long_NumericWidening_CompileAccepts() {
        // F25 sibling: long→double stays plausible (rank 4≤6) even when overload scorer leaves ResolvedMember null.
        var methodInvocation = new Invoke(new Member(Wrap(new DateTime(2026, 1, 1)), "AddDays"), Wrap(1L));
        var analysis = Interpreter.Analyze(methodInvocation);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("no matching member", StringComparison.OrdinalIgnoreCase))).IsFalse();
        using var exec = Interpreter.Execute(Interpreter.Compile(methodInvocation));
        await Assert.That(exec.GetValue<DateTime>()).IsEqualTo(new DateTime(2026, 1, 2));
    }
}
