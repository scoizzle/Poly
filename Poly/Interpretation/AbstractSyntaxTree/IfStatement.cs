namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents an if statement that conditionally executes one of two branches.
/// </summary>
/// <remarks>
/// If the condition evaluates to true, the then-branch is executed; otherwise, the else-branch is executed (if present).
/// The else branch is optional. When both branches are present, they should have compatible types.
/// </remarks>
public sealed record IfStatement(Node Condition, Node ThenBranch, Node? ElseBranch = null) : Operator {
    public override IEnumerable<Node?> Children => [Condition, ThenBranch, ElseBranch];

    /// <inheritdoc />
    public override string ToString()
    {
        var result = $"if ({Condition}) {{ {ThenBranch} }}";
        if (ElseBranch is not null) {
            result += $" else {{ {ElseBranch} }}";
        }
        return result;
    }
}