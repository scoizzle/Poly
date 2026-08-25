namespace Poly.Ast.Nodes;

/// <summary>
/// Represents an implicit reference to the current instance inside an instance member body.
/// </summary>
public sealed record ThisReference : Expression {
    public override string ToString() => "this";

}