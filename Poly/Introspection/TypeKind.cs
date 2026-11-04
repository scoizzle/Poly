namespace Poly.Introspection;

/// <summary>
/// The closed set of type shapes an <see cref="ITypeDefinition"/> can have.
/// A type is exactly one kind, so combinations that are not a shape — for
/// example <c>Integer | FloatingPoint</c> — cannot be expressed.
/// </summary>
public enum TypeKind {
    /// <summary>
    /// A reference type with identity, including synthetic dictionary-backed types.
    /// Zero on purpose: a zero-initialized field must not claim <see cref="Primitive"/>,
    /// the one kind that also requires a <see cref="ITypeDefinition.PrimitiveType"/>.
    /// </summary>
    Class,

    /// <summary>An atomic type, described by <see cref="ITypeDefinition.PrimitiveType"/>.</summary>
    Primitive,

    /// <summary>A value type that is not a primitive.</summary>
    Struct,

    /// <summary>An interface declaration.</summary>
    Interface,

    /// <summary>An enumeration declaration.</summary>
    Enum,

    /// <summary>A delegate declaration.</summary>
    Delegate,

    /// <summary>
    /// Reserved for a union/sum type. No producer and no C# printer arm today; the
    /// closed set records the shape so a future <c>ITypeExpression</c> model can
    /// claim it without widening this enum.
    /// </summary>
    Union,

    /// <summary>
    /// Reserved for a product/tuple type. No producer and no C# printer arm today;
    /// see <see cref="Union"/>.
    /// </summary>
    Tuple
}