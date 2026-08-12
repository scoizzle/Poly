namespace Poly.Ast.Nodes;

/// <summary>
/// Represents a <c>typeof(T)</c> expression, e.g. an enum type argument for
/// <c>[EnumDataType(typeof(Genre))]</c>.
/// </summary>
public sealed record TypeOf(TypeReference Type) : Expression {
    /// <inheritdoc />
    public override string ToString() => $"typeof({Type})";
}