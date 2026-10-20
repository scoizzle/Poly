using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

/// <summary>Direct AST-to-VM-ABI emitter — the primary (and sole) compilation path.
/// Walks the analyzed AST and emits <see cref="Expression"/> trees targeting the
/// bespoke VM ABI (<see cref="VmState"/>, ring registers, 2-word frame model,
/// heap). No intermediate primitive flattening or expansion step exists.</summary>
/// <remarks>
/// <para>This is the canonical lowering path described in
/// <see href="../../../../docs/decisions/2026-07-04-primitives-as-canonical-ir.md">primitives-as-canonical-ir.md</see>.
/// The emitter produces a single <c>Action&lt;VmState&gt;</c> delegate per program.
/// All control flow (loops, branches, try/catch/finally) uses native CLR Expression
/// nodes — no side tables or handler dispatching.</para>
///
/// <para>Key design points:</para>
/// <list type="bullet">
///   <item><b>Ring registers</b> — Values flow through <c>_r0.._rN</c> locals
///   allocated inline during the AST walk. No global pre-pass (RingAllocator).</item>
///   <item><b>Structured EH</b> — Uses native <c>Expression.TryCatchFinally</c>
///   directly for TryCatchFinally nodes.</item>
///   <item><b>Frame model</b> — 2-word header (previousFP + savedSP) with
///   compile-time-known argument/local counts.</item>
///   <item><b>Debug modes</b> — <see cref="CompilationMode.Normal"/> includes
///   DebugHook, PC tracking, and loop-tick guards; <see cref="CompilationMode.NoDebug"/> omits
///   them for maximum speed.</item>
/// </list>
/// </remarks>
public static partial class DirectVmAbiEmitter {
    /// <summary>
    /// Describes a captured binding. Analysis records free bindings per
    /// <see cref="Lambda"/>; stored closures share a heap <c>long[1]</c>
    /// cell with the enclosing frame so later writes are visible.
    /// </summary>
    private sealed record Capture(Variable? Variable, Parameter? Parameter) {
        public object Binding => (object?)Variable ?? Parameter!;
    }

    // Cached reflection lookups
    private static readonly MethodInfo HeapUnsafeGet = Ref<Heap>.Method(h => h.UnsafeGet(0));
    private static readonly MethodInfo HeapAllocate = Ref<Heap>.Method(h => h.Allocate(default!));
    private static readonly ConstructorInfo AstDictionaryCtor =
        typeof(Dictionary<string, object?>).GetConstructor([typeof(IEqualityComparer<string>)])!;
    private static readonly MethodInfo ObjectEquals = Ref.Method(
        (Expression<Func<object?, object?, bool>>)((a, b) => object.Equals(a, b)));

    private static readonly MethodInfo VmHeapCompare = Ref.Method(
        (Expression<Func<object?, object?, int>>)((a, b) => VmHeapComparison.Compare(a, b)));
    private static readonly MethodInfo BitOperationsPopCount = Ref.Method(
        (Expression<Func<ulong, int>>)(v => System.Numerics.BitOperations.PopCount(v)));
    private static readonly MethodInfo IDisposableDispose =
        Ref<IDisposable>.Method(d => d.Dispose());
    private static readonly ConstructorInfo InvalidOperationExceptionStringCtor =
        Ref.Constructor(() => new InvalidOperationException(""));
    private static readonly MethodInfo GetEnumeratorOrThrowInfo =
        Ref.Method((Expression<Func<object?, System.Collections.IEnumerator>>)(c => GetEnumeratorOrThrow(c)));
    private static readonly MethodInfo EnumeratorMoveNext =
        Ref<System.Collections.IEnumerator>.Method(e => e.MoveNext());
    private static readonly PropertyInfo EnumeratorCurrent =
        Ref<System.Collections.IEnumerator>.Property(e => e.Current);
    private static readonly MethodInfo BoxToAbiInfo =
        Ref.Method((Expression<Func<Heap, object?, long>>)((h, v) => RingCodec.BoxToAbi(h, v)));
    private static readonly MethodInfo CastAbiInfo =
        Ref.Method((Expression<Func<Heap, long, Type, long>>)((h, r, t) => RingCodec.Cast(h, r, t)));
    private static readonly MethodInfo TypeAsAbiInfo =
        Ref.Method((Expression<Func<Heap, long, Type, long>>)((h, r, t) => RingCodec.TypeAs(h, r, t)));
    private static readonly MethodInfo UnboxNullableToLongInfo =
        Ref.Method((Expression<Func<Heap, long, long>>)((h, v) => RingCodec.UnboxNullable(h, v)));
    // Expression<Func<T>> cannot close over a ref struct (CS9244).
    private static readonly ConstructorInfo ReadOnlySpanLongArrayCtor =
        typeof(ReadOnlySpan<long>).GetConstructor([typeof(long[])])!;
    private static readonly ConstructorInfo ReadOnlySpanLongSliceCtor =
        typeof(ReadOnlySpan<long>).GetConstructor([typeof(long[]), typeof(int), typeof(int)])!;

    public static VmProgram Emit(
        Node root,
        AnalysisResult analysis,
        CompilationMode mode = CompilationMode.Normal,
        IReadOnlyList<Parameter>? rootParameters = null) {

        var ctx = new AbiCtx();
        ctx.Mode = mode;
        ctx.Analysis = analysis;
        // C1a root module bodies (SetArgs(this, params…)): slot 0 = this, params at 1+.
        // Nested CompileFunctionBody uses a fresh AbiCtx and keeps slots from 0.
        // Locals must not reuse SetArgs slots — EmitScopeStores would wipe them.
        // Ordinary root programs (no rootParameters) keep the pre-C1a layout.
        var rootArgCount = 0;
        if (rootParameters is not null) {
            ctx.IsCompiledFunctionBody = true;
            ctx.RestoreParamSlots(1);
            ctx.PushFrameParameters();
            foreach (var parameter in rootParameters)
                ctx.MapFrameParameter(parameter, ctx.DeclareParameter(parameter));
            rootArgCount = 1 + rootParameters.Count;
            ctx.ReserveFrameSlots(rootArgCount);
        }
        var body = new List<Expression>();

        var lambdas = new List<Lambda>();
        CollectLambdas(root, lambdas);
        var capturedBindings = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var captureMeta = analysis.GetMetadata<VariableAnalysisMetadata>(root);
        if (captureMeta is not null) {
            foreach (var v in captureMeta.CapturedVariables)
                capturedBindings.Add(v);
            foreach (var p in captureMeta.CapturedParameters)
                capturedBindings.Add(p);
        }
        else {
            foreach (var lam in lambdas) {
                foreach (var cap in FindBodyCaptures(lam, analysis))
                    capturedBindings.Add(cap.Binding);
            }
        }
        ctx.CapturedBindings = capturedBindings;
        var functionTable = new Action<VmState>[lambdas.Count];
        ctx.FunctionTableExpr = Constant(functionTable);
        for (int i = 0; i < lambdas.Count; i++) {
            var lam = lambdas[i];
            functionTable[i] = CompileFunctionBody(
                lam.Body, lam.Parameters, FindBodyCaptures(lam, analysis), functionTable, mode, analysis,
                capturedBindings);
        }

        body.Add(Label(ctx.EntryLabel));
        body.Add(Assign(ctx.SlotsLocal, ctx.SlotsInitExpression));
        body.Add(Assign(ctx.HeapLocal, ctx.HeapInitExpression));
        body.Add(Assign(ctx.FramePosLocal,
            Condition(
                Equal(Property(ctx.State, nameof(VmState.Status)),
                    Constant(InterpreterStatus.Resuming)),
                Property(ctx.State, nameof(VmState.FramePos)),
                Constant(0))));
        body.Add(Assign(ctx.InstanceHandle, ArrayAccess(ctx.SlotsLocal, ctx.FramePosLocal)));

        if (mode != CompilationMode.NoDebug) {
            body.Add(Assign(ctx.ProgramCounter, Constant(0)));
            ctx.DebugHookProp = Property(ctx.State, nameof(VmState.DebugHook));
        }

        // Compile root BEFORE PC dispatch so SuspendNode resume labels are registered.
        var rootExpr = CompileStatement(root, ctx);
        body.Add(ctx.EmitPcDispatch(Goto(ctx.ExitLabel)));
        // Nested AST calls (ProbeCreate/CreateIn host jobs) allocate frames at
        // StackPointer. SetArgs writes this+params at slots[0..] without
        // advancing SP, so a nested frame at SP=0 would overwrite them. Park
        // SP past the reserved frame (runs only on a fresh start: resume jumps
        // away in PC dispatch above).
        if (rootArgCount > 0)
            body.Add(Assign(ctx.SlotsStackPointer, Constant(ctx.FrameSlotHighWater)));
        body.Add(rootExpr);
        if (ctx.RingDepth > 0) {
            body.Add(Assign(ArrayAccess(ctx.SlotsLocal, ctx.FramePosLocal),
                ctx.RingVar(ctx.RingDepth - 1)));
            body.Add(Assign(ctx.SlotsStackPointer,
                Add(ctx.FramePosLocal, Constant(1))));
        }
        body.Add(Label(ctx.ExitLabel));

        var rootMeta = analysis.GetMetadata<ValueRepresentationMetadata>(root);
        var delegateExpr = Lambda<Action<VmState>>(Block(ctx.Locals, body), ctx.State);
        var del = delegateExpr.Compile();
        int registerScratchSize = ctx.MaxRingDepth;
        var debugInfo = new VmDebugInfo(ctx.VariableLayouts);
        return new VmProgram(del, registerScratchSize, RootValueKind: rootMeta?.Kind,
            RootClrType: rootMeta?.ClrType,
            StepNodes: ctx.StepNodes, DebugInfo: debugInfo,
            RootParameterClrTypes: ctx.RootParameterClrTypes);
    }

    private static Expression CompileNode(Node node, AbiCtx ctx) {
        if (ctx.Analysis?.GetNodeReplacement(node) is { } replacement && replacement != node)
            return CompileNode(replacement, ctx);
        return CompileNodeInner(node, ctx);
    }

    private static Expression CompileStatement(Node node, AbiCtx ctx) {
        if (ctx.DebugHookProp is null) return CompileNode(node, ctx);
        var stores = ctx.EmitScopeStores();
        var body = CompileNode(node, ctx);
        int localCount = ctx.FrameSlotHighWater;
        Expression spanExpr = localCount == 0
            ? New(ReadOnlySpanLongArrayCtor,
                NewArrayBounds(typeof(long), Constant(0)))
            : New(ReadOnlySpanLongSliceCtor,
                ctx.SlotsLocal, ctx.FramePosLocal, Constant(localCount));
        var invoke = Block(
            Assign(Property(ctx.State, nameof(VmState.CurrentAstNode)), Constant(node)),
            Invoke(ctx.DebugHookProp, Constant(node), spanExpr, ctx.HeapLocal));
        return Block(
            Block(stores),
            IfThen(NotEqual(ctx.DebugHookProp,
                Constant(null, typeof(Action<Node, ReadOnlySpan<long>, Heap>))),
                invoke),
            body);
    }

    private static Expression CompileNodeInner(Node node, AbiCtx ctx) {
        return node switch {
            Constant c => EmitConstant(c, ctx),
            Add n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Add, ctx),
            Subtract n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Subtract, ctx),
            Multiply n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Multiply, ctx),
            Divide n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Divide, ctx),
            Modulo n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Modulo, ctx),
            BitwiseAnd n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, And, ctx),
            BitwiseOr n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, Or, ctx),
            BitwiseXor n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, ExclusiveOr, ctx),
            ShiftLeft n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, LeftShift, ctx),
            ShiftRight n => EmitBinaryArithmetic(n.LeftHandValue, n.RightHandValue, RightShift, ctx),
            Equal n => SpillToRing(EmitComparisonValue(n.LeftHandValue, n.RightHandValue, Equal, ctx, n), ctx),
            NotEqual n => SpillToRing(EmitComparisonValue(n.LeftHandValue, n.RightHandValue, NotEqual, ctx, n), ctx),
            LessThan n => SpillToRing(EmitComparisonValue(n.LeftHandValue, n.RightHandValue, LessThan, ctx), ctx),
            LessThanOrEqual n => SpillToRing(EmitComparisonValue(n.LeftHandValue, n.RightHandValue, LessThanOrEqual, ctx), ctx),
            GreaterThan n => SpillToRing(EmitComparisonValue(n.LeftHandValue, n.RightHandValue, GreaterThan, ctx), ctx),
            GreaterThanOrEqual n => SpillToRing(EmitComparisonValue(n.LeftHandValue, n.RightHandValue, GreaterThanOrEqual, ctx), ctx),
            Variable v => EmitVariable(v, ctx),
            Default d => EmitDefault(d, ctx),
            ThisReference _ => EmitThis(ctx),
            ParameterReference => RejectCompile("ParameterReference is not a VM value; bind a Parameter."),
            NamedTypeReference or TypeReference or PrimitiveTypeReference or TypeDefinitionReference =>
                RejectCompile("Type name is not a VM value; use it as a Member/Invoke/New receiver."),
            NullForgiving n => SpillToRing(CompileValue(n, ctx), ctx),
            TypeAs t => EmitTypeAs(t, ctx),
            TypeCast t => EmitTypeCast(t, ctx),
            Await aw => EmitAwait(aw, ctx),
            TypeOf t => EmitTypeOf(t, ctx),
            ThrowExpression te => EmitThrow(new ThrowStatement(te.Value), ctx),
            Not n => SpillToRing(EmitNotValue(n, ctx), ctx),
            UnaryMinus n => SpillToRing(EmitUnaryMinusValue(n, ctx), ctx),
            BitwiseNot n => SpillToRing(EmitBitwiseNotValue(n, ctx), ctx),
            Conditional c => SpillToRing(EmitConditionalValue(c, ctx), ctx),
            Coalesce n => SpillToRing(EmitCoalesceValue(n, ctx), ctx),
            And n => SpillToRing(EmitLogicalAndValue(n, ctx), ctx),
            Or n => SpillToRing(EmitLogicalOrValue(n, ctx), ctx),
            PopCount pc => SpillToRing(EmitPopCountValue(pc, ctx), ctx),
            Member m => EmitMember(m, ctx),
            TypeIs t => EmitTypeIs(t, ctx),
            Return r => EmitReturn(r, ctx),
            IfStatement ifStmt => EmitIfStatement(ifStmt, ctx),
            WhileLoop wl => EmitWhileLoop(wl, ctx),
            DoWhileLoop dwl => EmitDoWhileLoop(dwl, ctx),
            ForLoop fl => EmitForLoop(fl, ctx),
            ForEachLoop fel => EmitForEachLoop(fel, ctx),
            BreakStatement bs => EmitBreakStatement(bs, ctx),
            ContinueStatement cs => EmitContinueStatement(cs, ctx),
            GotoStatement gs => EmitGotoStatement(gs, ctx),
            LabelDeclaration ld => EmitLabelDeclaration(ld, ctx),
            ThrowStatement ts => EmitThrow(ts, ctx),
            TryCatchFinally tcf => EmitTryCatchFinally(tcf, ctx),
            UsingStatement us => EmitUsingStatement(us, ctx),
            SuspendNode sn => EmitSuspendNode(sn, ctx),
            Comment => Empty(),
            Assignment a => EmitAssignment(a, ctx),
            Block b => EmitBlock(b, ctx),
            Parameter p => EmitParameter(p, ctx),
            Lambda l => EmitLambda(l, ctx),
            Invoke inv => EmitInvoke(inv, ctx),
            InterpolatedString interp => EmitInterpolatedString(interp, ctx),
            New n => EmitNew(n, ctx),
            NewArray n => EmitNewArray(n, ctx),
            IndexAccess n => EmitIndexAccess(n, ctx),
            StridedSetBits ssb => EmitStridedSetBits(ssb, ctx),
            SwitchStatement sw => EmitSwitch(sw, ctx),
            _ => throw new NotSupportedException(
                $"DirectVmAbiEmitter: unsupported node type {node.GetType().Name}")
        };
    }

    private static Expression RejectCompile(string message) =>
        throw new InvalidOperationException($"VM compile rejected: {message}");

    private static Expression EmitConstant(Constant c, AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        if (RingCodec.TryInlineConstant(c.Value, out long val))
            return Assign(ctx.RingVar(slot), Constant(val));
        return Assign(ctx.RingVar(slot),
            RingCodec.Box(Convert(Constant(c.Value), typeof(object)), ctx.HeapLocal));
    }

    private static Expression EmitNew(New n, AbiCtx ctx) {
        Type? targetType = n.Type switch {
            ClrTypeReference ctr => ctr.RuntimeType,
            _ => null
        };
        if (targetType is null && ctx.Analysis is not null) {
            var resolvedType = ctx.Analysis.GetResolvedType(n);
            if (resolvedType is ClrTypeDefinition clrDef)
                targetType = clrDef.RuntimeType;
        }
        ConstructorInfo? ctor = null;
        ITypeMember? resolved = null;
        if (ctx.Analysis is not null) {
            resolved = ctx.Analysis.GetResolvedMember(n);
            if (resolved is ClrConstructor clrCtor)
                ctor = clrCtor.ConstructorInfo;
        }
        if (resolved is AstConstructorDefinition astCtor)
            return EmitAstNew(n, astCtor, ctx);
        if (resolved is AstCollectionParameterlessConstructor or AstCollectionCopyConstructor)
            return EmitSyntheticCollection(n, (AstCollectionTypeDefinition)resolved.DeclaringTypeDefinition, ctx);

        if (ctor is not null) {
            var args = ctx.OpenRing();
            int[] argSlots = new int[n.Arguments.Length];
            for (int i = 0; i < n.Arguments.Length; i++)
                argSlots[i] = args.Add(CompileNode(n.Arguments[i], ctx));
            var ctorParams = ctor.GetParameters();
            var ctorArgs = new Expression[ctorParams.Length];
            for (int i = 0; i < ctorParams.Length; i++) {
                var paramType = ctorParams[i].ParameterType;
                if (i < n.Arguments.Length) {
                    var ringVal = ctx.RingVar(argSlots[i]);
                    if (paramType.IsValueType) {
                        ctorArgs[i] = Convert(ringVal, paramType);
                    }
                    else {
                        ctorArgs[i] = Convert(
                            Call(ctx.HeapLocal, HeapUnsafeGet,
                                Convert(ringVal, typeof(int))),
                            paramType);
                    }
                }
                else if (ctorParams[i].HasDefaultValue) {
                    ctorArgs[i] = Constant(ctorParams[i].DefaultValue, paramType);
                }
                else {
                    throw new InvalidOperationException(
                        $"VM compile rejected: constructor argument {i} has no value.");
                }
            }
            var newExpr = New(ctor, ctorArgs);
            var boxed = Convert(newExpr, typeof(object));
            int slot = ctx.AllocSlot();
            var handle = Call(ctx.HeapLocal, HeapAllocate, boxed);
            ctx.RingDepth = slot + 1;
            return args.Then(Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));
        }
        var typeName = targetType?.Name ?? n.Type.ToString();
        throw new InvalidOperationException(
            $"VM compile rejected: no matching constructor for {typeName} with {n.Arguments.Length} argument(s).");
    }

    /// <summary>
    /// <c>new List&lt;Entity&gt;()</c> and <c>new List&lt;Entity&gt;(items)</c>. The
    /// element is an AST type, so the collection is not a closed CLR
    /// <c>List&lt;T&gt;</c>. The instance is <c>List&lt;object&gt;</c> (or
    /// <c>HashSet&lt;object&gt;</c>), which <c>Add</c> finds by name.
    /// </summary>
    private static Expression EmitSyntheticCollection(New n, AstCollectionTypeDefinition collection, AbiCtx ctx) {
        var seq = new List<Expression>();
        Expression created;
        if (n.Arguments.Length == 0) {
            created = Call(null, NewEmptyCollectionInfo, Constant((int)collection.Kind));
        }
        else {
            var ring = ctx.OpenRing();
            int got = ring.Add(CompileNode(n.Arguments[0], ctx));
            seq.AddRange(ring.Steps);
            var source = Call(ctx.HeapLocal, HeapUnsafeGet, Convert(ctx.RingVar(got), typeof(int)));
            created = Call(null, CopyCollectionInfo, Constant((int)collection.Kind), source);
        }
        int slot = ctx.AllocSlot();
        var handle = Call(ctx.HeapLocal, HeapAllocate, created);
        ctx.RingDepth = slot + 1;
        seq.Add(Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));
        return Block(seq);
    }

    private static readonly MethodInfo NewEmptyCollectionInfo =
        typeof(DirectVmAbiEmitter).GetMethod(nameof(NewEmptyCollection), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CopyCollectionInfo =
        typeof(DirectVmAbiEmitter).GetMethod(nameof(CopyCollection), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static object NewEmptyCollection(int kind) => (CollectionKind)kind switch {
        CollectionKind.Set => new HashSet<object>(),
        CollectionKind.List => new List<object>(),
        _ => throw new InvalidOperationException(
            $"VM compile rejected: synthetic {(CollectionKind)kind} has no parameterless constructor.")
    };

    private static object CopyCollection(int kind, object? source) {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        var items = source as System.Collections.IEnumerable
            ?? throw new InvalidOperationException(
                $"Collection constructor source '{source.GetType().Name}' is not IEnumerable.");
        switch ((CollectionKind)kind) {
            case CollectionKind.Set: {
                    var set = new HashSet<object>();
                    foreach (var item in items)
                        set.Add(item!);
                    return set;
                }
            case CollectionKind.List: {
                    var list = new List<object>();
                    foreach (var item in items)
                        list.Add(item!);
                    return list;
                }
            default:
                throw new InvalidOperationException(
                    $"VM compile rejected: synthetic {(CollectionKind)kind} has no copy constructor.");
        }
    }

    /// <summary>
    /// AST <c>new</c> allocates <see cref="Dictionary{TKey,TValue}"/> and, when the
    /// constructor body is non-empty, runs that body as its own VM function.
    /// Frame slot 0 is the new handle (<c>this</c>); parameters follow it.
    /// The <c>new</c> result is the dictionary handle. A constructor
    /// <c>return</c> stays inside that function and is not the result.
    /// </summary>
    private static Expression EmitAstNew(New n, AstConstructorDefinition astCtor, AbiCtx ctx) {
        var definition = astCtor.Definition;
        var parameters = definition.Parameters ?? [];
        var seq = new List<Expression>();
        var args = EmitAstArguments(n.Arguments, parameters, "constructor", seq, ctx);

        int slot = ctx.AllocSlot();
        ctx.RingDepth = slot + 1;
        var dict = New(AstDictionaryCtor, Constant(StringComparer.Ordinal));
        var handle = Call(ctx.HeapLocal, HeapAllocate, Convert(dict, typeof(object)));
        seq.Add(Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));

        if (!HasExecutableBody(definition.Body))
            return Block(seq);

        var table = TryCompileAstCallable(
            definition, definition.Body!, parameters, astCtor.DeclaringTypeDefinition, ctx, instanceSlot: true)
            ?? throw new InvalidOperationException(
                "VM compile rejected: AST constructor body could not be compiled.");
        var inits = new List<Expression>(parameters.Count + 1) { ctx.RingVar(slot) };
        inits.AddRange(args);

        var ignored = Variable(typeof(long), "_astCtor");
        seq.Add(Assign(ignored, Call(
            null, InvokeAstFunctionInfo, ctx.State,
            ArrayIndex(Constant(table), Constant(0)),
            NewArrayInit(typeof(long), inits))));
        return Block([ignored], seq);
    }

    private static Expression EmitNewArray(NewArray n, AbiCtx ctx) {
        var ring = ctx.OpenRing();
        int lenSlot = ring.Add(CompileNode(n.Length, ctx));
        int slot = ctx.AllocSlot();
        // A VM array stores scalar slots in a `long[]` and heap handles in an `object[]`;
        // the index routes key on the element's CLR value-type-ness, so creation must agree
        // (a `PrimitiveTypeReference` element such as `long` used to become `object`).
        Type elemType = RingCodec.ArraySlotElementType(ArrayElementClrType(n.ElementType, ctx));
        // A value-type element that is not a ring slot (decimal, DateTime, Guid, or any
        // nullable) is a heap handle, and a fresh array's zero slot would be the null handle,
        // not default(T); reject rather than read null where C# gives the default value.
        var elementClr = ArrayElementClrType(n.ElementType, ctx);
        if (n.ElementType is OptionalTypeReference
            || (elementClr is { IsValueType: true } nonSlot && !RingCodec.IsInline(nonSlot))) {
            throw new InvalidOperationException(
                $"VM compile rejected: an array of '{elementClr?.Name ?? "?"}' is not supported.");
        }
        // Always a heap array here. The frame-local small-array encoding was removed: it was
        // not readable through a member access and no caller tracked its base offset.
        var arr = NewArrayBounds(elemType, Convert(ctx.RingVar(lenSlot), typeof(int)));
        var handle = Call(ctx.HeapLocal, HeapAllocate,
            Convert(arr, typeof(object)));
        return ring.Then(Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));
    }

    /// <summary>The CLR type of an array's element from its type node, or null when it cannot
    /// be resolved statically.</summary>
    private static Type? ArrayElementClrType(Node elementType, AbiCtx ctx) => elementType switch {
        ClrTypeReference c => c.RuntimeType,
        PrimitiveTypeReference p => p.PrimitiveId.GetClrType(),
        _ => ctx.Analysis?.GetResolvedType(elementType)?.GetRuntimeType()
    };

    private static Expression EmitIndexAccess(IndexAccess n, AbiCtx ctx) {
        var indexKind = ctx.Analysis?.GetValueRepresentation(n.Value);
        if (indexKind is ValueRepresentationKind.StackScalar or ValueRepresentationKind.Bool)
            throw new InvalidOperationException(
                "VM compile rejected: index access requires an array or indexer.");
        var ring = ctx.OpenRing();
        int arrSlot = ring.Add(CompileNode(n.Value, ctx));
        int idxSlot = ring.Add(CompileNode(
            n.Arguments.Length > 0 ? n.Arguments[0] : new Constant(0), ctx));
        int outSlot = ctx.AllocSlot();
        Type? elementType = ctx.Analysis?.GetResolvedType(n) is ClrTypeDefinition clrElem
            ? clrElem.RuntimeType
            : null;
        var read = RingCodec.ArrayElementRead(
            ctx.HeapLocal, ctx.RingVar(arrSlot), ctx.RingVar(idxSlot), elementType);
        return ring.Then(Assign(ctx.RingVar(outSlot), read));
    }

    private static Expression CompileValue(Node node, AbiCtx ctx) {
        if (ctx.Analysis?.GetNodeReplacement(node) is { } replacement && replacement != node)
            return CompileValue(replacement, ctx);
        return node switch {
            Constant c when RingCodec.TryInlineConstant(c.Value, out _)
                => EmitConstantValue(c),
            Constant c => SpillRingRead(EmitConstant(c, ctx), ctx),
            Variable v => ctx.TryGetCapture(v, out _)
                ? SpillRingRead(EmitVariable(v, ctx), ctx)
                : ctx.VariableRead(v),
            Add n => EmitBinaryArithmeticValue(n.LeftHandValue, n.RightHandValue, Add, ctx),
            Subtract n => EmitBinaryArithmeticValue(n.LeftHandValue, n.RightHandValue, Subtract, ctx),
            Multiply n => EmitBinaryArithmeticValue(n.LeftHandValue, n.RightHandValue, Multiply, ctx),
            Divide n => EmitBinaryArithmeticValue(n.LeftHandValue, n.RightHandValue, Divide, ctx),
            Modulo n => EmitBinaryArithmeticValue(n.LeftHandValue, n.RightHandValue, Modulo, ctx),
            BitwiseAnd n => EmitBinaryArithmeticValue(n.LeftHandValue, n.RightHandValue, And, ctx),
            BitwiseOr n => EmitBinaryArithmeticValue(n.LeftHandValue, n.RightHandValue, Or, ctx),
            BitwiseXor n => EmitBinaryArithmeticValue(n.LeftHandValue, n.RightHandValue, ExclusiveOr, ctx),
            ShiftLeft n => EmitBinaryArithmeticValue(n.LeftHandValue, n.RightHandValue, LeftShift, ctx),
            ShiftRight n => EmitBinaryArithmeticValue(n.LeftHandValue, n.RightHandValue, RightShift, ctx),
            Equal n => EmitComparisonValue(n.LeftHandValue, n.RightHandValue, Equal, ctx, n),
            NotEqual n => EmitComparisonValue(n.LeftHandValue, n.RightHandValue, NotEqual, ctx, n),
            LessThan n => EmitComparisonValue(n.LeftHandValue, n.RightHandValue, LessThan, ctx),
            LessThanOrEqual n => EmitComparisonValue(n.LeftHandValue, n.RightHandValue, LessThanOrEqual, ctx),
            GreaterThan n => EmitComparisonValue(n.LeftHandValue, n.RightHandValue, GreaterThan, ctx),
            GreaterThanOrEqual n => EmitComparisonValue(n.LeftHandValue, n.RightHandValue, GreaterThanOrEqual, ctx),
            Not n => EmitNotValue(n, ctx),
            UnaryMinus n => EmitUnaryMinusValue(n, ctx),
            BitwiseNot n => EmitBitwiseNotValue(n, ctx),
            PopCount pc => EmitPopCountValue(pc, ctx),
            Conditional c => EmitConditionalValue(c, ctx),
            Coalesce n => EmitCoalesceValue(n, ctx),
            And n => EmitLogicalAndValue(n, ctx),
            Or n => EmitLogicalOrValue(n, ctx),
            Default d => SpillRingRead(EmitDefault(d, ctx), ctx),
            ThisReference _ => SpillRingRead(EmitThis(ctx), ctx),
            ParameterReference => RejectCompile("ParameterReference is not a VM value; bind a Parameter."),
            NullForgiving n => CompileValue(n.Operand, ctx),
            TypeAs ta => SpillRingRead(EmitTypeAs(ta, ctx), ctx),
            TypeCast tc => SpillRingRead(EmitTypeCast(tc, ctx), ctx),
            Await aw => SpillRingRead(EmitAwait(aw, ctx), ctx),
            TypeOf t => SpillRingRead(EmitTypeOf(t, ctx), ctx),
            ThrowExpression te => EmitThrow(new ThrowStatement(te.Value), ctx),
            IndexAccess n => EmitIndexAccessValue(n, ctx),
            Block b => SpillRingRead(CompileNode(b, ctx), ctx),
            Member m => SpillRingRead(CompileNode(m, ctx), ctx),
            New n => SpillRingRead(CompileNode(n, ctx), ctx),
            NewArray n => SpillRingRead(CompileNode(n, ctx), ctx),
            Parameter p => SpillRingRead(CompileNode(p, ctx), ctx),
            Invoke inv => SpillRingRead(CompileNode(inv, ctx), ctx),
            InterpolatedString interp => SpillRingRead(CompileNode(interp, ctx), ctx),
            TypeIs t => SpillRingRead(CompileNode(t, ctx), ctx),
            SwitchStatement sw => SpillRingRead(CompileNode(sw, ctx), ctx),
            StridedSetBits ssb => SpillRingRead(CompileNode(ssb, ctx), ctx),
            Assignment a => SpillRingRead(CompileNode(a, ctx), ctx),
            Comment => RejectCompile("Comment is not a VM value."),
            NamedTypeReference or TypeReference or PrimitiveTypeReference or TypeDefinitionReference =>
                RejectCompile("Type name is not a VM value; use it as a Member/Invoke/New receiver."),
            _ => throw new NotSupportedException(
                $"CompileValue: unhandled {node.GetType().Name}")
        };
    }

    private static Expression EmitIndexAccessValue(IndexAccess n, AbiCtx ctx) {
        return SpillRingRead(CompileNode(n, ctx), ctx);
    }

    private static Expression SpillToRing(Expression value, AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        ctx.RingDepth = slot + 1;
        return Assign(ctx.RingVar(slot), value);
    }

    private static Expression SpillRingRead(Expression compiled, AbiCtx ctx) {
        int slot = ctx.RingDepth - 1;
        return Block(compiled, ctx.RingVar(slot));
    }

    private static Expression EmitConstantValue(Constant c) {
        if (RingCodec.TryInlineConstant(c.Value, out long val)) return Constant(val);
        return Constant(0L);
    }

    private static Expression EmitBinaryArithmeticValue(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        AbiCtx ctx) {
        var leftVal = CompileValue(left, ctx);
        var rightVal = CompileValue(right, ctx);
        if (RingCodec.IsIeee(left, ctx.Analysis) || RingCodec.IsIeee(right, ctx.Analysis))
            return Call(BitConverterDoubleToInt64Bits,
                factory(AsIeeeDouble(left, leftVal, ctx), AsIeeeDouble(right, rightVal, ctx)));
        var rhs = rightVal;
        if (factory == LeftShift || factory == RightShift) rhs = Convert(rhs, typeof(int));
        return factory(leftVal, rhs);
    }

    private static Expression AsIeeeDouble(Node node, Expression ringVal, AbiCtx ctx) =>
        RingCodec.IsIeee(node, ctx.Analysis)
            ? Call(BitConverterInt64BitsToDouble, ringVal)
            : Convert(ringVal, typeof(double));

    private static Expression EmitNotValue(Not n, AbiCtx ctx) =>
        Condition(Equal(CompileValue(n.Value, ctx), Constant(0L)), Constant(1L), Constant(0L));

    private static Expression EmitUnaryMinusValue(UnaryMinus n, AbiCtx ctx) {
        var v = CompileValue(n.Operand, ctx);
        return RingCodec.IsIeee(n.Operand, ctx.Analysis)
            ? Call(BitConverterDoubleToInt64Bits, Negate(Call(BitConverterInt64BitsToDouble, v)))
            : Negate(v);
    }

    private static Expression EmitBitwiseNotValue(BitwiseNot n, AbiCtx ctx) =>
        Not(CompileValue(n.Operand, ctx));

    private static Expression EmitPopCountValue(PopCount pc, AbiCtx ctx) =>
        Convert(Call(null, BitOperationsPopCount,
            Convert(CompileValue(pc.Operand, ctx), typeof(ulong))), typeof(long));

    private static Expression EmitConditionalValue(Conditional c, AbiCtx ctx) {
        var test = CompileConditionAsBool(c.Condition, ctx);
        var ifTrue = CompileConditionalBranch(c, c.IfTrue, ctx);
        var ifFalse = CompileConditionalBranch(c, c.IfFalse, ctx);
        if (ifTrue.Type != typeof(void) && ifFalse.Type != typeof(void))
            return Condition(test, ifTrue, ifFalse);

        // A `throw` arm compiles to a void block, and Expression.Condition rejects a
        // void arm. Give the void arm the other arm's type so the ternary still
        // type-checks: the arm throws when taken, so the default it appends is
        // unreachable. A conditional whose arms both throw has no value at all, so
        // branch with statements instead.
        if (ifTrue.Type == typeof(void) && ifFalse.Type == typeof(void))
            return Block(IfThenElse(test, ifTrue, ifFalse), Constant(0L));
        return ifTrue.Type == typeof(void)
            ? Condition(test, Block(ifTrue, Expression.Default(ifFalse.Type)), ifFalse)
            : Condition(test, ifTrue, Block(ifFalse, Expression.Default(ifTrue.Type)));
    }

    /// <summary>Compiles one arm of a <see cref="Conditional"/>. A literal <c>null</c>
    /// arm of a value-represented conditional — the null-receiver arm of a <c>?.</c>
    /// or <c>?[</c> whose selected member or element is a scalar, as in <c>s?.Length</c>
    /// — has no slot representation: the value slot would hold <c>0</c>, a silently
    /// wrong result where C# produces <c>int?</c> (a shape this subset cannot
    /// represent). Fail closed by throwing when that arm is taken instead of yielding
    /// the sentinel.</summary>
    private static Expression CompileConditionalBranch(Conditional c, Node branch, AbiCtx ctx) {
        if (branch is Constant { Value: null } && IsValueRepresented(c, ctx))
            return Block(
                Throw(New(InvalidOperationExceptionStringCtor,
                    Constant("Conditional access on a null receiver has no value in this subset."))),
                Constant(0L));
        return CompileValue(branch, ctx);
    }

    private static bool IsValueRepresented(Node node, AbiCtx ctx) =>
        ctx.Analysis?.GetMetadata<ValueRepresentationMetadata>(node)?.Kind
            is ValueRepresentationKind.StackScalar or ValueRepresentationKind.Bool;

    private static Expression EmitCoalesceValue(Coalesce n, AbiCtx ctx) {
        var tmp = Variable(typeof(long), "_coal");
        var leftVal = CompileValue(n.LeftHandValue, ctx);
        var rightVal = CompileValue(n.RightHandValue, ctx);
        Expression whenLeft = RingCodec.UsesNullSentinel(n.LeftHandValue, ctx.Analysis)
            ? UnboxIfHeapNullable(ctx, n.LeftHandValue, tmp)
            : tmp;
        return Block([tmp],
            Assign(tmp, leftVal),
            Condition(
                RingCodec.UsesNullSentinel(n.LeftHandValue, ctx.Analysis)
                    ? NotEqual(tmp, Constant(0L))
                    : Constant(true),
                whenLeft,
                rightVal));
    }

    private static Expression UnboxIfHeapNullable(AbiCtx ctx, Node node, Expression handle) {
        var meta = ctx.Analysis?.GetMetadata<ValueRepresentationMetadata>(node);
        var clr = meta?.ClrType;
        var underlying = clr is null ? null : Nullable.GetUnderlyingType(clr);
        if (underlying is null || !RingCodec.IsInline(underlying))
            return handle;
        return Call(null, UnboxNullableToLongInfo, ctx.HeapLocal, handle);
    }

    private static Expression EmitLogicalAndValue(And n, AbiCtx ctx) =>
        Condition(Equal(CompileValue(n.LeftHandValue, ctx), Constant(0L)),
            Constant(0L),
            CompileValue(n.RightHandValue, ctx));

    private static Expression EmitLogicalOrValue(Or n, AbiCtx ctx) =>
        Condition(NotEqual(CompileValue(n.LeftHandValue, ctx), Constant(0L)),
            Constant(1L),
            CompileValue(n.RightHandValue, ctx));

    private static Expression CompileConditionAsBool(Node node, AbiCtx ctx) =>
        node switch {
            Equal n => CompileCompareTest(n.LeftHandValue, n.RightHandValue, Equal, ctx),
            NotEqual n => CompileCompareTest(n.LeftHandValue, n.RightHandValue, NotEqual, ctx),
            LessThan n => CompileCompareTest(n.LeftHandValue, n.RightHandValue, LessThan, ctx),
            LessThanOrEqual n => CompileCompareTest(n.LeftHandValue, n.RightHandValue, LessThanOrEqual, ctx),
            GreaterThan n => CompileCompareTest(n.LeftHandValue, n.RightHandValue, GreaterThan, ctx),
            GreaterThanOrEqual n => CompileCompareTest(n.LeftHandValue, n.RightHandValue, GreaterThanOrEqual, ctx),
            Not n => Not(CompileConditionAsBool(n.Value, ctx)),
            And n => AndAlso(CompileConditionAsBool(n.LeftHandValue, ctx),
                CompileConditionAsBool(n.RightHandValue, ctx)),
            Or n => OrElse(CompileConditionAsBool(n.LeftHandValue, ctx),
                CompileConditionAsBool(n.RightHandValue, ctx)),
            _ => NotEqual(CompileValue(node, ctx), Constant(0L))
        };
}