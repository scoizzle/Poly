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
        // ── Alias ownership tracking ──
        public Dictionary<string, int> AssignmentCount;  // local name → assignment count
        public HashSet<string> EscapedLocals;            // local names that escape their scope
        /// <summary>True if a local can be aliased (assigned once, never escapes).</summary>
        public bool CanAlias(string name) =>
            AssignmentCount.GetValueOrDefault(name) == 1 && !EscapedLocals.Contains(name);
        /// <summary>Map from local variable name to alias name for alias-eligible locals.</summary>
        public Dictionary<string, string> LocalAliases;  // var name → alias name
        /// <summary>Set by Block before emitting a child when the child's value
        /// will be immediately popped.  Assignment case uses this to skip DupOp.</summary>
        public bool PoppingAssignmentValue;
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
            AssignmentCount = [],
            EscapedLocals = [],
            LocalAliases = [],
            PoppingAssignmentValue = false,
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
            // Temporarily add with +1 for potential closure handle
            ctx.Functions.Add(new FunctionEntry(0, (lambda.Parameters?.Count ?? 0) + 1, 1, 0));

            var captures = new List<string>();
            var scope = GetVariableScopeMeta(lambda.Body, analysis);
            if (scope is not null)
                DiscoverCapturesFromAnalysis(lambda, scope, ctx.ParamIndexMap, ctx.LocalIndexMap, ctx.FunctionIndexMap, null, new HashSet<Block>(), analysis, captures);
            ctx.LambdaCaptureMap[lambda] = captures;
        }

        // Pre-scan: collect assignment counts and escape info for alias analysis
        CollectEscapeInfo(root, ctx);

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

            // Zero-init locals (skip those definitely assigned in the body)
            var definiteInit = (lambda.Body is Block initBlock)
                ? analysis.GetMetadata<DefiniteAssignmentMetadata>(initBlock)
                : null;
            foreach (var (name, lIdx) in localIndexMap) {
                if (definiteInit is not null && definiteInit.DefinitelyAssigned.Contains(name))
                    continue;
                bodyCtx.Code.Add(new PushOp(0L));
                bodyCtx.Code.Add(new StoreLocalOp(lIdx));
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

        // Build NodeRanges from µop Source tracking
        var nodeRanges = new Dictionary<NodeId, (int, int)>();
        NodeId? currentId = null;
        int rangeStart = 0;
        for (int i = 0; i < ctx.Code.Count; i++) {
            var src = ctx.Code[i].Source;
            if (src != currentId) {
                if (currentId is not null)
                    nodeRanges[currentId.Value] = (rangeStart, i);
                if (src is not null) {
                    currentId = src;
                    rangeStart = i;
                }
                else {
                    currentId = null;
                }
            }
        }
        if (currentId is not null)
            nodeRanges[currentId.Value] = (rangeStart, ctx.Code.Count);

        return new Bytecode(ctx.Code, ctx.Functions, ctx.Constants, ctx.CallSites,
            ctx.CallSiteTargets, ctx.ExceptionRegions, null, analysis, ctx.LoopBodies,
            nodeRanges: nodeRanges);
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
                    if (c.Value is int iv) { EmitOp(ref ctx, new PushOp(iv), c); return; }
                    if (c.Value is long lv) { EmitOp(ref ctx, new PushOp(lv), c); return; }
                    if (c.Value is short sv) { EmitOp(ref ctx, new PushOp((long)sv), c); return; }
                    if (c.Value is byte bv) { EmitOp(ref ctx, new PushOp((long)bv), c); return; }
                    if (c.Value is bool bvv) { EmitOp(ref ctx, new PushOp(bvv ? 1L : 0L), c); return; }
                    if (c.Value is uint uiv) { EmitOp(ref ctx, new PushOp((long)uiv), c); return; }
                    int constIdx = ctx.Constants!.Count;
                    ctx.Constants!.Add(c.Value);
                    EmitOp(ref ctx, new PushOp(constIdx), c);
                    return;
                }

            case Add a: EmitBinary(a.LeftHandValue, a.RightHandValue, static () => new AddOp(), ref ctx, lambdaState, a); return;
            case Subtract s: EmitBinary(s.LeftHandValue, s.RightHandValue, static () => new SubOp(), ref ctx, lambdaState, s); return;
            case Multiply m: EmitBinary(m.LeftHandValue, m.RightHandValue, static () => new MulOp(), ref ctx, lambdaState, m); return;
            case Divide d: EmitBinary(d.LeftHandValue, d.RightHandValue, static () => new DivOp(), ref ctx, lambdaState, d); return;
            case Modulo m: EmitDivRem(m.LeftHandValue, m.RightHandValue, ref ctx, lambdaState); return;

            case Equal e: EmitBinary(e.LeftHandValue, e.RightHandValue, static () => new EqOp(), ref ctx, lambdaState, e); return;
            case NotEqual ne: EmitBinary(ne.LeftHandValue, ne.RightHandValue, static () => new NeOp(), ref ctx, lambdaState, ne); return;
            case LessThan lt: EmitBinary(lt.LeftHandValue, lt.RightHandValue, static () => new LtOp(), ref ctx, lambdaState, lt); return;
            case LessThanOrEqual le: EmitBinary(le.LeftHandValue, le.RightHandValue, static () => new LeOp(), ref ctx, lambdaState, le); return;
            case GreaterThan gt: EmitBinary(gt.LeftHandValue, gt.RightHandValue, static () => new GtOp(), ref ctx, lambdaState, gt); return;
            case GreaterThanOrEqual ge: EmitBinary(ge.LeftHandValue, ge.RightHandValue, static () => new GeOp(), ref ctx, lambdaState, ge); return;

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
            case BitwiseAnd ba: EmitBinary(ba.LeftHandValue, ba.RightHandValue, static () => new BitAndOp(), ref ctx, lambdaState, ba); return;
            case BitwiseOr bo: EmitBinary(bo.LeftHandValue, bo.RightHandValue, static () => new BitOrOp(), ref ctx, lambdaState, bo); return;
            case BitwiseXor bx: EmitBinary(bx.LeftHandValue, bx.RightHandValue, static () => new BitXorOp(), ref ctx, lambdaState, bx); return;
            case ShiftLeft sl: EmitBinary(sl.LeftHandValue, sl.RightHandValue, static () => new ShlOp(), ref ctx, lambdaState, sl); return;
            case ShiftRight sr: EmitBinary(sr.LeftHandValue, sr.RightHandValue, static () => new ShrOp(), ref ctx, lambdaState, sr); return;

            case And and: EmitShortCircuit(and.LeftHandValue, and.RightHandValue, false, ref ctx, lambdaState); return;
            case Or or: EmitShortCircuit(or.LeftHandValue, or.RightHandValue, true, ref ctx, lambdaState); return;

            case Variable v:
                EmitVariable(v, ref ctx, lambdaState);
                return;
            case Parameter p: EmitParameter(p, ref ctx, lambdaState); return;

            case Assignment assign: {
                    // Alias-eligible new array: skip heap entirely
                    if (assign.Value is NewArray na && assign.Destination is Variable destVar
                        && ctx.CanAlias(destVar.Name)) {
                        int localIdx = ctx.LocalIndexMap?.GetValueOrDefault(destVar.Name) ?? -1;
                        if (localIdx >= 0) {
                            var aliasName = $"a{localIdx}";
                            ctx.LocalAliases[destVar.Name] = aliasName;
                            EmitNode(na.Length, ref ctx, lambdaState);
                            ctx.Code.Add(new NewArrayOp(Alias: aliasName));
                            if (EmitsValue(assign, ctx.Analysis))
                                ctx.Code.Add(new DupOp());
                            EmitVariableStore(assign.Destination, ref ctx, lambdaState);
                            return;
                        }
                    }
                    // Array store: arr[i] = val — aliased path (no handle)
                    if (assign.Destination is IndexAccess ia && IsArrayType(ia.Value, ctx)) {
                        if (ia.Value is Variable iaVar && ctx.LocalAliases.TryGetValue(iaVar.Name, out var arrAlias)) {
                            EmitNode(ia.Arguments[0], ref ctx, lambdaState); // index
                            EmitNode(assign.Value, ref ctx, lambdaState);    // val
                            ctx.Code.Add(new ArrayStoreOp(Alias: arrAlias));
                            if (EmitsValue(assign, ctx.Analysis))
                                ctx.Code.Add(new DupOp());
                        }
                        else {
                            EmitNode(ia.Value, ref ctx, lambdaState);        // arr_handle
                            EmitNode(ia.Arguments[0], ref ctx, lambdaState); // index
                            EmitNode(assign.Value, ref ctx, lambdaState);    // val
                            ctx.Code.Add(new ArrayStoreOp());
                            if (EmitsValue(assign, ctx.Analysis))
                                ctx.Code.Add(new DupOp());
                        }
                        return;
                    }
                    EmitNode(assign.Value, ref ctx, lambdaState);
                    if (EmitsValue(assign, ctx.Analysis))
                        ctx.Code.Add(new DupOp());
                    EmitVariableStore(assign.Destination, ref ctx, lambdaState);
                    return;
                }

            case Invoke invoke: EmitInvoke(invoke, ref ctx, lambdaState); return;
            case Lambda lam: EmitLambda(lam, ref ctx, lambdaState); return;
            case Return: TraceReturn(ref ctx); ctx.Code.Add(new ReturnFromCallOp(ctx.CurrentArgSlots)); return;

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
            case NewArray na: EmitNode(na.Length, ref ctx, lambdaState); ctx.Code.Add(new NewArrayOp()); return;

            case Await aw: EmitAwait(aw, ref ctx, lambdaState); return;
            case SuspendNode sn: EmitSuspendNode(sn, ref ctx, lambdaState); return;

            default:
                throw new InvalidOperationException($"Lowering not yet implemented for {node.GetType().Name}");
        }
    }

    private static void EmitBinary(Node left, Node right, Func<MicroOp> makeOp, ref EmitContext ctx, LambdaEmitState? lambdaState, Node? source = null) {
        EmitNode(left, ref ctx, lambdaState);
        if (TryGetConstantLong(right, out long val)) {
            var op = makeOp();
            if (op is AddOp or SubOp or MulOp or EqOp or NeOp or LtOp or LeOp or GtOp or GeOp) {
                EmitOp(ref ctx, op switch {
                    AddOp => new AddImmOp(val),
                    SubOp => new SubImmOp(val),
                    MulOp => new MulImmOp(val),
                    EqOp => new EqImmOp(val),
                    NeOp => new NeImmOp(val),
                    LtOp => new LtImmOp(val),
                    LeOp => new LeImmOp(val),
                    GtOp => new GtImmOp(val),
                    GeOp => new GeImmOp(val),
                    _ => op
                }, source);
                return;
            }
            EmitOp(ref ctx, new PushOp(val), source);
            EmitOp(ref ctx, op, source);
        }
        else {
            EmitNode(right, ref ctx, lambdaState);
            EmitOp(ref ctx, makeOp(), source);
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
        ctx.Code.Add(new CommentOp("while start"));
        if (TryEmitStridedSet(wl, ref ctx, lambdaState)) {
            ctx.Code.Add(new CommentOp("while end (strided)"));
            return;
        }

        int cont = EmitLabel(ref ctx);
        int end = EmitLabel(ref ctx);

        ctx.LabelTargets[cont] = ctx.Code.Count;
        ctx.Code.Add(new CommentOp("while cond"));
        EmitNode(wl.Condition, ref ctx, lambdaState);
        ctx.Code.Add(new JumpIfFalseOp(end));
        int bodyStart = ctx.Code.Count;
        ctx.Code.Add(new CommentOp("while body"));
        EmitNode(wl.Body, ref ctx, lambdaState);
        if (EmitsValue(wl.Body, ctx.Analysis))
            ctx.Code.Add(new PopOp());
        int bodyEnd = ctx.Code.Count;
        ctx.Code.Add(new JumpOp(cont));
        ctx.LabelTargets[end] = ctx.Code.Count;
        ctx.Code.Add(new CommentOp("while end"));

        ctx.LoopBodies?.Add(new LoopBodyEntry(bodyStart, bodyEnd - bodyStart, cont, cont, end, wl.Body) {
            ParamIndexMap = ctx.ParamIndexMap,
            LocalIndexMap = ctx.LocalIndexMap,
        });
    }

    /// <summary>Detect and emit the strided bit-set pattern:
    /// <c>while (idx &lt;= limit) { arr[idx&gt;&gt;6] |= 1L &lt;&lt; (idx&amp;63); idx += step; }</c>
    /// Uses <c>StridedSetOp</c> — single compiled loop, no µop dispatch per iteration.</summary>
    private static bool TryEmitStridedSet(WhileLoop wl, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        // Condition must be LessThanOrEqual(Variable, something)
        if (wl.Condition is not LessThanOrEqual le) return false;
        if (le.LeftHandValue is not Variable idxVar) return false;
        // Limit can be another variable or a constant
        Node limitNode = le.RightHandValue;

        // Body must be Block with exactly 2 nodes
        if (wl.Body is not Block body || body.Nodes.Count != 2) return false;

        // First node: Assignment(IndexAccess(arr, ShiftRight(idx, 6)), BitwiseOr(...))
        if (body.Nodes[0] is not Assignment assign) return false;
        if (assign.Destination is not IndexAccess ia) return false;
        if (ia.Value is not Variable arrVar) return false;
        if (ia.Arguments.Length != 1) return false;

        // Index expression: ShiftRight(idx, 6)
        if (ia.Arguments[0] is not ShiftRight sr) return false;
        if (sr.LeftHandValue is not Variable srVar || srVar.Name != idxVar.Name) return false;
        if (!IsConstant(sr.RightHandValue, 6)) return false;

        // Value expression: BitwiseOr(IndexAccess(...), ShiftLeft(1, BitwiseAnd(idx, 63)))
        if (assign.Value is not BitwiseOr bor) return false;
        if (bor.LeftHandValue is not IndexAccess ia2) return false;
        // verify ia2 is the same array pattern (app  ended during StridedSetOp internally)
        if (bor.RightHandValue is not ShiftLeft sl) return false;
        if (sl.LeftHandValue is not Constant c || !(c.Value is long lv && lv == 1L)) return false;
        if (sl.RightHandValue is not BitwiseAnd ba) return false;
        if (ba.RightHandValue is not Constant cmask || !IsConstant(cmask, 63)) return false;
        if (ba.LeftHandValue is not Variable baVar || baVar.Name != idxVar.Name) return false;

        // Second node: Assignment(idx, Add(idx, step))
        if (body.Nodes[1] is not Assignment inc) return false;
        if (inc.Destination is not Variable incVar || incVar.Name != idxVar.Name) return false;
        if (inc.Value is not Add add) return false;
        if (add.LeftHandValue is not Variable addVar || addVar.Name != idxVar.Name) return false;
        // step is add.RightHandValue — could be Variable or Constant

        // Determine limit value
        Node startNode = ia.Arguments[0]; // recomputed below
        Node stepNode = add.RightHandValue;

        // Emit StridedSetOp: load handle, push start, step, limit
        // Need to evaluate start and step expressions
        string? aliasName = ctx.LocalAliases?.GetValueOrDefault(arrVar.Name);
        if (aliasName is not null) {
            // Use aliased read — but StridedSetOp doesn't support alias yet
            // Fall back to normal path
            return false;
        }

        // Push the array handle
        EmitNode(arrVar, ref ctx, lambdaState);  // → push handle (stays on stack via Top())
        // Push start (i*i), step (i), limit
        EmitNode(ia.Arguments[0], ref ctx, lambdaState);  // start = i*i (or the expression)
        EmitNode(stepNode, ref ctx, lambdaState);          // step
        EmitNode(limitNode, ref ctx, lambdaState);         // limit
        ctx.Code.Add(new StridedSetOp());
        return true;
    }

    /// <summary>Check if a node is a Constant with the given integer value.</summary>
    private static bool IsConstant(Node node, long value) {
        return node is Constant c && c.Value is long lv && lv == value
            || node is Constant c2 && c2.Value is int iv && iv == value;
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
        TraceInvoke(ref ctx, invoke);
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
            if (!isStatic && invoke.Delegate is Member instanceMethod)
                EmitNode(instanceMethod.Value, ref ctx, lambdaState);
            foreach (var arg in args)
                EmitNode(arg, ref ctx, lambdaState);
            int siteIdx = GetOrAddCallSite(ref ctx, clrMethod.MethodInfo, isStatic);
            ctx.Code.Add(new CallExternalOp(siteIdx));
            return;
        }

        if (invoke.Delegate is Lambda lambda2 && ctx.LambdaFuncMap!.TryGetValue(lambda2, out int lambdaIdx2)) {
            ctx.Code.Add(new PushOp(-1L)); // dummy closure handle at index 0
            foreach (var arg in args)
                EmitNode(arg, ref ctx, lambdaState);
            ctx.Code.Add(new CallOp(lambdaIdx2, args.Length + 1));
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
            // Skip the TypeReference for static members — it's a type marker, not a value
            if (m.Value is not TypeReference)
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

        // Array index read via direct µop (no CLR call overhead)
        if (IsArrayType(ia.Value, ctx)) {
            if (ia.Value is Variable v && ctx.LocalAliases.TryGetValue(v.Name, out var aliasName)) {
                EmitNode(ia.Arguments[0], ref ctx, lambdaState);
                ctx.Code.Add(new ArrayLoadOp(Alias: aliasName));
            }
            else {
                EmitNode(ia.Value, ref ctx, lambdaState);     // push arr_handle
                EmitNode(ia.Arguments[0], ref ctx, lambdaState); // push index
                ctx.Code.Add(new ArrayLoadOp());
            }
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
        if (resolved is ClrMethod setter) {
            int siteIdx = ctx.CallSites!.Count;
            bool isStatic = setter.LifetimeModifier == LifetimeModifier.Static;
            ctx.CallSites!.Add(CallSiteCompiler.Compile(setter.MethodInfo, isStatic));
            ctx.Code.Add(new CallExternalOp(siteIdx));
            return;
        }

        // Array index write: arr[i] = val → Array.SetValue(val, i)
        // Stack at entry: [arr, val, index] (arranged by Assignment case)
        if (IsArrayType(ia.Value, ctx)) {
            var setValue = typeof(Array).GetMethod("SetValue", [typeof(object), typeof(int)])!;
            int siteIdx = ctx.CallSites!.Count;
            ctx.CallSites!.Add(CallSiteCompiler.Compile(setValue, false));
            ctx.Code.Add(new CallExternalOp(siteIdx));
            return;
        }

        throw new InvalidOperationException($"Index setter not found");
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

    private static bool IsArrayType(Node node, EmitContext ctx) {
        var type = ctx.Analysis.GetResolvedType(node);
        if (type is null) return false;
        if (type is Introspection.CommonLanguageRuntime.ClrTypeDefinition ctd)
            return ctd.RuntimeType.IsArray;
        return type.FullName is { } n && n.EndsWith("[]");
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

    // ── Trace helpers (always compiled, cheap when state.Trace is null) ──

    /// <summary>Emit a µop and attach the source AST node's text for
    /// trace visibility.  The compiled delegate fires a trace call before
    /// the operation (gated at runtime by <c>state.Trace != null</c>).</summary>
    private static void EmitOp(ref EmitContext ctx, MicroOp op, Node? source = null) {
        if (source is not null) {
            var text = source.ToString() ?? "";
            if (text.Length > 60) text = text[..57] + "...";
            ctx.Code.Add(op with { Source = source.Id, SourceName = text });
        }
        else {
            ctx.Code.Add(op);
        }
    }

    private static string FormatTarget(Invoke invoke, EmitContext ctx) {
        var resolved = ctx.Analysis.GetResolvedMember(invoke);
        if (resolved is Introspection.CommonLanguageRuntime.ClrMethod cm)
            return $"{cm.DeclaringTypeDefinition.Name}.{cm.Name}";
        if (resolved is Introspection.CommonLanguageRuntime.ClrConstructor cc)
            return $"new {cc.Name}";
        if (resolved is Introspection.CommonLanguageRuntime.ClrTypeProperty cp)
            return $"{cp.Name}";
        if (invoke.Delegate is Lambda lam) {
            var pm = lam.Parameters?.Count > 0
                ? string.Join(",", lam.Parameters.Select(p => p.Name ?? "?"))
                : "";
            var body = lam.Body switch {
                Block b => b.Nodes.Count == 1 ? b.Nodes[0].GetType().Name : $"block[{b.Nodes.Count}]",
                Node n => n.GetType().Name,
                null => "?"
            };
            return $"λ({pm}) → {body}";
        }
        if (invoke.Delegate is Member m) return m.MemberName;
        return invoke.Delegate?.GetType().Name ?? "?";
    }

    private static void TraceInvoke(ref EmitContext ctx, Invoke invoke) {
        ctx.Code.Add(new CommentOp($"CALL {FormatTarget(invoke, ctx)}"));
    }

    private static void TraceReturn(ref EmitContext ctx) {
        ctx.Code.Add(new CommentOp($"RETURN (args={ctx.CurrentArgSlots})"));
    }

    // ── Alias ownership pre-scan ──

    /// <summary>Walk the AST to collect assignment counts and escape
    /// information for all variable references.  Runs once before emission.</summary>
    private static void CollectEscapeInfo(Node node, EmitContext ctx) {
        switch (node) {
            case Assignment { Destination: Variable v }:
                ctx.AssignmentCount[v.Name] = ctx.AssignmentCount.GetValueOrDefault(v.Name) + 1;
                break;
            case Invoke invoke when invoke.Delegate is not Lambda:
                foreach (var arg in invoke.Arguments)
                    MarkEscape(arg, ctx);
                break;
            case Return r:
                MarkEscape(r.Value, ctx);
                break;
            case ForEachLoop fel:
                MarkEscape(fel.Collection, ctx);
                break;
            case Lambda lam:
                // Lambda body is a new scope — variables referenced inside are
                // local to the lambda, not escaped from the enclosing scope.
                // True captures (from enclosing scope) are handled by the
                // enclosing scope's walk of the assignment/invoke/return nodes.
                // The body walk still visits the lambda's children for
                // inner Assignments (to count them), but the body's variable
                // references don't escape.
                break;
        }
        foreach (var child in node.Children) {
            if (child is not null)
                CollectEscapeInfo(child, ctx);
        }
    }

    /// <summary>Mark all Variable nodes in an expression as escaped.</summary>
    private static void MarkEscape(Node? node, EmitContext ctx) {
        if (node is Variable v)
            ctx.EscapedLocals.Add(v.Name);
        if (node is not null)
            foreach (var child in node.Children)
                if (child is not null)
                    MarkEscape(child, ctx);
    }
}