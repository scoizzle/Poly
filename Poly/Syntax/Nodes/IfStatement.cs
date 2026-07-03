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
        var mergeLabel = new Poly.Syntax.Primitives.Label("merge");

        // Use a temp slot to store the result (avoids PHI)
        var env = context.GetMetadata<Poly.Syntax.Primitives.ExpandEnv>(null);
        if (env is null) {
            env = new Poly.Syntax.Primitives.ExpandEnv();
            context.SetMetadata<Poly.Syntax.Primitives.ExpandEnv>(null, env);
        }
        int tempSlot = env.AllocateTempSlot();

        // Condition — CondGoto jumps when the value is 0 (false)
        foreach (var p in Condition.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.CondGoto(elseLabel);

        // Then branch: store result to temp slot
        foreach (var p in ThenBranch.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.StoreLocal(tempSlot);
        yield return new Poly.Syntax.Primitives.Goto(mergeLabel);

        // Else branch: store result to temp slot
        yield return elseLabel;
        if (ElseBranch is not null) {
            foreach (var p in ElseBranch.ToPrimitives(context))
                yield return p;
        }
        else {
            yield return new Poly.Syntax.Primitives.PushConstant(0L);
        }
        yield return new Poly.Syntax.Primitives.StoreLocal(tempSlot);

        // Merge: read result from temp slot
        yield return mergeLabel;
        yield return new Poly.Syntax.Primitives.LoadLocal(tempSlot);
    }
}