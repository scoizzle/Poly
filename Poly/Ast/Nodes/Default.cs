namespace Poly.Ast.Nodes;

public sealed record Default(Node? TargetType = null) : Expression {
    public override string ToString() => TargetType is not null ? $"default({TargetType})" : "default";

}