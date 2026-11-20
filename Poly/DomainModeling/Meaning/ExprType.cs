namespace Poly.DomainModeling.Meaning;

/// <summary>
/// The type of a domain expression: an Introspection definition plus optionality.
/// <see cref="Unresolved"/> has a null <see cref="Definition"/> and is never assignable;
/// assignability goes only through <see cref="DomainTypeModel.Assign"/>, never record equality.
/// </summary>
/// <param name="Definition">Null only for <see cref="Unresolved"/>.</param>
/// <param name="Optional">True when the value may be absent.</param>
/// <param name="DomainName">The domain-level name this type was resolved from.</param>
/// <param name="Declared">What the domain declared <paramref name="DomainName"/> as, when the definition is one of its
/// own declarations (#998 T8): the domain's Entity, Value and Enum families are its declarations, never what a
/// definition answers about itself.</param>
internal readonly record struct ExprType(ITypeDefinition? Definition, bool Optional, string? DomainName, DomainDeclaration? Declared = null) {
    public static ExprType Unresolved => default;

    public bool IsResolved => Definition is not null;

    /// <summary>
    /// The type of the literal <c>null</c>: no definition, optional. It assigns only into an optional target, and it
    /// compares with an optional operand or with a Text, Guid, entity, value-type or Binary operand.
    /// </summary>
    public static ExprType NullLiteral => new(null, Optional: true, null);

    public bool IsNullLiteral => Definition is null && Optional;

    /// <summary>
    /// This type, marked as possibly absent when <paramref name="optional"/> is true. Only a resolved type takes the
    /// mark: an unresolved type that did would read as <see cref="NullLiteral"/>.
    /// </summary>
    public ExprType AsOptional(bool optional = true) => optional && IsResolved ? this with { Optional = true } : this;
}

/// <summary>What a domain declared a type name as (#998 T8).</summary>
internal enum DomainDeclaration { Entity, Value, Enum }