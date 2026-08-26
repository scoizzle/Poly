using System.Linq.Expressions;
using System.Reflection;

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
    /// Describes a captured binding. Stored closures share a heap <c>long[1]</c>
    /// cell with the enclosing frame so later writes are visible (late-bind).
    /// </summary>
    private sealed record Capture(Variable? Variable, Parameter? Parameter) {
        public object Binding => (object?)Variable ?? Parameter!;
    }

    // Cached reflection lookups
    private static readonly MethodInfo HeapUnsafeGet = Ref<Heap>.Method(h => h.UnsafeGet(0));
    private static readonly MethodInfo HeapAllocate = Ref<Heap>.Method(h => h.Allocate(default!));
    private static readonly MethodInfo ObjectEquals = Ref.Method(
        (Expression<Func<object?, object?, bool>>)((a, b) => object.Equals(a, b)));
    private static readonly MethodInfo StringConcat = Ref.Method(
        (Expression<Func<object?, object?, string?>>)((a, b) => string.Concat(a, b)));
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
        Ref.Method((Expression<Func<Heap, object?, long>>)((h, v) => BoxToAbi(h, v)));
    private static readonly MethodInfo ConvertAbiInfo =
        Ref.Method((Expression<Func<Heap, long, Type?, Type, long>>)((h, r, s, t) => ConvertAbi(h, r, s, t)));
    private static readonly MethodInfo TypeAsAbiInfo =
        Ref.Method((Expression<Func<Heap, long, Type, long>>)((h, r, t) => TypeAsAbi(h, r, t)));
    private static readonly MethodInfo UnboxNullableToLongInfo =
        Ref.Method((Expression<Func<Heap, long, long>>)((h, v) => UnboxNullableToLong(h, v)));
    private static readonly MethodInfo SetStackPointer = Ref<ValueStack>.Method(s => s.SetStackPointer(0));
    // Expression<Func<T>> cannot close over a ref struct (CS9244).
    private static readonly ConstructorInfo ReadOnlySpanLongArrayCtor =
        typeof(ReadOnlySpan<long>).GetConstructor([typeof(long[])])!;
    private static readonly ConstructorInfo ReadOnlySpanLongSliceCtor =
        typeof(ReadOnlySpan<long>).GetConstructor([typeof(long[]), typeof(int), typeof(int)])!;

    public static VmProgram Emit(
        Node root,
        AnalysisResult analysis,
        CompilationMode mode = CompilationMode.Normal) {

        var ctx = new AbiCtx();
        ctx.Mode = mode;
        ctx.Analysis = analysis;
        var body = new List<Expression>();

        var lambdas = new List<Lambda>();
        CollectLambdas(root, lambdas);
        var capturedBindings = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var lam in lambdas) {
            foreach (var cap in FindBodyCaptures(lam))
                capturedBindings.Add(cap.Binding);
        }
        ctx.CapturedBindings = capturedBindings;
        var functionTable = new Action<VmState>[lambdas.Count];
        ctx.FunctionTableExpr = Constant(functionTable);
        for (int i = 0; i < lambdas.Count; i++) {
            var lam = lambdas[i];
            functionTable[i] = CompileFunctionBody(
                lam.Body, lam.Parameters, FindBodyCaptures(lam), functionTable, mode, analysis,
                capturedBindings);
        }

        body.Add(Label(ctx.EntryLabel));
        body.Add(Assign(ctx.SlotsLocal, ctx.SlotsInitExpression));
        body.Add(Assign(ctx.HeapLocal, ctx.HeapInitExpression));
        body.Add(Assign(ctx.Registers,
            Coalesce(ctx.Registers, NewArrayBounds(typeof(long), Constant(256)))));
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

        body.Add(ctx.EmitPcDispatch(Goto(ctx.ExitLabel)));
        ctx.EnterActivation(0, 0);
        var rootExpr = CompileStatement(root, ctx);
        body.Add(rootExpr);
        if (ctx.RingDepth > 0) {
            body.Add(Assign(ArrayAccess(ctx.SlotsLocal, ctx.FramePosLocal),
                ctx.RingVar(ctx.RingDepth - 1)));
            body.Add(Assign(ctx.SlotsStackPointer,
                Add(ctx.FramePosLocal, Constant(1))));
        }
        ctx.LeaveActivation();
        body.Add(Label(ctx.ExitLabel));

        var rootMeta = analysis.GetMetadata<ValueRepresentationMetadata>(root);
        var delegateExpr = Lambda<Action<VmState>>(Block(ctx.Locals, body), ctx.State);
        var del = delegateExpr.Compile();
        int registerScratchSize = ctx.MaxRingDepth;
        var debugInfo = new VmDebugInfo(ctx.VariableLayouts);
        return new VmProgram(del, registerScratchSize, RootValueKind: rootMeta?.Kind,
            RootClrType: rootMeta?.ClrType,
            StepNodes: ctx.StepNodes, DebugInfo: debugInfo,
            RegisterCount: ctx.RegisterCount,
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
        int localCount = ctx.CurrentLocalCount;
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
            NullForgiving n => SpillToRing(CompileValue(n, ctx), ctx),
            TypeAs t => EmitTypeAs(t, ctx),
            TypeCast t => EmitTypeCast(t, ctx),
            Await => RejectCompile("Await is not executable on the VM."),
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
        if (TryValueToLong(c.Value, out long val))
            return Assign(ctx.RingVar(slot), Constant(val));
        if (c.Value is double dbl)
            return Assign(ctx.RingVar(slot), Constant(BitConverter.DoubleToInt64Bits(dbl)));
        if (c.Value is float flt)
            return Assign(ctx.RingVar(slot), Constant(BitConverter.DoubleToInt64Bits(flt)));
        var allocate = Call(ctx.HeapLocal, HeapAllocate,
            Convert(Constant(c.Value), typeof(object)));
        return Assign(ctx.RingVar(slot), Convert(allocate, typeof(long)));
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
        if (ctx.Analysis is not null) {
            var resolved = ctx.Analysis.GetResolvedMember(n);
            if (resolved is ClrConstructor clrCtor)
                ctor = clrCtor.ConstructorInfo;
        }
        if (ctor is null && targetType is not null)
            ctor = MatchConstructor(targetType, n.Arguments.Length);
        if (ctor is not null) {
            int d = ctx.RingDepth;
            var argExprs = new List<Expression>();
            int[] argSlots = new int[n.Arguments.Length];
            for (int i = 0; i < n.Arguments.Length; i++) {
                argExprs.Add(CompileNode(n.Arguments[i], ctx));
                argSlots[i] = ctx.RingDepth - 1;
            }
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
            return Block(argExprs.Concat([Assign(ctx.RingVar(slot), Convert(handle, typeof(long)))]));
        }
        if (targetType is { IsValueType: true } && n.Arguments.Length == 0) {
            var boxedDefault = Convert(Default(targetType), typeof(object));
            int slot2 = ctx.AllocSlot();
            var handle2 = Call(ctx.HeapLocal, HeapAllocate, boxedDefault);
            return Assign(ctx.RingVar(slot2), Convert(handle2, typeof(long)));
        }
        var typeName = targetType?.Name ?? n.Type.ToString();
        throw new InvalidOperationException(
            $"VM compile rejected: no matching constructor for {typeName} with {n.Arguments.Length} argument(s).");
    }

    private static ConstructorInfo? MatchConstructor(Type targetType, int argumentCount) {
        return targetType.GetConstructors()
            .Where(c => {
                var ps = c.GetParameters();
                int required = ps.Count(p => !p.HasDefaultValue && !p.IsOptional);
                return argumentCount >= required && argumentCount <= ps.Length;
            })
            .OrderBy(c => c.GetParameters().Length)
            .FirstOrDefault();
    }

    private static Expression EmitNewArray(NewArray n, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var lenExpr = CompileNode(n.Length, ctx);
        int lenSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref lenSlot, d, ctx);
        int slot = ctx.AllocSlot();
        Type elemType = n.ElementType switch {
            ClrTypeReference ctr when ctr.RuntimeType.IsValueType => ctr.RuntimeType,
            _ => typeof(object)
        };
        if (elemType.IsValueType && n.Length is Constant len && TryValueToLong(len.Value, out long lv)
            && lv > 0 && lv <= AbiCtx.SmallArrayThreshold) {
            int baseOffset = ctx.AllocateSmallArray();
            var zeroInits = new Expression[(int)lv];
            for (int zi = 0; zi < (int)lv; zi++)
                zeroInits[zi] = Assign(
                    ArrayAccess(ctx.SlotsLocal, Constant(baseOffset + zi)),
                    Constant(0L));
            return Block(lenExpr, fold, Block(zeroInits),
                Assign(ctx.RingVar(slot), Constant((long)baseOffset)));
        }
        var arr = NewArrayBounds(elemType, Convert(ctx.RingVar(lenSlot), typeof(int)));
        var handle = Call(ctx.HeapLocal, HeapAllocate,
            Convert(arr, typeof(object)));
        return Block(lenExpr, fold, Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));
    }

    private static Expression EmitIndexAccess(IndexAccess n, AbiCtx ctx) {
        var indexKind = ctx.Analysis?.GetValueRepresentation(n.Value);
        if (indexKind is ValueRepresentationKind.StackScalar or ValueRepresentationKind.Bool)
            throw new InvalidOperationException(
                "VM compile rejected: index access requires an array or indexer.");
        if (n.Value is Variable arrVar && ctx.TryGetFrameLocalBase(arrVar) is int) {
            return SpillToRing(EmitIndexAccessValue(n, ctx), ctx);
        }
        var arrExpr = CompileNode(n.Value, ctx);
        int arrSlot = ctx.RingDepth - 1;
        var idxExpr = CompileNode(n.Arguments.Length > 0 ? n.Arguments[0] : new Constant(0), ctx);
        int idxSlot = ctx.RingDepth - 1;
        int outSlot = ctx.AllocSlot();
        var rawObj = Call(ctx.HeapLocal, HeapUnsafeGet,
            Convert(ctx.RingVar(arrSlot), typeof(int)));
        var idx = Convert(ctx.RingVar(idxSlot), typeof(int));
        Type? elementType = ctx.Analysis?.GetResolvedType(n) is ClrTypeDefinition clrElem
            ? clrElem.RuntimeType
            : null;
        if (elementType is { IsValueType: true }) {
            var longArr = Variable(typeof(long[]), "_longArr");
            var valExpr = Block([longArr],
                Assign(longArr, Convert(rawObj, typeof(long[]))),
                ArrayAccess(longArr, idx));
            return Block(arrExpr, idxExpr,
                Assign(ctx.RingVar(outSlot), valExpr));
        }
        if (elementType is not null) {
            var objArr = Variable(typeof(object[]), "_objArr");
            var valExpr = Block([objArr],
                Assign(objArr, Convert(rawObj, typeof(object[]))),
                Convert(ArrayAccess(objArr, idx), typeof(long)));
            return Block(arrExpr, idxExpr,
                Assign(ctx.RingVar(outSlot), valExpr));
        }
        var longArrU = Variable(typeof(long[]), "_longArr");
        var objArrU = Variable(typeof(object[]), "_objArr");
        var valExprU = Condition(
            TypeIs(rawObj, typeof(long[])),
            Block([longArrU],
                Assign(longArrU, Convert(rawObj, typeof(long[]))),
                ArrayAccess(longArrU, idx)),
            Block([objArrU],
                Assign(objArrU, Convert(rawObj, typeof(object[]))),
                Convert(ArrayAccess(objArrU, idx), typeof(long))));
        return Block(arrExpr, idxExpr,
            Assign(ctx.RingVar(outSlot), valExprU));
    }

    private static bool TryValueToLong(object? value, out long result) {
        switch (value) {
            case null: result = 0; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case bool b: result = b ? 1 : 0; return true;
            case short s: result = s; return true;
            case byte bVal: result = bVal; return true;
            case sbyte sb: result = sb; return true;
            case ushort us: result = us; return true;
            case uint ui: result = ui; return true;
            case ulong ul: result = unchecked((long)ul); return true;
            case char c: result = c; return true;
            default: result = 0; return false;
        }
    }

    private static Expression FoldResultToSlot(ref int resultSlot, int d, AbiCtx ctx) {
        if (resultSlot <= d) return Empty();
        var copy = Assign(ctx.RingVar(d), ctx.RingVar(resultSlot));
        resultSlot = d;
        ctx.RingDepth = d + 1;
        return copy;
    }

    private static Expression CompileValue(Node node, AbiCtx ctx) {
        return node switch {
            Constant c when TryValueToLong(c.Value, out _) || c.Value is double || c.Value is float
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
            Await => RejectCompile("Await is not executable on the VM."),
            TypeOf t => SpillRingRead(EmitTypeOf(t, ctx), ctx),
            ThrowExpression te => EmitThrow(new ThrowStatement(te.Value), ctx),
            IndexAccess n => EmitIndexAccessValue(n, ctx),
            Block b => SpillRingRead(CompileNode(b, ctx), ctx),
            Member m => SpillRingRead(CompileNode(m, ctx), ctx),
            New n => SpillRingRead(CompileNode(n, ctx), ctx),
            NewArray n => SpillRingRead(CompileNode(n, ctx), ctx),
            Parameter p => SpillRingRead(CompileNode(p, ctx), ctx),
            Invoke inv => SpillRingRead(CompileNode(inv, ctx), ctx),
            TypeIs t => SpillRingRead(CompileNode(t, ctx), ctx),
            SwitchStatement sw => SpillRingRead(CompileNode(sw, ctx), ctx),
            StridedSetBits ssb => SpillRingRead(CompileNode(ssb, ctx), ctx),
            Assignment a => SpillRingRead(CompileNode(a, ctx), ctx),
            Comment => RejectCompile("Comment is not a VM value."),
            _ => throw new NotSupportedException(
                $"CompileValue: unhandled {node.GetType().Name}")
        };
    }

    private static Expression EmitIndexAccessValue(IndexAccess n, AbiCtx ctx) {
        if (n.Value is Variable arrVar && ctx.TryGetFrameLocalBase(arrVar) is int flBase) {
            Expression idx = n.Arguments.Length > 0
                ? CompileValue(n.Arguments[0], ctx)
                : Constant(0L);
            return ArrayAccess(ctx.SlotsLocal,
                Add(Constant(flBase), Convert(idx, typeof(int))));
        }
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
        if (TryValueToLong(c.Value, out long val)) return Constant(val);
        if (c.Value is double dbl) return Constant(BitConverter.DoubleToInt64Bits(dbl));
        if (c.Value is float flt) return Constant(BitConverter.DoubleToInt64Bits(flt));
        return Constant(0L);
    }

    private static Expression EmitBinaryArithmeticValue(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        AbiCtx ctx) {
        var leftVal = CompileValue(left, ctx);
        var rightVal = CompileValue(right, ctx);
        if (TryEmitStringConcat(left, right, leftVal, rightVal, factory, ctx) is { } concat)
            return concat;
        if (TryEmitDecimalArithmetic(left, right, leftVal, rightVal, factory, ctx) is { } dec)
            return dec;
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right))
            return Call(BitConverterDoubleToInt64Bits,
                factory(AsIeeeDouble(left, leftVal, ctx), AsIeeeDouble(right, rightVal, ctx)));
        var rhs = rightVal;
        if (factory == LeftShift || factory == RightShift) rhs = Convert(rhs, typeof(int));
        return factory(leftVal, rhs);
    }

    private static Expression? TryEmitStringConcat(
        Node left, Node right, Expression leftVal, Expression rightVal,
        Func<Expression, Expression, BinaryExpression> factory, AbiCtx ctx) {
        if (factory != Add)
            return null;
        bool ls = IsStringValue(ctx, left);
        bool rs = IsStringValue(ctx, right);
        if (ls ^ rs)
            throw new InvalidOperationException(
                "VM compile rejected: string concatenation requires both operands to be strings.");
        if (!ls)
            return null;
        var lo = HeapValueToObject(leftVal, ctx);
        var ro = HeapValueToObject(rightVal, ctx);
        var concat = Call(StringConcat, lo, ro);
        var handle = Call(ctx.HeapLocal,
            HeapAllocate,
            Convert(concat, typeof(object)));
        return Convert(handle, typeof(long));
    }

    private static Expression AsIeeeDouble(Node node, Expression ringVal, AbiCtx ctx) =>
        IsDoubleValue(ctx, node)
            ? Call(BitConverterInt64BitsToDouble, ringVal)
            : Convert(ringVal, typeof(double));

    private static bool IsDecimalValue(AbiCtx ctx, Node node) {
        if (node is Constant { Value: decimal }) return true;
        var meta = ctx.Analysis?.GetMetadata<ValueRepresentationMetadata>(node);
        return meta?.ClrType == typeof(decimal);
    }

    private static Expression? TryEmitDecimalArithmetic(
        Node left, Node right, Expression leftVal, Expression rightVal,
        Func<Expression, Expression, BinaryExpression> factory, AbiCtx ctx) {
        if (!(IsDecimalValue(ctx, left) || IsDecimalValue(ctx, right)))
            return null;
        var l = AsDecimal(left, leftVal, ctx);
        var r = AsDecimal(right, rightVal, ctx);
        var computed = factory(l, r);
        var handle = Call(ctx.HeapLocal, HeapAllocate, Convert(computed, typeof(object)));
        return Convert(handle, typeof(long));
    }

    private static Expression AsDecimal(Node node, Expression ringVal, AbiCtx ctx) {
        if (IsDecimalValue(ctx, node)) {
            return Convert(
                Call(ctx.HeapLocal, HeapUnsafeGet, Convert(ringVal, typeof(int))),
                typeof(decimal));
        }
        return Convert(ringVal, typeof(decimal));
    }

    private static bool IsStringValue(AbiCtx ctx, Node node) {
        var meta = ctx.Analysis?.GetMetadata<ValueRepresentationMetadata>(node);
        if (meta?.ClrType == typeof(string)) return true;
        return node is Constant { Value: string };
    }

    private static Expression EmitNotValue(Not n, AbiCtx ctx) =>
        Condition(Equal(CompileValue(n.Value, ctx), Constant(0L)), Constant(1L), Constant(0L));

    private static Expression EmitUnaryMinusValue(UnaryMinus n, AbiCtx ctx) {
        var v = CompileValue(n.Operand, ctx);
        return IsDoubleValue(ctx, n.Operand)
            ? Call(BitConverterDoubleToInt64Bits, Negate(Call(BitConverterInt64BitsToDouble, v)))
            : Negate(v);
    }

    private static Expression EmitBitwiseNotValue(BitwiseNot n, AbiCtx ctx) =>
        Not(CompileValue(n.Operand, ctx));

    private static Expression EmitPopCountValue(PopCount pc, AbiCtx ctx) =>
        Convert(Call(null, BitOperationsPopCount,
            Convert(CompileValue(pc.Operand, ctx), typeof(ulong))), typeof(long));

    private static Expression EmitConditionalValue(Conditional c, AbiCtx ctx) =>
        Condition(CompileConditionAsBool(c.Condition, ctx),
            CompileValue(c.IfTrue, ctx), CompileValue(c.IfFalse, ctx));

    private static Expression EmitCoalesceValue(Coalesce n, AbiCtx ctx) {
        var tmp = Variable(typeof(long), "_coal");
        var leftVal = CompileValue(n.LeftHandValue, ctx);
        var rightVal = CompileValue(n.RightHandValue, ctx);
        Expression whenLeft = UsesAbiNullSentinel(ctx, n.LeftHandValue)
            ? UnboxIfHeapNullable(ctx, n.LeftHandValue, tmp)
            : tmp;
        return Block([tmp],
            Assign(tmp, leftVal),
            Condition(
                UsesAbiNullSentinel(ctx, n.LeftHandValue)
                    ? NotEqual(tmp, Constant(0L))
                    : Constant(true),
                whenLeft,
                rightVal));
    }

    private static bool UsesAbiNullSentinel(AbiCtx ctx, Node node) {
        if (node is Constant { Value: null }) return true;
        var meta = ctx.Analysis?.GetMetadata<ValueRepresentationMetadata>(node);
        if (meta?.Kind == ValueRepresentationKind.HeapRef) return true;
        if (meta?.ClrType is Type t && Nullable.GetUnderlyingType(t) is not null) return true;
        return false;
    }

    private static Expression UnboxIfHeapNullable(AbiCtx ctx, Node node, Expression handle) {
        var meta = ctx.Analysis?.GetMetadata<ValueRepresentationMetadata>(node);
        var clr = meta?.ClrType;
        var underlying = clr is null ? null : Nullable.GetUnderlyingType(clr);
        if (underlying is null || !AbiValueTypes.IsLongRepresentable(underlying))
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