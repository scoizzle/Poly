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
    /// True when <paramref name="other"/> is the same type as this one, by this platform's identity notion. Identity is
    /// the platform's answer, not the root's: a host decides what makes two of its definitions the same (a runtime
    /// type, a declared name, a synthetic collection's kind and element), and a definition from another platform is
    /// never the same type. Callers ask through <see cref="TypeDefinitionExtensions.IsSameType"/>.
    /// </summary>
    internal bool IsSameTypeAs(ITypeDefinition other);

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
    /// that the other end declares, and what a type defined later may add, are <see cref="ResolveConversionFrom"/>.
    /// Optionality is the caller's (T-06), so the lift into a nullable target is not listed, and reference conversions
    /// are assignability, not conversion.
    /// </summary>
    internal IEnumerable<ITypeConversion> Conversions { get; }

    /// <summary>
    /// Gets the primitive type identifier if this type is a primitive type, null otherwise.
    /// Primitive types are the atomic building blocks from which all other types are composed.
    /// </summary>
    PrimitiveType? PrimitiveType { get; }

    /// <summary>
    /// Gets the platform's own atom when this definition is a primitive the shared <see cref="PrimitiveType"/> vocabulary
    /// does not name, or null. Never set beside <see cref="PrimitiveType"/>: a primitive is named once, by the shared
    /// vocabulary or by the platform that states it. A consumer that knows only the shared vocabulary sees an unnamed
    /// primitive (<see cref="Kind"/> is still <see cref="TypeKind.Primitive"/>).
    /// </summary>
    internal PrimitiveAtom? PlatformAtom { get; }

    /// <summary>
    /// Gets the shape of this type. Exactly one kind, so a type cannot be in two
    /// shapes at once. A <see cref="TypeKind.Primitive"/> always carries a
    /// <see cref="PrimitiveType"/> and every other kind reports null.
    /// </summary>
    TypeKind Kind { get; }

    /// <summary>
    /// The conversion from <paramref name="source"/> to this type that the platform defines, or null.
    /// Declared operators and built-in rules both answer here: a conversion that states a method is a declared
    /// operator, one with no method is applied by the platform itself. Which conversions are candidates and how one is
    /// chosen is the platform's rule.
    /// </summary>
    internal ITypeConversion? ResolveConversionFrom(ITypeDefinition source);

    /// <summary>
    /// True when a value of <paramref name="source"/> is a value of this type with no conversion applied:
    /// the same type, a base type or an interface, or whatever the platform adds on top of that (for .NET variance,
    /// array covariance, the lift from <c>T</c> to <c>T?</c> and the box into <c>object</c>). A declared conversion
    /// operator is not in this set, and neither is a built-in numeric conversion: those are
    /// <see cref="ResolveConversionFrom"/>. Assignment, casts, type tests and the emitter read this through
    /// <see cref="TypeDefinitionExtensions.IsAssignableFrom"/>, which widening would loosen.
    /// </summary>
    internal bool IsAssignableWithoutConversion(ITypeDefinition source) => TypeDefinitionExtensions.IsAssignableByHierarchy(this, source);

    /// <summary>
    /// The operator <paramref name="kind"/> applied to this type and <paramref name="right"/> (null for a unary kind),
    /// or null when none applies or several do. Which operators are candidates and how one is chosen is the platform's
    /// rule; consumers ask through <see cref="TypeDefinitionOperatorExtensions.GetOperator"/>.
    /// </summary>
    internal ITypeOperator? ResolveOperator(OperatorKind kind, ITypeDefinition? right);
}