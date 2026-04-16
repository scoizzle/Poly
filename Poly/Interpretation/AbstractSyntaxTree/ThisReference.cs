namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents an implicit reference to the current instance inside an instance member body.
/// </summary>
public sealed record ThisReference : Node {
    public override string ToString() => "this";
}