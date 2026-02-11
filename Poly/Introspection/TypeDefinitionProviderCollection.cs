namespace Poly.Introspection;

/// <summary>
/// A LIFO stack of <see cref="ITypeDefinitionProvider"/> instances that resolves types by
/// querying the most recently added provider first. Useful for layering overrides above defaults.
/// </summary>
public sealed class TypeDefinitionProviderCollection(params IEnumerable<ITypeDefinitionProvider> providers) : ITypeDefinitionProvider, ICollection<ITypeDefinitionProvider> {
    private readonly List<ITypeDefinitionProvider> _providers = [.. providers];

    /// <summary>
    /// Adds a provider to the top of the stack.
    /// </summary>
    public void Add(ITypeDefinitionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Insert(0, provider);
    }

    /// <summary>
    /// Removes the first matching provider instance from the stack.
    /// </summary>
    /// <returns>True if the provider was found and removed.</returns>
    public bool Remove(ITypeDefinitionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return _providers.Remove(provider);
    }

    /// <summary>
    /// Removes all providers.
    /// </summary>
    public void Clear()
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
    public IReadOnlyList<ITypeDefinitionProvider> Providers => _providers.AsReadOnly();

    /// <summary>
    /// Gets the number of providers in the collection.
    /// </summary>
    public int Count => _providers.Count;

    /// <summary>
    /// Gets whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => false;

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

    /// <summary>
    /// Resolves by generic type parameter, querying providers from top to bottom. Returns null when not found.
    /// </summary>
    /// <typeparam name="T">The generic type parameter to resolve.</typeparam>
    /// <returns>The type definition if found; otherwise, null.</returns>
    public ITypeDefinition? GetTypeDefinition<T>() => GetTypeDefinition(typeof(T));

    /// <summary>
    /// Determines whether the collection contains the specified provider.
    /// </summary>
    /// <param name="item">The provider to locate in the collection.</param>
    /// <returns>True if the provider is found; otherwise, false.</returns>
    public bool Contains(ITypeDefinitionProvider item)
    {
        return _providers.Contains(item);
    }

    /// <summary>
    /// Copies the providers to an array, starting at the specified array index.
    /// </summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in the array at which copying begins.</param>
    public void CopyTo(ITypeDefinitionProvider[] array, int arrayIndex)
    {
        _providers.CopyTo(array, arrayIndex);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<ITypeDefinitionProvider> GetEnumerator()
    {
        return _providers.GetEnumerator();
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}