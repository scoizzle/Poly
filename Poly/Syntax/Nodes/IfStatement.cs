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
        var env = context.GetMetadata<Poly.Syntax.Primitives.ExpansionEnvironment>(null);
        if (env is null) {
            env = new Poly.Syntax.Primitives.ExpansionEnvironment();
            context.SetMetadata<Poly.Syntax.Primitives.ExpansionEnvironment>(null, env);
        }
        int tempSlot = env.AllocateTempSlot();

        // Condition — CondGoto jumps when the value is 0 (false)
        foreach (var p in Condition.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.CondGoto(elseLabel);

        // Then branch: compute net push; only emit StoreLocal when the branch
        // actually produces a value (statements like StridedSetBits don't).
        var thenPrims = ThenBranch.ToPrimitives(context).ToList();
        int thenNetPush = 0;
        foreach (var p in thenPrims) {
            var (pop, push) = p.StackEffect;
            thenNetPush += push - pop;
        }
        foreach (var p in thenPrims)
            yield return p;
        if (thenNetPush > 0) {
            yield return new Poly.Syntax.Primitives.StoreLocal(tempSlot);
            yield return new Poly.Syntax.Primitives.Goto(mergeLabel);
        }

        // Else branch: compute net push; skip StoreLocal for statements.
        yield return elseLabel;
        List<Poly.Syntax.Primitives.PrimitiveNode> elsePrims;
        int elseNetPush;
        if (ElseBranch is not null) {
            var ep = ElseBranch.ToPrimitives(context).ToList();
            elsePrims = ep;
            elseNetPush = 0;
            foreach (var p in ep) {
                var (pop, push) = p.StackEffect;
                elseNetPush += push - pop;
            }
        }
        else {
            elsePrims = [new Poly.Syntax.Primitives.PushConstant(0L)];
            elseNetPush = 1;
        }
        foreach (var p in elsePrims)
            yield return p;
        if (elseNetPush > 0)
            yield return new Poly.Syntax.Primitives.StoreLocal(tempSlot);

        // Merge: load result when one branch stored one.
        yield return mergeLabel;
        if (thenNetPush > 0 || elseNetPush > 0)
            yield return new Poly.Syntax.Primitives.LoadLocal(tempSlot);
    }
}