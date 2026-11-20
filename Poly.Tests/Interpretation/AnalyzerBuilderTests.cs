using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

public class AnalyzerBuilderTests {
    [Test]
    public async Task Builder_WithRegisteredSource_ResolvesItsNames() {
        var definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(Uri));
        var source = new NamedSource("Custom", definition);
        var node = new TypeReference("Custom");
        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver().Build()
            .Analyze(node, setup: context => context.AddSource(source));

        await Assert.That(result.GetResolvedType(node)).IsSameReferenceAs(definition);
    }

    [Test]
    public async Task Builder_WithEmptySource_StillFallsBackToTheCSharpTarget() {
        // An own source that answers nothing does not hide the target: C# over the CLR still resolves Int32.
        var node = new Constant(123);
        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver().Build()
            .Analyze(node, setup: context => context.AddSource(new NamedSource("Unused", definition: null)));

        await Assert.That(result.GetResolvedType(node)).IsNotNull();
        await Assert.That(result.GetResolvedType(node)!.FullName).IsEqualTo("System.Int32");
    }

    /// <summary>A source the analysis registers as its own, answering exactly one name (#998 T12b).</summary>
    private sealed class NamedSource(string name, ITypeDefinition? definition) : IQualifiedNameSource {
        public ITypeDefinition? ByQualifiedName(string qualifiedName) =>
            string.Equals(qualifiedName, name, StringComparison.Ordinal) ? definition : null;

        public IEnumerable<ITypeDefinition> TypesIn(string? @namespace) => [];
    }
}