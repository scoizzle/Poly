namespace Poly.Introspection;

/// <summary>
/// Defines a type with introspectable members and metadata, decoupled from any concrete
/// implementation (e.g., CLR reflection, data models). Implementations must be thread-safe
/// for concurrent reads and return stable results across calls.
/// </summary>
/// <remarks>
/// Thread-safety: Implementations should build and cache internal structures once, then
/// serve read-only views. The <see cref="Members"/> enumeration should be idempotent.
/// </remarks>
public interface ITypeDefinition {
    /// <summary>
    /// Gets the simple (non-qualified) name of the type.
    /// </summary>
    /// <example>"String", "List`1"</example>
    public string Name { get; }

    /// <summary>
    /// Gets the namespace qualification, or null for global types.
    /// </summary>
    /// <example>"System", "System.Collections.Generic", null</example>
    public string? Namespace { get; }

    /// <summary>
    /// Gets the fully-qualified name combining <see cref="Namespace"/> and <see cref="Name"/>.
    /// </summary>
    public string FullName => Namespace != null ? $"{Namespace}.{Name}" : Name;

    /// <summary>
    /// Gets all members (fields, properties, methods) defined on the type.
    /// </summary>
    public IEnumerable<ITypeMember> Members { get; }

    /// <summary>
    /// Gets all field members defined on the type.
    /// </summary>
    public IEnumerable<ITypeField> Fields { get; }

    /// <summary>
    /// Gets all property members defined on the type.
    /// </summary>
    public IEnumerable<ITypeProperty> Properties { get; }

    /// <summary>
    /// Gets all method members defined on the type.
    /// </summary>
    public IEnumerable<ITypeMethod> Methods { get; }

    /// <summary>
    /// Gets all overloads of the method with the specified name.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <returns>All methods with the specified name.</returns>
    public IEnumerable<ITypeMethod> GetMethodOverloads(string name) => Methods.Where(m => m.Name == name);

    /// <summary>
    /// Attempts to find a method overload matching the specified parameter types.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <param name="parameterTypes">The types of the parameters to match.</param>
    /// <param name="method">The matching method, if found.</param>
    /// <returns>True if a matching method was found; otherwise, false.</returns>
    public bool TryGetMethod(string name, IEnumerable<Type> parameterTypes, out ITypeMethod? method);

    /// <summary>
    /// Gets the best-matching method overload for the given name and argument types.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <param name="argumentTypes">The types of the arguments to match against.</param>
    /// <returns>The best-matching method, or null if none found.</returns>
    public ITypeMethod? GetBestMatchingMethod(string name, IEnumerable<Type> argumentTypes);

    /// <summary>
    /// Gets the underlying reflected runtime type, when available.
    /// </summary>
    public Type ReflectedType { get; }

    /// <summary>
    /// Retrieves members by name.
    /// </summary>
    /// <param name="name">The member name to search for.</param>
    public IEnumerable<ITypeMember> GetMembers(string name);

    /// <summary>
    /// Gets indexer members (commonly named "Item").
    /// </summary>
    public IEnumerable<ITypeMember> GetIndexers() => GetMembers("Item");

    /// <summary>
    /// Gets the base type of this type, or null if this is <c>object</c> or an interface.
    /// </summary>
    public ITypeDefinition? BaseType { get; }

    /// <summary>
    /// Gets all interfaces implemented by this type (including inherited).
    /// </summary>
    public IEnumerable<ITypeDefinition> Interfaces { get; }

    /// <summary>
    /// Gets the generic parameter types for this type when it is generic.
    /// For closed generics, these correspond to the concrete type arguments; for open generic
    /// type definitions, these represent the generic parameter placeholders.
    /// Returns null for non-generic types.
    /// </summary>
    public IEnumerable<IParameter>? GenericParameters { get; }

    /// <summary>
    /// Gets static members only.
    /// </summary>
    public IEnumerable<ITypeMember> StaticMembers => Members.Where(m => m.IsStatic);

    /// <summary>
    /// Gets instance members only.
    /// </summary>
    public IEnumerable<ITypeMember> InstanceMembers => Members.Where(m => !m.IsStatic);

    /// <summary>
    /// Determines if values of <paramref name="other"/> can be assigned to this type.
    /// </summary>
    /// <remarks>
    /// Default implementation walks the base type chain and interface list. Implementations
    /// can override with more precise or faster logic.
    /// </remarks>
    public bool IsAssignableFrom(ITypeDefinition other) {
        if (this == other) return true;

        var current = other.BaseType;
        while (current != null) {
            if (this == current) return true;
            current = current.BaseType;
        }

        if (other.Interfaces.Any(i => this == i)) return true;

        return false;
    }

    /// <summary>
    /// Determines if this type can be assigned to <paramref name="other"/>.
    /// </summary>
    public bool IsAssignableTo(ITypeDefinition other) => other.IsAssignableFrom(this);
}