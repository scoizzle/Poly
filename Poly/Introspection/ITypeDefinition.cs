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
    /// The type system that made this definition, and whose rules decide its identity and its relations (#998 T9,
    /// decision §2.4). Two systems never share a definition.
    /// </summary>
    ITypeSystem System { get; }

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
    /// Gets every member the type declares: fields, properties, methods and constructors (#998 T11). The typed views
    /// (<c>Fields</c>, <c>Properties</c>, <c>Methods</c>, <c>Constructors</c>) are extensions over it.
    /// </summary>
    IEnumerable<ITypeMember> Members { get; }

    /// <summary>
    /// Gets the operators that apply to operands of this type. A platform decides which: a .NET primitive lists the
    /// operators its numeric promotion reaches, which another type may declare.
    /// </summary>
    IEnumerable<ITypeOperator> Operators { get; }

    /// <summary>
    /// Gets the conversions this type declares: the operators it declares (either end, which is why an item names both
    /// types) and, until T10, the ones the platform applies with this type as the source (no method). Which conversion
    /// applies is the type system's <see cref="ITypeSystem.Conversion"/>. Optionality is the caller's (T-06), so the lift
    /// into a nullable target is not listed, and reference conversions are assignability, not conversion.
    /// </summary>
    internal IEnumerable<ITypeConversion> Conversions { get; }
}