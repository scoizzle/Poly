namespace Poly.Introspection;

using Poly.Ast.Nodes;
using Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// A LIFO stack of <see cref="ITypeDefinitionProvider"/> instances that resolves types by
/// querying the most recently added provider first. Useful for layering overrides above defaults.
/// It holds no fallback stack of its own (#998 T12a): attached to an analysis it is that layer's own sources, and a
/// name it cannot answer goes to the layer's target chain, never to a provider it appended.
/// </summary>
public sealed class TypeDefinitionProviderCollection(params IEnumerable<ITypeDefinitionProvider> providers) : IClrTypeDefinitionProvider, ITypeDefinitionNodeProvider, ICollection<ITypeDefinitionProvider> {
    private readonly List<ITypeDefinitionProvider> _providers = [.. providers];

    /// <summary>The analysis layer this collection is the own sources of, or null when it stands alone. Set by
    /// <see cref="Poly.Analysis.AnalysisContext"/>; a name lookup then answers exactly what the layer resolves.</summary>
    internal ITypeSystem? Layer { get; set; }

    /// <summary>
    /// Adds a provider to the top of the stack.
    /// </summary>
    public void Add(ITypeDefinitionProvider provider) {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Insert(0, provider);
    }

    /// <summary>
    /// Adds a provider at the bottom of the stack (last queried). No-op if already present.
    /// </summary>
    public void AddFallback(ITypeDefinitionProvider provider) {
        ArgumentNullException.ThrowIfNull(provider);
        if (!_providers.Contains(provider))
            _providers.Add(provider);
    }

    /// <summary>
    /// Removes the first matching provider instance from the stack.
    /// </summary>
    /// <returns>True if the provider was found and removed.</returns>
    public bool Remove(ITypeDefinitionProvider provider) {
        ArgumentNullException.ThrowIfNull(provider);
        return _providers.Remove(provider);
    }

    /// <summary>
    /// Removes all providers.
    /// </summary>
    public void Clear() {
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
    /// Resolves by name. Attached to an analysis layer, it answers exactly what the layer resolves: the layer's own
    /// providers, then its target chain. A standalone collection queries its providers from top to bottom.
    /// Returns null when not found.
    /// </summary>
    public ITypeDefinition? GetTypeDefinition(string name) {
        if (Layer is { } layer)
            return layer.Resolve(name);
        foreach (var provider in _providers.AsReadOnly()) {
            var typeDef = provider.GetTypeDefinition(name);
            if (typeDef is not null) {
                return typeDef;
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves by runtime type through this collection's own providers, then the target chain's CLR source (#998 T12a):
    /// the CLR is the C# target's source, and a chain with no CLR beneath it answers null. A standalone collection
    /// queries its own providers' CLR handles.
    /// </summary>
    public ITypeDefinition? GetTypeDefinition(Type type) {
        foreach (var provider in _providers.AsReadOnly()) {
            if (provider is not IClrTypeDefinitionProvider clr)
                continue;
            var typeDef = clr.GetTypeDefinition(type);
            if (typeDef is not null) {
                return typeDef;
            }
        }
        if (Layer is { } layer) {
            for (var system = layer.Target; system is not null; system = system.Target) {
                foreach (var source in system.Sources) {
                    if (source is IClrTypeDefinitionProvider clr && clr.GetTypeDefinition(type) is { } definition)
                        return definition;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Every definition the providers hold, each listed once by identity.
    /// </summary>
    public IEnumerable<ITypeDefinition> Types =>
        _providers.SelectMany(static provider => provider.Types)
            .Distinct(IdenticalDefinition.Instance);

    /// <summary>Identity, so one definition reached through two providers is listed once.</summary>
    private sealed class IdenticalDefinition : IEqualityComparer<ITypeDefinition> {
        public static readonly IdenticalDefinition Instance = new();
        public bool Equals(ITypeDefinition? x, ITypeDefinition? y) => ReferenceEquals(x, y);
        public int GetHashCode(ITypeDefinition definition) => RuntimeHelpers.GetHashCode(definition);
    }

    /// <summary>
    /// Resolves by generic type parameter, querying providers from top to bottom. Returns null when not found.
    /// </summary>
    /// <typeparam name="T">The generic type parameter to resolve.</typeparam>
    /// <returns>The type definition if found; otherwise, null.</returns>
    public ITypeDefinition? GetTypeDefinition<T>() => GetTypeDefinition(typeof(T));

    /// <summary>AST type declarations supplied by providers in this stack, in
    /// query order, without reference duplicates.</summary>
    public IEnumerable<TypeDefinitionNode> GetTypeDefinitionNodes() =>
        _providers
            .OfType<ITypeDefinitionNodeProvider>()
            .SelectMany(static provider => provider.GetTypeDefinitionNodes())
            .Distinct<TypeDefinitionNode>(ReferenceEqualityComparer.Instance);

    /// <summary>The first provider's definition built for exactly this node, in
    /// query order.</summary>
    public ITypeDefinition? GetTypeDefinition(TypeDefinitionNode node) {
        foreach (var provider in _providers.AsReadOnly()) {
            if (provider is ITypeDefinitionNodeProvider nodeProvider
                && nodeProvider.GetTypeDefinition(node) is { } definition)
                return definition;
        }
        return null;
    }

    /// <summary>
    /// Determines whether the collection contains the specified provider.
    /// </summary>
    /// <param name="item">The provider to locate in the collection.</param>
    /// <returns>True if the provider is found; otherwise, false.</returns>
    public bool Contains(ITypeDefinitionProvider item) {
        return _providers.Contains(item);
    }

    /// <summary>
    /// Copies the providers to an array, starting at the specified array index.
    /// </summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in the array at which copying begins.</param>
    public void CopyTo(ITypeDefinitionProvider[] array, int arrayIndex) {
        _providers.CopyTo(array, arrayIndex);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<ITypeDefinitionProvider> GetEnumerator() {
        return _providers.GetEnumerator();
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }
}