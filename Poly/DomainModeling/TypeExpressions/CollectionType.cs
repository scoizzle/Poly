using Poly.Introspection;

namespace Poly.DomainModeling.TypeExpressions;

/// <summary>
/// Specifies the kind of collection.
/// </summary>
public enum CollectionKind {
    /// <summary>A fixed-size ordered sequence.</summary>
    Array,

    /// <summary>A variable-size ordered sequence.</summary>
    List,

    /// <summary>An unordered collection of unique elements.</summary>
    Set
}

/// <summary>
/// A collection type that contains elements of another type.
/// </summary>
public sealed record CollectionType(TypeExpression Element, CollectionKind Kind) : TypeExpression {
    public override TypeCategory Category => TypeCategory.Collection;

    public override string ToString() => Kind switch {
        CollectionKind.Array => $"{Element}[]",
        CollectionKind.List => $"List<{Element}>",
        CollectionKind.Set => $"Set<{Element}>",
        _ => $"Collection<{Element}>"
    };
}