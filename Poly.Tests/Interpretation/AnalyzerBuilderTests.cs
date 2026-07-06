using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

public class AnalyzerBuilderTests {
    [Test]
    public async Task Builder_WithExplicitTypeDefinitionProvider_UsesProvider() {
        var provider = new TrackingTypeDefinitionProvider(ClrTypeDefinitionRegistry.Shared);
        var node = new Constant(123);
        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver().Build().Analyze(node, typeDefinitions: provider);

        await Assert.That(result.GetResolvedType(node)).IsNotNull();
        await Assert.That(provider.RequestedTypes).Contains(typeof(int));
    }

    [Test]
    public async Task Builder_WithExplicitEmptyProviderSet_DoesNotFallbackToShared() {
        var emptyProvider = new NoOpTypeDefinitionProvider();
        var node = new Constant(123);
        var result = new AnalyzerBuilder().UseThisReferenceContext()
            .UseTypeAndMemberResolver().Build().Analyze(node, typeDefinitions: emptyProvider);

        await Assert.That(result.GetResolvedType(node)).IsNull();
    }

    private sealed class NoOpTypeDefinitionProvider : ITypeDefinitionProvider {
        public ITypeDefinition? GetTypeDefinition(string name) => null;
        public ITypeDefinition? GetTypeDefinition(Type type) => null;
    }

    private sealed class TrackingTypeDefinitionProvider(ITypeDefinitionProvider innerProvider) : ITypeDefinitionProvider {
        private readonly List<Type> _requestedTypes = new();

        public IReadOnlyList<Type> RequestedTypes => _requestedTypes;

        public ITypeDefinition? GetTypeDefinition(string name) => innerProvider.GetTypeDefinition(name);

        public ITypeDefinition? GetTypeDefinition(Type type) {
            _requestedTypes.Add(type);
            return innerProvider.GetTypeDefinition(type);
        }
    }
}