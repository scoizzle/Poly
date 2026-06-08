using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.VirtualMachine;

internal static class Lowering {
    private sealed record LambdaEmitState(
        IReadOnlyDictionary<Lambda, int>? FuncMap,
        IReadOnlyDictionary<Lambda, List<string>>? CaptureMap,
        IReadOnlyDictionary<string, int>? UpvalueMap);

    private readonly record struct EmitWork(Node? Node, EmitPhase Phase, int Data = 0, int Data2 = 0);

    private enum EmitPhase : byte {
        Enter,
        AfterChildren,
        EmitPop,
        MarkLabel,
        Jump,
        JumpIfFalse,
        PushInt,
        AllocateClosure,
        Call,
    }

    private ref struct EmitContext {
        public List<byte> Code;
        public Dictionary<int, NodeId> SourceMap;
        public AnalysisResult? Analysis;
        public List<FunctionEntry> Functions;
        public Dictionary<MethodDefinitionNode, int>? FunctionIndexMap;
        public IReadOnlyDictionary<string, int>? ParamIndexMap;
        public IReadOnlyDictionary<string, int>? LocalIndexMap;
        public List<object?>? Constants;
        public List<CallSiteDelegate>? CallSites;
        public List<(int CodeOffset, int TargetPc)>? Relocations;
        public LabelContext? Labels;
        public Dictionary<Lambda, int>? LambdaFuncMap;
        public Dictionary<Lambda, List<string>>? LambdaCaptureMap;
        public IReadOnlyDictionary<string, int>? UpvalueMap;
    }

    private static readonly CallSiteDelegate IsNotNullDelegate = state => {
        var (argSlots, hasRet) = state.Stack.Pop<(int, int)>();
        int baseOff = state.Stack.SP - argSlots;
        int raw = state.Stack.AsSpan()[baseOff];
        bool isNotNull;
        if (raw >= 0 && raw < state.Heap.Count) {
            isNotNull = state.Heap.Get(raw) is not null;
        }
        else {
            isNotNull = raw != 0;
        }
        if (argSlots > 0) state.Stack.Drop(argSlots);
        if (hasRet != 0) state.Stack.Push(isNotNull ? 1 : 0);
    };

    private static CallSiteDelegate CreateTypeIsDelegate(Type targetType) {
        return state => {
            var (argSlots, hasRet) = state.Stack.Pop<(int, int)>();
            int baseOff = state.Stack.SP - argSlots;
            int raw = state.Stack.AsSpan()[baseOff];
            bool result;
            if (raw >= 0 && raw < state.Heap.Count) {
                var val = state.Heap.Get(raw);
                result = val is not null && targetType.IsInstanceOfType(val);
            }
            else {
                result = raw != 0;
            }
            if (argSlots > 0) state.Stack.Drop(argSlots);
            if (hasRet != 0) state.Stack.Push(result ? 1 : 0);
        };
    }

    private static readonly CallSiteDelegate AwaitResultDelegate = state => {
        var (argSlots, hasRet) = state.Stack.Pop<(int, int)>();
        int baseOff = state.Stack.SP - argSlots;
        int handle = state.Stack.AsSpan()[baseOff];
        object? awaitable = handle >= 0 && handle < state.Heap.Count ? state.Heap.Get(handle) : (object?)handle;
        var awaiter = awaitable?.GetType().GetMethod("GetAwaiter", Type.EmptyTypes)?.Invoke(awaitable, null);
        var result = awaiter?.GetType().GetMethod("GetResult", Type.EmptyTypes)?.Invoke(awaiter, null);
        if (argSlots > 0) state.Stack.Drop(argSlots);
        if (hasRet != 0) {
            if (result is int iv) state.Stack.Push(iv);
            else state.Stack.Push(state.Heap.Allocate(result));
        }
    };

    private static readonly CallSiteDelegate InitEnumeratorDelegate = state => {
        var (argSlots, hasRet) = state.Stack.Pop<(int, int)>();
        int baseOff = state.Stack.SP - argSlots;
        int holderHandle = state.Stack.AsSpan()[baseOff];
        int collectionHandle = state.Stack.AsSpan()[baseOff + 1];
        object? collection = collectionHandle >= 0 && collectionHandle < state.Heap.Count ? state.Heap.Get(collectionHandle) : (object?)collectionHandle;
        if (collection is IEnumerable enumerable) {
            var enumerator = enumerable.GetEnumerator();
            if (holderHandle >= 0 && holderHandle < state.Heap.Count && state.Heap.Get(holderHandle) is object[] holder)
                holder[0] = enumerator;
        }
        if (argSlots > 0) state.Stack.Drop(argSlots);
    };

    private static readonly CallSiteDelegate GetCurrentDelegate = state => {
        var (argSlots, hasRet) = state.Stack.Pop<(int, int)>();
        int baseOff = state.Stack.SP - argSlots;
        int holderHandle = state.Stack.AsSpan()[baseOff];
        var enumerator = holderHandle >= 0 && holderHandle < state.Heap.Count && state.Heap.Get(holderHandle) is object[] h2 ? h2[0] as IEnumerator : null;
        object? current = enumerator?.Current;
        if (argSlots > 0) state.Stack.Drop(argSlots);
        if (hasRet != 0) {
            if (current is int iv) state.Stack.Push(iv);
            else state.Stack.Push(state.Heap.Allocate(current));
        }
    };

    private static readonly CallSiteDelegate DisposeEnumeratorDelegate = state => {
        var (argSlots, hasRet) = state.Stack.Pop<(int, int)>();
        int baseOff = state.Stack.SP - argSlots;
        int holderHandle = state.Stack.AsSpan()[baseOff];
        if (holderHandle >= 0 && holderHandle < state.Heap.Count && state.Heap.Get(holderHandle) is object[] h3 && h3[0] is IDisposable d)
            d.Dispose();
        if (argSlots > 0) state.Stack.Drop(argSlots);
    };

    private static readonly CallSiteDelegate SaveResourceDelegate = state => {
        var (argSlots, hasRet) = state.Stack.Pop<(int, int)>();
        int baseOff = state.Stack.SP - argSlots;
        int holderHandle = state.Stack.AsSpan()[baseOff];
        int resourceHandle = state.Stack.AsSpan()[baseOff + 1];
        if (holderHandle >= 0 && holderHandle < state.Heap.Count && state.Heap.Get(holderHandle) is object[] holder) {
            object? resource = resourceHandle >= 0 && resourceHandle < state.Heap.Count ? state.Heap.Get(resourceHandle) : (object?)resourceHandle;
            holder[0] = resource!;
        }
        if (argSlots > 0) state.Stack.Drop(argSlots);
    };

    private static readonly CallSiteDelegate DisposeResourceDelegate = state => {
        var (argSlots, hasRet) = state.Stack.Pop<(int, int)>();
        int baseOff = state.Stack.SP - argSlots;
        int holderHandle = state.Stack.AsSpan()[baseOff];
        if (holderHandle >= 0 && holderHandle < state.Heap.Count && state.Heap.Get(holderHandle) is object[] h4 && h4[0] is IDisposable d)
            d.Dispose();
        if (argSlots > 0) state.Stack.Drop(argSlots);
    };

    public static Bytecode Lower(Node root, AnalysisResult analysis) {
        var ctx = new EmitContext {
            Code = new List<byte>(),
            SourceMap = new Dictionary<int, NodeId>(),
            Analysis = analysis,
            Functions = new List<FunctionEntry>(),
            FunctionIndexMap = new Dictionary<MethodDefinitionNode, int>(),
            Constants = new List<object?>(),
            CallSites = new List<CallSiteDelegate>(),
            Relocations = new List<(int CodeOffset, int TargetPc)>(),
            Labels = new LabelContext(),
            LambdaFuncMap = new Dictionary<Lambda, int>(ReferenceEqualityComparer.Instance),
            LambdaCaptureMap = new Dictionary<Lambda, List<string>>(ReferenceEqualityComparer.Instance),
        };

        var referencedMethods = new List<MethodDefinitionNode>();
        DiscoverFunctions(root, analysis, referencedMethods);

        var referencedLambdas = new List<Lambda>();
        DiscoverLambdas(root, referencedLambdas);

        int jumpOverMainPc = EmitJump(ctx.Code, 0, ctx.Relocations);

        foreach (var method in referencedMethods) {
            int entryPc = ctx.Code.Count;
            ctx.FunctionIndexMap![method] = ctx.Functions.Count;
            int paramCount = method.Parameters?.Count ?? 0;
            var paramIndexMap = new Dictionary<string, int>();
            if (method.Parameters is not null) {
                for (int i = 0; i < method.Parameters.Count; i++)
                    paramIndexMap[method.Parameters[i].Name ?? ""] = i;
            }

            var localIndexMap = new Dictionary<string, int>();
            if (method.Body is not null)
                DiscoverLocals(method.Body, paramIndexMap, localIndexMap);

            int methodRetBytes = (method.Body is not null && EmitsValue(method.Body)) ? 1 : 0;
            ctx.Functions.Add(new FunctionEntry(entryPc, paramCount, methodRetBytes, localIndexMap.Count));

            if (method.Body is not null) {
                ctx.ParamIndexMap = paramIndexMap;
                ctx.LocalIndexMap = localIndexMap;
                Emit(method.Body, ref ctx);
            }
            ctx.Code.Add((byte)OpCode.Return);
        }

        // Pre-scan: allocate function indices for all lambdas before emitting any bodies
        foreach (var lambda in referencedLambdas) {
            int funcIdx = ctx.Functions.Count;
            ctx.LambdaFuncMap![lambda] = funcIdx;
            ctx.Functions.Add(new FunctionEntry(0, 0, 0, 0)); // placeholder
        }

        // Emit lambda bodies with full LambdaFuncMap available
        for (int i = 0; i < referencedLambdas.Count; i++) {
            var lambda = referencedLambdas[i];
            int entryPc = ctx.Code.Count;

            int paramCount = lambda.Parameters.Count;
            var paramIndexMap = new Dictionary<string, int>();
            for (int j = 0; j < lambda.Parameters.Count; j++)
                paramIndexMap[lambda.Parameters[j].Name ?? ""] = j + 1;

            var localIndexMap = new Dictionary<string, int>();
            if (lambda.Body is not null)
                DiscoverLocals(lambda.Body, paramIndexMap, localIndexMap);

            var captures = new List<string>();
            if (lambda.Body is not null)
                DiscoverCapturesWalk(lambda.Body, paramIndexMap, localIndexMap, captures);

            var upvalueMap = new Dictionary<string, int>();
            for (int j = 0; j < captures.Count; j++)
                upvalueMap[captures[j]] = j;
            ctx.LambdaCaptureMap![lambda] = captures;

            int retBytes = (lambda.Body is not null && EmitsValue(lambda.Body)) ? 1 : 0;
            ctx.Functions[i] = new FunctionEntry(entryPc, paramCount + 1, retBytes, localIndexMap.Count);

            ctx.ParamIndexMap = paramIndexMap;
            ctx.LocalIndexMap = localIndexMap;
            ctx.UpvalueMap = upvalueMap;
            if (lambda.Body is not null) {
                var bodyLambdaState = new LambdaEmitState(ctx.LambdaFuncMap, ctx.LambdaCaptureMap, upvalueMap);
                Emit(lambda.Body, ref ctx, bodyLambdaState);
            }
            ctx.Code.Add((byte)OpCode.Return);
        }

        ctx.ParamIndexMap = null;
        ctx.LocalIndexMap = null;
        ctx.UpvalueMap = null;

        int mainEntry = ctx.Code.Count;
        PatchJump(ctx.Code, jumpOverMainPc, mainEntry);
        var rootLambdaState = new LambdaEmitState(ctx.LambdaFuncMap, ctx.LambdaCaptureMap, null);
        Emit(root, ref ctx, rootLambdaState);

        foreach (var (codeOff, targetPc) in ctx.Relocations!)
            PatchJump(ctx.Code, codeOff, targetPc);

        ResolveAllJumps(ctx.Code, ctx.Labels!);

        Type? resultType = analysis?.GetResolvedType(root)?.GetRuntimeType();
        return new Bytecode([.. ctx.Code], ctx.SourceMap, ctx.Functions, ctx.Constants, ctx.CallSites, ctx.Labels!.ExceptionRegions, resultType);
    }

    private sealed class LabelContext {
        public Dictionary<string, int> Targets = new();
        public List<(int CodeOffset, string Name)> Unresolved = new();
        public int Counter;
        public Stack<(string Name, string Break, string Continue)> LoopLabels = new();
        public List<ExceptionRegion> ExceptionRegions = new();
        public string? PendingLoopLabel;

        public string Next() => $"L{Counter++}";

        public void Mark(string name, List<byte> code) {
            Targets[name] = code.Count;
            for (int i = Unresolved.Count - 1; i >= 0; i--) {
                var (co, n) = Unresolved[i];
                if (n == name) {
                    PatchJump(code, co, code.Count);
                    Unresolved.RemoveAt(i);
                }
            }
        }

        public void JumpTo(string name, List<byte> code, List<(int CodeOffset, int TargetPc)>? relocations) {
            if (Targets.TryGetValue(name, out int targetPc)) {
                int pc = code.Count;
                code.Add((byte)OpCode.Jump);
                EmitInt32(code, targetPc);
            }
            else {
                int pc = code.Count;
                code.Add((byte)OpCode.Jump);
                EmitInt32(code, 0);
                Unresolved.Add((pc, name));
            }
        }

        public void JumpIfFalseTo(string name, List<byte> code, List<(int CodeOffset, int TargetPc)>? relocations) {
            if (Targets.TryGetValue(name, out int targetPc)) {
                code.Add((byte)OpCode.JumpIfFalse);
                EmitInt32(code, targetPc);
            }
            else {
                int pc = code.Count;
                code.Add((byte)OpCode.JumpIfFalse);
                EmitInt32(code, 0);
                Unresolved.Add((pc, name));
            }
        }
    }

    private static void ResolveAllJumps(List<byte> code, LabelContext labels) {
        foreach (var (co, name) in labels.Unresolved) {
            if (labels.Targets.TryGetValue(name, out int pc))
                PatchJump(code, co, pc);
        }
    }

    private static void DiscoverFunctions(Node node, AnalysisResult? analysis, List<MethodDefinitionNode> result) {
        if (node is null) return;
        if (node is Invoke invoke && analysis?.GetResolvedMember(invoke) is AstMethodDefinition astMethod) {
            var methodDef = astMethod.DefinitionNode;
            if (!result.Exists(m => ReferenceEquals(m, methodDef))) {
                result.Add(methodDef);
                if (methodDef.Body is not null)
                    DiscoverFunctions(methodDef.Body, analysis, result);
            }
        }
        foreach (var child in node.Children) {
            if (child is not null)
                DiscoverFunctions(child, analysis, result);
        }
    }

    private static void DiscoverLambdas(Node node, List<Lambda> result) {
        if (node is Lambda lambda) {
            if (!result.Exists(l => ReferenceEquals(l, lambda)))
                result.Add(lambda);
        }
        foreach (var child in node.Children) {
            if (child is not null)
                DiscoverLambdas(child, result);
        }
    }

    private static void DiscoverCapturesWalk(
        Node node,
        IReadOnlyDictionary<string, int>? paramIndexMap,
        IReadOnlyDictionary<string, int>? localIndexMap,
        List<string> captures) {
        if (node is null) return;
        if (node is Variable v && v.Name is not null) {
            bool isParam = paramIndexMap is not null && paramIndexMap.ContainsKey(v.Name);
            bool isLocal = localIndexMap is not null && localIndexMap.ContainsKey(v.Name);
            if (!isParam && !isLocal && !captures.Contains(v.Name))
                captures.Add(v.Name);
        }
        if (node is Assignment a && a.Destination is Variable dv && dv.Name is not null) {
            bool isParam = paramIndexMap is not null && paramIndexMap.ContainsKey(dv.Name);
            bool isLocal = localIndexMap is not null && localIndexMap.ContainsKey(dv.Name);
            if (!isParam && !isLocal && !captures.Contains(dv.Name))
                captures.Add(dv.Name);
        }
        foreach (var child in node.Children) {
            if (child is not null)
                DiscoverCapturesWalk(child, paramIndexMap, localIndexMap, captures);
        }
    }

    private static void DiscoverLocals(Node node, IReadOnlyDictionary<string, int> paramIndexMap, Dictionary<string, int> localIndexMap) {
        if (node is Variable v && v.Name is not null && !paramIndexMap.ContainsKey(v.Name) && !localIndexMap.ContainsKey(v.Name))
            localIndexMap[v.Name] = localIndexMap.Count;
        if (node is Assignment a && a.Destination is Variable dv && dv.Name is not null && !paramIndexMap.ContainsKey(dv.Name) && !localIndexMap.ContainsKey(dv.Name))
            localIndexMap[dv.Name] = localIndexMap.Count;
        if (node is ForEachLoop fl && fl.LoopVariable.Name is not null && !paramIndexMap.ContainsKey(fl.LoopVariable.Name) && !localIndexMap.ContainsKey(fl.LoopVariable.Name))
            localIndexMap[fl.LoopVariable.Name] = localIndexMap.Count;
        if (node is Block block) {
            foreach (var bv2 in block.Variables)
                if (bv2 is Variable bv && bv.Name is not null && !paramIndexMap.ContainsKey(bv.Name) && !localIndexMap.ContainsKey(bv.Name))
                    localIndexMap[bv.Name] = localIndexMap.Count;
        }
        foreach (var child in node.Children) {
            if (child is not null)
                DiscoverLocals(child, paramIndexMap, localIndexMap);
        }
    }

    private static bool EmitsValue(Node node) {
        if (node is null) return false;
        return node switch {
            Constant => true,
            Add or Subtract or Multiply or Divide or Modulo or UnaryMinus => true,
            Equal or NotEqual or LessThan or LessThanOrEqual or GreaterThan or GreaterThanOrEqual => true,
            And or Or or Not => true,
            Conditional => true,
            Variable or Coalesce or NullForgiving => true,
            Member or IndexAccess or New => true,
            Invoke or Default or Await => true,
            Parameter or TypeCast or TypeIs => true,
            Assignment or Lambda => true,
            SuspendNode => false,
            Block block => block.Nodes.Count > 0 && EmitsValue(block.Nodes[^1]),
            _ => false,
        };
    }

    private static void Emit(Node node, ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        if (node is null) return;

        var replacement = ctx.Analysis?.GetNodeReplacement(node);
        if (replacement is not null && !ReferenceEquals(replacement, node)) {
            Emit(replacement, ref ctx, lambdaState);
            return;
        }

        if (node is Constant constant) {
            int pc = ctx.Code.Count;
            ctx.SourceMap[pc] = node.Id;
            if (TryInlinableInt(constant.Value, out int intVal)) {
                ctx.Code.Add((byte)OpCode.PushInt);
                EmitInt32(ctx.Code, intVal);
            }
            else if (TryInlinableLong(constant.Value, out long longVal)) {
                ctx.Code.Add((byte)OpCode.PushLong);
                EmitInt64(ctx.Code, longVal);
            }
            else if (TryInlinableDouble(constant.Value, out double doubleVal)) {
                ctx.Code.Add((byte)OpCode.PushDouble);
                EmitDouble(ctx.Code, doubleVal);
            }
            else {
                int idx = ctx.Constants?.Count ?? 0;
                ctx.Constants?.Add(constant.Value);
                ctx.Code.Add((byte)OpCode.LoadConst);
                EmitInt32(ctx.Code, idx);
            }
            return;
        }

        int emitPc = ctx.Code.Count;
        ctx.SourceMap[emitPc] = node.Id;

        switch (node) {
            case Add add:
                if (ctx.Analysis?.GetResolvedType(add)?.GetRuntimeType() == typeof(string)) {
                    Emit(add.LeftHandValue, ref ctx, lambdaState);
                    Emit(add.RightHandValue, ref ctx, lambdaState);
                    int concatIdx = ctx.CallSites?.Count ?? 0;
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 2);
                    ctx.Code.Add((byte)OpCode.StrConcat);
                }
                else {
                    EmitBinary(add.LeftHandValue, add.RightHandValue, ResolveBinaryOp(add, OpCode.Add, OpCode.Add, OpCode.DAdd, ctx.Analysis),
                              ref ctx, lambdaState);
                }
                return;

            case Subtract sub:
                EmitBinary(sub.LeftHandValue, sub.RightHandValue, ResolveBinaryOp(sub, OpCode.Sub, OpCode.Sub, OpCode.DSub, ctx.Analysis),
                          ref ctx, lambdaState);
                return;

            case Multiply mul:
                EmitBinary(mul.LeftHandValue, mul.RightHandValue, ResolveBinaryOp(mul, OpCode.Mul, OpCode.Mul, OpCode.DMul, ctx.Analysis),
                          ref ctx, lambdaState);
                return;

            case Divide div:
                EmitBinary(div.LeftHandValue, div.RightHandValue, ResolveBinaryOp(div, OpCode.Div, OpCode.UDiv, OpCode.DDiv, ctx.Analysis),
                          ref ctx, lambdaState);
                return;

            case Modulo mod:
                EmitBinary(mod.LeftHandValue, mod.RightHandValue, ResolveBinaryOp(mod, OpCode.Mod, OpCode.UMod, OpCode.Mod, ctx.Analysis),
                          ref ctx, lambdaState);
                return;

            case UnaryMinus un:
                Emit(un.Operand, ref ctx, lambdaState); {
                    var typeDef = ctx.Analysis?.GetResolvedType(un);
                    var clrType = typeDef?.GetRuntimeType();
                    if (clrType == typeof(double) || clrType == typeof(float)) {
                        ctx.Code.Add((byte)OpCode.DNeg);
                        return;
                    }
                }
                ctx.Code.Add((byte)OpCode.Neg);
                return;

            case Equal eq:
                EmitBinary(eq.LeftHandValue, eq.RightHandValue, ResolveComparisonOp(eq.LeftHandValue, eq.RightHandValue, OpCode.Eq, OpCode.Eq, OpCode.DEq, ctx.Analysis),
                          ref ctx, lambdaState);
                return;

            case NotEqual ne:
                EmitBinary(ne.LeftHandValue, ne.RightHandValue, ResolveComparisonOp(ne.LeftHandValue, ne.RightHandValue, OpCode.Ne, OpCode.Ne, OpCode.DNe, ctx.Analysis),
                          ref ctx, lambdaState);
                return;

            case LessThan lt:
                EmitBinary(lt.LeftHandValue, lt.RightHandValue, ResolveComparisonOp(lt.LeftHandValue, lt.RightHandValue, OpCode.Lt, OpCode.ULt, OpCode.DLt, ctx.Analysis),
                          ref ctx, lambdaState);
                return;

            case LessThanOrEqual le:
                EmitBinary(le.LeftHandValue, le.RightHandValue, ResolveComparisonOp(le.LeftHandValue, le.RightHandValue, OpCode.Le, OpCode.ULe, OpCode.DLe, ctx.Analysis),
                          ref ctx, lambdaState);
                return;

            case GreaterThan gt:
                EmitBinary(gt.LeftHandValue, gt.RightHandValue, ResolveComparisonOp(gt.LeftHandValue, gt.RightHandValue, OpCode.Gt, OpCode.UGt, OpCode.DGt, ctx.Analysis),
                          ref ctx, lambdaState);
                return;

            case GreaterThanOrEqual ge:
                EmitBinary(ge.LeftHandValue, ge.RightHandValue, ResolveComparisonOp(ge.LeftHandValue, ge.RightHandValue, OpCode.Ge, OpCode.UGe, OpCode.DGe, ctx.Analysis),
                          ref ctx, lambdaState);
                return;

            case And andNode:
                EmitShortCircuitAnd(andNode, ref ctx, lambdaState);
                return;

            case Or orNode:
                EmitShortCircuitOr(orNode, ref ctx, lambdaState);
                return;

            case Not notNode:
                Emit(notNode.Value, ref ctx, lambdaState);
                ctx.Code.Add((byte)OpCode.Not);
                return;

            case Conditional cond:
                EmitConditional(cond, ref ctx, lambdaState);
                return;

            case IfStatement ifStmt:
                EmitIf(ifStmt, ref ctx, lambdaState);
                return;

            case WhileLoop wl:
                EmitWhileLoop(wl, ref ctx, lambdaState);
                return;

            case DoWhileLoop dwl:
                EmitDoWhileLoop(dwl, ref ctx, lambdaState);
                return;

            case ForLoop fl:
                EmitForLoop(fl, ref ctx, lambdaState);
                return;

            case BreakStatement brk:
                if (ctx.Labels?.LoopLabels.Count > 0) {
                    if (brk.Label is not null) {
                        foreach (var entry in ctx.Labels.LoopLabels)
                            if (entry.Name == brk.Label) { ctx.Labels.JumpTo(entry.Break, ctx.Code, ctx.Relocations); break; }
                    }
                    else {
                        ctx.Labels.JumpTo(ctx.Labels.LoopLabels.Peek().Break, ctx.Code, ctx.Relocations);
                    }
                }
                return;

            case ContinueStatement cont:
                if (ctx.Labels?.LoopLabels.Count > 0) {
                    if (cont.Label is not null) {
                        foreach (var entry in ctx.Labels.LoopLabels)
                            if (entry.Name == cont.Label) { ctx.Labels.JumpTo(entry.Continue, ctx.Code, ctx.Relocations); break; }
                    }
                    else {
                        ctx.Labels.JumpTo(ctx.Labels.LoopLabels.Peek().Continue, ctx.Code, ctx.Relocations);
                    }
                }
                return;

            case GotoStatement got:
                ctx.Labels?.JumpTo(got.Target, ctx.Code, ctx.Relocations);
                return;

            case LabelDeclaration lbl:
                ctx.Labels?.Mark(lbl.Name, ctx.Code);
                if (ctx.Labels is not null) ctx.Labels.PendingLoopLabel = lbl.Name;
                Emit(lbl.Statement, ref ctx, lambdaState);
                if (ctx.Labels is not null) ctx.Labels.PendingLoopLabel = null;
                return;

            case SwitchStatement swt:
                EmitSwitch(swt, ref ctx, lambdaState);
                return;

            case ThrowStatement thr:
                Emit(thr.Exception, ref ctx, lambdaState);
                ctx.Code.Add((byte)OpCode.Throw);
                return;

            case Default def:
                ctx.Code.Add((byte)OpCode.PushInt);
                EmitInt32(ctx.Code, 0);
                return;

            case NullForgiving nf:
                Emit(nf.Operand, ref ctx, lambdaState);
                return;

            case Assignment assign:
                Emit(assign.Value, ref ctx, lambdaState);
                string? destName = assign.Destination switch {
                    Variable v => v.Name,
                    Parameter p => p.Name,
                    _ => null
                };
                if (destName is not null) {
                    if (ctx.ParamIndexMap is not null && ctx.ParamIndexMap.TryGetValue(destName, out int storeIdx)) {
                        ctx.Code.Add((byte)OpCode.Dup);
                        ctx.Code.Add((byte)OpCode.StoreArg);
                        EmitInt32(ctx.Code, storeIdx);
                    }
                    else if (ctx.LocalIndexMap is not null && ctx.LocalIndexMap.TryGetValue(destName, out int localStoreIdx)) {
                        ctx.Code.Add((byte)OpCode.Dup);
                        ctx.Code.Add((byte)OpCode.StoreLocal);
                        EmitInt32(ctx.Code, localStoreIdx);
                    }
                    else if (lambdaState?.UpvalueMap is not null && lambdaState.UpvalueMap.TryGetValue(destName, out int upStoreIdx)) {
                        ctx.Code.Add((byte)OpCode.Dup);
                        ctx.Code.Add((byte)OpCode.StoreUpvalue);
                        EmitInt32(ctx.Code, upStoreIdx);
                    }
                }
                else if (assign.Destination is Member memberDest) {
                    EmitAssignmentMember(memberDest, assign.Value, ref ctx, lambdaState);
                }
                else if (assign.Destination is IndexAccess idxDest) {
                    EmitAssignmentIndexAccess(idxDest, assign.Value, ref ctx, lambdaState);
                }
                return;

            case Coalesce coalesce:
                EmitCoalesce(coalesce, ref ctx, lambdaState);
                return;

            case Member member:
                EmitMember(member, ref ctx);
                return;

            case IndexAccess idxAccess:
                EmitIndexAccess(idxAccess, ref ctx);
                return;

            case New newExpr:
                EmitNew(newExpr, ref ctx);
                return;

            case TypeCast tc:
                Emit(tc.Operand, ref ctx, lambdaState);
                return;

            case TypeIs ti:
                Emit(ti.Operand, ref ctx, lambdaState); {
                    Type? targetType = ctx.Analysis?.GetResolvedType(ti.TargetTypeReference)?.GetRuntimeType();
                    int typeCheckIdx = ctx.CallSites?.Count ?? 0;
                    ctx.CallSites?.Add(targetType is not null ? CreateTypeIsDelegate(targetType) : IsNotNullDelegate);
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
                    ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, typeCheckIdx);
                }
                return;

            case TypeAs:
            case ParameterReference:
            case ThisReference:
                return;

            case Parameter param:
                if (param.Name is not null) {
                    if (ctx.ParamIndexMap is not null && ctx.ParamIndexMap.TryGetValue(param.Name, out int pIdx)) {
                        ctx.Code.Add((byte)OpCode.LoadArg);
                        EmitInt32(ctx.Code, pIdx);
                        return;
                    }
                    if (ctx.LocalIndexMap is not null && ctx.LocalIndexMap.TryGetValue(param.Name, out int lIdx)) {
                        ctx.Code.Add((byte)OpCode.LoadLocal);
                        EmitInt32(ctx.Code, lIdx);
                        return;
                    }
                    if (param.DefaultValue is not null) {
                        Emit(param.DefaultValue, ref ctx, lambdaState);
                        return;
                    }
                }
                return;

            case Invoke invoke:
                EmitInvoke(invoke, ref ctx, lambdaState);
                return;

            case Block block:
                for (int i = 0; i < block.Nodes.Count; i++) {
                    Emit(block.Nodes[i], ref ctx, lambdaState);
                    if (i < block.Nodes.Count - 1 && EmitsValue(block.Nodes[i]))
                        ctx.Code.Add((byte)OpCode.Pop);
                }
                return;

            case Return retNode:
                if (retNode.Value is not null)
                    Emit(retNode.Value, ref ctx, lambdaState);
                return;

            case Variable variable:
                if (variable.Name is null) return;
                if (ctx.ParamIndexMap is not null && ctx.ParamIndexMap.TryGetValue(variable.Name, out int paramIdx)) {
                    ctx.Code.Add((byte)OpCode.LoadArg);
                    EmitInt32(ctx.Code, paramIdx);
                }
                else if (ctx.LocalIndexMap is not null && ctx.LocalIndexMap.TryGetValue(variable.Name, out int localIdx)) {
                    ctx.Code.Add((byte)OpCode.LoadLocal);
                    EmitInt32(ctx.Code, localIdx);
                }
                else if (lambdaState?.UpvalueMap is not null && lambdaState.UpvalueMap.TryGetValue(variable.Name, out int upIdx)) {
                    ctx.Code.Add((byte)OpCode.LoadUpvalue);
                    EmitInt32(ctx.Code, upIdx);
                }
                return;

            case SuspendNode sn:
                Emit(sn.Inner, ref ctx, lambdaState);
                ctx.Code.Add((byte)OpCode.Pop);
                ctx.Code.Add((byte)OpCode.Int);
                EmitInt32(ctx.Code, 0);
                return;

            case Lambda lambda:
                if (lambdaState?.FuncMap is not null && lambdaState.FuncMap.TryGetValue(lambda, out int lambdaFuncIdx)) {
                    var captures = lambdaState?.CaptureMap is not null && lambdaState.CaptureMap.TryGetValue(lambda, out var caps)
                        ? caps : [];
                    for (int i = 0; i < captures.Count; i++) {
                        if (ctx.ParamIndexMap is not null && ctx.ParamIndexMap.TryGetValue(captures[i], out int pIdx)) {
                            ctx.Code.Add((byte)OpCode.LoadArg);
                            EmitInt32(ctx.Code, pIdx);
                        }
                        else if (ctx.LocalIndexMap is not null && ctx.LocalIndexMap.TryGetValue(captures[i], out int localIdx)) {
                            ctx.Code.Add((byte)OpCode.LoadLocal);
                            EmitInt32(ctx.Code, localIdx);
                        }
                        else {
                            ctx.Code.Add((byte)OpCode.PushInt);
                            EmitInt32(ctx.Code, 0);
                        }
                    }
                    ctx.Code.Add((byte)OpCode.AllocateClosure);
                    EmitInt32(ctx.Code, lambdaFuncIdx);
                    EmitInt32(ctx.Code, captures.Count);
                }
                return;

            case Await awaitNode:
                Emit(awaitNode.Operand, ref ctx, lambdaState);
                int awaitSiteIdx = ctx.CallSites?.Count ?? 0;
                ctx.CallSites?.Add(AwaitResultDelegate);
                ctx.Code.Add((byte)OpCode.PushInt);
                EmitInt32(ctx.Code, 1);
                ctx.Code.Add((byte)OpCode.PushInt);
                EmitInt32(ctx.Code, 1);
                ctx.Code.Add((byte)OpCode.CallExternal);
                EmitInt32(ctx.Code, awaitSiteIdx);
                return;

            case TypeDefinitionNode:
                return;

            case ForEachLoop fe:
                EmitForEachLoop(fe, ref ctx);
                return;

            case UsingStatement us:
                EmitUsingStatement(us, ref ctx);
                return;

            case TryCatchFinally tcf: {
                    int tryStart = ctx.Code.Count;
                    Emit(tcf.TryBlock, ref ctx, lambdaState);
                    int tryEnd = ctx.Code.Count;

                    var labels = ctx.Labels!;
                    string endLabel = labels.Next();
                    string? finallyEntry = tcf.FinallyBlock is not null ? labels.Next() : null;

                    // Normal path: run finally (if any), then end
                    if (finallyEntry is not null)
                        labels.JumpTo(finallyEntry, ctx.Code, ctx.Relocations);
                    else
                        labels.JumpTo(endLabel, ctx.Code, ctx.Relocations);

                    int? finallyStart = null;
                    int catchStart = -1;

                    if (tcf.CatchClauses is not null && tcf.CatchClauses.Count > 0) {
                        catchStart = ctx.Code.Count;
                        foreach (var cc in tcf.CatchClauses) {
                            if (cc.VariableName is not null
                                && ctx.ParamIndexMap is not null
                                && ctx.ParamIndexMap.TryGetValue(cc.VariableName, out int varIdx)) {
                                ctx.Code.Add((byte)OpCode.StoreArg);
                                EmitInt32(ctx.Code, varIdx);
                            }
                            else {
                                ctx.Code.Add((byte)OpCode.Pop);
                            }
                            Emit(cc.Body, ref ctx, lambdaState);
                            if (finallyEntry is not null) {
                                // fall through to finally
                            }
                            else {
                                labels.JumpTo(endLabel, ctx.Code, ctx.Relocations);
                            }
                        }
                    }

                    if (tcf.FinallyBlock is not null) {
                        if (finallyEntry is not null)
                            labels.Mark(finallyEntry, ctx.Code);
                        finallyStart = ctx.Code.Count;
                        Emit(tcf.FinallyBlock, ref ctx, lambdaState);
                        ctx.Code.Add((byte)OpCode.EndFinally);
                        if (EmitsValue(tcf.FinallyBlock))
                            ctx.Code.Add((byte)OpCode.Pop);
                    }

                    labels.Mark(endLabel, ctx.Code);
                    labels.ExceptionRegions.Add(new ExceptionRegion(tryStart, tryEnd, catchStart, finallyStart));
                    return;
                }

            default:
                return;
        }
    }

    private static OpCode ResolveBinaryOp(Node node, OpCode signedOp, OpCode unsignedOp, OpCode doubleOp, AnalysisResult? analysis) {
        if (analysis is null) return signedOp;
        var typeDef = analysis.GetResolvedType(node);
        if (typeDef is null) return signedOp;
        var clrType = typeDef.GetRuntimeType();
        if (clrType == typeof(double) || clrType == typeof(float))
            return doubleOp;
        if (clrType == typeof(uint) || clrType == typeof(ulong))
            return unsignedOp;
        return signedOp;
    }

    private static OpCode ResolveComparisonOp(Node left, Node right, OpCode signedOp, OpCode unsignedOp, OpCode doubleOp, AnalysisResult? analysis) {
        if (analysis is null) return signedOp;
        var leftType = analysis.GetResolvedType(left);
        if (leftType is null) return signedOp;
        var clrType = leftType.GetRuntimeType();
        if (clrType == typeof(double) || clrType == typeof(float))
            return doubleOp;
        if (clrType == typeof(uint) || clrType == typeof(ulong))
            return unsignedOp;
        return signedOp;
    }

    private static void EmitBinary(
        Node left, Node right, OpCode op,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        Emit(left, ref ctx, lambdaState);
        Emit(right, ref ctx, lambdaState);
        ctx.Code.Add((byte)op);
    }

    private static void EmitShortCircuitAnd(
        And andNode,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        ctx.Labels ??= new LabelContext();
        var labels = ctx.Labels;
        string end = labels.Next();
        Emit(andNode.LeftHandValue, ref ctx, lambdaState);
        ctx.Code.Add((byte)OpCode.Dup);
        labels.JumpIfFalseTo(end, ctx.Code, ctx.Relocations);
        ctx.Code.Add((byte)OpCode.Pop);
        Emit(andNode.RightHandValue, ref ctx, lambdaState);
        labels.Mark(end, ctx.Code);
    }

    private static void EmitShortCircuitOr(
        Or orNode,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        ctx.Labels ??= new LabelContext();
        var labels = ctx.Labels;
        string evalRight = labels.Next();
        string after = labels.Next();
        Emit(orNode.LeftHandValue, ref ctx, lambdaState);
        ctx.Code.Add((byte)OpCode.Dup);
        labels.JumpIfFalseTo(evalRight, ctx.Code, ctx.Relocations);
        labels.JumpTo(after, ctx.Code, ctx.Relocations);
        labels.Mark(evalRight, ctx.Code);
        ctx.Code.Add((byte)OpCode.Pop);
        Emit(orNode.RightHandValue, ref ctx, lambdaState);
        labels.Mark(after, ctx.Code);
    }

    private static void EmitConditional(
        Conditional cond,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        ctx.Labels ??= new LabelContext();
        var labels = ctx.Labels;
        string elseL = labels.Next();
        string endL = labels.Next();
        Emit(cond.Condition, ref ctx, lambdaState);
        labels.JumpIfFalseTo(elseL, ctx.Code, ctx.Relocations);
        Emit(cond.IfTrue, ref ctx, lambdaState);
        labels.JumpTo(endL, ctx.Code, ctx.Relocations);
        labels.Mark(elseL, ctx.Code);
        Emit(cond.IfFalse, ref ctx, lambdaState);
        labels.Mark(endL, ctx.Code);
    }

    private static void EmitIf(
        IfStatement ifStmt,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        ctx.Labels ??= new LabelContext();
        var labels = ctx.Labels;
        string elseL = labels.Next();
        string endL = labels.Next();
        Emit(ifStmt.Condition, ref ctx, lambdaState);
        labels.JumpIfFalseTo(elseL, ctx.Code, ctx.Relocations);
        Emit(ifStmt.ThenBranch, ref ctx, lambdaState);
        labels.JumpTo(endL, ctx.Code, ctx.Relocations);
        labels.Mark(elseL, ctx.Code);
        if (ifStmt.ElseBranch is not null)
            Emit(ifStmt.ElseBranch, ref ctx, lambdaState);
        labels.Mark(endL, ctx.Code);
    }

    private static void EmitWhileLoop(
        WhileLoop wl,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        ctx.Labels ??= new LabelContext();
        var labels = ctx.Labels;
        string breakL = labels.Next();
        string contL = labels.Next();
        string loopLabel = labels.PendingLoopLabel ?? "";
        labels.PendingLoopLabel = null;
        labels.LoopLabels.Push((loopLabel, breakL, contL));
        labels.Mark(contL, ctx.Code);
        Emit(wl.Condition, ref ctx, lambdaState);
        labels.JumpIfFalseTo(breakL, ctx.Code, ctx.Relocations);
        Emit(wl.Body, ref ctx, lambdaState);
        labels.JumpTo(contL, ctx.Code, ctx.Relocations);
        labels.Mark(breakL, ctx.Code);
        labels.LoopLabels.Pop();
    }

    private static void EmitDoWhileLoop(
        DoWhileLoop dwl,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        ctx.Labels ??= new LabelContext();
        var labels = ctx.Labels;
        string breakL = labels.Next();
        string contL = labels.Next();
        string loopLabel = labels.PendingLoopLabel ?? "";
        labels.PendingLoopLabel = null;
        labels.LoopLabels.Push((loopLabel, breakL, contL));
        labels.Mark(contL, ctx.Code);
        Emit(dwl.Body, ref ctx, lambdaState);
        Emit(dwl.Condition, ref ctx, lambdaState);
        labels.JumpIfFalseTo(breakL, ctx.Code, ctx.Relocations);
        labels.JumpTo(contL, ctx.Code, ctx.Relocations);
        labels.Mark(breakL, ctx.Code);
        labels.LoopLabels.Pop();
    }

    private static void EmitForLoop(
        ForLoop fl,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        ctx.Labels ??= new LabelContext();
        var labels = ctx.Labels;
        string breakL = labels.Next();
        string contL = labels.Next();
        string loopLabel = labels.PendingLoopLabel ?? "";
        labels.PendingLoopLabel = null;
        labels.LoopLabels.Push((loopLabel, breakL, contL));

        if (fl.Initializer is not null)
            Emit(fl.Initializer, ref ctx, lambdaState);

        labels.Mark(contL, ctx.Code);

        if (fl.Condition is not null) {
            Emit(fl.Condition, ref ctx, lambdaState);
            labels.JumpIfFalseTo(breakL, ctx.Code, ctx.Relocations);
        }

        Emit(fl.Body, ref ctx, lambdaState);

        if (fl.Increment is not null)
            Emit(fl.Increment, ref ctx, lambdaState);

        labels.JumpTo(contL, ctx.Code, ctx.Relocations);
        labels.Mark(breakL, ctx.Code);
        labels.LoopLabels.Pop();
    }

    private static void EmitForEachLoop(
        ForEachLoop fe,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        var enumHolder = new object[1];
        int holderIdx = ctx.Constants?.Count ?? 0;
        ctx.Constants?.Add(enumHolder);

        ctx.Labels ??= new LabelContext();
        var labels = ctx.Labels;
        string breakL = labels.Next();
        string contL = labels.Next();
        string loopLabel = labels.PendingLoopLabel ?? "";
        labels.PendingLoopLabel = null;
        labels.LoopLabels.Push((loopLabel, breakL, contL));

        ctx.Code.Add((byte)OpCode.LoadConst);
        EmitInt32(ctx.Code, holderIdx);
        Emit(fe.Collection, ref ctx, lambdaState);
        int initEnumIdx = ctx.CallSites?.Count ?? 0;
        ctx.CallSites?.Add(InitEnumeratorDelegate);
        ctx.Code.Add((byte)OpCode.PushInt);
        EmitInt32(ctx.Code, 2);
        ctx.Code.Add((byte)OpCode.PushInt);
        EmitInt32(ctx.Code, 0);
        ctx.Code.Add((byte)OpCode.CallExternal);
        EmitInt32(ctx.Code, initEnumIdx);

        labels.Mark(contL, ctx.Code);

        ctx.Code.Add((byte)OpCode.LoadConst);
        EmitInt32(ctx.Code, holderIdx);
        ctx.Code.Add((byte)OpCode.EnumeratorMoveNext);
        labels.JumpIfFalseTo(breakL, ctx.Code, ctx.Relocations);

        ctx.Code.Add((byte)OpCode.LoadConst);
        EmitInt32(ctx.Code, holderIdx);
        int getCurrIdx = ctx.CallSites?.Count ?? 0;
        ctx.CallSites?.Add(GetCurrentDelegate);
        ctx.Code.Add((byte)OpCode.PushInt);
        EmitInt32(ctx.Code, 1);
        ctx.Code.Add((byte)OpCode.PushInt);
        EmitInt32(ctx.Code, 1);
        ctx.Code.Add((byte)OpCode.CallExternal);
        EmitInt32(ctx.Code, getCurrIdx);

        if (fe.LoopVariable is { Name: not null } lv) {
            if (ctx.ParamIndexMap is not null && ctx.ParamIndexMap.TryGetValue(lv.Name, out int paramIdx)) {
                ctx.Code.Add((byte)OpCode.StoreArg);
                EmitInt32(ctx.Code, paramIdx);
            }
            else if (ctx.LocalIndexMap is not null && ctx.LocalIndexMap.TryGetValue(lv.Name, out int localIdx)) {
                ctx.Code.Add((byte)OpCode.StoreLocal);
                EmitInt32(ctx.Code, localIdx);
            }
            else {
                ctx.Code.Add((byte)OpCode.Pop);
            }
        }
        else {
            ctx.Code.Add((byte)OpCode.Pop);
        }

        Emit(fe.Body, ref ctx, lambdaState);
        if (EmitsValue(fe.Body))
            ctx.Code.Add((byte)OpCode.Pop);

        labels.JumpTo(contL, ctx.Code, ctx.Relocations);

        labels.Mark(breakL, ctx.Code);

        ctx.Code.Add((byte)OpCode.LoadConst);
        EmitInt32(ctx.Code, holderIdx);
        int disposeEnumIdx = ctx.CallSites?.Count ?? 0;
        ctx.CallSites?.Add(DisposeEnumeratorDelegate);
        ctx.Code.Add((byte)OpCode.PushInt);
        EmitInt32(ctx.Code, 1);
        ctx.Code.Add((byte)OpCode.PushInt);
        EmitInt32(ctx.Code, 0);
        ctx.Code.Add((byte)OpCode.CallExternal);
        EmitInt32(ctx.Code, disposeEnumIdx);

        labels.LoopLabels.Pop();
    }

    private static void EmitUsingStatement(
        UsingStatement us,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        var resourceHolder = new object[1];
        int holderIdx = ctx.Constants?.Count ?? 0;
        ctx.Constants?.Add(resourceHolder);

        ctx.Labels ??= new LabelContext();
        var labels = ctx.Labels;
        int tryStart = ctx.Code.Count;

        ctx.Code.Add((byte)OpCode.LoadConst);
        EmitInt32(ctx.Code, holderIdx);
        Emit(us.Resource, ref ctx, lambdaState);
        int saveIdx = ctx.CallSites?.Count ?? 0;
        ctx.CallSites?.Add(SaveResourceDelegate);
        ctx.Code.Add((byte)OpCode.PushInt);
        EmitInt32(ctx.Code, 2);
        ctx.Code.Add((byte)OpCode.PushInt);
        EmitInt32(ctx.Code, 0);
        ctx.Code.Add((byte)OpCode.CallExternal);
        EmitInt32(ctx.Code, saveIdx);

        Emit(us.Body, ref ctx, lambdaState);
        if (EmitsValue(us.Body))
            ctx.Code.Add((byte)OpCode.Pop);

        int tryEnd = ctx.Code.Count;

        int finallyStart = ctx.Code.Count;
        ctx.Code.Add((byte)OpCode.LoadConst);
        EmitInt32(ctx.Code, holderIdx);
        int disposeIdx = ctx.CallSites?.Count ?? 0;
        ctx.CallSites?.Add(DisposeResourceDelegate);
        ctx.Code.Add((byte)OpCode.PushInt);
        EmitInt32(ctx.Code, 1);
        ctx.Code.Add((byte)OpCode.PushInt);
        EmitInt32(ctx.Code, 0);
        ctx.Code.Add((byte)OpCode.CallExternal);
        EmitInt32(ctx.Code, disposeIdx);

        ctx.Code.Add((byte)OpCode.EndFinally);

        labels.ExceptionRegions.Add(new ExceptionRegion(tryStart, tryEnd, -1, finallyStart));
    }

    private static void EmitSwitch(
        SwitchStatement swt,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        ctx.Labels ??= new LabelContext();
        var lb = ctx.Labels;
        string endL = lb.Next();
        var caseLabels = swt.Cases.Select(_ => lb.Next()).ToList();

        Emit(swt.Value, ref ctx, lambdaState);

        for (int i = 0; i < swt.Cases.Count; i++) {
            ctx.Code.Add((byte)OpCode.Dup);
            Emit(swt.Cases[i].Pattern, ref ctx, lambdaState);
            ctx.Code.Add((byte)OpCode.Eq);
            lb.JumpIfFalseTo(caseLabels[i], ctx.Code, ctx.Relocations);
            ctx.Code.Add((byte)OpCode.Pop);
            Emit(swt.Cases[i].Body, ref ctx, lambdaState);
            lb.JumpTo(endL, ctx.Code, ctx.Relocations);
            lb.Mark(caseLabels[i], ctx.Code);
        }

        ctx.Code.Add((byte)OpCode.Pop);
        if (swt.DefaultCase is not null)
            Emit(swt.DefaultCase, ref ctx, lambdaState);
        lb.Mark(endL, ctx.Code);
    }

    private static void EmitCoalesce(
        Coalesce coalesce,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        ctx.Labels ??= new LabelContext();
        var labels = ctx.Labels;
        string after = labels.Next();
        Emit(coalesce.LeftHandValue, ref ctx, lambdaState);
        ctx.Code.Add((byte)OpCode.Dup);
        ctx.Code.Add((byte)OpCode.IsNull);
        labels.JumpIfFalseTo(after, ctx.Code, ctx.Relocations);
        ctx.Code.Add((byte)OpCode.Pop);
        Emit(coalesce.RightHandValue, ref ctx, lambdaState);
        labels.Mark(after, ctx.Code);
    }

    private static void EmitInvoke(
        Invoke invoke,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        if (ctx.Analysis?.GetResolvedMember(invoke) is AstMethodDefinition astMethod) {
            var methodDef = astMethod.DefinitionNode;
            if (ctx.FunctionIndexMap is not null && ctx.FunctionIndexMap.TryGetValue(methodDef, out int funcIndex)) {
                foreach (var arg in invoke.Arguments) {
                    Emit(arg, ref ctx, lambdaState);
                }
                int paramCount = methodDef.Parameters?.Count ?? 0;
                ctx.Code.Add((byte)OpCode.PushInt);
                EmitInt32(ctx.Code, paramCount);
                ctx.Code.Add((byte)OpCode.Call);
                EmitInt32(ctx.Code, funcIndex);
                ctx.SourceMap[ctx.Code.Count - 5] = invoke.Id;
                return;
            }
        }

        if (ctx.Analysis?.GetResolvedMember(invoke) is ClrMethod clrMethod) {
            var methodInfo = clrMethod.MethodInfo;
            bool isStatic = clrMethod.LifetimeModifier == LifetimeModifier.Static;

            int siteIndex = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.Compile(methodInfo, isStatic));

            if (!isStatic && invoke.Delegate is Member memberAccess) {
                Emit(memberAccess.Value, ref ctx, lambdaState);
            }

            foreach (var arg in invoke.Arguments) {
                Emit(arg, ref ctx, lambdaState);
            }

            int argCount = invoke.Arguments.Length + (isStatic ? 0 : 1);
            ctx.Code.Add((byte)OpCode.PushInt);
            EmitInt32(ctx.Code, argCount);
            ctx.Code.Add((byte)OpCode.PushInt);
            EmitInt32(ctx.Code, methodInfo.ReturnType != typeof(void) ? 1 : 0);
            ctx.Code.Add((byte)OpCode.CallExternal);
            EmitInt32(ctx.Code, siteIndex);
            return;
        }

        // Lambda path: direct call when Delegate is a known Lambda in FuncMap
        Lambda? lambdaTarget = invoke.Delegate as Lambda;
        int lFuncIdx = -1;
        if (lambdaTarget is not null && lambdaState?.FuncMap is not null) {
            if (!lambdaState.FuncMap.TryGetValue(lambdaTarget, out lFuncIdx)) {
                foreach (var kvp in lambdaState.FuncMap) {
                    if (ReferenceEquals(kvp.Key, lambdaTarget)) { lFuncIdx = kvp.Value; break; }
                }
            }
        }

        if (lambdaTarget is not null && lFuncIdx >= 0) {
            Emit(lambdaTarget, ref ctx, lambdaState);
            foreach (var arg in invoke.Arguments) {
                Emit(arg, ref ctx, lambdaState);
            }
            int totalArgs = (lambdaTarget.Parameters?.Count ?? 0) + 1;
            ctx.Code.Add((byte)OpCode.PushInt);
            EmitInt32(ctx.Code, totalArgs);
            ctx.Code.Add((byte)OpCode.Call);
            EmitInt32(ctx.Code, lFuncIdx);
            ctx.SourceMap[ctx.Code.Count - 5] = invoke.Id;
            return;
        }

        // Generic delegate path
        if (invoke.Delegate is not null) {
            Emit(invoke.Delegate, ref ctx, lambdaState);
            foreach (var arg in invoke.Arguments) {
                Emit(arg, ref ctx, lambdaState);
            }
            int totalArgs = invoke.Arguments.Length + 1;
            ctx.Code.Add((byte)OpCode.PushInt);
            EmitInt32(ctx.Code, totalArgs);
            ctx.Code.Add((byte)OpCode.CallClosure);
            ctx.SourceMap[ctx.Code.Count - 5] = invoke.Id;
        }
    }

    private static void EmitMember(
        Member member,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        var resolved = ctx.Analysis?.GetResolvedMember(member);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var getter = pi.GetGetMethod(nonPublic: true);
            if (getter is null) return;
            if (!isStatic)
                Emit(member.Value, ref ctx, lambdaState);
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.Compile(getter, isStatic));
            int argCount = isStatic ? 0 : 1;
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, argCount);
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
            ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, siteIdx);
            return;
        }
        if (resolved is ClrTypeField { FieldInfo: var fi, LifetimeModifier: var lm2 }) {
            bool isStatic = lm2 == LifetimeModifier.Static;
            if (!isStatic)
                Emit(member.Value, ref ctx, lambdaState);
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.CompileFieldGetter(fi, isStatic));
            int argCount = isStatic ? 0 : 1;
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, argCount);
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
            ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, siteIdx);
            return;
        }
    }

    private static void EmitIndexAccess(
        IndexAccess idx,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        var resolved = ctx.Analysis?.GetResolvedMember(idx);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var getter = pi.GetGetMethod(nonPublic: true);
            if (getter is null) return;
            if (!isStatic)
                Emit(idx.Value, ref ctx, lambdaState);
            foreach (var arg in idx.Arguments)
                Emit(arg, ref ctx, lambdaState);
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.Compile(getter, isStatic));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length;
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, argCount);
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
            ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, siteIdx);
            return;
        }
        if (resolved is ClrTypeSyntheticProperty { Read: not null, LifetimeModifier: var lm2 }) {
            var synReader = ((ClrTypeSyntheticProperty)resolved).Read!;
            bool isStatic = lm2 == LifetimeModifier.Static;
            if (!isStatic)
                Emit(idx.Value, ref ctx, lambdaState);
            foreach (var arg in idx.Arguments)
                Emit(arg, ref ctx, lambdaState);
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CompileSyntheticGetter(synReader, isStatic, idx.Arguments.Length));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length;
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, argCount);
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
            ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, siteIdx);
            return;
        }
    }

    private static void EmitAssignmentMember(
        Member member, Node value,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        var resolved = ctx.Analysis?.GetResolvedMember(member);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var setter = pi.GetSetMethod(nonPublic: true);
            if (setter is null) return;
            if (!isStatic)
                Emit(member.Value, ref ctx, lambdaState);
            Emit(value, ref ctx, lambdaState);
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.Compile(setter, isStatic));
            int argCount = (isStatic ? 0 : 1) + 1;
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, argCount);
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 0);
            ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, siteIdx);
            return;
        }
        if (resolved is ClrTypeField { FieldInfo: var fi, LifetimeModifier: var lm2 }) {
            bool isStatic = lm2 == LifetimeModifier.Static;
            if (!isStatic)
                Emit(member.Value, ref ctx, lambdaState);
            Emit(value, ref ctx, lambdaState);
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.CompileFieldSetter(fi, isStatic));
            int argCount = (isStatic ? 0 : 1) + 1;
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, argCount);
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 0);
            ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, siteIdx);
            return;
        }
    }

    private static void EmitAssignmentIndexAccess(
        IndexAccess idx, Node value,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        var resolved = ctx.Analysis?.GetResolvedMember(idx);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var setter = pi.GetSetMethod(nonPublic: true);
            if (setter is null) return;
            if (!isStatic)
                Emit(idx.Value, ref ctx, lambdaState);
            foreach (var arg in idx.Arguments)
                Emit(arg, ref ctx, lambdaState);
            Emit(value, ref ctx, lambdaState);
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.Compile(setter, isStatic));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length + 1;
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, argCount);
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 0);
            ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, siteIdx);
            return;
        }
        if (resolved is ClrTypeSyntheticProperty { Write: not null, LifetimeModifier: var lm2 }) {
            var synWriter = ((ClrTypeSyntheticProperty)resolved).Write!;
            bool isStatic = lm2 == LifetimeModifier.Static;
            if (!isStatic)
                Emit(idx.Value, ref ctx, lambdaState);
            foreach (var arg in idx.Arguments)
                Emit(arg, ref ctx, lambdaState);
            Emit(value, ref ctx, lambdaState);
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CompileSyntheticSetter(synWriter, isStatic, idx.Arguments.Length + 1));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length + 1;
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, argCount);
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 0);
            ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, siteIdx);
            return;
        }
    }

    private static void EmitNew(
        New newExpr,
        ref EmitContext ctx, LambdaEmitState? lambdaState = null) {

        var resolved = ctx.Analysis?.GetResolvedMember(newExpr);
        if (resolved is ClrConstructor { ConstructorInfo: var ci }) {
            foreach (var arg in newExpr.Arguments)
                Emit(arg, ref ctx, lambdaState);
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.CompileConstructor(ci));
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, newExpr.Arguments.Length);
            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
            ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, siteIdx);
            return;
        }
    }

    private static CallSiteDelegate CompileSyntheticGetter(MemberReadDelegate reader, bool isStatic, int argCount) {
        return state => {
            var (argSlots, hasRet) = state.Stack.Pop<(int argSlots, int hasRet)>();
            int baseOff = state.Stack.SP - argSlots;
            object? owner = null;
            int off = 0;
            if (!isStatic) {
                int handle = state.Stack.AsSpan()[baseOff + off];
                owner = handle >= 0 && handle < state.Heap.Count ? state.Heap.Get(handle) : (object?)handle;
                off++;
            }
            var args = new object?[argCount];
            for (int i = 0; i < argCount; i++) {
                int handle = state.Stack.AsSpan()[baseOff + off + i];
                args[i] = handle >= 0 && handle < state.Heap.Count ? state.Heap.Get(handle) : (object?)handle;
            }
            var result = reader(owner!, args);
            if (argSlots > 0) state.Stack.Drop(argSlots);
            if (hasRet != 0) {
                if (result is int iv) state.Stack.Push(iv);
                else state.Stack.Push(state.Heap.Allocate(result));
            }
        };
    }

    private static CallSiteDelegate CompileSyntheticSetter(MemberWriteDelegate writer, bool isStatic, int argCount) {
        return state => {
            var (argSlots, hasRet) = state.Stack.Pop<(int argSlots, int hasRet)>();
            int baseOff = state.Stack.SP - argSlots;
            object? owner = null;
            int off = 0;
            if (!isStatic) {
                int handle = state.Stack.AsSpan()[baseOff + off];
                owner = handle >= 0 && handle < state.Heap.Count ? state.Heap.Get(handle) : (object?)handle;
                off++;
            }
            int indexArgCount = argCount - 1;
            var args = new object?[indexArgCount];
            for (int i = 0; i < indexArgCount; i++) {
                int handle = state.Stack.AsSpan()[baseOff + off + i];
                args[i] = handle >= 0 && handle < state.Heap.Count ? state.Heap.Get(handle) : (object?)handle;
            }
            int valueHandle = state.Stack.AsSpan()[baseOff + off + indexArgCount];
            object? value = valueHandle >= 0 && valueHandle < state.Heap.Count ? state.Heap.Get(valueHandle) : (object?)valueHandle;
            writer(owner!, value, args);
            if (argSlots > 0) state.Stack.Drop(argSlots);
            if (hasRet != 0) {
                if (value is int iv) state.Stack.Push(iv);
                else state.Stack.Push(state.Heap.Allocate(value));
            }
        };
    }

    private static int EmitJump(List<byte> code, int targetPc, List<(int CodeOffset, int TargetPc)>? relocations) {
        int pc = code.Count;
        code.Add((byte)OpCode.Jump);
        EmitInt32(code, targetPc);
        return pc;
    }

    private static void PatchJump(List<byte> code, int jumpCodeOffset, int targetPc) {
        int valOffset = jumpCodeOffset + 1;
        code[valOffset] = (byte)(targetPc & 0xFF);
        code[valOffset + 1] = (byte)((targetPc >> 8) & 0xFF);
        code[valOffset + 2] = (byte)((targetPc >> 16) & 0xFF);
        code[valOffset + 3] = (byte)((targetPc >> 24) & 0xFF);
    }

    private static void EmitInt32(List<byte> code, int value) {
        code.Add((byte)(value & 0xFF));
        code.Add((byte)((value >> 8) & 0xFF));
        code.Add((byte)((value >> 16) & 0xFF));
        code.Add((byte)((value >> 24) & 0xFF));
    }

    private static void EmitInt64(List<byte> code, long value) {
        code.Add((byte)(value & 0xFF));
        code.Add((byte)((value >> 8) & 0xFF));
        code.Add((byte)((value >> 16) & 0xFF));
        code.Add((byte)((value >> 24) & 0xFF));
        code.Add((byte)((value >> 32) & 0xFF));
        code.Add((byte)((value >> 40) & 0xFF));
        code.Add((byte)((value >> 48) & 0xFF));
        code.Add((byte)((value >> 56) & 0xFF));
    }

    private static bool TryInlinableInt(object? value, out int result) {
        result = 0;
        if (value is int i) { result = i; return true; }
        if (value is short s) { result = s; return true; }
        if (value is ushort us) { result = us; return true; }
        if (value is byte b) { result = b; return true; }
        if (value is sbyte sb) { result = sb; return true; }
        if (value is uint ui) { result = (int)ui; return true; }
        if (value is bool bv) { result = bv ? 1 : 0; return true; }
        return false;
    }

    private static bool TryInlinableLong(object? value, out long result) {
        result = 0;
        if (value is long l) { result = l; return true; }
        return false;
    }

    private static bool TryInlinableDouble(object? value, out double result) {
        result = 0;
        if (value is double d) { result = d; return true; }
        if (value is float f) { result = f; return true; }
        return false;
    }

    private static void EmitDouble(List<byte> code, double value) {
        long raw = BitConverter.DoubleToInt64Bits(value);
        EmitInt64(code, raw);
    }
}