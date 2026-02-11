using Poly.Introspection;

namespace Poly.DomainModeling.TypeExpressions;

/// <summary>
/// A primitive (leaf) type in the type expression system.
/// Represents atomic types like int, string, bool, etc.
/// </summary>
public sealed record PrimitiveType(PrimitiveTypeId Id) : TypeExpression {
    public override TypeCategory Category => Id.GetCategory();

    public override string ToString() => Id.ToString();
}