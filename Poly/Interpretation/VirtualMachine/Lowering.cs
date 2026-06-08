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
        int hasRet = state.Stack.PopInt();
        int argSlots = state.Stack.PopInt();
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

    private static readonly CallSiteDelegate AwaitResultDelegate = state => {
        int hasRet = state.Stack.PopInt();
        int argSlots = state.Stack.PopInt();
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
        int hasRet = state.Stack.PopInt();
        int argSlots = state.Stack.PopInt();
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

    private static readonly CallSiteDelegate MoveNextDelegate = state => {
        int hasRet = state.Stack.PopInt();
        int argSlots = state.Stack.PopInt();
        int baseOff = state.Stack.SP - argSlots;
        int holderHandle = state.Stack.AsSpan()[baseOff];
        var enumerator = holderHandle >= 0 && holderHandle < state.Heap.Count && state.Heap.Get(holderHandle) is object[] h1 ? h1[0] as IEnumerator : null;
        bool moved = enumerator?.MoveNext() ?? false;
        if (argSlots > 0) state.Stack.Drop(argSlots);
        if (hasRet != 0) state.Stack.Push(moved ? 1 : 0);
    };

    private static readonly CallSiteDelegate StringConcatDelegate = state => {
        int hasRet = state.Stack.PopInt();
        int argSlots = state.Stack.PopInt();
        int baseOff = state.Stack.SP - argSlots;
        int leftHandle = state.Stack.AsSpan()[baseOff];
        int rightHandle = state.Stack.AsSpan()[baseOff + 1];
        object? left = leftHandle >= 0 && leftHandle < state.Heap.Count ? state.Heap.Get(leftHandle) : (object?)leftHandle;
        object? right = rightHandle >= 0 && rightHandle < state.Heap.Count ? state.Heap.Get(rightHandle) : (object?)rightHandle;
        var result = string.Concat(left, right);
        if (argSlots > 0) state.Stack.Drop(argSlots);
        if (hasRet != 0) state.Stack.Push(state.Heap.Allocate(result));
    };

    private static readonly CallSiteDelegate GetCurrentDelegate = state => {
        int hasRet = state.Stack.PopInt();
        int argSlots = state.Stack.PopInt();
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
        int hasRet = state.Stack.PopInt();
        int argSlots = state.Stack.PopInt();
        int baseOff = state.Stack.SP - argSlots;
        int holderHandle = state.Stack.AsSpan()[baseOff];
        if (holderHandle >= 0 && holderHandle < state.Heap.Count && state.Heap.Get(holderHandle) is object[] h3 && h3[0] is IDisposable d)
            d.Dispose();
        if (argSlots > 0) state.Stack.Drop(argSlots);
    };

    private static readonly CallSiteDelegate SaveResourceDelegate = state => {
        int hasRet = state.Stack.PopInt();
        int argSlots = state.Stack.PopInt();
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
        int hasRet = state.Stack.PopInt();
        int argSlots = state.Stack.PopInt();
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
        }; var code = ctx.Code;
        var sourceMap = ctx.SourceMap;
        var functions = ctx.Functions;
        var functionIndexMap = ctx.FunctionIndexMap;
        var constants = ctx.Constants;
        var callSites = ctx.CallSites;
        var relocations = ctx.Relocations;
        var labels = ctx.Labels;



        var referencedMethods = new List<MethodDefinitionNode>();
        DiscoverFunctions(root, analysis, referencedMethods);

        var referencedLambdas = new List<Lambda>();
        DiscoverLambdas(root, referencedLambdas);

        int jumpOverMainPc = EmitJump(ctx.Code, 0, ctx.Relocations);

        foreach (var method in referencedMethods) {
            int entryPc = ctx.Code.Count;
            ctx.FunctionIndexMap[method] = ctx.Functions.Count;
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
                Emit(method.Body, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            }
            ctx.Code.Add((byte)OpCode.Return);
        }

        // Pre-scan: allocate function indices for all lambdas before emitting any bodies
        foreach (var lambda in referencedLambdas) {
            int funcIdx = ctx.Functions.Count;
            ctx.LambdaFuncMap[lambda] = funcIdx;
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
            ctx.LambdaCaptureMap[lambda] = captures;

            int retBytes = (lambda.Body is not null && EmitsValue(lambda.Body)) ? 1 : 0;
            ctx.Functions[i] = new FunctionEntry(entryPc, paramCount + 1, retBytes, localIndexMap.Count);

            ctx.ParamIndexMap = paramIndexMap;
            ctx.LocalIndexMap = localIndexMap;
            ctx.UpvalueMap = upvalueMap;
            if (lambda.Body is not null) {
                var bodyLambdaState = new LambdaEmitState(ctx.LambdaFuncMap, ctx.LambdaCaptureMap, upvalueMap);
                Emit(lambda.Body, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels, bodyLambdaState);
            }
            ctx.Code.Add((byte)OpCode.Return);
        }

        ctx.ParamIndexMap = null;
        ctx.LocalIndexMap = null;
        ctx.UpvalueMap = null;

        int mainEntry = ctx.Code.Count;
        PatchJump(ctx.Code, jumpOverMainPc, mainEntry);
        var rootLambdaState = new LambdaEmitState(ctx.LambdaFuncMap, ctx.LambdaCaptureMap, null);
        Emit(root, code, sourceMap, analysis, functions, functionIndexMap, null, null, constants, callSites, relocations, labels, rootLambdaState);

        foreach (var (codeOff, targetPc) in ctx.Relocations)
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

    private static void Emit(Node node, List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis,
        List<FunctionEntry>? functions = null, Dictionary<MethodDefinitionNode, int>? functionIndexMap = null,
        IReadOnlyDictionary<string, int>? paramIndexMap = null, IReadOnlyDictionary<string, int>? localIndexMap = null,
        List<object?>? constants = null, List<CallSiteDelegate>? callSites = null,
        List<(int CodeOffset, int TargetPc)>? relocations = null, LabelContext? labels = null, LambdaEmitState? lambdaState = null) {

        if (node is null) return;

        var replacement = analysis?.GetNodeReplacement(node);
        if (replacement is not null && !ReferenceEquals(replacement, node)) {
            Emit(replacement, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            return;
        }

        if (node is Constant constant) {
            int pc = code.Count;
            sourceMap[pc] = node.Id;
            if (TryInlinableInt(constant.Value, out int intVal)) {
                code.Add((byte)OpCode.PushInt);
                EmitInt32(code, intVal);
            }
            else if (TryInlinableLong(constant.Value, out long longVal)) {
                code.Add((byte)OpCode.PushLong);
                EmitInt64(code, longVal);
            }
            else if (TryInlinableDouble(constant.Value, out double doubleVal)) {
                code.Add((byte)OpCode.PushDouble);
                EmitDouble(code, doubleVal);
            }
            else {
                int idx = constants?.Count ?? 0;
                constants?.Add(constant.Value);
                code.Add((byte)OpCode.LoadConst);
                EmitInt32(code, idx);
            }
            return;
        }

        int emitPc = code.Count;
        sourceMap[emitPc] = node.Id;

        switch (node) {
            case Add add:
                if (analysis?.GetResolvedType(add)?.GetRuntimeType() == typeof(string)) {
                    Emit(add.LeftHandValue, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                    Emit(add.RightHandValue, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                    int concatIdx = callSites?.Count ?? 0;
                    callSites?.Add(StringConcatDelegate);
                    code.Add((byte)OpCode.PushInt); EmitInt32(code, 2);
                    code.Add((byte)OpCode.PushInt); EmitInt32(code, 1);
                    code.Add((byte)OpCode.CallExternal); EmitInt32(code, concatIdx);
                }
                else {
                    EmitBinary(add.LeftHandValue, add.RightHandValue, ResolveBinaryOp(add, OpCode.Add, OpCode.Add, OpCode.DAdd, analysis),
                              code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                }
                return;

            case Subtract sub:
                EmitBinary(sub.LeftHandValue, sub.RightHandValue, ResolveBinaryOp(sub, OpCode.Sub, OpCode.Sub, OpCode.DSub, analysis),
                          code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case Multiply mul:
                EmitBinary(mul.LeftHandValue, mul.RightHandValue, ResolveBinaryOp(mul, OpCode.Mul, OpCode.Mul, OpCode.DMul, analysis),
                          code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case Divide div:
                EmitBinary(div.LeftHandValue, div.RightHandValue, ResolveBinaryOp(div, OpCode.Div, OpCode.UDiv, OpCode.DDiv, analysis),
                          code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case Modulo mod:
                EmitBinary(mod.LeftHandValue, mod.RightHandValue, ResolveBinaryOp(mod, OpCode.Mod, OpCode.UMod, OpCode.Mod, analysis),
                          code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case UnaryMinus un:
                Emit(un.Operand, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels); {
                    var typeDef = analysis?.GetResolvedType(un);
                    var clrType = typeDef?.GetRuntimeType();
                    if (clrType == typeof(double) || clrType == typeof(float)) {
                        code.Add((byte)OpCode.DNeg);
                        return;
                    }
                }
                code.Add((byte)OpCode.Neg);
                return;

            case Equal eq:
                EmitBinary(eq.LeftHandValue, eq.RightHandValue, ResolveComparisonOp(eq.LeftHandValue, eq.RightHandValue, OpCode.Eq, OpCode.Eq, OpCode.DEq, analysis),
                          code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case NotEqual ne:
                EmitBinary(ne.LeftHandValue, ne.RightHandValue, ResolveComparisonOp(ne.LeftHandValue, ne.RightHandValue, OpCode.Ne, OpCode.Ne, OpCode.DNe, analysis),
                          code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case LessThan lt:
                EmitBinary(lt.LeftHandValue, lt.RightHandValue, ResolveComparisonOp(lt.LeftHandValue, lt.RightHandValue, OpCode.Lt, OpCode.ULt, OpCode.DLt, analysis),
                          code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case LessThanOrEqual le:
                EmitBinary(le.LeftHandValue, le.RightHandValue, ResolveComparisonOp(le.LeftHandValue, le.RightHandValue, OpCode.Le, OpCode.ULe, OpCode.DLe, analysis),
                          code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case GreaterThan gt:
                EmitBinary(gt.LeftHandValue, gt.RightHandValue, ResolveComparisonOp(gt.LeftHandValue, gt.RightHandValue, OpCode.Gt, OpCode.UGt, OpCode.DGt, analysis),
                          code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case GreaterThanOrEqual ge:
                EmitBinary(ge.LeftHandValue, ge.RightHandValue, ResolveComparisonOp(ge.LeftHandValue, ge.RightHandValue, OpCode.Ge, OpCode.UGe, OpCode.DGe, analysis),
                          code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case And andNode:
                EmitShortCircuitAnd(andNode,
code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case Or orNode:
                EmitShortCircuitOr(orNode,
code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case Not notNode:
                Emit(notNode.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                code.Add((byte)OpCode.Not);
                return;

            case Conditional cond:
                EmitConditional(cond,
code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case IfStatement ifStmt:
                EmitIf(ifStmt,
code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case WhileLoop wl:
                EmitWhileLoop(wl,
code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case DoWhileLoop dwl:
                EmitDoWhileLoop(dwl,
code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case ForLoop fl:
                EmitForLoop(fl,
                code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case BreakStatement brk:
                if (labels?.LoopLabels.Count > 0) {
                    if (brk.Label is not null) {
                        foreach (var entry in labels.LoopLabels)
                            if (entry.Name == brk.Label) { labels.JumpTo(entry.Break, code, relocations); break; }
                    }
                    else {
                        labels.JumpTo(labels.LoopLabels.Peek().Break, code, relocations);
                    }
                }
                return;

            case ContinueStatement cont:
                if (labels?.LoopLabels.Count > 0) {
                    if (cont.Label is not null) {
                        foreach (var entry in labels.LoopLabels)
                            if (entry.Name == cont.Label) { labels.JumpTo(entry.Continue, code, relocations); break; }
                    }
                    else {
                        labels.JumpTo(labels.LoopLabels.Peek().Continue, code, relocations);
                    }
                }
                return;

            case GotoStatement got:
                labels?.JumpTo(got.Target, code, relocations);
                return;

            case LabelDeclaration lbl:
                labels?.Mark(lbl.Name, code);
                if (labels is not null) labels.PendingLoopLabel = lbl.Name;
                Emit(lbl.Statement, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                if (labels is not null) labels.PendingLoopLabel = null;
                return;

            case SwitchStatement swt:
                EmitSwitch(swt,
                code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case ThrowStatement thr:
                Emit(thr.Exception, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                code.Add((byte)OpCode.Throw);
                return;

            case Default def:
                code.Add((byte)OpCode.PushInt);
                EmitInt32(code, 0);
                return;

            case NullForgiving nf:
                Emit(nf.Operand, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case Assignment assign:
                Emit(assign.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                string? destName = assign.Destination switch {
                    Variable v => v.Name,
                    Parameter p => p.Name,
                    _ => null
                };
                if (destName is not null) {
                    if (paramIndexMap is not null && paramIndexMap.TryGetValue(destName, out int storeIdx)) {
                        code.Add((byte)OpCode.Dup);
                        code.Add((byte)OpCode.StoreArg);
                        EmitInt32(code, storeIdx);
                    }
                    else if (localIndexMap is not null && localIndexMap.TryGetValue(destName, out int localStoreIdx)) {
                        code.Add((byte)OpCode.Dup);
                        code.Add((byte)OpCode.StoreLocal);
                        EmitInt32(code, localStoreIdx);
                    }
                    else if (lambdaState?.UpvalueMap is not null && lambdaState.UpvalueMap.TryGetValue(destName, out int upStoreIdx)) {
                        code.Add((byte)OpCode.Dup);
                        code.Add((byte)OpCode.StoreUpvalue);
                        EmitInt32(code, upStoreIdx);
                    }
                }
                else if (assign.Destination is Member memberDest) {
                    EmitAssignmentMember(memberDest, assign.Value, code, sourceMap, analysis,
                        functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                }
                else if (assign.Destination is IndexAccess idxDest) {
                    EmitAssignmentIndexAccess(idxDest, assign.Value, code, sourceMap, analysis,
                        functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                }
                return;

            case Coalesce coalesce:
                EmitCoalesce(coalesce, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, null);
                return;

            case Member member:
                EmitMember(member, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, null);
                return;

            case IndexAccess idxAccess:
                EmitIndexAccess(idxAccess, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, null);
                return;

            case New newExpr:
                EmitNew(newExpr, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, null);
                return;

            case TypeCast tc:
                Emit(tc.Operand, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case TypeIs ti:
                Emit(ti.Operand, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                int isNotNullIdx = callSites?.Count ?? 0;
                callSites?.Add(IsNotNullDelegate);
                code.Add((byte)OpCode.PushInt); EmitInt32(code, 1);
                code.Add((byte)OpCode.PushInt); EmitInt32(code, 1);
                code.Add((byte)OpCode.CallExternal); EmitInt32(code, isNotNullIdx);
                return;

            case TypeAs:
            case ParameterReference:
            case ThisReference:
                return;

            case Parameter param:
                if (param.Name is not null) {
                    if (paramIndexMap is not null && paramIndexMap.TryGetValue(param.Name, out int pIdx)) {
                        code.Add((byte)OpCode.LoadArg);
                        EmitInt32(code, pIdx);
                        return;
                    }
                    if (localIndexMap is not null && localIndexMap.TryGetValue(param.Name, out int lIdx)) {
                        code.Add((byte)OpCode.LoadLocal);
                        EmitInt32(code, lIdx);
                        return;
                    }
                    if (param.DefaultValue is not null) {
                        Emit(param.DefaultValue, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                        return;
                    }
                }
                return;

            case Invoke invoke:
                EmitInvoke(invoke, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels, lambdaState);
                return;

            case Block block:
                for (int i = 0; i < block.Nodes.Count; i++) {
                    Emit(block.Nodes[i], code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                    if (i < block.Nodes.Count - 1 && EmitsValue(block.Nodes[i]))
                        code.Add((byte)OpCode.Pop);
                }
                return;

            case Return retNode:
                if (retNode.Value is not null)
                    Emit(retNode.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                return;

            case Variable variable:
                if (variable.Name is null) return;
                if (paramIndexMap is not null && paramIndexMap.TryGetValue(variable.Name, out int paramIdx)) {
                    code.Add((byte)OpCode.LoadArg);
                    EmitInt32(code, paramIdx);
                }
                else if (localIndexMap is not null && localIndexMap.TryGetValue(variable.Name, out int localIdx)) {
                    code.Add((byte)OpCode.LoadLocal);
                    EmitInt32(code, localIdx);
                }
                else if (lambdaState?.UpvalueMap is not null && lambdaState.UpvalueMap.TryGetValue(variable.Name, out int upIdx)) {
                    code.Add((byte)OpCode.LoadUpvalue);
                    EmitInt32(code, upIdx);
                }
                return;

            case SuspendNode sn:
                Emit(sn.Inner, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                code.Add((byte)OpCode.Pop);
                code.Add((byte)OpCode.Int);
                EmitInt32(code, 0);
                return;

            case Lambda lambda:
                if (lambdaState?.FuncMap is not null && lambdaState.FuncMap.TryGetValue(lambda, out int lambdaFuncIdx)) {
                    var captures = lambdaState?.CaptureMap is not null && lambdaState.CaptureMap.TryGetValue(lambda, out var caps)
                        ? caps : [];
                    for (int i = 0; i < captures.Count; i++) {
                        if (paramIndexMap is not null && paramIndexMap.TryGetValue(captures[i], out int pIdx)) {
                            code.Add((byte)OpCode.LoadArg);
                            EmitInt32(code, pIdx);
                        }
                        else if (localIndexMap is not null && localIndexMap.TryGetValue(captures[i], out int localIdx)) {
                            code.Add((byte)OpCode.LoadLocal);
                            EmitInt32(code, localIdx);
                        }
                        else {
                            code.Add((byte)OpCode.PushInt);
                            EmitInt32(code, 0);
                        }
                    }
                    code.Add((byte)OpCode.AllocateClosure);
                    EmitInt32(code, lambdaFuncIdx);
                    EmitInt32(code, captures.Count);
                }
                return;

            case Await awaitNode:
                Emit(awaitNode.Operand, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                int awaitSiteIdx = callSites?.Count ?? 0;
                callSites?.Add(AwaitResultDelegate);
                code.Add((byte)OpCode.PushInt);
                EmitInt32(code, 1);
                code.Add((byte)OpCode.PushInt);
                EmitInt32(code, 1);
                code.Add((byte)OpCode.CallExternal);
                EmitInt32(code, awaitSiteIdx);
                return;

            case TypeDefinitionNode:
                return;

            case ForEachLoop fe:
                EmitForEachLoop(fe, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels, null);
                return;

            case UsingStatement us:
                EmitUsingStatement(us, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels, null);
                return;

            case TryCatchFinally tcf: {
                    int tryStart = code.Count;
                    Emit(tcf.TryBlock, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                    int tryEnd = code.Count;

                    string endLabel = labels!.Next();
                    string? finallyEntry = tcf.FinallyBlock is not null ? labels.Next() : null;

                    // Normal path: run finally (if any), then end
                    if (finallyEntry is not null)
                        labels.JumpTo(finallyEntry, code, relocations);
                    else
                        labels.JumpTo(endLabel, code, relocations);

                    int? finallyStart = null;
                    int catchStart = -1;

                    if (tcf.CatchClauses is not null && tcf.CatchClauses.Count > 0) {
                        catchStart = code.Count;
                        foreach (var cc in tcf.CatchClauses) {
                            if (cc.VariableName is not null
                                && paramIndexMap is not null
                                && paramIndexMap.TryGetValue(cc.VariableName, out int varIdx)) {
                                code.Add((byte)OpCode.StoreArg);
                                EmitInt32(code, varIdx);
                            }
                            else {
                                code.Add((byte)OpCode.Pop);
                            }
                            Emit(cc.Body, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                            if (finallyEntry is not null) {
                                // fall through to finally
                            }
                            else {
                                labels.JumpTo(endLabel, code, relocations);
                            }
                        }
                    }

                    if (tcf.FinallyBlock is not null) {
                        if (finallyEntry is not null)
                            labels.Mark(finallyEntry, code);
                        finallyStart = code.Count;
                        Emit(tcf.FinallyBlock, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                        code.Add((byte)OpCode.EndFinally);
                        if (EmitsValue(tcf.FinallyBlock))
                            code.Add((byte)OpCode.Pop);
                    }

                    labels.Mark(endLabel, code);
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
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels, LambdaEmitState? lambdaState = null) {

        Emit(left, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels, lambdaState);
        Emit(right, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels, lambdaState);
        code.Add((byte)op);
    }

    private static void EmitShortCircuitAnd(
        And andNode,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels) {

        labels ??= new LabelContext();
        string end = labels.Next();
        Emit(andNode.LeftHandValue, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        code.Add((byte)OpCode.Dup);
        labels.JumpIfFalseTo(end, code, relocations);
        code.Add((byte)OpCode.Pop);
        Emit(andNode.RightHandValue, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.Mark(end, code);
    }

    private static void EmitShortCircuitOr(
        Or orNode,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels) {

        labels ??= new LabelContext();
        string evalRight = labels.Next();
        string after = labels.Next();
        Emit(orNode.LeftHandValue, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        code.Add((byte)OpCode.Dup);
        labels.JumpIfFalseTo(evalRight, code, relocations);
        labels.JumpTo(after, code, relocations);
        labels.Mark(evalRight, code);
        code.Add((byte)OpCode.Pop);
        Emit(orNode.RightHandValue, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.Mark(after, code);
    }

    private static void EmitConditional(
        Conditional cond,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels) {

        labels ??= new LabelContext();
        string elseL = labels.Next();
        string endL = labels.Next();
        Emit(cond.Condition, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.JumpIfFalseTo(elseL, code, relocations);
        Emit(cond.IfTrue, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.JumpTo(endL, code, relocations);
        labels.Mark(elseL, code);
        Emit(cond.IfFalse, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.Mark(endL, code);
    }

    private static void EmitIf(
        IfStatement ifStmt,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels) {

        labels ??= new LabelContext();
        string elseL = labels.Next();
        string endL = labels.Next();
        Emit(ifStmt.Condition, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.JumpIfFalseTo(elseL, code, relocations);
        Emit(ifStmt.ThenBranch, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.JumpTo(endL, code, relocations);
        labels.Mark(elseL, code);
        if (ifStmt.ElseBranch is not null)
            Emit(ifStmt.ElseBranch, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.Mark(endL, code);
    }

    private static void EmitWhileLoop(
        WhileLoop wl,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels) {

        labels ??= new LabelContext();
        string breakL = labels.Next();
        string contL = labels.Next();
        string loopLabel = labels?.PendingLoopLabel ?? "";
        if (labels is not null) labels.PendingLoopLabel = null;
        labels!.LoopLabels.Push((loopLabel, breakL, contL));
        labels!.Mark(contL, code);
        Emit(wl.Condition, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.JumpIfFalseTo(breakL, code, relocations);
        Emit(wl.Body, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.JumpTo(contL, code, relocations);
        labels.Mark(breakL, code);
        labels.LoopLabels.Pop();
    }

    private static void EmitDoWhileLoop(
        DoWhileLoop dwl,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels) {

        labels ??= new LabelContext();
        string breakL = labels.Next();
        string contL = labels.Next();
        string loopLabel = labels?.PendingLoopLabel ?? "";
        if (labels is not null) labels.PendingLoopLabel = null;
        labels!.LoopLabels.Push((loopLabel, breakL, contL));
        labels!.Mark(contL, code);
        Emit(dwl.Body, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        Emit(dwl.Condition, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.JumpIfFalseTo(breakL, code, relocations);
        labels.JumpTo(contL, code, relocations);
        labels.Mark(breakL, code);
        labels.LoopLabels.Pop();
    }

    private static void EmitForLoop(
        ForLoop fl,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels) {

        labels ??= new LabelContext();
        string breakL = labels.Next();
        string contL = labels.Next();
        string loopLabel = labels?.PendingLoopLabel ?? "";
        if (labels is not null) labels.PendingLoopLabel = null;
        labels!.LoopLabels.Push((loopLabel, breakL, contL));

        if (fl.Initializer is not null)
            Emit(fl.Initializer, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);

        labels!.Mark(contL, code);

        if (fl.Condition is not null) {
            Emit(fl.Condition, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            labels.JumpIfFalseTo(breakL, code, relocations);
        }

        Emit(fl.Body, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);

        if (fl.Increment is not null)
            Emit(fl.Increment, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);

        labels.JumpTo(contL, code, relocations);
        labels.Mark(breakL, code);
        labels.LoopLabels.Pop();
    }

    private static void EmitForEachLoop(
        ForEachLoop fe,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels,
        LambdaEmitState? lambdaState) {

        var enumHolder = new object[1];
        int holderIdx = constants?.Count ?? 0;
        constants?.Add(enumHolder);

        labels ??= new LabelContext();
        string breakL = labels.Next();
        string contL = labels.Next();
        string loopLabel = labels?.PendingLoopLabel ?? "";
        if (labels is not null) labels.PendingLoopLabel = null;
        labels!.LoopLabels.Push((loopLabel, breakL, contL));

        code.Add((byte)OpCode.LoadConst);
        EmitInt32(code, holderIdx);
        Emit(fe.Collection, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        int initEnumIdx = callSites?.Count ?? 0;
        callSites?.Add(InitEnumeratorDelegate);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 2);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 0);
        code.Add((byte)OpCode.CallExternal);
        EmitInt32(code, initEnumIdx);

        labels!.Mark(contL, code);

        code.Add((byte)OpCode.LoadConst);
        EmitInt32(code, holderIdx);
        int moveNextIdx = callSites?.Count ?? 0;
        callSites?.Add(MoveNextDelegate);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 1);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 1);
        code.Add((byte)OpCode.CallExternal);
        EmitInt32(code, moveNextIdx);
        labels.JumpIfFalseTo(breakL, code, relocations);

        code.Add((byte)OpCode.LoadConst);
        EmitInt32(code, holderIdx);
        int getCurrIdx = callSites?.Count ?? 0;
        callSites?.Add(GetCurrentDelegate);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 1);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 1);
        code.Add((byte)OpCode.CallExternal);
        EmitInt32(code, getCurrIdx);

        if (fe.LoopVariable is { Name: not null } lv) {
            if (paramIndexMap is not null && paramIndexMap.TryGetValue(lv.Name, out int paramIdx)) {
                code.Add((byte)OpCode.StoreArg);
                EmitInt32(code, paramIdx);
            }
            else if (localIndexMap is not null && localIndexMap.TryGetValue(lv.Name, out int localIdx)) {
                code.Add((byte)OpCode.StoreLocal);
                EmitInt32(code, localIdx);
            }
            else {
                code.Add((byte)OpCode.Pop);
            }
        }
        else {
            code.Add((byte)OpCode.Pop);
        }

        Emit(fe.Body, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        if (EmitsValue(fe.Body))
            code.Add((byte)OpCode.Pop);

        labels.JumpTo(contL, code, relocations);

        labels.Mark(breakL, code);

        code.Add((byte)OpCode.LoadConst);
        EmitInt32(code, holderIdx);
        int disposeEnumIdx = callSites?.Count ?? 0;
        callSites?.Add(DisposeEnumeratorDelegate);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 1);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 0);
        code.Add((byte)OpCode.CallExternal);
        EmitInt32(code, disposeEnumIdx);

        labels.LoopLabels.Pop();
    }

    private static void EmitUsingStatement(
        UsingStatement us,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels,
        LambdaEmitState? lambdaState) {

        var resourceHolder = new object[1];
        int holderIdx = constants?.Count ?? 0;
        constants?.Add(resourceHolder);

        labels ??= new LabelContext();
        int tryStart = code.Count;

        code.Add((byte)OpCode.LoadConst);
        EmitInt32(code, holderIdx);
        Emit(us.Resource, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        int saveIdx = callSites?.Count ?? 0;
        callSites?.Add(SaveResourceDelegate);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 2);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 0);
        code.Add((byte)OpCode.CallExternal);
        EmitInt32(code, saveIdx);

        Emit(us.Body, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        if (EmitsValue(us.Body))
            code.Add((byte)OpCode.Pop);

        int tryEnd = code.Count;

        int finallyStart = code.Count;
        code.Add((byte)OpCode.LoadConst);
        EmitInt32(code, holderIdx);
        int disposeIdx = callSites?.Count ?? 0;
        callSites?.Add(DisposeResourceDelegate);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 1);
        code.Add((byte)OpCode.PushInt);
        EmitInt32(code, 0);
        code.Add((byte)OpCode.CallExternal);
        EmitInt32(code, disposeIdx);

        code.Add((byte)OpCode.EndFinally);

        labels.ExceptionRegions.Add(new ExceptionRegion(tryStart, tryEnd, -1, finallyStart));
    }

    private static void EmitSwitch(
        SwitchStatement swt,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels) {

        labels ??= new LabelContext();
        string endL = labels.Next();
        var caseLabels = swt.Cases.Select(_ => labels.Next()).ToList();
        string defaultL = labels.Next();

        Emit(swt.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);

        for (int i = 0; i < swt.Cases.Count; i++) {
            code.Add((byte)OpCode.Dup);
            Emit(swt.Cases[i].Pattern, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            code.Add((byte)OpCode.Eq);
            labels.JumpIfFalseTo(caseLabels[i], code, relocations);
            code.Add((byte)OpCode.Pop);
            Emit(swt.Cases[i].Body, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            labels.JumpTo(endL, code, relocations);
            labels.Mark(caseLabels[i], code);
        }

        code.Add((byte)OpCode.Pop);
        if (swt.DefaultCase is not null)
            Emit(swt.DefaultCase, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.Mark(endL, code);
    }

    private static void EmitCoalesce(
        Coalesce coalesce,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis, List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LambdaEmitState? lambdaState) {

        var labels = new LabelContext();
        string after = labels.Next();
        Emit(coalesce.LeftHandValue, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        code.Add((byte)OpCode.Dup);
        code.Add((byte)OpCode.IsNull);
        labels.JumpIfFalseTo(after, code, relocations);
        code.Add((byte)OpCode.Pop);
        Emit(coalesce.RightHandValue, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
        labels.Mark(after, code);
    }

    private static void EmitInvoke(
        Invoke invoke,
        List<byte> code,
        Dictionary<int, NodeId> sourceMap,
        AnalysisResult? analysis,
        List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap,
        IReadOnlyDictionary<string, int>? localIndexMap,
        List<object?>? constants,
        List<CallSiteDelegate>? callSites,
        List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels,
        LambdaEmitState? lambdaState = null) {

        if (analysis?.GetResolvedMember(invoke) is AstMethodDefinition astMethod) {
            var methodDef = astMethod.DefinitionNode;
            if (functionIndexMap is not null && functionIndexMap.TryGetValue(methodDef, out int funcIndex)) {
                foreach (var arg in invoke.Arguments) {
                    Emit(arg, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
                }
                int paramCount = methodDef.Parameters?.Count ?? 0;
                code.Add((byte)OpCode.PushInt);
                EmitInt32(code, paramCount);
                code.Add((byte)OpCode.Call);
                EmitInt32(code, funcIndex);
                sourceMap[code.Count - 5] = invoke.Id;
                return;
            }
        }

        if (analysis?.GetResolvedMember(invoke) is ClrMethod clrMethod) {
            var methodInfo = clrMethod.MethodInfo;
            bool isStatic = clrMethod.LifetimeModifier == LifetimeModifier.Static;

            int siteIndex = callSites?.Count ?? 0;
            callSites?.Add(CallSiteCompiler.Compile(methodInfo, isStatic));

            if (!isStatic && invoke.Delegate is Member memberAccess) {
                Emit(memberAccess.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            }

            foreach (var arg in invoke.Arguments) {
                Emit(arg, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            }

            int argCount = invoke.Arguments.Length + (isStatic ? 0 : 1);
            code.Add((byte)OpCode.PushInt);
            EmitInt32(code, argCount);
            code.Add((byte)OpCode.PushInt);
            EmitInt32(code, methodInfo.ReturnType != typeof(void) ? 1 : 0);
            code.Add((byte)OpCode.CallExternal);
            EmitInt32(code, siteIndex);
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
            Emit(lambdaTarget, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels, lambdaState);
            foreach (var arg in invoke.Arguments) {
                Emit(arg, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels, lambdaState);
            }
            int totalArgs = (lambdaTarget.Parameters?.Count ?? 0) + 1;
            code.Add((byte)OpCode.PushInt);
            EmitInt32(code, totalArgs);
            code.Add((byte)OpCode.Call);
            EmitInt32(code, lFuncIdx);
            sourceMap[code.Count - 5] = invoke.Id;
            return;
        }

        // Generic delegate path
        if (invoke.Delegate is not null) {
            Emit(invoke.Delegate, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels, lambdaState);
            foreach (var arg in invoke.Arguments) {
                Emit(arg, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            }
            int totalArgs = invoke.Arguments.Length + 1;
            code.Add((byte)OpCode.PushInt);
            EmitInt32(code, totalArgs);
            code.Add((byte)OpCode.CallClosure);
            sourceMap[code.Count - 5] = invoke.Id;
        }
    }

    private static void EmitMember(
        Member member,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis,
        List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels) {

        var resolved = analysis?.GetResolvedMember(member);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var getter = pi.GetGetMethod(nonPublic: true);
            if (getter is null) return;
            if (!isStatic)
                Emit(member.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            int siteIdx = callSites?.Count ?? 0;
            callSites?.Add(CallSiteCompiler.Compile(getter, isStatic));
            int argCount = isStatic ? 0 : 1;
            code.Add((byte)OpCode.PushInt); EmitInt32(code, argCount);
            code.Add((byte)OpCode.PushInt); EmitInt32(code, 1);
            code.Add((byte)OpCode.CallExternal); EmitInt32(code, siteIdx);
            return;
        }
        if (resolved is ClrTypeField { FieldInfo: var fi, LifetimeModifier: var lm2 }) {
            bool isStatic = lm2 == LifetimeModifier.Static;
            if (!isStatic)
                Emit(member.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            int siteIdx = callSites?.Count ?? 0;
            callSites?.Add(CallSiteCompiler.CompileFieldGetter(fi, isStatic));
            int argCount = isStatic ? 0 : 1;
            code.Add((byte)OpCode.PushInt); EmitInt32(code, argCount);
            code.Add((byte)OpCode.PushInt); EmitInt32(code, 1);
            code.Add((byte)OpCode.CallExternal); EmitInt32(code, siteIdx);
            return;
        }
    }

    private static void EmitIndexAccess(
        IndexAccess idx,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis,
        List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels) {

        var resolved = analysis?.GetResolvedMember(idx);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var getter = pi.GetGetMethod(nonPublic: true);
            if (getter is null) return;
            if (!isStatic)
                Emit(idx.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            foreach (var arg in idx.Arguments)
                Emit(arg, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            int siteIdx = callSites?.Count ?? 0;
            callSites?.Add(CallSiteCompiler.Compile(getter, isStatic));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length;
            code.Add((byte)OpCode.PushInt); EmitInt32(code, argCount);
            code.Add((byte)OpCode.PushInt); EmitInt32(code, 1);
            code.Add((byte)OpCode.CallExternal); EmitInt32(code, siteIdx);
            return;
        }
        if (resolved is ClrTypeSyntheticProperty { Read: not null, LifetimeModifier: var lm2 }) {
            var synReader = ((ClrTypeSyntheticProperty)resolved).Read!;
            bool isStatic = lm2 == LifetimeModifier.Static;
            if (!isStatic)
                Emit(idx.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            foreach (var arg in idx.Arguments)
                Emit(arg, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            int siteIdx = callSites?.Count ?? 0;
            callSites?.Add(CompileSyntheticGetter(synReader, isStatic, idx.Arguments.Length));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length;
            code.Add((byte)OpCode.PushInt); EmitInt32(code, argCount);
            code.Add((byte)OpCode.PushInt); EmitInt32(code, 1);
            code.Add((byte)OpCode.CallExternal); EmitInt32(code, siteIdx);
            return;
        }
    }

    private static void EmitAssignmentMember(
        Member member, Node value,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis,
        List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap,
        List<object?>? constants, List<CallSiteDelegate>? callSites,
        List<(int CodeOffset, int TargetPc)>? relocations, LabelContext? labels) {

        var resolved = analysis?.GetResolvedMember(member);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var setter = pi.GetSetMethod(nonPublic: true);
            if (setter is null) return;
            if (!isStatic)
                Emit(member.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            Emit(value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            int siteIdx = callSites?.Count ?? 0;
            callSites?.Add(CallSiteCompiler.Compile(setter, isStatic));
            int argCount = (isStatic ? 0 : 1) + 1;
            code.Add((byte)OpCode.PushInt); EmitInt32(code, argCount);
            code.Add((byte)OpCode.PushInt); EmitInt32(code, 0);
            code.Add((byte)OpCode.CallExternal); EmitInt32(code, siteIdx);
            return;
        }
        if (resolved is ClrTypeField { FieldInfo: var fi, LifetimeModifier: var lm2 }) {
            bool isStatic = lm2 == LifetimeModifier.Static;
            if (!isStatic)
                Emit(member.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            Emit(value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            int siteIdx = callSites?.Count ?? 0;
            callSites?.Add(CallSiteCompiler.CompileFieldSetter(fi, isStatic));
            int argCount = (isStatic ? 0 : 1) + 1;
            code.Add((byte)OpCode.PushInt); EmitInt32(code, argCount);
            code.Add((byte)OpCode.PushInt); EmitInt32(code, 0);
            code.Add((byte)OpCode.CallExternal); EmitInt32(code, siteIdx);
            return;
        }
    }

    private static void EmitAssignmentIndexAccess(
        IndexAccess idx, Node value,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis,
        List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap,
        List<object?>? constants, List<CallSiteDelegate>? callSites,
        List<(int CodeOffset, int TargetPc)>? relocations, LabelContext? labels) {

        var resolved = analysis?.GetResolvedMember(idx);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var setter = pi.GetSetMethod(nonPublic: true);
            if (setter is null) return;
            if (!isStatic)
                Emit(idx.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            foreach (var arg in idx.Arguments)
                Emit(arg, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            Emit(value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            int siteIdx = callSites?.Count ?? 0;
            callSites?.Add(CallSiteCompiler.Compile(setter, isStatic));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length + 1;
            code.Add((byte)OpCode.PushInt); EmitInt32(code, argCount);
            code.Add((byte)OpCode.PushInt); EmitInt32(code, 0);
            code.Add((byte)OpCode.CallExternal); EmitInt32(code, siteIdx);
            return;
        }
        if (resolved is ClrTypeSyntheticProperty { Write: not null, LifetimeModifier: var lm2 }) {
            var synWriter = ((ClrTypeSyntheticProperty)resolved).Write!;
            bool isStatic = lm2 == LifetimeModifier.Static;
            if (!isStatic)
                Emit(idx.Value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            foreach (var arg in idx.Arguments)
                Emit(arg, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            Emit(value, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            int siteIdx = callSites?.Count ?? 0;
            callSites?.Add(CompileSyntheticSetter(synWriter, isStatic, idx.Arguments.Length + 1));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length + 1;
            code.Add((byte)OpCode.PushInt); EmitInt32(code, argCount);
            code.Add((byte)OpCode.PushInt); EmitInt32(code, 0);
            code.Add((byte)OpCode.CallExternal); EmitInt32(code, siteIdx);
            return;
        }
    }

    private static void EmitNew(
        New newExpr,
        List<byte> code, Dictionary<int, NodeId> sourceMap, AnalysisResult? analysis,
        List<FunctionEntry>? functions,
        Dictionary<MethodDefinitionNode, int>? functionIndexMap,
        IReadOnlyDictionary<string, int>? paramIndexMap, IReadOnlyDictionary<string, int>? localIndexMap, List<object?>? constants,
        List<CallSiteDelegate>? callSites, List<(int CodeOffset, int TargetPc)>? relocations,
        LabelContext? labels) {

        var resolved = analysis?.GetResolvedMember(newExpr);
        if (resolved is ClrConstructor { ConstructorInfo: var ci }) {
            foreach (var arg in newExpr.Arguments)
                Emit(arg, code, sourceMap, analysis, functions, functionIndexMap, paramIndexMap, localIndexMap, constants, callSites, relocations, labels);
            int siteIdx = callSites?.Count ?? 0;
            callSites?.Add(CallSiteCompiler.CompileConstructor(ci));
            code.Add((byte)OpCode.PushInt); EmitInt32(code, newExpr.Arguments.Length);
            code.Add((byte)OpCode.PushInt); EmitInt32(code, 1);
            code.Add((byte)OpCode.CallExternal); EmitInt32(code, siteIdx);
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