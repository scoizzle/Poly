using System.Linq.Expressions;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.VirtualMachine;

internal static class Lowering {
    private readonly record struct EmitWork(Node? Node, EmitPhase Phase, int Data = 0, string? Label = null, string? Label2 = null);

    private enum EmitPhase : byte {
        Enter, AfterChildren, MarkLabel, Jump, JumpIfFalse, Pop, Dup,
    }

    private ref struct EmitContext {
        public BytecodeBuilder Code;
        public Dictionary<int, NodeId> SourceMap;
        public AnalysisResult Analysis;
        public List<FunctionEntry> Functions;
        public Dictionary<MethodDefinitionNode, int>? FunctionIndexMap;
        public IReadOnlyDictionary<string, int>? ParamIndexMap, LocalIndexMap;
        public List<object?>? Constants;
        public List<CallSiteDelegate>? CallSites;
        public Dictionary<Lambda, int>? LambdaFuncMap;
        public Dictionary<Lambda, List<string>>? LambdaCaptureMap;
        public IReadOnlyDictionary<string, int>? UpvalueMap;
        public List<ExceptionRegion> ExceptionRegions;
        public List<LoopBodyEntry>? LoopBodies;
    }

    public static Bytecode Lower(Node root, AnalysisResult analysis) {
        var ctx = new EmitContext {
            Code = new(),
            SourceMap = [],
            Analysis = analysis,
            Functions = [],
            Constants = [],
            CallSites = [],
            ExceptionRegions = [],
            LoopBodies = [],
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
            int entryPc = ctx.Code.Offset;
            var bodyCtx = ctx;
            bodyCtx.ParamIndexMap = paramIndexMap;

            EmitNode(method.Body ?? method, ref bodyCtx, null);

            var func = ctx.Functions[methodIdx];
            ctx.Functions[methodIdx] = new FunctionEntry(entryPc, func.ArgBytes, func.RetBytes, 0) {
                SourceNode = method
            };
        }
        else {
            EmitNode(root, ref ctx, null);
        }

        // Emit final Return if not already present
        ctx.Code.Emit(OpCode.Return);
        ctx.SourceMap[ctx.Code.Offset] = NodeId.NewId();

        // Emit lambda bodies (each with its own Return to pop the call frame)
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
            int entryPc = ctx.Code.Offset;
            var bodyCtx = ctx;
            bodyCtx.ParamIndexMap = paramIndexMap;
            bodyCtx.LocalIndexMap = localIndexMap;
            bodyCtx.UpvalueMap = upvalueMap;

            // Zero-init locals
            foreach (var (name, lIdx) in localIndexMap) {
                var node = FindVariableInBody(lambda.Body, name);
                if (node is null || !IsDefinitelyAssigned(node, analysis)) {
                    bodyCtx.Code.Emit(OpCode.Push, 0L);
                    bodyCtx.Code.Emit(OpCode.StoreLocal, lIdx);
                }
            }

            EmitNode(lambda.Body, ref bodyCtx, new LambdaEmitState(ctx.LambdaFuncMap, ctx.LambdaCaptureMap, upvalueMap));
            ctx.Code.Emit(OpCode.Return);

            var lambdaFunc = ctx.Functions[lambdaIdx];
            ctx.Functions[lambdaIdx] = new FunctionEntry(entryPc, lambdaFunc.ArgBytes, lambdaFunc.RetBytes, localIndexMap.Count) {
                SourceNode = lambda
            };
        }

        // Build programs for each function + main
        // For simplicity, the entire code is one linear sequence.
        // Functions are referenced by PC directly.

        return ctx.Code.BuildProgram(ctx.Functions, ctx.Constants, ctx.CallSites, ctx.ExceptionRegions,
            sourceMap: ctx.SourceMap, analysisResult: analysis, loopBodies: ctx.LoopBodies);
    }

    private sealed record LambdaEmitState(
        IReadOnlyDictionary<Lambda, int>? FuncMap,
        IReadOnlyDictionary<Lambda, List<string>>? CaptureMap,
        IReadOnlyDictionary<string, int>? UpvalueMap
    );

    private static void EmitNode(Node node, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        int sourcePc = ctx.Code.Offset;
        ctx.SourceMap[sourcePc] = node.Id;

        switch (node) {
            case Constant c: {
                    if (c.Value is int iv) { ctx.Code.Emit(OpCode.Push, (long)iv); return; }
                    if (c.Value is long lv) { ctx.Code.Emit(OpCode.Push, lv); return; }
                    if (c.Value is short sv) { ctx.Code.Emit(OpCode.Push, (long)sv); return; }
                    if (c.Value is byte bv) { ctx.Code.Emit(OpCode.Push, (long)bv); return; }
                    if (c.Value is bool bvv) { ctx.Code.Emit(OpCode.Push, bvv ? 1L : 0L); return; }
                    if (c.Value is uint uiv) { ctx.Code.Emit(OpCode.Push, (long)uiv); return; }
                    // Heap-allocated constant
                    int constIdx = ctx.Constants!.Count;
                    ctx.Constants!.Add(c.Value);
                    ctx.Code.Emit(OpCode.Push, constIdx);
                    return;
                }

            case Add a: EmitBinary(a.LeftHandValue, a.RightHandValue, OpCode.Add, ref ctx, lambdaState); return;
            case Subtract s: EmitBinary(s.LeftHandValue, s.RightHandValue, OpCode.Sub, ref ctx, lambdaState); return;
            case Multiply m: EmitBinary(m.LeftHandValue, m.RightHandValue, OpCode.Mul, ref ctx, lambdaState); return;
            case Divide d: EmitBinary(d.LeftHandValue, d.RightHandValue, OpCode.Div, ref ctx, lambdaState); return;
            case Modulo m: EmitDivRem(m.LeftHandValue, m.RightHandValue, ref ctx, lambdaState); return;

            case Equal e: EmitBinary(e.LeftHandValue, e.RightHandValue, OpCode.Eq, ref ctx, lambdaState); return;
            case NotEqual ne: EmitBinary(ne.LeftHandValue, ne.RightHandValue, OpCode.Ne, ref ctx, lambdaState); return;
            case LessThan lt: EmitBinary(lt.LeftHandValue, lt.RightHandValue, OpCode.Lt, ref ctx, lambdaState); return;
            case LessThanOrEqual le: EmitBinary(le.LeftHandValue, le.RightHandValue, OpCode.Le, ref ctx, lambdaState); return;
            case GreaterThan gt: EmitBinary(gt.LeftHandValue, gt.RightHandValue, OpCode.Gt, ref ctx, lambdaState); return;
            case GreaterThanOrEqual ge: EmitBinary(ge.LeftHandValue, ge.RightHandValue, OpCode.Ge, ref ctx, lambdaState); return;

            case UnaryMinus um:
                if (TryGetConstantLong(um.Operand, out long negVal)) {
                    ctx.Code.Emit(OpCode.Push, -negVal); // fold: -Constant → Push -val
                }
                else {
                    EmitNode(um.Operand, ref ctx, lambdaState);
                    ctx.Code.Emit(OpCode.Neg);
                }
                return;
            case Not n: EmitNodeOrConstant(n.Value, OpCode.Not, ref ctx, lambdaState); return;

            case BitwiseNot bn: EmitNode(bn.Operand, ref ctx, lambdaState); ctx.Code.Emit(OpCode.BitNot); return;
            case BitwiseAnd ba: EmitBinary(ba.LeftHandValue, ba.RightHandValue, OpCode.BitAnd, ref ctx, lambdaState); return;
            case BitwiseOr bo: EmitBinary(bo.LeftHandValue, bo.RightHandValue, OpCode.BitOr, ref ctx, lambdaState); return;
            case BitwiseXor bx: EmitBinary(bx.LeftHandValue, bx.RightHandValue, OpCode.BitXor, ref ctx, lambdaState); return;
            case ShiftLeft sl: EmitBinary(sl.LeftHandValue, sl.RightHandValue, OpCode.Shl, ref ctx, lambdaState); return;
            case ShiftRight sr: EmitBinary(sr.LeftHandValue, sr.RightHandValue, OpCode.Shr, ref ctx, lambdaState); return;

            case And and: EmitShortCircuit(and.LeftHandValue, and.RightHandValue, false, ref ctx, lambdaState); return;
            case Or or: EmitShortCircuit(or.LeftHandValue, or.RightHandValue, true, ref ctx, lambdaState); return;

            case Conditional cond: EmitConditional(cond, ref ctx, lambdaState); return;
            case IfStatement iff: EmitIfStatement(iff, ref ctx, lambdaState); return;
            case WhileLoop wl: EmitWhileLoop(wl, ref ctx, lambdaState); return;
            case DoWhileLoop dw: EmitDoWhileLoop(dw, ref ctx, lambdaState); return;
            case ForLoop fl: EmitForLoop(fl, ref ctx, lambdaState); return;
            case BreakStatement: ctx.Code.Emit(OpCode.Jump, 0 /* fixed in for loop */); return;
            case ContinueStatement: ctx.Code.Emit(OpCode.Jump, 0 /* fixed in for loop */); return;

            case Block block: {
                    for (int i = 0; i < block.Nodes.Count; i++) {
                        var child = block.Nodes[i];
                        bool isLast = i == block.Nodes.Count - 1 && EmitsValue(block, ctx.Analysis);
                        EmitNode(child, ref ctx, lambdaState);
                        if (!isLast && EmitsValue(child, ctx.Analysis))
                            ctx.Code.Emit(OpCode.Pop);
                    }
                    return;
                }

            case Variable v: EmitVariable(v, ref ctx, lambdaState); return;
            case Parameter p: EmitParameter(p, ref ctx, lambdaState); return;

            case Assignment assign: {
                    if (TryFuseIncDec(assign, ref ctx, lambdaState)) return;
                    EmitNode(assign.Value, ref ctx, lambdaState);
                    // Dup if this assignment yields the assigned value
                    if (EmitsValue(assign, ctx.Analysis))
                        ctx.Code.Emit(OpCode.Dup);
                    EmitVariableStore(assign.Destination, ref ctx, lambdaState);
                    return;
                }

            case Invoke invoke: EmitInvoke(invoke, ref ctx, lambdaState); return;

            case Lambda lam: EmitLambda(lam, ref ctx, lambdaState); return;
            case Return: return; // handled by caller

            case ThrowStatement thr: EmitNode(thr.Exception, ref ctx, lambdaState); ctx.Code.Emit(OpCode.Throw); return;

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

    private static void EmitBinary(Node left, Node right, OpCode op, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        EmitNode(left, ref ctx, lambdaState);
        // When the right operand is a compile-time integer constant, fuse Push+op into a single
        // operand-bearing instruction (SizeBit set).  The VM's nullary/operand-bearing dispatch
        // naturally separates the two forms using the same opcode value.
        EmitNodeOrConstant(right, op, ref ctx, lambdaState);
    }

    private static void EmitNodeOrConstant(Node node, OpCode op, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        if (TryGetConstantLong(node, out long val)) {
            // Fused form: opcode | SizeBit + inline operand → 9 bytes
            ctx.Code.Emit(op, val);
        }
        else {
            EmitNode(node, ref ctx, lambdaState);
            ctx.Code.Emit(op);
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

    private static bool TryFuseIncDec(Assignment assign, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        if (assign.Destination is not Variable dst || ctx.LocalIndexMap is null)
            return false;
        if (!ctx.LocalIndexMap.TryGetValue(dst.Name, out int slot))
            return false;

        // dst = dst + const  →  IncLocal dst, +const
        if (assign.Value is Add add) {
            if (MatchIncDec(add.LeftHandValue, add.RightHandValue, dst.Name, out long delta) ||
                MatchIncDec(add.RightHandValue, add.LeftHandValue, dst.Name, out delta)) {
                long packed = ((long)slot << 32) | (long)(int)delta;
                ctx.Code.Emit(OpCode.IncLocal, packed);
                return true;
            }
        }

        // dst = dst - const  →  IncLocal dst, -const
        if (assign.Value is Subtract sub &&
            MatchIncDec(sub.LeftHandValue, sub.RightHandValue, dst.Name, out long delta2)) {
            long packed = ((long)slot << 32) | (long)(int)(-delta2);
            ctx.Code.Emit(OpCode.IncLocal, packed);
            return true;
        }

        return false;

        static bool MatchIncDec(Node left, Node right, string dstName, out long delta) {
            delta = 0;
            if (left is Variable v && v.Name == dstName && TryGetConstantLong(right, out delta))
                return true;
            return false;
        }
    }

    private static void EmitDivRem(Node left, Node right, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        EmitNode(left, ref ctx, lambdaState);
        EmitNode(right, ref ctx, lambdaState);
        ctx.Code.Emit(OpCode.DivRem);
        ctx.Code.Emit(OpCode.Pop); // discard remainder, keep quotient
    }

    private static void EmitShortCircuit(Node left, Node right, bool isOr, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        string end = ctx.Code.NextLabel();
        string evalRight = ctx.Code.NextLabel();

        EmitNode(left, ref ctx, lambdaState);
        ctx.Code.Emit(OpCode.Dup);
        if (isOr)
            ctx.Code.EmitJump(OpCode.JumpIfFalse, evalRight);
        else
            ctx.Code.EmitJump(OpCode.JumpIfFalse, end);
        ctx.Code.Mark(evalRight);
        ctx.Code.Emit(OpCode.Pop);
        EmitNode(right, ref ctx, lambdaState);
        ctx.Code.Mark(end);
    }

    private static void EmitConditional(Conditional cond, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        string end = ctx.Code.NextLabel();
        string else_ = ctx.Code.NextLabel();

        EmitNode(cond.Condition, ref ctx, lambdaState);
        ctx.Code.EmitJump(OpCode.JumpIfFalse, else_);
        EmitNode(cond.IfTrue, ref ctx, lambdaState);
        ctx.Code.EmitJump(OpCode.Jump, end);
        ctx.Code.Mark(else_);
        EmitNode(cond.IfFalse, ref ctx, lambdaState);
        ctx.Code.Mark(end);
    }

    private static void EmitIfStatement(IfStatement iff, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        string end = ctx.Code.NextLabel();

        EmitNode(iff.Condition, ref ctx, lambdaState);
        if (iff.ElseBranch is not null) {
            string else_ = ctx.Code.NextLabel();
            ctx.Code.EmitJump(OpCode.JumpIfFalse, else_);
            EmitNode(iff.ThenBranch, ref ctx, lambdaState);
            ctx.Code.EmitJump(OpCode.Jump, end);
            ctx.Code.Mark(else_);
            EmitNode(iff.ElseBranch, ref ctx, lambdaState);
        }
        else {
            ctx.Code.EmitJump(OpCode.JumpIfFalse, end);
            EmitNode(iff.ThenBranch, ref ctx, lambdaState);
        }
        ctx.Code.Mark(end);
    }

    private static void EmitWhileLoop(WhileLoop wl, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        string cont = ctx.Code.NextLabel();
        string end = ctx.Code.NextLabel();

        ctx.Code.Mark(cont);
        int contPC = ctx.Code.Offset;
        EmitNode(wl.Condition, ref ctx, lambdaState);
        ctx.Code.EmitJump(OpCode.JumpIfFalse, end);
        int bodyPC = ctx.Code.Offset;
        EmitNode(wl.Body, ref ctx, lambdaState);
        if (EmitsValue(wl.Body, ctx.Analysis))
            ctx.Code.Emit(OpCode.Pop);
        int bodyEndPC = ctx.Code.Offset;
        ctx.Code.EmitJump(OpCode.Jump, cont);
        ctx.Code.Mark(end);
        int endPC = ctx.Code.Offset;

        ctx.LoopBodies?.Add(new(bodyPC, bodyEndPC - bodyPC, contPC, contPC, endPC, wl.Body) {
            ParamIndexMap = ctx.ParamIndexMap,
            LocalIndexMap = ctx.LocalIndexMap,
        });
    }

    private static void EmitDoWhileLoop(DoWhileLoop dw, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        string cont = ctx.Code.NextLabel();
        string end = ctx.Code.NextLabel();

        ctx.Code.Mark(cont);
        EmitNode(dw.Body, ref ctx, lambdaState);
        EmitNode(dw.Condition, ref ctx, lambdaState);
        ctx.Code.EmitJump(OpCode.JumpIfFalse, end);
        ctx.Code.EmitJump(OpCode.Jump, cont);
        ctx.Code.Mark(end);
    }

    private static void EmitForLoop(ForLoop fl, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        string cont = ctx.Code.NextLabel();
        string end = ctx.Code.NextLabel();

        if (fl.Initializer is not null) {
            EmitNode(fl.Initializer, ref ctx, lambdaState);
            if (EmitsValue(fl.Initializer, ctx.Analysis))
                ctx.Code.Emit(OpCode.Pop);
        }
        ctx.Code.Mark(cont);
        int contPC = ctx.Code.Offset;
        if (fl.Condition is not null) {
            EmitNode(fl.Condition, ref ctx, lambdaState);
            ctx.Code.EmitJump(OpCode.JumpIfFalse, end);
        }
        int bodyPC = ctx.Code.Offset;
        EmitNode(fl.Body, ref ctx, lambdaState);
        int bodyEndPC = ctx.Code.Offset;
        int incPC = ctx.Code.Offset;
        if (fl.Increment is not null) {
            EmitNode(fl.Increment, ref ctx, lambdaState);
            if (EmitsValue(fl.Increment, ctx.Analysis))
                ctx.Code.Emit(OpCode.Pop);
        }
        ctx.Code.EmitJump(OpCode.Jump, cont);
        ctx.Code.Mark(end);
        int endPC = ctx.Code.Offset;

        ctx.LoopBodies?.Add(new(bodyPC, bodyEndPC - bodyPC, contPC, incPC, endPC, fl.Body) {
            ParamIndexMap = ctx.ParamIndexMap,
            LocalIndexMap = ctx.LocalIndexMap,
        });
    }

    private static void EmitVariable(Variable v, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        if (ctx.ParamIndexMap?.TryGetValue(v.Name, out int pIdx) == true) {
            ctx.Code.Emit(OpCode.LoadArg, pIdx);
            return;
        }
        if (ctx.LocalIndexMap?.TryGetValue(v.Name, out int lIdx) == true) {
            ctx.Code.Emit(OpCode.LoadLocal, lIdx);
            return;
        }
        if (lambdaState?.UpvalueMap?.TryGetValue(v.Name, out int uIdx) == true) {
            ctx.Code.Emit(OpCode.LoadUpvalue, uIdx);
            return;
        }
        throw new InvalidOperationException($"Variable '{v.Name}' not found in any scope");
    }

    private static void EmitParameter(Parameter p, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        if (p.DefaultValue is not null) {
            EmitNode(p.DefaultValue, ref ctx, lambdaState);
        }
        else if (ctx.ParamIndexMap?.TryGetValue(p.Name ?? "", out int pIdx) == true) {
            ctx.Code.Emit(OpCode.LoadArg, pIdx);
        }
        else {
            ctx.Code.Emit(OpCode.Push, 0L);
        }
    }

    private static void EmitVariableStore(Node target, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        if (target is Variable v) {
            if (ctx.ParamIndexMap?.TryGetValue(v.Name, out int pIdx) == true) {
                ctx.Code.Emit(OpCode.StoreArg, pIdx);
                return;
            }
            if (ctx.LocalIndexMap?.TryGetValue(v.Name, out int lIdx) == true) {
                ctx.Code.Emit(OpCode.StoreLocal, lIdx);
                return;
            }
            if (lambdaState?.UpvalueMap?.TryGetValue(v.Name, out int uIdx) == true) {
                ctx.Code.Emit(OpCode.StoreUpvalue, uIdx);
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
            ctx.Code.Emit(OpCode.Push, (long)args.Length);
            ctx.Code.Emit(OpCode.Call, funcIdx);
            return;
        }

        if (resolved is ClrMethod clrMethod) {
            int siteIdx = ctx.CallSites!.Count;
            bool isStatic = clrMethod.LifetimeModifier == LifetimeModifier.Static;
            foreach (var arg in args)
                EmitNode(arg, ref ctx, lambdaState);
            ctx.CallSites!.Add(CallSiteCompiler.Compile(clrMethod.MethodInfo, isStatic));
            ctx.Code.Emit(OpCode.CallExternal, siteIdx);
            return;
        }

        if (invoke.Delegate is Lambda lambda && ctx.LambdaFuncMap!.TryGetValue(lambda, out int lambdaIdx)) {
            // Direct lambda call: args layout = [dummy_closure][user_args][argCount]
            ctx.Code.Emit(OpCode.Push, -1L); // dummy closure handle at index 0
            foreach (var arg in args)
                EmitNode(arg, ref ctx, lambdaState);
            ctx.Code.Emit(OpCode.Push, (long)args.Length + 1);
            ctx.Code.Emit(OpCode.Call, lambdaIdx);
            return;
        }

        // Generic lambda/delegate call via CallClosure: [closure_handle][user_args][argCount]
        EmitNode(invoke.Delegate, ref ctx, lambdaState);
        foreach (var arg in args)
            EmitNode(arg, ref ctx, lambdaState);
        ctx.Code.Emit(OpCode.Push, (long)args.Length + 1);
        ctx.Code.Emit(OpCode.CallClosure);
    }

    private static void EmitLambda(Lambda lam, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        if (ctx.LambdaCaptureMap is null || !ctx.LambdaCaptureMap.TryGetValue(lam, out var captures))
            throw new InvalidOperationException("Lambda not found in capture map");

        int funcIdx = ctx.LambdaFuncMap!.TryGetValue(lam, out int idx) ? idx : 0;

        // Push captures
        foreach (var cap in captures) {
            if (ctx.ParamIndexMap?.TryGetValue(cap, out int pIdx) == true)
                ctx.Code.Emit(OpCode.LoadArg, pIdx);
            else if (ctx.LocalIndexMap?.TryGetValue(cap, out int lIdx) == true)
                ctx.Code.Emit(OpCode.LoadLocal, lIdx);
            else if (lambdaState?.UpvalueMap?.TryGetValue(cap, out int uIdx) == true)
                ctx.Code.Emit(OpCode.LoadUpvalue, uIdx);
            else
                ctx.Code.Emit(OpCode.Push, 0L);
        }

        ctx.Code.Emit(OpCode.AllocClosure, (long)(captures.Count << 32) | (long)(uint)funcIdx);
    }

    private static void EmitTryCatchFinally(TryCatchFinally tcf, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        string end = ctx.Code.NextLabel();
        int? finallyEntry = null;
        int? catchStart = null;

        int tryStart = ctx.Code.Offset;
        EmitNode(tcf.TryBlock, ref ctx, lambdaState);
        ctx.Code.EmitJump(OpCode.Jump, end);
        int tryEnd = ctx.Code.Offset;

        if (tcf.CatchClauses is not null) {
            catchStart = ctx.Code.Offset;
            foreach (var cc in tcf.CatchClauses) {
                ctx.Code.Mark(ctx.Code.NextLabel());
                if (cc.VariableName is not null) {
                    ctx.Code.Emit(OpCode.Dup);
                    if (ctx.ParamIndexMap?.TryGetValue(cc.VariableName, out int pi) == true)
                        ctx.Code.Emit(OpCode.StoreArg, pi);
                    else if (ctx.LocalIndexMap?.TryGetValue(cc.VariableName, out int li) == true)
                        ctx.Code.Emit(OpCode.StoreLocal, li);
                }
                else ctx.Code.Emit(OpCode.Pop);
                EmitNode(cc.Body, ref ctx, lambdaState);
                ctx.Code.EmitJump(OpCode.Jump, end);
            }
        }

        if (tcf.FinallyBlock is not null) {
            finallyEntry = ctx.Code.Offset;
            EmitNode(tcf.FinallyBlock, ref ctx, lambdaState);
            if (EmitsValue(tcf.FinallyBlock, ctx.Analysis))
                ctx.Code.Emit(OpCode.Pop);
            ctx.Code.Emit(OpCode.EndFinally);
        }

        ctx.Code.Mark(end);

        ctx.ExceptionRegions.Add(new ExceptionRegion(tryStart, tryEnd, catchStart ?? -1, finallyEntry));
    }

    private static void EmitForEachLoop(ForEachLoop fel, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        EmitNode(fel.Collection, ref ctx, lambdaState);
        var getEnum = typeof(IEnumerable<>).MakeGenericType(typeof(object))
            .GetMethod("GetEnumerator")!
            .MakeGenericMethod(typeof(object));
        int initSite = AddCallSite(ref ctx, CallSiteCompiler.Compile(getEnum, false));
        ctx.Code.Emit(OpCode.Push, 1L);
        ctx.Code.Emit(OpCode.CallExternal, initSite);

        string cont = ctx.Code.NextLabel();
        string end = ctx.Code.NextLabel();

        ctx.Code.Mark(cont);
        ctx.Code.Emit(OpCode.Dup);
        int moveNextSite = AddCallSite(ref ctx, CallSiteCompiler.Compile(
            typeof(IEnumerator).GetMethod("MoveNext")!, false));
        ctx.Code.Emit(OpCode.Push, 1L);
        ctx.Code.Emit(OpCode.CallExternal, moveNextSite);
        ctx.Code.EmitJump(OpCode.JumpIfFalse, end);

        ctx.Code.Emit(OpCode.Dup);
        int currentSite = AddCallSite(ref ctx, CallSiteCompiler.Compile(
            typeof(IEnumerator).GetProperty("Current")!.GetGetMethod()!, false));
        ctx.Code.Emit(OpCode.Push, 1L);
        ctx.Code.Emit(OpCode.CallExternal, currentSite);

        EmitVariableStore(fel.LoopVariable, ref ctx, lambdaState);
        EmitNode(fel.Body, ref ctx, lambdaState);
        if (EmitsValue(fel.Body, ctx.Analysis))
            ctx.Code.Emit(OpCode.Pop);
        ctx.Code.EmitJump(OpCode.Jump, cont);
        ctx.Code.Mark(end);
    }

    private static void EmitUsingStatement(UsingStatement us, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        int holderIdx = ctx.Constants!.Count;
        ctx.Constants.Add(new object[1]);

        EmitNode(us.Resource, ref ctx, lambdaState);
        ctx.Code.Emit(OpCode.StoreValue);
        EmitNode(us.Body, ref ctx, lambdaState);
        if (EmitsValue(us.Body, ctx.Analysis))
            ctx.Code.Emit(OpCode.Pop);
    }

    private static void EmitMember(Member m, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis.GetResolvedMember(m);
        if (resolved is ClrMethod getter) {
            EmitNode(m.Value, ref ctx, lambdaState);
            int siteIdx = ctx.CallSites!.Count;
            bool isStatic = getter.LifetimeModifier == LifetimeModifier.Static;
            ctx.CallSites!.Add(CallSiteCompiler.Compile(getter.MethodInfo, isStatic));
            ctx.Code.Emit(OpCode.CallExternal, siteIdx);
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
            int siteIdx = ctx.CallSites!.Count;
            bool isStatic = getter.LifetimeModifier == LifetimeModifier.Static;
            ctx.CallSites!.Add(CallSiteCompiler.Compile(getter.MethodInfo, isStatic));
            ctx.Code.Emit(OpCode.CallExternal, siteIdx);
            return;
        }
        throw new InvalidOperationException($"Index access not resolved");
    }

    private static void EmitNew(New n, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis.GetResolvedMember(n);
        if (resolved is ClrConstructor ctor) {
            foreach (var arg in n.Arguments)
                EmitNode(arg, ref ctx, lambdaState);
            int siteIdx = ctx.CallSites!.Count;
            ctx.CallSites!.Add(CallSiteCompiler.CompileConstructor(ctor.ConstructorInfo));
            ctx.Code.Emit(OpCode.CallExternal, siteIdx);
            return;
        }
        throw new InvalidOperationException($"Constructor not resolved for new {n.Type}");
    }

    private static void EmitAwait(Await aw, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        EmitNode(aw.Operand, ref ctx, lambdaState);
        var getAwaiter = typeof(Task<>).GetMethod("GetAwaiter")?.MakeGenericMethod(typeof(object))
            ?? typeof(Task).GetMethod("GetAwaiter");
        if (getAwaiter is not null) {
            int siteIdx = ctx.CallSites!.Count;
            ctx.CallSites.Add(CallSiteCompiler.Compile(getAwaiter, false));
            ctx.Code.Emit(OpCode.CallExternal, siteIdx);
        }
    }

    private static void EmitSuspendNode(SuspendNode sn, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        EmitNode(sn.Inner, ref ctx, lambdaState);
        ctx.Code.Emit(OpCode.Pop);
    }

    private static int AddCallSite(ref EmitContext ctx, CallSiteDelegate d) {
        int idx = ctx.CallSites!.Count;
        ctx.CallSites!.Add(d);
        return idx;
    }

    private static void EmitIndexAccessStore(IndexAccess ia, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis.GetResolvedMember(ia);
        if (resolved is not ClrMethod setter)
            throw new InvalidOperationException($"Index setter not found");

        int siteIdx = ctx.CallSites!.Count;
        bool isStatic = setter.LifetimeModifier == LifetimeModifier.Static;
        ctx.CallSites!.Add(CallSiteCompiler.Compile(setter.MethodInfo, isStatic));
        ctx.Code.Emit(OpCode.CallExternal, siteIdx);
    }

    private static void EmitMemberStore(Member m, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis.GetResolvedMember(m);
        if (resolved is not ClrMethod setter)
            throw new InvalidOperationException($"Member setter not found for {m.MemberName}");

        int siteIdx = ctx.CallSites!.Count;
        bool isStatic = setter.LifetimeModifier == LifetimeModifier.Static;
        ctx.CallSites!.Add(CallSiteCompiler.Compile(setter.MethodInfo, isStatic));
        ctx.Code.Emit(OpCode.CallExternal, siteIdx);
    }

    // ── Discovery helpers (unchanged from original) ──

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
        // Collect names, sort for deterministic slot assignment
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
        // Loop constructs don't produce values — their bodies' results are
        // consumed inside the loop by an explicit Pop in EmitWhileLoop etc.
        if (node is WhileLoop or DoWhileLoop or ForLoop) return false;
        // Assignments always produce a value, regardless of analysis metadata.
        if (node is Assignment) return true;
        var type = analysis.GetResolvedType(node);
        if (type is not null && type.Name != "Void")
            return true;
        if (node is Block block && block.Nodes.Count > 0)
            return EmitsValue(block.Nodes[^1], analysis);
        return false;
    }
}