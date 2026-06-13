using System.Reflection;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
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

    private sealed class EmitContext {
        public List<MicroOp> Code = null!;
        public AnalysisResult Analysis = null!;
        public List<FunctionEntry> Functions = null!;
        public Dictionary<MethodDefinitionNode, int>? FunctionIndexMap;
        public IReadOnlyDictionary<string, int>? ParamIndexMap, LocalIndexMap;
        public List<object?>? Constants;
        public List<CallSiteDelegate>? CallSites;
        public List<string>? CallSiteTargets;
        public Dictionary<MethodInfo, int>? CallSiteCache;
        public Dictionary<Lambda, int>? LambdaFuncMap;
        public Dictionary<Lambda, List<string>>? LambdaCaptureMap;
        public IReadOnlyDictionary<string, int>? UpvalueMap;
        public List<ExceptionRegion> ExceptionRegions = null!;
        public List<LoopBodyEntry>? LoopBodies;
        public int CurrentArgSlots;
        public Dictionary<int, int> LabelTargets = null!;
        // ── Alias ownership tracking ──
        public Dictionary<string, int> AssignmentCount = null!;
        public HashSet<string> EscapedLocals = null!;
        public bool CanAlias(string name) =>
            AssignmentCount.GetValueOrDefault(name) == 1 && !EscapedLocals.Contains(name);
        public Dictionary<string, string> LocalAliases = null!;

        /// <summary>Create a scope child that shares all mutable collections
        /// but resets per-scope fields (ParamIndexMap, LocalIndexMap, etc.).</summary>
        public EmitContext NewScope() => new() {
            Code = Code,
            Analysis = Analysis,
            Functions = Functions,
            FunctionIndexMap = FunctionIndexMap,
            Constants = Constants,
            CallSites = CallSites,
            CallSiteTargets = CallSiteTargets,
            CallSiteCache = CallSiteCache,
            LambdaFuncMap = LambdaFuncMap,
            LambdaCaptureMap = LambdaCaptureMap,
            ExceptionRegions = ExceptionRegions,
            LoopBodies = LoopBodies,
            LabelTargets = LabelTargets,
            AssignmentCount = AssignmentCount,
            EscapedLocals = EscapedLocals,
            LocalAliases = LocalAliases,
        };

        // ── Trace helpers ──

        /// <summary>Emit a µop and attach the source AST node's text for
        /// trace visibility.  The compiled delegate fires a trace call before
        /// the operation (gated at runtime by <c>state.Trace != null</c>).</summary>
        private void EmitOp(MicroOp op, Node? source = null) {
            if (source is not null) {
                var text = source.ToString() ?? "";
                if (text.Length > 60) text = text[..57] + "...";
                Code.Add(op with { Source = source.Id, SourceName = text });
            }
            else {
                Code.Add(op);
            }
        }

        private string FormatTarget(Invoke invoke) {
            var resolved = Analysis.GetResolvedMember(invoke);
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

        private void TraceInvoke(Invoke invoke) {
            Code.Add(new CommentOp($"CALL {FormatTarget(invoke)}"));
        }

        private void TraceReturn() {
            Code.Add(new CommentOp($"RETURN (args={CurrentArgSlots})"));
        }

        // ── Label management ──

        private int EmitLabel() {
            int label = LabelTargets.Count;
            LabelTargets[label] = Code.Count;
            return label;
        }

        public void ResolveLabels() {
            for (int i = 0; i < Code.Count; i++) {
                switch (Code[i]) {
                    case JumpOp jmp when LabelTargets.TryGetValue(jmp.Target, out int target):
                        Code[i] = new JumpOp(target);
                        break;
                    case JumpIfFalseOp jif when LabelTargets.TryGetValue(jif.Target, out int target):
                        Code[i] = new JumpIfFalseOp(target);
                        break;
                }
            }
        }

        // ── Call site management ──

        private int AddCallSite(CallSiteDelegate d) {
            int idx = CallSites!.Count;
            CallSites!.Add(d);
            CallSiteTargets!.Add(d.Method.ToString() ?? "");
            return idx;
        }

        private int GetOrAddCallSite(MethodInfo mi, bool isStatic) {
            if (CallSiteCache!.TryGetValue(mi, out int idx))
                return idx;
            idx = CallSites!.Count;
            CallSites!.Add(CallSiteCompiler.Compile(mi, isStatic));
            CallSiteCache[mi] = idx;
            CallSiteTargets!.Add(FormatMethodTarget(mi, isStatic));
            return idx;
        }

        // ── Type / analysis helpers ──

        private bool TryGetConstantLong(Node node, out long value) {
            var val = Analysis.GetConstantValue(node);
            if (val is null) { value = 0; return false; }
            if (val is int iv) { value = iv; return true; }
            if (val is long lv) { value = lv; return true; }
            if (val is short sv) { value = sv; return true; }
            if (val is byte bv) { value = bv; return true; }
            if (val is uint uiv) { value = uiv; return true; }
            if (val is bool bvv) { value = bvv ? 1 : 0; return true; }
            value = 0;
            return false;
        }

        private bool IsArrayType(Node node) {
            var type = Analysis.GetResolvedType(node);
            if (type is null) return false;
            if (type is Introspection.CommonLanguageRuntime.ClrTypeDefinition ctd)
                return ctd.RuntimeType.IsArray;
            return type.FullName is { } n && n.EndsWith("[]");
        }

        // ── Alias ownership pre-scan (recursive tree walk) ──

        private void MarkEscape(Node? node) {
            if (node is Variable v)
                EscapedLocals.Add(v.Name);
            if (node is not null)
                foreach (var child in node.Children)
                    if (child is not null)
                        MarkEscape(child);
        }

        /// <summary>Pre-scan: collect assignment counts and escape info
        /// for alias analysis.  Runs once before emission.</summary>
        public void CollectEscapeInfo(Node node) {
            switch (node) {
                case Assignment { Destination: Variable v }:
                    AssignmentCount[v.Name] = AssignmentCount.GetValueOrDefault(v.Name) + 1;
                    break;
                case Invoke invoke when invoke.Delegate is not Lambda:
                    foreach (var arg in invoke.Arguments)
                        MarkEscape(arg);
                    break;
                case Return r:
                    MarkEscape(r.Value);
                    break;
                case ForEachLoop fel:
                    MarkEscape(fel.Collection);
                    break;
                case Lambda lam:
                    break;
            }
            foreach (var child in node.Children) {
                if (child is not null)
                    CollectEscapeInfo(child);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Emission — entry point
        // ═══════════════════════════════════════════════════════════════════

        public void EmitNode(Node node, LambdaEmitState? lambdaState) {
            switch (node) {
                case Constant c: {
                        if (c.Value is int iv) { EmitOp(new PushOp(iv), c); return; }
                        if (c.Value is long lv) { EmitOp(new PushOp(lv), c); return; }
                        if (c.Value is short sv) { EmitOp(new PushOp((long)sv), c); return; }
                        if (c.Value is byte bv) { EmitOp(new PushOp((long)bv), c); return; }
                        if (c.Value is bool bvv) { EmitOp(new PushOp(bvv ? 1L : 0L), c); return; }
                        if (c.Value is uint uiv) { EmitOp(new PushOp((long)uiv), c); return; }
                        int constIdx = Constants!.Count;
                        Constants!.Add(c.Value);
                        EmitOp(new PushOp(constIdx), c);
                        return;
                    }

                case Add a: EmitBinary(a.LeftHandValue, a.RightHandValue, static () => new AddOp(), lambdaState, a); return;
                case Subtract s: EmitBinary(s.LeftHandValue, s.RightHandValue, static () => new SubOp(), lambdaState, s); return;
                case Multiply m: EmitBinary(m.LeftHandValue, m.RightHandValue, static () => new MulOp(), lambdaState, m); return;
                case Divide d: EmitBinary(d.LeftHandValue, d.RightHandValue, static () => new DivOp(), lambdaState, d); return;
                case Modulo m: EmitDivRem(m.LeftHandValue, m.RightHandValue, lambdaState); return;

                case Equal e: EmitBinary(e.LeftHandValue, e.RightHandValue, static () => new EqOp(), lambdaState, e); return;
                case NotEqual ne: EmitBinary(ne.LeftHandValue, ne.RightHandValue, static () => new NeOp(), lambdaState, ne); return;
                case LessThan lt: EmitBinary(lt.LeftHandValue, lt.RightHandValue, static () => new LtOp(), lambdaState, lt); return;
                case LessThanOrEqual le: EmitBinary(le.LeftHandValue, le.RightHandValue, static () => new LeOp(), lambdaState, le); return;
                case GreaterThan gt: EmitBinary(gt.LeftHandValue, gt.RightHandValue, static () => new GtOp(), lambdaState, gt); return;
                case GreaterThanOrEqual ge: EmitBinary(ge.LeftHandValue, ge.RightHandValue, static () => new GeOp(), lambdaState, ge); return;

                case UnaryMinus um:
                    if (TryGetConstantLong(um.Operand, out long negVal)) {
                        Code.Add(new PushOp(-negVal));
                    }
                    else {
                        EmitNode(um.Operand, lambdaState);
                        Code.Add(new NegOp());
                    }
                    return;

                case Not n:
                    if (TryGetConstantLong(n.Value, out long notVal)) {
                        Code.Add(new PushOp(notVal == 0 ? 1L : 0L));
                    }
                    else {
                        EmitNode(n.Value, lambdaState);
                        Code.Add(new NotOp());
                    }
                    return;

                case BitwiseNot bn: EmitNode(bn.Operand, lambdaState); Code.Add(new BitNotOp()); return;
                case BitwiseAnd ba: EmitBinary(ba.LeftHandValue, ba.RightHandValue, static () => new BitAndOp(), lambdaState, ba); return;
                case BitwiseOr bo: EmitBinary(bo.LeftHandValue, bo.RightHandValue, static () => new BitOrOp(), lambdaState, bo); return;
                case BitwiseXor bx: EmitBinary(bx.LeftHandValue, bx.RightHandValue, static () => new BitXorOp(), lambdaState, bx); return;
                case ShiftLeft sl: EmitBinary(sl.LeftHandValue, sl.RightHandValue, static () => new ShlOp(), lambdaState, sl); return;
                case ShiftRight sr: EmitBinary(sr.LeftHandValue, sr.RightHandValue, static () => new ShrOp(), lambdaState, sr); return;

                case And and: EmitShortCircuit(and.LeftHandValue, and.RightHandValue, false, lambdaState); return;
                case Or or: EmitShortCircuit(or.LeftHandValue, or.RightHandValue, true, lambdaState); return;

                case Variable v:
                    EmitVariable(v, lambdaState);
                    return;
                case Parameter p: EmitParameter(p, lambdaState); return;

                case Assignment assign: {
                        if (assign.Value is NewArray na && assign.Destination is Variable destVar
                            && CanAlias(destVar.Name)) {
                            int localIdx = LocalIndexMap?.GetValueOrDefault(destVar.Name) ?? -1;
                            if (localIdx >= 0) {
                                var aliasName = $"a{localIdx}";
                                LocalAliases[destVar.Name] = aliasName;
                                EmitNode(na.Length, lambdaState);
                                Code.Add(new NewArrayOp(Alias: aliasName));
                                if (EmitsValue(assign, Analysis))
                                    Code.Add(new DupOp());
                                EmitVariableStore(assign.Destination, lambdaState);
                                return;
                            }
                        }
                        if (assign.Destination is IndexAccess ia && IsArrayType(ia.Value)) {
                            if (ia.Value is Variable iaVar && LocalAliases.TryGetValue(iaVar.Name, out var arrAlias)) {
                                EmitNode(ia.Arguments[0], lambdaState);
                                EmitNode(assign.Value, lambdaState);
                                Code.Add(new ArrayStoreOp(Alias: arrAlias));
                                if (EmitsValue(assign, Analysis))
                                    Code.Add(new DupOp());
                            }
                            else {
                                EmitNode(ia.Value, lambdaState);
                                EmitNode(ia.Arguments[0], lambdaState);
                                EmitNode(assign.Value, lambdaState);
                                Code.Add(new ArrayStoreOp());
                                if (EmitsValue(assign, Analysis))
                                    Code.Add(new DupOp());
                            }
                            return;
                        }
                        EmitNode(assign.Value, lambdaState);
                        if (EmitsValue(assign, Analysis))
                            Code.Add(new DupOp());
                        EmitVariableStore(assign.Destination, lambdaState);
                        return;
                    }

                case Invoke invoke: EmitInvoke(invoke, lambdaState); return;
                case Lambda lam: EmitLambda(lam, lambdaState); return;
                case Return: TraceReturn(); Code.Add(new ReturnFromCallOp(CurrentArgSlots)); return;

                case Conditional cond: EmitConditional(cond, lambdaState); return;
                case IfStatement iff: EmitIfStatement(iff, lambdaState); return;
                case WhileLoop wl: EmitWhileLoop(wl, lambdaState); return;
                case DoWhileLoop dw: EmitDoWhileLoop(dw, lambdaState); return;
                case ForLoop fl: EmitForLoop(fl, lambdaState); return;
                case BreakStatement: Code.Add(new JumpOp(0)); return;
                case ContinueStatement: Code.Add(new JumpOp(0)); return;

                case Block block: {
                        for (int i = 0; i < block.Nodes.Count; i++) {
                            var child = block.Nodes[i];
                            bool isLast = i == block.Nodes.Count - 1 && EmitsValue(block, Analysis);
                            EmitNode(child, lambdaState);
                            if (!isLast && EmitsValue(child, Analysis))
                                Code.Add(new PopOp());
                        }
                        return;
                    }

                case ThrowStatement thr: EmitNode(thr.Exception, lambdaState); Code.Add(new ThrowOp()); return;
                case TryCatchFinally tcf: EmitTryCatchFinally(tcf, lambdaState); return;
                case ForEachLoop fel: EmitForEachLoop(fel, lambdaState); return;
                case UsingStatement us: EmitUsingStatement(us, lambdaState); return;

                case Member m: EmitMember(m, lambdaState); return;
                case IndexAccess ia: EmitIndexAccess(ia, lambdaState); return;
                case New n: EmitNew(n, lambdaState); return;
                case NewArray na: EmitNode(na.Length, lambdaState); Code.Add(new NewArrayOp()); return;

                case Await aw: EmitAwait(aw, lambdaState); return;
                case SuspendNode sn: EmitSuspendNode(sn, lambdaState); return;

                case MethodDefinitionNode:
                    return;

                default:
                    throw new InvalidOperationException($"Lowering not yet implemented for {node.GetType().Name}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Emission — expressions
        // ═══════════════════════════════════════════════════════════════════

        private void EmitBinary(Node left, Node right, Func<MicroOp> makeOp, LambdaEmitState? lambdaState, Node? source = null) {
            EmitNode(left, lambdaState);
            if (TryGetConstantLong(right, out long val)) {
                var op = makeOp();
                if (op is AddOp or SubOp or MulOp or EqOp or NeOp or LtOp or LeOp or GtOp or GeOp) {
                    EmitOp(op switch {
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
                EmitOp(new PushOp(val), source);
                EmitOp(op, source);
            }
            else {
                EmitNode(right, lambdaState);
                EmitOp(makeOp(), source);
            }
        }

        private void EmitDivRem(Node left, Node right, LambdaEmitState? lambdaState) {
            EmitNode(left, lambdaState);
            EmitNode(right, lambdaState);
            Code.Add(new DivRemOp());
            Code.Add(new PopOp());
        }

        private void EmitShortCircuit(Node left, Node right, bool isOr, LambdaEmitState? lambdaState) {
            int end = EmitLabel();

            EmitNode(left, lambdaState);
            Code.Add(new DupOp());
            if (isOr) {
                int evalRight = EmitLabel();
                Code.Add(new JumpIfFalseOp(evalRight));
                Code.Add(new PopOp());
                Code.Add(new JumpOp(end));
                LabelTargets[evalRight] = Code.Count;
                Code.Add(new PopOp());
            }
            else {
                Code.Add(new JumpIfFalseOp(end));
                Code.Add(new PopOp());
            }
            EmitNode(right, lambdaState);
            LabelTargets[end] = Code.Count;
        }

        private void EmitConditional(Conditional cond, LambdaEmitState? lambdaState) {
            int else_ = EmitLabel();
            int end = EmitLabel();

            EmitNode(cond.Condition, lambdaState);
            Code.Add(new JumpIfFalseOp(else_));
            EmitNode(cond.IfTrue, lambdaState);
            Code.Add(new JumpOp(end));
            LabelTargets[else_] = Code.Count;
            EmitNode(cond.IfFalse, lambdaState);
            LabelTargets[end] = Code.Count;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Emission — statements
        // ═══════════════════════════════════════════════════════════════════

        private void EmitIfStatement(IfStatement iff, LambdaEmitState? lambdaState) {
            int end = EmitLabel();

            EmitNode(iff.Condition, lambdaState);
            if (iff.ElseBranch is not null) {
                int else_ = EmitLabel();
                Code.Add(new JumpIfFalseOp(else_));
                EmitNode(iff.ThenBranch, lambdaState);
                Code.Add(new JumpOp(end));
                LabelTargets[else_] = Code.Count;
                EmitNode(iff.ElseBranch, lambdaState);
            }
            else {
                Code.Add(new JumpIfFalseOp(end));
                EmitNode(iff.ThenBranch, lambdaState);
            }
            LabelTargets[end] = Code.Count;
        }

        private void EmitWhileLoop(WhileLoop wl, LambdaEmitState? lambdaState) {
            Code.Add(new CommentOp("while start"));
            if (TryEmitStridedSet(wl, lambdaState)) {
                Code.Add(new CommentOp("while end (strided)"));
                return;
            }

            int cont = EmitLabel();
            int end = EmitLabel();

            LabelTargets[cont] = Code.Count;
            Code.Add(new CommentOp("while cond"));
            EmitNode(wl.Condition, lambdaState);
            Code.Add(new JumpIfFalseOp(end));
            int bodyStart = Code.Count;
            Code.Add(new CommentOp("while body"));
            EmitNode(wl.Body, lambdaState);
            if (EmitsValue(wl.Body, Analysis))
                Code.Add(new PopOp());
            int bodyEnd = Code.Count;
            Code.Add(new JumpOp(cont));
            LabelTargets[end] = Code.Count;
            Code.Add(new CommentOp("while end"));

            LoopBodies?.Add(new LoopBodyEntry(bodyStart, bodyEnd - bodyStart, cont, cont, end, wl.Body) {
                ParamIndexMap = ParamIndexMap,
                LocalIndexMap = LocalIndexMap,
            });
        }

        private bool TryEmitStridedSet(WhileLoop wl, LambdaEmitState? lambdaState) {
            if (wl.Condition is not LessThanOrEqual le) return false;
            if (le.LeftHandValue is not Variable idxVar) return false;
            Node limitNode = le.RightHandValue;

            if (wl.Body is not Block body || body.Nodes.Count != 2) return false;

            if (body.Nodes[0] is not Assignment assign) return false;
            if (assign.Destination is not IndexAccess ia) return false;
            if (ia.Value is not Variable arrVar) return false;
            if (ia.Arguments.Length != 1) return false;

            if (ia.Arguments[0] is not ShiftRight sr) return false;
            if (sr.LeftHandValue is not Variable srVar || srVar.Name != idxVar.Name) return false;
            if (!TryGetConstantLong(sr.RightHandValue, out var sv) || sv != 6) return false;

            if (assign.Value is not BitwiseOr bor) return false;
            if (bor.LeftHandValue is not IndexAccess ia2) return false;
            if (bor.RightHandValue is not ShiftLeft sl) return false;
            if (!TryGetConstantLong(sl.LeftHandValue, out var oneVal) || oneVal != 1L) return false;
            if (sl.RightHandValue is not BitwiseAnd ba) return false;
            if (!TryGetConstantLong(ba.RightHandValue, out var cmaskVal) || cmaskVal != 63) return false;
            if (ba.LeftHandValue is not Variable baVar || baVar.Name != idxVar.Name) return false;

            if (body.Nodes[1] is not Assignment inc) return false;
            if (inc.Destination is not Variable incVar || incVar.Name != idxVar.Name) return false;
            if (inc.Value is not Add add) return false;
            if (add.LeftHandValue is not Variable addVar || addVar.Name != idxVar.Name) return false;

            Node stepNode = add.RightHandValue;

            string? aliasName = LocalAliases?.GetValueOrDefault(arrVar.Name);
            if (aliasName is not null) {
                return false;
            }

            EmitNode(arrVar, lambdaState);
            EmitNode(ia.Arguments[0], lambdaState);
            EmitNode(stepNode, lambdaState);
            EmitNode(limitNode, lambdaState);
            Code.Add(new StridedSetOp());
            return true;
        }

        private void EmitDoWhileLoop(DoWhileLoop dw, LambdaEmitState? lambdaState) {
            int cont = EmitLabel();
            int end = EmitLabel();

            LabelTargets[cont] = Code.Count;
            EmitNode(dw.Body, lambdaState);
            EmitNode(dw.Condition, lambdaState);
            Code.Add(new JumpIfFalseOp(end));
            Code.Add(new JumpOp(cont));
            LabelTargets[end] = Code.Count;
        }

        private void EmitForLoop(ForLoop fl, LambdaEmitState? lambdaState) {
            int cont = EmitLabel();
            int end = EmitLabel();

            if (fl.Initializer is not null) {
                EmitNode(fl.Initializer, lambdaState);
                if (EmitsValue(fl.Initializer, Analysis))
                    Code.Add(new PopOp());
            }
            LabelTargets[cont] = Code.Count;
            if (fl.Condition is not null) {
                EmitNode(fl.Condition, lambdaState);
                Code.Add(new JumpIfFalseOp(end));
            }
            int bodyStart = Code.Count;
            EmitNode(fl.Body, lambdaState);
            int bodyEnd = Code.Count;
            if (fl.Increment is not null) {
                EmitNode(fl.Increment, lambdaState);
                if (EmitsValue(fl.Increment, Analysis))
                    Code.Add(new PopOp());
            }
            Code.Add(new JumpOp(cont));
            LabelTargets[end] = Code.Count;

            LoopBodies?.Add(new LoopBodyEntry(bodyStart, bodyEnd - bodyStart, cont, Code.Count, end, fl.Body) {
                ParamIndexMap = ParamIndexMap,
                LocalIndexMap = LocalIndexMap,
            });
        }

        private void EmitVariable(Variable v, LambdaEmitState? lambdaState) {
            if (ParamIndexMap?.TryGetValue(v.Name, out int pIdx) == true) {
                Code.Add(new LoadArgOp(pIdx));
                return;
            }
            if (LocalIndexMap?.TryGetValue(v.Name, out int lIdx) == true) {
                Code.Add(new LoadLocalOp(lIdx));
                return;
            }
            if (lambdaState?.UpvalueMap?.TryGetValue(v.Name, out int uIdx) == true) {
                Code.Add(new LoadUpvalueOp(uIdx));
                return;
            }
            throw new InvalidOperationException($"Variable '{v.Name}' not found in any scope");
        }

        private void EmitParameter(Parameter p, LambdaEmitState? lambdaState) {
            if (p.DefaultValue is not null) {
                EmitNode(p.DefaultValue, lambdaState);
            }
            else if (ParamIndexMap?.TryGetValue(p.Name ?? "", out int pIdx) == true) {
                Code.Add(new LoadArgOp(pIdx));
            }
            else {
                Code.Add(new PushOp(0L));
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Emission — stores / assignments
        // ═══════════════════════════════════════════════════════════════════

        private void EmitVariableStore(Node target, LambdaEmitState? lambdaState) {
            if (target is Variable v) {
                if (ParamIndexMap?.TryGetValue(v.Name, out int pIdx) == true) {
                    Code.Add(new StoreArgOp(pIdx));
                    return;
                }
                if (LocalIndexMap?.TryGetValue(v.Name, out int lIdx) == true) {
                    Code.Add(new StoreLocalOp(lIdx));
                    return;
                }
                if (lambdaState?.UpvalueMap?.TryGetValue(v.Name, out int uIdx) == true) {
                    Code.Add(new StoreUpvalueOp(uIdx));
                    return;
                }
                throw new InvalidOperationException($"Store target '{v.Name}' not found");
            }
            if (target is IndexAccess ia) {
                EmitIndexAccessStore(ia, lambdaState);
                return;
            }
            if (target is Member m) {
                EmitMemberStore(m, lambdaState);
                return;
            }
            throw new InvalidOperationException($"Unsupported assignment target: {target.GetType().Name}");
        }

        private void EmitIndexAccessStore(IndexAccess ia, LambdaEmitState? lambdaState) {
            var resolved = Analysis.GetResolvedMember(ia);
            if (resolved is ClrMethod setter) {
                int siteIdx = CallSites!.Count;
                bool isStatic = setter.LifetimeModifier == LifetimeModifier.Static;
                CallSites!.Add(CallSiteCompiler.Compile(setter.MethodInfo, isStatic));
                Code.Add(new CallExternalOp(siteIdx));
                return;
            }

            if (IsArrayType(ia.Value)) {
                var setValue = typeof(Array).GetMethod("SetValue", [typeof(object), typeof(int)])!;
                int siteIdx = CallSites!.Count;
                CallSites!.Add(CallSiteCompiler.Compile(setValue, false));
                Code.Add(new CallExternalOp(siteIdx));
                return;
            }

            throw new InvalidOperationException($"Index setter not found");
        }

        private void EmitMemberStore(Member m, LambdaEmitState? lambdaState) {
            var resolved = Analysis.GetResolvedMember(m);
            if (resolved is not ClrMethod setter)
                throw new InvalidOperationException($"Member setter not found for {m.MemberName}");

            int siteIdx = CallSites!.Count;
            bool isStatic = setter.LifetimeModifier == LifetimeModifier.Static;
            CallSites!.Add(CallSiteCompiler.Compile(setter.MethodInfo, isStatic));
            Code.Add(new CallExternalOp(siteIdx));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Emission — calls
        // ═══════════════════════════════════════════════════════════════════

        private void EmitInvoke(Invoke invoke, LambdaEmitState? lambdaState) {
            TraceInvoke(invoke);
            var resolved = Analysis.GetResolvedMember(invoke);
            var args = invoke.Arguments;

            if (resolved is AstMethodDefinition astMethod) {
                int funcIdx = FunctionIndexMap!.TryGetValue(astMethod.DefinitionNode, out int idx) ? idx : 0;
                foreach (var arg in args)
                    EmitNode(arg, lambdaState);
                Code.Add(new CallOp(funcIdx, args.Length));
                return;
            }

            if (resolved is ClrMethod clrMethod) {
                bool isStatic = clrMethod.LifetimeModifier == LifetimeModifier.Static;
                if (!isStatic && invoke.Delegate is Member instanceMethod)
                    EmitNode(instanceMethod.Value, lambdaState);
                foreach (var arg in args)
                    EmitNode(arg, lambdaState);
                int siteIdx = GetOrAddCallSite(clrMethod.MethodInfo, isStatic);
                Code.Add(new CallExternalOp(siteIdx));
                return;
            }

            if (invoke.Delegate is Lambda lambda2 && LambdaFuncMap!.TryGetValue(lambda2, out int lambdaIdx2)) {
                Code.Add(new PushOp(-1L));
                foreach (var arg in args)
                    EmitNode(arg, lambdaState);
                Code.Add(new CallOp(lambdaIdx2, args.Length + 1));
                return;
            }

            if (invoke.Delegate is MethodDefinitionNode mdn2 && FunctionIndexMap!.TryGetValue(mdn2, out int mdnIdx)) {
                foreach (var arg in args)
                    EmitNode(arg, lambdaState);
                Code.Add(new CallOp(mdnIdx, args.Length));
                return;
            }

            EmitNode(invoke.Delegate, lambdaState);
            foreach (var arg in args)
                EmitNode(arg, lambdaState);
            Code.Add(new CallClosureOp());
        }

        private void EmitLambda(Lambda lam, LambdaEmitState? lambdaState) {
            if (LambdaCaptureMap is null || !LambdaCaptureMap.TryGetValue(lam, out var captures))
                throw new InvalidOperationException("Lambda not found in capture map");

            int funcIdx = LambdaFuncMap!.TryGetValue(lam, out int idx) ? idx : 0;

            foreach (var cap in captures) {
                if (ParamIndexMap?.TryGetValue(cap, out int pIdx) == true)
                    Code.Add(new LoadArgOp(pIdx));
                else if (LocalIndexMap?.TryGetValue(cap, out int lIdx) == true)
                    Code.Add(new LoadLocalOp(lIdx));
                else if (lambdaState?.UpvalueMap?.TryGetValue(cap, out int uIdx) == true)
                    Code.Add(new LoadUpvalueOp(uIdx));
                else
                    Code.Add(new PushOp(0L));
            }

            Code.Add(new AllocClosureOp(funcIdx, captures.Count));
        }

        private void EmitTryCatchFinally(TryCatchFinally tcf, LambdaEmitState? lambdaState) {
            int end = EmitLabel();
            int? finallyEntry = null;
            int? catchStart = null;

            int tryStart = Code.Count;
            EmitNode(tcf.TryBlock, lambdaState);
            Code.Add(new JumpOp(end));
            int tryEnd = Code.Count;

            if (tcf.CatchClauses is not null) {
                catchStart = Code.Count;
                foreach (var cc in tcf.CatchClauses) {
                    EmitLabel();
                    if (cc.VariableName is not null) {
                        Code.Add(new DupOp());
                        if (ParamIndexMap?.TryGetValue(cc.VariableName, out int pi) == true)
                            Code.Add(new StoreArgOp(pi));
                        else if (LocalIndexMap?.TryGetValue(cc.VariableName, out int li) == true)
                            Code.Add(new StoreLocalOp(li));
                    }
                    else Code.Add(new PopOp());
                    EmitNode(cc.Body, lambdaState);
                    Code.Add(new JumpOp(end));
                }
            }

            if (tcf.FinallyBlock is not null) {
                finallyEntry = Code.Count;
                EmitNode(tcf.FinallyBlock, lambdaState);
                if (EmitsValue(tcf.FinallyBlock, Analysis))
                    Code.Add(new PopOp());
                Code.Add(new EndFinallyOp());
            }

            LabelTargets[end] = Code.Count;
            ExceptionRegions.Add(new ExceptionRegion(tryStart, tryEnd, catchStart ?? -1, finallyEntry));
        }

        private void EmitForEachLoop(ForEachLoop fel, LambdaEmitState? lambdaState) {
            EmitNode(fel.Collection, lambdaState);
            var getEnum = typeof(IEnumerable<>).MakeGenericType(typeof(object))
                .GetMethod("GetEnumerator")!
                .MakeGenericMethod(typeof(object));
            int initSite = AddCallSite(CallSiteCompiler.Compile(getEnum, false));
            Code.Add(new PushOp(1L));
            Code.Add(new CallExternalOp(initSite));

            int cont = EmitLabel();
            int end = EmitLabel();

            LabelTargets[cont] = Code.Count;
            Code.Add(new DupOp());
            int moveNextSite = AddCallSite(CallSiteCompiler.Compile(
                typeof(IEnumerator).GetMethod("MoveNext")!, false));
            Code.Add(new PushOp(1L));
            Code.Add(new CallExternalOp(moveNextSite));
            Code.Add(new JumpIfFalseOp(end));

            Code.Add(new DupOp());
            int currentSite = AddCallSite(CallSiteCompiler.Compile(
                typeof(IEnumerator).GetProperty("Current")!.GetGetMethod()!, false));
            Code.Add(new PushOp(1L));
            Code.Add(new CallExternalOp(currentSite));

            EmitVariableStore(fel.LoopVariable, lambdaState);
            EmitNode(fel.Body, lambdaState);
            if (EmitsValue(fel.Body, Analysis))
                Code.Add(new PopOp());
            Code.Add(new JumpOp(cont));
            LabelTargets[end] = Code.Count;
        }

        private void EmitUsingStatement(UsingStatement us, LambdaEmitState? lambdaState) {
            int holderIdx = Constants!.Count;
            Constants.Add(new object[1]);

            EmitNode(us.Resource, lambdaState);
            Code.Add(new StoreValueOp());
            EmitNode(us.Body, lambdaState);
            if (EmitsValue(us.Body, Analysis))
                Code.Add(new PopOp());
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Emission — member / index / new / await / suspend
        // ═══════════════════════════════════════════════════════════════════

        private void EmitMember(Member m, LambdaEmitState? lambdaState) {
            var resolved = Analysis.GetResolvedMember(m);
            if (resolved is ClrMethod getter) {
                if (m.Value is not TypeReference)
                    EmitNode(m.Value, lambdaState);
                int siteIdx = GetOrAddCallSite(getter.MethodInfo,
                    getter.LifetimeModifier == LifetimeModifier.Static);
                Code.Add(new CallExternalOp(siteIdx));
                return;
            }
            throw new InvalidOperationException($"Member access not resolved: {m.MemberName}");
        }

        private void EmitIndexAccess(IndexAccess ia, LambdaEmitState? lambdaState) {
            var resolved = Analysis.GetResolvedMember(ia);
            if (resolved is ClrMethod getter) {
                EmitNode(ia.Value, lambdaState);
                foreach (var arg in ia.Arguments)
                    EmitNode(arg, lambdaState);
                int siteIdx = GetOrAddCallSite(getter.MethodInfo,
                    getter.LifetimeModifier == LifetimeModifier.Static);
                Code.Add(new CallExternalOp(siteIdx));
                return;
            }

            if (IsArrayType(ia.Value)) {
                if (ia.Value is Variable v && LocalAliases.TryGetValue(v.Name, out var aliasName)) {
                    EmitNode(ia.Arguments[0], lambdaState);
                    Code.Add(new ArrayLoadOp(Alias: aliasName));
                }
                else {
                    EmitNode(ia.Value, lambdaState);
                    EmitNode(ia.Arguments[0], lambdaState);
                    Code.Add(new ArrayLoadOp());
                }
                return;
            }

            throw new InvalidOperationException($"Index access not resolved");
        }

        private void EmitNew(New n, LambdaEmitState? lambdaState) {
            var resolved = Analysis.GetResolvedMember(n);
            if (resolved is ClrConstructor ctor) {
                foreach (var arg in n.Arguments)
                    EmitNode(arg, lambdaState);
                int siteIdx = AddCallSite(
                    CallSiteCompiler.CompileConstructor(ctor.ConstructorInfo));
                Code.Add(new CallExternalOp(siteIdx));
                return;
            }
            throw new InvalidOperationException($"Constructor not resolved for new {n.Type}");
        }

        private void EmitAwait(Await aw, LambdaEmitState? lambdaState) {
            EmitNode(aw.Operand, lambdaState);
            var getAwaiter = typeof(Task<>).GetMethod("GetAwaiter")?.MakeGenericMethod(typeof(object))
                ?? typeof(Task).GetMethod("GetAwaiter");
            if (getAwaiter is not null) {
                int siteIdx = AddCallSite(CallSiteCompiler.Compile(getAwaiter, false));
                Code.Add(new CallExternalOp(siteIdx));
            }
        }

        private void EmitSuspendNode(SuspendNode sn, LambdaEmitState? lambdaState) {
            EmitNode(sn.Inner, lambdaState);
            Code.Add(new PopOp());
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Lower (entry point)
    // ═══════════════════════════════════════════════════════════════════

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

        // Pre-scan: collect assignment counts and escape info for alias analysis
        ctx.CollectEscapeInfo(root);

        // ── Emit root as entry point ──
        if (root is MethodDefinitionNode rootMethod) {
            var paramIndexMap = new Dictionary<string, int>();
            if (rootMethod.Parameters is not null) {
                int pi = 0;
                foreach (var p in rootMethod.Parameters)
                    paramIndexMap[p.Name ?? ""] = pi++;
            }

            int rootIdx = ctx.FunctionIndexMap[rootMethod];
            int entryUop = ctx.Code.Count;
            var bodyCtx = ctx.NewScope();
            bodyCtx.ParamIndexMap = paramIndexMap;
            bodyCtx.CurrentArgSlots = paramIndexMap.Count;

            bodyCtx.EmitNode(rootMethod.Body ?? rootMethod, null);
            bodyCtx.Code.Add(new ReturnFromCallOp(bodyCtx.CurrentArgSlots));

            ctx.Functions[rootIdx] = new FunctionEntry(entryUop, paramIndexMap.Count, 1, 0) {
                SourceNode = rootMethod
            };
        }
        else {
            ctx.EmitNode(root, null);
            ctx.Code.Add(new ReturnOp());
        }

        // ── Emit utility method bodies (all referenced methods except root) ──
        foreach (var method in referencedMethods) {
            if (method == root) continue;

            var paramIndexMap = new Dictionary<string, int>();
            if (method.Parameters is not null) {
                int pi = 0;
                foreach (var p in method.Parameters)
                    paramIndexMap[p.Name ?? ""] = pi++;
            }

            var funcIndexMap = ctx.FunctionIndexMap!;
            int methodIdx = funcIndexMap[method];
            int entryUop = ctx.Code.Count;
            var bodyCtx = ctx.NewScope();
            bodyCtx.ParamIndexMap = paramIndexMap;
            bodyCtx.CurrentArgSlots = paramIndexMap.Count;

            bodyCtx.EmitNode(method.Body ?? method, null);
            bodyCtx.Code.Add(new ReturnFromCallOp(bodyCtx.CurrentArgSlots));

            ctx.Functions[methodIdx] = new FunctionEntry(entryUop, paramIndexMap.Count, 1, 0) {
                SourceNode = method
            };
        }

        // ── Emit lambda bodies ──
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
            var bodyCtx = ctx.NewScope();
            bodyCtx.ParamIndexMap = paramIndexMap;
            bodyCtx.LocalIndexMap = localIndexMap;
            bodyCtx.UpvalueMap = upvalueMap;
            bodyCtx.CurrentArgSlots = paramIndexMap.Count + 1;

            var definiteInit = (lambda.Body is Block initBlock)
                ? analysis.GetMetadata<DefiniteAssignmentMetadata>(initBlock)
                : null;
            foreach (var (name, lIdx) in localIndexMap) {
                if (definiteInit is not null && definiteInit.DefinitelyAssigned.Contains(name))
                    continue;
                bodyCtx.Code.Add(new PushOp(0L));
                bodyCtx.Code.Add(new StoreLocalOp(lIdx));
            }

            bodyCtx.EmitNode(lambda.Body, new LambdaEmitState(ctx.LambdaFuncMap, ctx.LambdaCaptureMap, upvalueMap));
            bodyCtx.Code.Add(new ReturnFromCallOp(bodyCtx.CurrentArgSlots));

            var lambdaFunc = ctx.Functions[lambdaIdx];
            ctx.Functions[lambdaIdx] = new FunctionEntry(entryUop, lambdaFunc.ArgSlots, lambdaFunc.RetSlots, localIndexMap.Count) {
                SourceNode = lambda
            };
        }

        // Resolve pending labels to µop indices
        ctx.ResolveLabels();

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

    // ═══════════════════════════════════════════════════════════════════
    //  Static helpers (no EmitContext dependency)
    // ═══════════════════════════════════════════════════════════════════

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
        else if (node is MethodDefinitionNode mdn && !result.Contains(mdn)) {
            result.Add(mdn);
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

    private static string FormatMethodTarget(MethodInfo mi, bool isStatic) {
        var par = string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name));
        var ret = mi.ReturnType == typeof(void) ? "void" : mi.ReturnType.Name;
        var cls = mi.DeclaringType?.Name ?? "?";
        return $"{ret} {cls}.{mi.Name}({par})";
    }

}