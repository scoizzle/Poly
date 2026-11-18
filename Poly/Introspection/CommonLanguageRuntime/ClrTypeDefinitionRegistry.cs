using System.Collections.Concurrent;
using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Thread-safe registry and provider for CLR-backed <see cref="ClrTypeDefinition"/> instances.
/// Caches definitions by fully-qualified name and supports deferred resolution.
/// </summary>
internal sealed class ClrTypeDefinitionRegistry : IClrTypeDefinitionProvider, ITypeSource {
    public static readonly ClrTypeDefinitionRegistry Shared = new();
    private readonly ConcurrentDictionary<string, ClrTypeDefinition> _types = new();

    /// <summary>
    /// Gets a thread-safe lazy resolver for a runtime <see cref="Type"/>.
    /// If the type is already cached, returns a pre-initialized lazy wrapper.
    /// </summary>
    public Lazy<ClrTypeDefinition> GetDeferredTypeDefinitionResolver(Type type) {
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

    /// <summary>The definitions this registry has resolved so far. The CLR's universe is open, so this is a cache
    /// snapshot, never a claim that no other CLR type exists.</summary>
    public IEnumerable<ITypeDefinition> Types => _types.Values;

    /// <summary>
    /// Gets or creates a definition for a runtime <see cref="Type"/>.
    /// </summary>
    public ClrTypeDefinition GetTypeDefinition(Type type) {
        ArgumentNullException.ThrowIfNull(type);

        var name = type.FullName ?? type.Name;
        return _types.GetOrAdd(name, CreateTypeDefinition, (type, this));

        static ClrTypeDefinition CreateTypeDefinition(string typeName, (Type clrType, ClrTypeDefinitionRegistry registry) context) {
            return new ClrTypeDefinition(context.clrType, context.registry);
        }
    }

    /// <summary>
    /// Adds an existing definition instance to the cache.
    /// </summary>
    public void AddType(ClrTypeDefinition type) {
        ArgumentNullException.ThrowIfNull(type);
        if (_types.ContainsKey(type.FullName)) throw new ArgumentException($"Type with name '{type.FullName}' already exists.", nameof(type));
        _types.TryAdd(type.FullName, type);
    }

    /// <summary>
    /// Removes an existing definition instance from the cache.
    /// </summary>
    public void RemoveType(ClrTypeDefinition type) {
        ArgumentNullException.ThrowIfNull(type);
        if (!_types.ContainsKey(type.FullName)) throw new ArgumentException($"Type with name '{type.FullName}' does not exist.", nameof(type));
        _types.TryRemove(type.FullName, out _);
    }

    /// <summary>
    /// Resolves by fully-qualified name using cache, falling back to <see cref="Type.GetType(string)"/>.
    /// Returns null if the type cannot be resolved.
    /// </summary>
    public ITypeDefinition? GetTypeDefinition(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        // A bare name is a scope's to bind (#998 S2): only a qualified name is looked up, in the cache or across the
        // loaded assemblies, so what a name means never depends on which type another caller happened to resolve or
        // load. Interim until S5: a bare name that spells a ClrPrimitive member still names that primitive's type.
        if (!name.Contains('.', StringComparison.Ordinal))
            return Enum.TryParse<ClrPrimitive>(name, ignoreCase: false, out var primitive) && primitive.GetClrType() is { } named
                ? GetTypeDefinition(named)
                : null;
        if (_types.TryGetValue(name, out var existing)) {
            return existing;
        }

        var clrType = Type.GetType(name) ?? ResolveLoadedAssemblyType(name);
        if (clrType is null) {
            LoadReferencedAssemblies(typeof(ClrTypeDefinitionRegistry).Assembly);
            clrType = ResolveLoadedAssemblyType(name);
        }
        if (clrType is null) {
            return null;
        }

        var created = GetTypeDefinition(clrType);
        _types.TryAdd(name, created);
        return created;
    }

    /// <summary>The loaded assemblies as one source: what each declares in <paramref name="namespace"/>.</summary>
    public IEnumerable<ITypeDefinition> TypesIn(string? @namespace) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic)
            .SelectMany(assembly => Sources.GetValue(assembly, static a => new ClrAssemblySource(a)).TypesIn(@namespace))
            .Distinct();

    /// <summary>The type a namespace of the loaded assemblies declares under <paramref name="name"/>. The global
    /// namespace answers nothing: a bare name is never looked up across the process.</summary>
    public ITypeDefinition? TypeIn(string? @namespace, string name) =>
        @namespace is null ? null : GetTypeDefinition($"{@namespace}.{name}");

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Assembly, ClrAssemblySource> Sources = [];

    /// <summary>
    /// Loads <paramref name="assembly"/>'s references that are not loaded yet, so a bare-name
    /// lookup still resolves a type this assembly does not declare. R-01 moved the runtime
    /// contract types to Poly.Runtime, and the scan above only sees assemblies something else
    /// has already loaded. A reference that cannot load cannot hold the type, so load failures
    /// leave the name unresolved rather than throwing.
    /// </summary>
    private static void LoadReferencedAssemblies(Assembly assembly) {
        foreach (var reference in assembly.GetReferencedAssemblies()) {
            try {
                Assembly.Load(reference);
            }
            catch (Exception) {
                // Unloadable reference: the caller returns null for an unresolvable name.
            }
        }
    }

    private static Type? ResolveLoadedAssemblyType(string name) {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var resolved = assembly.GetType(name, throwOnError: false, ignoreCase: false);
            if (resolved != null) {
                return resolved;
            }
        }

        return null;
    }

    ITypeDefinition? IClrTypeDefinitionProvider.GetTypeDefinition(Type type) => GetTypeDefinition(type);
}