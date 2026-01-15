namespace Poly.Introspection;

/// <summary>
/// A LIFO stack of <see cref="ITypeDefinitionProvider"/> instances that resolves types by
/// querying the most recently added provider first. Useful for layering overrides above defaults.
/// </summary>
public sealed class TypeDefinitionProviderCollection(params IEnumerable<ITypeDefinitionProvider> providers) : ITypeDefinitionProvider {
    private readonly Stack<ITypeDefinitionProvider> _providers = new(providers);

    /// <summary>
    /// Adds a provider to the top of the stack.
    /// </summary>
    public void AddProvider(ITypeDefinitionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Push(provider);
    }

    /// <summary>
    /// Removes the first matching provider instance from the stack.
    /// </summary>
    /// <returns>True if the provider was found and removed.</returns>
    public bool RemoveProvider(ITypeDefinitionProvider provider)
    {
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

    /// <summary>
    /// Removes all providers.
    /// </summary>
    public void ClearProviders()
    {
        _providers.Clear();
    }

    /// <summary>
    /// Gets the number of providers.
    /// </summary>
    public int ProviderCount => _providers.Count;

    /// <summary>
    /// Gets a snapshot of the providers in query order (top to bottom).
    /// </summary>
    public IReadOnlyList<ITypeDefinitionProvider> Providers => _providers.ToList().AsReadOnly();

    /// <summary>
    /// Resolves by name, querying providers from top to bottom. Returns null when not found.
    /// </summary>
    public ITypeDefinition? GetTypeDefinition(string name)
    {
        foreach (var provider in _providers) {
            var typeDef = provider.GetTypeDefinition(name);
            if (typeDef is not null) {
                return typeDef;
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves by runtime type, querying providers from top to bottom. Returns null when not found.
    /// </summary>
    public ITypeDefinition? GetTypeDefinition(Type type)
    {
        foreach (var provider in _providers) {
            var typeDef = provider.GetTypeDefinition(type);
            if (typeDef is not null) {
                return typeDef;
            }
        }
        return null;
    }
}