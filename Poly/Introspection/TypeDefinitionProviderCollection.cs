namespace Poly.Introspection;

public sealed class TypeDefinitionProviderCollection(params IEnumerable<ITypeDefinitionProvider> providers) : ITypeDefinitionProvider {
    private readonly Stack<ITypeDefinitionProvider> _providers = new(providers);

    public void AddProvider(ITypeDefinitionProvider provider) {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Push(provider);
    }

    public bool RemoveProvider(ITypeDefinitionProvider provider) {
        ArgumentNullException.ThrowIfNull(provider);
        if (_providers.Count == 0) return false;

        var buffer = new Stack<ITypeDefinitionProvider>(_providers.Count);
        var removed = false;

        while (_providers.Count > 0) {
            var top = _providers.Pop();
            if (!removed && ReferenceEquals(top, provider)) {
                removed = true;
                continue;
            }
            buffer.Push(top);
        }

        while (buffer.Count > 0) {
            _providers.Push(buffer.Pop());
        }

        return removed;
    }

    public void ClearProviders() {
        _providers.Clear();
    }

    public int ProviderCount => _providers.Count;

    public IReadOnlyList<ITypeDefinitionProvider> Providers => _providers.ToList().AsReadOnly();

    public ITypeDefinition? GetTypeDefinition(string name) {
        foreach (var provider in _providers) {
            var typeDef = provider.GetTypeDefinition(name);
            if (typeDef is not null) {
                return typeDef;
            }
        }
        return null;
    }
    
    public ITypeDefinition? GetTypeDefinition(Type type) {
        foreach (var provider in _providers) {
            var typeDef = provider.GetTypeDefinition(type);
            if (typeDef is not null) {
                return typeDef;
            }
        }
        return null;
    }
}