using Poly.Introspection;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// The type of a domain expression: an Introspection definition plus optionality.
/// <see cref="Unresolved"/> has a null <see cref="Definition"/> and is never assignable;
/// assignability goes only through <see cref="DomainTypeModel.Assign"/>, never record equality.
/// </summary>
/// <param name="Definition">Null only for <see cref="Unresolved"/>.</param>
/// <param name="Optional">True when the value may be absent.</param>
/// <param name="DomainName">The domain-level name this type was resolved from.</param>
internal readonly record struct ExprType(ITypeDefinition? Definition, bool Optional, string? DomainName) {
    public static ExprType Unresolved => default;

    public bool IsResolved => Definition is not null;
}