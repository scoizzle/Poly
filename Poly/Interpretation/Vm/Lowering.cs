using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm.Instructions;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Vm;

/// <summary>Pure AST → µop transformation. Uses analysis metadata for
/// variable scope, constant folding, side effects, and control flow.</summary>
public static class Lowering {
    public static LoweringResult Lower(Node node, AnalysisResult analysis) {
        var ctx = new LowerCtx(analysis);
        EmitNode(node, ctx);
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
        // Check for analysis-pass node replacements
        if (ctx.Analysis.GetNodeReplacement(node) is Node replacement) {
            EmitNode(replacement, ctx);
            return;
        }

        switch (node) {
            case Constant c: EmitConstant(c, ctx); return;

            case Add a: EmitBinary(a.LeftHandValue, a.RightHandValue, BinOpKind.Add, ctx, a); return;
            case Subtract s: EmitBinary(s.LeftHandValue, s.RightHandValue, BinOpKind.Sub, ctx, s); return;
            case Multiply m: EmitBinary(m.LeftHandValue, m.RightHandValue, BinOpKind.Mul, ctx, m); return;
            case Divide d: EmitBinary(d.LeftHandValue, d.RightHandValue, BinOpKind.Div, ctx, d); return;
            case Modulo mo: EmitBinary(mo.LeftHandValue, mo.RightHandValue, BinOpKind.Mod, ctx, mo); return;

            case Equal e: EmitBinary(e.LeftHandValue, e.RightHandValue, BinOpKind.Eq, ctx, e); return;
            case NotEqual ne: EmitBinary(ne.LeftHandValue, ne.RightHandValue, BinOpKind.Ne, ctx, ne); return;
            case LessThan lt: EmitBinary(lt.LeftHandValue, lt.RightHandValue, BinOpKind.Lt, ctx, lt); return;
            case LessThanOrEqual le: EmitBinary(le.LeftHandValue, le.RightHandValue, BinOpKind.Le, ctx, le); return;
            case GreaterThan gt: EmitBinary(gt.LeftHandValue, gt.RightHandValue, BinOpKind.Gt, ctx, gt); return;
            case GreaterThanOrEqual ge: EmitBinary(ge.LeftHandValue, ge.RightHandValue, BinOpKind.Ge, ctx, ge); return;

            case And and: EmitBinary(and.LeftHandValue, and.RightHandValue, BinOpKind.And, ctx, and); return;
            case Or or: EmitBinary(or.LeftHandValue, or.RightHandValue, BinOpKind.Or, ctx, or); return;

            case UnaryMinus um: EmitNode(um.Operand, ctx); ctx.Instructions.Add(new BinOp(BinOpKind.Sub, Immediate: 0) { SourceNodeId = um.Id }); return;
            case Not n: EmitUnary(n.Value, UnaryOpKind.Not, ctx, n); return;

            case BitwiseNot bn: EmitUnary(bn.Operand, UnaryOpKind.BitNot, ctx, bn); return;
            case BitwiseAnd ba: EmitBinary(ba.LeftHandValue, ba.RightHandValue, BinOpKind.And, ctx, ba); return;
            case BitwiseOr bor: EmitBinary(bor.LeftHandValue, bor.RightHandValue, BinOpKind.Or, ctx, bor); return;
            case BitwiseXor bx: EmitBinary(bx.LeftHandValue, bx.RightHandValue, BinOpKind.Xor, ctx, bx); return;
            case ShiftLeft sl: EmitBinary(sl.LeftHandValue, sl.RightHandValue, BinOpKind.Shl, ctx, sl); return;
            case ShiftRight sr: EmitBinary(sr.LeftHandValue, sr.RightHandValue, BinOpKind.Shr, ctx, sr); return;

            case Variable v: EmitVariable(v, ctx); return;
            case Parameter p: EmitParameter(p, ctx); return;
            case ThisReference: ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = node.Id }); return;

            case Assignment a: EmitAssignment(a, ctx); return;

            case IfStatement iff: EmitIfStatement(iff, ctx); return;
            case WhileLoop wl: EmitWhileLoop(wl, ctx); return;
            case Conditional cond: EmitConditional(cond, ctx); return;
            case Block block: EmitBlock(block, ctx); return;

            case Lambda lam: EmitLambda(lam, ctx); return;
            case Invoke inv: EmitInvoke(inv, ctx); return;
            case Return ret: EmitReturn(ret, ctx); return;

            case BreakStatement: ctx.Instructions.Add(new Jump(0) { SourceNodeId = node.Id }); return;
            case ContinueStatement: ctx.Instructions.Add(new Jump(0) { SourceNodeId = node.Id }); return;

            case TypeCast tc: EmitNode(tc.Operand, ctx); return;
            case TypeIs ti: EmitNode(ti.Operand, ctx); ctx.Instructions.Add(new LoadConst(1L) { SourceNodeId = node.Id }); return;

            case Member m: EmitMember(m, ctx); return;
            case IndexAccess ia: EmitIndexAccess(ia, ctx); return;
            case New n:
                foreach (var arg in n.Arguments) EmitNode(arg, ctx);
                ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = node.Id });
                return;
            case NewArray na:
                EmitNode(na.Length, ctx);
                ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = node.Id });
                return;
            case DoWhileLoop dw: EmitWhileLoop(new WhileLoop(dw.Condition ?? new Constant(0L), dw.Body) { Id = dw.Id }, ctx); return;
            case ForLoop fl: EmitForLoop(fl, ctx); return;
            case ThrowStatement thr: EmitNode(thr.Exception, ctx); return;
            case SuspendNode sn: EmitNode(sn.Inner, ctx); return;
            case Await aw: EmitNode(aw.Operand, ctx); return;

            default:
                ctx.Instructions.Add(new Nop { SourceNodeId = node.Id });
                return;
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

    private static void EmitShortCircuit(Node left, Node right, bool isOr, LowerCtx ctx, Node source) {
        // Short-circuit: for OR (isOr=true), if left is true, skip right.
        // For AND (isOr=false), if left is false, skip right.
        int skipRight = ctx.DefineLabel();
        int end = ctx.DefineLabel();
        EmitNode(left, ctx);
        ctx.Instructions.Add(new DupOp { SourceNodeId = source.Id });
        if (isOr) {
            // OR: if left is true (non-zero), skip right
            ctx.EmitBranchIfFalse(skipRight, source.Id);
            ctx.Instructions.Add(new PopOp { SourceNodeId = source.Id });
            ctx.EmitJump(end, source.Id);
        }
        else {
            // AND: if left is false (zero), skip right
            ctx.EmitBranchIfFalse(end, source.Id);
            ctx.Instructions.Add(new PopOp { SourceNodeId = source.Id });
        }
        ctx.MarkLabel(skipRight);
        EmitNode(right, ctx);
        ctx.MarkLabel(end);
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
        else if (a.Destination is Member m) {
            EmitNode(m.Value, ctx);
            ctx.Instructions.Add(new PopOp { SourceNodeId = a.Id });
            ctx.Instructions.Add(new StoreSlot(ctx.GetOrCreateLocalSlot(m.MemberName)) { SourceNodeId = a.Id });
        }
        else {
            ctx.Instructions.Add(new PopOp { SourceNodeId = a.Id });
        }
    }

    private static void EmitBlock(Block block, LowerCtx ctx) {
        foreach (var v in block.Variables) {
            if (v is Variable var && !ctx.Parameters.ContainsKey(var.Name) && !ctx.Locals.ContainsKey(var.Name)) {
                ctx.Locals[var.Name] = ctx.Locals.Count;
                ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = var.Id });
                ctx.Instructions.Add(new StoreSlot(ctx.GetOrCreateLocalSlot(var.Name)) { SourceNodeId = var.Id });
            }
        }
        for (int i = 0; i < block.Nodes.Count; i++) {
            EmitNode(block.Nodes[i], ctx);
            if (i < block.Nodes.Count - 1 && block.Nodes[i] is Poly.Syntax.Nodes.Expression)
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
            ctx.Instructions.Add(new PopOp { SourceNodeId = iff.Id });
            ctx.EmitJump(endLabel, iff.Id);
            ctx.MarkLabel(elseLabel);
            EmitNode(iff.ElseBranch, ctx);
            ctx.Instructions.Add(new PopOp { SourceNodeId = iff.Id });
            ctx.MarkLabel(endLabel);
        }
        else {
            int endLabel = ctx.DefineLabel();
            ctx.EmitBranchIfFalse(endLabel, iff.Id);
            EmitNode(iff.ThenBranch, ctx);
            ctx.Instructions.Add(new PopOp { SourceNodeId = iff.Id });
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
        var resolved = ctx.Analysis.GetResolvedMember(inv);
        if (resolved is ClrMethod clrMethod) {
            // Direct CLR method call — no call site indirection
            foreach (var arg in inv.Arguments)
                EmitNode(arg, ctx);
            bool isStatic = clrMethod.IsStatic;
            int argCount = clrMethod.MethodInfo.GetParameters().Length + (isStatic ? 0 : 1);
            ctx.Instructions.Add(new CallExternalDirect(
                clrMethod.MethodInfo, argCount, isStatic) { SourceNodeId = inv.Id });
            return;
        }
        if (resolved is ClrConstructor clrCtor) {
            // CLR constructor call — use ConstructorInfo.Invoke
            foreach (var arg in inv.Arguments)
                EmitNode(arg, ctx);
            // Constructors return void in the VM — we emit them via MemberInit
            ctx.Instructions.Add(new CallExternal(0, inv.Arguments.Length) { SourceNodeId = inv.Id });
            return;
        }
        if (resolved is ClrTypeProperty clrProp) {
            // Property getter access
            bool isStatic = clrProp.IsStatic;
            var getter = clrProp.PropertyInfo.GetGetMethod(nonPublic: true);
            if (getter is not null) {
                if (!isStatic)
                    EmitNode(inv.Delegate is Member m ? m.Value : new Constant(0L), ctx);
                int argCount = getter.GetParameters().Length + (isStatic ? 0 : 1);
                ctx.Instructions.Add(new CallExternalDirect(
                    getter, argCount, isStatic) { SourceNodeId = inv.Id });
                return;
            }
            // Fallback
            EmitNode(inv.Delegate is Member m2 ? m2.Value : new Constant(0L), ctx);
            ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = inv.Id });
            return;
        }

        if (inv.Delegate is Lambda lambda) {
            // Inline lambda
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
        else if (inv.Delegate is Member member) {
            foreach (var arg in inv.Arguments)
                EmitNode(arg, ctx);
            ctx.Instructions.Add(new Call(0, inv.Arguments.Length) { SourceNodeId = inv.Id });
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

    private static void EmitMember(Member m, LowerCtx ctx) {
        var resolved = ctx.Analysis.GetResolvedMember(m);
        if (resolved is ClrTypeProperty property) {
            var getter = property.PropertyInfo.GetGetMethod(nonPublic: true);
            if (getter is not null) {
                bool isStatic = property.IsStatic;
                if (!isStatic) EmitNode(m.Value, ctx);
                int argCount = getter.GetParameters().Length + (isStatic ? 0 : 1);
                ctx.Instructions.Add(new CallExternalDirect(
                    getter, argCount, isStatic) { SourceNodeId = m.Id });
                return;
            }
        }
        if (resolved is ClrMethod clrGetter) {
            bool isStatic = clrGetter.IsStatic;
            if (!isStatic) EmitNode(m.Value, ctx);
            int argCount = clrGetter.MethodInfo.GetParameters().Length + (isStatic ? 0 : 1);
            ctx.Instructions.Add(new CallExternalDirect(
                clrGetter.MethodInfo, argCount, isStatic) { SourceNodeId = m.Id });
            return;
        }
        // Fallback: push 0
        EmitNode(m.Value, ctx);
        ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = m.Id });
    }

    private static void EmitIndexAccess(IndexAccess ia, LowerCtx ctx) {
        EmitNode(ia.Value, ctx);
        foreach (var arg in ia.Arguments)
            EmitNode(arg, ctx);
        // Fallback: load via ArrayLoad
        ctx.Instructions.Add(new LoadConst(0L) { SourceNodeId = ia.Id });
    }

    private static void EmitForLoop(ForLoop fl, LowerCtx ctx) {
        if (fl.Initializer is not null) {
            EmitNode(fl.Initializer, ctx);
            ctx.Instructions.Add(new PopOp { SourceNodeId = fl.Id });
        }
        int cont = ctx.Instructions.Count;
        if (fl.Condition is not null) {
            EmitNode(fl.Condition, ctx);
            int endLabel = ctx.DefineLabel();
            ctx.EmitBranchIfFalse(endLabel, fl.Id);
        }
        EmitNode(fl.Body, ctx);
        ctx.Instructions.Add(new PopOp { SourceNodeId = fl.Id });
        if (fl.Increment is not null) {
            EmitNode(fl.Increment, ctx);
            ctx.Instructions.Add(new PopOp { SourceNodeId = fl.Id });
        }
        ctx.EmitJumpDirect(cont, fl.Id);
        if (fl.Condition is not null)
            ctx.MarkLabel(ctx.DefineLabel() - 1); // re-mark the end label
    }
}