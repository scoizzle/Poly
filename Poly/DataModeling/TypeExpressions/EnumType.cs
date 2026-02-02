using Poly.Introspection;

namespace Poly.DataModeling.TypeExpressions;

/// <summary>
/// An enumeration type with a defined set of allowed values.
/// </summary>
public sealed record EnumType(string EnumName, IReadOnlyList<string> Values) : TypeExpression {
    public override TypeCategory Category => TypeCategory.Enumeration;

    public override string ToString() => EnumName;
}