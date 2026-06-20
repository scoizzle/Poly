using Poly.Interpretation.Vm.Instructions;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Vm;

/// <summary>Pure AST → µop transformation. Produces a flat instruction list
/// with forward label references resolved via post-processing.</summary>
public static class Lowering {
    public static LoweringResult Lower(Node node, AnalysisResult analysis) {
        var ctx = new LowerCtx(analysis);
        EmitNode(node, ctx);
        // Ensure a ReturnOp ends the program — top-level expressions
        // like Divide(Multiply(...), ...) don't go through Invoke/Lambda.
        if (ctx.Instructions.Count == 0 || ctx.Instructions[^1] is not ReturnOp)
            ctx.Instructions.Add(new ReturnOp { SourceNodeId = node.Id });
        ctx.ResolveLabels();
        return new LoweringResult(ctx.Instructions);
    }

    private sealed class LowerCtx {
        public List<Instruction> Instructions { get; } = [];
        public AnalysisResult Analysis { get; }

        // Label resolution
        private int _nextLabel;
        private readonly Dictionary<int, int> _labelPositions = [];
        private readonly List<(int InstIdx, bool IsBranch, int Label)> _forwardRefs = [];

        // Variable scope
        public Dictionary<string, int> Parameters { get; } = [];
        public Dictionary<string, int> Locals { get; } = [];
        public int CurrentArgSlots { get; set; }

        public LowerCtx(AnalysisResult analysis) {
            Analysis = analysis;
        }

        public int GetOrCreateLocalSlot(string name) {
            if (Parameters.TryGetValue(name, out int pIdx)) return pIdx;
            if (Locals.TryGetValue(name, out int lIdx)) return CurrentArgSlots + 1 + lIdx;
            lIdx = Locals.Count;
            Locals[name] = lIdx;
            return CurrentArgSlots + 1 + lIdx;
        }

        public int DefineLabel() {
            int label = _nextLabel++;
            _labelPositions[label] = -1;
            return label;
        }

        public void MarkLabel(int label) {
            _labelPositions[label] = Instructions.Count;
        }

        public void EmitBranchIfFalse(int label, NodeId? source) {
            _forwardRefs.Add((Instructions.Count, true, label));
            Instructions.Add(new BranchIfFalse(-1) { SourceNodeId = source });
        }

        public void EmitJump(int label, NodeId? source) {
            _forwardRefs.Add((Instructions.Count, false, label));
            Instructions.Add(new Jump(-1) { SourceNodeId = source });
        }

        public void EmitJumpDirect(int target, NodeId? source) {
            Instructions.Add(new Jump(target) { SourceNodeId = source });
        }

        public void ResolveLabels() {
            foreach (var (instIdx, isBranch, label) in _forwardRefs) {
                if (_labelPositions.TryGetValue(label, out int pos) && pos >= 0) {
                    Instructions[instIdx] = isBranch
                        ? new BranchIfFalse(pos) { SourceNodeId = Instructions[instIdx].SourceNodeId }
                        : new Jump(pos) { SourceNodeId = Instructions[instIdx].SourceNodeId };
                }
            }
        }
    }

    private static void EmitNode(Node node, LowerCtx ctx) {
        switch (node) {
            case Constant c: EmitConstant(c, ctx); return;

            // Arithmetic
            case Add a: EmitBinary(a.LeftHandValue, a.RightHandValue, BinOpKind.Add, ctx, a); return;
            case Subtract s: EmitBinary(s.LeftHandValue, s.RightHandValue, BinOpKind.Sub, ctx, s); return;
            case Multiply m: EmitBinary(m.LeftHandValue, m.RightHandValue, BinOpKind.Mul, ctx, m); return;
            case Divide d: EmitBinary(d.LeftHandValue, d.RightHandValue, BinOpKind.Div, ctx, d); return;
            case Modulo mo: EmitBinary(mo.LeftHandValue, mo.RightHandValue, BinOpKind.Mod, ctx, mo); return;

            // Comparison
            case Equal e: EmitBinary(e.LeftHandValue, e.RightHandValue, BinOpKind.Eq, ctx, e); return;
            case NotEqual ne: EmitBinary(ne.LeftHandValue, ne.RightHandValue, BinOpKind.Ne, ctx, ne); return;
            case LessThan lt: EmitBinary(lt.LeftHandValue, lt.RightHandValue, BinOpKind.Lt, ctx, lt); return;
            case LessThanOrEqual le: EmitBinary(le.LeftHandValue, le.RightHandValue, BinOpKind.Le, ctx, le); return;
            case GreaterThan gt: EmitBinary(gt.LeftHandValue, gt.RightHandValue, BinOpKind.Gt, ctx, gt); return;
            case GreaterThanOrEqual ge: EmitBinary(ge.LeftHandValue, ge.RightHandValue, BinOpKind.Ge, ctx, ge); return;

            // Logical (And/Or should use short-circuit; for now treat as binary)
            case And and: EmitBinary(and.LeftHandValue, and.RightHandValue, BinOpKind.And, ctx, and); return;
            case Or or: EmitBinary(or.LeftHandValue, or.RightHandValue, BinOpKind.Or, ctx, or); return;

            // Unary
            case Not not: EmitUnary(not.Value, UnaryOpKind.Not, ctx, not); return;
            case UnaryMinus um: EmitUnary(um.Operand, UnaryOpKind.Neg, ctx, um); return;

            // Bitwise
            case BitwiseNot bn: EmitUnary(bn.Operand, UnaryOpKind.BitNot, ctx, bn); return;
            case BitwiseAnd ba: EmitBinary(ba.LeftHandValue, ba.RightHandValue, BinOpKind.And, ctx, ba); return;
            case BitwiseOr bor: EmitBinary(bor.LeftHandValue, bor.RightHandValue, BinOpKind.Or, ctx, bor); return;
            case BitwiseXor bx: EmitBinary(bx.LeftHandValue, bx.RightHandValue, BinOpKind.Xor, ctx, bx); return;
            case ShiftLeft sl: EmitBinary(sl.LeftHandValue, sl.RightHandValue, BinOpKind.Shl, ctx, sl); return;
            case ShiftRight sr: EmitBinary(sr.LeftHandValue, sr.RightHandValue, BinOpKind.Shr, ctx, sr); return;

            // Variables and parameters
            case Variable v: EmitVariable(v, ctx); return;
            case Parameter p: EmitParameter(p, ctx); return;
            case ThisReference: ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = node.Id }); return;

            // Assignment
            case Assignment a: EmitAssignment(a, ctx); return;

            // Control flow
            case IfStatement iff: EmitIfStatement(iff, ctx); return;
            case WhileLoop wl: EmitWhileLoop(wl, ctx); return;
            case Conditional cond: EmitConditional(cond, ctx); return;
            case Block block: EmitBlock(block, ctx); return;

            // Functions and calls
            case Lambda lam: EmitLambda(lam, ctx); return;
            case Invoke inv: EmitInvoke(inv, ctx); return;
            case Return ret: EmitReturn(ret, ctx); return;

            // Jump statements
            case BreakStatement: ctx.Instructions.Add(new Jump(0) { SourceNodeId = node.Id }); return;
            case ContinueStatement: ctx.Instructions.Add(new Jump(0) { SourceNodeId = node.Id }); return;

            // Type operations — no-ops in typeless VM, emit operand
            case TypeCast tc: EmitNode(tc.Operand, ctx); return;
            case TypeIs ti: EmitNode(ti.Operand, ctx); ctx.Instructions.Add(new LoadConst(1L) { SourceNodeId = node.Id }); return;

            // Placeholder for complex constructs
            case Member m: EmitNode(m.Value, ctx); ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = node.Id }); return;
            case IndexAccess ia: EmitNode(ia.Value, ctx); ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = node.Id }); return;
            case New n: foreach (var arg in n.Arguments) EmitNode(arg, ctx); ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = node.Id }); return;
            case NewArray na: EmitNode(na.Length, ctx); ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = node.Id }); return;

            default: ctx.Instructions.Add(new Nop { SourceNodeId = node.Id }); return;
        }
    }

    private static void EmitConstant(Constant c, LowerCtx ctx) {
        if (c.Value is long lv) { ctx.Instructions.Add(new LoadConst(lv) { SourceNodeId = c.Id }); return; }
        if (c.Value is int iv) { ctx.Instructions.Add(new LoadConst(iv) { SourceNodeId = c.Id }); return; }
        if (c.Value is short sv) { ctx.Instructions.Add(new LoadConst((long)sv) { SourceNodeId = c.Id }); return; }
        if (c.Value is byte bv) { ctx.Instructions.Add(new LoadConst((long)bv) { SourceNodeId = c.Id }); return; }
        if (c.Value is bool bvv) { ctx.Instructions.Add(new LoadConst(bvv ? 1L : 0L) { SourceNodeId = c.Id }); return; }
        if (c.Value is uint uiv) { ctx.Instructions.Add(new LoadConst((long)uiv) { SourceNodeId = c.Id }); return; }
        ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = c.Id });
    }

    private static void EmitBinary(Node left, Node right, BinOpKind kind, LowerCtx ctx, Node source) {
        EmitNode(left, ctx);
        EmitNode(right, ctx);
        ctx.Instructions.Add(new BinOp(kind) { SourceNodeId = source.Id });
    }

    private static void EmitUnary(Node operand, UnaryOpKind kind, LowerCtx ctx, Node source) {
        EmitNode(operand, ctx);
        ctx.Instructions.Add(new UnaryOp(kind) { SourceNodeId = source.Id });
    }

    private static void EmitVariable(Variable v, LowerCtx ctx) {
        ctx.Instructions.Add(new LoadSlot(ctx.GetOrCreateLocalSlot(v.Name)) { SourceNodeId = v.Id });
    }

    private static void EmitParameter(Parameter p, LowerCtx ctx) {
        if (p.DefaultValue is not null) {
            EmitNode(p.DefaultValue, ctx);
        }
        else {
            ctx.Instructions.Add(new LoadSlot(ctx.GetOrCreateLocalSlot(p.Name ?? "")) { SourceNodeId = p.Id });
        }
    }

    private static void EmitAssignment(Assignment a, LowerCtx ctx) {
        EmitNode(a.Value, ctx);
        if (a.Destination is Variable v) {
            int slot = ctx.GetOrCreateLocalSlot(v.Name);
            ctx.Instructions.Add(new StoreSlot(slot) { SourceNodeId = a.Id });
            ctx.Instructions.Add(new LoadSlot(slot) { SourceNodeId = a.Id });
        }
        else if (a.Destination is IndexAccess ia) {
            EmitNode(ia.Value, ctx);
            EmitNode(ia.Arguments[0], ctx);
            ctx.Instructions.Add(new ArrayStore { SourceNodeId = a.Id });
        }
        else {
            ctx.Instructions.Add(new PopOp { SourceNodeId = a.Id });
        }
    }

    private static void EmitBlock(Block block, LowerCtx ctx) {
        // Register and initialize local variable declarations
        foreach (var v in block.Variables) {
            if (v is Variable var && !ctx.Parameters.ContainsKey(var.Name) && !ctx.Locals.ContainsKey(var.Name)) {
                ctx.Locals[var.Name] = ctx.Locals.Count;
                ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = var.Id });
                ctx.Instructions.Add(new StoreSlot(ctx.GetOrCreateLocalSlot(var.Name)) { SourceNodeId = var.Id });
            }
        }
        for (int i = 0; i < block.Nodes.Count; i++) {
            EmitNode(block.Nodes[i], ctx);
            if (i < block.Nodes.Count - 1)
                ctx.Instructions.Add(new PopOp { SourceNodeId = block.Id });
        }
    }

    private static void EmitIfStatement(IfStatement iff, LowerCtx ctx) {
        EmitNode(iff.Condition, ctx);
        if (iff.ElseBranch is not null) {
            int elseLabel = ctx.DefineLabel();
            int endLabel = ctx.DefineLabel();
            ctx.EmitBranchIfFalse(elseLabel, iff.Id);
            EmitNode(iff.ThenBranch, ctx);
            ctx.EmitJump(endLabel, iff.Id);
            ctx.MarkLabel(elseLabel);
            EmitNode(iff.ElseBranch, ctx);
            ctx.MarkLabel(endLabel);
        }
        else {
            int endLabel = ctx.DefineLabel();
            ctx.EmitBranchIfFalse(endLabel, iff.Id);
            EmitNode(iff.ThenBranch, ctx);
            ctx.MarkLabel(endLabel);
        }
    }

    private static void EmitWhileLoop(WhileLoop wl, LowerCtx ctx) {
        int cont = ctx.Instructions.Count;
        EmitNode(wl.Condition, ctx);
        int endLabel = ctx.DefineLabel();
        ctx.EmitBranchIfFalse(endLabel, wl.Id);
        EmitNode(wl.Body, ctx);
        ctx.Instructions.Add(new PopOp { SourceNodeId = wl.Id });
        ctx.EmitJumpDirect(cont, wl.Id);
        ctx.MarkLabel(endLabel);
    }

    private static void EmitConditional(Conditional cond, LowerCtx ctx) {
        EmitNode(cond.Condition, ctx);
        int falseLabel = ctx.DefineLabel();
        int endLabel = ctx.DefineLabel();
        ctx.EmitBranchIfFalse(falseLabel, cond.Id);
        EmitNode(cond.IfTrue, ctx);
        ctx.EmitJump(endLabel, cond.Id);
        ctx.MarkLabel(falseLabel);
        EmitNode(cond.IfFalse, ctx);
        ctx.MarkLabel(endLabel);
    }

    private static void EmitLambda(Lambda lam, LowerCtx ctx) {
        // Register parameters
        if (lam.Parameters is not null) {
            for (int i = 0; i < lam.Parameters.Count; i++) {
                var p = lam.Parameters[i];
                if (p.Name is { } name && !ctx.Parameters.ContainsKey(name))
                    ctx.Parameters[name] = i;
            }
        }
        ctx.CurrentArgSlots = lam.Parameters?.Count ?? 0;
        EmitNode(lam.Body, ctx);
        ctx.Instructions.Add(new ReturnOp { SourceNodeId = lam.Id });
    }

    private static void EmitInvoke(Invoke inv, LowerCtx ctx) {
        if (inv.Delegate is Lambda lambda) {
            // Inline lambda: store arguments to parameter slots,
            // then emit the body. The StoreSlot consumes the pushed value.
            int savedArgSlots = ctx.CurrentArgSlots;
            if (lambda.Parameters is not null) {
                for (int i = 0; i < lambda.Parameters.Count && i < inv.Arguments.Length; i++) {
                    var p = lambda.Parameters[i];
                    if (p.Name is { } name) {
                        if (!ctx.Parameters.ContainsKey(name))
                            ctx.Parameters[name] = i;
                        EmitNode(inv.Arguments[i], ctx);
                        ctx.Instructions.Add(new StoreSlot(ctx.GetOrCreateLocalSlot(name)) { SourceNodeId = inv.Id });
                    }
                }
            }
            ctx.CurrentArgSlots = lambda.Parameters?.Count ?? 0;
            EmitNode(lambda.Body, ctx);
            ctx.Instructions.Add(new ReturnOp { SourceNodeId = lambda.Id });
            ctx.CurrentArgSlots = savedArgSlots;
        }
        else {
            foreach (var arg in inv.Arguments)
                EmitNode(arg, ctx);
            ctx.Instructions.Add(new Call(0, inv.Arguments.Length) { SourceNodeId = inv.Id });
        }
    }

    private static void EmitReturn(Return ret, LowerCtx ctx) {
        if (ret.Value is not null)
            EmitNode(ret.Value, ctx);
        ctx.Instructions.Add(new ReturnOp { SourceNodeId = ret.Id });
    }
}