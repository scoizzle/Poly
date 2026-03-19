using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

public class AnalyzerBuilderTests {
    [Test]
    public async Task Builder_WithExplicitTypeDefinitionProvider_UsesProvider()
    {
        var provider = new TrackingTypeDefinitionProvider(ClrTypeDefinitionRegistry.Shared);
        var analyzer = new AnalyzerBuilder(provider)
            .UseTypeResolver()
            .Build();

        var node = new Constant(123);
        var result = analyzer.Analyze(node);

        await Assert.That(result.GetResolvedType(node)).IsNotNull();
        await Assert.That(provider.RequestedTypes).Contains(typeof(int));
    }

    [Test]
    public async Task Builder_WithExplicitEmptyProviderSet_DoesNotFallbackToShared()
    {
        var analyzer = new AnalyzerBuilder(Array.Empty<ITypeDefinitionProvider>())
            .UseTypeResolver()
            .Build();

        var node = new Constant(123);
        var result = analyzer.Analyze(node);

        await Assert.That(result.GetResolvedType(node)).IsNull();
    }

    private sealed class TrackingTypeDefinitionProvider(ITypeDefinitionProvider innerProvider) : ITypeDefinitionProvider {
        private readonly List<Type> _requestedTypes = new();

        public IReadOnlyList<Type> RequestedTypes => _requestedTypes;

        public ITypeDefinition? GetTypeDefinition(string name) => innerProvider.GetTypeDefinition(name);

        public ITypeDefinition? GetTypeDefinition(Type type)
        {
            _requestedTypes.Add(type);
            return innerProvider.GetTypeDefinition(type);
        }
    }
}