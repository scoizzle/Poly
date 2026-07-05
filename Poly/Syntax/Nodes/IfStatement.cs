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
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        var elseLabel = new Primitives.Label("else");
        var mergeLabel = new Primitives.Label("merge");

        var env = context.Env;

        // Condition — CondGoto jumps when the value is 0 (false)
        foreach (var p in Condition.ToPrimitives(context))
            yield return p;
        yield return new Primitives.CondGoto(elseLabel);

        if (env.IsInStatementContext) {
            // Statement context — result not needed, just execute branches
            // for side effects. No temp-slot, no LoadLocal at merge.
            foreach (var p in ThenBranch.ToPrimitives(context))
                yield return p;
            yield return new Primitives.Goto(mergeLabel);

            yield return elseLabel;
            if (ElseBranch is not null) {
                foreach (var p in ElseBranch.ToPrimitives(context))
                    yield return p;
            }

            yield return mergeLabel;
        }
        else {
            // Expression context — both branches produce one value.
            // Branch-aware ring analysis ensures they converge at the
            // same ring depth — no StoreLocal/LoadLocal needed.
            foreach (var p in ThenBranch.ToPrimitives(context))
                yield return p;
            yield return new Primitives.Goto(mergeLabel);

            // Else branch — leaves value at same ring depth
            yield return elseLabel;
            if (ElseBranch is not null) {
                foreach (var p in ElseBranch.ToPrimitives(context))
                    yield return p;
            }
            else {
                yield return new Primitives.PushConstant(0L);
            }

            // Merge: Phi annotation.
            yield return mergeLabel;
            yield return new Primitives.Phi();
        }
    }
}