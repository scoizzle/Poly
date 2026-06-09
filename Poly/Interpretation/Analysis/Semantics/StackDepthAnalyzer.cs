using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Analysis.Semantics;

internal sealed class StackDepthAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<StackDepthAnalyzer>(node))
            return;

        var (push, pop) = node switch {
            // Leaf expressions — push exactly 1 value
            Constant or Default or Parameter or Variable => (1, 0),
            TypeCast or TypeAs or NullForgiving => (1, 0),

            // Pure unary — push operands, then pop 0 (no operation pop)
            Not or UnaryMinus => ComputeChildren(context, node) with { Push = 0 },

            // Pure binary — push both operands, pop 2 for the operation
            Add or Subtract or Multiply or Divide or Modulo
                or Equal or NotEqual or LessThan or LessThanOrEqual
                or GreaterThan or GreaterThanOrEqual
                or And or Or => ComputeChildren(context, node) with { Push = 0, Pop = 2 },

            // Conditional(A, B, C) — A pushes, B or C pushes, op pops A (condition)
            Conditional => ComputeChildren(context, node) with { Pop = 1 },

            // Invoke — args push, op pops all + pushes return
            Invoke invoke => ComputeInvoke(context, invoke),

            // Member / IndexAccess / New — children push, op pops and pushes result
            Member or IndexAccess or New => ComputeChildren(context, node) with { Push = 0, Pop = 1 },

            // Assignment — value pushes, then Dup + Store keeps 1 on stack
            Assignment => ComputeChildren(context, node) with { Push = 1, Pop = 0 },

            // Block — sum of children, pop for unused intermediate results
            Block block => ComputeBlock(context, block),

            // Control flow — balanced (push same as pop)
            IfStatement ifStmt => ComputeIf(context, ifStmt),
            WhileLoop => (0, 0),
            DoWhileLoop => (0, 0),
            ForLoop fl => ComputeFor(context, fl),
            ForEachLoop => (0, 0),

            // Exceptions — balanced
            TryCatchFinally tcf => ComputeTry(context, tcf),
            ThrowStatement => (0, 1), // pops exception value, pushes nothing

            // Jumps — no net effect
            BreakStatement or ContinueStatement or GotoStatement or LabelDeclaration => (0, 0),
            Return => (0, 0),

            // Suspend — inner pushes, then Pop + Int
            SuspendNode => ComputeChildren(context, node) with { Push = 0, Pop = 1 },

            // Default to leaf
            _ => (1, 0),
        };

        context.SetMetadata(node, new StackDepthMetadata(push, pop));
        this.AnalyzeChildren(context, node);
    }

    private static (int Push, int Pop) ComputeChildren(AnalysisContext context, Node node) {
        int push = 0, pop = 0;
        foreach (var child in node.Children) {
            if (child is null) continue;
            var meta = context.GetMetadata<StackDepthMetadata>(child);
            if (meta is not null) {
                push += meta.Push;
                pop += meta.Pop;
            }
        }
        return (push, pop);
    }

    private static (int Push, int Pop) ComputeBlock(AnalysisContext context, Block block) {
        int push = 0, pop = 0;
        for (int i = 0; i < block.Nodes.Count; i++) {
            var meta = context.GetMetadata<StackDepthMetadata>(block.Nodes[i]);
            if (meta is null) continue;
            push += meta.Push;
            pop += meta.Pop;
            if (i < block.Nodes.Count - 1 && meta.Push > 0)
                pop += 1; // Block emits a Pop after non-last value-producing nodes
        }
        return (push, pop);
    }

    private static (int Push, int Pop) ComputeInvoke(AnalysisContext context, Invoke invoke) {
        int push = 0, pop = 0;
        // Delegate expression pushes closure / method reference
        if (invoke.Delegate is not null) {
            var delMeta = context.GetMetadata<StackDepthMetadata>(invoke.Delegate);
            if (delMeta is not null) { push += delMeta.Push; pop += delMeta.Pop; }
        }
        // Arguments push values
        foreach (var arg in invoke.Arguments) {
            var argMeta = context.GetMetadata<StackDepthMetadata>(arg);
            if (argMeta is not null) { push += argMeta.Push; pop += argMeta.Pop; }
        }
        // Call pops all args + delegate, pushes 1 result
        int argCount = invoke.Arguments.Length + (invoke.Delegate is not null ? 1 : 0);
        pop += argCount;
        push += 1;
        return (push, pop);
    }

    private static (int Push, int Pop) ComputeIf(AnalysisContext context, IfStatement ifStmt) {
        var total = ComputeChildren(context, ifStmt.Condition);
        // Condition pushes 1, then if pops it
        int branchPush = 0;
        if (ifStmt.ThenBranch is not null) {
            var tm = context.GetMetadata<StackDepthMetadata>(ifStmt.ThenBranch);
            if (tm is not null) branchPush = tm.Push;
        }
        if (ifStmt.ElseBranch is not null) {
            var em = context.GetMetadata<StackDepthMetadata>(ifStmt.ElseBranch);
            if (em is not null && em.Push > branchPush) branchPush = em.Push;
        }
        return (total.Push - total.Pop + branchPush, 0);
    }

    private static (int Push, int Pop) ComputeFor(AnalysisContext context, ForLoop fl) {
        (int Push, int Pop) total = fl.Condition is not null
            ? ComputeChildren(context, fl.Condition) : (0, 0);
        if (fl.Body is not null) {
            var bm = context.GetMetadata<StackDepthMetadata>(fl.Body);
            if (bm is not null)
                total = (total.Push + bm.Push, total.Pop + bm.Pop);
        }
        return total;
    }

    private static (int Push, int Pop) ComputeTry(AnalysisContext context, TryCatchFinally tcf) {
        var total = ComputeChildren(context, tcf.TryBlock);
        // Catches produce values too
        if (tcf.CatchClauses is not null) {
            foreach (var cc in tcf.CatchClauses) {
                var cm = context.GetMetadata<StackDepthMetadata>(cc.Body);
                if (cm is not null) total = (total.Push + cm.Push, total.Pop + cm.Pop);
            }
        }
        // Finally doesn't produce value
        return total;
    }
}

public sealed record StackDepthMetadata(int Push, int Pop) : IAnalysisMetadata;

public static class StackDepthExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseStackDepthAnalysis() {
            builder.AddAnalyzer(new StackDepthAnalyzer());
            return builder;
        }
    }
}