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
    /// Gets the primitive type identifier if this type is a primitive type, null otherwise.
    /// Primitive types are the atomic building blocks from which all other types are composed.
    /// </summary>
    PrimitiveType? PrimitiveType { get; }

    /// <summary>
    /// Gets the shape of this type. Exactly one kind, so a type cannot be in two
    /// shapes at once. A <see cref="TypeKind.Primitive"/> always carries a
    /// <see cref="PrimitiveType"/> and every other kind reports null.
    /// </summary>
    TypeKind Kind { get; }

    /// <summary>
    /// The conversion from <paramref name="source"/> to this type that the platform defines, or null.
    /// Declared operators and built-in rules both answer here: a conversion that states a method is a declared
    /// operator, one with no method is applied by the platform itself.
    /// </summary>
    internal TypeConversion? ResolveConversionFrom(ITypeDefinition source) => ModeledConversions.Find(source, this);

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
    /// The operators of <paramref name="kind"/> this definition declares when it meets <paramref name="other"/>
    /// (null: the other operand is this type). Modeled definitions declare static <c>op_*</c> members and ignore
    /// <paramref name="other"/>; a platform adds the operators it leaves to its compiler, which depend on the pair.
    /// </summary>
    internal IEnumerable<DeclaredOperator> GetOperators(OperatorKind kind, ITypeDefinition? other = null) => ModeledOperators.Find(this, kind);
}