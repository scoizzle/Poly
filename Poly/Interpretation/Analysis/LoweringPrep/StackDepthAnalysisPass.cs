namespace Poly.Interpretation.Analysis.LoweringPrep;

using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

/// <summary>
/// Walks the AST bottom-up, computing entry/exit stack depth for every node.
/// Stores the result as <see cref="StackDepthMetadata"/> on each node.
/// </summary>
internal sealed class StackDepthAnalysisPass : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<StackDepthAnalysisPass>(node))
            return;

        this.AnalyzeChildren(context, node);

        var (entry, exit) = ComputeDepth(context, node);
        context.SetMetadata(node, new StackDepthMetadata(entry, exit));
    }

    private static (int Entry, int Exit) ComputeDepth(AnalysisContext context, Node node) {
        return node switch {
            // ── Leaf / simple-value nodes ──
            Constant or Variable or ThisReference
                or Default or SuspendNode or Await
                or Coalesce or NullForgiving
                or TypeCast or TypeAs or TypeIs => (0, 1),

            // Parameter with default: emits the default expression (no read from slot).
            // Parameter without default: emits LoadSlot (reads from argument slot).
            Parameter p => p.DefaultValue is not null ? (0, 1) : (1, 1),

            // ── Binary: pop 2, push 1 ──
            Add or Subtract or Multiply or Divide or Modulo
                or Equal or NotEqual or LessThan or LessThanOrEqual
                or GreaterThan or GreaterThanOrEqual
                or And or Or
                or BitwiseAnd or BitwiseOr or BitwiseXor
                or ShiftLeft or ShiftRight => (0, 1),

            // ── Unary: pop 1, push 1 ──
            UnaryMinus or Not or BitwiseNot => (0, 1),

            // ── Compound expressions ──
            Member => (0, 1),
            IndexAccess ia => (0, 1),
            Invoke inv => (0, 1),
            New n => (0, 1),
            NewArray na => (0, 1),
            Assignment a => (0, 1),
            Lambda lam => (0, 1),

            // ── Control flow ──
            WhileLoop or DoWhileLoop or ForLoop or ForEachLoop => (0, 0),
            Conditional => (0, 1),
            IfStatement ifs => ComputeIfStatement(context, ifs),
            Block b => ComputeBlock(context, b),

            // ── Statements ──
            Return r => (r.Value is not null ? 1 : 0, 0),
            BreakStatement or ContinueStatement or GotoStatement => (0, 0),
            ThrowStatement => (1, 0),
            UsingStatement => (0, 0),
            SwitchStatement sw => (0, 1),
            TryCatchFinally tcf => ComputeTryCatch(context, tcf),
            LabelDeclaration ld => (0, 0),

            // ── Default: walk children ──
            _ => AggregateDepths(context, node),
        };
    }

    // ── Block ───────────────────────────────────────────────────────────

    private static (int Entry, int Exit) ComputeBlock(AnalysisContext context, Block block) {
        int total = 0;

        for (int i = 0; i < block.Nodes.Count; i++) {
            var child = block.Nodes[i];
            var depth = context.GetMetadata<StackDepthMetadata>(child);
            if (depth is null) continue;

            int net = depth.ExitDepth - depth.EntryDepth;

            // PopOp after non-last children (except loops, which have no net push).
            if (i < block.Nodes.Count - 1 && child is not (WhileLoop or DoWhileLoop or ForLoop))
                net--;

            total += net;
        }

        return (0, total);
    }

    // ── IfStatement ─────────────────────────────────────────────────────

    private static (int Entry, int Exit) ComputeIfStatement(AnalysisContext context, IfStatement ifs) {
        var thenDepth = context.GetMetadata<StackDepthMetadata>(ifs.ThenBranch);

        int thenNet = thenDepth?.ExitDepth ?? 0;

        if (ifs.ElseBranch is not null) {
            var elseDepth = context.GetMetadata<StackDepthMetadata>(ifs.ElseBranch);
            int elseNet = elseDepth?.ExitDepth ?? 0;
            return (0, Math.Max(thenNet, elseNet));
        }

        return (0, 0);
    }

    // ── TryCatchFinally ─────────────────────────────────────────────────

    private static (int Entry, int Exit) ComputeTryCatch(AnalysisContext context, TryCatchFinally tcf) {
        var bodyDepth = context.GetMetadata<StackDepthMetadata>(tcf.TryBlock);
        int bodyNet = bodyDepth?.ExitDepth ?? 0;

        int maxNet = bodyNet;
        if (tcf.CatchClauses is not null) {
            foreach (var c in tcf.CatchClauses) {
                var catchDepth = context.GetMetadata<StackDepthMetadata>(c.Body);
                if (catchDepth is not null)
                    maxNet = Math.Max(maxNet, catchDepth.ExitDepth);
            }
        }

        return (0, maxNet);
    }

    // ── Default: aggregate children ─────────────────────────────────────

    private static (int Entry, int Exit) AggregateDepths(AnalysisContext context, Node node) {
        int total = 0;
        foreach (var child in node.Children) {
            if (child is null) continue;
            var depth = context.GetMetadata<StackDepthMetadata>(child);
            if (depth is not null)
                total += depth.ExitDepth - depth.EntryDepth;
        }
        return (0, total);
    }
}

public static class StackDepthAnalysisExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseStackDepthAnalysis() {
            builder.AddAnalyzer(new StackDepthAnalysisPass());
            return builder;
        }
    }
}