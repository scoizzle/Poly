using System.Collections.Concurrent;

namespace Poly.Introspection.CommonLanguageRuntime;

public sealed class ClrTypeDefinitionRegistry : ITypeDefinitionProvider {
    private readonly ConcurrentDictionary<string, ClrTypeDefinition> _types = new();

    public Lazy<ClrTypeDefinition> GetDeferredTypeDefinitionResolver(Type type) {
        ArgumentNullException.ThrowIfNull(type);
        var name = type.FullName ?? type.Name;

        if (_types.TryGetValue(name, out var clrType)) {
            return new Lazy<ClrTypeDefinition>(clrType);
        }

        return new Lazy<ClrTypeDefinition>(() => GetTypeDefinition(type));
    }

    public ClrTypeDefinition GetTypeDefinition<T>() => GetTypeDefinition(typeof(T));

    public ClrTypeDefinition GetTypeDefinition(Type type) {
        var name = type.FullName ?? type.Name;
        return _types.GetOrAdd(name, _ => new ClrTypeDefinition(type, this));
    }

    public void AddType(ClrTypeDefinition type) {
        ArgumentNullException.ThrowIfNull(type);
        if (_types.ContainsKey(type.FullName)) throw new ArgumentException($"Type with name '{type.FullName}' already exists.", nameof(type));
        _types.TryAdd(type.FullName, type);
    }

    public void RemoveType(ClrTypeDefinition type) {
        ArgumentNullException.ThrowIfNull(type);
        if (!_types.ContainsKey(type.FullName)) throw new ArgumentException($"Type with name '{type.FullName}' does not exist.", nameof(type));
        _types.TryRemove(type.FullName, out _);
    }

    public ITypeDefinition? GetType(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _types.GetOrAdd(name, CreateTypeDefinition, this);

        static ClrTypeDefinition CreateTypeDefinition(string typeName, ClrTypeDefinitionRegistry registry) {
            var clrType = Type.GetType(typeName);
            if (clrType is null) throw new ArgumentException($"Type with name '{typeName}' is not defined.", nameof(typeName));
            return new ClrTypeDefinition(clrType, registry);
        }
    }
}
