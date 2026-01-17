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
    /// Gets a value indicating whether this type is nullable (Nullable&lt;T&gt; or reference type).
    /// </summary>
    bool IsNullable { get; }

    /// <summary>
    /// Gets a value indicating whether this type represents a numeric type (int, float, decimal, etc.).
    /// </summary>
    bool IsNumeric { get; }

    /// <summary>
    /// Gets a value indicating whether this type is an array.
    /// </summary>
    bool IsArray { get; }

    /// <summary>
    /// Gets the CLR System.Type if this is CLR-backed; otherwise null.
    /// </summary>
    Type? ClrType { get; }

    /// <summary>
    /// Gets the element type of an array, or null if this type is not an array.
    /// </summary>
    ITypeDefinition? ElementType { get; }

    /// <summary>
    /// Gets the underlying type for nullable types, or null if this type is not nullable.
    /// </summary>
    ITypeDefinition? UnderlyingType { get; }

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
    IEnumerable<IParameter>? GenericParameters { get; }

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
}