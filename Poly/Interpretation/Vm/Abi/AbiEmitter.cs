using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Abi;

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
public static partial class AbiEmitter {
    /// <summary>
    /// Describes a captured binding. Analysis records free bindings per
    /// <see cref="Lambda"/>; stored closures share a heap <c>long[1]</c>
    /// cell with the enclosing frame so later writes are visible.
    /// </summary>
    private sealed record Capture(Variable? Variable, Parameter? Parameter) {
        public object Binding => (object?)Variable ?? Parameter!;
    }

    // Cached reflection lookups
    private static readonly MethodInfo HeapUnsafeGet = Ref<VmManagedObjectHeap>.Method(h => h.UnsafeGet(0));
    private static readonly MethodInfo HeapAllocate = Ref<VmManagedObjectHeap>.Method(h => h.Allocate(default!));
    private static readonly MethodInfo HeapRecycle = Ref<VmManagedObjectHeap>.Method(h => h.Recycle(0));
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
        Ref.Method((Expression<Func<object?, IEnumerator>>)(c => GetEnumeratorOrThrow(c)));
    private static readonly MethodInfo EnumeratorMoveNext =
        Ref<IEnumerator>.Method(e => e.MoveNext());
    private static readonly PropertyInfo EnumeratorCurrent =
        Ref<IEnumerator>.Property(e => e.Current);
    private static readonly MethodInfo BoxToAbiInfo =
        Ref.Method((Expression<Func<VmManagedObjectHeap, object?, long>>)((h, v) => RingCodec.BoxToAbi(h, v)));
    private static readonly MethodInfo CastAbiInfo =
        Ref.Method((Expression<Func<VmManagedObjectHeap, long, Type, long>>)((h, r, t) => RingCodec.Cast(h, r, t)));
    private static readonly MethodInfo TypeAsAbiInfo =
        Ref.Method((Expression<Func<VmManagedObjectHeap, long, Type, long>>)((h, r, t) => RingCodec.TypeAs(h, r, t)));
    private static readonly MethodInfo UnboxNullableToLongInfo =
        Ref.Method((Expression<Func<VmManagedObjectHeap, long, long>>)((h, v) => RingCodec.UnboxNullable(h, v)));
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
        var built = Build(root, analysis, mode, rootParameters);
        var del = (Action<VmState>)built.Expression.Compile();
        return new VmProgram(del, built.MaxRingDepth,
            RootValueKind: built.RootValueKind,
            RootClrType: built.RootClrType,
            StepNodes: built.StepNodes, DebugInfo: built.DebugInfo,
            RootParameterClrTypes: built.RootParameterClrTypes);
    }

    /// <summary>Build the program's <see cref="LambdaExpression"/> and metadata
    /// without compiling it. <see cref="Emit"/> compiles this expression; tests and
    /// the VM README inspect it to show the produced body.</summary>
    internal static BuiltVmProgram Build(
        Node root,
        AnalysisResult analysis,
        CompilationMode mode = CompilationMode.Normal,
        IReadOnlyList<Parameter>? rootParameters = null) {

        var ctx = new Context {
            Mode = mode,
            Analysis = analysis
        };
        // C1a root module bodies (SetArgs(this, params…)): slot 0 = this, params at 1+.
        // Nested CompileFunctionBody uses a fresh Context and keeps slots from 0.
        // Locals must not reuse SetArgs slots — EmitScopeStores would wipe them.
        // Ordinary root programs (no rootParameters) keep the pre-C1a layout.
        var rootArgCount = 0;
        if (rootParameters is not null) {
            ctx.IsCompiledFunctionBody = true;
            ctx.RestoreParamSlots(1);
            foreach (var parameter in rootParameters)
                ctx.DeclareParameter(parameter);
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
        var inlineInvoked = new HashSet<Lambda>(ReferenceEqualityComparer.Instance);
        CollectInlineInvokedLambdas(root, inlineInvoked);
        var functionTable = new Action<VmState>[lambdas.Count];
        ctx.FunctionTableExpr = Constant(functionTable);
        for (int i = 0; i < lambdas.Count; i++) {
            var lam = lambdas[i];
            if (inlineInvoked.Contains(lam)) {
                functionTable[i] = _ => throw new InvalidOperationException(
                    "VM: a lambda invoked inline is emitted in the caller's frame and cannot be reached through the function table.");
                continue;
            }
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

        if (mode != CompilationMode.NoDebug)
            ctx.DebugHookProp = Property(ctx.State, nameof(VmState.DebugHook));

        ctx.IsRootBody = true;
        // Compile root BEFORE PC dispatch so SuspendNode resume labels are registered.
        var rootExpr = CompileStatement(root, ctx);
        // Seed one heap entry per distinct typeof type before anything can read it. Guarded so a
        // resume (which re-enters above the PC dispatch) reuses the handle it already allocated
        // instead of leaking one per resume.
        foreach (var (runtimeType, local) in ctx.TypeHandles) {
            body.Add(IfThen(
                Equal(local, Constant(0L)),
                Assign(local, Convert(
                    Call(ctx.HeapLocal, HeapAllocate,
                        Convert(Constant(runtimeType, typeof(object)), typeof(object))),
                    typeof(long)))));
        }
        body.Add(ctx.EmitPcDispatch(Goto(ctx.ExitLabel)));
        // Nested AST calls (ProbeCreate/CreateIn host jobs) allocate frames at
        // StackPointer. SetArgs writes this+params at slots[0..] without
        // advancing SP, so a nested frame at SP=0 would overwrite them. Park
        // SP past the reserved frame (runs only on a fresh start: resume jumps
        // away in PC dispatch above).
        if (rootArgCount > 0)
            body.Add(Assign(ctx.SlotsStackPointer, Constant(ctx.FrameSlotHighWater)));
        body.Add(rootExpr);
        var rootResult = ctx.RingDepth > 0
            ? new SlotLocation.Ring(ctx.RingDepth - 1, rootExpr.Type)
            : null;
        if (rootResult is not null) {
            body.Add(new SlotLocation.Frame(0).Write(ctx, rootResult.Read(ctx)));
            body.Add(Assign(ctx.SlotsStackPointer,
                Add(ctx.FramePosLocal, Constant(1))));
        }
        body.Add(Label(ctx.ExitLabel));

        var (rootKind, rootClr) = ctx.RepresentationOf(root);
        var delegateExpr = Lambda<Action<VmState>>(Block(ctx.Locals, body), ctx.State);
        var debugInfo = new VmDebugInfo(ctx.VariableLayouts);
        return new BuiltVmProgram(delegateExpr, ctx.MaxRingDepth,
            rootKind is ValueRepresentationKind.Unknown ? null : rootKind,
            rootClr, ctx.StepNodes, debugInfo, ctx.RootParameterClrTypes, rootResult);
    }

    /// <summary>The expression and metadata produced by <see cref="Build"/>.
    /// <c>RootResult</c> is where the root's result lives — the slot the program copies
    /// out — or null for a root that leaves none (a <c>Comment</c>).</summary>
    internal sealed record BuiltVmProgram(
        LambdaExpression Expression,
        int MaxRingDepth,
        ValueRepresentationKind? RootValueKind,
        Type? RootClrType,
        IReadOnlyList<Node>? StepNodes,
        VmDebugInfo DebugInfo,
        IReadOnlyList<Type?>? RootParameterClrTypes,
        SlotLocation? RootResult);

    private static Value CompileNode(Node node, Context ctx) => CompileValue(node, ctx);

    /// <summary>Compile a node and name its value. <see cref="Value.Code"/> is the
    /// expression to run; <see cref="Value.Read"/> is the value it leaves. The
    /// location is the node's <see cref="CompiledNode.Result"/>, so a consumer that
    /// wants the value after the code has run uses <c>Read</c> and never
    /// <c>RingDepth - 1</c>.</summary>
    private static Value CompileValue(Node node, Context ctx) {
        var compiled = Compile(node, ctx);
        var read = compiled.Result is { } place ? place.Read(ctx) : compiled.Code;
        var type = compiled.Result is { } p ? LocalType.Of(p.Representation) : null;
        return new Value(compiled.Code, read, compiled.Result, type);
    }

    /// <summary>A node's value: its code, then a read of the slot that code left the result
    /// in. Every arm leaves one, so this is the only way a value is reached — there is no
    /// second value compiler, and an arm that leaves nothing fails closed here.</summary>
    private static Expression ValueOf(Node node, Context ctx) {
        var compiled = Compile(node, ctx);
        return Block(compiled.Code, compiled.RequireResult(node).Read(ctx));
    }

    /// <summary>A statement's ring answer: <c>0L</c> at <c>RingDepth - 1</c>. A node with no
    /// value — a loop, a branch, a jump, a throw — answers the same way a value does, so a
    /// caller never has to ask which kind of node it read. <paramref name="code"/> is a
    /// delegate so the slot is reserved before the statement compiles: the statement's own
    /// slots sit above its result instead of leaving a branch's value behind as it.</summary>
    private static Expression Statement(Context ctx, Func<Expression> code) {
        int slot = ctx.AllocSlot();
        var body = code();
        ctx.RingDepth = slot + 1;
        return Block(body, Assign(ctx.RingVar(slot), Constant(0L)));
    }

    /// <summary>Compile a node and report where its result lives. Every arm of
    /// <see cref="CompileNodeInner"/> leaves its result at the top of the ring — a node
    /// with no value leaves <c>0L</c> — so <see cref="CompiledNode.Result"/> is the slot a
    /// consumer reads. Only a node that emits no code at all (<see cref="Comment"/>) has no
    /// location, and a consumer that needs one asks with
    /// <see cref="CompiledNode.RequireResult"/>, which fails closed.</summary>
    private static CompiledNode Compile(Node node, Context ctx) {
        if (ctx.Analysis?.GetNodeReplacement(node) is { } replacement && replacement != node)
            return Compile(replacement, ctx);
        int entryDepth = ctx.RingDepth;
        var code = CompileNodeInner(node, ctx);
        // The arm's own type names the local it wrote: an IEEE arm assigns the value
        // itself, every other arm the word. Reading the reported result from the code
        // rather than from the analysis keeps producer and consumer in one answer.
        var result = ctx.RingDepth > entryDepth
            ? new SlotLocation.Ring(ctx.RingDepth - 1, code.Type)
            : null;
        return new CompiledNode(code, result);
    }

    private static Expression CompileStatement(Node node, Context ctx) {
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
            Block(stores),
            Assign(Property(ctx.State, nameof(VmState.CurrentAstNode)), Constant(node)),
            Invoke(ctx.DebugHookProp, Constant(node), spanExpr, ctx.HeapLocal));
        return Block(
            IfThen(NotEqual(ctx.DebugHookProp, Constant(null, ctx.DebugHookProp.Type)),
                invoke),
            body);
    }

    private static Expression CompileNodeInner(Node node, Context ctx) {
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
            Equal n => EmitComparison(n.LeftHandValue, n.RightHandValue, Equal, ctx),
            NotEqual n => EmitComparison(n.LeftHandValue, n.RightHandValue, NotEqual, ctx),
            LessThan n => EmitComparison(n.LeftHandValue, n.RightHandValue, LessThan, ctx),
            LessThanOrEqual n => EmitComparison(n.LeftHandValue, n.RightHandValue, LessThanOrEqual, ctx),
            GreaterThan n => EmitComparison(n.LeftHandValue, n.RightHandValue, GreaterThan, ctx),
            GreaterThanOrEqual n => EmitComparison(n.LeftHandValue, n.RightHandValue, GreaterThanOrEqual, ctx),
            Variable v => EmitVariable(v, ctx),
            Default d => EmitDefault(d, ctx),
            ThisReference _ => EmitThis(ctx),
            ParameterReference => RejectCompile("ParameterReference is not a VM value; bind a Parameter."),
            NamedTypeReference or TypeReference or PrimitiveTypeReference or TypeDefinitionReference =>
                RejectCompile("Type name is not a VM value; use it as a Member/Invoke/New receiver."),
            NullForgiving n => CompileNode(n.Operand, ctx),
            TypeAs t => EmitTypeAs(t, ctx),
            TypeCast t => EmitTypeCast(t, ctx),
            Await aw => EmitAwait(aw, ctx),
            TypeOf t => EmitTypeOf(t, ctx),
            ThrowExpression te => EmitThrow(new ThrowStatement(te.Value), ctx),
            Not n => EmitNot(n, ctx),
            UnaryMinus n => EmitUnaryMinus(n, ctx),
            BitwiseNot n => EmitBitwiseNot(n, ctx),
            Conditional c => EmitConditional(c, ctx),
            Coalesce n => EmitCoalesce(n, ctx),
            And n => EmitLogicalAnd(n, ctx),
            Or n => EmitLogicalOr(n, ctx),
            PopCount pc => EmitPopCount(pc, ctx),
            Member m => EmitMember(m, ctx),
            TypeIs t => EmitTypeIs(t, ctx),
            Return r => Statement(ctx, () => EmitReturn(r, ctx)),
            IfStatement ifStmt => Statement(ctx, () => EmitIfStatement(ifStmt, ctx)),
            WhileLoop wl => Statement(ctx, () => EmitWhileLoop(wl, ctx)),
            DoWhileLoop dwl => Statement(ctx, () => EmitDoWhileLoop(dwl, ctx)),
            ForLoop fl => Statement(ctx, () => EmitForLoop(fl, ctx)),
            ForEachLoop fel => Statement(ctx, () => EmitForEachLoop(fel, ctx)),
            BreakStatement bs => Statement(ctx, () => EmitBreakStatement(bs, ctx)),
            ContinueStatement cs => Statement(ctx, () => EmitContinueStatement(cs, ctx)),
            GotoStatement gs => Statement(ctx, () => EmitGotoStatement(gs, ctx)),
            // A label is a marker over its statement and answers with that statement's
            // result, so it adds no slot of its own.
            LabelDeclaration ld => EmitLabelDeclaration(ld, ctx),
            ThrowStatement ts => Statement(ctx, () => EmitThrow(ts, ctx)),
            // A try answers with its try block's result, so it adds no slot of its own —
            // the classifier reads a TryCatchFinally as what its try block yields.
            TryCatchFinally tcf => EmitTryCatchFinally(tcf, ctx),
            UsingStatement us => Statement(ctx, () => EmitUsingStatement(us, ctx)),
            SuspendNode sn => Statement(ctx, () => EmitSuspendNode(sn, ctx)),
            // Transparent, not a statement: a comment emits nothing and leaves whatever the
            // previous statement put on the ring, so `[x, comment]` still yields x. It is the
            // one arm with no ring location, and a consumer that asks for one fails closed.
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
                $"AbiEmitter: unsupported node type {node.GetType().Name}")
        };
    }

    private static Expression RejectCompile(string message) =>
        throw new InvalidOperationException($"VM compile rejected: {message}");

    /// <summary>A rejection a nested-frame fallback must propagate, not swallow: the body
    /// it rejected would otherwise compile into a frame no resume can re-enter.</summary>
    private sealed class UnresumableBodyRejection(string message) : InvalidOperationException(message);

    private static Expression EmitConstant(Constant c, Context ctx) {
        if (c.Value is double d)
            return Assign(ctx.HoldRing(typeof(double)), Constant(d));
        if (c.Value is float f)
            return Assign(ctx.HoldRing(typeof(float)), Constant(f));
        // An int literal is an int: its boxed CLR type is the width analysis resolves for
        // the constant, so the slot carries the same width and an all-int expression pays
        // no conversion between a variable and a literal.
        if (c.Value is int i)
            return Assign(ctx.HoldRing(typeof(int)), Constant(i));
        if (RingCodec.TryInlineConstant(c.Value, out long val))
            return Assign(ctx.HoldRing(typeof(long)), Constant(val));
        return Assign(ctx.HoldRing(typeof(long)),
            RingCodec.Box(Convert(Constant(c.Value), typeof(object)), ctx.HeapLocal));
    }

    private static Expression EmitNew(New n, Context ctx) {
        var resolved = ctx.Analysis?.GetResolvedMember(n);
        if (resolved is AstConstructorDefinition astCtor)
            return EmitAstNew(n, astCtor, ctx);
        if (resolved is AstCollectionParameterlessConstructor or AstCollectionCopyConstructor)
            return EmitSyntheticCollection(n, (AstCollectionTypeDefinition)resolved.DeclaringTypeDefinition, ctx);

        ConstructorInfo? ctor = (resolved as ClrConstructor)?.ConstructorInfo;
        Type? targetType = ctx.TryClrType(n);
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
                        ctorArgs[i] = LocalType.Of(paramType) is LocalType.TemporalLocal { IsWord: true }
                            ? LocalType.Decode(ringVal, paramType, paramType, ctx.HeapLocal)
                            : Convert(ringVal, paramType);
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
            int slot = ctx.AllocSlot();
            ctx.RingDepth = slot + 1;
            // An inline value (a temporal word, an int, an IEEE bit pattern) is the
            // word itself. Boxing it and then reading the handle as that word returns
            // the default.
            Expression stored = targetType is not null && RingCodec.IsInline(targetType)
                ? LocalType.Encode(newExpr, targetType, ctx.HeapLocal)
                : Convert(
                    Call(ctx.HeapLocal, HeapAllocate, Convert(newExpr, typeof(object))),
                    typeof(long));
            return args.Then(Assign(ctx.RingVar(slot), stored));
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
    private static Expression EmitSyntheticCollection(New n, AstCollectionTypeDefinition collection, Context ctx) {
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

    private static readonly MethodInfo NewEmptyCollectionInfo = Ref.Method((Expression<Func<int, object>>)(kind => NewEmptyCollection(kind)));

    private static readonly MethodInfo CopyCollectionInfo = Ref.Method((Expression<Func<int, object?, object>>)((kind, source) => CopyCollection(kind, source)));

    private static object NewEmptyCollection(int kind) => (CollectionKind)kind switch {
        CollectionKind.Set => new HashSet<object>(),
        CollectionKind.List => new List<object>(),
        _ => throw new InvalidOperationException(
            $"VM compile rejected: synthetic {(CollectionKind)kind} has no parameterless constructor.")
    };

    private static object CopyCollection(int kind, object? source) {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        var items = source as IEnumerable
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
    private static Expression EmitAstNew(New n, AstConstructorDefinition astCtor, Context ctx) {
        var definition = astCtor.Definition;
        var parameters = definition.Parameters ?? [];
        var seq = new List<Expression>();
        var args = EmitAstArguments(n.Arguments, parameters, astCtor.Parameters.ToArray(),
            "constructor", seq, ctx);

        int slot = ctx.AllocSlot();
        ctx.RingDepth = slot + 1;
        var dict = New(AstDictionaryCtor, Constant(StringComparer.Ordinal));
        var handle = Call(ctx.HeapLocal, HeapAllocate, Convert(dict, typeof(object)));
        seq.Add(Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));

        if (!HasExecutableBody(definition.Body))
            return Block(seq);

        var table = TryCompileAstCallable(
            definition, definition.Body!, parameters, ctx, instanceSlot: true, rejection: out var rejection)
            ?? throw new InvalidOperationException(
                "VM compile rejected: AST constructor body could not be compiled. " + rejection);
        var inits = new List<Expression>(parameters.Count + 1) { ctx.RingVar(slot) };
        inits.AddRange(args);

        var ignored = Variable(typeof(long), "_astCtor");
        seq.Add(Assign(ignored, EmitCallFrame(ArrayIndex(Constant(table), Constant(0)), inits, ctx)));
        return Block([ignored], seq);
    }

    private static Expression EmitNewArray(NewArray n, Context ctx) =>
        EmitNewArray(n, ctx, cacheLocal: null);

    /// <summary>Create an array. When <paramref name="cacheLocal"/> is supplied the one
    /// array built here is bound to it <em>and</em> to the heap: a promoted definition
    /// allocates once, so the local and the variable's slot hold the same object.</summary>
    private static Expression EmitNewArray(NewArray n, Context ctx, ParameterExpression? cacheLocal) {
        var ring = ctx.OpenRing();
        var lenSlot = ring.AddValue(CompileNode(n.Length, ctx));
        var slot = ctx.Reserve();
        // A VM array stores scalar slots in a `long[]` and heap handles in an `object[]`;
        // the index routes key on the element's CLR value-type-ness, so creation must agree
        // (a `PrimitiveTypeReference` element such as `long` used to become `object`).
        // A value-type element that is not a ring slot (decimal, Guid) is a heap
        // handle, and a fresh array's zero slot would be the null handle, not default(T);
        // reject the optional spelling rather than read null where C# gives the default.
        var elementClr = ArrayElementClrType(n.ElementType, ctx);
        if (n.ElementType is OptionalTypeReference
            || (elementClr is { IsValueType: true } nonSlot && !RingCodec.IsInline(nonSlot))) {
            throw new InvalidOperationException(
                $"VM compile rejected: an array of '{elementClr?.Name ?? "?"}' is not supported.");
        }
        // Always a heap array here. The frame-local small-array encoding was removed: it was
        // not readable through a member access and no caller tracked its base offset.
        Type elemType = RingCodec.ArraySlotElementType(elementClr);
        var arr = NewArrayBounds(elemType, Convert(lenSlot.Read, typeof(int)));
        if (cacheLocal is not null)
            ring.AddUncounted(Assign(cacheLocal, arr));
        Expression source = cacheLocal is null ? arr : cacheLocal;
        var handle = Call(ctx.HeapLocal, HeapAllocate, Convert(source, typeof(object)));
        return ring.Then(Assign(slot.Read, Convert(handle, typeof(long))));
    }

    /// <summary>The CLR type of an array's element from the type pass, or null when it
    /// cannot be resolved statically.</summary>
    private static Type? ArrayElementClrType(Node elementType, Context ctx) =>
        ctx.TryClrType(elementType);

    private static Expression EmitIndexAccess(IndexAccess n, Context ctx) {
        if (ctx.KindOf(n.Value) is ValueRepresentationKind.StackScalar)
            throw new InvalidOperationException(
                "VM compile rejected: index access requires an array or indexer.");
        var ring = ctx.OpenRing();
        // A promoted array is the CLR local the definition bound, so the element is the
        // array object indexed — one bounds check, no heap deref, no cast.
        if (n.Value is Variable cached && ctx.TryArrayCache(cached, out var arrayLocal)) {
            var cachedIdxSlot = ring.AddValue(CompileNode(
                n.Arguments.Length > 0 ? n.Arguments[0] : new Constant(0), ctx));
            var cachedOutSlot = ctx.Reserve();
            var cachedRead = RingCodec.CachedElementRead(
                arrayLocal, cachedIdxSlot.Read, ctx.HeapLocal);
            return ring.Then(Assign(cachedOutSlot.Read, cachedRead));
        }
        var arrSlot = ring.AddValue(CompileNode(n.Value, ctx));
        var idxSlot = ring.AddValue(CompileNode(
            n.Arguments.Length > 0 ? n.Arguments[0] : new Constant(0), ctx));
        var outSlot = ctx.Reserve();
        Type? elementType = ctx.Analysis?.GetResolvedType(n) is ClrTypeDefinition clrElem
            ? clrElem.RuntimeType
            : null;
        var read = RingCodec.ArrayElementRead(
            ctx.HeapLocal, arrSlot.Read, idxSlot.Read, elementType);
        return ring.Then(Assign(outSlot.Read, read));
    }

    private static Expression SpillToRing(Expression value, Context ctx) {
        // An IEEE value keeps its own local: that is the whole point of the typed
        // slot, and the reason an all-IEEE expression needs no bitcast between operands.
        using var scope = ctx.Scope();
        return scope.Yield(value);
    }

    private static Expression EmitBinaryArithmeticValue(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        Context ctx) {
        if ((factory == LeftShift || factory == RightShift)
            && (ContainsSuspend(left) || ContainsSuspend(right)))
            return EmitShiftAcrossSuspend(left, right, factory, ctx);
        var leftVal = ValueOf(left, ctx);
        var rightVal = ValueOf(right, ctx);
        if (Nullable.GetUnderlyingType(leftVal.Type) is not null
            || Nullable.GetUnderlyingType(rightVal.Type) is not null) {
            if (factory == LeftShift || factory == RightShift)
                return EmitLiftedShift(left, right, leftVal, rightVal, factory, ctx);
            var nullable = typeof(Nullable<>).MakeGenericType(
                PromoteUnderlying(left, right, leftVal, rightVal, ctx, shift: false));
            return factory(
                AsNullableOperand(left, leftVal, nullable, ctx),
                AsNullableOperand(right, rightVal, nullable, ctx));
        }
        bool leftIeee = RingCodec.IsIeee(left, ctx.Analysis);
        bool rightIeee = RingCodec.IsIeee(right, ctx.Analysis);
        if (leftIeee || rightIeee) {
            var target = IeeeArithmeticType(
                leftIeee ? ctx.ClrTypeOf(left) : null,
                rightIeee ? ctx.ClrTypeOf(right) : null);
            return factory(
                RingCodec.FromFrame(leftVal, ctx.ClrTypeOf(left) ?? leftVal.Type, target, ctx.HeapLocal),
                RingCodec.FromFrame(rightVal, ctx.ClrTypeOf(right) ?? rightVal.Type, target, ctx.HeapLocal));
        }
        // ulong is a bitcast in the word. Dividing or shifting the signed word turns
        // ulong.MaxValue into -1, so the operands are materialised as ulong first.
        var leftUnderlying = UnderlyingOf(left, leftVal, ctx) ?? leftVal.Type;
        var rightUnderlying = UnderlyingOf(right, rightVal, ctx) ?? rightVal.Type;
        if (leftUnderlying == typeof(ulong) || rightUnderlying == typeof(ulong)) {
            if (factory == LeftShift || factory == RightShift)
                return factory(
                    RingCodec.FromFrame(leftVal, leftUnderlying, typeof(ulong), ctx.HeapLocal),
                    RingCodec.AsRepresentation(rightVal, typeof(int)));
            return factory(
                RingCodec.FromFrame(leftVal, leftUnderlying, typeof(ulong), ctx.HeapLocal),
                RingCodec.FromFrame(rightVal, rightUnderlying, typeof(ulong), ctx.HeapLocal));
        }
        // A shift's result width is its left operand's; its count is always an int in C#.
        if (factory == LeftShift || factory == RightShift)
            return factory(
                RingCodec.AsRepresentation(leftVal, IntegerWidthOf(left, ctx)),
                RingCodec.AsRepresentation(rightVal, typeof(int)));
        // Every other integer operator — bitwise included — runs at the width analysis
        // resolved for its operands, so `Expression.Add(int, int)` wraps at 32 bits because
        // the CLR does, and an all-int expression needs no conversion between operations.
        var width = IntegerArithmeticWidthOf(left, right, ctx);
        return factory(
            RingCodec.AsRepresentation(leftVal, width),
            RingCodec.AsRepresentation(rightVal, width));
    }

    /// <summary>C# binary numeric promotion of the underlying types. A null constant
    /// takes the other operand's type. A shift's width is its left operand, and
    /// <c>ulong</c> stays <c>ulong</c> so the right shift is logical.</summary>
    private static Type PromoteUnderlying(
        Node left, Node right, Expression leftVal, Expression rightVal, Context ctx, bool shift) {
        var leftType = UnderlyingOf(left, leftVal, ctx);
        var rightType = UnderlyingOf(right, rightVal, ctx);
        if (leftType is null)
            leftType = rightType;
        if (rightType is null)
            rightType = leftType;
        if (leftType is null || rightType is null)
            throw new InvalidOperationException(
                "VM compile rejected: cannot resolve the width of a nullable arithmetic operand.");
        return CSharpBinaryNumericType(leftType, rightType, shift);
    }

    /// <summary>The type a binary operator on these underlying types takes.
    /// <c>decimal</c> with <c>float</c> or <c>double</c>, and <c>ulong</c> with a
    /// signed operand, have no operator and fail closed.</summary>
    private static Type CSharpBinaryNumericType(Type left, Type right, bool shift) {
        if (shift)
            return left == typeof(ulong) ? typeof(ulong)
                : RingCodec.IsInt32Width(left) ? typeof(int) : typeof(long);
        if (left == typeof(bool) || right == typeof(bool))
            throw RejectBinary(left, right);
        if (left == typeof(decimal) || right == typeof(decimal)) {
            if (RingCodec.IsIeee(left) || RingCodec.IsIeee(right)
                || !ConvertsToDecimal(left) || !ConvertsToDecimal(right))
                throw RejectBinary(left, right);
            return typeof(decimal);
        }
        if (RingCodec.IsIeee(left) || RingCodec.IsIeee(right))
            return IeeeArithmeticType(
                RingCodec.IsIeee(left) ? left : null,
                RingCodec.IsIeee(right) ? right : null);
        // DateTime, TimeSpan, and an enum compare as themselves. Routing them
        // through the integer width turns the operator into a long comparison.
        if (left == right && !IsBinaryIntegral(left))
            return left;
        if (left == typeof(ulong) || right == typeof(ulong)) {
            if (IsUnsignedOrChar(left) && IsUnsignedOrChar(right))
                return typeof(ulong);
            throw RejectBinary(left, right);
        }
        if (left == typeof(long) || right == typeof(long))
            return typeof(long);
        return RingCodec.IntegerArithmeticWidth(left, right);
    }

    private static InvalidOperationException RejectBinary(Type? left, Type? right) =>
        new($"VM compile rejected: no operator for '{left?.Name ?? "unresolved"}' and '{right?.Name ?? "unresolved"}'.");

    private static bool ConvertsToDecimal(Type? type) =>
        type == typeof(decimal) || IsBinaryIntegral(type);

    private static bool IsBinaryIntegral(Type? type) =>
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short)
        || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong) || type == typeof(char);

    /// <summary>Types that promote to <c>ulong</c> rather than to a signed width.</summary>
    private static bool IsUnsignedOrChar(Type type) =>
        type == typeof(byte) || type == typeof(ushort) || type == typeof(char)
        || type == typeof(uint) || type == typeof(ulong);

    /// <summary>The operand's CLR type with <c>Nullable&lt;T&gt;</c> removed.
    /// A null constant has no numeric type.</summary>
    private static Type? UnderlyingOf(Node node, Expression value, Context ctx) {
        if (node is Constant { Value: null })
            return null;
        var clr = ctx.ClrTypeOf(node)
            ?? ctx.TryClrType(node)
            ?? (node is Constant constant ? constant.Value?.GetType() : null)
            ?? value.Type;
        return Nullable.GetUnderlyingType(clr) ?? clr;
    }

    /// <summary>A shift's left operand promotes like the non-nullable shift, then
    /// lifts. The count stays an <c>int</c>. Expression trees have no lifted shift,
    /// so an absent left answers <c>default</c> without shifting.</summary>
    private static Expression EmitLiftedShift(
        Node left, Node right, Expression leftVal, Expression rightVal,
        Func<Expression, Expression, BinaryExpression> factory,
        Context ctx) {
        var nullable = typeof(Nullable<>).MakeGenericType(
            PromoteUnderlying(left, right, leftVal, rightVal, ctx, shift: true));
        using var shift = ctx.LeaseHelper(nullable, "_shift");
        var local = shift.Local;
        var shifted = factory(
            Property(local, "Value"),
            RingCodec.AsRepresentation(rightVal, typeof(int)));
        return Block(
            Assign(local, AsNullableOperand(left, leftVal, nullable, ctx)),
            Condition(
                Property(local, "HasValue"),
                Convert(shifted, nullable),
                Default(nullable)));
    }

    private static Expression AsNullableOperand(
        Node node, Expression value, Type nullable, Context ctx) {
        if (node is Constant { Value: null })
            return Default(nullable);
        if (value.Type == nullable)
            return value;
        var underlying = Nullable.GetUnderlyingType(nullable)!;
        if (Nullable.GetUnderlyingType(value.Type) is not null || value.Type == underlying)
            return Convert(value, nullable);
        value = RingCodec.FromFrame(value, UnderlyingOf(node, value, ctx) ?? value.Type, underlying, ctx.HeapLocal);
        return value.Type == nullable ? value : Convert(value, nullable);
    }

    /// <summary>True when a long slot holds a heap handle for this non-nullable
    /// value type. An inline primitive, including an enum, is the word itself.</summary>
    private static bool NeedsHeapUnbox(Type source) =>
        source.IsValueType
        && Nullable.GetUnderlyingType(source) is null
        && !RingCodec.IsInline(source);

    /// <summary>The object stored for a <c>Nullable&lt;T&gt;</c> member. A nullable
    /// source's word is a handle (0 is null). A scalar word is the number, so 0
    /// stays present. A boxed decimal or enum is already the object.</summary>
    private static Expression NullableMemberWrite(
        Node value, int valueSlot, Type nullableType, Type underlying, Context ctx) {
        var word = ctx.RingVar(valueSlot);
        var valueClr = ctx.ClrTypeOf(value);
        // The region word of a nullable is already the handle. The optional
        // location is the only unbox; the member stores the object it produces.
        if (valueClr is not null && Nullable.GetUnderlyingType(valueClr) is not null)
            return Convert(
                new SlotLocation.Ring(valueSlot).ReadAs(ctx, nullableType),
                typeof(object));
        if (valueClr is not null && NeedsHeapUnbox(valueClr))
            return Call(ctx.HeapLocal, HeapUnsafeGet, Convert(word, typeof(int)));
        return Convert(
            RingCodec.FromFrame(word, ctx.ClrTypeOf(value) ?? word.Type, underlying, ctx.HeapLocal),
            typeof(object));
    }

    /// <summary>The width an integer operation runs at, from the operand types analysis
    /// resolved. An operand whose width is unresolved is a compile rejection that names it:
    /// falling back to the word is how
    /// [#289](https://github.com/scoizzle/Poly/issues/289) arose.</summary>
    private static Type IntegerArithmeticWidthOf(Node left, Node right, Context ctx) =>
        RingCodec.IntegerArithmeticWidth(
            ResolvedOperandType(left, ctx), ResolvedOperandType(right, ctx));

    /// <summary>The width one operand's own arithmetic runs at: <c>int</c> for the types C#
    /// promotes to 32 bits, the word otherwise.</summary>
    private static Type IntegerWidthOf(Node node, Context ctx) {
        var type = ResolvedOperandType(node, ctx);
        return type == typeof(ulong) ? typeof(ulong)
            : RingCodec.IsInt32Width(type) ? typeof(int) : typeof(long);
    }

    /// <summary>The CLR type analysis resolved for an arithmetic operand, or a rejection
    /// that names the operand.</summary>
    private static Type ResolvedOperandType(Node node, Context ctx) =>
        ctx.ClrTypeOf(node)
        ?? throw new InvalidOperationException(
            "VM compile rejected: cannot resolve the integer width of "
            + $"'{node}' ({node.GetType().Name}).");

    /// <summary>The type an IEEE arithmetic result takes: <c>double</c> as soon as
    /// either operand is one, otherwise <c>float</c> — C#'s own promotion, which keeps
    /// a <c>float</c> expression 32-bit.</summary>
    private static Type IeeeArithmeticType(Type? left, Type? right) =>
        left == typeof(double) || right == typeof(double) ? typeof(double) : typeof(float);

    private static Expression EmitConditionalValue(Conditional c, Context ctx) {
        var test = CompileConditionAsBool(c.Condition, ctx);
        var ifTrue = CompileConditionalBranch(c, c.IfTrue, ctx);
        var ifFalse = CompileConditionalBranch(c, c.IfFalse, ctx);
        if (ifTrue.Type != typeof(void) && ifFalse.Type != typeof(void)) {
            // An IEEE arm keeps the value, so the arms must agree on one width before
            // Expression.Condition can take them.
            if (ifTrue.Type != ifFalse.Type) {
                var target = ValueTypeOf(ifTrue.Type, ifFalse.Type);
                ifTrue = Convert(ifTrue, target);
                ifFalse = Convert(ifFalse, target);
            }
            return Condition(test, ifTrue, ifFalse);
        }

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
    private static Expression CompileConditionalBranch(Conditional c, Node branch, Context ctx) {
        if (branch is Constant { Value: null } && IsValueRepresented(c, ctx))
            return Block(
                Throw(New(InvalidOperationExceptionStringCtor,
                    Constant("Conditional access on a null receiver has no value in this subset."))),
                Constant(0L));
        return ValueOf(branch, ctx);
    }

    private static bool IsValueRepresented(Node node, Context ctx) =>
        ctx.KindOf(node) is ValueRepresentationKind.StackScalar;

    /// <summary>The one type two arms of a value can share: <c>double</c> if either is
    /// one, else <c>float</c> if either is one, else <c>long</c> if either is one, else the
    /// left type unchanged.</summary>
    private static Type ValueTypeOf(Type left, Type right) =>
        left == typeof(double) || right == typeof(double) ? typeof(double)
            : left == typeof(float) || right == typeof(float) ? typeof(float)
            : left == typeof(long) || right == typeof(long) ? typeof(long)
            : left;

    private static bool ContainsSuspend(Node node) {
        if (node is SuspendNode)
            return true;
        foreach (var child in node.Children)
            if (child is not null && ContainsSuspend(child))
                return true;
        return false;
    }

    /// <summary>A shift whose operand suspends cannot leave the resume label inside
    /// the value assigned to the ring. The helper is leased before either operand is
    /// compiled, so a suspend in the count still sees <c>_shift</c>.</summary>
    private static Expression EmitShiftAcrossSuspend(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        Context ctx) {
        if (NullableOperand(left, ctx) || NullableOperand(right, ctx)) {
            var nullable = typeof(Nullable<>).MakeGenericType(ShiftWidth(left, ctx));
            using var shift = ctx.LeaseHelper(nullable, "_shift");
            var local = shift.Local;
            var leftCompiled = Compile(left, ctx);
            var leftRead = leftCompiled.RequireResult(left).Read(ctx);
            var rightCompiled = Compile(right, ctx);
            var rightRead = rightCompiled.RequireResult(right).Read(ctx);
            var shifted = factory(
                Property(local, "Value"),
                RingCodec.AsRepresentation(rightRead, typeof(int)));
            return Block(
                leftCompiled.Code,
                Assign(local, AsNullableOperand(left, leftRead, nullable, ctx)),
                rightCompiled.Code,
                Condition(
                    Property(local, "HasValue"),
                    Convert(shifted, nullable),
                    Default(nullable)));
        }
        var leftCode = Compile(left, ctx);
        var rightCode = Compile(right, ctx);
        return Block(
            leftCode.Code,
            rightCode.Code,
            factory(
                RingCodec.AsRepresentation(leftCode.RequireResult(left).Read(ctx), IntegerWidthOf(left, ctx)),
                RingCodec.AsRepresentation(rightCode.RequireResult(right).Read(ctx), typeof(int))));
    }

    private static bool NullableOperand(Node node, Context ctx) {
        var clr = ctx.ClrTypeOf(node);
        return clr is not null && Nullable.GetUnderlyingType(clr) is not null;
    }

    private static Type ShiftWidth(Node left, Context ctx) {
        var clr = ctx.ClrTypeOf(left);
        var underlying = clr is null ? null : Nullable.GetUnderlyingType(clr) ?? clr;
        if (underlying is null)
            throw new InvalidOperationException(
                "VM compile rejected: cannot resolve the width of a nullable arithmetic operand.");
        return underlying == typeof(ulong) ? typeof(ulong)
            : RingCodec.IsInt32Width(underlying) ? typeof(int) : typeof(long);
    }

    /// <summary>A coalesce whose arm suspends cannot leave the resume label inside
    /// <c>Expression.Coalesce</c> or an assignment. <c>_coal</c> is leased before the
    /// suspending arm is compiled. The right arm runs only when the left has no value,
    /// as a statement, and the result is a condition that does not contain the label.</summary>
    private static Expression EmitCoalesceAcrossSuspend(Coalesce n, Context ctx) {
        bool leftSuspends = ContainsSuspend(n.LeftHandValue);
        CompiledNode leftCompiled = default;
        Expression leftRead;
        Type helperType;
        if (!leftSuspends) {
            leftCompiled = Compile(n.LeftHandValue, ctx);
            leftRead = leftCompiled.RequireResult(n.LeftHandValue).Read(ctx);
            helperType = Nullable.GetUnderlyingType(leftRead.Type) is not null ? leftRead.Type : typeof(long);
        }
        else {
            var declared = ctx.ClrTypeOf(n.LeftHandValue);
            helperType = declared is not null && Nullable.GetUnderlyingType(declared) is not null
                ? declared
                : typeof(long);
            leftRead = Constant(0L);
        }
        using var coal = ctx.LeaseHelper(helperType, "_coal");
        if (leftSuspends) {
            leftCompiled = Compile(n.LeftHandValue, ctx);
            leftRead = leftCompiled.RequireResult(n.LeftHandValue).Read(ctx);
        }
        var rightCompiled = Compile(n.RightHandValue, ctx);
        var rightRead = rightCompiled.RequireResult(n.RightHandValue).Read(ctx);
        var done = Label("coalDone");
        var tmp = coal.Local;
        if (Nullable.GetUnderlyingType(tmp.Type) is { } underlying) {
            Expression stored = leftRead!.Type == tmp.Type ? leftRead : Convert(leftRead, tmp.Type);
            Expression right = rightRead.Type == underlying || rightRead.Type == tmp.Type
                ? rightRead
                : Convert(rightRead, underlying);
            return Block(
                leftCompiled.Code,
                Assign(tmp, stored),
                IfThen(Property(tmp, "HasValue"), Goto(done)),
                rightCompiled.Code,
                Label(done),
                Condition(Property(tmp, "HasValue"), Property(tmp, "Value"), right));
        }
        bool sentinel = RingCodec.UsesNullSentinel(n.LeftHandValue, ctx.Analysis);
        Expression leftWord = leftRead!.Type == typeof(long) ? leftRead : RingCodec.ToWord(leftRead);
        Expression whenLeft = sentinel
            ? UnboxIfHeapNullable(ctx, n.LeftHandValue, tmp)
            : tmp;
        Expression present = sentinel ? NotEqual(tmp, Constant(0L)) : Constant(true);
        return Block(
            leftCompiled.Code,
            Assign(tmp, leftWord),
            IfThen(present, Goto(done)),
            rightCompiled.Code,
            Label(done),
            Condition(present, whenLeft, RingCodec.ToWord(rightRead)));
    }

    private static Expression EmitCoalesceValue(Coalesce n, Context ctx) {
        if (ContainsSuspend(n))
            return EmitCoalesceAcrossSuspend(n, ctx);
        var leftVal = ValueOf(n.LeftHandValue, ctx);
        if (Nullable.GetUnderlyingType(leftVal.Type) is { } underlying) {
            var rightArm = ValueOf(n.RightHandValue, ctx);
            Expression right = rightArm.Type == underlying || rightArm.Type == leftVal.Type
                ? rightArm
                : Convert(rightArm, underlying);
            using var coal = ctx.LeaseHelper(leftVal.Type, "_coal");
            var leftLocal = coal.Local;
            return Block(Assign(leftLocal, leftVal), Coalesce(leftLocal, right));
        }
        using var held = ctx.LeaseHelper(typeof(long), "_coal");
        var tmp = held.Local;
        // A coalesce answers in the word: its left side is a nullable heap handle, so
        // an IEEE right arm crosses the boundary here rather than widening the result.
        var rightVal = RingCodec.ToWord(ValueOf(n.RightHandValue, ctx));
        Expression whenLeft = RingCodec.UsesNullSentinel(n.LeftHandValue, ctx.Analysis)
            ? UnboxIfHeapNullable(ctx, n.LeftHandValue, tmp)
            : tmp;
        return Block(
            Assign(tmp, leftVal),
            Condition(
                RingCodec.UsesNullSentinel(n.LeftHandValue, ctx.Analysis)
                    ? NotEqual(tmp, Constant(0L))
                    : Constant(true),
                whenLeft,
                rightVal));
    }

    private static Expression UnboxIfHeapNullable(Context ctx, Node node, Expression handle) {
        var clr = ctx.ClrTypeOf(node);
        var underlying = clr is null ? null : Nullable.GetUnderlyingType(clr);
        if (underlying is null || !RingCodec.IsInline(underlying))
            return handle;
        return Call(null, UnboxNullableToLongInfo, ctx.HeapLocal, handle);
    }

    private static Expression CompileConditionAsBool(Node node, Context ctx) =>
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
            _ => IsNonZero(ValueOf(node, ctx))
        };

    /// <summary>Truthiness at the value's own width: an <c>int</c> slot compares with an
    /// <c>int</c> zero, a word with the word's.</summary>
    private static Expression IsNonZero(Expression value) =>
        value.Type == typeof(int) ? NotEqual(value, Constant(0)) : NotEqual(value, Constant(0L));

}