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
    /// Gets the visibility of the type.
    /// </summary>
    AccessModifier AccessModifier { get; }

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
    /// Gets all constructors defined on the type.
    /// </summary>
    IEnumerable<ITypeConstructor> Constructors { get; }

    /// <summary>
    /// Gets the operators that apply to operands of this type. A platform decides which: a .NET primitive lists the
    /// operators its numeric promotion reaches, which another type may declare.
    /// </summary>
    IEnumerable<ITypeOperator> Operators { get; }


    /// <summary>
    /// True when this type exposes a sequence shape: a platform's own collection definition, or a type whose
    /// representation is an array or a closed <c>IEnumerable&lt;&gt;</c>. A shape is structural, so the platform answers,
    /// and the root names no collection type.
    /// </summary>
    internal bool IsCollectionShape { get; }

    /// <summary>
    /// The element type a sequence-shaped type yields when it exposes no indexer, or null. A representation fact, so
    /// the platform answers it; <see cref="TypeDefinitionExtensions"/>'s <c>GetElementType</c> prefers an indexer.
    /// </summary>
    internal ITypeDefinition? SequenceElementType { get; }

    /// <summary>
    /// Gets the conversions this type states: the operators it declares (either end, which is why an item names both
    /// types) and the ones the platform applies with this type as the source (no method). A conversion into this type
    /// that the other end declares, and what a type defined later may add, are the type system's
    /// <see cref="ITypeSystem.Conversion"/>.
    /// Optionality is the caller's (T-06), so the lift into a nullable target is not listed, and reference conversions
    /// are assignability, not conversion.
    /// </summary>
    internal IEnumerable<ITypeConversion> Conversions { get; }

    /// <summary>
    /// Gets the type this definition wraps when it is this platform's nullable form of another type (<c>T?</c> for
    /// <c>T</c>), or null. Nullability is the platform's relation, not the root's: a host states which definitions are
    /// its nullable forms, so a conversion into one can be stated (<see cref="ResolveConversionFrom"/>) without the
    /// root knowing the host's model. Where optionality applies stays the caller's (T-06).
    /// </summary>
    internal ITypeDefinition? NullableOf { get; }
}