namespace Poly.Introspection;

public sealed class TypeDefinitionProviderCollection(params IEnumerable<ITypeDefinitionProvider> providers) : ITypeDefinitionProvider {
    private readonly Stack<ITypeDefinitionProvider> _providers = new(providers);

    public void AddProvider(ITypeDefinitionProvider provider) {
        _providers.Push(provider);
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