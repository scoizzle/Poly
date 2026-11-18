namespace Poly.Introspection;

/// <summary>
/// Provides <see cref="ITypeDefinition"/> instances by name. A provider that has CLR
/// types also implements <see cref="CommonLanguageRuntime.IClrTypeDefinitionProvider"/> for the runtime-type handle.
/// Implementations may compose other providers and should be safe for concurrent use.
/// </summary>
public interface ITypeDefinitionProvider {
    /// <summary>
    /// Resolves a type definition by fully-qualified name.
    /// Returns null when not found.
    /// </summary>
    ITypeDefinition? GetTypeDefinition(string name);

    /// <summary>
    /// Gets the definitions this provider holds now. Not a claim of completeness: a platform with an open universe (the
    /// CLR) lists what it has resolved so far, and a declaration registry lists what it declares. A consumer that needs
    /// "every type this session knows" enumerates the composed provider instead of supplying candidates itself.
    /// </summary>
    IEnumerable<ITypeDefinition> Types { get; }

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