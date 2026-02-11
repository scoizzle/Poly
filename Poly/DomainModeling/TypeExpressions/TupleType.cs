using Poly.Introspection;

namespace Poly.DomainModeling.TypeExpressions;

/// <summary>
/// A tuple / product type.
/// Represents a fixed collection of values of potentially different types.
/// </summary>
public sealed record TupleType(IReadOnlyList<TypeExpression> Elements) : TypeExpression {
    public override TypeCategory Category => TypeCategory.Product;

    public override string ToString() => $"({string.Join(", ", Elements)})";
}