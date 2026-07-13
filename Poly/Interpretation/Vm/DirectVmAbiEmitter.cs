using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax.Analysis;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

/// <summary>Direct AST-to-VM-ABI emitter — the primary (and sole) compilation path.
/// Walks the analyzed AST and emits <see cref="Expression"/> trees targeting the
/// bespoke VM ABI (<see cref="VmState"/>, ring registers, 2-word frame model,
/// heap). No intermediate primitive flattening or expansion step exists.</summary>
/// <remarks>
/// <para>This is the canonical lowering path described in
/// <see href=\"../../../../docs/decisions/2026-07-04-primitives-as-canonical-ir.md\">primitives-as-canonical-ir.md</see>.
/// The emitter produces a single <c>Action&lt;VmState&gt;</c> delegate per program.
/// All control flow (loops, branches, try/catch/finally) uses native CLR Expression
/// nodes — no side tables or handler dispatching.</para>
///
/// <para>Key design points:</para>
/// <list type=\"bullet\">
///   <item><b>Ring registers</b> — Values flow through <c>_r0.._rN</c> locals
///   allocated inline during the AST walk. No global pre-pass (RingAllocator).</item>
///   <item><b>Structured EH</b> — Uses native <c>Expression.TryCatchFinally</c>
///   directly for TryCatchFinally nodes.</item>
///   <item><b>Frame model</b> — 2-word header (previousFP + savedSP) with
///   compile-time-known argument/local counts.</item>
///   <item><b>Debug modes</b> — <see cref="CompilationMode.Normal"/> includes
///   DebugHook and PC tracking; <see cref="CompilationMode.NoDebug"/> omits
///   them for maximum speed.</item>
/// </list>
/// </remarks>
public static class DirectVmAbiEmitter {
    /// <summary>
    /// Describes a captured variable: the <see cref="Variable"/> node and its
    /// slot index in the <em>outer</em> (capturing) scope's value stack.
    /// </summary>
    private sealed record Capture(Variable Variable, int OuterSlotIndex);

    /// <summary>Emits a compiled <see cref="VmProgram"/> from an analyzed AST
    /// root node, targeting the bespoke VM ABI directly. This is the canonical
    /// compilation path — no primitives or intermediate IR.</summary>
    /// <param name="root">The AST root node to compile.</param>
    /// <param name="analysis">Analysis result from the standard pipeline
    /// (without PrimitiveExpansion). Must contain all metadata the emitter
    /// requires (types, side effects, value representation, etc.).</param>
    /// <param name="mode">Compilation mode. <see cref="CompilationMode.Normal"/>
    /// (default) includes debug/trace hooks; <see cref="CompilationMode.NoDebug"/>
    /// omits them for peak performance.</param>
    /// <returns>A <see cref="VmProgram"/> containing the compiled delegate and
    /// metadata (function table, call sites, step nodes, debug info). Execute
    /// via <see cref="Interpreter.Execute(VmProgram, Action{VmState})"/>.</returns>
    public static VmProgram Emit(
        Node root,
        AnalysisResult analysis,
        CompilationMode mode = CompilationMode.Normal) {

        var ctx = new AbiCtx();
        ctx.Mode = mode;
        ctx.Analysis = analysis;
        var body = new List<Expression>();

        // ── Pre-collect lambdas and pre-compile function bodies ───
        // Collect lambdas to assign sequential LambdaIndex values.
        var lambdas = new List<Lambda>();
        CollectLambdas(root, lambdas);

        // ── Preamble ────────────────────────────────────────────────
        body.Add(Label(ctx.EntryLabel));
        body.Add(Assign(ctx.SlotsLocal, ctx.SlotsInitExpression));
        body.Add(Assign(ctx.HeapLocal, ctx.HeapInitExpression));
        body.Add(Assign(ctx.Registers,
            Coalesce(ctx.Registers, NewArrayBounds(typeof(long), Constant(256)))));
        // Restore persistent frame position if resuming, otherwise start at slot 0.
        // The condition is a single CompareExchange-style check on status, trivially
        // elided by the CLR JIT on fresh execution (the Resuming branch is never taken).
        body.Add(Assign(ctx.FramePosLocal,
            Condition(
                Equal(Property(ctx.State, nameof(VmState.Status)),
                    Constant(InterpreterStatus.Resuming)),
                Property(ctx.State, nameof(VmState.FramePos)),
                Constant(0))));

        // Track a small step for legacy/compatibility if needed, but the primary
        // "current position" for the direct path is now the AST node itself
        // (set inside CompileNode / at suspend points).
        if (mode != CompilationMode.NoDebug) {
            body.Add(Assign(ctx.ProgramCounter, Constant(0)));
            ctx.DebugHookProp = Property(ctx.State, nameof(VmState.DebugHook));
        }

        // ── PC-dispatch switch ──────────────────────────────────────
        // When resuming from a suspend point, the preamble restores _fp then
        // dispatches on state.ProgramCounter to jump directly to the right
        // pause point.  Fresh executions fall through linearly.
        body.Add(ctx.EmitPcDispatch(Goto(ctx.ExitLabel)));

        // ── Compile root node ────────────────────────────────────────
        // Enter the top-level activation in the compile-time simulator.
        // The root frame has 0 arguments and 0 known locals at this point;
        // DeclareVariable inside blocks will still assign scope-relative slots.
        ctx.EnterActivation(0, 0);

        // CompileStatement wraps the root with CurrentAstNode + DebugHook,
        // consistent with statement-level tracking in EmitBlock.
        var rootExpr = CompileStatement(root, ctx);

        // Flush the root result from the ring to the value stack,
        // matching the ABI convention: result at _slots[_fp], SP = _fp + 1.
        // The top ring slot holds the expression result.
        body.Add(rootExpr);
        if (ctx.RingDepth > 0) {
            body.Add(Assign(ArrayAccess(ctx.SlotsLocal, ctx.FramePosLocal),
                ctx.RingVar(ctx.RingDepth - 1)));
            body.Add(Assign(ctx.SlotsStackPointer,
                Add(ctx.FramePosLocal, Constant(1))));
        }

        // Leave the top-level activation (compile-time simulator bookkeeping).
        ctx.LeaveActivation();

        // ── Exit ─────────────────────────────────────────────────────
        body.Add(Label(ctx.ExitLabel));

        // Determine root value kind for result extraction
        var rootKind = analysis.GetMetadata<ValueRepresentationMetadata>(root)?.Kind;

        // Build and compile the delegate
        var delegateExpr = Lambda<Action<VmState>>(Block(ctx.Locals, body), ctx.State);
        var del = delegateExpr.Compile();

        int registerScratchSize = ctx.MaxRingDepth;
        var debugInfo = new VmDebugInfo(ctx.VariableLayouts);
        return new VmProgram(del, registerScratchSize, RootValueKind: rootKind,
            StepNodes: ctx.StepNodes, DebugInfo: debugInfo,
            RegisterCount: ctx.RegisterCount);
    }

    // ── Compile dispatch ───────────────────────────────────────────

    /// <summary>
    /// Compile a single AST node. In all modes, delegates directly to
    /// <see cref="CompileNodeInner"/> without adding tracking expressions.
    /// Step tracking and node-field assignments are handled by <see cref="EmitBlock"/>
    /// at statement boundaries, avoiding deep call stacks from deeply nested
    /// expression trees (<c>CompileNode->CompileNodeInner->Emit*->CompileNode</c>
    /// recursion that overflows the stack at ~400 expression levels).
    ///
    /// DebugHook support: at statement boundaries <see cref="CompileStatement"/>
    /// inserts a lightweight <c>CurrentAstNode</c> set + null-guarded hook invoke.
    /// The expensive locals span + step recording are exclusive to
    /// <see cref="EmitSuspendNode"/> so Normal mode stays fast.
    /// </summary>
    private static Expression CompileNode(Node node, AbiCtx ctx) {
        // If analysis registered a replacement (e.g. constant folding), use that instead.
        if (ctx.Analysis?.GetNodeReplacement(node) is { } replacement && replacement != node)
            return CompileNode(replacement, ctx);
        return CompileNodeInner(node, ctx);
    }

    /// <summary>
    /// Compile a statement node with DebugHook support.
    /// Before invoking the hook, flushes the register file to <c>_slots</c> so
    /// the locals span contains accurate variable values.  The entire hook path
    /// is inside a runtime null guard — zero overhead when no hook is set.
    /// </summary>
    private static Expression CompileStatement(Node node, AbiCtx ctx) {
        if (ctx.DebugHookProp is null) return CompileNode(node, ctx);

        // Flush register file to _slots so the debug hook span sees current values.
        var stores = ctx.EmitScopeStores();
        var body = CompileNode(node, ctx);

        int localCount = ctx.CurrentLocalCount;
        Expression spanExpr = localCount == 0
            ? New(typeof(ReadOnlySpan<long>).GetConstructor([typeof(long[])])!,
                NewArrayBounds(typeof(long), Constant(0)))
            : New(typeof(ReadOnlySpan<long>).GetConstructor([typeof(long[]), typeof(int), typeof(int)])!,
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

    /// <summary>
    /// Inner dispatch — no interrupt wrapping. Called by <see cref="CompileNode"/>.
    /// </summary>
    private static Expression CompileNodeInner(Node node, AbiCtx ctx) {
        return node switch {
            // All constants — EmitConstant handles numeric, float bits, and heap objects.
            // (Do not route heap constants through CompileValue: that used to call
            // CompileNode back into this arm and stack-overflow.)
            Constant c => EmitConstant(c, ctx),

            // Pure expression trees — CompileValue builds nested Expression nodes
            // (no intermediate ring stores), then SpillToRing once. Complex leaves
            // (Member, heap IndexAccess, Invoke, …) fall back via SpillRingRead
            // inside CompileValue, so statement-level DebugHook is unaffected.
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

            Variable v => SpillToRing(CompileValue(v, ctx), ctx),
            Default d => SpillToRing(CompileValue(d, ctx), ctx),
            ThisReference _ => SpillToRing(CompileValue(new Default(), ctx), ctx),
            ParameterReference pr => SpillToRing(CompileValue(pr, ctx), ctx),
            NullForgiving n => SpillToRing(CompileValue(n, ctx), ctx),
            TypeAs t => SpillToRing(CompileValue(t, ctx), ctx),
            TypeCast t => SpillToRing(CompileValue(t, ctx), ctx),
            Await a => SpillToRing(CompileValue(a, ctx), ctx),

            Not n => SpillToRing(EmitNotValue(n, ctx), ctx),
            UnaryMinus n => SpillToRing(EmitUnaryMinusValue(n, ctx), ctx),
            BitwiseNot n => SpillToRing(EmitBitwiseNotValue(n, ctx), ctx),
            Conditional c => SpillToRing(EmitConditionalValue(c, ctx), ctx),
            Coalesce n => SpillToRing(EmitCoalesceValue(n, ctx), ctx),
            And n => SpillToRing(EmitLogicalAndValue(n, ctx), ctx),
            Or n => SpillToRing(EmitLogicalOrValue(n, ctx), ctx),
            PopCount pc => SpillToRing(EmitPopCountValue(pc, ctx), ctx),

            // Complex expressions — ring path (side effects / heap / reflection)
            Member m => EmitMember(m, ctx),
            TypeIs t => EmitTypeIs(t, ctx),

            // ── Statement / complex nodes ────────────────────────
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

            // Variables and blocks
            Assignment a => EmitAssignment(a, ctx),
            Block b => EmitBlock(b, ctx),

            // Closures and lambdas
            Parameter p => EmitParameter(p, ctx),
            Lambda l => EmitLambda(l, ctx),
            Invoke inv => EmitInvoke(inv, ctx),

            // Allocations and indexing
            New n => EmitNew(n, ctx),
            NewArray n => EmitNewArray(n, ctx),
            IndexAccess n => EmitIndexAccess(n, ctx),
            StridedSetBits ssb => EmitStridedSetBits(ssb, ctx),

            // Switch as conditional chain
            SwitchStatement sw => EmitSwitch(sw, ctx),

            _ => throw new NotSupportedException(
                $"DirectVmAbiEmitter: unsupported node type {node.GetType().Name}")
        };
    }

    // ── Emit helpers ───────────────────────────────────────────────

    /// <summary>Allocate a new ring slot and emit a constant.
    /// Numeric/primitive values are inlined as long; non-numeric (strings, etc.)
    /// are allocated on the heap at runtime, matching the ABI convention.</summary>
    private static Expression EmitConstant(Constant c, AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        if (TryValueToLong(c.Value, out long val))
            return Assign(ctx.RingVar(slot), Constant(val));
        // float/double: store bit pattern directly in the long ring slot
        if (c.Value is double dbl)
            return Assign(ctx.RingVar(slot), Constant(BitConverter.DoubleToInt64Bits(dbl)));
        if (c.Value is float flt)
            return Assign(ctx.RingVar(slot), Constant(BitConverter.DoubleToInt64Bits(flt)));
        // Non-numeric: allocate on heap
        var allocate = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)),
            Convert(Constant(c.Value), typeof(object)));
        return Assign(ctx.RingVar(slot), Convert(allocate, typeof(long)));
    }

    private static Expression EmitNew(New n, AbiCtx ctx) {
        // Resolve target type
        Type? targetType = n.Type switch {
            ClrTypeReference ctr => ctr.RuntimeType,
            _ => null
        };

        if (targetType is null && ctx.Analysis is not null) {
            var resolvedType = ctx.Analysis.GetResolvedType(n);
            if (resolvedType is ClrTypeDefinition clrDef)
                targetType = clrDef.RuntimeType;
        }

        // Resolve constructor from analysis metadata
        ConstructorInfo? ctor = null;
        if (ctx.Analysis is not null) {
            var resolved = ctx.Analysis.GetResolvedMember(n);
            if (resolved is ClrConstructor clrCtor)
                ctor = clrCtor.ConstructorInfo;
        }

        // If we have a target type but no constructor yet, search for matching ctor
        if (ctor is null && targetType is not null) {
            if (n.Arguments.Length == 0) {
                ctor = targetType.GetConstructor(Type.EmptyTypes);
            }
            else {
                ctor = targetType.GetConstructors()
                    .FirstOrDefault(c => c.GetParameters().Length == n.Arguments.Length);
            }
        }

        if (ctor is not null) {
            // Compile arguments onto the ring — track actual result slots
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
                var ringVal = ctx.RingVar(argSlots[i]);
                var paramType = ctorParams[i].ParameterType;
                if (paramType.IsValueType) {
                    ctorArgs[i] = Convert(ringVal, paramType);
                }
                else if (paramType == typeof(string)) {
                    ctorArgs[i] = Convert(
                        Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                            Convert(ringVal, typeof(int))),
                        paramType);
                }
                else {
                    ctorArgs[i] = Convert(
                        Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                            Convert(ringVal, typeof(int))),
                        paramType);
                }
            }

            var newExpr = New(ctor, ctorArgs);
            var boxed = Convert(newExpr, typeof(object));
            int slot = ctx.AllocSlot();
            var handle = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)), boxed);
            ctx.RingDepth = slot + 1;
            return Block(argExprs.Concat([Assign(ctx.RingVar(slot), Convert(handle, typeof(long)))]));
        }

        // Fallback: store the value directly on heap if it's a constant
        if (targetType is not null && n.Arguments.Length == 0) {
            Expression defaultObj = targetType.IsValueType
                ? Convert(Constant(0L), typeof(object))
                : Constant(null, typeof(object));
            int slot2 = ctx.AllocSlot();
            var handle2 = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)), defaultObj);
            return Assign(ctx.RingVar(slot2), Convert(handle2, typeof(long)));
        }

        // Last resort: empty object[]
        int slot3 = ctx.AllocSlot();
        var placeholder = NewArrayBounds(typeof(object), Constant(0));
        var handle3 = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)),
            Convert(placeholder, typeof(object)));
        return Assign(ctx.RingVar(slot3), Convert(handle3, typeof(long)));
    }

    private static Expression EmitNewArray(NewArray n, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var lenExpr = CompileNode(n.Length, ctx);
        int lenSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref lenSlot, d, ctx);

        int slot = ctx.AllocSlot();
        // Resolve element type: value types like long[] store unboxed values,
        // reference types and unknown types use object[].
        Type elemType = n.ElementType switch {
            ClrTypeReference ctr when ctr.RuntimeType.IsValueType => ctr.RuntimeType,
            _ => typeof(object)
        };

        // Small fixed-size value-type arrays: allocate in frame slots instead of
        // on the heap.  The handle is the absolute slot base (>= SmallArraySlotBase).
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
        var handle = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)),
            Convert(arr, typeof(object)));
        return Block(lenExpr, fold, Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));
    }

    private static Expression EmitIndexAccess(IndexAccess n, AbiCtx ctx) {
        // Compile-time fast path: tracked frame-local variable → pure value tree.
        if (n.Value is Variable arrVar && ctx.TryGetFrameLocalBase(arrVar) is int) {
            return SpillToRing(EmitIndexAccessValue(n, ctx), ctx);
        }

        var arrExpr = CompileNode(n.Value, ctx);
        int arrSlot = ctx.RingDepth - 1;

        var idxExpr = CompileNode(n.Arguments.Length > 0 ? n.Arguments[0] : new Constant(0), ctx);
        int idxSlot = ctx.RingDepth - 1;

        int outSlot = ctx.AllocSlot();

        var rawObj = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
            Convert(ctx.RingVar(arrSlot), typeof(int)));
        var idx = Convert(ctx.RingVar(idxSlot), typeof(int));

        // Resolve element type from analysis to skip runtime TypeIs check.
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

        // Unknown element type — runtime TypeIs check fallback
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

    /// <summary>Try to convert a CLR value to the long-based ABI representation.
    /// Returns true for numeric/primitive types that inline directly.</summary>
    private static bool TryValueToLong(object? value, out long result) {
        switch (value) {
            case null: result = 0; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case bool b: result = b ? 1 : 0; return true;
            case short s: result = s; return true;
            case byte bVal: result = bVal; return true;
            default: result = 0; return false;
        }
    }

    /// <summary>
    /// If the operand used more ring slots than expected, fold its result back to slot <paramref name="d"/>
    /// so the caller can reliably find it at <c>RingVar(d)</c>. Returns an expression that does the copy
    /// (or <see cref="Empty()"/> if no copy is needed), and sets <paramref name="resultSlot"/> to <paramref name="d"/>.
    /// </summary>
    private static Expression FoldResultToSlot(ref int resultSlot, int d, AbiCtx ctx) {
        if (resultSlot <= d) return Empty();
        var copy = Assign(ctx.RingVar(d), ctx.RingVar(resultSlot));
        resultSlot = d;
        ctx.RingDepth = d + 1;
        return copy;
    }

    /// <summary>Find the highest set bit position (0-based) for a positive power of two.</summary>
    private static int BitScanReverse(long v) {
        int pos = 0;
        while (v > 1) { v >>= 1; pos++; }
        return pos;
    }

    // ── CompileValue: expression-returning compilation ────────────

    /// <summary>Compile a node to an Expression that produces its value on the
    /// LINQ eval stack — no ring slot.</summary>
    private static Expression CompileValue(Node node, AbiCtx ctx) {
        return node switch {
            // Numeric constants: eval stack only, no ring
            Constant c when TryValueToLong(c.Value, out _) || c.Value is double || c.Value is float
                => EmitConstantValue(c),
            // String/heap constants: allocate on heap via EmitConstant (not CompileNode —
            // avoids CompileValue↔CompileNodeInner recursion on object constants).
            Constant c => SpillRingRead(EmitConstant(c, ctx), ctx),
            Variable v => ctx.VariableRead(v),
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
            Default _ => Constant(0L),
            ThisReference _ => Constant(0L),
            ParameterReference _ => Constant(0L),
            NullForgiving n => CompileValue(n.Operand, ctx),
            TypeAs ta => CompileValue(ta.Operand, ctx),
            TypeCast tc => CompileValue(tc.Operand, ctx),
            Await a => CompileValue(a.Operand, ctx),

            // Frame-local small arrays: pure _slots[base+i] expression (no ring).
            IndexAccess n => EmitIndexAccessValue(n, ctx),

            // Complex expression nodes: route through ring path.
            // Must be explicit (not a fallback) to prevent unbounded recursion.
            // Block is used as an expression (e.g. mandelbrot pixel block yielding `iter`).
            Block b => SpillRingRead(CompileNode(b, ctx), ctx),
            Member m => SpillRingRead(CompileNode(m, ctx), ctx),
            New n => SpillRingRead(CompileNode(n, ctx), ctx),
            NewArray n => SpillRingRead(CompileNode(n, ctx), ctx),
            Parameter p => SpillRingRead(CompileNode(p, ctx), ctx),
            Invoke inv => SpillRingRead(CompileNode(inv, ctx), ctx),
            TypeIs t => SpillRingRead(CompileNode(t, ctx), ctx),
            SwitchStatement sw => SpillRingRead(CompileNode(sw, ctx), ctx),
            StridedSetBits ssb => SpillRingRead(CompileNode(ssb, ctx), ctx),
            _ => throw new NotSupportedException(
                $"CompileValue: unhandled {node.GetType().Name}")
        };
    }

    /// <summary>
    /// IndexAccess as a value expression. Frame-local arrays lower to a pure
    /// <c>_slots[base + idx]</c> tree; heap arrays fall back to the ring path.
    /// </summary>
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

    /// <summary>Allocate a ring slot and assign a value expression to it.</summary>
    private static Expression SpillToRing(Expression value, AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        ctx.RingDepth = slot + 1;
        return Assign(ctx.RingVar(slot), value);
    }

    /// <summary>Wrap a ring-store expression and return the ring variable.</summary>
    private static Expression SpillRingRead(Expression compiled, AbiCtx ctx) {
        int slot = ctx.RingDepth - 1;
        return Block(compiled, ctx.RingVar(slot));
    }

    // ── Value-returning helpers (eval stack, no ring) ────────────

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
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right))
            return Call(BitConverterDoubleToInt64Bits,
                factory(Call(BitConverterInt64BitsToDouble, leftVal),
                        Call(BitConverterInt64BitsToDouble, rightVal)));
        if (right is Constant rc && TryValueToLong(rc.Value, out long rv) && rv > 0 && (rv & (rv - 1)) == 0) {
            if (factory.Method.Name == nameof(Expression.Modulo))
                return Expression.And(leftVal, Constant(rv - 1));
            if (factory.Method.Name == nameof(Expression.Divide))
                return Expression.RightShift(leftVal, Constant(BitScanReverse(rv)));
            if (factory.Method.Name == nameof(Expression.Multiply))
                return Expression.LeftShift(leftVal, Constant(BitScanReverse(rv)));
        }
        var rhs = rightVal;
        if (factory == LeftShift || factory == RightShift) rhs = Convert(rhs, typeof(int));
        return factory(leftVal, rhs);
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
        Convert(Call(null, typeof(System.Numerics.BitOperations)
            .GetMethod(nameof(System.Numerics.BitOperations.PopCount), [typeof(ulong)])!,
            Convert(CompileValue(pc.Operand, ctx), typeof(ulong))), typeof(long));

    private static Expression EmitConditionalValue(Conditional c, AbiCtx ctx) =>
        Condition(CompileConditionAsBool(c.Condition, ctx),
            CompileValue(c.IfTrue, ctx), CompileValue(c.IfFalse, ctx));

    /// <summary>
    /// Coalesce with single evaluation of the left operand (temp local in the
    /// expression tree). Right is only evaluated when left is 0.
    /// </summary>
    private static Expression EmitCoalesceValue(Coalesce n, AbiCtx ctx) {
        var tmp = Variable(typeof(long), "_coal");
        return Block([tmp],
            Assign(tmp, CompileValue(n.LeftHandValue, ctx)),
            Condition(NotEqual(tmp, Constant(0L)), tmp, CompileValue(n.RightHandValue, ctx)));
    }

    private static Expression EmitLogicalAndValue(And n, AbiCtx ctx) =>
        Condition(Equal(CompileValue(n.LeftHandValue, ctx), Constant(0L)),
            Constant(0L),
            CompileValue(n.RightHandValue, ctx));

    private static Expression EmitLogicalOrValue(Or n, AbiCtx ctx) =>
        Condition(NotEqual(CompileValue(n.LeftHandValue, ctx), Constant(0L)),
            Constant(1L),
            CompileValue(n.RightHandValue, ctx));

    /// <summary>
    /// Compile a condition to a <see cref="bool"/>-typed Expression for use in
    /// <c>IfThen</c>/<c>Loop</c>/<c>Condition</c> tests — fused comparisons skip
    /// the intermediate 0/1 long materialization.
    /// </summary>
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

    /// <summary>Comparison as a bool Expression (no 0/1 long).</summary>
    private static Expression CompileCompareTest(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        AbiCtx ctx) {
        var lv = CompileValue(left, ctx);
        var rv = CompileValue(right, ctx);
        bool eq = cf == Equal || cf == NotEqual;
        if (eq && AreHeapValues(ctx, left, right)) {
            var lo = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!, Convert(lv, typeof(int)));
            var ro = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!, Convert(rv, typeof(int)));
            var ec = Call(typeof(object).GetMethod("Equals", [typeof(object), typeof(object)])!, lo, ro);
            return cf == Equal ? ec : Not(ec);
        }
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right))
            return cf(Call(BitConverterInt64BitsToDouble, lv), Call(BitConverterInt64BitsToDouble, rv));
        return cf(lv, rv);
    }

    private static Expression EmitComparisonValue(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        AbiCtx ctx, Node? cn = null) {
        var lv = CompileValue(left, ctx);
        var rv = CompileValue(right, ctx);
        bool eq = cf == Equal || cf == NotEqual;
        if (eq && AreHeapValues(ctx, left, right)) {
            var lo = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!, Convert(lv, typeof(int)));
            var ro = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!, Convert(rv, typeof(int)));
            var ec = Call(typeof(object).GetMethod("Equals", [typeof(object), typeof(object)])!, lo, ro);
            return Condition(ec, cf == Equal ? Constant(1L) : Constant(0L),
                                cf == Equal ? Constant(0L) : Constant(1L));
        }
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right))
            return Condition(cf(Call(BitConverterInt64BitsToDouble, lv), Call(BitConverterInt64BitsToDouble, rv)),
                Constant(1L), Constant(0L));
        return Condition(cf(lv, rv), Constant(1L), Constant(0L));
    }

    // ── Ring-based expression helpers ──
    // Retained for paths that still walk operands via CompileNode (Member, etc.).
    // Pure expression dispatch uses CompileValue + SpillToRing instead.

    /// <summary>Binary arithmetic — ring-based (operands through CompileNode).</summary>
    private static Expression EmitBinaryArithmeticRing(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        AbiCtx ctx) {
        int d = ctx.RingDepth;
        var leftCompiled = CompileNode(left, ctx);
        int leftSlot = ctx.RingDepth - 1;
        var rightCompiled = CompileNode(right, ctx);
        int rightSlot = ctx.RingDepth - 1;

        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right)) {
            var resultBits = Call(BitConverterDoubleToInt64Bits,
                factory(Call(BitConverterInt64BitsToDouble, ctx.RingVar(leftSlot)),
                        Call(BitConverterInt64BitsToDouble, ctx.RingVar(rightSlot))));
            ctx.RingDepth = d + 1;
            return Block(leftCompiled, rightCompiled, Assign(ctx.RingVar(d), resultBits));
        }

        if (right is Constant rc && TryValueToLong(rc.Value, out long rv) && rv > 0 && (rv & (rv - 1)) == 0) {
            if (factory.Method.Name == nameof(Expression.Modulo)) {
                ctx.RingDepth = d + 1;
                return Block(leftCompiled, rightCompiled,
                    Assign(ctx.RingVar(d), Expression.And(ctx.RingVar(leftSlot), Constant(rv - 1))));
            }
            if (factory.Method.Name == nameof(Expression.Divide)) {
                ctx.RingDepth = d + 1;
                return Block(leftCompiled, rightCompiled,
                    Assign(ctx.RingVar(d), Expression.RightShift(ctx.RingVar(leftSlot), Constant(BitScanReverse(rv)))));
            }
            if (factory.Method.Name == nameof(Expression.Multiply)) {
                ctx.RingDepth = d + 1;
                return Block(leftCompiled, rightCompiled,
                    Assign(ctx.RingVar(d), Expression.LeftShift(ctx.RingVar(leftSlot), Constant(BitScanReverse(rv)))));
            }
        }

        Expression rhs = ctx.RingVar(rightSlot);
        if (factory == LeftShift || factory == RightShift)
            rhs = Convert(rhs, typeof(int));
        ctx.RingDepth = d + 1;
        return Block(leftCompiled, rightCompiled, Assign(ctx.RingVar(d), factory(ctx.RingVar(leftSlot), rhs)));
    }

    /// <summary>Comparison — ring-based (operands through CompileNode).</summary>
    private static Expression EmitComparisonRing(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> cf,
        AbiCtx ctx, Node? cn = null) {
        int d = ctx.RingDepth;
        var leftCompiled = CompileNode(left, ctx);
        int leftSlot = ctx.RingDepth - 1;
        var rightCompiled = CompileNode(right, ctx);
        int rightSlot = ctx.RingDepth - 1;

        bool eq = cf == Equal || cf == NotEqual;
        if (eq && AreHeapValues(ctx, left, right)) {
            var lo = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!, Convert(ctx.RingVar(leftSlot), typeof(int)));
            var ro = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!, Convert(ctx.RingVar(rightSlot), typeof(int)));
            var ec = Call(typeof(object).GetMethod("Equals", [typeof(object), typeof(object)])!, lo, ro);
            ctx.RingDepth = d + 1;
            return Block(leftCompiled, rightCompiled, Assign(ctx.RingVar(d),
                Condition(ec, cf == Equal ? Constant(1L) : Constant(0L),
                              cf == Equal ? Constant(0L) : Constant(1L))));
        }
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right)) {
            ctx.RingDepth = d + 1;
            return Block(leftCompiled, rightCompiled, Assign(ctx.RingVar(d),
                Condition(cf(Call(BitConverterInt64BitsToDouble, ctx.RingVar(leftSlot)),
                              Call(BitConverterInt64BitsToDouble, ctx.RingVar(rightSlot))),
                    Constant(1L), Constant(0L))));
        }
        ctx.RingDepth = d + 1;
        return Block(leftCompiled, rightCompiled, Assign(ctx.RingVar(d),
            Condition(cf(ctx.RingVar(leftSlot), ctx.RingVar(rightSlot)), Constant(1L), Constant(0L))));
    }

    /// <summary>Binary arithmetic — uses CompileValue for operands,</summary>
    private static Expression EmitBinaryArithmetic(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> factory,
        AbiCtx ctx) {
        var leftVal = CompileValue(left, ctx);
        var rightVal = CompileValue(right, ctx);
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right))
            return SpillToRing(Call(BitConverterDoubleToInt64Bits,
                factory(Call(BitConverterInt64BitsToDouble, leftVal),
                        Call(BitConverterInt64BitsToDouble, rightVal))), ctx);
        if (right is Constant rc && TryValueToLong(rc.Value, out long rv) && rv > 0 && (rv & (rv - 1)) == 0) {
            if (factory.Method.Name == nameof(Expression.Modulo))
                return SpillToRing(Expression.And(leftVal, Constant(rv - 1)), ctx);
            if (factory.Method.Name == nameof(Expression.Divide))
                return SpillToRing(Expression.RightShift(leftVal, Constant(BitScanReverse(rv))), ctx);
            if (factory.Method.Name == nameof(Expression.Multiply))
                return SpillToRing(Expression.LeftShift(leftVal, Constant(BitScanReverse(rv))), ctx);
        }
        var rhs = rightVal;
        if (factory == LeftShift || factory == RightShift) rhs = Convert(rhs, typeof(int));
        return SpillToRing(factory(leftVal, rhs), ctx);
    }

    /// <summary>Comparison (eq, neq, lt, gt, etc.) → 0/1 long.
    /// For Equal/NotEqual, heap reference values (strings, objects) are compared
    /// using object.Equals at runtime rather than handle equality.</summary>
    private static Expression EmitComparison(
        Node left, Node right,
        Func<Expression, Expression, BinaryExpression> comparisonFactory,
        AbiCtx ctx,
        Node? comparisonNode = null) {
        int d = ctx.RingDepth;
        var leftExpr = CompileNode(left, ctx);
        int leftResult = ctx.RingDepth - 1;

        var rightExpr = CompileNode(right, ctx);
        int rightResult = ctx.RingDepth - 1;

        // Detect string comparison: when Equal/NotEqual and both operands are
        // heap references (strings), compare via object.Equals at runtime.
        bool isEquality = comparisonFactory == Equal
                       || comparisonFactory == NotEqual;
        if (isEquality && AreHeapValues(ctx, left, right)) {
            // Read both objects from heap and compare
            var leftObj = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                Convert(ctx.RingVar(leftResult), typeof(int)));
            var rightObj = Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                Convert(ctx.RingVar(rightResult), typeof(int)));
            var equalCheck = Call(typeof(object).GetMethod("Equals", [typeof(object), typeof(object)])!,
                leftObj, rightObj);
            var result = Assign(ctx.RingVar(d),
                comparisonFactory == Equal
                    ? Condition(equalCheck, Constant(1L), Constant(0L))
                    : Condition(equalCheck, Constant(0L), Constant(1L)));
            ctx.RingDepth = d + 1;
            return Block(leftExpr, rightExpr, result);
        }

        // Double/float comparison: reinterpret bits before comparing
        if (IsDoubleValue(ctx, left) || IsDoubleValue(ctx, right)) {
            var leftDbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(leftResult));
            var rightDbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(rightResult));
            var result = Assign(ctx.RingVar(d),
                Condition(comparisonFactory(leftDbl, rightDbl),
                    Constant(1L), Constant(0L)));
            ctx.RingDepth = d + 1;
            return Block(leftExpr, rightExpr, result);
        }

        var simpleResult = Assign(
            ctx.RingVar(d),
            Condition(comparisonFactory(ctx.RingVar(leftResult), ctx.RingVar(rightResult)),
                Constant(1L), Constant(0L)));
        ctx.RingDepth = d + 1;
        return Block(leftExpr, rightExpr, simpleResult);
    }

    /// <summary>Check if both nodes likely produce heap reference values that

    /// <summary>Check if both nodes likely produce heap reference values that
    /// should be compared by value rather than handle.</summary>
    private static bool AreHeapValues(AbiCtx ctx, Node left, Node right) {
        // Check analysis metadata for value representation
        if (ctx.Analysis is not null) {
            var leftRep = ctx.Analysis.GetValueRepresentation(left);
            var rightRep = ctx.Analysis.GetValueRepresentation(right);
            if (leftRep == ValueRepresentationKind.HeapRef
                || rightRep == ValueRepresentationKind.HeapRef)
                return true;
        }
        // Heuristic: string constants produce heap handles
        if (left is Constant cl && cl.Value is string) return true;
        if (right is Constant cr && cr.Value is string) return true;
        // Member access on CLR objects may produce heap values
        if (left is Member) return true;
        if (right is Member) return true;
        return false;
    }

    /// <summary>Short-circuit AND: if left is false, skip right.</summary>
    private static Expression EmitLogicalAnd(And and, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var leftExpr = CompileNode(and.LeftHandValue, ctx);
        int leftSlot = ctx.RingDepth - 1;
        var foldLeft = FoldResultToSlot(ref leftSlot, d, ctx);

        int rightStart = ctx.RingDepth;
        var rightExpr = CompileNode(and.RightHandValue, ctx);
        int rightSlot = ctx.RingDepth - 1;

        var result = Assign(ctx.RingVar(d),
            Block(
                leftExpr, foldLeft,
                Condition(
                    Equal(ctx.RingVar(leftSlot), Constant(0L)),
                    Constant(0L),
                    Block(
                        rightExpr,
                        ctx.RingVar(rightSlot)
                    )
                )
            ));
        ctx.RingDepth = d + 1;
        return result;
    }

    /// <summary>Short-circuit OR: if left is true, skip right.</summary>
    private static Expression EmitLogicalOr(Or or, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var leftExpr = CompileNode(or.LeftHandValue, ctx);
        int leftSlot = ctx.RingDepth - 1;
        var foldLeft = FoldResultToSlot(ref leftSlot, d, ctx);

        int rightStart = ctx.RingDepth;
        var rightExpr = CompileNode(or.RightHandValue, ctx);
        int rightSlot = ctx.RingDepth - 1;

        var result = Assign(ctx.RingVar(d),
            Block(
                leftExpr, foldLeft,
                Condition(
                    NotEqual(ctx.RingVar(leftSlot), Constant(0L)),
                    Constant(1L),
                    Block(
                        rightExpr,
                        ctx.RingVar(rightSlot)
                    )
                )
            ));
        ctx.RingDepth = d + 1;
        return result;
    }

    /// <summary>Logical NOT: 0→1, anything else→0.</summary>
    private static Expression EmitNot(Not not, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(not.Value, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        var result = Assign(ctx.RingVar(resultSlot),
            Condition(Equal(ctx.RingVar(resultSlot), Constant(0L)), Constant(1L), Constant(0L)));
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    /// <summary>Unary minus: negate value.</summary>
    private static Expression EmitUnaryMinus(UnaryMinus unaryMinus, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(unaryMinus.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        Expression result;
        if (IsDoubleValue(ctx, unaryMinus.Operand)) {
            var dbl = Call(BitConverterInt64BitsToDouble, ctx.RingVar(resultSlot));
            result = Assign(ctx.RingVar(resultSlot),
                Call(BitConverterDoubleToInt64Bits, Negate(dbl)));
        }
        else {
            result = Assign(ctx.RingVar(resultSlot), Negate(ctx.RingVar(resultSlot)));
        }
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    private static Expression EmitBitwiseNot(BitwiseNot n, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(n.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        var result = Assign(ctx.RingVar(resultSlot), Not(ctx.RingVar(resultSlot)));
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    /// <summary>PopCount via System.Numerics.BitOperations.PopCount.</summary>
    private static Expression EmitPopCount(PopCount pc, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(pc.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);
        var call = Call(null,
            typeof(System.Numerics.BitOperations).GetMethod(nameof(System.Numerics.BitOperations.PopCount), [typeof(ulong)])!,
            Convert(ctx.RingVar(resultSlot), typeof(ulong)));
        var result = Assign(ctx.RingVar(resultSlot), Convert(call, typeof(long)));
        ctx.RingDepth = resultSlot + 1;
        return Block(operandExpr, fold, result);
    }

    /// <summary>Member access via CLR reflection: resolve from analysis metadata
    /// and emit a property getter, field read, or method call.</summary>
    private static Expression EmitMember(Member m, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var instanceExpr = CompileNode(m.Value, ctx);
        int instanceSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref instanceSlot, d, ctx);

        var resolved = ctx.Analysis?.GetResolvedMember(m);

        // Static member — no instance needed
        if (resolved?.LifetimeModifier == LifetimeModifier.Static) {
            return EmitResolvedMember(resolved, null, d, ctx, Block(instanceExpr, fold));
        }

        if (resolved is not null) {
            var declaringTypeDef = resolved.DeclaringTypeDefinition;
            bool isValueType = declaringTypeDef is ClrTypeDefinition clrDef
                && clrDef.RuntimeType.IsValueType;

            Expression instanceObj;
            if (isValueType) {
                instanceObj = Convert(ctx.RingVar(instanceSlot), typeof(object));
            }
            else {
                instanceObj = Call(ctx.HeapLocal,
                    typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                    Convert(ctx.RingVar(instanceSlot), typeof(int)));
            }
            return EmitResolvedMember(resolved, instanceObj, d, ctx, Block(instanceExpr, fold));
        }

        // No metadata — fallback passthrough
        return instanceExpr;
    }

    /// <summary>Emit the resolved member access expression and store the result
    /// on the ring.  Uses the member's <see cref="ITypeMember.EmitRead"/> hook so
    /// the emitter stays provider-agnostic — CLR types, AST-backed types, and
    /// future provider types each return their own expression trees.</summary>
    private static Expression EmitResolvedMember(
        ITypeMember resolved,
        Expression? instanceObj,
        int resultSlot,
        AbiCtx ctx,
        Expression instanceExpr) {

        // Polymorphic EmitRead — each ITypeMember implementation provides its
        // own expression tree. CLR properties use Property(inst, propInfo),
        // AST properties use Dictionary indexer, fields use Field(inst, fieldInfo).
        if (resolved.EmitRead(instanceObj) is Expression readExpr) {
            return Block(instanceExpr, Assign(ctx.RingVar(resultSlot),
                ConvertMemberResult(readExpr, resolved, ctx)));
        }

        // Parameterless method call (e.g. ToString, GetHashCode) — invoke via MethodInfo.
        // Methods don't have an EmitRead path; they need explicit invocation.
        if (resolved is ITypeMethod method) {
            var clrMethod = resolved as ClrMethod;
            var methodInfo = clrMethod?.MethodInfo;
            if (methodInfo is not null && methodInfo.GetParameters().Length == 0) {
                Expression? instanceForCall = instanceObj;
                if (instanceObj is not null && methodInfo.DeclaringType?.IsValueType == true) {
                    instanceForCall = Convert(instanceObj, methodInfo.DeclaringType);
                }
                Expression resultExpr = instanceForCall is not null
                    ? Call(instanceForCall, methodInfo)
                    : Call(null, methodInfo);
                return Block(instanceExpr, Assign(ctx.RingVar(resultSlot),
                    ConvertMemberResult(resultExpr, resolved, ctx)));
            }
        }

        // Fallback: return instance
        return instanceExpr;
    }

    /// <summary>Convert a member access result (object?) to the ring ABI (long).
    /// Value types are unboxed to long; reference types are heap-allocated.</summary>
    private static Expression ConvertMemberResult(Expression readCall, ITypeMember resolved, AbiCtx ctx) {
        // Try to determine if the member type is a value type via CLR metadata.
        var memberTypeDef = resolved.MemberTypeDefinition;
        if (memberTypeDef is ClrTypeDefinition clrDef) {
            var clrType = clrDef.RuntimeType;
            if (clrType.IsValueType) {
                // Unbox: (long)(T)(object?)readCall
                return Convert(Convert(readCall, clrType), typeof(long));
            }
        }
        // Reference type: allocate on heap and return handle
        var handle = Call(ctx.HeapLocal,
            typeof(Heap).GetMethod(nameof(Heap.Allocate))!,
            readCall);
        return Convert(handle, typeof(long));
    }

    /// <summary>TypeIs: check if the operand's heap object is assignable to the target type.</summary>
    private static Expression EmitTypeIs(TypeIs t, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var operandExpr = CompileNode(t.Operand, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);

        // Resolve target type from TypeReference via analysis or CLR type lookup
        Type? targetType = null;
        if (t.TargetTypeReference is ClrTypeReference clrRef) {
            targetType = clrRef.RuntimeType;
        }
        // Try analysis metadata fallback
        if (targetType is null && ctx.Analysis is not null) {
            var resolvedType = ctx.Analysis.GetResolvedType(t);
            if (resolvedType is ClrTypeDefinition clrDef) {
                targetType = clrDef.RuntimeType;
            }
        }

        if (targetType is null) {
            // Cannot resolve — return 0 (false)
            return Block(operandExpr, fold, Assign(ctx.RingVar(resultSlot), Constant(0L)));
        }

        // Read heap object and check type: _heap.UnsafeGet((int)handle) is TargetType
        var heapObj = Call(ctx.HeapLocal,
            typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
            Convert(ctx.RingVar(resultSlot), typeof(int)));
        var typeCheck = TypeIs(heapObj, targetType);
        var result = Condition(typeCheck, Constant(1L), Constant(0L));
        return Block(operandExpr, fold, Assign(ctx.RingVar(resultSlot), result));
    }

    /// <summary>TypeAs: Expression.TypeAs(operand, targetType).</summary>
    private static Expression EmitTypeAs(TypeAs t, AbiCtx ctx) {
        // Passthrough for POC — same as EmitMember passthrough
        return CompileNode(t.Operand, ctx);
    }

    /// <summary>TypeCast: Expression.Convert(operand, targetType).</summary>
    private static Expression EmitTypeCast(TypeCast t, AbiCtx ctx) {
        // Passthrough for POC — values are already long in the ABI
        return CompileNode(t.Operand, ctx);
    }

    /// <summary>Await: synchronous extraction via GetAwaiter().GetResult().</summary>
    private static Expression EmitAwait(Await a, AbiCtx ctx) {
        // Passthrough for POC — the operand value is the result
        return CompileNode(a.Operand, ctx);
    }

    /// <summary>Default: produce the default value for a type (0 for long/scalar).</summary>
    private static Expression EmitDefault(Default d, AbiCtx ctx) {
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot), Constant(0L));
    }

    /// <summary>ParameterReference: resolve to the referenced Parameter node and emit it.</summary>
    private static Expression EmitParameterReference(ParameterReference pr, AbiCtx ctx) {
        // Try to resolve the referenced Parameter via analysis metadata.
        // The DomainExpressionLoweringPass may produce Member(ParameterReference, ...)
        // where ParameterReference aliases a concrete Parameter node from the lowering.
        // Fall back to 0L if unresolvable.
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot), Constant(0L));
    }

    /// <summary>StridedSetBits: bit-level strided set (handle, start, step, limit).</summary>
    private static Expression EmitStridedSetBits(StridedSetBits ssb, AbiCtx ctx) {
        int d = ctx.RingDepth;

        // Compile-time fast path: if the array is a tracked frame-local variable,
        // access _slots[base + elemIdx] directly with no runtime dispatch.
        if (ssb.Array is Variable arrVarS && ctx.TryGetFrameLocalBase(arrVarS) is int flBaseS) {
            var startExprS = CompileNode(ssb.StartValue, ctx);
            int startSlotS = ctx.RingDepth - 1;
            var stepExprS = CompileNode(ssb.Step, ctx);
            int stepSlotS = ctx.RingDepth - 1;
            var limitExprS = CompileNode(ssb.Limit, ctx);
            int limitSlotS = ctx.RingDepth - 1;

            var foldStartS = FoldResultToSlot(ref startSlotS, d, ctx);
            var foldStepS = FoldResultToSlot(ref stepSlotS, d + 1, ctx);
            var foldLimitS = FoldResultToSlot(ref limitSlotS, d + 2, ctx);
            ctx.RingDepth = d + 3;

            var jS = Variable(typeof(long), "_bits_j");
            var elemIdxS = Convert(RightShift(jS, Constant(6)), typeof(int));
            var slotAddr = Add(Constant(flBaseS), elemIdxS);

            var loopStartS = Label("_stride_loop_f");
            var loopEndS = Label("_stride_done_f");
            var loopBodyS = Block(
                Assign(ArrayAccess(ctx.SlotsLocal, slotAddr),
                    Or(ArrayAccess(ctx.SlotsLocal, slotAddr),
                        LeftShift(Constant(1L), Convert(And(jS, Constant(63L)), typeof(int))))),
                Assign(jS, Add(jS, ctx.RingVar(stepSlotS))),
                IfThen(GreaterThan(jS, ctx.RingVar(limitSlotS)), Goto(loopEndS)),
                Goto(loopStartS));
            return Block(startExprS, stepExprS, limitExprS,
                Block([jS],
                    Assign(jS, ctx.RingVar(startSlotS)),
                    Label(loopStartS), loopBodyS, Label(loopEndS)));
        }

        var arrExpr = CompileNode(ssb.Array, ctx);
        int arrSlot = ctx.RingDepth - 1;
        var startExpr = CompileNode(ssb.StartValue, ctx);
        int startSlot = ctx.RingDepth - 1;
        var stepExpr = CompileNode(ssb.Step, ctx);
        int stepSlot = ctx.RingDepth - 1;
        var limitExpr = CompileNode(ssb.Limit, ctx);
        int limitSlot = ctx.RingDepth - 1;

        // Fold all four operands to consecutive slots starting at d
        var foldArr = FoldResultToSlot(ref arrSlot, d, ctx);
        var foldStart = FoldResultToSlot(ref startSlot, d + 1, ctx);
        var foldStep = FoldResultToSlot(ref stepSlot, d + 2, ctx);
        var foldLimit = FoldResultToSlot(ref limitSlot, d + 3, ctx);
        ctx.RingDepth = d + 4;

        // ABI-level strided set — heap array path (direct cast to long[]).
        // Frame-local arrays are handled via the compile-time fast path above.
        var arrObj = Convert(ArrayAccess(ctx.HeapRawSlots,
            Convert(ctx.RingVar(arrSlot), typeof(int))), typeof(long[]));
        var j = Variable(typeof(long), "_bits_j");
        var loopStart = Label("_stride_loop");
        var loopEnd = Label("_stride_done");
        var loopBody = Block(
            Assign(ArrayAccess(arrObj, Convert(RightShift(j, Constant(6)), typeof(int))),
                Or(ArrayAccess(arrObj, Convert(RightShift(j, Constant(6)), typeof(int))),
                    LeftShift(Constant(1L), Convert(And(j, Constant(63L)), typeof(int))))),
            Assign(j, Add(j, ctx.RingVar(stepSlot))), // j += step
            IfThen(GreaterThan(j, ctx.RingVar(limitSlot)), Goto(loopEnd)),
            Goto(loopStart)
        );
        var result = Block(
            [j],
            Assign(j, ctx.RingVar(startSlot)), // j = start
            Label(loopStart),
            loopBody,
            Label(loopEnd)
        );
        return Block(arrExpr, startExpr, stepExpr, limitExpr, result);
    }

    /// <summary>Break statement: jump to current loop's break label.</summary>
    private static Expression EmitBreakStatement(BreakStatement bs, AbiCtx ctx) {
        var labels = ctx.CurrentLoopLabels;
        if (labels == null)
            throw new InvalidOperationException("Break outside loop");
        return Goto(labels.Value.breakLabel);
    }

    /// <summary>Continue statement: jump to current loop's continue label.</summary>
    private static Expression EmitContinueStatement(ContinueStatement cs, AbiCtx ctx) {
        var labels = ctx.CurrentLoopLabels;
        if (labels == null)
            throw new InvalidOperationException("Continue outside loop");
        return Goto(labels.Value.continueLabel);
    }

    /// <summary>
    /// ForLoop: lower to { init; while(condition) { body; increment; } }
    /// When condition is null, treat as 'while(true)'.
    /// </summary>
    private static Expression EmitForLoop(ForLoop fl, AbiCtx ctx) {
        var breakLabel = Label("for_break");
        var continueLabel = Label("for_continue");
        ctx.PushLoopScope(breakLabel, continueLabel);

        var stmts = new List<Expression>();
        if (fl.Initializer != null)
            stmts.Add(CompileNode(fl.Initializer, ctx));
        var condition = fl.Condition ?? new Constant(1L);
        int d = ctx.RingDepth;

        int bodyDepth = ctx.RingDepth;
        var bodyExpr = CompileNode(fl.Body, ctx);
        ctx.RingDepth = bodyDepth;

        Expression? incrementExpr = null;
        if (fl.Increment != null) {
            ctx.RingDepth = d;
            incrementExpr = CompileNode(fl.Increment, ctx);
            ctx.RingDepth = d;
        }

        var test = CompileConditionAsBool(condition, ctx);
        ctx.RingDepth = d;

        var loopBody = new List<Expression> {
            IfThen(Not(test), Goto(breakLabel)),
            bodyExpr
        };
        if (incrementExpr != null) loopBody.Add(incrementExpr);
        loopBody.Add(Label(continueLabel));

        stmts.Add(Loop(Block(loopBody), breakLabel));
        ctx.PopLoopScope();
        ctx.RingDepth = d;
        return Block(stmts);
    }

    /// <summary>ForEachLoop: compile the body (POC — real enumeration requires CLR interop).</summary>
    private static Expression EmitForEachLoop(ForEachLoop fel, AbiCtx ctx) {
        var breakLabel = Label("foreach_break");
        var continueLabel = Label("foreach_continue");
        ctx.PushLoopScope(breakLabel, continueLabel);

        var collectionExpr = CompileNode(fel.Collection, ctx);
        // collection result is not used (POC — enumeration stub)

        int bodyDepth = ctx.RingDepth;
        var bodyExpr = CompileNode(fel.Body, ctx);
        ctx.RingDepth = bodyDepth;

        ctx.PopLoopScope();
        ctx.RingDepth = 0;
        return Block(collectionExpr, bodyExpr);
    }

    /// <summary>Goto statement: jump to a named label.</summary>
    private static Expression EmitGotoStatement(GotoStatement gs, AbiCtx ctx) {
        var target = ctx.GetLabel(gs.Target);
        return Goto(target);
    }

    /// <summary>Label declaration: emit a label marker in the expression tree.</summary>
    private static Expression EmitLabelDeclaration(LabelDeclaration ld, AbiCtx ctx) {
        var label = ctx.GetLabel(ld.Name);
        return Block(Label(label), CompileNode(ld.Statement, ctx));
    }

    /// <summary>UsingStatement: lower to try/finally with Dispose call.</summary>
    private static Expression EmitUsingStatement(UsingStatement us, AbiCtx ctx) {
        // Evaluate resource, wrap body in try/finally with Dispose
        var resourceExpr = CompileNode(us.Resource, ctx);
        var bodyExpr = CompileNode(us.Body, ctx);

        // Dispose call on the resource handle (simplified — real impl casts to IDisposable via heap)
        var resourceSlot = ctx.RingDepth - 1;
        var disposeMethod = typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!;
        // For POC, just wrap body in try/finally (dispose omitted at ABI level)
        return Block(resourceExpr, TryFinally(bodyExpr, Empty()));
    }

    // ── Statements ─────────────────────────────────────────────────

    /// <summary>Return statement: write value to frame slot, set SP, jump to exit.</summary>
    private static Expression EmitReturn(Return ret, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var retVal = ret.Value ?? throw new InvalidOperationException("Return with null value");
        var valueExpr = CompileNode(retVal, ctx);
        int resultSlot = ctx.RingDepth - 1;
        var fold = FoldResultToSlot(ref resultSlot, d, ctx);

        // Write to _slots[_fp], set SP = _fp + 1, goto exit
        return Block(
            valueExpr, fold,
            Assign(ArrayAccess(ctx.SlotsLocal, ctx.FramePosLocal), ctx.RingVar(resultSlot)),
            Assign(ctx.SlotsStackPointer,
                Add(ctx.FramePosLocal, Constant(1))),
            Goto(ctx.ExitLabel));
    }

    /// <summary>Variable reference: read from value stack or from
    /// closure capture array (if this is a captured upvalue).
    /// Leaving the value on the ring for expression chaining.</summary>
    private static Expression EmitVariable(Variable v, AbiCtx ctx) {
        // Check capture (upvalue) first — used inside lambda bodies
        if (ctx.TryGetCapture(v, out int capIndex)) {
            int slot = ctx.AllocSlot();
            // Read from heap[ state.ClosureHandle ][ capIndex + 1 ]
            // The closure array is object[] stored at the handle in heap raw slots.
            var closureHandle = ctx.ClosureHandle;
            var closureArr = Convert(
                ArrayAccess(ctx.HeapRawSlots, Convert(closureHandle, typeof(int))),
                typeof(object[]));
            var captured = Convert(
                ArrayAccess(closureArr, Constant(capIndex + 1)),
                typeof(long));
            return Assign(ctx.RingVar(slot), captured);
        }
        // Local variable on value stack — read via compile-time frame offset
        int slot2 = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot2), ctx.VariableRead(v));
    }

    /// <summary>Assignment: evaluate RHS, store to variable (local or capture),
    /// array index, or other writable target, push value on ring.</summary>
    private static Expression EmitAssignment(Assignment a, AbiCtx ctx) {
        // Array element assignment: arr[index] = value
        if (a.Destination is IndexAccess indexAccess) {
            // Compile-time fast path: if the array is a tracked frame-local variable,
            // emit _slots[base + idx] = val directly — no dispatch.
            if (indexAccess.Value is Variable arrVarW
                && ctx.TryGetFrameLocalBase(arrVarW) is int flBaseW) {
                Expression idxVal = indexAccess.Arguments.Length > 0
                    ? CompileValue(indexAccess.Arguments[0], ctx)
                    : Constant(0L);
                Expression valVal = CompileValue(a.Value, ctx);
                int flResultSlot = ctx.AllocSlot();
                ctx.RingDepth = flResultSlot + 1;
                var idxTmp = Variable(typeof(int), "_flIdx");
                return Block([idxTmp],
                    Assign(idxTmp, Convert(idxVal, typeof(int))),
                    Assign(ctx.RingVar(flResultSlot), valVal),
                    Assign(ArrayAccess(ctx.SlotsLocal, Add(Constant(flBaseW), idxTmp)),
                        ctx.RingVar(flResultSlot)),
                    ctx.RingVar(flResultSlot));
            }

            var arrExpr = CompileNode(indexAccess.Value, ctx);
            int arrSlot = ctx.RingDepth - 1;
            var idxExpr = CompileNode(
                indexAccess.Arguments.Length > 0 ? indexAccess.Arguments[0] : new Constant(0), ctx);
            int idxSlot = ctx.RingDepth - 1;
            var valExpr = CompileNode(a.Value, ctx);
            int valSlot = ctx.RingDepth - 1;

            var rawObj = Convert(
                ArrayAccess(ctx.HeapRawSlots, Convert(ctx.RingVar(arrSlot), typeof(int))),
                typeof(object));
            var idx = Convert(ctx.RingVar(idxSlot), typeof(int));
            var val = ctx.RingVar(valSlot);

            // Try to resolve element type from analysis to skip runtime TypeIs check.
            Type? elemType = ctx.Analysis?.GetResolvedType(indexAccess) is ClrTypeDefinition clrElem
                ? clrElem.RuntimeType
                : null;

            Expression store;
            if (elemType is { IsValueType: true }) {
                var longArr = Variable(typeof(long[]), "_assignLongArr");
                store = Block([longArr],
                    Assign(longArr, Convert(rawObj, typeof(long[]))),
                    Assign(ArrayAccess(longArr, idx), val));
            }
            else if (elemType is not null) {
                var objArr = Variable(typeof(object[]), "_assignObjArr");
                store = Block([objArr],
                    Assign(objArr, Convert(rawObj, typeof(object[]))),
                    Assign(ArrayAccess(objArr, idx), Convert(val, typeof(object))));
            }
            else {
                var longArr = Variable(typeof(long[]), "_assignLongArr");
                var objArr = Variable(typeof(object[]), "_assignObjArr");
                store = IfThenElse(
                    TypeIs(rawObj, typeof(long[])),
                    Block([longArr],
                        Assign(longArr, Convert(rawObj, typeof(long[]))),
                        Assign(ArrayAccess(longArr, idx), val)),
                    Block([objArr],
                        Assign(objArr, Convert(rawObj, typeof(object[]))),
                        Assign(ArrayAccess(objArr, idx), Convert(val, typeof(object)))));
            }

            ctx.RingDepth = arrSlot + 1;
            return Block(arrExpr, idxExpr, valExpr, store,
                Assign(ctx.RingVar(arrSlot), val));
        }

        if (a.Destination is not Variable destVar) {
            throw new NotSupportedException(
                $"Assignment destination must be a Variable or IndexAccess, got {a.Destination.GetType().Name}");
        }

        // Frame-local array allocation: Variable = NewArray(elemType, constLen)
        // where the array is a small value-type array (≤ SmallArrayThreshold).
        // Bypass heap entirely: allocate in _slots and track the variable so
        // subsequent IndexAccess can emit direct _slots access without dispatch.
        if (a.Value is NewArray newArr && newArr.Length is Constant lenConst
            && TryValueToLong(lenConst.Value, out long arrLen)
            && arrLen > 0 && arrLen <= AbiCtx.SmallArrayThreshold) {
            Type elemType = newArr.ElementType switch {
                ClrTypeReference ctr when ctr.RuntimeType.IsValueType => ctr.RuntimeType,
                _ => typeof(object)
            };
            if (elemType.IsValueType) {
                int baseOffset = ctx.AllocateSmallArray();
                ctx.TrackFrameLocalArray(destVar, baseOffset);
                // ArrayPool-backed _slots are dirty — zero the frame-local region.
                var zeroInits = new Expression[(int)arrLen];
                for (int zi = 0; zi < (int)arrLen; zi++)
                    zeroInits[zi] = Assign(
                        ArrayAccess(ctx.SlotsLocal, Constant(baseOffset + zi)),
                        Constant(0L));
                int slot = ctx.AllocSlot();
                return Block(
                    Block(zeroInits),
                    ctx.VariableWrite(destVar, Constant((long)baseOffset)),
                    Assign(ctx.RingVar(slot), Constant((long)baseOffset)));
            }
        }

        // Variable reassigned — if it was tracked as frame-local, untrack it.
        // The new value might be a heap array, a non-array, etc.
        ctx.UntrackFrameLocalArray(destVar);

        // Check capture (upvalue) first
        if (ctx.TryGetCapture(destVar, out int capIndex)) {
            int d = ctx.RingDepth;
            var valueExpr = CompileNode(a.Value, ctx);
            int valSlot = ctx.RingDepth - 1;
            var foldVal = FoldResultToSlot(ref valSlot, d, ctx);
            var closureArr = Convert(
                ArrayAccess(ctx.HeapRawSlots, Convert(ctx.ClosureHandle, typeof(int))),
                typeof(object[]));
            var store = Assign(
                ArrayAccess(closureArr, Constant(capIndex + 1)),
                Convert(ctx.RingVar(valSlot), typeof(object)));
            ctx.RingDepth = valSlot + 1;
            return Block(valueExpr, foldVal, store);
        }

        // Local variable — write via compile-time frame offset
        int d2 = ctx.RingDepth;
        var valueExpr2 = CompileNode(a.Value, ctx);
        // Result is at ctx.RingDepth - 1 — use that slot (not d2), because
        // complex value expressions (NewArray, etc.) may allocate multiple slots.
        int resultSlot = ctx.RingDepth - 1;

        var result = ctx.VariableWrite(destVar, ctx.RingVar(resultSlot));
        ctx.RingDepth = resultSlot + 1;
        return Block(valueExpr2, result);
    }

    /// <summary>Block: compile statements sequentially in a child scope.</summary>
    private static Expression EmitBlock(Block block, AbiCtx ctx) {
        ctx.PushScope();
        var varInitExprs = new List<Expression>();

        foreach (var v in block.Variables) {
            if (v is Variable variable) {
                // Declare variable: allocates a register file slot
                ctx.DeclareVariable(variable);
                // Initialize to 0 (ABI convention for long slots).
                // This writes to the register file, not _slots.
                varInitExprs.Add(Assign(ctx.VariableRead(variable), Constant(0L)));
            }
        }

        var stmtExprs = new List<Expression>();
        for (int i = 0; i < block.Nodes.Count; i++) {
            stmtExprs.Add(CompileStatement(block.Nodes[i], ctx));
        }

        // Flush register file back to _slots before scope exit.
        // This gives the JIT a clear load-compute-store pattern.
        var stores = ctx.EmitScopeStores();
        ctx.PopScope();

        // Flatten: merge varInitExprs, stmtExprs, stores into one block.
        var all = new List<Expression>(varInitExprs.Count + stmtExprs.Count + stores.Count);
        all.AddRange(varInitExprs);
        all.AddRange(stmtExprs);
        all.AddRange(stores);
        return Block(all);
    }

    /// <summary>If statement: conditionally execute branches.
    /// Comparison conditions fuse to a bool test (no 0/1 long). Ring depth
    /// converges to the pre-condition depth.</summary>
    private static Expression EmitIfStatement(IfStatement ifStmt, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var test = CompileConditionAsBool(ifStmt.Condition, ctx);
        // Condition temps (if any) are embedded in <c>test</c>; reset depth for arms.
        ctx.RingDepth = d;

        var thenBody = CompileNode(ifStmt.ThenBranch, ctx);
        ctx.RingDepth = d;

        Expression elseBody = Empty();
        if (ifStmt.ElseBranch is not null) {
            elseBody = CompileNode(ifStmt.ElseBranch, ctx);
            ctx.RingDepth = d;
        }

        return IfThenElse(test, thenBody, elseBody);
    }

    /// <summary>Conditional (ternary): condition ? true : false. Lazy arms.</summary>
    private static Expression EmitConditional(Conditional c, AbiCtx ctx) =>
        SpillToRing(EmitConditionalValue(c, ctx), ctx);

    private static Expression EmitCoalesce(Coalesce n, AbiCtx ctx) =>
        SpillToRing(EmitCoalesceValue(n, ctx), ctx);

    private static Expression EmitSwitch(SwitchStatement sw, AbiCtx ctx) {
        // Lower to chained conditionals matching the pattern used by EmitConditional:
        // pre-compile all patterns and bodies into ring slots, then build nested
        // LINQ Condition expressions that select the correct body result.
        int d = ctx.RingDepth;
        var valueExpr = CompileNode(sw.Value, ctx);
        int valSlot = ctx.RingDepth - 1;
        var foldVal = FoldResultToSlot(ref valSlot, d, ctx);
        ctx.RingDepth = valSlot + 1;

        // Pre-compile default case (evaluated unconditionally; result selected via Condition).
        Expression defExpr;
        int defSlot;
        if (sw.DefaultCase != null) {
            int defDepth = ctx.RingDepth;
            defExpr = CompileNode(sw.DefaultCase, ctx);
            ctx.RingDepth = defDepth + 1;
            defSlot = defDepth;
        }
        else {
            defSlot = ctx.AllocSlot();
            defExpr = Assign(ctx.RingVar(defSlot), Constant(0L));
        }

        // Pre-compile all cases (patterns and bodies) in order.
        var compiledCases = new (Expression pExpr, int pSlot, Expression bExpr, int bSlot)[sw.Cases.Count];
        for (int i = 0; i < sw.Cases.Count; i++) {
            var c = sw.Cases[i];

            int pDepth = ctx.RingDepth;
            var pExpr = CompileNode(c.Pattern, ctx);
            ctx.RingDepth = pDepth + 1;
            int pSlot = pDepth;

            int bDepth = ctx.RingDepth;
            var bExpr = CompileNode(c.Body, ctx);
            ctx.RingDepth = bDepth + 1;
            int bSlot = bDepth;

            compiledCases[i] = (pExpr, pSlot, bExpr, bSlot);
        }

        // Build nested Condition expressions from last case to first.
        Expression resultExpr = ctx.RingVar(defSlot);
        for (int i = sw.Cases.Count - 1; i >= 0; i--) {
            var (_, pSlot, _, bSlot) = compiledCases[i];
            resultExpr = Condition(
                Expression.Equal(ctx.RingVar(valSlot), ctx.RingVar(pSlot)),
                ctx.RingVar(bSlot),
                resultExpr
            );
        }

        // Assign the final selected value to a ring slot.
        int outSlot = ctx.AllocSlot();
        ctx.RingDepth = outSlot + 1;

        var allExprs = new List<Expression> { valueExpr, defExpr };
        foreach (var (pExpr, _, bExpr, _) in compiledCases) {
            allExprs.Add(pExpr);
            allExprs.Add(bExpr);
        }
        allExprs.Add(foldVal);
        allExprs.Add(Assign(ctx.RingVar(outSlot), resultExpr));

        return Block(allExprs);
    }

    /// <summary>While loop: evaluate condition, execute body, repeat.
    /// Comparison conditions fuse to a bool test (no 0/1 long).</summary>
    private static Expression EmitWhileLoop(WhileLoop wl, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var breakLabel = Label("wl_break");
        var continueLabel = Label("wl_continue");
        ctx.PushLoopScope(breakLabel, continueLabel);

        var test = CompileConditionAsBool(wl.Condition, ctx);
        ctx.RingDepth = d;

        var bodyExpr = CompileNode(wl.Body, ctx);
        ctx.RingDepth = d;

        var loopBody = Block(
            IfThen(Not(test), Goto(breakLabel)),
            bodyExpr,
            Label(continueLabel));

        var result = Loop(loopBody, breakLabel);
        ctx.PopLoopScope();
        ctx.RingDepth = d;
        return result;
    }

    /// <summary>Do-while: body then condition.</summary>
    private static Expression EmitDoWhileLoop(DoWhileLoop dwl, AbiCtx ctx) {
        int d = ctx.RingDepth;
        var breakLabel = Label("dwl_break");
        var continueLabel = Label("dwl_continue");
        ctx.PushLoopScope(breakLabel, continueLabel);

        var bodyExpr = CompileNode(dwl.Body, ctx);
        ctx.RingDepth = d;

        var test = CompileConditionAsBool(dwl.Condition, ctx);
        ctx.RingDepth = d;

        var loopBody = Block(
            bodyExpr,
            Label(continueLabel),
            IfThen(Not(test), Goto(breakLabel)));

        var result = Loop(loopBody, breakLabel);
        ctx.PopLoopScope();
        ctx.RingDepth = d;
        return result;
    }

    /// <summary>Throw statement: compile the operand for side effects if any,
    /// then throw a CLR Exception.</summary>
    private static Expression EmitThrow(ThrowStatement ts, AbiCtx ctx) {
        // If the exception operand is a New node (creating a real CLR exception),
        // compile it and read the heap result. Otherwise, throw a generic Exception.
        if (ts.Exception is New) {
            int d = ctx.RingDepth;
            var compiled = CompileNode(ts.Exception, ctx); // result at ctx.RingDepth-1
            int resultSlot = ctx.RingDepth - 1;

            var heapObj = Call(ctx.HeapLocal,
                typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                Convert(ctx.RingVar(resultSlot), typeof(int)));
            var exVar = Variable(typeof(Exception), "_thrownEx");
            return Block(
                [exVar],
                compiled,
                Assign(exVar, Convert(heapObj, typeof(Exception))),
                Throw(exVar));
        }

        // For non-New operands (constants, expressions), just throw generic Exception
        var sideEffects = CompileNode(ts.Exception, ctx);
        return Block(sideEffects, Throw(New(typeof(Exception))));
    }

    /// <summary>
    /// TryCatchFinally: use native CLR structured EH instead of flat markers + side table.
    /// This is much simpler than the primitive path's reconstruction.
    /// </summary>
    private static Expression EmitTryCatchFinally(TryCatchFinally tcf, AbiCtx ctx) {
        var tryBody = CompileNode(tcf.TryBlock, ctx);

        Expression? finallyBody = null;
        if (tcf.FinallyBlock != null) {
            finallyBody = CompileNode(tcf.FinallyBlock, ctx);
        }

        if (tcf.CatchClauses == null || tcf.CatchClauses.Count == 0) {
            return finallyBody != null ? TryFinally(tryBody, finallyBody) : tryBody;
        }

        var catchBlocks = new List<CatchBlock>();
        foreach (var clause in tcf.CatchClauses) {
            var exType = typeof(Exception);
            var exParam = Parameter(exType, clause.VariableName ?? "ex");

            // For POC binding: allocate the exception on the heap and store handle to a ring slot.
            // The catch body is compiled; if it references the variable by name in tests, we use a synthetic.
            ctx.PushScope();
            Variable? synthetic = null;
            if (!string.IsNullOrEmpty(clause.VariableName)) {
                synthetic = new Variable(clause.VariableName);
                ctx.DeclareVariable(synthetic);
            }

            var bodyExpr = CompileNode(clause.Body, ctx);

            // Allocate handle for the ex so ABI code can see it as a "value".
            var allocate = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)), Convert(exParam, typeof(object)));
            var handle = Convert(allocate, typeof(long));

            Expression catchBodyExpr = bodyExpr;
            if (synthetic != null) {
                // Store the handle so EmitVariable for this synthetic can find it.
                // Use compile-time frame offset for the write.
                catchBodyExpr = Block(
                    ctx.VariableWrite(synthetic, handle),
                    bodyExpr
                );
            }

            // Emit scope stores before popping, then PopScope.
            var catchStores = ctx.EmitScopeStores();
            if (catchStores.Count > 0)
                catchBodyExpr = Block(catchBodyExpr, Block(catchStores));
            ctx.PopScope();

            catchBlocks.Add(Catch(exParam,
                catchBodyExpr.Type == typeof(void)
                    ? catchBodyExpr
                    : Block(catchBodyExpr, Empty())));
        }

        if (finallyBody != null) {
            return TryCatchFinally(tryBody, finallyBody, catchBlocks.ToArray());
        }
        return TryCatch(tryBody, catchBlocks.ToArray());
    }

    /// <summary>
    /// SuspendNode: evaluate inner, set Suspended status, save step as PC, jump to exit.
    /// Supports non-trivial suspend/resume validation (e.g. inside loops with captures).
    /// </summary>
    private static Expression EmitSuspendNode(SuspendNode sn, AbiCtx ctx) {
        // Manage our own step counter.  DebugHook invoke and CurrentAstNode set
        // at statement boundaries are handled by CompileStatement in EmitBlock.
        // When the SuspendNode is the root node (not inside a Block), this
        // method sets CurrentAstNode directly.
        int step = ctx.StepCounter++;
        ctx.RecordStepNode(step, sn);
        var resumeLabel = ctx.RegisterOrGetResumeLabel(step);
        var innerExpr = CompileNode(sn.Inner, ctx);

        var setCurrentNode = Block(
            Assign(Property(ctx.State, nameof(VmState.CurrentAstNode)), Constant(sn)),
            Assign(Property(ctx.State, nameof(VmState.CurrentNodeId)), Constant(sn.Id, typeof(NodeId?))));
        var setStatus = Assign(
            Property(ctx.State, nameof(VmState.Status)),
            Constant(InterpreterStatus.Suspended));
        var saveResumeId = Assign(ctx.ProgramCounter, Constant(step));
        var saveFramePos = Assign(
            Property(ctx.State, nameof(VmState.FramePos)),
            ctx.FramePosLocal);

        return Block(
            Label(resumeLabel),
            innerExpr,
            setCurrentNode,
            setStatus,
            saveResumeId,
            saveFramePos,
            Goto(ctx.ExitLabel));
    }

    /// <summary>Parameter: read from the value-stack slot set up by the caller
    /// before the function body is invoked. Parameters are at _slots[_fb + paramIndex]
    /// (adjusted by ParamSlotOffset for lambda bodies where parameters live before
    /// local variables). Top-level parameters (passed via <c>SetArgs</c>) are
    /// auto-declared if not already registered in a lambda scope.</summary>
    private static Expression EmitParameter(Parameter p, AbiCtx ctx) {
        if (!ctx.TryGetParameterSlot(p, out int paramIdx)) {
            // Auto-declare as a top-level parameter (e.g. from SetArgs in Policy evaluation).
            paramIdx = ctx.DeclareParameter(p);
        }
        int slot = ctx.AllocSlot();
        return Assign(ctx.RingVar(slot), ctx.ParameterRead(paramIdx));
    }

    /// <summary>
    /// Lambda: detect captures from the outer scope and allocate a closure
    /// carrying <c>[funcIndex, capture0, capture1, ...]</c>.
    /// For the spike, the function body is compiled inline in the outer
    /// expression (no separate delegate).  This avoids NRE issues with
    /// closure handle access in standalone function bodies.
    /// </summary>
    private static Expression EmitLambda(Lambda lambda, AbiCtx ctx) {
        if (lambda.LambdaIndex < 0)
            throw new InvalidOperationException("Lambda.LambdaIndex not set during lambda collection");

        // 1. Detect captures — variables in the body that belong to outer scopes
        var captures = FindCaptures(lambda.Body, ctx);

        // Register captures on the context so EmitVariable can find them
        for (int i = 0; i < captures.Count; i++)
            ctx.DeclareCapture(captures[i].Variable, i);

        // 2. Allocate closure: object[] { (long)funcIndex, (long)capture0, ... }
        int arrLen = 1 + captures.Count;
        var closureArrVar = Variable(typeof(object[]), "_closureArr");
        var body = new List<Expression>();
        body.Add(Assign(closureArrVar, NewArrayBounds(typeof(object), Constant(arrLen))));
        body.Add(Assign(ArrayAccess(closureArrVar, Constant(0)),
            Convert(Constant((long)lambda.LambdaIndex), typeof(object))));

        for (int i = 0; i < captures.Count; i++) {
            var cap = captures[i];
            body.Add(Assign(ArrayAccess(closureArrVar, Constant(1 + i)),
                Convert(ctx.VariableRead(cap.Variable), typeof(object))));
        }

        var handle = Call(ctx.HeapLocal, Ref<Heap>.Method(h => h.Allocate(null!)),
            Convert(closureArrVar, typeof(object)));
        int slot = ctx.AllocSlot();
        body.Add(Assign(ctx.RingVar(slot), Convert(handle, typeof(long))));
        return Block([closureArrVar], body);
    }

    /// <summary>
    /// Recursively walk a lambda body and collect all <see cref="Variable"/> nodes
    /// that resolve to the outer context's scope (i.e., captures).
    /// </summary>
    private static List<Capture> FindCaptures(Node body, AbiCtx outerCtx) {
        var result = new List<Capture>();
        var seen = new HashSet<Variable>(ReferenceEqualityComparer.Instance);
        FindCapturesRecursive(body, outerCtx, result, seen);
        return result;
    }

    private static void FindCapturesRecursive(
        Node node, AbiCtx outerCtx, List<Capture> result, HashSet<Variable> seen) {
        if (node is Variable v && seen.Add(v)) {
            if (outerCtx.TryGetVariable(v, out int slotIndex)) {
                result.Add(new Capture(v, slotIndex));
            }
            // Don't recurse into Variable's children (it has a Value expression
            // that isn't part of variable resolution).
            return;
        }
        foreach (var child in node.Children) {
            if (child is not null)
                FindCapturesRecursive(child, outerCtx, result, seen);
        }
    }

    /// <summary>
    /// Invoke: for Lambda targets, compile the body inline (no separate delegate).
    /// </summary>
    private static Expression EmitInvoke(Invoke invoke, AbiCtx ctx) {
        // Handle Invoke(Member(instance, "Method"), args) — resolve the method
        // and call it directly via CLR reflection, bypassing full lambda handling.
        if (invoke.Delegate is Member member) {
            var resolved = ctx.Analysis?.GetResolvedMember(member);
            if (resolved is ITypeMethod method) {
                var clrMethod = resolved as ClrMethod;
                var methodInfo = clrMethod?.MethodInfo;
                if (methodInfo is not null) {
                    int d = ctx.RingDepth;
                    // Compile instance (for instance methods) or null (for static)
                    bool isStatic = resolved.LifetimeModifier == LifetimeModifier.Static;
                    int instanceSlot = -1;
                    Expression? instanceExpr = null;
                    if (!isStatic) {
                        instanceExpr = CompileNode(member.Value, ctx);
                        instanceSlot = ctx.RingDepth - 1;
                        var foldInst = FoldResultToSlot(ref instanceSlot, d, ctx);
                        instanceExpr = Block(instanceExpr, foldInst);
                        ctx.RingDepth = instanceSlot + 1;
                    }

                    // Compile arguments — track actual result slots
                    var argExprs = new List<Expression>();
                    int[] argSlots = new int[invoke.Arguments.Length];
                    for (int i = 0; i < invoke.Arguments.Length; i++) {
                        argExprs.Add(CompileNode(invoke.Arguments[i], ctx));
                        argSlots[i] = ctx.RingDepth - 1;
                    }
                    ctx.RingDepth = d + (isStatic ? 0 : 1) + invoke.Arguments.Length + 1;

                    var methodParams = methodInfo.GetParameters();
                    var methodArgs = new Expression[methodParams.Length];
                    int baseIdx = isStatic ? 0 : 1;
                    for (int i = 0; i < methodParams.Length; i++) {
                        int slotIdx = i < invoke.Arguments.Length ? argSlots[i] : d + baseIdx + i;
                        var ringVal = ctx.RingVar(slotIdx);
                        var paramType = methodParams[i].ParameterType;
                        if (paramType.IsValueType) {
                            methodArgs[i] = Convert(ringVal, paramType);
                        }
                        else if (paramType == typeof(string)) {
                            methodArgs[i] = Convert(
                                Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                                    Convert(ringVal, typeof(int))),
                                paramType);
                        }
                        else {
                            methodArgs[i] = Convert(
                                Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                                    Convert(ringVal, typeof(int))),
                                paramType);
                        }
                    }

                    Expression callExpr;
                    if (isStatic) {
                        callExpr = Call(null, methodInfo, methodArgs);
                    }
                    else {
                        var instanceObj = Call(ctx.HeapLocal,
                            typeof(Heap).GetMethod(nameof(Heap.UnsafeGet))!,
                            Convert(ctx.RingVar(instanceSlot), typeof(int)));
                        // Convert from 'object' to the declaring type so Expression.Call
                        // can resolve the method. This is a no-op cast at runtime for
                        // reference types but satisfies Expression tree type validation.
                        var declaringType = methodInfo.DeclaringType!;
                        callExpr = Call(Convert(instanceObj, declaringType), methodInfo, methodArgs);
                    }

                    int slot = ctx.AllocSlot();
                    ctx.RingDepth = slot + 1;

                    // Build the full expression sequence: instance → args → call → result
                    var fullBody = new List<Expression>();
                    if (instanceExpr is not null)
                        fullBody.Add(instanceExpr);
                    fullBody.AddRange(argExprs);

                    // Convert result to ABI (value types unboxed, ref types heap-allocated)
                    var resultType = methodInfo.ReturnType;
                    if (resultType == typeof(void)) {
                        fullBody.Add(callExpr);
                        return Block(fullBody);
                    }
                    if (resultType.IsValueType) {
                        fullBody.Add(Assign(ctx.RingVar(slot), Convert(callExpr, typeof(long))));
                        return Block(fullBody);
                    }
                    // Reference type return: allocate on heap
                    fullBody.Add(Assign(ctx.RingVar(slot),
                        Convert(Call(ctx.HeapLocal, typeof(Heap).GetMethod(nameof(Heap.Allocate))!,
                            Convert(callExpr, typeof(object))), typeof(long))));
                    return Block(fullBody);
                }
            }
            // If method resolution fails, throw a clear error
            throw new NotSupportedException(
                $"DirectVmAbiEmitter: Invoke with Member delegate '{member.MemberName}' " +
                $"could not resolve to a method. Ensure TypeAndMemberResolver is in the pipeline.");
        }

        if (invoke.Delegate is Lambda lambda) {

            // Trivial inline: no captures → compile body directly in the caller's
            // context.  Skip when the lambda has parameters but Invoke has no
            // argument expressions (SetArgs pattern): those parameters live in
            // value-stack slots and need ParamSlotOffset/FramePos setup from the
            // frame path so locals don't overwrite them on EmitScopeStores.
            if (FindCaptures(lambda.Body, ctx).Count == 0
                && !(lambda.Parameters.Count > 0 && invoke.Arguments.Length == 0)) {
                return EmitInlineInvoke(lambda, invoke, ctx);
            }

            // 0. Declare lambda parameters in a child scope so EmitParameter works.
            // Reset the parameter slot counter for this invocation level so that
            // nested functions use their own parameter index space (INT-027).
            ctx.PushScope();
            int savedNextParamSlot = ctx.SaveAndResetParamSlots();
            foreach (var param in lambda.Parameters)
                ctx.DeclareParameter(param);

            // 1. Compile the Lambda node — allocates closure on heap, result on ring
            var closureExpr = EmitLambda(lambda, ctx);
            int closureSlot = ctx.RingDepth - 1;

            // 2. Compile arguments — each goes on ring
            var argExprs = new List<Expression>();
            int[] argSlots = new int[invoke.Arguments.Length];
            for (int i = 0; i < invoke.Arguments.Length; i++) {
                argExprs.Add(CompileNode(invoke.Arguments[i], ctx));
                argSlots[i] = ctx.RingDepth - 1;
            }
            int saveDepth = ctx.RingDepth;

            var preBody = new List<Expression>();   // before inline body
            var postBody = new List<Expression>();  // after inline body
            var spProp = Property(Property(ctx.State, "Stack"), "StackPointer");
            var regSlots = Property(ctx.State, "Registers");

            // 3. Create a per-invocation SP local (avoids nested invoke corruption
            // of the shared ctx.SavedSp). Save current ring to Registers[invokeSp + k].
            var invokeSp = Variable(typeof(int), "_invokeSp");
            preBody.Add(Assign(invokeSp, spProp));
            for (int k = 0; k < saveDepth; k++)
                preBody.Add(Assign(ArrayAccess(regSlots, Add(invokeSp, Constant(k))), ctx.RingVar(k)));

            // 4. Set state.ClosureHandle from the closure slot
            preBody.Add(Assign(
                Property(ctx.State, nameof(VmState.ClosureHandle)),
                Convert(ctx.RingVar(closureSlot), typeof(int))));

            // 5. Push arguments from ring to value stack with optional 2-word frame header.
            // When there are explicit Invoke arguments, push a 2-word header (PreviousFP,
            // SavedSP) before the arguments. When there are 0 arguments (the SetArgs pattern),
            // skip the header to preserve backward compatibility with root-level SetArgs.
            //
            // Layout with header:
            //   [_callSp + 0] = PreviousFP (current _fp value)
            //   [_callSp + 1] = SavedSP
            //   [_callSp + 2] = arg0, [_callSp + 3] = arg1, ...
            //   _fp = callSp + 2 + max(args.Length, 1)
            //
            // ParameterRead uses _fp - ParamSlotOffset + paramIdx which resolves to
            // _slots[callSp + 2 + paramIdx] — correct with the 2-word header.
            var callSp = Variable(typeof(int), "_callSp");
            preBody.Add(Assign(callSp, spProp));

            int headerSize = invoke.Arguments.Length > 0 ? 2 : 0;

            if (headerSize > 0) {
                // Push PreviousFP (= current _fp as long)
                preBody.Add(Assign(ArrayAccess(ctx.SlotsLocal, callSp),
                    Convert(ctx.FramePosLocal, typeof(long))));
                // Push SavedSP (= callSp as long, the SP before the header)
                preBody.Add(Assign(ArrayAccess(ctx.SlotsLocal, Add(callSp, Constant(1))),
                    Convert(callSp, typeof(long))));
                // Advance SP past the 2-word header
                preBody.Add(Call(Property(ctx.State, "Stack"),
                    Ref<ValueStack>.Method(s => s.SetStackPointer(0)),
                    Add(callSp, Constant(headerSize))));
            }

            // Push arguments
            for (int i = 0; i < invoke.Arguments.Length; i++) {
                preBody.Add(Assign(ArrayAccess(ctx.SlotsLocal,
                    Add(callSp, Constant(headerSize + i))),
                    ctx.RingVar(argSlots[i])));
            }
            // Advance SP past args
            if (invoke.Arguments.Length > 0 || headerSize > 0) {
                preBody.Add(Call(Property(ctx.State, "Stack"),
                    Ref<ValueStack>.Method(s => s.SetStackPointer(0)),
                    Add(callSp, Constant(headerSize + invoke.Arguments.Length))));
            }

            // _fp = callSp + headerSize + max(args.Length, 1) so params are at slots[_fp - N + paramIdx]
            // Even with 0 args, reserve 1 slot for implicit SetArgs parameter.
            int paramCount = Math.Max(1, invoke.Arguments.Length);
            Expression newFp = Add(callSp, Constant(headerSize + paramCount));

            // 6. Set new _fp (saved PreviousFP is already in the 2-word header).
            preBody.Add(Assign(ctx.FramePosLocal, newFp));

            // Set ParamSlotOffset so EmitParameter reads from corrected slot
            int savedParamOffset = ctx.ParamSlotOffset;
            ctx.ParamSlotOffset = paramCount;

            // Compile lambda body inline — leaves result on ring
            var bodyExpr = CompileNode(lambda.Body, ctx);
            int bodyResultSlot = ctx.RingDepth - 1;
            preBody.Add(bodyExpr);
            // Save result to temp local BEFORE ring restore (use per-invocation local)
            var invokeResult = Variable(typeof(long), "_invokeResult");
            preBody.Add(Assign(invokeResult, ctx.RingVar(bodyResultSlot)));
            ctx.RingDepth = 1;

            // Restore ring from state.Registers using per-invocation invokeSp
            for (int k = 0; k < saveDepth; k++)
                postBody.Add(Assign(ctx.RingVar(k), ArrayAccess(regSlots, Add(invokeSp, Constant(k)))));

            // Write saved result to ring slot 0 (ring now fully restored)
            postBody.Add(Assign(ctx.RingVar(0), invokeResult));

            // Restore _fp from the 2-word header (PreviousFP at _slots[callSp]).
            // When headerSize == 0 (SetArgs pattern), restore from OldFramePos legacy field.
            if (headerSize > 0) {
                var prevFp = Convert(ArrayAccess(ctx.SlotsLocal, callSp), typeof(int));
                postBody.Add(Assign(ctx.FramePosLocal, prevFp));
            }
            else {
                postBody.Add(Assign(ctx.FramePosLocal,
                    Property(ctx.State, nameof(VmState.OldFramePos))));
            }

            ctx.ParamSlotOffset = savedParamOffset;  // restore
            ctx.RestoreParamSlots(savedNextParamSlot);  // restore outer param slot counter
            ctx.PopScope();
            return Block([invokeSp, callSp, invokeResult], closureExpr, Block(argExprs),
                Block(preBody.Concat(postBody)));
        }

        throw new NotSupportedException(
            $"DirectVmAbiEmitter: Invoke not supported for delegate type {invoke.Delegate.GetType().Name}");
    }

    /// <summary>Inline a small lambda with no captures and a single-expression body.
    /// Skips frame push, closure allocation, and ring save/restore — compiles
    /// arguments directly to the ring and maps parameter reads to argument slots.</summary>
    private static Expression EmitInlineInvoke(Lambda lambda, Invoke invoke, AbiCtx ctx) {
        int depth = ctx.RingDepth;
        var argExprs = new Expression[invoke.Arguments.Length];
        for (int i = 0; i < invoke.Arguments.Length; i++) {
            argExprs[i] = CompileNode(invoke.Arguments[i], ctx);
            ctx.MapInlineParameter(i, ctx.RingDepth - 1);
        }
        var bodyExpr = CompileNode(lambda.Body, ctx);
        ctx.ClearInlineParameters();
        return Block(argExprs.Concat([bodyExpr]));
    }

    /// <summary>
    /// Recursively collect all Lambda nodes from an AST subtree.
    /// </summary>
    private static void CollectLambdas(Node node, List<Lambda> result) {
        if (node is Lambda lambda) {
            lambda.LambdaIndex = result.Count; // assign index in collection order
            result.Add(lambda);
            // Still recurse into body for nested lambdas
            CollectLambdas(lambda.Body, result);
            return;
        }
        foreach (var child in node.Children) {
            if (child is not null)
                CollectLambdas(child, result);
        }
    }

    /// <summary>
    /// Compile a lambda body as a standalone <c>Action&lt;VmState&gt;</c> delegate.
    /// Parameters are read from value-stack slots.
    /// Captured variables are read from/written to <c>heap[state.ClosureHandle][captureIndex]</c>.
    /// </summary>
    private static Action<VmState> CompileFunctionBody(
        Node body,
        IReadOnlyList<Parameter> parameters,
        List<Capture> captures,
        Action<VmState>[] functionTable,
        CompilationMode mode) {

        var fnCtx = new AbiCtx();
        fnCtx.FunctionTableExpr = Constant(functionTable);
        fnCtx.Mode = mode;
        var bodyExprs = new List<Expression>();

        // Preamble
        bodyExprs.Add(Label(fnCtx.EntryLabel));
        bodyExprs.Add(Assign(fnCtx.SlotsLocal, fnCtx.SlotsInitExpression));
        bodyExprs.Add(Assign(fnCtx.HeapLocal, fnCtx.HeapInitExpression));
        bodyExprs.Add(Assign(fnCtx.Registers,
            Coalesce(fnCtx.Registers, NewArrayBounds(typeof(long), Constant(256)))));
        bodyExprs.Add(Assign(fnCtx.FramePosLocal,
            Condition(
                Equal(Property(fnCtx.State, nameof(VmState.Status)),
                    Constant(InterpreterStatus.Resuming)),
                Property(fnCtx.State, nameof(VmState.FramePos)),
                Constant(0))));

        if (mode != CompilationMode.NoDebug) {
            fnCtx.DebugHookProp = Property(fnCtx.State, nameof(VmState.DebugHook));
        }

        // Register captures so EmitVariable/EmitAssignment can route to heap reads
        for (int i = 0; i < captures.Count; i++)
            fnCtx.DeclareCapture(captures[i].Variable, i);

        // Declare parameters as value-stack variables (mapped to _slots[_fp + idx])
        fnCtx.PushScope();
        foreach (var param in parameters)
            fnCtx.DeclareParameter(param);

        // Enter the activation in the compile-time simulator.
        // Local count is 0 initially; DeclareVariable calls inside the body
        // will assign scope-relative slots but the simulator currently tracks
        // frame size based on the counts passed here.
        fnCtx.EnterActivation(parameters.Count, 0);

        // Compile body
        var bodyCompiled = CompileNode(body, fnCtx);
        bodyExprs.Add(bodyCompiled);

        // Leave the activation in the compile-time simulator.
        fnCtx.LeaveActivation();

        // Flush result: return value at _slots[_fp], SP = _fp + 1
        if (fnCtx.RingDepth > 0) {
            bodyExprs.Add(Assign(ArrayAccess(fnCtx.SlotsLocal, fnCtx.FramePosLocal),
                fnCtx.RingVar(fnCtx.RingDepth - 1)));
            bodyExprs.Add(Assign(fnCtx.SlotsStackPointer,
                Add(fnCtx.FramePosLocal, Constant(1))));
        }

        bodyExprs.Add(Label(fnCtx.ExitLabel));

        var delegateExpr = Lambda<Action<VmState>>(Block(fnCtx.Locals, bodyExprs), fnCtx.State);
        return delegateExpr.Compile();
    }

    // ── Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Wrap a compiled node expression with a simplified debug hook guard.
    /// In NoDebug mode (<see cref="AbiCtx.DebugHookProp"/> is null),
    /// returns the body unchanged — zero overhead.
    ///
    /// When a hook is set, emits code that snapshots the current frame's
    /// locals into a <c>long[]</c> buffer (using compile-time known offsets),
    /// then invokes <c>DebugHook(node, ReadOnlySpan&lt;long&gt;(buffer), heap)</c>.
    ///
    /// The <c>ProgramCounter</c> is also flushed to the current step for
    /// legacy compatibility.
    /// </summary>
    private static Expression WithInterrupt(Expression body, AbiCtx ctx, int step) {
        if (ctx.DebugHookProp is null) return body;

        int localCount = ctx.CurrentLocalCount;

        // Build the hook invocation expression only when the hook is non-null.
        // Emit: if (state.DebugHook != null) { snapshot + invoke }
        var stmts = new List<Expression>();

        // Create a ReadOnlySpan<long> directly over _slots[_fb .. _fb + localCount].
        // This is a zero-allocation slice — no buffer copy needed.
        Expression spanExpr = localCount == 0
            ? New(
                typeof(ReadOnlySpan<long>).GetConstructor([typeof(long[])])!,
                NewArrayBounds(typeof(long), Constant(0)))
            : New(
                typeof(ReadOnlySpan<long>).GetConstructor([typeof(long[]), typeof(int), typeof(int)])!,
                ctx.SlotsLocal, ctx.FramePosLocal, Constant(localCount));

        stmts.Add(Invoke(ctx.DebugHookProp, ctx.CurrentAstNodeExpr, spanExpr, ctx.HeapLocal));

        return Block(
            IfThen(
                NotEqual(ctx.DebugHookProp, Constant(null, typeof(Action<Node, ReadOnlySpan<long>, Heap>))),
                Block(Assign(ctx.StatePcFlush, Constant(step)), Block(stmts))),
            body);
    }

    /// <summary>Convert a CLR constant to the long-based ABI representation.
    /// Only inline-able types; non-inline types must use <see cref="TryValueToLong"/>.</summary>

    // ── Static property/method refs ─────────────────────────────────

    private static readonly PropertyInfo StateStackProperty =
        Ref<VmState>.Property(e => e.Stack);
    private static readonly PropertyInfo ValueStackRawSlotsProperty =
        Ref<ValueStack>.Property(s => s.RawSlots);
    private static readonly PropertyInfo ValueStackStackPointerProperty =
        Ref<ValueStack>.Property(s => s.StackPointer);
    private static readonly PropertyInfo StateRegistersProperty =
        Ref<VmState>.Property(e => e.Registers);
    private static readonly PropertyInfo StateHeapRawSlotsProperty =
        Ref<VmState>.Property(e => e.Heap.RawSlots);
    private static readonly PropertyInfo StateClosureHandleProperty =
        Ref<VmState>.Property(e => e.ClosureHandle);

    // ── Float/double helpers ────────────────────────────────────────

    private static readonly MethodInfo BitConverterInt64BitsToDouble =
        typeof(BitConverter).GetMethod(nameof(BitConverter.Int64BitsToDouble), [typeof(long)])!;

    private static readonly MethodInfo BitConverterDoubleToInt64Bits =
        typeof(BitConverter).GetMethod(nameof(BitConverter.DoubleToInt64Bits), [typeof(double)])!;

    /// <summary>Check if a node is statically known to produce a double/float value.
    /// Returns true when the analysis metadata says the ClrType is double or float.</summary>
    private static bool IsDoubleValue(AbiCtx ctx, Node node) {
        if (ctx.Analysis is null) return false;
        var meta = ctx.Analysis.GetMetadata<ValueRepresentationMetadata>(node);
        if (meta?.ClrType is null) return false;
        return meta.ClrType == typeof(double) || meta.ClrType == typeof(float);
    }

    /// <summary>
    /// ABI Compilation Context — manages ring registers, variable mapping,
    /// and scope tracking for the direct AST-to-ABI emitter.
    ///
    /// RING DISCIPLINE (local, no global allocator):
    /// A stack of "ring slots" (<c>_r0</c>..<c>_rN</c>, each a LINQ
    /// <c>ParameterExpression</c> typed <c>long</c>) represents the
    /// eval stack.  <see cref="RingDepth"/> is the current number of
    /// live values.  Values are placed at successive absolute indices
    /// as they're produced and reclaimed as they're consumed.
    ///
    /// Unlike a global ring allocator, there is no
    /// pre-pass — ring assignment happens inline during the AST walk.
    /// </summary>
    public sealed class AbiCtx {
        private readonly ParameterExpression _stateParam;
        private readonly List<ParameterExpression> _ringVars = new();
        private readonly List<ParameterExpression> _locals = new();

        // ── Construction ─────────────────────────────────────────

        public AbiCtx() : this(8) { }

        public AbiCtx(int registerCount) {
            _registerCount = Math.Clamp(registerCount, 8, MaxRegisterCount);
            _regUsed = new bool[_registerCount];

            _stateParam = Parameter(typeof(VmState), "state");

            ProgramCounter = Variable(typeof(int), "_pc");
            _locals.Add(ProgramCounter);

            SlotsLocal = Variable(typeof(long[]), "_slots");
            _locals.Add(SlotsLocal);

            HeapLocal = Variable(typeof(Heap), "_heap");
            _locals.Add(HeapLocal);

            FramePosLocal = Variable(typeof(int), "_fp");
            _locals.Add(FramePosLocal);

            SavedSp = Variable(typeof(int), "_savedSp");
            _locals.Add(SavedSp);

            ResultLocal = Variable(typeof(long), "_result");
            _locals.Add(ResultLocal);

            // Initialize the register file (user variable cache).
            // Grows on demand up to MaxRegisterCount via GrowRegisterFile.
            _regVars = new List<ParameterExpression>(_registerCount);
            for (int i = 0; i < _registerCount; i++) {
                var r = Variable(typeof(long), $"_reg{i}");
                _regVars.Add(r);
                _locals.Add(r);
            }

            EntryLabel = Label("_entry");
            ExitLabel = Label("_exit");
        }

        // ── Public state ─────────────────────────────────────────

        public ParameterExpression State => _stateParam;
        public ParameterExpression ProgramCounter { get; }
        public ParameterExpression SlotsLocal { get; }
        public ParameterExpression HeapLocal { get; }
        public ParameterExpression FramePosLocal { get; }
        public ParameterExpression SavedSp { get; }
        public ParameterExpression ResultLocal { get; }
        public LabelTarget EntryLabel { get; }
        public LabelTarget ExitLabel { get; }

        /// <summary>Monotonic counter for generating unique label names.</summary>
        public int LabelCounter { get; set; }

        /// <summary>Debug hook callback expression (state.DebugHook), or null in NoDebug mode.
        /// When non-null, the emitter generates local snapshot + hook invocation before each node.</summary>
        public Expression? DebugHookProp { get; set; }

        /// <summary>Expression to read <c>state.CurrentAstNode</c>.</summary>
        public Expression CurrentAstNodeExpr =>
            Property(_stateParam, nameof(VmState.CurrentAstNode));

        /// <summary>
        /// Number of locally declared variables across all active scopes.
        /// Used to size the locals snapshot buffer for the debug hook.
        /// </summary>
        public int CurrentLocalCount {
            get {
                int count = 0;
                foreach (var scope in _scopeStack)
                    count += scope.Count;
                return count;
            }
        }

        /// <summary>Monotonic step counter for DebugInterrupt indexing.
        /// Incremented for each AST node to give stable interrupt points.</summary>
        public int StepCounter { get; set; }

        /// <summary>Map from step number (PC value) to resume label target.
        /// Populated during lowering; consumed to emit the PC-dispatch switch.</summary>
        public LabelTarget RegisterOrGetResumeLabel(int step) {
            if (!_resumeLabels.TryGetValue(step, out var label)) {
                label = Label($"resume_{step}");
                _resumeLabels[step] = label;
            }
            return label;
        }

        private readonly Dictionary<int, LabelTarget> _resumeLabels = new();

        /// <summary>Build the PC-dispatch switch: a single SwitchExpression that Gotos
        /// the right resume label based on <c>state.ProgramCounter</c>.</summary>
        public Expression EmitPcDispatch(Expression defaultBody) {
            if (_resumeLabels.Count == 0) return Empty();
            var cases = new System.Linq.Expressions.SwitchCase[_resumeLabels.Count];
            int i = 0;
            foreach (var (step, label) in _resumeLabels) {
                cases[i++] = System.Linq.Expressions.Expression.SwitchCase(Goto(label), Constant(step));
            }
            return IfThen(
                Equal(Property(_stateParam, nameof(VmState.Status)),
                    Constant(InterpreterStatus.Resuming)),
                System.Linq.Expressions.Expression.Switch(
                    Property(_stateParam, nameof(VmState.ProgramCounter)),
                    defaultBody, cases));
        }

        /// <summary>Expression for <c>state.ProgramCounter</c> — flushed before interrupt.</summary>
        public Expression StatePcFlush => Property(_stateParam, "ProgramCounter");

        /// <summary>Compilation mode for the current emitter context.</summary>
        public CompilationMode Mode { get; set; }

        /// <summary>Number of register file slots in use. Starts at 8, grows on demand to 32.</summary>
        public int RegisterCount => _registerCount;

        /// <summary>
        /// Constant expression referencing the compiled function table array
        /// (<c>Action&lt;VmState&gt;[]</c>), or null if no lambdas are present.
        /// </summary>
        public Expression? FunctionTableExpr { get; set; }

        /// <summary>Analysis result from the standard pipeline, for resolving
        /// member access, type information, call sites, etc.</summary>
        public AnalysisResult? Analysis { get; set; }

        /// <summary>All local variables used in the compiled expression tree.</summary>
        public IReadOnlyList<ParameterExpression> Locals => _locals;

        /// <summary>Maximum slot-based array size that gets allocated in the frame
        /// instead of on the heap.  These use absolute slot indices at or above
        /// <see cref="SmallArraySlotBase"/> so they don't collide with user variables.</summary>
        internal const int SmallArrayThreshold = 16;
        internal const int SmallArraySlotBase = 128; // well past variables, within ValueStack initial capacity
        private int _nextSmallArraySlot = SmallArraySlotBase; // absolute slot base, grows up

        /// <summary>Reserve slots in the frame for a small fixed-size value-type array.
        /// Returns the absolute slot index base (handle) for this array, which is
        /// >= <see cref="SmallArraySlotBase"/> — distinct from heap handles (always &lt; 256).</summary>
        public int AllocateSmallArray() {
            int baseOffset = _nextSmallArraySlot;
            _nextSmallArraySlot += SmallArrayThreshold;
            return baseOffset;
        }

        /// <summary>Expression to initialize <c>_slots</c> from <c>state.Stack.RawSlots</c>.</summary>
        public Expression SlotsInitExpression =>
            Property(Property(_stateParam, StateStackProperty), ValueStackRawSlotsProperty);

        /// <summary>Expression to initialize <c>_heap</c> from <c>state.Heap</c>.</summary>
        public Expression HeapInitExpression =>
            Property(_stateParam, "Heap");

        /// <summary>Expression: <c>state.Registers</c>.</summary>
        public Expression Registers =>
            Property(_stateParam, StateRegistersProperty);

        /// <summary>Expression: <c>state.Heap.RawSlots</c> — the underlying object? array
        /// of the heap, indexed by handle.</summary>
        public Expression HeapRawSlots =>
            Property(HeapLocal, StateHeapRawSlotsProperty);

        /// <summary>Expression: <c>state.ClosureHandle</c>.</summary>
        public Expression ClosureHandle =>
            Property(_stateParam, StateClosureHandleProperty);

        /// <summary>Expression to set the stack pointer (used by return).</summary>
        public Expression SlotsStackPointer =>
            Property(Property(_stateParam, StateStackProperty), ValueStackStackPointerProperty);

        // ── Ring management ──────────────────────────────────────

        /// <summary>Current number of live values on the ring (compile-time depth).</summary>
        public int RingDepth { get; set; }

        private int _maxDepth;

        /// <summary>Peak ring depth observed during compilation.</summary>
        public int MaxRingDepth => _maxDepth;

        /// <summary>Get or create the <c>ParameterExpression</c> for ring slot at absolute index.</summary>
        public ParameterExpression RingVar(int absoluteIndex) {
            while (_ringVars.Count <= absoluteIndex) {
                var v = Variable(typeof(long), $"_r{_ringVars.Count}");
                _ringVars.Add(v);
                _locals.Add(v);
            }
            if (absoluteIndex + 1 > _maxDepth)
                _maxDepth = absoluteIndex + 1;
            return _ringVars[absoluteIndex];
        }

        /// <summary>Allocate a new ring slot (increment depth, ensure var exists).</summary>
        public int AllocSlot() {
            int slot = RingDepth;
            RingDepth = slot + 1;
            RingVar(slot); // ensure it exists
            return slot;
        }

        // ── Frame-local array variable tracking ──────────────────

        // Variables whose most recent assignment was a frame-local NewArray.
        // Maps variable → base offset into _slots (always >= SmallArraySlotBase).
        // Used by EmitIndexAccess to bypass runtime handle dispatch.
        private Dictionary<Variable, int>? _frameLocalVars;

        /// <summary>Track a variable as holding a frame-local array.</summary>
        public void TrackFrameLocalArray(Variable v, int baseOffset) {
            _frameLocalVars ??= new(ReferenceEqualityComparer.Instance);
            _frameLocalVars[v] = baseOffset;
        }

        /// <summary>Remove a variable from frame-local tracking (e.g. on reassignment).</summary>
        public void UntrackFrameLocalArray(Variable v) {
            _frameLocalVars?.Remove(v);
        }

        /// <summary>Get the frame-local base offset for a variable, or null if not tracked.</summary>
        public int? TryGetFrameLocalBase(Variable v) =>
            _frameLocalVars is { } dict && dict.TryGetValue(v, out int baseOffset) ? baseOffset : null;

        // ── Variable scope management / Register file ────────────

        private readonly Stack<Dictionary<Variable, int>> _scopeStack = new();
        private readonly Dictionary<Variable, int> _variableRegisters = new(ReferenceEqualityComparer.Instance);
        private readonly Stack<List<Variable>> _scopeVars = new();

        // Configurable register file for user variables. The JIT can enregister
        // a small set of locals far more efficiently than per-variable ones or
        // array accesses. Default is 8; grows on demand up to MaxRegisterCount
        // when a scope needs more.
        private const int MaxRegisterCount = 32;
        private int _registerCount;
        private readonly List<ParameterExpression> _regVars;
        private bool[] _regUsed;

        /// <summary>Enter a new block scope for variable declarations.</summary>
        public void PushScope() {
            _scopeStack.Push(new Dictionary<Variable, int>(ReferenceEqualityComparer.Instance));
            _scopeVars.Push(new List<Variable>());
        }

        /// <summary>Exit the current block scope — emits no expressions;
        /// the caller calls <see cref="EmitScopeStores"/> before this.</summary>
        public void PopScope() {
            // Free registers allocated in this scope
            if (_scopeVars.Count > 0) {
                foreach (var v in _scopeVars.Peek()) {
                    if (_variableRegisters.TryGetValue(v, out int regIdx)) {
                        _regUsed[regIdx] = false;
                        _variableRegisters.Remove(v);
                    }
                }
            }
            _scopeStack.Pop();
            _scopeVars.Pop();
        }

        /// <summary>Store-back expressions for the current scope, flushing register
        /// values back to <c>_slots</c>. Call BEFORE <see cref="PopScope"/>.</summary>
        public IReadOnlyList<Expression> EmitScopeStores() {
            if (_scopeVars.Count == 0 || _scopeStack.Count == 0) return Array.Empty<Expression>();
            var vars = _scopeVars.Peek();
            var scope = _scopeStack.Peek();
            var stores = new List<Expression>(vars.Count);
            foreach (var v in vars) {
                if (_variableRegisters.TryGetValue(v, out int regIdx) && scope.TryGetValue(v, out int slot)) {
                    stores.Add(Assign(
                        ArrayAccess(SlotsLocal, Add(FramePosLocal, Constant(slot))),
                        _regVars[regIdx]));
                }
            }
            return stores;
        }

        /// <summary>Load expressions for the current scope, loading register
        /// values from <c>_slots</code> into the register file.</summary>
        public IReadOnlyList<Expression> EmitScopeLoads() {
            if (_scopeVars.Count == 0 || _scopeStack.Count == 0) return Array.Empty<Expression>();
            var vars = _scopeVars.Peek();
            var scope = _scopeStack.Peek();
            var loads = new List<Expression>(vars.Count);
            foreach (var v in vars) {
                if (_variableRegisters.TryGetValue(v, out int regIdx) && scope.TryGetValue(v, out int slot)) {
                    loads.Add(Assign(
                        _regVars[regIdx],
                        ArrayAccess(SlotsLocal, Add(FramePosLocal, Constant(slot)))));
                }
            }
            return loads;
        }

        /// <summary>Declare a variable, allocating it to a register file slot.
        /// Writes go through the register; the caller must emit
        /// <see cref="EmitScopeStores"/> before scope exit.</summary>
        public void DeclareVariable(Variable v) {
            if (_scopeStack.Count == 0)
                throw new InvalidOperationException("No active scope");
            int slot = _scopeStack.Peek().Count;
            _scopeStack.Peek()[v] = slot;
            // Allocate a register file slot, growing on demand up to MaxRegisterCount
            int regIdx = -1;
            while (regIdx < 0) {
                for (int i = 0; i < _registerCount; i++) {
                    if (!_regUsed[i]) { regIdx = i; break; }
                }
                if (regIdx < 0 && _registerCount < MaxRegisterCount) {
                    GrowRegisterFile();
                }
                else {
                    break;
                }
            }
            if (regIdx < 0) {
                // Out of registers — use _slots directly (fallback; should be rare)
                _scopeVars.Peek().Add(v);
                _variableLayouts.Add(new VariableLayout(v.Name, slot));
                return;
            }
            _regUsed[regIdx] = true;
            _variableRegisters[v] = regIdx;
            _scopeVars.Peek().Add(v);
            _variableLayouts.Add(new VariableLayout(v.Name, slot));
        }

        /// <summary>Grow the register file by 8 (up to <see cref="MaxRegisterCount"/>).
        /// Creates new LINQ ParameterExpression variables and adds them to the
        /// expression tree locals list so they're available at compile time.</summary>
        private void GrowRegisterFile() {
            int old = _registerCount;
            int grown = Math.Min(old + 8, MaxRegisterCount);
            int added = grown - old;
            // Expand _regUsed
            var newRegUsed = new bool[grown];
            Array.Copy(_regUsed, newRegUsed, old);
            _regUsed = newRegUsed;
            // Add new register variables
            for (int i = old; i < grown; i++) {
                var r = Variable(typeof(long), $"_reg{i}");
                _regVars.Add(r);
                _locals.Add(r);
            }
            _registerCount = grown;
        }

        private readonly List<VariableLayout> _variableLayouts = new();

        /// <summary>Variable layouts collected during lowering for debug info.</summary>
        public IReadOnlyList<VariableLayout> VariableLayouts => _variableLayouts;

        /// <summary>Try to resolve a variable to its value-stack slot index.</summary>
        public bool TryGetVariable(Variable v, out int slot) {
            foreach (var scope in _scopeStack) {
                if (scope.TryGetValue(v, out slot))
                    return true;
            }
            slot = -1;
            return false;
        }

        /// <summary>Number of active function parameters that occupy value stack
        /// slots BEFORE local variables. Parameters live at <c>_slots[_fb - ParamSlotOffset + paramIndex]</c>,
        /// variables live at <c>_slots[_fb + varIndex]</c>.</summary>
        public int ParamSlotOffset { get; set; }

        /// <summary>Expression to read a variable from the value stack: <c>_slots[_fb + varIndex]</c>.
        /// Fallback when no register is available.</summary>
        public Expression VariableRead(int varIndex) =>
            ArrayAccess(SlotsLocal, Add(FramePosLocal, Constant(varIndex)));

        /// <summary>Read a variable from its register file slot.
        /// The JIT enregisters this for hot-loop performance.</summary>
        public Expression VariableRead(Variable v) {
            if (_variableRegisters.TryGetValue(v, out int regIdx))
                return _regVars[regIdx];
            if (TryGetVariable(v, out int slotIndex))
                return VariableRead(slotIndex);
            throw new InvalidOperationException($"Variable '{v.Name}' not declared in any scope");
        }

        /// <summary>Expression to write to a variable: <c>_slots[_fb + varIndex] = value</c>.
        /// Fallback when no register is available.</summary>
        public Expression VariableWrite(int varIndex, Expression value) =>
            Assign(VariableRead(varIndex), value);

        /// <summary>Write a variable to its register file slot.
        /// The JIT enregisters this for hot-loop performance.</summary>
        public Expression VariableWrite(Variable v, Expression value) {
            if (_variableRegisters.TryGetValue(v, out int regIdx))
                return Assign(_regVars[regIdx], value);
            if (TryGetVariable(v, out int slotIndex))
                return VariableWrite(slotIndex, value);
            throw new InvalidOperationException($"Variable '{v.Name}' not declared in any scope");
        }

        /// <summary>Read a function parameter from the value stack.
        /// Parameters are stored BEFORE the local variable region:
        /// <c>_slots[_fb - ParamSlotOffset + paramIndex]</c>.</summary>
        public Expression ParameterRead(int paramIndex) =>
            _inlineParameterMap is { } map && map.TryGetValue(paramIndex, out int ringSlot)
                ? RingVar(ringSlot)
                : (Expression)ArrayAccess(SlotsLocal,
                    Add(FramePosLocal, Constant(paramIndex - ParamSlotOffset)));

        // ── Inline parameter mapping (for small lambda inlining) ─

        private Dictionary<int, int>? _inlineParameterMap;

        /// <summary>Map a parameter index to a ring slot for inlined lambda invocation.</summary>
        public void MapInlineParameter(int paramIndex, int ringSlot) {
            _inlineParameterMap ??= new();
            _inlineParameterMap[paramIndex] = ringSlot;
        }

        /// <summary>Clear all inline parameter mappings.</summary>
        public void ClearInlineParameters() => _inlineParameterMap = null;

        // ── Loop scope management (for break/continue) ──────────

        private readonly Stack<(LabelTarget breakLabel, LabelTarget continueLabel)> _loopScopes = new();

        /// <summary>Enter a loop scope with the given break and continue labels.</summary>
        public void PushLoopScope(LabelTarget breakLabel, LabelTarget continueLabel) {
            _loopScopes.Push((breakLabel, continueLabel));
        }

        /// <summary>Exit the current loop scope.</summary>
        public void PopLoopScope() => _loopScopes.Pop();

        /// <summary>Get the current loop's break/continue labels, or null if not in a loop.</summary>
        public (LabelTarget breakLabel, LabelTarget continueLabel)? CurrentLoopLabels =>
            _loopScopes.Count > 0 ? _loopScopes.Peek() : null;

        // ── Label management (for goto/label declarations) ──────

        private readonly Dictionary<string, LabelTarget> _labels = new();

        /// <summary>Get or create a label target by name.</summary>
        public LabelTarget GetLabel(string name) {
            if (!_labels.TryGetValue(name, out var target)) {
                target = Label(name);
                _labels[name] = target;
            }
            return target;
        }

        /// <summary>Check if a label has been created.</summary>
        public bool HasLabel(string name) => _labels.ContainsKey(name);

        // ── Parameter management ────────────────────────────────

        private readonly Dictionary<Parameter, int> _parameters = new(ReferenceEqualityComparer.Instance);
        private int _nextParamSlot;

        /// <summary>Register a parameter and assign it the next sequential slot index.</summary>
        public int DeclareParameter(Parameter p) {
            int slot = _nextParamSlot++;
            _parameters[p] = slot;
            return slot;
        }

        /// <summary>Try to resolve a parameter to its slot index.</summary>
        public bool TryGetParameterSlot(Parameter p, out int slot) =>
            _parameters.TryGetValue(p, out slot);

        /// <summary>Save current param slot counter and reset to 0 for a new invocation level.</summary>
        public int SaveAndResetParamSlots() {
            int saved = _nextParamSlot;
            _nextParamSlot = 0;
            return saved;
        }

        /// <summary>Restore the param slot counter from an outer invocation level.</summary>
        public void RestoreParamSlots(int saved) => _nextParamSlot = saved;

        // ── Capture management (upvalues in lambda bodies) ────────

        private readonly Dictionary<Variable, int> _capturedVars = new(ReferenceEqualityComparer.Instance);

        /// <summary>Register a variable as a closure capture with the given capture-array index.</summary>
        public void DeclareCapture(Variable v, int captureIndex) {
            _capturedVars[v] = captureIndex;
        }

        /// <summary>Check if a variable is a capture and get its capture-array index.</summary>
        public bool TryGetCapture(Variable v, out int captureIndex) =>
            _capturedVars.TryGetValue(v, out captureIndex);

        // List of nodes indexed by the step/PC assigned during lowering.
        // This is passed to VmProgram so the debugger can resolve PC -> Node
        // for stack traces, variable name lookup (via the node's scope), etc.
        private readonly List<Node?> _stepNodes = new();
        public IReadOnlyList<Node> StepNodes => _stepNodes.ToArray().Where(n => n is not null).Select(n => n!).ToList().AsReadOnly();

        /// <summary>Record the node for a given step index (used to populate StepNodes for PC->Node debug mapping).</summary>
        public void RecordStepNode(int step, Node node) {
            while (_stepNodes.Count <= step)
                _stepNodes.Add(null);
            _stepNodes[step] = node;
        }

        // ── Compile-time stack / frame simulator (used only while emitting) ──
        //
        // This lets the lowering "simulate" SP adjustments and frame allocation
        // at compile time. Because we know argument/local counts for every
        // scope at lowering time, we can pre-compute exact offsets for every
        // user variable and only emit the minimal runtime operations
        // (the 2-word frame header push + SP advance) at actual call boundaries.
        //
        // Inside a function body the emitted code uses a frameBase local +
        // constant offsets — almost no runtime arithmetic for variable access.

        private readonly Stack<CompileTimeFrame> _ctFrames = new();
        private int _ctSp; // virtual stack pointer, only for layout decisions during emit

        private sealed class CompileTimeFrame {
            public int ArgumentCount { get; }
            public int LocalCount { get; }
            public int BaseOffset { get; }   // offset from this frame's "frame base" where data starts
            public int HeaderSize { get; }   // size of the frame header (0 for current layout, 2 for 2-value model)

            public CompileTimeFrame(int args, int locals, int baseOffset, int headerSize) {
                ArgumentCount = args;
                LocalCount = locals;
                BaseOffset = baseOffset;
                HeaderSize = headerSize;
            }
        }

        /// <summary>
        /// Called during lowering when we cross into a new activation (function entry
        /// or non-inlined call). We "push" a frame in the virtual model.
        /// </summary>
        /// <param name="argumentCount">Number of arguments for this activation.</param>
        /// <param name="localCount">Number of local variables for this activation.</param>
        /// <param name="headerSize">Size of the frame header in words (2 for the full
        /// 2-value frame model; 0 for the current layout where no header is pushed).</param>
        public void EnterActivation(int argumentCount, int localCount, int headerSize = 0) {
            // The data for this frame starts after the header (if any).
            var frame = new CompileTimeFrame(argumentCount, localCount, _ctSp + headerSize, headerSize);
            _ctFrames.Push(frame);
            _ctSp += headerSize + argumentCount + localCount;
        }

        public void LeaveActivation() {
            if (_ctFrames.Count == 0) throw new InvalidOperationException("No active frame");
            var f = _ctFrames.Pop();
            _ctSp -= f.HeaderSize + f.ArgumentCount + f.LocalCount;
        }

        /// <summary>
        /// Returns the compile-time offset for a user variable relative to
        /// the current frame base. The emitted expression will be something like
        /// ArrayAccess(frameBaseLocal, Constant(offset)).
        /// 
        /// Currently returns the scope-relative slot index directly, since _fb
        /// points to the start of the local variable area (no header pushed on
        /// the runtime stack yet). When the 2-word header is added to the
        /// runtime preamble, this method will account for the header size,
        /// argument area, and interleaved linkage values.
        /// </summary>
        public int GetCompileTimeVariableOffset(Variable v) {
            if (!TryGetVariable(v, out int slotInScope))
                throw new InvalidOperationException($"Variable '{v.Name}' has no slot");
            return slotInScope;
        }

        /// <summary>
        /// The exact number of stack slots this frame occupies (known at lowering time).
        /// Used when emitting the "advance SP" part of a frame prologue.
        /// </summary>
        public int GetCurrentFrameSize() =>
            _ctFrames.Count == 0 ? 0 : _ctFrames.Peek().ArgumentCount + _ctFrames.Peek().LocalCount + 2;

        // Note: the *runtime* side of this is the CallStack class (and the
        // actual pushes of the two Word values + SP adjustment that we emit).
    }
}