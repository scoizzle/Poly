namespace Poly.Ast.Nodes;

/// <summary>
/// Represents a <c>typeof(T)</c> expression, e.g. an enum type argument for
/// <c>[EnumDataType(typeof(Genre))]</c>.
/// </summary>
public sealed record TypeOf(Node Type) : Expression {
    /// <summary>The type reference is a child so the type resolver visits it — without it
    /// the emitter's lookup has nothing to resolve and <c>typeof(System.String)</c> fails.</summary>
    public override IEnumerable<Node?> Children => [Type];

    /// <inheritdoc />
    public override string ToString() => $"typeof({Type})";
}