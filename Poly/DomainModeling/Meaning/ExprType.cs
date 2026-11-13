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

    /// <summary>
    /// The type of the literal <c>null</c>: no definition, optional. Compatible with Text and Guid only
    /// (the analyzer's rule today); T-06 replaces it with the real null rule.
    /// </summary>
    public static ExprType NullLiteral => new(null, Optional: true, null);

    public bool IsNullLiteral => Definition is null && Optional;
}