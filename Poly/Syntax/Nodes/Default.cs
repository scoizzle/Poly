namespace Poly.Syntax.Nodes;

public sealed record Default(Node? TargetType = null) : Node {
    public override string ToString() => TargetType is not null ? $"default({TargetType})" : "default";
}