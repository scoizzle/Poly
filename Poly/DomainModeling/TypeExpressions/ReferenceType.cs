using Poly.Introspection;

namespace Poly.DomainModeling.TypeExpressions;

/// <summary>
/// A reference to another type defined in the data model.
/// </summary>
public sealed record ReferenceType(string TypeName) : TypeExpression {
    public override TypeCategory Category => TypeCategory.Reference;

    public override string ToString() => TypeName;
}