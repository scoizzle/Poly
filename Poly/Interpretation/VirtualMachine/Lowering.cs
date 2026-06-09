using Poly.Interpretation.Analysis;
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

    private readonly record struct EmitWork(
        Node? Node,
        EmitPhase Phase,
        int Data = 0,
        string? Label = null,
        string? Label2 = null
    );

    private enum EmitPhase : byte {
        Enter,
        AfterChildren,
        MarkLabel,
        Jump,
        JumpIfFalse,
        Pop,
        Dup,
        Eq,
        IsNull,
        EndFinally,
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
        if (Vm.IsValidHeapHandle(state, raw)) {
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
            if (Vm.IsValidHeapHandle(state, raw)) {
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
        object? awaitable = Vm.ResolveHeapValue(state, handle);
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
        object? collection = Vm.IsValidHeapHandle(state, collectionHandle) ? state.Heap.Get(collectionHandle) : (object?)collectionHandle;
        if (collection is IEnumerable enumerable) {
            var enumerator = enumerable.GetEnumerator();
            if (Vm.IsValidHeapHandle(state, holderHandle) && state.Heap.Get(holderHandle) is object[] holder)
                holder[0] = enumerator;
        }
        if (argSlots > 0) state.Stack.Drop(argSlots);
    };

    private static readonly CallSiteDelegate GetCurrentDelegate = state => {
        var (argSlots, hasRet) = state.Stack.Pop<(int, int)>();
        int baseOff = state.Stack.SP - argSlots;
        int holderHandle = state.Stack.AsSpan()[baseOff];
        var enumerator = Vm.IsValidHeapHandle(state, holderHandle) && state.Heap.Get(holderHandle) is object[] h2 ? h2[0] as IEnumerator : null;
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
        if (Vm.IsValidHeapHandle(state, holderHandle) && state.Heap.Get(holderHandle) is object[] h3 && h3[0] is IDisposable d)
            d.Dispose();
        if (argSlots > 0) state.Stack.Drop(argSlots);
    };

    private static readonly CallSiteDelegate SaveResourceDelegate = state => {
        var (argSlots, hasRet) = state.Stack.Pop<(int, int)>();
        int baseOff = state.Stack.SP - argSlots;
        int holderHandle = state.Stack.AsSpan()[baseOff];
        int resourceHandle = state.Stack.AsSpan()[baseOff + 1];
        if (Vm.IsValidHeapHandle(state, holderHandle) && state.Heap.Get(holderHandle) is object[] holder) {
            object? resource = Vm.IsValidHeapHandle(state, resourceHandle) ? state.Heap.Get(resourceHandle) : (object?)resourceHandle;
            holder[0] = resource!;
        }
        if (argSlots > 0) state.Stack.Drop(argSlots);
    };

    private static readonly CallSiteDelegate DisposeResourceDelegate = state => {
        var (argSlots, hasRet) = state.Stack.Pop<(int, int)>();
        int baseOff = state.Stack.SP - argSlots;
        int holderHandle = state.Stack.AsSpan()[baseOff];
        if (Vm.IsValidHeapHandle(state, holderHandle) && state.Heap.Get(holderHandle) is object[] h4 && h4[0] is IDisposable d)
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

            int methodRetBytes = (method.Body is not null && EmitsValue(method.Body, analysis)) ? 1 : 0;
            ctx.Functions.Add(new FunctionEntry(entryPc, paramCount, methodRetBytes, localIndexMap.Count));

            if (method.Body is not null) {
                ctx.ParamIndexMap = paramIndexMap;
                ctx.LocalIndexMap = localIndexMap;
                var methodWorklist = new Stack<EmitWork>();
                methodWorklist.Push(new EmitWork(method.Body, EmitPhase.Enter));
                RunWorklist(methodWorklist, ref ctx, null);
            }
            ctx.Code.Add((byte)OpCode.Return);
        }

        // Pre-scan: allocate function indices for all lambdas before emitting any bodies
        foreach (var lambda in referencedLambdas) {
            int funcIdx = ctx.Functions.Count;
            ctx.LambdaFuncMap![lambda] = funcIdx;
            ctx.Functions.Add(new FunctionEntry(0, 0, 0, 0)); // placeholder
        }

        // Pre-scan: populate capture maps for all lambdas (before any body emission)
        for (int i = 0; i < referencedLambdas.Count; i++) {
            var lambda = referencedLambdas[i];
            int paramCount = lambda.Parameters.Count;
            var paramIndexMap = new Dictionary<string, int>();
            for (int j = 0; j < lambda.Parameters.Count; j++)
                paramIndexMap[lambda.Parameters[j].Name ?? ""] = j + 1;

            var localIndexMap = new Dictionary<string, int>();
            if (lambda.Body is not null)
                DiscoverLocalsFromAnalysis(lambda.Body, paramIndexMap, localIndexMap, analysis);

            var captures = new List<string>();
            if (lambda.Body is not null)
                DiscoverCapturesFromAnalysis(lambda.Body, paramIndexMap, localIndexMap, captures, analysis);

            ctx.LambdaCaptureMap![lambda] = captures;
        }

        // Emit lambda bodies with full LambdaFuncMap + LambdaCaptureMap available
        for (int i = 0; i < referencedLambdas.Count; i++) {
            var lambda = referencedLambdas[i];
            int entryPc = ctx.Code.Count;

            int paramCount = lambda.Parameters.Count;
            var paramIndexMap = new Dictionary<string, int>();
            for (int j = 0; j < lambda.Parameters.Count; j++)
                paramIndexMap[lambda.Parameters[j].Name ?? ""] = j + 1;

            var localIndexMap = new Dictionary<string, int>();
            if (lambda.Body is not null)
                DiscoverLocalsFromAnalysis(lambda.Body, paramIndexMap, localIndexMap, ctx.Analysis);

            var captures = ctx.LambdaCaptureMap![lambda];

            var upvalueMap = new Dictionary<string, int>();
            for (int j = 0; j < captures.Count; j++)
                upvalueMap[captures[j]] = j;
            ctx.LambdaCaptureMap![lambda] = captures;

            int retBytes = (lambda.Body is not null && EmitsValue(lambda.Body, ctx.Analysis)) ? 1 : 0;
            ctx.Functions[i] = new FunctionEntry(entryPc, paramCount + 1, retBytes, localIndexMap.Count);

            ctx.ParamIndexMap = paramIndexMap;
            ctx.LocalIndexMap = localIndexMap;
            ctx.UpvalueMap = upvalueMap;
            if (lambda.Body is not null) {
                var bodyLambdaState = new LambdaEmitState(ctx.LambdaFuncMap, ctx.LambdaCaptureMap, upvalueMap);
                var lambdaWorklist = new Stack<EmitWork>();
                lambdaWorklist.Push(new EmitWork(lambda.Body, EmitPhase.Enter));
                RunWorklist(lambdaWorklist, ref ctx, bodyLambdaState);
            }
            ctx.Code.Add((byte)OpCode.Return);
        }

        ctx.ParamIndexMap = null;
        ctx.LocalIndexMap = null;
        ctx.UpvalueMap = null;

        int mainEntry = ctx.Code.Count;
        PatchJump(ctx.Code, jumpOverMainPc, mainEntry);
        var rootLambdaState = new LambdaEmitState(ctx.LambdaFuncMap, ctx.LambdaCaptureMap, null);
        var worklist = new Stack<EmitWork>();
        worklist.Push(new EmitWork(root, EmitPhase.Enter));
        RunWorklist(worklist, ref ctx, rootLambdaState);

        foreach (var (codeOff, targetPc) in ctx.Relocations!)
            PatchJump(ctx.Code, codeOff, targetPc);

        ResolveAllJumps(ctx.Code, ctx.Labels!);

        Type? resultType = analysis?.GetResolvedType(root)?.GetRuntimeType();
        if (resultType == typeof(object)) resultType = null;
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

    private static void DiscoverLocals(Node node, IReadOnlyDictionary<string, int> paramIndexMap, Dictionary<string, int> localIndexMap, HashSet<string>? captures = null) {
        if (node is Assignment a && a.Destination is Variable dv && dv.Name is not null && !paramIndexMap.ContainsKey(dv.Name) && !localIndexMap.ContainsKey(dv.Name) && (captures is null || !captures.Contains(dv.Name)))
            localIndexMap[dv.Name] = localIndexMap.Count;
        if (node is ForEachLoop fl && fl.LoopVariable.Name is not null && !paramIndexMap.ContainsKey(fl.LoopVariable.Name) && !localIndexMap.ContainsKey(fl.LoopVariable.Name) && (captures is null || !captures.Contains(fl.LoopVariable.Name)))
            localIndexMap[fl.LoopVariable.Name] = localIndexMap.Count;
        if (node is Block block) {
            foreach (var bv2 in block.Variables)
                if (bv2 is Variable bv && bv.Name is not null && !paramIndexMap.ContainsKey(bv.Name) && !localIndexMap.ContainsKey(bv.Name) && (captures is null || !captures.Contains(bv.Name)))
                    localIndexMap[bv.Name] = localIndexMap.Count;
        }
        foreach (var child in node.Children) {
            if (child is not null)
                DiscoverLocals(child, paramIndexMap, localIndexMap, captures);
        }
    }

    private static int EmitSourceMap(ref EmitContext ctx, Node node) {
        int pc = ctx.Code.Count;
        ctx.SourceMap[pc] = node.Id;
        return pc;
    }

    private static void DiscoverLocalsFromAnalysis(
        Node body, IReadOnlyDictionary<string, int> paramIndexMap,
        Dictionary<string, int> localIndexMap, AnalysisResult? analysis) {
        if (analysis is null) { DiscoverLocals(body, paramIndexMap, localIndexMap); return; }
        var meta = analysis.GetMetadata<VariableScopeMetadata>(body);
        if (meta is null) { DiscoverLocals(body, paramIndexMap, localIndexMap); return; }
        // Collect Blocks within this lambda body
        var bodyBlocks = new HashSet<Block>();
        CollectBlocks(body, bodyBlocks);
        // Use metadata to register each variable declared in those Blocks
        foreach (var (block, vars) in meta.BlockScopes) {
            if (!bodyBlocks.Contains(block)) continue;
            foreach (var v in vars)
                if (v.Name is not null && !paramIndexMap.ContainsKey(v.Name) && !localIndexMap.ContainsKey(v.Name))
                    localIndexMap[v.Name] = localIndexMap.Count;
        }
    }

    private static void CollectBlocks(Node node, HashSet<Block> result) {
        if (node is Block b) result.Add(b);
        foreach (var child in node.Children)
            if (child is not null) CollectBlocks(child, result);
    }

    private static void DiscoverCapturesFromAnalysis(
        Node body, IReadOnlyDictionary<string, int> paramIndexMap,
        Dictionary<string, int> localIndexMap, List<string> captures,
        AnalysisResult? analysis) {
        if (analysis is null) { DiscoverCapturesWalk(body, paramIndexMap, localIndexMap, captures); return; }
        var meta = analysis.GetMetadata<VariableScopeMetadata>(body);
        if (meta is null) { DiscoverCapturesWalk(body, paramIndexMap, localIndexMap, captures); return; }
        var bodyBlocks = new HashSet<Block>();
        CollectBlocks(body, bodyBlocks);
        var captureNames = new HashSet<string>();
        foreach (var (useVar, declVar) in meta.VariableReferences) {
            if (useVar.Name is null) continue;
            if (paramIndexMap.ContainsKey(useVar.Name)) continue;
            if (localIndexMap.ContainsKey(useVar.Name)) continue;
            if (!IsDescendantOf(useVar, body)) continue;
            if (captureNames.Add(useVar.Name))
                captures.Add(useVar.Name);
        }
    }

    private static bool IsDescendantOf(Node node, Node ancestor) {
        var current = node;
        // Walk up through parents — but we don't have parent pointers.
        // Instead, walk the ancestor's children recursively.
        // Use the metadata: if the node is reachable from ancestor via Children.
        if (ReferenceEquals(node, ancestor)) return true;
        foreach (var child in ancestor.Children)
            if (child is not null && IsDescendantOf(node, child))
                return true;
        return false;
    }

    private static bool EmitsValue(Node node, AnalysisResult? analysis = null) {
        if (node is null) return false;
        // When analysis is available, use the resolved type (more accurate, auto-maintained)
        if (analysis is not null) {
            var type = analysis.GetResolvedType(node);
            if (type is not null) {
                var rt = type.GetRuntimeType();
                if (rt is not null && rt != typeof(void))
                    return true;
                if (rt == typeof(void))
                    return false;
                // null runtime type: fall through to the switch
            }
        }
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
            UsingStatement us => us.Body is not null && EmitsValue(us.Body, analysis),
            ForEachLoop fe => fe.Body is not null && EmitsValue(fe.Body, analysis),
            Block block => block.Nodes.Count > 0 && EmitsValue(block.Nodes[^1], analysis),
            _ => false,
        };
    }

    private static void RunWorklist(Stack<EmitWork> worklist, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        int wc = 0;
        while (worklist.TryPop(out var work)) {
            if (++wc > 5000) throw new InvalidOperationException("Worklist exceeded 5000 items (possible infinite work generation)");
            switch (work.Phase) {
                case EmitPhase.Enter:
                    EnterNode(work.Node!, worklist, ref ctx, lambdaState);
                    break;
                case EmitPhase.AfterChildren:
                    AfterChildren(work.Node!, work, worklist, ref ctx, lambdaState);
                    break;
                case EmitPhase.MarkLabel:
                    ctx.Labels!.Mark(work.Label!, ctx.Code);
                    break;
                case EmitPhase.Jump:
                    ctx.Labels!.JumpTo(work.Label!, ctx.Code, ctx.Relocations);
                    break;
                case EmitPhase.JumpIfFalse:
                    ctx.Labels!.JumpIfFalseTo(work.Label!, ctx.Code, ctx.Relocations);
                    break;
                case EmitPhase.Pop:
                    if (work.Data > 0) {
                        ctx.Code.Add((byte)OpCode.StoreArg);
                        EmitInt32(ctx.Code, work.Data);
                    }
                    else {
                        ctx.Code.Add((byte)OpCode.Pop);
                    }
                    break;
                case EmitPhase.Dup:
                    ctx.Code.Add((byte)OpCode.Dup);
                    break;
                case EmitPhase.Eq:
                    ctx.Code.Add((byte)OpCode.Eq);
                    break;
                case EmitPhase.IsNull:
                    ctx.Code.Add((byte)OpCode.IsNull);
                    break;
                case EmitPhase.EndFinally:
                    ctx.Code.Add((byte)OpCode.EndFinally);
                    break;
            }
        }
    }

    private static void EnterNode(Node node, Stack<EmitWork> worklist, ref EmitContext ctx, LambdaEmitState? lambdaState) {

        var replacement = ctx.Analysis?.GetNodeReplacement(node);
        if (replacement is not null && !ReferenceEquals(replacement, node)) {
            worklist.Push(new EmitWork(replacement, EmitPhase.Enter));
            return;
        }

        if (node is Constant constant) {
            int pc = ctx.Code.Count;
            ctx.SourceMap[pc] = node.Id;
            if (TryInlinableInt(constant.Value, out int intVal)) {
                ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, intVal);
            }
            else if (TryInlinableLong(constant.Value, out long longVal)) {
                ctx.Code.Add((byte)OpCode.PushLong); EmitInt64(ctx.Code, longVal);
            }
            else if (TryInlinableDouble(constant.Value, out double doubleVal)) {
                ctx.Code.Add((byte)OpCode.PushDouble); EmitDouble(ctx.Code, doubleVal);
            }
            else {
                int idx = ctx.Constants?.Count ?? 0;
                ctx.Constants?.Add(constant.Value);
                ctx.Code.Add((byte)OpCode.LoadConst); EmitInt32(ctx.Code, idx);
            }
            return;
        }

        int emitPc = ctx.Code.Count;
        ctx.SourceMap[emitPc] = node.Id;

        switch (node) {
            case Add add:
                if (ctx.Analysis?.GetResolvedType(add)?.GetRuntimeType() == typeof(string)) {
                    worklist.Push(new EmitWork(add, EmitPhase.AfterChildren, Data: -1));
                    worklist.Push(new EmitWork(add.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(add.LeftHandValue, EmitPhase.Enter));
                }
                else {
                    int op = (int)ResolveBinaryOp(add, OpCode.Add, OpCode.Add, OpCode.DAdd, ctx.Analysis);
                    worklist.Push(new EmitWork(add, EmitPhase.AfterChildren, Data: op));
                    worklist.Push(new EmitWork(add.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(add.LeftHandValue, EmitPhase.Enter));
                }
                return;

            case Subtract sub: {
                    int op = (int)ResolveBinaryOp(sub, OpCode.Sub, OpCode.Sub, OpCode.DSub, ctx.Analysis);
                    worklist.Push(new EmitWork(sub, EmitPhase.AfterChildren, Data: op));
                    worklist.Push(new EmitWork(sub.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(sub.LeftHandValue, EmitPhase.Enter));
                    return;
                }

            case Multiply mul: {
                    int op = (int)ResolveBinaryOp(mul, OpCode.Mul, OpCode.Mul, OpCode.DMul, ctx.Analysis);
                    worklist.Push(new EmitWork(mul, EmitPhase.AfterChildren, Data: op));
                    worklist.Push(new EmitWork(mul.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(mul.LeftHandValue, EmitPhase.Enter));
                    return;
                }

            case Divide div: {
                    int op = (int)ResolveBinaryOp(div, OpCode.Div, OpCode.UDiv, OpCode.DDiv, ctx.Analysis);
                    worklist.Push(new EmitWork(div, EmitPhase.AfterChildren, Data: op));
                    worklist.Push(new EmitWork(div.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(div.LeftHandValue, EmitPhase.Enter));
                    return;
                }

            case Modulo mod: {
                    int op = (int)ResolveBinaryOp(mod, OpCode.Mod, OpCode.UMod, OpCode.Mod, ctx.Analysis);
                    worklist.Push(new EmitWork(mod, EmitPhase.AfterChildren, Data: op));
                    worklist.Push(new EmitWork(mod.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(mod.LeftHandValue, EmitPhase.Enter));
                    return;
                }

            case UnaryMinus un:
                worklist.Push(new EmitWork(un, EmitPhase.AfterChildren));
                worklist.Push(new EmitWork(un.Operand, EmitPhase.Enter));
                return;

            case Equal eq: {
                    int op = (int)ResolveComparisonOp(eq.LeftHandValue, eq.RightHandValue, OpCode.Eq, OpCode.Eq, OpCode.DEq, ctx.Analysis);
                    worklist.Push(new EmitWork(eq, EmitPhase.AfterChildren, Data: op));
                    worklist.Push(new EmitWork(eq.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(eq.LeftHandValue, EmitPhase.Enter));
                    return;
                }

            case NotEqual ne: {
                    int op = (int)ResolveComparisonOp(ne.LeftHandValue, ne.RightHandValue, OpCode.Ne, OpCode.Ne, OpCode.DNe, ctx.Analysis);
                    worklist.Push(new EmitWork(ne, EmitPhase.AfterChildren, Data: op));
                    worklist.Push(new EmitWork(ne.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(ne.LeftHandValue, EmitPhase.Enter));
                    return;
                }

            case LessThan lt: {
                    int op = (int)ResolveComparisonOp(lt.LeftHandValue, lt.RightHandValue, OpCode.Lt, OpCode.ULt, OpCode.DLt, ctx.Analysis);
                    worklist.Push(new EmitWork(lt, EmitPhase.AfterChildren, Data: op));
                    worklist.Push(new EmitWork(lt.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(lt.LeftHandValue, EmitPhase.Enter));
                    return;
                }

            case LessThanOrEqual le: {
                    int op = (int)ResolveComparisonOp(le.LeftHandValue, le.RightHandValue, OpCode.Le, OpCode.ULe, OpCode.DLe, ctx.Analysis);
                    worklist.Push(new EmitWork(le, EmitPhase.AfterChildren, Data: op));
                    worklist.Push(new EmitWork(le.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(le.LeftHandValue, EmitPhase.Enter));
                    return;
                }

            case GreaterThan gt: {
                    int op = (int)ResolveComparisonOp(gt.LeftHandValue, gt.RightHandValue, OpCode.Gt, OpCode.UGt, OpCode.DGt, ctx.Analysis);
                    worklist.Push(new EmitWork(gt, EmitPhase.AfterChildren, Data: op));
                    worklist.Push(new EmitWork(gt.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(gt.LeftHandValue, EmitPhase.Enter));
                    return;
                }

            case GreaterThanOrEqual ge: {
                    int op = (int)ResolveComparisonOp(ge.LeftHandValue, ge.RightHandValue, OpCode.Ge, OpCode.UGe, OpCode.DGe, ctx.Analysis);
                    worklist.Push(new EmitWork(ge, EmitPhase.AfterChildren, Data: op));
                    worklist.Push(new EmitWork(ge.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(ge.LeftHandValue, EmitPhase.Enter));
                    return;
                }

            case And andNode:
                ctx.Labels ??= new LabelContext(); {
                    string end = ctx.Labels.Next();
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: end));
                    worklist.Push(new EmitWork(andNode.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(null, EmitPhase.Pop));
                    worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: end));
                    worklist.Push(new EmitWork(null, EmitPhase.Dup));
                    worklist.Push(new EmitWork(andNode.LeftHandValue, EmitPhase.Enter));
                }
                return;

            case Or orNode:
                ctx.Labels ??= new LabelContext(); {
                    string evalRight = ctx.Labels.Next(), after = ctx.Labels.Next();
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: after));
                    worklist.Push(new EmitWork(orNode.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(null, EmitPhase.Pop));
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: evalRight));
                    worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: after));
                    worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: evalRight));
                    worklist.Push(new EmitWork(null, EmitPhase.Dup));
                    worklist.Push(new EmitWork(orNode.LeftHandValue, EmitPhase.Enter));
                }
                return;

            case Not notNode:
                worklist.Push(new EmitWork(notNode, EmitPhase.AfterChildren));
                worklist.Push(new EmitWork(notNode.Value, EmitPhase.Enter));
                return;

            case Conditional cond:
                ctx.Labels ??= new LabelContext(); {
                    string elseL = ctx.Labels.Next(), endL = ctx.Labels.Next();
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: endL));
                    worklist.Push(new EmitWork(cond.IfFalse, EmitPhase.Enter));
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: elseL));
                    worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: endL));
                    worklist.Push(new EmitWork(cond.IfTrue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: elseL));
                    worklist.Push(new EmitWork(cond.Condition, EmitPhase.Enter));
                }
                return;

            case IfStatement ifStmt:
                ctx.Labels ??= new LabelContext(); {
                    string elseL, endL;
                    if (ifStmt.ElseBranch is not null) {
                        elseL = ctx.Labels.Next(); endL = ctx.Labels.Next();
                        worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: endL));
                        worklist.Push(new EmitWork(ifStmt.ElseBranch, EmitPhase.Enter));
                        worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: elseL));
                        worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: endL));
                        worklist.Push(new EmitWork(ifStmt.ThenBranch, EmitPhase.Enter));
                        worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: elseL));
                    }
                    else {
                        endL = ctx.Labels.Next();
                        worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: endL));
                        worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: endL));
                        worklist.Push(new EmitWork(ifStmt.ThenBranch, EmitPhase.Enter));
                        worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: endL));
                    }
                    worklist.Push(new EmitWork(ifStmt.Condition, EmitPhase.Enter));
                }
                return;

            case WhileLoop wl:
                ctx.Labels ??= new LabelContext(); {
                    string breakL = ctx.Labels.Next(), contL = ctx.Labels.Next();
                    string loopLabel = ctx.Labels.PendingLoopLabel ?? ""; ctx.Labels.PendingLoopLabel = null;
                    ctx.Labels.LoopLabels.Push((loopLabel, breakL, contL));
                    worklist.Push(new EmitWork(wl, EmitPhase.AfterChildren));
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: breakL));
                    worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: contL));
                    worklist.Push(new EmitWork(wl.Body, EmitPhase.Enter));
                    worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: breakL));
                    worklist.Push(new EmitWork(wl.Condition, EmitPhase.Enter));
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: contL));
                }
                return;

            case DoWhileLoop dwl:
                ctx.Labels ??= new LabelContext(); {
                    string breakL = ctx.Labels.Next(), contL = ctx.Labels.Next();
                    string loopLabel = ctx.Labels.PendingLoopLabel ?? ""; ctx.Labels.PendingLoopLabel = null;
                    ctx.Labels.LoopLabels.Push((loopLabel, breakL, contL));
                    worklist.Push(new EmitWork(dwl, EmitPhase.AfterChildren));
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: breakL));
                    worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: contL));
                    worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: breakL));
                    worklist.Push(new EmitWork(dwl.Condition, EmitPhase.Enter));
                    worklist.Push(new EmitWork(dwl.Body, EmitPhase.Enter));
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: contL));
                }
                return;

            case ForLoop fl:
                ctx.Labels ??= new LabelContext(); {
                    string breakL = ctx.Labels.Next(), contL = ctx.Labels.Next();
                    string loopLabel = ctx.Labels.PendingLoopLabel ?? ""; ctx.Labels.PendingLoopLabel = null;
                    ctx.Labels.LoopLabels.Push((loopLabel, breakL, contL));
                    worklist.Push(new EmitWork(fl, EmitPhase.AfterChildren));
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: breakL));
                    worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: contL));
                    if (fl.Increment is not null)
                        worklist.Push(new EmitWork(fl.Increment, EmitPhase.Enter));
                    worklist.Push(new EmitWork(fl.Body, EmitPhase.Enter));
                    if (fl.Condition is not null) {
                        worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: breakL));
                        worklist.Push(new EmitWork(fl.Condition, EmitPhase.Enter));
                    }
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: contL));
                    if (fl.Initializer is not null)
                        worklist.Push(new EmitWork(fl.Initializer, EmitPhase.Enter));
                }
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
                        Console.Error.WriteLine($"CONT: loopLabels={ctx.Labels.LoopLabels.Count} target={ctx.Labels.LoopLabels.Peek().Continue}");
                        ctx.Labels.JumpTo(ctx.Labels.LoopLabels.Peek().Continue, ctx.Code, ctx.Relocations);
                    }
                }
                else {
                    Console.Error.WriteLine("CONT: NO LOOP LABELS");
                }
                return;

            case GotoStatement got:
                ctx.Labels?.JumpTo(got.Target, ctx.Code, ctx.Relocations);
                return;

            case LabelDeclaration lbl:
                ctx.Labels?.Mark(lbl.Name, ctx.Code);
                if (ctx.Labels is not null) ctx.Labels.PendingLoopLabel = lbl.Name;
                worklist.Push(new EmitWork(lbl, EmitPhase.AfterChildren));
                worklist.Push(new EmitWork(lbl.Statement, EmitPhase.Enter));
                return;

            case SwitchStatement swt:
                ctx.Labels ??= new LabelContext(); {
                    string endL = ctx.Labels.Next();
                    var lb = ctx.Labels;
                    var caseLabels = swt.Cases.Select(_ => lb.Next()).ToList();
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: endL));
                    if (swt.DefaultCase is not null)
                        worklist.Push(new EmitWork(swt.DefaultCase, EmitPhase.Enter));
                    worklist.Push(new EmitWork(null, EmitPhase.Pop));
                    for (int i = swt.Cases.Count - 1; i >= 0; i--) {
                        worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: caseLabels[i]));
                        worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: endL));
                        worklist.Push(new EmitWork(swt.Cases[i].Body, EmitPhase.Enter));
                        worklist.Push(new EmitWork(null, EmitPhase.Pop));
                        worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: caseLabels[i]));
                        worklist.Push(new EmitWork(null, EmitPhase.Eq));
                        worklist.Push(new EmitWork(swt.Cases[i].Pattern, EmitPhase.Enter));
                        worklist.Push(new EmitWork(null, EmitPhase.Dup));
                    }
                    worklist.Push(new EmitWork(swt.Value, EmitPhase.Enter));
                }
                return;

            case ThrowStatement thr:
                worklist.Push(new EmitWork(thr, EmitPhase.AfterChildren));
                worklist.Push(new EmitWork(thr.Exception, EmitPhase.Enter));
                return;

            case Default def:
                ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 0);
                return;

            case NullForgiving nf:
                worklist.Push(new EmitWork(nf.Operand, EmitPhase.Enter));
                return;

            case Assignment assign:
                string? destName = assign.Destination switch {
                    Variable v => v.Name,
                    Parameter p => p.Name,
                    _ => null
                };
                if (destName is not null) {
                    int storeIdx;
                    int storeType;
                    if (ctx.ParamIndexMap is not null && ctx.ParamIndexMap.TryGetValue(destName, out int pIdx)) {
                        storeIdx = pIdx; storeType = 0;
                    }
                    else if (ctx.LocalIndexMap is not null && ctx.LocalIndexMap.TryGetValue(destName, out int lIdx)) {
                        storeIdx = lIdx; storeType = 1;
                    }
                    else if (lambdaState?.UpvalueMap is not null && lambdaState.UpvalueMap.TryGetValue(destName, out int uIdx)) {
                        storeIdx = uIdx; storeType = 2;
                    }
                    else {
                        storeIdx = -1; storeType = -1;
                    }
                    if (storeIdx >= 0) {
                        worklist.Push(new EmitWork(assign, EmitPhase.AfterChildren, Data: storeIdx | (storeType << 16)));
                        worklist.Push(new EmitWork(assign.Value, EmitPhase.Enter));
                        return;
                    }
                }
                if (assign.Destination is Member memberDest) {
                    EmitAssignmentMember(memberDest, assign.Value, worklist, ref ctx, lambdaState);
                    return;
                }
                if (assign.Destination is IndexAccess idxDest) {
                    EmitAssignmentIndexAccess(idxDest, assign.Value, worklist, ref ctx, lambdaState);
                    return;
                }
                return;

            case Coalesce coalesce:
                ctx.Labels ??= new LabelContext(); {
                    string after = ctx.Labels.Next();
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: after));
                    worklist.Push(new EmitWork(coalesce.RightHandValue, EmitPhase.Enter));
                    worklist.Push(new EmitWork(null, EmitPhase.Pop));
                    worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: after));
                    worklist.Push(new EmitWork(null, EmitPhase.IsNull));
                    worklist.Push(new EmitWork(null, EmitPhase.Dup));
                    worklist.Push(new EmitWork(coalesce.LeftHandValue, EmitPhase.Enter));
                }
                return;

            case Member member:
                EmitMember(member, worklist, ref ctx);
                return;

            case IndexAccess idxAccess:
                EmitIndexAccess(idxAccess, worklist, ref ctx);
                return;

            case New newExpr:
                EmitNew(newExpr, worklist, ref ctx);
                return;

            case TypeCast tc:
                worklist.Push(new EmitWork(tc.Operand, EmitPhase.Enter));
                return;

            case TypeIs ti:
                worklist.Push(new EmitWork(ti, EmitPhase.AfterChildren));
                worklist.Push(new EmitWork(ti.Operand, EmitPhase.Enter));
                return;

            case TypeAs:
            case ParameterReference:
            case ThisReference:
                return;

            case Parameter param:
                if (param.Name is not null) {
                    if (ctx.ParamIndexMap is not null && ctx.ParamIndexMap.TryGetValue(param.Name, out int pIdx)) {
                        ctx.Code.Add((byte)OpCode.LoadArg); EmitInt32(ctx.Code, pIdx);
                        return;
                    }
                    if (ctx.LocalIndexMap is not null && ctx.LocalIndexMap.TryGetValue(param.Name, out int lIdx)) {
                        ctx.Code.Add((byte)OpCode.LoadLocal); EmitInt32(ctx.Code, lIdx);
                        return;
                    }
                    if (param.DefaultValue is not null) {
                        worklist.Push(new EmitWork(param.DefaultValue, EmitPhase.Enter));
                        return;
                    }
                }
                return;

            case Invoke invoke:
                EmitInvoke(invoke, worklist, ref ctx, lambdaState);
                return;

            case Block block:
                for (int i = block.Nodes.Count - 1; i >= 0; i--) {
                    if (i < block.Nodes.Count - 1 && ctx.Analysis is not null && ctx.Analysis.CanElide(block.Nodes[i]))
                        continue; // pure expression with unused result — skip entirely
                    if (i < block.Nodes.Count - 1 && EmitsValue(block.Nodes[i], ctx.Analysis))
                        worklist.Push(new EmitWork(null, EmitPhase.Pop));
                    worklist.Push(new EmitWork(block.Nodes[i], EmitPhase.Enter));
                }
                return;

            case Return retNode:
                if (retNode.Value is not null)
                    worklist.Push(new EmitWork(retNode.Value, EmitPhase.Enter));
                return;

            case Variable variable:
                if (variable.Name is null) return;
                if (ctx.ParamIndexMap is not null && ctx.ParamIndexMap.TryGetValue(variable.Name, out int paramIdx)) {
                    ctx.Code.Add((byte)OpCode.LoadArg); EmitInt32(ctx.Code, paramIdx);
                }
                else if (ctx.LocalIndexMap is not null && ctx.LocalIndexMap.TryGetValue(variable.Name, out int localIdx)) {
                    ctx.Code.Add((byte)OpCode.LoadLocal); EmitInt32(ctx.Code, localIdx);
                }
                else if (lambdaState?.UpvalueMap is not null && lambdaState.UpvalueMap.TryGetValue(variable.Name, out int upIdx)) {
                    ctx.Code.Add((byte)OpCode.LoadUpvalue); EmitInt32(ctx.Code, upIdx);
                }
                return;

            case SuspendNode sn:
                worklist.Push(new EmitWork(sn, EmitPhase.AfterChildren));
                worklist.Push(new EmitWork(sn.Inner, EmitPhase.Enter));
                return;

            case Lambda lambda:
                if (lambdaState?.FuncMap is not null && lambdaState.FuncMap.TryGetValue(lambda, out int lambdaFuncIdx)) {
                    List<string>? caps = null;
                    bool found = lambdaState.CaptureMap is not null && lambdaState.CaptureMap.TryGetValue(lambda, out caps);
                    var captures = found && caps is not null ? caps : [];
                    for (int i = captures.Count - 1; i >= 0; i--) {
                        if (ctx.ParamIndexMap is not null && ctx.ParamIndexMap.TryGetValue(captures[i], out int pIdx)) {
                            ctx.Code.Add((byte)OpCode.LoadArg); EmitInt32(ctx.Code, pIdx);
                        }
                        else if (ctx.LocalIndexMap is not null && ctx.LocalIndexMap.TryGetValue(captures[i], out int localIdx)) {
                            ctx.Code.Add((byte)OpCode.LoadLocal); EmitInt32(ctx.Code, localIdx);
                        }
                        else {
                            ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 0);
                        }
                    }
                    ctx.Code.Add((byte)OpCode.AllocateClosure); EmitInt32(ctx.Code, lambdaFuncIdx); EmitInt32(ctx.Code, captures.Count);
                }
                return;

            case Await awaitNode:
                worklist.Push(new EmitWork(awaitNode, EmitPhase.AfterChildren));
                worklist.Push(new EmitWork(awaitNode.Operand, EmitPhase.Enter));
                return;

            case TypeDefinitionNode:
                return;

            case ForEachLoop fe:
                EmitForEachLoop(fe, worklist, ref ctx);
                return;

            case UsingStatement us:
                EmitUsingStatement(us, worklist, ref ctx);
                return;

            case TryCatchFinally tcf: {
                    ctx.Labels ??= new LabelContext();
                    int tryStart = ctx.Code.Count;
                    string catchStartL = ctx.Labels.Next();
                    string? finallyEntryL = tcf.FinallyBlock is not null ? ctx.Labels.Next() : null;
                    string endL = ctx.Labels.Next();
                    worklist.Push(new EmitWork(tcf, EmitPhase.AfterChildren,
                        Data: tryStart, Label: catchStartL, Label2: finallyEntryL ?? endL));
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: endL));
                    if (tcf.FinallyBlock is not null) {
                        worklist.Push(new EmitWork(null, EmitPhase.EndFinally));
                        if (EmitsValue(tcf.FinallyBlock, ctx.Analysis))
                            worklist.Push(new EmitWork(null, EmitPhase.Pop));
                        worklist.Push(new EmitWork(tcf.FinallyBlock, EmitPhase.Enter));
                        worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: finallyEntryL!));
                    }
                    if (tcf.CatchClauses is not null) {
                        for (int i = tcf.CatchClauses.Count - 1; i >= 0; i--) {
                            var cc = tcf.CatchClauses[i];
                            if (tcf.FinallyBlock is null)
                                worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: endL));
                            worklist.Push(new EmitWork(cc.Body, EmitPhase.Enter));
                            if (cc.VariableName is not null
                                && ctx.ParamIndexMap is not null
                                && ctx.ParamIndexMap.TryGetValue(cc.VariableName, out int varIdx))
                                worklist.Push(new EmitWork(null, EmitPhase.Pop, Data: varIdx));
                            else
                                worklist.Push(new EmitWork(null, EmitPhase.Pop, Data: -1));
                        }
                    }
                    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: catchStartL));
                    worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: finallyEntryL ?? endL));
                    worklist.Push(new EmitWork(tcf.TryBlock, EmitPhase.Enter));
                    return;
                }

            default:
                return;
        }
    }

    private static void AfterChildren(Node node, EmitWork work, Stack<EmitWork> worklist, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        switch (node) {
            case Add add:
                if (work.Data == -1) {
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 2);
                    ctx.Code.Add((byte)OpCode.StrConcat);
                }
                else {
                    ctx.Code.Add((byte)(OpCode)work.Data);
                }
                return;

            case Subtract _:
            case Multiply _:
            case Divide _:
            case Modulo _:
            case Equal _:
            case NotEqual _:
            case LessThan _:
            case LessThanOrEqual _:
            case GreaterThan _:
            case GreaterThanOrEqual _:
                ctx.Code.Add((byte)(OpCode)work.Data);
                return;

            case UnaryMinus un: {
                    var typeDef = ctx.Analysis?.GetResolvedType(un);
                    var clrType = typeDef?.GetRuntimeType();
                    if (clrType == typeof(double) || clrType == typeof(float))
                        ctx.Code.Add((byte)OpCode.DNeg);
                    else
                        ctx.Code.Add((byte)OpCode.Neg);
                }
                return;

            case Not _:
                ctx.Code.Add((byte)OpCode.Not);
                return;

            case WhileLoop _:
            case DoWhileLoop _:
            case ForLoop _:
                ctx.Labels!.LoopLabels.Pop();
                return;

            case Assignment assign:
                int storeIdx = work.Data & 0xFFFF;
                int storeType = (work.Data >> 16) & 0xFFFF;
                ctx.Code.Add((byte)OpCode.Dup);
                switch (storeType) {
                    case 0: ctx.Code.Add((byte)OpCode.StoreArg); break;
                    case 1: ctx.Code.Add((byte)OpCode.StoreLocal); break;
                    case 2: ctx.Code.Add((byte)OpCode.StoreUpvalue); break;
                }
                EmitInt32(ctx.Code, storeIdx);
                return;

            case LabelDeclaration _:
                if (ctx.Labels is not null) ctx.Labels.PendingLoopLabel = null;
                return;

            case SuspendNode _:
                ctx.Code.Add((byte)OpCode.Pop);
                ctx.Code.Add((byte)OpCode.Int);
                EmitInt32(ctx.Code, 0);
                return;

            case Await _: {
                    int awaitSiteIdx = ctx.CallSites?.Count ?? 0;
                    ctx.CallSites?.Add(AwaitResultDelegate);
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
                    ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, awaitSiteIdx);
                }
                return;

            case ThrowStatement _:
                ctx.Code.Add((byte)OpCode.Throw);
                return;

            case TypeIs ti: {
                    Type? targetType = ctx.Analysis?.GetResolvedType(ti.TargetTypeReference)?.GetRuntimeType();
                    int typeCheckIdx = ctx.CallSites?.Count ?? 0;
                    ctx.CallSites?.Add(targetType is not null ? CreateTypeIsDelegate(targetType) : IsNotNullDelegate);
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
                    ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, typeCheckIdx);
                }
                return;

            case Lambda lambda:
                if (lambdaState?.FuncMap is not null && lambdaState.FuncMap.TryGetValue(lambda, out int lambdaFuncIdx)) {
                    var captures = lambdaState.CaptureMap is not null && lambdaState.CaptureMap.TryGetValue(lambda, out var caps) ? caps : [];
                    ctx.Code.Add((byte)OpCode.AllocateClosure); EmitInt32(ctx.Code, lambdaFuncIdx); EmitInt32(ctx.Code, captures.Count);
                }
                return;

            case TryCatchFinally tcf: {
                    int tryStart = work.Data;
                    int tryEnd;
                    int catchStart = -1;
                    if (ctx.Labels!.Targets.TryGetValue(work.Label!, out int csPos)) {
                        tryEnd = csPos - 5;
                        if (tcf.CatchClauses is { Count: > 0 })
                            catchStart = csPos;
                    }
                    else {
                        tryEnd = ctx.Code.Count;
                    }
                    int? finallyStart = tcf.FinallyBlock is not null && work.Label2 is not null
                        && ctx.Labels.Targets.TryGetValue(work.Label2, out int fs) ? fs : null;
                    ctx.Labels.ExceptionRegions.Add(new ExceptionRegion(tryStart, tryEnd, catchStart, finallyStart));
                }
                return;

            case Member member:
                EmitCallSiteAfterChildren(member, work, ref ctx);
                return;

            case IndexAccess idx:
                EmitCallSiteAfterChildren(idx, work, ref ctx);
                return;

            case New newExpr:
                EmitCallSiteAfterChildren(newExpr, work, ref ctx);
                return;

            case Invoke invoke:
                EmitInvokeAfterChildren(invoke, work, ref ctx);
                return;

            case ForEachLoop _: {
                    int bt = work.Data & 0xF;
                    if (bt is >= 0 and <= 6) {
                        switch (bt) {
                            case 0:
                                ctx.Code.Add((byte)OpCode.LoadConst); EmitInt32(ctx.Code, (work.Data >> 4) & 0x3FF);
                                break;
                            case 1:
                                ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 2);
                                ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 0);
                                ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, (work.Data >> 4) & 0x3FF);
                                break;
                            case 2:
                                ctx.Code.Add((byte)OpCode.LoadConst); EmitInt32(ctx.Code, (work.Data >> 4) & 0x3FF);
                                ctx.Code.Add((byte)OpCode.EnumeratorMoveNext);
                                break;
                            case 3:
                                ctx.Code.Add((byte)OpCode.LoadConst); EmitInt32(ctx.Code, (work.Data >> 4) & 0x3FF);
                                ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
                                ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
                                ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, (work.Data >> 14) & 0x3FF);
                                break;
                            case 4:
                                if (((work.Data >> 30) & 1) == 1) {
                                    ctx.Code.Add((byte)OpCode.Pop);
                                }
                                else {
                                    int storeIdxFe = (work.Data >> 4) & 0xFFFF;
                                    int storeTypeFe = (work.Data >> 20) & 3;
                                    switch (storeTypeFe) {
                                        case 0: ctx.Code.Add((byte)OpCode.StoreArg); break;
                                        case 1: ctx.Code.Add((byte)OpCode.StoreLocal); break;
                                        default: ctx.Code.Add((byte)OpCode.Pop); break;
                                    }
                                    if (storeTypeFe is 0 or 1)
                                        EmitInt32(ctx.Code, storeIdxFe);
                                }
                                break;
                            case 5:
                                ctx.Code.Add((byte)OpCode.LoadConst); EmitInt32(ctx.Code, (work.Data >> 4) & 0x3FF);
                                ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
                                ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 0);
                                ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, (work.Data >> 14) & 0x3FF);
                                break;
                            case 6:
                                ctx.Labels!.LoopLabels.Pop();
                                break;
                        }
                    }
                }
                return;

            case UsingStatement us:
                if (work.Label is not null) {
                    int tryStartUs = work.Data;
                    int tryEndUs = ctx.Labels!.Targets[work.Label];
                    int? finallyStartUs = work.Label2 is not null ? ctx.Labels.Targets[work.Label2] : null;
                    ctx.Labels.ExceptionRegions.Add(new ExceptionRegion(tryStartUs, tryEndUs, -1, finallyStartUs));
                }
                else {
                    int val = work.Data;
                    if ((val & 0x10000) != 0) {
                        ctx.Code.Add((byte)OpCode.LoadConst); EmitInt32(ctx.Code, (val >> 4) & 0x3FF);
                        ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 1);
                        ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 0);
                        ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, (val >> 20) & 0x3FF);
                        ctx.Code.Add((byte)OpCode.EndFinally);
                    }
                    else if ((val & 0x20000) != 0) {
                        ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 2);
                        ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 0);
                        ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, (val >> 4) & 0x3FF);
                    }
                    else if ((val & 0x40000) != 0) {
                        ctx.Code.Add((byte)OpCode.LoadConst); EmitInt32(ctx.Code, (val >> 4) & 0x3FF);
                    }
                }
                return;

            default:
                if (work.Data == 1) {
                    ctx.Code.Add((byte)OpCode.EndFinally);
                }
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

    private static void EmitForEachLoop(ForEachLoop fe, Stack<EmitWork> worklist, ref EmitContext ctx) {
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

        int initEnumIdx = ctx.CallSites?.Count ?? 0;
        ctx.CallSites?.Add(InitEnumeratorDelegate);
        int getCurrIdx = ctx.CallSites?.Count ?? 0;
        ctx.CallSites?.Add(GetCurrentDelegate);
        int disposeEnumIdx = ctx.CallSites?.Count ?? 0;
        ctx.CallSites?.Add(DisposeEnumeratorDelegate);

        // Encode loop variable store info
        int storeIdx = 0, storeType = 0;
        if (fe.LoopVariable is { Name: not null } lv) {
            if (ctx.ParamIndexMap is not null && ctx.ParamIndexMap.TryGetValue(lv.Name, out int pi)) {
                storeIdx = pi; storeType = 0;
            }
            else if (ctx.LocalIndexMap is not null && ctx.LocalIndexMap.TryGetValue(lv.Name, out int li)) {
                storeIdx = li; storeType = 1;
            }
            else {
                storeIdx = 0; storeType = -1;
            }
        }
        else {
            storeType = -1;
        }

        worklist.Push(new EmitWork(fe, EmitPhase.AfterChildren, Data: 6)); // pop labels
        // dispose
        worklist.Push(new EmitWork(fe, EmitPhase.AfterChildren,
            Data: 5 | (holderIdx << 4) | (disposeEnumIdx << 14)));
        worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: breakL));
        worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: contL));
        if (EmitsValue(fe.Body, ctx.Analysis))
            worklist.Push(new EmitWork(null, EmitPhase.Pop));
        worklist.Push(new EmitWork(fe.Body, EmitPhase.Enter));
        // store loop var
        if (storeType == -1) {
            worklist.Push(new EmitWork(fe, EmitPhase.AfterChildren, Data: 4 | (1 << 30))); // Pop only
        }
        else {
            worklist.Push(new EmitWork(fe, EmitPhase.AfterChildren,
                Data: 4 | (storeIdx << 4) | (storeType << 20)));
        }
        // getCurrent
        worklist.Push(new EmitWork(fe, EmitPhase.AfterChildren,
            Data: 3 | (holderIdx << 4) | (getCurrIdx << 14)));
        worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: breakL));
        // moveNext
        worklist.Push(new EmitWork(fe, EmitPhase.AfterChildren, Data: 2 | (holderIdx << 4)));
        worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: contL));
        // initAfter
        worklist.Push(new EmitWork(fe, EmitPhase.AfterChildren, Data: 1 | (initEnumIdx << 4)));
        worklist.Push(new EmitWork(fe.Collection, EmitPhase.Enter));
        // initBefore
        worklist.Push(new EmitWork(fe, EmitPhase.AfterChildren, Data: 0 | (holderIdx << 4)));
    }

    private static void EmitUsingStatement(UsingStatement us, Stack<EmitWork> worklist, ref EmitContext ctx) {
        var resourceHolder = new object[1];
        int holderIdx = ctx.Constants?.Count ?? 0;
        ctx.Constants?.Add(resourceHolder);

        ctx.Labels ??= new LabelContext();
        var labels = ctx.Labels;
        int tryStart = ctx.Code.Count;
        string tryEndL = labels.Next();
        string finallyStartL = labels.Next();

        int saveIdx = ctx.CallSites?.Count ?? 0;
        ctx.CallSites?.Add(SaveResourceDelegate);
        int disposeIdx = ctx.CallSites?.Count ?? 0;
        ctx.CallSites?.Add(DisposeResourceDelegate);

        worklist.Push(new EmitWork(us, EmitPhase.AfterChildren,
            Data: tryStart, Label: tryEndL, Label2: finallyStartL));
        // dispose + EndFinally
        worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: finallyStartL));
        worklist.Push(new EmitWork(us, EmitPhase.AfterChildren,
            Data: 0x10000 | (holderIdx << 4) | (disposeIdx << 20)));
        worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: tryEndL));
        worklist.Push(new EmitWork(us.Body, EmitPhase.Enter));
        // call save
        worklist.Push(new EmitWork(us, EmitPhase.AfterChildren, Data: 0x20000 | (saveIdx << 4)));
        worklist.Push(new EmitWork(us.Resource, EmitPhase.Enter));
        // loadConst holderIdx
        worklist.Push(new EmitWork(us, EmitPhase.AfterChildren, Data: 0x40000 | (holderIdx << 4)));
    }

    private static void EmitCallSiteAfterChildren(Node node, EmitWork work, ref EmitContext ctx) {
        int siteIdx = work.Data & 0xFFFFF;
        int argCount = (work.Data >> 20) & 0xFF;
        int hasRet = (work.Data >> 28) & 1;
        ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, argCount);
        ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, hasRet);
        ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, siteIdx);
    }

    private static void EmitInvokeAfterChildren(Invoke invoke, EmitWork work, ref EmitContext ctx) {
        int path = (work.Data >> 28) & 3;
        switch (path) {
            case 0: { // AstMethod
                    int funcIndex = work.Data & 0xFFFFF;
                    int paramCount = (work.Data >> 20) & 0xFF;
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, paramCount);
                    ctx.Code.Add((byte)OpCode.Call); EmitInt32(ctx.Code, funcIndex);
                    ctx.SourceMap[ctx.Code.Count - 5] = invoke.Id;
                    break;
                }
            case 1: { // ClrMethod
                    int siteIndex = work.Data & 0xFFFFF;
                    int argCount = (work.Data >> 20) & 0xFF;
                    int hasRet = (work.Data >> 30) & 1;
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, argCount);
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, hasRet);
                    ctx.Code.Add((byte)OpCode.CallExternal); EmitInt32(ctx.Code, siteIndex);
                    break;
                }
            case 2: { // Lambda
                    int lFuncIdx = work.Data & 0xFFFFF;
                    int totalArgs = (work.Data >> 20) & 0xFF;
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, totalArgs);
                    ctx.Code.Add((byte)OpCode.Call); EmitInt32(ctx.Code, lFuncIdx);
                    ctx.SourceMap[ctx.Code.Count - 5] = invoke.Id;
                    break;
                }
            case 3: { // Generic delegate
                    int totalArgs = work.Data & 0xFFFFF;
                    ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, totalArgs);
                    ctx.Code.Add((byte)OpCode.CallClosure);
                    ctx.SourceMap[ctx.Code.Count - 5] = invoke.Id;
                    break;
                }
        }
    }

    private static void EmitInvoke(Invoke invoke, Stack<EmitWork> worklist, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        // AstMethod path
        if (ctx.Analysis?.GetResolvedMember(invoke) is AstMethodDefinition astMethod) {
            var methodDef = astMethod.DefinitionNode;
            if (ctx.FunctionIndexMap is not null && ctx.FunctionIndexMap.TryGetValue(methodDef, out int funcIndex)) {
                int paramCount = methodDef.Parameters?.Count ?? 0;
                worklist.Push(new EmitWork(invoke, EmitPhase.AfterChildren,
                    Data: funcIndex | (paramCount << 20) | (0 << 28)));
                for (int i = invoke.Arguments.Length - 1; i >= 0; i--)
                    worklist.Push(new EmitWork(invoke.Arguments[i], EmitPhase.Enter));
                return;
            }
        }

        // ClrMethod path
        if (ctx.Analysis?.GetResolvedMember(invoke) is ClrMethod clrMethod) {
            var methodInfo = clrMethod.MethodInfo;
            bool isStatic = clrMethod.LifetimeModifier == LifetimeModifier.Static;
            int siteIndex = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.Compile(methodInfo, isStatic));
            int argCount = invoke.Arguments.Length + (isStatic ? 0 : 1);
            int hasRet = methodInfo.ReturnType != typeof(void) ? 1 : 0;
            worklist.Push(new EmitWork(invoke, EmitPhase.AfterChildren,
                Data: siteIndex | (argCount << 20) | (hasRet << 30) | (1 << 28)));
            for (int i = invoke.Arguments.Length - 1; i >= 0; i--)
                worklist.Push(new EmitWork(invoke.Arguments[i], EmitPhase.Enter));
            if (!isStatic && invoke.Delegate is Member memberAccess)
                worklist.Push(new EmitWork(memberAccess.Value, EmitPhase.Enter));
            return;
        }

        // Lambda path
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
            int totalArgs = (lambdaTarget.Parameters?.Count ?? 0) + 1;
            worklist.Push(new EmitWork(invoke, EmitPhase.AfterChildren,
                Data: lFuncIdx | (totalArgs << 20) | (2 << 28)));
            for (int i = invoke.Arguments.Length - 1; i >= 0; i--)
                worklist.Push(new EmitWork(invoke.Arguments[i], EmitPhase.Enter));
            worklist.Push(new EmitWork(lambdaTarget, EmitPhase.Enter));
            return;
        }

        // Generic delegate path
        if (invoke.Delegate is not null) {
            int totalArgs = invoke.Arguments.Length + 1;
            worklist.Push(new EmitWork(invoke, EmitPhase.AfterChildren,
                Data: totalArgs | (3 << 28)));
            for (int i = invoke.Arguments.Length - 1; i >= 0; i--)
                worklist.Push(new EmitWork(invoke.Arguments[i], EmitPhase.Enter));
            worklist.Push(new EmitWork(invoke.Delegate, EmitPhase.Enter));
        }
    }

    private static void EmitMember(Member member, Stack<EmitWork> worklist, ref EmitContext ctx) {
        var resolved = ctx.Analysis?.GetResolvedMember(member);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var getter = pi.GetGetMethod(nonPublic: true);
            if (getter is null) return;
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.Compile(getter, isStatic));
            int argCount = isStatic ? 0 : 1;
            worklist.Push(new EmitWork(member, EmitPhase.AfterChildren,
                Data: siteIdx | (argCount << 20) | (1 << 28)));
            if (!isStatic)
                worklist.Push(new EmitWork(member.Value, EmitPhase.Enter));
            return;
        }
        if (resolved is ClrTypeField { FieldInfo: var fi, LifetimeModifier: var lm2 }) {
            bool isStatic = lm2 == LifetimeModifier.Static;
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.CompileFieldGetter(fi, isStatic));
            int argCount = isStatic ? 0 : 1;
            worklist.Push(new EmitWork(member, EmitPhase.AfterChildren,
                Data: siteIdx | (argCount << 20) | (1 << 28)));
            if (!isStatic)
                worklist.Push(new EmitWork(member.Value, EmitPhase.Enter));
            return;
        }
    }

    private static void EmitIndexAccess(IndexAccess idx, Stack<EmitWork> worklist, ref EmitContext ctx) {
        var resolved = ctx.Analysis?.GetResolvedMember(idx);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var getter = pi.GetGetMethod(nonPublic: true);
            if (getter is null) return;
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.Compile(getter, isStatic));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length;
            worklist.Push(new EmitWork(idx, EmitPhase.AfterChildren,
                Data: siteIdx | (argCount << 20) | (1 << 28)));
            for (int i = idx.Arguments.Length - 1; i >= 0; i--)
                worklist.Push(new EmitWork(idx.Arguments[i], EmitPhase.Enter));
            if (!isStatic)
                worklist.Push(new EmitWork(idx.Value, EmitPhase.Enter));
            return;
        }
        if (resolved is ClrTypeSyntheticProperty { Read: not null, LifetimeModifier: var lm2 }) {
            var synReader = ((ClrTypeSyntheticProperty)resolved).Read!;
            bool isStatic = lm2 == LifetimeModifier.Static;
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CompileSyntheticGetter(synReader, isStatic, idx.Arguments.Length));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length;
            worklist.Push(new EmitWork(idx, EmitPhase.AfterChildren,
                Data: siteIdx | (argCount << 20) | (1 << 28)));
            for (int i = idx.Arguments.Length - 1; i >= 0; i--)
                worklist.Push(new EmitWork(idx.Arguments[i], EmitPhase.Enter));
            if (!isStatic)
                worklist.Push(new EmitWork(idx.Value, EmitPhase.Enter));
            return;
        }
    }

    private static void EmitAssignmentMember(Member member, Node value, Stack<EmitWork> worklist, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis?.GetResolvedMember(member);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var setter = pi.GetSetMethod(nonPublic: true);
            if (setter is null) return;
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.Compile(setter, isStatic));
            int argCount = (isStatic ? 0 : 1) + 1;
            worklist.Push(new EmitWork(member, EmitPhase.AfterChildren,
                Data: siteIdx | (argCount << 20) | (0 << 28)));
            worklist.Push(new EmitWork(value, EmitPhase.Enter));
            if (!isStatic)
                worklist.Push(new EmitWork(member.Value, EmitPhase.Enter));
            return;
        }
        if (resolved is ClrTypeField { FieldInfo: var fi, LifetimeModifier: var lm2 }) {
            bool isStatic = lm2 == LifetimeModifier.Static;
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.CompileFieldSetter(fi, isStatic));
            int argCount = (isStatic ? 0 : 1) + 1;
            worklist.Push(new EmitWork(member, EmitPhase.AfterChildren,
                Data: siteIdx | (argCount << 20) | (0 << 28)));
            worklist.Push(new EmitWork(value, EmitPhase.Enter));
            if (!isStatic)
                worklist.Push(new EmitWork(member.Value, EmitPhase.Enter));
            return;
        }
    }

    private static void EmitAssignmentIndexAccess(IndexAccess idx, Node value, Stack<EmitWork> worklist, ref EmitContext ctx, LambdaEmitState? lambdaState) {
        var resolved = ctx.Analysis?.GetResolvedMember(idx);
        if (resolved is ClrTypeProperty { PropertyInfo: var pi, LifetimeModifier: var lm }) {
            bool isStatic = lm == LifetimeModifier.Static;
            var setter = pi.GetSetMethod(nonPublic: true);
            if (setter is null) return;
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.Compile(setter, isStatic));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length + 1;
            worklist.Push(new EmitWork(idx, EmitPhase.AfterChildren,
                Data: siteIdx | (argCount << 20) | (0 << 28)));
            worklist.Push(new EmitWork(value, EmitPhase.Enter));
            for (int i = idx.Arguments.Length - 1; i >= 0; i--)
                worklist.Push(new EmitWork(idx.Arguments[i], EmitPhase.Enter));
            if (!isStatic)
                worklist.Push(new EmitWork(idx.Value, EmitPhase.Enter));
            return;
        }
        if (resolved is ClrTypeSyntheticProperty { Write: not null, LifetimeModifier: var lm2 }) {
            var synWriter = ((ClrTypeSyntheticProperty)resolved).Write!;
            bool isStatic = lm2 == LifetimeModifier.Static;
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CompileSyntheticSetter(synWriter, isStatic, idx.Arguments.Length + 1));
            int argCount = (isStatic ? 0 : 1) + idx.Arguments.Length + 1;
            worklist.Push(new EmitWork(idx, EmitPhase.AfterChildren,
                Data: siteIdx | (argCount << 20) | (0 << 28)));
            worklist.Push(new EmitWork(value, EmitPhase.Enter));
            for (int i = idx.Arguments.Length - 1; i >= 0; i--)
                worklist.Push(new EmitWork(idx.Arguments[i], EmitPhase.Enter));
            if (!isStatic)
                worklist.Push(new EmitWork(idx.Value, EmitPhase.Enter));
            return;
        }
    }

    private static void EmitNew(New newExpr, Stack<EmitWork> worklist, ref EmitContext ctx) {
        var resolved = ctx.Analysis?.GetResolvedMember(newExpr);
        if (resolved is ClrConstructor { ConstructorInfo: var ci }) {
            int siteIdx = ctx.CallSites?.Count ?? 0;
            ctx.CallSites?.Add(CallSiteCompiler.CompileConstructor(ci));
            worklist.Push(new EmitWork(newExpr, EmitPhase.AfterChildren,
                Data: siteIdx | (newExpr.Arguments.Length << 20) | (1 << 28)));
            for (int i = newExpr.Arguments.Length - 1; i >= 0; i--)
                worklist.Push(new EmitWork(newExpr.Arguments[i], EmitPhase.Enter));
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
                owner = Vm.ResolveHeapValue(state, handle);
                off++;
            }
            var args = new object?[argCount];
            for (int i = 0; i < argCount; i++) {
                int handle = state.Stack.AsSpan()[baseOff + off + i];
                args[i] = Vm.ResolveHeapValue(state, handle);
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
                owner = Vm.ResolveHeapValue(state, handle);
                off++;
            }
            int indexArgCount = argCount - 1;
            var args = new object?[indexArgCount];
            for (int i = 0; i < indexArgCount; i++) {
                int handle = state.Stack.AsSpan()[baseOff + off + i];
                args[i] = Vm.ResolveHeapValue(state, handle);
            }
            int valueHandle = state.Stack.AsSpan()[baseOff + off + indexArgCount];
            object? value = Vm.IsValidHeapHandle(state, valueHandle) ? state.Heap.Get(valueHandle) : (object?)valueHandle;
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