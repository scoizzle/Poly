namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an if statement that conditionally executes one of two branches.
/// </summary>
/// <remarks>
/// If the condition evaluates to true, the then-branch is executed; otherwise, the else-branch is executed (if present).
/// The else branch is optional. When both branches are present, they should have compatible types.
/// </remarks>
public sealed record IfStatement(Node Condition, Node ThenBranch, Node? ElseBranch = null) : Statement {
    public override IEnumerable<Node?> Children => [Condition, ThenBranch, ElseBranch];

    /// <inheritdoc />
    public override string ToString() {
        var result = $"if ({Condition}) {{ {ThenBranch} }}";
        if (ElseBranch is not null) {
            result += $" else {{ {ElseBranch} }}";
        }
        return result;
    }

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        var elseLabel = new Poly.Syntax.Primitives.Label("else");

        // Condition — CondGoto jumps when the value is 0 (false)
        foreach (var p in Condition.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.CondGoto(elseLabel);

        // Then branch (returns directly — no merge to avoid PHI)
        foreach (var p in ThenBranch.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.Return();

        // Else branch (returns directly)
        yield return elseLabel;
        if (ElseBranch is not null) {
            foreach (var p in ElseBranch.ToPrimitives(context))
                yield return p;
        }
        else {
            // No else: push 0 as a default return value
            yield return new Poly.Syntax.Primitives.PushConstant(0L);
        }
        yield return new Poly.Syntax.Primitives.Return();
    }
}