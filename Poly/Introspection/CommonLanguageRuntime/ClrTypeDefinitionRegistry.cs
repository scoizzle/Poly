using System.Collections.Concurrent;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Thread-safe registry and provider for CLR-backed <see cref="ClrTypeDefinition"/> instances.
/// Caches definitions by fully-qualified name and supports deferred resolution.
/// </summary>
internal sealed class ClrTypeDefinitionRegistry : ITypeDefinitionProvider {
    public static readonly ClrTypeDefinitionRegistry Shared = new();
    private readonly ConcurrentDictionary<string, ClrTypeDefinition> _types = new();

    /// <summary>
    /// Gets a thread-safe lazy resolver for a runtime <see cref="Type"/>.
    /// If the type is already cached, returns a pre-initialized lazy wrapper.
    /// </summary>
    public Lazy<ClrTypeDefinition> GetDeferredTypeDefinitionResolver(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var name = type.FullName ?? type.Name;

        if (_types.TryGetValue(name, out var clrType)) {
            return new Lazy<ClrTypeDefinition>(clrType);
        }

        return new Lazy<ClrTypeDefinition>(() => GetTypeDefinition(type));
    }

    /// <summary>
    /// Gets or creates a definition for <typeparamref name="T"/>.
    /// </summary>
    public ClrTypeDefinition GetTypeDefinition<T>() => GetTypeDefinition(typeof(T));

    /// <summary>
    /// Gets or creates a definition for a runtime <see cref="Type"/>.
    /// </summary>
    public ClrTypeDefinition GetTypeDefinition(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var name = type.FullName ?? type.Name;
        return _types.GetOrAdd(name, CreateTypeDefinition, (type, this));

        static ClrTypeDefinition CreateTypeDefinition(string typeName, (Type clrType, ClrTypeDefinitionRegistry registry) context) 
            => new ClrTypeDefinition(context.clrType, context.registry);
    }

    /// <summary>
    /// Adds an existing definition instance to the cache.
    /// </summary>
    public void AddType(ClrTypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_types.ContainsKey(type.FullName)) throw new ArgumentException($"Type with name '{type.FullName}' already exists.", nameof(type));
        _types.TryAdd(type.FullName, type);
    }

    /// <summary>
    /// Removes an existing definition instance from the cache.
    /// </summary>
    public void RemoveType(ClrTypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!_types.ContainsKey(type.FullName)) throw new ArgumentException($"Type with name '{type.FullName}' does not exist.", nameof(type));
        _types.TryRemove(type.FullName, out _);
    }

    /// <summary>
    /// Resolves by fully-qualified name using cache, falling back to <see cref="Type.GetType(string)"/>.
    /// Returns null if the type cannot be resolved.
    /// </summary>
    public ITypeDefinition? GetTypeDefinition(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_types.TryGetValue(name, out var existing)) {
            return existing;
        }

        try {
            var clrType = Type.GetType(name);
            if (clrType is null) {
                return null;
            }

            var created = new ClrTypeDefinition(clrType, this);
            return _types.GetOrAdd(name, created);
        }
        catch {
            Trace.TraceError($"Failed to resolve CLR type for name '{name}'.");
            return null;
        }
    }

    ITypeDefinition? ITypeDefinitionProvider.GetTypeDefinition(Type type) => GetTypeDefinition(type);
}