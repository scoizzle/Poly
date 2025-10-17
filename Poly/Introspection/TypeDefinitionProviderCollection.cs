namespace Poly.Introspection;

public sealed class TypeDefinitionProviderCollection : ITypeDefinitionProvider {
    private readonly List<ITypeDefinitionProvider> _providers = new();

    public TypeDefinitionProviderCollection(params IEnumerable<ITypeDefinitionProvider> providers) {
        _providers.AddRange(providers);
    }

    public void AddProvider(ITypeDefinitionProvider provider) {
        _providers.Add(provider);
    }

    public ITypeDefinition? GetTypeDefinition(string name) {
        foreach (var provider in _providers) {
            var typeDef = provider.GetTypeDefinition(name);
            if (typeDef is not null) {
                return typeDef;
            }
        }
        return null;
    }
}