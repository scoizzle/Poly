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
    string Name { get; }

    /// <summary>
    /// Gets the namespace qualification, or null for global types.
    /// </summary>
    /// <example>"System", "System.Collections.Generic", null</example>
    string? Namespace { get; }

    /// <summary>
    /// Gets the fully-qualified name combining <see cref="Namespace"/> and <see cref="Name"/>.
    /// </summary>
    string FullName => Namespace != null ? $"{Namespace}.{Name}" : Name;

    /// <summary>
    /// Gets the base type of this type, or null if this is <c>object</c> or an interface.
    /// </summary>
    ITypeDefinition? BaseType { get; }

    /// <summary>
    /// Gets all interfaces implemented by this type (including inherited).
    /// </summary>
    IEnumerable<ITypeDefinition> Interfaces { get; }

    /// <summary>
    /// Gets the generic parameter types for this type when it is generic.
    /// For closed generics, these correspond to the concrete type arguments; for open generic
    /// type definitions, these represent the generic parameter placeholders.
    /// Returns null for non-generic types.
    /// </summary>
    IEnumerable<IParameter> GenericParameters { get; }

    /// <summary>
    /// Gets all members (fields, properties, methods) defined on the type.
    /// </summary>
    IEnumerable<ITypeMember> Members { get; }

    /// <summary>
    /// Gets all field members defined on the type.
    /// </summary>
    IEnumerable<ITypeField> Fields { get; }

    /// <summary>
    /// Gets all property members defined on the type.
    /// </summary>
    IEnumerable<ITypeProperty> Properties { get; }

    /// <summary>
    /// Gets all method members defined on the type.
    /// </summary>
    IEnumerable<ITypeMethod> Methods { get; }

    /// <summary>
    /// Gets the underlying reflected runtime type, when available.
    /// </summary>
    Type ReflectedType { get; }

    /// <summary>
    /// Determines if values of <paramref name="other"/> can be assigned to this type.
    /// </summary>
    /// <remarks>
    /// Default implementation walks the base type chain and interface list. Implementations
    /// can override with more precise or faster logic.
    /// </remarks>
    bool IsAssignableFrom(ITypeDefinition other) {
        ArgumentNullException.ThrowIfNull(other);
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
    bool IsAssignableTo(ITypeDefinition other) {
        ArgumentNullException.ThrowIfNull(other);
        return other.IsAssignableFrom(this);
    }
}
