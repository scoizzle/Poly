using Poly.Introspection;

namespace Poly.DataModeling.TypeExpressions;

/// <summary>
/// A discriminated union / sum type.
/// Represents a value that can be one of several possible types.
/// </summary>
public sealed record UnionType(IReadOnlyList<TypeExpression> Cases) : TypeExpression {
    public override TypeCategory Category => TypeCategory.Union;

    public override string ToString() => string.Join(" | ", Cases);
}