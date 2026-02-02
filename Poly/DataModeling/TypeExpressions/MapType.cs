using Poly.Introspection;

namespace Poly.DataModeling.TypeExpressions;

/// <summary>
/// A map/dictionary type with key and value types.
/// </summary>
public sealed record MapType(TypeExpression Key, TypeExpression Value) : TypeExpression {
    public override TypeCategory Category => TypeCategory.Collection | TypeCategory.Keyed;

    public override string ToString() => $"Map<{Key}, {Value}>";
}