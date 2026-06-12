using System.Reflection;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>AST → µop lowering.  Each AST node produces one or more
/// <see cref="MicroOp"/> records.  The resulting µop list is compiled
/// directly by <see cref="ProgramCompiler.Compile"/> — there is no
/// intermediate bytecode format.</summary>
internal static class Lowering {
    private sealed record LambdaEmitState(
        IReadOnlyDictionary<Lambda, int>? FuncMap,
        IReadOnlyDictionary<Lambda, List<string>>? CaptureMap,
        IReadOnlyDictionary<string, int>? UpvalueMap
    );

    private static readonly HashSet<Type> _voidTypes = [typeof(void), typeof(ValueTuple), typeof(ValueTuple<>)];

    private ref struct EmitContext {
        public List<MicroOp> Code;
        public AnalysisResult Analysis;
        public List<FunctionEntry> Functions;
        public Dictionary<MethodDefinitionNode, int>? FunctionIndexMap;
        public IReadOnlyDictionary<string, int>? ParamIndexMap, LocalIndexMap;
        public List<object?>? Constants;
        public List<CallSiteDelegate>? CallSites;
        public List<string>? CallSiteTargets;
        public Dictionary<MethodInfo, int>? CallSiteCache;
        public Dictionary<Lambda, int>? LambdaFuncMap;
        public Dictionary<Lambda, List<string>>? LambdaCaptureMap;
        public IReadOnlyDictionary<string, int>? UpvalueMap;
        public List<ExceptionRegion> ExceptionRegions;
        public List<LoopBodyEntry>? LoopBodies;
        public int CurrentArgSlots;
        public Dictionary<int, int> LabelTargets;   // label name → µop index
    }

    public static Bytecode Lower(Node root, AnalysisResult analysis) {
        var ctx = new EmitContext {
            Code = [],
            Analysis = analysis,
            Functions = [],
            Constants = [],
            CallSites = [],
            CallSiteTargets = [],
            CallSiteCache = [],
            ExceptionRegions = [],
            LoopBodies = [],
            LabelTargets = [],
        };

        // Discover referenced functions and lambdas
        var referencedMethods = new List<MethodDefinitionNode>();
        DiscoverFunctions(root, analysis, referencedMethods);

        var referencedLambdas = new List<Lambda>();
        DiscoverLambdas(root, referencedLambdas);

        // Assign function indices and param maps for methods
        ctx.FunctionIndexMap = [];
        foreach (var method in referencedMethods) {
            int idx = ctx.Functions.Count;
            ctx.FunctionIndexMap[method] = idx;
            int paramCount = method.Parameters?.Count ?? 0;
            ctx.Functions.Add(new FunctionEntry(0, paramCount, 1, 0));
        }

        // Pre-scan lambdas: assign indices, compute captures
        ctx.LambdaFuncMap = [];
        ctx.LambdaCaptureMap = [];
        foreach (var lambda in referencedLambdas) {
            int idx = ctx.Functions.Count;
            ctx.LambdaFuncMap[lambda] = idx;
            ctx.Functions.Add(new FunctionEntry(0, (lambda.Parameters?.Count ?? 0) + 1, 1, 0));

            var captures = new List<string>();
            var scope = GetVariableScopeMeta(lambda.Body, analysis);
            if (scope is not null)
                DiscoverCapturesFromAnalysis(lambda, scope, ctx.ParamIndexMap, ctx.LocalIndexMap, ctx.FunctionIndexMap, null, new HashSet<Block>(), analysis, captures);
            ctx.LambdaCaptureMap[lambda] = captures;
        }

        // Emit main body
        if (referencedMethods.Count > 0) {
            var method = referencedMethods[0];
            var paramIndexMap = new Dictionary<string, int>();
            if (method.Parameters is not null) {
                int pi = 0;
                foreach (var p in method.Parameters)
                    paramIndexMap[p.Name ?? ""] = pi++;
            }

            int methodIdx = ctx.FunctionIndexMap[method];
            int entryUop = ctx.Code.Count;
            var bodyCtx = ctx;
            bodyCtx.ParamIndexMap = paramIndexMap;
            bodyCtx.CurrentArgSlots = paramIndexMap.Count;

            EmitNode(method.Body ?? method, ref bodyCtx, null);
            bodyCtx.Code.Add(new ReturnFromCallOp(bodyCtx.CurrentArgSlots));

            var func = ctx.Functions[methodIdx];
            ctx.Functions[methodIdx] = new FunctionEntry(entryUop, func.ArgSlots, func.RetSlots, 0) {
                SourceNode = method
            };
        }
        else {
            EmitNode(root, ref ctx, null);
            ctx.Code.Add(new ReturnOp());
        }

        // Emit lambda bodies
        foreach (var lambda in referencedLambdas) {
            var paramIndexMap = new Dictionary<string, int>();
            int idx = 1;
            if (lambda.Parameters is not null) {
                foreach (var p in lambda.Parameters)
                    paramIndexMap[p.Name ?? ""] = idx++;
            }

            var localIndexMap = new Dictionary<string, int>();
            var scope = GetVariableScopeMeta(lambda.Body, analysis);
            if (scope is not null)
                DiscoverLocalsFromAnalysis(lambda.Body, scope, paramIndexMap, localIndexMap);

            var upvalueMap = new Dictionary<string, int>();
            var captures = ctx.LambdaCaptureMap![lambda];
            for (int i = 0; i < captures.Count; i++)
                upvalueMap[captures[i]] = i;

            int lambdaIdx = ctx.LambdaFuncMap![lambda];
            int entryUop = ctx.Code.Count;
            var bodyCtx = ctx;
            bodyCtx.ParamIndexMap = paramIndexMap;
            bodyCtx.LocalIndexMap = localIndexMap;
            bodyCtx.UpvalueMap = upvalueMap;
            bodyCtx.CurrentArgSlots = paramIndexMap.Count + 1;

            // Zero-init locals
            foreach (var (name, lIdx) in localIndexMap) {
                var node = FindVariableInBody(lambda.Body, name);
                if (node is null || !IsDefinitelyAssigned(node, analysis)) {
                    bodyCtx.Code.Add(new PushOp(0L));
                    bodyCtx.Code.Add(new StoreLocalOp(lIdx));
                }
            }

            EmitNode(lambda.Body, ref bodyCtx, new LambdaEmitState(ctx.LambdaFuncMap, ctx.LambdaCaptureMap, upvalueMap));
            bodyCtx.Code.Add(new ReturnFromCallOp(bodyCtx.CurrentArgSlots));

            var lambdaFunc = ctx.Functions[lambdaIdx];
            ctx.Functions[lambdaIdx] = new FunctionEntry(entryUop, lambdaFunc.ArgSlots, lambdaFunc.RetSlots, localIndexMap.Count) {
                SourceNode = lambda
            };
        }

        // Resolve pending labels to µop indices
        ResolveLabels(ref ctx);

        return new Bytecode(ctx.Code, ctx.Functions, ctx.Constants, ctx.CallSites,
            ctx.CallSiteTargets, ctx.ExceptionRegions, null, analysis, ctx.LoopBodies);
    }

    private static void ResolveLabels(ref EmitContext ctx) {
        // Walk the µop list and fix up any JumpOp/JumpIfFalseOp targets
        // that were emitted as unresolved label indices.
        for (int i = 0; i < ctx.Code.Count; i++) {
            switch (ctx.Code[i]) {
                case JumpOp jmp when ctx.LabelTargets.TryGetValue(jmp.Target, out int target):
                    ctx.Code[i] = new JumpOp(target);
                    break;
                case JumpIfFalseOp jif when ctx.LabelTargets.TryGetValue(jif.Target, out int target):
                    ctx.Code[i] = new JumpIfFalseOp(target);
                    break;
            }
        }
    }

    private static int EmitLabel(ref EmitContext ctx) {
        int label = ctx.LabelTargets.Count;
        ctx.LabelTargets[label] = ctx.Code.Count;
        return label;
    }

    private static void EmitNode(Node node, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        switch (node) {
            case Constant c: {
                    if (c.Value is int iv) { ctx.Code.Add(new PushOp(iv)); return; }
                    if (c.Value is long lv) { ctx.Code.Add(new PushOp(lv)); return; }
                    if (c.Value is short sv) { ctx.Code.Add(new PushOp((long)sv)); return; }
                    if (c.Value is byte bv) { ctx.Code.Add(new PushOp((long)bv)); return; }
                    if (c.Value is bool bvv) { ctx.Code.Add(new PushOp(bvv ? 1L : 0L)); return; }
                    if (c.Value is uint uiv) { ctx.Code.Add(new PushOp((long)uiv)); return; }
                    // Heap-allocated constant
                    int constIdx = ctx.Constants!.Count;
                    ctx.Constants!.Add(c.Value);
                    ctx.Code.Add(new PushOp(constIdx));
                    return;
                }

            case Add a: EmitBinary(a.LeftHandValue, a.RightHandValue, static () => new AddOp(), ref ctx, lambdaState); return;
            case Subtract s: EmitBinary(s.LeftHandValue, s.RightHandValue, static () => new SubOp(), ref ctx, lambdaState); return;
            case Multiply m: EmitBinary(m.LeftHandValue, m.RightHandValue, static () => new MulOp(), ref ctx, lambdaState); return;
            case Divide d: EmitBinary(d.LeftHandValue, d.RightHandValue, static () => new DivOp(), ref ctx, lambdaState); return;
            case Modulo m: EmitDivRem(m.LeftHandValue, m.RightHandValue, ref ctx, lambdaState); return;

            case Equal e: EmitBinary(e.LeftHandValue, e.RightHandValue, static () => new EqOp(), ref ctx, lambdaState); return;
            case NotEqual ne: EmitBinary(ne.LeftHandValue, ne.RightHandValue, static () => new NeOp(), ref ctx, lambdaState); return;
            case LessThan lt: EmitBinary(lt.LeftHandValue, lt.RightHandValue, static () => new LtOp(), ref ctx, lambdaState); return;
            case LessThanOrEqual le: EmitBinary(le.LeftHandValue, le.RightHandValue, static () => new LeOp(), ref ctx, lambdaState); return;
            case GreaterThan gt: EmitBinary(gt.LeftHandValue, gt.RightHandValue, static () => new GtOp(), ref ctx, lambdaState); return;
            case GreaterThanOrEqual ge: EmitBinary(ge.LeftHandValue, ge.RightHandValue, static () => new GeOp(), ref ctx, lambdaState); return;

            case UnaryMinus um:
                if (TryGetConstantLong(um.Operand, out long negVal)) {
                    ctx.Code.Add(new PushOp(-negVal));
                }
                else {
                    EmitNode(um.Operand, ref ctx, lambdaState);
                    ctx.Code.Add(new NegOp());
                }
                return;

            case Not n:
                if (TryGetConstantLong(n.Value, out long notVal)) {
                    ctx.Code.Add(new PushOp(notVal == 0 ? 1L : 0L));
                }
                else {
                    EmitNode(n.Value, ref ctx, lambdaState);
                    ctx.Code.Add(new NotOp());
                }
                return;

            case BitwiseNot bn: EmitNode(bn.Operand, ref ctx, lambdaState); ctx.Code.Add(new BitNotOp()); return;
            case BitwiseAnd ba: EmitBinary(ba.LeftHandValue, ba.RightHandValue, static () => new BitAndOp(), ref ctx, lambdaState); return;
            case BitwiseOr bo: EmitBinary(bo.LeftHandValue, bo.RightHandValue, static () => new BitOrOp(), ref ctx, lambdaState); return;
            case BitwiseXor bx: EmitBinary(bx.LeftHandValue, bx.RightHandValue, static () => new BitXorOp(), ref ctx, lambdaState); return;
            case ShiftLeft sl: EmitBinary(sl.LeftHandValue, sl.RightHandValue, static () => new ShlOp(), ref ctx, lambdaState); return;
            case ShiftRight sr: EmitBinary(sr.LeftHandValue, sr.RightHandValue, static () => new ShrOp(), ref ctx, lambdaState); return;

            case And and: EmitShortCircuit(and.LeftHandValue, and.RightHandValue, false, ref ctx, lambdaState); return;
            case Or or: EmitShortCircuit(or.LeftHandValue, or.RightHandValue, true, ref ctx, lambdaState); return;

            case Variable v: EmitVariable(v, ref ctx, lambdaState); return;
            case Parameter p: EmitParameter(p, ref ctx, lambdaState); return;

            case Assignment assign: {
                    EmitNode(assign.Value, ref ctx, lambdaState);
                    if (EmitsValue(assign, ctx.Analysis))
                        ctx.Code.Add(new DupOp());
                    EmitVariableStore(assign.Destination, ref ctx, lambdaState);
                    return;
                }

            case Invoke invoke: EmitInvoke(invoke, ref ctx, lambdaState); return;
            case Lambda lam: EmitLambda(lam, ref ctx, lambdaState); return;
            case Return: ctx.Code.Add(new ReturnFromCallOp(ctx.CurrentArgSlots)); return;

            case Conditional cond: EmitConditional(cond, ref ctx, lambdaState); return;
            case IfStatement iff: EmitIfStatement(iff, ref ctx, lambdaState); return;
            case WhileLoop wl: EmitWhileLoop(wl, ref ctx, lambdaState); return;
            case DoWhileLoop dw: EmitDoWhileLoop(dw, ref ctx, lambdaState); return;
            case ForLoop fl: EmitForLoop(fl, ref ctx, lambdaState); return;
            case BreakStatement: ctx.Code.Add(new JumpOp(0)); return;
            case ContinueStatement: ctx.Code.Add(new JumpOp(0)); return;

            case Block block: {
                    for (int i = 0; i < block.Nodes.Count; i++) {
                        var child = block.Nodes[i];
                        bool isLast = i == block.Nodes.Count - 1 && EmitsValue(block, ctx.Analysis);
                        EmitNode(child, ref ctx, lambdaState);
                        if (!isLast && EmitsValue(child, ctx.Analysis))
                            ctx.Code.Add(new PopOp());
                    }
                    return;
                }

            case ThrowStatement thr: EmitNode(thr.Exception, ref ctx, lambdaState); ctx.Code.Add(new ThrowOp()); return;
            case TryCatchFinally tcf: EmitTryCatchFinally(tcf, ref ctx, lambdaState); return;
            case ForEachLoop fel: EmitForEachLoop(fel, ref ctx, lambdaState); return;
            case UsingStatement us: EmitUsingStatement(us, ref ctx, lambdaState); return;

            case Member m: EmitMember(m, ref ctx, lambdaState); return;
            case IndexAccess ia: EmitIndexAccess(ia, ref ctx, lambdaState); return;
            case New n: EmitNew(n, ref ctx, lambdaState); return;

            case Await aw: EmitAwait(aw, ref ctx, lambdaState); return;
            case SuspendNode sn: EmitSuspendNode(sn, ref ctx, lambdaState); return;

            default:
                throw new InvalidOperationException($"Lowering not yet implemented for {node.GetType().Name}");
        }
    }

    private static void EmitBinary(Node left, Node right, Func<MicroOp> makeOp, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        EmitNode(left, ref ctx, lambdaState);
        if (TryGetConstantLong(right, out long val)) {
            var op = makeOp();
            // Only fuse if there's a dedicated Imm variant
            if (op is AddOp or SubOp or MulOp or EqOp or NeOp or LtOp or LeOp or GtOp or GeOp) {
                ctx.Code.Add(op switch {
                    AddOp => new AddImmOp(val),
                    SubOp => new SubImmOp(val),
                    MulOp => new MulImmOp(val),
                    EqOp => new EqImmOp(val),
                    NeOp => new NeImmOp(val),
                    LtOp => new LtImmOp(val),
                    LeOp => new LeImmOp(val),
                    GtOp => new GtImmOp(val),
                    GeOp => new GeImmOp(val),
                    _ => op  // unreachable
                });
                return;
            }
            // No fused form — push constant as a regular value
            ctx.Code.Add(new PushOp(val));
            ctx.Code.Add(op);
        }
        else {
            EmitNode(right, ref ctx, lambdaState);
            ctx.Code.Add(makeOp());
        }
    }

    private static bool TryGetConstantLong(Node node, out long value) {
        if (node is Constant c) {
            if (c.Value is int iv) { value = iv; return true; }
            if (c.Value is long lv) { value = lv; return true; }
            if (c.Value is short sv) { value = sv; return true; }
            if (c.Value is byte bv) { value = bv; return true; }
            if (c.Value is uint uiv) { value = uiv; return true; }
            if (c.Value is bool bvv) { value = bvv ? 1 : 0; return true; }
        }
        value = 0;
        return false;
    }

    private static void EmitDivRem(Node left, Node right, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        EmitNode(left, ref ctx, lambdaState);
        EmitNode(right, ref ctx, lambdaState);
        ctx.Code.Add(new DivRemOp());
        ctx.Code.Add(new PopOp()); // discard remainder, keep quotient
    }

    private static void EmitShortCircuit(Node left, Node right, bool isOr, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        int end = EmitLabel(ref ctx);

        EmitNode(left, ref ctx, lambdaState);
        ctx.Code.Add(new DupOp());
        if (isOr) {
            // OR: if left is 0, eval right; otherwise short-circuit to left
            int evalRight = EmitLabel(ref ctx);
            ctx.Code.Add(new JumpIfFalseOp(evalRight));
            ctx.Code.Add(new PopOp());
            ctx.Code.Add(new JumpOp(end));
            ctx.LabelTargets[evalRight] = ctx.Code.Count;
            ctx.Code.Add(new PopOp()); // remove original left
        }
        else {
            // AND: if left is 0, skip right (result is left on stack)
            ctx.Code.Add(new JumpIfFalseOp(end));
            ctx.Code.Add(new PopOp()); // remove original left
        }
        EmitNode(right, ref ctx, lambdaState);
        ctx.LabelTargets[end] = ctx.Code.Count;
    }

    private static void EmitConditional(Conditional cond, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        int else_ = EmitLabel(ref ctx);
        int end = EmitLabel(ref ctx);

        EmitNode(cond.Condition, ref ctx, lambdaState);
        ctx.Code.Add(new JumpIfFalseOp(else_));
        EmitNode(cond.IfTrue, ref ctx, lambdaState);
        ctx.Code.Add(new JumpOp(end));
        ctx.LabelTargets[else_] = ctx.Code.Count;
        EmitNode(cond.IfFalse, ref ctx, lambdaState);
        ctx.LabelTargets[end] = ctx.Code.Count;
    }

    private static void EmitIfStatement(IfStatement iff, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        int end = EmitLabel(ref ctx);

        EmitNode(iff.Condition, ref ctx, lambdaState);
        if (iff.ElseBranch is not null) {
            int else_ = EmitLabel(ref ctx);
            ctx.Code.Add(new JumpIfFalseOp(else_));
            EmitNode(iff.ThenBranch, ref ctx, lambdaState);
            ctx.Code.Add(new JumpOp(end));
            ctx.LabelTargets[else_] = ctx.Code.Count;
            EmitNode(iff.ElseBranch, ref ctx, lambdaState);
        }
        else {
            ctx.Code.Add(new JumpIfFalseOp(end));
            EmitNode(iff.ThenBranch, ref ctx, lambdaState);
        }
        ctx.LabelTargets[end] = ctx.Code.Count;
    }

    private static void EmitWhileLoop(WhileLoop wl, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        int cont = EmitLabel(ref ctx);
        int end = EmitLabel(ref ctx);

        // Mark condition label
        ctx.LabelTargets[cont] = ctx.Code.Count;
        EmitNode(wl.Condition, ref ctx, lambdaState);
        ctx.Code.Add(new JumpIfFalseOp(end));
        int bodyStart = ctx.Code.Count;
        EmitNode(wl.Body, ref ctx, lambdaState);
        if (EmitsValue(wl.Body, ctx.Analysis))
            ctx.Code.Add(new PopOp());
        int bodyEnd = ctx.Code.Count;
        ctx.Code.Add(new JumpOp(cont));
        ctx.LabelTargets[end] = ctx.Code.Count;

        ctx.LoopBodies?.Add(new LoopBodyEntry(bodyStart, bodyEnd - bodyStart, cont, cont, end, wl.Body) {
            ParamIndexMap = ctx.ParamIndexMap,
            LocalIndexMap = ctx.LocalIndexMap,
        });
    }

    private static void EmitDoWhileLoop(DoWhileLoop dw, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        int cont = EmitLabel(ref ctx);
        int end = EmitLabel(ref ctx);

        ctx.LabelTargets[cont] = ctx.Code.Count;
        EmitNode(dw.Body, ref ctx, lambdaState);
        EmitNode(dw.Condition, ref ctx, lambdaState);
        ctx.Code.Add(new JumpIfFalseOp(end));
        ctx.Code.Add(new JumpOp(cont));
        ctx.LabelTargets[end] = ctx.Code.Count;
    }

    private static void EmitForLoop(ForLoop fl, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        int cont = EmitLabel(ref ctx);
        int end = EmitLabel(ref ctx);

        if (fl.Initializer is not null) {
            EmitNode(fl.Initializer, ref ctx, lambdaState);
            if (EmitsValue(fl.Initializer, ctx.Analysis))
                ctx.Code.Add(new PopOp());
        }
        ctx.LabelTargets[cont] = ctx.Code.Count;
        if (fl.Condition is not null) {
            EmitNode(fl.Condition, ref ctx, lambdaState);
            ctx.Code.Add(new JumpIfFalseOp(end));
        }
        int bodyStart = ctx.Code.Count;
        EmitNode(fl.Body, ref ctx, lambdaState);
        int bodyEnd = ctx.Code.Count;
        if (fl.Increment is not null) {
            EmitNode(fl.Increment, ref ctx, lambdaState);
            if (EmitsValue(fl.Increment, ctx.Analysis))
                ctx.Code.Add(new PopOp());
        }
        ctx.Code.Add(new JumpOp(cont));
        ctx.LabelTargets[end] = ctx.Code.Count;

        ctx.LoopBodies?.Add(new LoopBodyEntry(bodyStart, bodyEnd - bodyStart, cont, ctx.Code.Count, end, fl.Body) {
            ParamIndexMap = ctx.ParamIndexMap,
            LocalIndexMap = ctx.LocalIndexMap,
        });
    }

    private static void EmitVariable(Variable v, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        if (ctx.ParamIndexMap?.TryGetValue(v.Name, out int pIdx) == true) {
            ctx.Code.Add(new LoadArgOp(pIdx));
            return;
        }
        if (ctx.LocalIndexMap?.TryGetValue(v.Name, out int lIdx) == true) {
            ctx.Code.Add(new LoadLocalOp(lIdx));
            return;
        }
        if (lambdaState?.UpvalueMap?.TryGetValue(v.Name, out int uIdx) == true) {
            ctx.Code.Add(new LoadUpvalueOp(uIdx));
            return;
        }
        throw new InvalidOperationException($"Variable '{v.Name}' not found in any scope");
    }

    private static void EmitParameter(Parameter p, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        if (p.DefaultValue is not null) {
            EmitNode(p.DefaultValue, ref ctx, lambdaState);
        }
        else if (ctx.ParamIndexMap?.TryGetValue(p.Name ?? "", out int pIdx) == true) {
            ctx.Code.Add(new LoadArgOp(pIdx));
        }
        else {
            ctx.Code.Add(new PushOp(0L));
        }
    }

    private static void EmitVariableStore(Node target, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        if (target is Variable v) {
            if (ctx.ParamIndexMap?.TryGetValue(v.Name, out int pIdx) == true) {
                ctx.Code.Add(new StoreArgOp(pIdx));
                return;
            }
            if (ctx.LocalIndexMap?.TryGetValue(v.Name, out int lIdx) == true) {
                ctx.Code.Add(new StoreLocalOp(lIdx));
                return;
            }
            if (lambdaState?.UpvalueMap?.TryGetValue(v.Name, out int uIdx) == true) {
                ctx.Code.Add(new StoreUpvalueOp(uIdx));
                return;
            }
            throw new InvalidOperationException($"Store target '{v.Name}' not found");
        }
        if (target is IndexAccess ia) {
            EmitIndexAccessStore(ia, ref ctx, lambdaState);
            return;
        }
        if (target is Member m) {
            EmitMemberStore(m, ref ctx, lambdaState);
            return;
        }
        throw new InvalidOperationException($"Unsupported assignment target: {target.GetType().Name}");
    }

    private static void EmitInvoke(Invoke invoke, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis.GetResolvedMember(invoke);
        var args = invoke.Arguments;

        if (resolved is AstMethodDefinition astMethod) {
            int funcIdx = ctx.FunctionIndexMap!.TryGetValue(astMethod.DefinitionNode, out int idx) ? idx : 0;
            foreach (var arg in args)
                EmitNode(arg, ref ctx, lambdaState);
            ctx.Code.Add(new CallOp(funcIdx, args.Length));
            return;
        }

        if (resolved is ClrMethod clrMethod) {
            bool isStatic = clrMethod.LifetimeModifier == LifetimeModifier.Static;
            foreach (var arg in args)
                EmitNode(arg, ref ctx, lambdaState);
            int siteIdx = GetOrAddCallSite(ref ctx, clrMethod.MethodInfo, isStatic);
            ctx.Code.Add(new CallExternalOp(siteIdx));
            return;
        }

        if (invoke.Delegate is Lambda lambda && ctx.LambdaFuncMap!.TryGetValue(lambda, out int lambdaIdx)) {
            ctx.Code.Add(new PushOp(-1L)); // dummy closure handle at index 0
            foreach (var arg in args)
                EmitNode(arg, ref ctx, lambdaState);
            ctx.Code.Add(new CallOp(lambdaIdx, args.Length + 1));
            return;
        }

        // Generic lambda/delegate call via CallClosure
        EmitNode(invoke.Delegate, ref ctx, lambdaState);
        foreach (var arg in args)
            EmitNode(arg, ref ctx, lambdaState);
        ctx.Code.Add(new CallClosureOp());
    }

    private static void EmitLambda(Lambda lam, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        if (ctx.LambdaCaptureMap is null || !ctx.LambdaCaptureMap.TryGetValue(lam, out var captures))
            throw new InvalidOperationException("Lambda not found in capture map");

        int funcIdx = ctx.LambdaFuncMap!.TryGetValue(lam, out int idx) ? idx : 0;

        foreach (var cap in captures) {
            if (ctx.ParamIndexMap?.TryGetValue(cap, out int pIdx) == true)
                ctx.Code.Add(new LoadArgOp(pIdx));
            else if (ctx.LocalIndexMap?.TryGetValue(cap, out int lIdx) == true)
                ctx.Code.Add(new LoadLocalOp(lIdx));
            else if (lambdaState?.UpvalueMap?.TryGetValue(cap, out int uIdx) == true)
                ctx.Code.Add(new LoadUpvalueOp(uIdx));
            else
                ctx.Code.Add(new PushOp(0L));
        }

        ctx.Code.Add(new AllocClosureOp(funcIdx, captures.Count));
    }

    private static void EmitTryCatchFinally(TryCatchFinally tcf, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        int end = EmitLabel(ref ctx);
        int? finallyEntry = null;
        int? catchStart = null;

        int tryStart = ctx.Code.Count;
        EmitNode(tcf.TryBlock, ref ctx, lambdaState);
        ctx.Code.Add(new JumpOp(end));
        int tryEnd = ctx.Code.Count;

        if (tcf.CatchClauses is not null) {
            catchStart = ctx.Code.Count;
            foreach (var cc in tcf.CatchClauses) {
                EmitLabel(ref ctx);
                if (cc.VariableName is not null) {
                    ctx.Code.Add(new DupOp());
                    if (ctx.ParamIndexMap?.TryGetValue(cc.VariableName, out int pi) == true)
                        ctx.Code.Add(new StoreArgOp(pi));
                    else if (ctx.LocalIndexMap?.TryGetValue(cc.VariableName, out int li) == true)
                        ctx.Code.Add(new StoreLocalOp(li));
                }
                else ctx.Code.Add(new PopOp());
                EmitNode(cc.Body, ref ctx, lambdaState);
                ctx.Code.Add(new JumpOp(end));
            }
        }

        if (tcf.FinallyBlock is not null) {
            finallyEntry = ctx.Code.Count;
            EmitNode(tcf.FinallyBlock, ref ctx, lambdaState);
            if (EmitsValue(tcf.FinallyBlock, ctx.Analysis))
                ctx.Code.Add(new PopOp());
            ctx.Code.Add(new EndFinallyOp());
        }

        ctx.LabelTargets[end] = ctx.Code.Count;
        ctx.ExceptionRegions.Add(new ExceptionRegion(tryStart, tryEnd, catchStart ?? -1, finallyEntry));
    }

    private static void EmitForEachLoop(ForEachLoop fel, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        EmitNode(fel.Collection, ref ctx, lambdaState);
        var getEnum = typeof(IEnumerable<>).MakeGenericType(typeof(object))
            .GetMethod("GetEnumerator")!
            .MakeGenericMethod(typeof(object));
        int initSite = AddCallSite(ref ctx, CallSiteCompiler.Compile(getEnum, false));
        ctx.Code.Add(new PushOp(1L));
        ctx.Code.Add(new CallExternalOp(initSite));

        int cont = EmitLabel(ref ctx);
        int end = EmitLabel(ref ctx);

        ctx.LabelTargets[cont] = ctx.Code.Count;
        ctx.Code.Add(new DupOp());
        int moveNextSite = AddCallSite(ref ctx, CallSiteCompiler.Compile(
            typeof(IEnumerator).GetMethod("MoveNext")!, false));
        ctx.Code.Add(new PushOp(1L));
        ctx.Code.Add(new CallExternalOp(moveNextSite));
        ctx.Code.Add(new JumpIfFalseOp(end));

        ctx.Code.Add(new DupOp());
        int currentSite = AddCallSite(ref ctx, CallSiteCompiler.Compile(
            typeof(IEnumerator).GetProperty("Current")!.GetGetMethod()!, false));
        ctx.Code.Add(new PushOp(1L));
        ctx.Code.Add(new CallExternalOp(currentSite));

        EmitVariableStore(fel.LoopVariable, ref ctx, lambdaState);
        EmitNode(fel.Body, ref ctx, lambdaState);
        if (EmitsValue(fel.Body, ctx.Analysis))
            ctx.Code.Add(new PopOp());
        ctx.Code.Add(new JumpOp(cont));
        ctx.LabelTargets[end] = ctx.Code.Count;
    }

    private static void EmitUsingStatement(UsingStatement us, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        int holderIdx = ctx.Constants!.Count;
        ctx.Constants.Add(new object[1]);

        EmitNode(us.Resource, ref ctx, lambdaState);
        ctx.Code.Add(new StoreValueOp());
        EmitNode(us.Body, ref ctx, lambdaState);
        if (EmitsValue(us.Body, ctx.Analysis))
            ctx.Code.Add(new PopOp());
    }

    private static void EmitMember(Member m, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis.GetResolvedMember(m);
        if (resolved is ClrMethod getter) {
            EmitNode(m.Value, ref ctx, lambdaState);
            int siteIdx = GetOrAddCallSite(ref ctx, getter.MethodInfo,
                getter.LifetimeModifier == LifetimeModifier.Static);
            ctx.Code.Add(new CallExternalOp(siteIdx));
            return;
        }
        throw new InvalidOperationException($"Member access not resolved: {m.MemberName}");
    }

    private static void EmitIndexAccess(IndexAccess ia, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis.GetResolvedMember(ia);
        if (resolved is ClrMethod getter) {
            EmitNode(ia.Value, ref ctx, lambdaState);
            foreach (var arg in ia.Arguments)
                EmitNode(arg, ref ctx, lambdaState);
            int siteIdx = GetOrAddCallSite(ref ctx, getter.MethodInfo,
                getter.LifetimeModifier == LifetimeModifier.Static);
            ctx.Code.Add(new CallExternalOp(siteIdx));
            return;
        }
        throw new InvalidOperationException($"Index access not resolved");
    }

    private static void EmitNew(New n, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis.GetResolvedMember(n);
        if (resolved is ClrConstructor ctor) {
            foreach (var arg in n.Arguments)
                EmitNode(arg, ref ctx, lambdaState);
            int siteIdx = AddCallSite(ref ctx,
                CallSiteCompiler.CompileConstructor(ctor.ConstructorInfo));
            ctx.Code.Add(new CallExternalOp(siteIdx));
            return;
        }
        throw new InvalidOperationException($"Constructor not resolved for new {n.Type}");
    }

    private static void EmitAwait(Await aw, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        EmitNode(aw.Operand, ref ctx, lambdaState);
        var getAwaiter = typeof(Task<>).GetMethod("GetAwaiter")?.MakeGenericMethod(typeof(object))
            ?? typeof(Task).GetMethod("GetAwaiter");
        if (getAwaiter is not null) {
            int siteIdx = AddCallSite(ref ctx, CallSiteCompiler.Compile(getAwaiter, false));
            ctx.Code.Add(new CallExternalOp(siteIdx));
        }
    }

    private static void EmitSuspendNode(SuspendNode sn, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        EmitNode(sn.Inner, ref ctx, lambdaState);
        ctx.Code.Add(new PopOp());
    }

    private static int AddCallSite(ref EmitContext ctx, CallSiteDelegate d) {
        int idx = ctx.CallSites!.Count;
        ctx.CallSites!.Add(d);
        ctx.CallSiteTargets!.Add(d.Method.ToString() ?? "");
        return idx;
    }

    private static int GetOrAddCallSite(ref EmitContext ctx, MethodInfo mi, bool isStatic) {
        if (ctx.CallSiteCache!.TryGetValue(mi, out int idx))
            return idx;
        idx = ctx.CallSites!.Count;
        ctx.CallSites!.Add(CallSiteCompiler.Compile(mi, isStatic));
        ctx.CallSiteCache[mi] = idx;
        ctx.CallSiteTargets!.Add(FormatMethodTarget(mi, isStatic));
        return idx;
    }

    private static string FormatMethodTarget(MethodInfo mi, bool isStatic) {
        var par = string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name));
        var ret = mi.ReturnType == typeof(void) ? "void" : mi.ReturnType.Name;
        var cls = mi.DeclaringType?.Name ?? "?";
        return $"{ret} {cls}.{mi.Name}({par})";
    }

    private static void EmitIndexAccessStore(IndexAccess ia, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis.GetResolvedMember(ia);
        if (resolved is not ClrMethod setter)
            throw new InvalidOperationException($"Index setter not found");

        int siteIdx = ctx.CallSites!.Count;
        bool isStatic = setter.LifetimeModifier == LifetimeModifier.Static;
        ctx.CallSites!.Add(CallSiteCompiler.Compile(setter.MethodInfo, isStatic));
        ctx.Code.Add(new CallExternalOp(siteIdx));
    }

    private static void EmitMemberStore(Member m, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis.GetResolvedMember(m);
        if (resolved is not ClrMethod setter)
            throw new InvalidOperationException($"Member setter not found for {m.MemberName}");

        int siteIdx = ctx.CallSites!.Count;
        bool isStatic = setter.LifetimeModifier == LifetimeModifier.Static;
        ctx.CallSites!.Add(CallSiteCompiler.Compile(setter.MethodInfo, isStatic));
        ctx.Code.Add(new CallExternalOp(siteIdx));
    }

    // ── Discovery helpers ──

    private static void DiscoverFunctions(Node node, AnalysisResult? analysis, List<MethodDefinitionNode> result) {
        if (node is Invoke invoke) {
            var resolved = analysis?.GetResolvedMember(invoke);
            if (resolved is AstMethodDefinition astNode) {
                var defNode = astNode.DefinitionNode;
                if (!result.Contains(defNode)) {
                    result.Add(defNode);
                    var body = defNode.Body ?? defNode;
                    DiscoverFunctions(body, analysis, result);
                }
            }
        }
        foreach (var child in node.Children) {
            if (child is not null)
                DiscoverFunctions(child, analysis, result);
        }
    }

    private static void DiscoverLambdas(Node node, List<Lambda> result) {
        if (node is Lambda lam && !result.Contains(lam))
            result.Add(lam);
        foreach (var child in node.Children) {
            if (child is not null)
                DiscoverLambdas(child, result);
        }
    }

    private static VariableScopeMetadata? GetVariableScopeMeta(Node body, AnalysisResult analysis) {
        if (analysis.GetMetadata<VariableScopeMetadata>(body) is { } meta)
            return meta;
        Variable? found = null;
        FindAnyVariable(body, ref found);
        return found is not null && analysis.GetMetadata<VariableScopeMetadata>(found) is { } m ? m : null;
    }

    private static void FindAnyVariable(Node node, ref Variable? result) {
        if (result is not null) return;
        if (node is Variable v) { result = v; return; }
        foreach (var child in node.Children) {
            if (child is not null) FindAnyVariable(child, ref result);
        }
    }

    private static void DiscoverLocalsFromAnalysis(Node body, VariableScopeMetadata scope, Dictionary<string, int> paramIndexMap, Dictionary<string, int> localIndexMap) {
        var names = new List<string>();
        foreach (var variable in scope.VariableReferences.Keys) {
            string name = variable.Name;
            if (!paramIndexMap.ContainsKey(name) && !localIndexMap.ContainsKey(name))
                names.Add(name);
        }
        names.Sort();
        foreach (var name in names)
            localIndexMap[name] = localIndexMap.Count;
    }

    private static void DiscoverCapturesFromAnalysis(Node lambdaBody, VariableScopeMetadata scope, IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, Dictionary<MethodDefinitionNode, int>? funcIndexMap, HashSet<Block>? parentBlocks, HashSet<Block> descendantBlocks, AnalysisResult analysis, List<string> captures) {
        foreach (var (variable, _) in scope.VariableReferences) {
            string name = variable.Name;
            bool isParam = paramIndexMap?.ContainsKey(name) == true;
            bool isLocal = localIndexMap?.ContainsKey(name) == true;
            if (!isParam && !isLocal && !captures.Contains(name))
                captures.Add(name);
        }
    }

    private static Variable? FindVariableInBody(Node body, string name) {
        Variable? result = null;
        Search(body);
        return result;

        void Search(Node n) {
            if (result is not null) return;
            if (n is Variable v && v.Name == name) { result = v; return; }
            foreach (var child in n.Children) {
                if (child is not null) Search(child);
            }
        }
    }

    private static bool IsDefinitelyAssigned(Node node, AnalysisResult analysis) {
        var meta = analysis.GetMetadata<DefiniteAssignmentMetadata>(node);
        return meta?.DefinitelyAssigned.Count > 0;
    }

    private static bool EmitsValue(Node node, AnalysisResult analysis) {
        if (node is null) return false;
        if (node is WhileLoop or DoWhileLoop or ForLoop) return false;
        if (node is Assignment) return true;
        var type = analysis.GetResolvedType(node);
        if (type is not null && type.Name != "Void")
            return true;
        if (node is Block block && block.Nodes.Count > 0)
            return EmitsValue(block.Nodes[^1], analysis);
        return false;
    }
}