namespace Poly.Syntax.AbstractSyntaxTree.TypeDefinitions;

/// <summary>
/// AST node representing a collection type (array, list, set, etc.).
/// </summary>
/// <param name="ElementType">The element type of the collection.</param>
/// <param name="Kind">The kind of collection (array, list, set, etc.).</param>
public sealed record CollectionTypeReference(
    Node ElementType,
    CollectionKind Kind = CollectionKind.List
) : Node {

    public override IEnumerable<Node?> Children => [ElementType];

    public override string ToString() => Kind switch {
        CollectionKind.Array => $"{ElementType}[]",
        CollectionKind.Set => $"Set<{ElementType}>",
        _ => $"List<{ElementType}>"
    };
}

/// <summary>
/// Describes the kind of collection.
/// </summary>
public enum CollectionKind {
    /// <summary>A fixed-size array of elements.</summary>
    Array,
    /// <summary>An ordered, indexable sequence of elements.</summary>
    List,
    /// <summary>An unordered collection of unique elements.</summary>
    Set
}