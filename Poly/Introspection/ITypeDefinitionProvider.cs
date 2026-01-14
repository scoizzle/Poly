namespace Poly.Introspection;

/// <summary>
/// Provides <see cref="ITypeDefinition"/> instances by name or runtime <see cref="Type"/>.
/// Implementations may compose other providers and should be safe for concurrent use.
/// </summary>
public interface ITypeDefinitionProvider {
    /// <summary>
    /// Resolves a type definition by fully-qualified name.
    /// Returns null when not found.
    /// </summary>
    ITypeDefinition? GetTypeDefinition(string name);

    /// <summary>
    /// Resolves a type definition by runtime <see cref="Type"/>.
    /// Returns null when not found.
    /// </summary>
    ITypeDefinition? GetTypeDefinition(Type type);

    /// <summary>
    /// Creates a thread-safe deferred resolver for a named type that throws if not found.
    /// </summary>
    /// <param name="name">Fully-qualified type name to resolve.</param>
    /// <exception cref="ArgumentException">Thrown when the name is null/whitespace or the type cannot be resolved.</exception>
    Lazy<ITypeDefinition> GetDeferredTypeDefinitionResolver(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Lazy<ITypeDefinition>(
            () => GetTypeDefinition(name)
                ?? throw new ArgumentException($"Type with name '{name}' not found.", nameof(name)),
            isThreadSafe: true);
    }
}