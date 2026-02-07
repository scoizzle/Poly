using Poly.Introspection;

namespace Poly.DomainModeling.TypeExpressions;

/// <summary>
/// An optional/nullable type wrapper.
/// Represents a type that may or may not have a value.
/// </summary>
public sealed record OptionalType(TypeExpression Inner) : TypeExpression {
    public override TypeCategory Category => TypeCategory.Nullable | Inner.Category;

    public override string ToString() => $"{Inner}?";
}