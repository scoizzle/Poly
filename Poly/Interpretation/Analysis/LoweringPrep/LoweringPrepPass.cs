namespace Poly.Interpretation.Analysis.LoweringPrep;

using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

/// <summary>
/// Unified lowering-preparation pass.  Assigns label IDs top-down, then
/// computes stack-entry/exit depths bottom-up, in a single tree walk.
/// Replaces the separate <see cref="StackDepthAnalysisPass"/> and
/// <see cref="LabelAssignmentPass"/> for callers that want both.
/// </summary>
internal sealed class LoweringPrepPass : INodeAnalyzer {
    // ── Label state ────────────────────────────────────────────────
    private int _nextLabel;
    private readonly Stack<LoopScope> _loopStack = new();

    // ── Analyze ─────────────────────────────────────────────────────

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<LoweringPrepPass>(node))
            return;

        // ── Top-down: assign labels ──
        switch (node) {
            case WhileLoop wl:
                AssignWhileLoopLabels(context, wl);
                break;
            case DoWhileLoop dwl:
                AssignDoWhileLoopLabels(context, dwl);
                break;
            case ForLoop fl:
                AssignForLoopLabels(context, fl);
                break;
            case IfStatement ifs:
                AssignIfLabels(context, ifs);
                break;
            case Conditional cond:
                AssignConditionalLabels(context, cond);
                break;
            case BreakStatement:
                AssignBreakLabel(context, node);
                return;
            case ContinueStatement:
                AssignContinueLabel(context, node);
                return;
        }

        // ── Recurse (children get labels, then depths) ──
        this.AnalyzeChildren(context, node);

        // ── Bottom-up: compute depth ──
        var (entry, exit) = ComputeDepth(context, node);
        context.SetMetadata(node, new StackDepthMetadata(entry, exit));
    }

    // ═══════════════════════════════════════════════════════════════
    //  LabelAssignment (top-down half)
    // ═══════════════════════════════════════════════════════════════

    private void AssignWhileLoopLabels(AnalysisContext context, WhileLoop wl) {
        int cont = Alloc();
        int end = Alloc();
        context.SetMetadata(wl, new WhileLoopLabelMetadata(cont, end));
        _loopStack.Push(new LoopScope(cont, end));
    }

    private void AssignDoWhileLoopLabels(AnalysisContext context, DoWhileLoop dwl) {
        int cont = Alloc();
        int end = Alloc();
        context.SetMetadata(dwl, new DoWhileLoopLabelMetadata(cont, end));
        _loopStack.Push(new LoopScope(cont, end));
    }

    private void AssignForLoopLabels(AnalysisContext context, ForLoop fl) {
        int cond = Alloc();
        int end = Alloc();
        context.SetMetadata(fl, new ForLoopLabelMetadata(cond, end));
        _loopStack.Push(new LoopScope(cond, end));
    }

    private void AssignIfLabels(AnalysisContext context, IfStatement ifs) {
        int? elseLabel = ifs.ElseBranch is not null ? Alloc() : null;
        int end = Alloc();
        context.SetMetadata(ifs, new IfLabelMetadata(elseLabel, end));
    }

    private void AssignConditionalLabels(AnalysisContext context, Conditional cond) {
        int falseLabel = Alloc();
        int end = Alloc();
        context.SetMetadata(cond, new ConditionalLabelMetadata(falseLabel, end));
    }

    private void AssignBreakLabel(AnalysisContext context, Node node) {
        if (_loopStack.Count > 0)
            context.SetMetadata(node, new BreakTargetMetadata(_loopStack.Peek().EndLabel));
    }

    private void AssignContinueLabel(AnalysisContext context, Node node) {
        if (_loopStack.Count > 0)
            context.SetMetadata(node, new ContinueTargetMetadata(_loopStack.Peek().ContLabel));
    }

    private int Alloc() => _nextLabel++;

    // ═══════════════════════════════════════════════════════════════
    //  StackDepthAnalysis (bottom-up half)
    // ═══════════════════════════════════════════════════════════════

    private static (int Entry, int Exit) ComputeDepth(AnalysisContext context, Node node) {
        return node switch {
            // ── Leaf / simple-value nodes ──
            Constant or Variable or ThisReference
                or Default or SuspendNode or Await
                or Coalesce or NullForgiving
                or TypeCast or TypeAs or TypeIs => (0, 1),

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
            Member or IndexAccess or Invoke or New or NewArray or Lambda => (0, 1),
            Assignment => (0, 1),

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
            SwitchStatement => (0, 1),
            TryCatchFinally tcf => ComputeTryCatch(context, tcf),
            LabelDeclaration => (0, 0),

            // ── Default ──
            _ => AggregateDepths(context, node),
        };
    }

    private static (int Entry, int Exit) ComputeBlock(AnalysisContext context, Block block) {
        int total = 0;
        for (int i = 0; i < block.Nodes.Count; i++) {
            var child = block.Nodes[i];
            var depth = context.GetMetadata<StackDepthMetadata>(child);
            if (depth is null) continue;

            int net = depth.ExitDepth - depth.EntryDepth;
            if (i < block.Nodes.Count - 1 && child is not (WhileLoop or DoWhileLoop or ForLoop))
                net--;

            total += net;
        }
        return (0, total);
    }

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

    private static (int Entry, int Exit) ComputeTryCatch(AnalysisContext context, TryCatchFinally tcf) {
        var bodyDepth = context.GetMetadata<StackDepthMetadata>(tcf.TryBlock);
        int maxNet = bodyDepth?.ExitDepth ?? 0;

        if (tcf.CatchClauses is not null) {
            foreach (var c in tcf.CatchClauses) {
                var d = context.GetMetadata<StackDepthMetadata>(c.Body);
                if (d is not null) maxNet = Math.Max(maxNet, d.ExitDepth);
            }
        }
        return (0, maxNet);
    }

    private static (int Entry, int Exit) AggregateDepths(AnalysisContext context, Node node) {
        int total = 0;
        foreach (var child in node.Children) {
            if (child is null) continue;
            var depth = context.GetMetadata<StackDepthMetadata>(child);
            if (depth is not null) total += depth.ExitDepth - depth.EntryDepth;
        }
        return (0, total);
    }
}

// ── Extension ──────────────────────────────────────────────────────

public static class LoweringPrepExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>Registers the unified lowering-preparation pass (depth + labels).</summary>
        public AnalyzerBuilder UseLoweringPreparation() {
            builder.AddAnalyzer(new LoweringPrepPass());
            return builder;
        }
    }
}