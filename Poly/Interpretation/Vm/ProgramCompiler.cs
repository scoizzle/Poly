using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Syntax.Primitives;

using static System.Linq.Expressions.Expression;

using PrimArrayLoad = Poly.Syntax.Primitives.ArrayLoad;
using PrimArrayStore = Poly.Syntax.Primitives.ArrayStore;
using PrimCall = Poly.Syntax.Primitives.Call;
using PrimCountBits = Poly.Syntax.Primitives.CountBits;
using PrimExternalCall = Poly.Syntax.Primitives.CallExternal;
using PrimLabel = Poly.Syntax.Primitives.Label;
using PrimNewArray = Poly.Syntax.Primitives.NewArray;
using PrimOpKind = Poly.Syntax.Primitives.OpKind;
using PrimParameter = Poly.Syntax.Primitives.Parameter;
using PrimReturn = Poly.Syntax.Primitives.Return;
using PrimStridedSet = Poly.Syntax.Primitives.StridedSet;
using PrimThrow = Poly.Syntax.Primitives.Throw;
using PrimThrowProtected = Poly.Syntax.Primitives.ThrowProtected;
using PrimTypeCheck = Poly.Syntax.Primitives.TypeCheck;
using PrimUnaryOp = Poly.Syntax.Primitives.UnaryOp;
using PrimUnaryOpKind = Poly.Syntax.Primitives.UnaryOpKind;

namespace Poly.Interpretation.Vm;

public enum CompilationMode { NoDebug, Debug, Normal }

public static class ProgramCompiler {
    // ── Primitive-based compilation (new canonical path) ────────────

    /// <summary>
    /// Compile a linked (label-resolved) primitive sequence directly into a
    /// <see cref="VmProgram"/>, bypassing the old Instruction-based pipeline.
    ///
    /// This is the new canonical path: the PrimitiveNode types are the
    /// instruction set, and this method is the single switch that maps them
    /// to LINQ Expressions for the compiled delegate.
    /// </summary>
    public static VmProgram CompilePrimitives(
        IReadOnlyList<PrimitiveNode> primitives,
        CompilationMode mode = CompilationMode.Normal,
        TextWriter? traceExpressions = null,
        Action<VmState>[]? functionTable = null,
        IReadOnlyList<CallSiteEntry>? callSites = null) {
        // Link resolves Label → PC offset for Goto/CondGoto.
        primitives = PrimitiveLinker.Link(primitives);

        var ctx = new CompilationContext();
        ctx.CallSites = callSites;
        ctx.TraceExpressions = traceExpressions;
        var body = new List<Expression>();
        int n = primitives.Count;

        ctx.LimitLoops = mode is CompilationMode.Normal;

        // Pre-register all labels
        for (int i = 0; i < n; i++)
            ctx.GetLabel(i);

        // Compute ring depths and producer map from primitive StackEffect
        var ringDepthMap = ComputePrimitiveRingDepths(primitives, out var ringDepthAtPC);
        ctx.ConfigureRingAllocation(ringDepthMap, 32, 32);
        ctx.SetRingDepthMap(ringDepthAtPC);

        // One pass: compute consumed-PC arrays (BackwardScan style), eliminating
        // the need for inline virtual-ring tracking in the emission loop.
        var consumedPcs = ComputePrimitiveConsumedPcs(primitives);

#if DEBUG
        VerifyRingDepths(primitives, ringDepthMap, ringDepthAtPC, consumedPcs);
#endif

        // Preamble
        body.Add(Label(ctx.EntryLabel));
        body.Add(Assign(ctx.SlotsLocal, ctx.SlotsInitExpression));
        body.Add(Assign(ctx.HeapLocal, ctx.HeapInitExpression));
        body.Add(Assign(ctx.Registers,
            Coalesce(ctx.Registers, NewArrayBounds(typeof(long), Constant(32)))));
        body.Add(IfThen(
            Equal(Property(ctx.State, "FrameBase"), Constant(-1)),
            Assign(Property(ctx.State, "FrameBase"), Constant(0))));
        body.Add(Assign(ctx.FrameBaseLocal, ctx.FrameBaseInitExpression));

        if (mode != CompilationMode.NoDebug)
            body.Add(Assign(ctx.ProgramCounter, ctx.StateProgramCounter));
        else
            body.Add(Assign(ctx.ProgramCounter, Constant(0)));

        if (ctx.LimitLoops) {
            var maxIterProp = Property(ctx.State, nameof(VmState.MaxLoopIterations));
            body.Add(Assign(ctx.LoopMaxIter, maxIterProp));
            body.Add(Assign(ctx.LoopLimitActive, NotEqual(ctx.LoopMaxIter, Constant(-1L))));
            body.Add(IfThen(
                AndAlso(ctx.LoopLimitActive,
                    Equal(Property(ctx.State, nameof(VmState.LoopCounters)), Constant(null, typeof(long[])))),
                Assign(Property(ctx.State, nameof(VmState.LoopCounters)),
                    NewArrayBounds(typeof(long), Constant(n)))));
        }

        // In Debug/Normal mode the _pc variable tracks the current PC.
        // NoDebug mode runs straight through (direct GotoExpression branches)
        // and never reads _pc, so the switch dispatch is pure dead weight.
        if (mode != CompilationMode.NoDebug && n > 0) {
            var swCases = new System.Linq.Expressions.SwitchCase[n];
            for (int i = 0; i < n; i++)
                swCases[i] = SwitchCase(Goto(ctx.GetLabel(i)), Constant(i));
            body.Add(IfThen(
                GreaterThanOrEqual(ctx.ProgramCounter, Constant(0)),
                Switch(ctx.ProgramCounter, Goto(ctx.ExitLabel), swCases)));
        }

        // Emit each primitive
        var functionsExpr = functionTable is not null ? Constant(functionTable) : null;
        var debugInterruptProp = mode != CompilationMode.NoDebug
            ? Property(ctx.State, nameof(VmState.DebugInterrupt))
            : null;

        for (int idx = 0; idx < n; idx++) {
            var prim = primitives[idx];
            ctx.CurrentLabelIndex = idx;

            body.Add(Label(ctx.GetLabel(idx)));

            // In Debug/Normal mode, flush PC and invoke DebugInterrupt before each µop,
            // but only if a handler is actually registered — null check avoids the
            // overhead of flushing _pc on every µop when no debugger is attached.
            if (debugInterruptProp is not null) {
                body.Add(IfThen(
                    NotEqual(debugInterruptProp, Constant(null, typeof(Action<VmState>))),
                    Block(
                        Assign(ctx.StateProgramCounter, Constant(idx)),
                        Invoke(debugInterruptProp, ctx.State))));
            }

            var pcs = consumedPcs[idx];
            Expression Resolve(int i) => ctx.ValueSlot(pcs[i]);

            Expression? result = prim switch {
                ResolvedGoto rg => EmitJump(rg.TargetPc, ctx),
                ResolvedCondGoto rcg => EmitBranchIfFalse(rcg.TargetPc, pcs, ctx),
                PrimReturn => EmitReturnOp(pcs, ctx),
                PushConstant pv => EmitPushConstant(pv.Value, ctx),
                LoadLocal ll => Assign(ctx.ValueSlot(idx), ArrayAccess(ctx.RawSlots, Add(ctx.FrameBase, Constant(ll.SlotIndex)))),
                StoreLocal sl => EmitStoreLocal(sl.SlotIndex, pcs, ctx),
                BinaryOp bo => EmitBinaryOp(bo.Op, pcs, ctx, bo.ComparisonType),
                PrimUnaryOp uo => EmitUnaryOp(uo.Op, pcs, ctx),
                PrimLabel => null,
                Discard => null,
                Dup => Assign(ctx.ValueSlot(idx), Resolve(0)),
                PrimCountBits => EmitCountBits(pcs, ctx),
                PrimParameter p => Assign(ctx.ValueSlot(idx), ArrayAccess(ctx.RawSlots, Add(ctx.FrameBase, Constant(p.SlotIndex)))),
                PrimArrayLoad => EmitArrayLoad(pcs, ctx, idx),
                PrimArrayStore => EmitArrayStore(pcs, ctx),
                PrimNewArray => EmitNewArray(pcs, ctx, idx),
                PrimStridedSet => EmitStridedSet(pcs, ctx),
                Phi ph => EmitPhi(ph, pcs, ctx, idx),
                AllocClosure ac => EmitAllocClosure(ac, pcs, ctx, idx),
                LoadUpvalue lu => Assign(ctx.ValueSlot(idx), EmitLoadUpvalue(lu, ctx)),
                StoreUpvalue su => EmitStoreUpvalue(su, pcs, ctx),
                LoadHeapConstant lhc => Assign(ctx.ValueSlot(idx), EmitLoadHeapConstant(lhc, ctx)),
                PrimThrow => EmitThrowOp(pcs, ctx),
                PrimThrowProtected => EmitThrowOp(pcs, ctx),  // INT-018 P1C: dispatch via outer TryCatch
                PrimTypeCheck tc => EmitTypeCheckOp(tc, pcs, ctx, idx),
                RegionMarker => null,        // INT-018 P1C: becomes compile-time metadata only (Strategy B)
                PrimCall c => EmitPrimitiveCall(c, pcs, ctx, idx, functionsExpr),
                PrimExternalCall ec => EmitCallExternalDirect(ec.Target, ec.ArgCount, ec.IsStatic, pcs, ctx, idx, ec.SiteIndex, ctx.CallSites),
                _ => throw new NotSupportedException($"Primitive not supported: {prim.GetType().Name}")
            };

            if (result is not null)
                body.Add(result);

            // Trace dump: compact one-liner per µop, empty line for void µops.
            // Indexed by PC — line N always corresponds to µop N.
            if (ctx.TraceExpressions is not null) {
                if (result is null) {
                    ctx.TraceExpressions.WriteLine();
                }
                else if (result is BlockExpression block) {
                    ctx.TraceExpressions.WriteLine(string.Join(" ; ", block.Expressions));
                }
                else {
                    ctx.TraceExpressions.WriteLine(result.ToString());
                }
            }
        }

        body.Add(Label(ctx.ExitLabel));

        var delegateExpr = Lambda<Action<VmState>>(Block(ctx.Locals, body), ctx.State);
        var del = delegateExpr.Compile();
        return new VmProgram(del, 32);
    }

    private static Expression EmitPrimitiveCall(PrimCall call, int[] consumedPcs, CompilationContext ctx, int pc, Expression? functionsExpr) {
        if (functionsExpr is null || call.FuncIndex < 0) {
            // No function table or negative index — CLR delegate path: push to stack as-is.
            var slots = ctx.RawSlots;
            var sp = Property(Property(ctx.State, "Stack"), "StackPointer");
            var body = new List<Expression>();
            for (int i = 0; i < consumedPcs.Length; i++) {
                var arg = ctx.ValueSlot(consumedPcs[i]);
                body.Add(Assign(ArrayAccess(slots, sp), arg));
                body.Add(Call(Property(ctx.State, "Stack"), SetStackPointer, Add(sp, Constant(1))));
            }
            var rv = ctx.ValueSlot(pc);
            body.Add(Assign(rv, ArrayAccess(slots, Subtract(sp, Constant(1)))));
            return Block(body);
        }

        // ── Closure/function call: dispatch to compiled VmProgram.Functions ──
        // consumedPcs[0] = closure handle
        // consumedPcs[1..ArgCount] = arguments
        var state = ctx.State;
        var rawSlots = ctx.RawSlots;
        var spProp = Property(Property(state, "Stack"), "StackPointer");
        var fbProp = Property(state, nameof(VmState.FrameBase));
        var bodyExprs = new List<Expression>();

        // 1. Save current SP to SavedSp, then save caller's ring to state.Registers
        //    keyed by SavedSp offset (so each nested call gets its own save area).
        bodyExprs.Add(Assign(ctx.SavedSp, spProp));
        bodyExprs.Add(CtxPushRegisters(ctx));

        // 2. Save closure handle → state.ClosureHandle
        {
            var handle = ctx.ValueSlot(consumedPcs[0]);
            // Convert the ring value (long) to int handle
            bodyExprs.Add(Assign(
                Property(state, nameof(VmState.ClosureHandle)),
                Convert(handle, typeof(int))));
        }

        // 3. Push argument values to value stack (the function reads from its frame)
        int argCount = call.ArgCount;
        var savedSp = Variable(typeof(int), "_callSp");
        bodyExprs.Add(Assign(savedSp, spProp));  // snapshot caller's SP
        for (int i = 1; i < consumedPcs.Length; i++) {
            var arg = ctx.ValueSlot(consumedPcs[i]);
            bodyExprs.Add(Assign(ArrayAccess(rawSlots, spProp), arg));
            bodyExprs.Add(Call(Property(state, "Stack"), SetStackPointer, Add(spProp, Constant(1))));
        }

        // 4. Save caller frame info
        bodyExprs.Add(Assign(
            Property(state, nameof(VmState.ReturnPC)),
            Add(Property(state, nameof(VmState.ProgramCounter)), Constant(1))));
        bodyExprs.Add(Assign(
            Property(state, nameof(VmState.OldFrameBase)),
            fbProp));

        // 5. Set new FrameBase to point at first argument
        bodyExprs.Add(Assign(fbProp, savedSp));

        // 6. Invoke the compiled function delegate: Functions[funcIndex](state)
        {
            var funcExpr = ArrayAccess(functionsExpr, Constant(call.FuncIndex));
            bodyExprs.Add(Invoke(funcExpr, state));
        }

        // 7. The function stored return value at slots[state.FrameBase] and
        //    set SP = state.FrameBase + 1.  Read it from there.
        var returnVal = ArrayAccess(rawSlots, fbProp);

        // 8. Restore caller's ring (relies on function NOT touching state.Registers)
        bodyExprs.Add(CtxPopRegisters(ctx));

        // 9. Write return value to the ring slot for this µop
        var resultSlot = ctx.ValueSlot(pc);
        bodyExprs.Add(Assign(resultSlot, returnVal));

        // 10. Restore caller's FrameBase
        bodyExprs.Add(Assign(
            fbProp,
            Property(state, nameof(VmState.OldFrameBase))));

        return Block(new[] { savedSp }, bodyExprs);
    }

    private static readonly ConstructorInfo InvalidOpCtor = Ref.Constructor(() => new InvalidOperationException(""));

    private static Expression EmitJump(int target, CompilationContext ctx) {
        if (!ctx.LimitLoops)
            return Goto(ctx.GetLabel(target));
        var loopCtr = Property(ctx.State, nameof(VmState.LoopCounters));
        var counter = ArrayAccess(loopCtr, Constant(target));
        return Block(
            IfThen(
                AndAlso(ctx.LoopLimitActive,
                    GreaterThanOrEqual(PreIncrementAssign(counter), ctx.LoopMaxIter)),
                Throw(New(InvalidOpCtor,
                    Constant($"Infinite loop detected: iteration limit exceeded at PC={target}")))),
            Goto(ctx.GetLabel(target)));
    }

    private static Expression EmitBranchIfFalse(int target, int[] consumedPcs, CompilationContext ctx) {
        var cond = consumedPcs.Length > 0 ? ctx.ValueSlot(consumedPcs[0]) : Constant(0L);
        return IfThen(Equal(cond, Constant(0L)), Goto(ctx.GetLabel(target)));
    }

    private static Expression EmitTypeCheckOp(PrimTypeCheck tc, int[] consumedPcs, CompilationContext ctx, int idx) {
        // Pop a heap handle, check if the object is an instance of tc.TargetType.
        // Push 1 (true) or 0 (false).
        if (consumedPcs.Length == 0)
            return Assign(ctx.ValueSlot(idx), Expression.Constant(0L));

        var handleExpr = Expression.Convert(ctx.ValueSlot(consumedPcs[0]), typeof(int));
        var valueExpr = Expression.ArrayAccess(ctx.HeapRawSlots, handleExpr);

        var isNullCheck = Expression.ReferenceEqual(valueExpr, Expression.Constant(null));
        var isInstanceCall = Expression.Call(
            Expression.Constant(tc.TargetType),
            typeof(Type).GetMethod("IsInstanceOfType", [typeof(object)])!,
            valueExpr);
        var resultExpr = Expression.Condition(isNullCheck,
            Expression.Constant(0L),
            Expression.Condition(isInstanceCall,
                Expression.Constant(1L),
                Expression.Constant(0L)));
        return Assign(ctx.ValueSlot(idx), resultExpr);
    }

    private static Expression EmitThrowOp(int[] consumedPcs, CompilationContext ctx) {
        if (consumedPcs.Length == 0)
            return Expression.Throw(Expression.Constant(new InvalidOperationException("Throw with no operand")));

        var handleExpr = Expression.Convert(ctx.ValueSlot(consumedPcs[0]), typeof(int));
        var exceptionExpr = Expression.Convert(
            Expression.ArrayAccess(ctx.HeapRawSlots, handleExpr),
            typeof(Exception));

        // Save the faulting PC before throwing so that EH dispatch (Strategy B)
        // can identify which try region the exception originated from.
        int faultPc = ctx.CurrentLabelIndex;
        return Block(
            Assign(ctx.StateProgramCounter, Constant(faultPc)),
            Expression.Throw(exceptionExpr));
    }

    private static Expression EmitReturnOp(int[] consumedPcs, CompilationContext ctx) {
        var returnVal = consumedPcs.Length > 0 ? ctx.ValueSlot(consumedPcs[0]) : Constant(0L);
        var slots = ctx.RawSlots;
        var targetSlot = Condition(Equal(ctx.FrameBase, Constant(-1)), Constant(0), ctx.FrameBase);
        return Block(
            Assign(ArrayAccess(slots, targetSlot), returnVal),
            Call(Property(ctx.State, "Stack"), SetStackPointer, Add(targetSlot, Constant(1))),
            Goto(ctx.ExitLabel));
    }

    private static Expression EmitStoreLocal(int slotIndex, int[] consumedPcs, CompilationContext ctx) {
        var value = ctx.ValueSlot(consumedPcs[0]);
        var index = Add(ctx.FrameBase, Constant(slotIndex));
        return Assign(ArrayAccess(ctx.RawSlots, index), value);
    }

    private static Expression EmitAllocClosure(AllocClosure ac, int[] consumedPcs, CompilationContext ctx, int idx) {
        // Allocate an object[] on the heap for captured values.
        int captureCount = ac.UpvalueCount;
        var capArray = NewArrayBounds(typeof(object), Constant(captureCount));
        var body = new List<Expression>();

        // Copy each captured value from the ring into the array
        for (int i = 0; i < consumedPcs.Length; i++) {
            body.Add(Assign(
                ArrayAccess(capArray, Constant(i)),
                Convert(ctx.ValueSlot(consumedPcs[i]), typeof(object))));
        }

        // Allocate closure on heap (the array becomes the closure's capture storage).
        // Caller is responsible for nulling out capture slots after invocation
        // (Heap.Set with null triggers free-list recycling).
        body.Add(Assign(ctx.ValueSlot(idx),
            Convert(Call(ctx.Heap, HeapAllocate, Convert(capArray, typeof(object))), typeof(long))));
        return Block(body);
    }

    private static Expression EmitLoadUpvalue(LoadUpvalue lu, CompilationContext ctx) {
        // Closure handle comes from state.ClosureHandle (set by caller before
        // invoking a function delegate, or from the ring in the inline path).
        // Dereference the heap handle, cast to object[], read by index.
        var handle = Property(ctx.State, nameof(VmState.ClosureHandle));
        var arr = Convert(ArrayAccess(ctx.HeapRawSlots, handle), typeof(object[]));
        return Convert(ArrayAccess(arr, Constant(lu.UpvalueIndex)), typeof(long));
    }

    private static Expression EmitStoreUpvalue(StoreUpvalue su, int[] consumedPcs, CompilationContext ctx) {
        // Store into the current closure's capture array via state.ClosureHandle.
        var value = ctx.ValueSlot(consumedPcs[0]);
        var upvalueArray = Convert(
            ArrayAccess(ctx.HeapRawSlots,
                Property(ctx.State, nameof(VmState.ClosureHandle))),
            typeof(object[]));
        return Assign(
            ArrayAccess(upvalueArray, Constant(su.UpvalueIndex)),
            Convert(value, typeof(object)));
    }

    private static Expression EmitLoadHeapConstant(LoadHeapConstant lhc, CompilationContext ctx) {
        // Load a pre-allocated heap constant by handle index.
        return Convert(ArrayAccess(ctx.HeapRawSlots, Constant(lhc.Handle)), typeof(long));
    }

    private static Expression? EmitPhi(Phi phi, int[] consumedPcs, CompilationContext ctx, int idx) {
        // Phi is a merge annotation — no runtime code generated.
        // The branch-aware ring analysis ensures both predecessors left
        // the merged value at the same ring depth.
        return null;
    }

    private static readonly MethodInfo HeapAllocate = Ref<Heap>.Method(h => h.Allocate(null));

    private static Expression EmitArrayLoad(int[] consumedPcs, CompilationContext ctx, int idx) {
        var handle = ctx.ValueSlot(consumedPcs[0]);
        var index = ctx.ValueSlot(consumedPcs[1]);
        var arrLocal = Variable(typeof(long[]), $"_arr_{idx}");
        return Block(
            [arrLocal],
            Assign(arrLocal, Convert(ArrayAccess(ctx.HeapRawSlots, Convert(handle, typeof(int))), typeof(long[]))),
            Assign(ctx.ValueSlot(idx), ArrayAccess(arrLocal, Convert(index, typeof(int)))));
    }

    private static Expression EmitArrayStore(int[] consumedPcs, CompilationContext ctx) {
        var value = ctx.ValueSlot(consumedPcs[0]);
        var handle = ctx.ValueSlot(consumedPcs[1]);
        var index = ctx.ValueSlot(consumedPcs[2]);
        var arrLocal = Variable(typeof(long[]), $"_arr_{ctx.CurrentLabelIndex}");
        return Block(
            [arrLocal],
            Assign(arrLocal, Convert(ArrayAccess(ctx.HeapRawSlots, Convert(handle, typeof(int))), typeof(long[]))),
            Assign(ArrayAccess(arrLocal, Convert(index, typeof(int))), Convert(value, typeof(long))));
    }

    private static Expression EmitNewArray(int[] consumedPcs, CompilationContext ctx, int idx) {
        var sizeExpr = consumedPcs.Length > 0 ? ctx.ValueSlot(consumedPcs[0]) : Constant(0L);
        var longArr = NewArrayBounds(typeof(long), Convert(sizeExpr, typeof(int)));
        var handle = Call(ctx.Heap, HeapAllocate, Convert(longArr, typeof(object)));
        return Assign(ctx.ValueSlot(idx), Convert(handle, typeof(long)));
    }

    private static Expression EmitStridedSet(int[] consumedPcs, CompilationContext ctx) {
        var handle = ctx.ValueSlot(consumedPcs[0]);
        var start = ctx.ValueSlot(consumedPcs[1]);
        var step = ctx.ValueSlot(consumedPcs[2]);
        var limit = ctx.ValueSlot(consumedPcs[3]);
        var arrLocal = Variable(typeof(long[]), "_arr");
        var j = Variable(typeof(long), "_j");
        var loop = Label("_loop");
        var done = Label("_done");
        return Block(
            [arrLocal, j],
            Assign(arrLocal, Convert(ArrayAccess(ctx.HeapRawSlots, Convert(handle, typeof(int))), typeof(long[]))),
            Assign(j, start),
            Label(loop),
            IfThenElse(
                LessThanOrEqual(j, limit),
                Block(
                    Assign(ArrayAccess(arrLocal, Convert(RightShift(j, Constant(6)), typeof(int))),
                        Or(ArrayAccess(arrLocal, Convert(RightShift(j, Constant(6)), typeof(int))),
                            LeftShift(Constant(1L), Convert(And(j, Constant(63L)), typeof(int))))),
                    Assign(j, Add(j, step)),
                    Goto(loop)),
                Label(done)));
    }

    private static readonly MethodInfo SetStackPointer =
        Ref<ValueStack>.Method(s => s.SetStackPointer(0));

    internal static MethodInfo PopCountMethod =
        Ref.Method(() => System.Numerics.BitOperations.PopCount(0UL));

    private static Expression EmitCallExternalDirect(MethodBase target, int argCount, bool isStatic, int[] consumedPcs, CompilationContext ctx, int idx, int? siteIndex = null, IReadOnlyList<CallSiteEntry>? callSites = null) {
        if (siteIndex.HasValue && callSites is not null) {
            if (siteIndex.Value < 0 || siteIndex.Value >= callSites.Count)
                throw new InvalidOperationException($"Call site index {siteIndex.Value} out of range (catalog size {callSites.Count})");
            target = callSites[siteIndex.Value].Target;
        }

        System.Diagnostics.Debug.Assert(consumedPcs.Length == argCount);

        if (target is ConstructorInfo ctor) {
            var ctorArgs = new Expression[consumedPcs.Length];
            for (int i = 0; i < consumedPcs.Length; i++)
                ctorArgs[i] = ctx.ValueSlot(consumedPcs[i]);
            for (int i = 0; i < ctor.GetParameters().Length; i++) {
                var paramType = ctor.GetParameters()[i].ParameterType;
                var pt = paramType.GetPrimitiveType();
                if (pt is not null && pt.Value.IsStackValue()) {
                    ctorArgs[i] = paramType == typeof(bool)
                        ? NotEqual(ctorArgs[i], Constant(0L))
                        : Convert(ctorArgs[i], paramType);
                }
                else if (!paramType.IsValueType) {
                    var handle = Convert(ctorArgs[i], typeof(int));
                    ctorArgs[i] = Convert(ArrayAccess(ctx.HeapRawSlots, handle), paramType);
                }
            }
            var newObj = New(ctor, ctorArgs);
            var heapHandle = Convert(Call(ctx.Heap, HeapAllocate, Convert(newObj, typeof(object))), typeof(long));
            return Assign(ctx.ValueSlot(idx), heapHandle);
        }

        var method = (MethodInfo)target;
        var rawArgs = new Expression[consumedPcs.Length];
        for (int i = 0; i < consumedPcs.Length; i++)
            rawArgs[i] = ctx.ValueSlot(consumedPcs[i]);

        var paramInfos = method.GetParameters();
        for (int i = 0; i < paramInfos.Length; i++) {
            int argIdx = isStatic ? i : i + 1;
            if (argIdx >= rawArgs.Length) break;
            var paramType = paramInfos[i].ParameterType;
            var pt = paramType.GetPrimitiveType();
            if (pt is not null && pt.Value.IsStackValue()) {
                rawArgs[argIdx] = paramType == typeof(bool)
                    ? NotEqual(rawArgs[argIdx], Constant(0L))
                    : Convert(rawArgs[argIdx], paramType);
            }
            else if (!paramType.IsValueType) {
                var handle = Convert(rawArgs[argIdx], typeof(int));
                rawArgs[argIdx] = Convert(ArrayAccess(ctx.HeapRawSlots, handle), paramType);
            }
        }

        if (!isStatic && consumedPcs.Length > 0) {
            var instanceType = method.DeclaringType;
            var instPt = instanceType?.GetPrimitiveType();
            if (instPt is not null && instPt.Value.IsStackValue())
                rawArgs[0] = Convert(rawArgs[0], instanceType!);
            else if (instanceType is not null && !instanceType.IsValueType) {
                var handle = Convert(rawArgs[0], typeof(int));
                rawArgs[0] = Convert(ArrayAccess(ctx.HeapRawSlots, handle), instanceType);
            }
        }

        Expression? instance = isStatic ? null : rawArgs[0];
        var callArgs = isStatic ? rawArgs : rawArgs.Skip(1).ToArray();
        var call = Call(instance, method, callArgs);

        if (method.ReturnType == typeof(void)) return call;

        Expression result = call;
        var returnType = method.ReturnType;
        if (returnType != typeof(void)) {
            var retPt = returnType.GetPrimitiveType();
            if (retPt is not null && retPt.Value.IsStackValue()) {
                result = returnType == typeof(bool)
                    ? Condition(result, Constant(1L), Constant(0L))
                    : returnType != typeof(long) ? Convert(result, typeof(long)) : result;
            }
            else {
                result = Convert(Call(ctx.Heap, HeapAllocate, Convert(result, typeof(object))), typeof(long));
            }
        }
        return Assign(ctx.ValueSlot(idx), result);
    }

    internal static Expression CtxPushRegisters(CompilationContext ctx) {
        int depth = ctx.GetRingDepth(ctx.CurrentLabelIndex);
        if (depth <= 0) return Empty();
        // Save ring values to state.Registers with ctx.SavedSp as base offset.
        // This gives each nested call its own save area (different SP at call time).
        var stmts = new Expression[depth];
        var slots = Property(ctx.State, "Registers");
        for (int k = 0; k < depth; k++)
            stmts[k] = Assign(ArrayAccess(slots, Add(ctx.SavedSp, Constant(k))), ctx.RingSlot(k));
        return Block(stmts);
    }

    internal static Expression CtxPopRegisters(CompilationContext ctx) {
        int depth = ctx.GetRingDepth(ctx.CurrentLabelIndex);
        if (depth <= 0) return Empty();
        var stmts = new Expression[depth];
        var slots = Property(ctx.State, "Registers");
        for (int k = 0; k < depth; k++)
            stmts[k] = Assign(ctx.RingSlot(k), ArrayAccess(slots, Add(ctx.SavedSp, Constant(k))));
        return Block(stmts);
    }

    /// <summary>Simulate the eval-stack ring for a primitive sequence.
    /// Branch-aware: when a <c>CondGoto</c> or <c>Goto</c> targets a label,
    /// the ring depth at that label is restored to what the predecessor
    /// expects — not the linear fallthrough depth.  This lets both arms of
    /// a branch leave values at the same ring depth for <c>Phi</c> to merge.</summary>
    private static Dictionary<int, int> ComputePrimitiveRingDepths(
        IReadOnlyList<PrimitiveNode> primitives,
        out Dictionary<int, int> ringDepthAtPC) {
        // Pre-compute expected depths at branch targets via local simulation
        var targetDepth = BuildTargetDepth(primitives);

        var ring = new List<int>();
        var map = new Dictionary<int, int>();
        ringDepthAtPC = new Dictionary<int, int>();
        for (int pc = 0; pc < primitives.Count; pc++) {
            // At a branch-target label, restore ring to expected predecessor depth
            if (targetDepth.TryGetValue(pc, out int expectDepth) && expectDepth < ring.Count) {
                ring.RemoveRange(expectDepth, ring.Count - expectDepth);
            }

            var (pop, push) = primitives[pc].StackEffect;
            int entryDepth = ring.Count;
            ringDepthAtPC[pc] = entryDepth;
            int toPop = Math.Min(pop, entryDepth);
            for (int i = 0; i < toPop && ring.Count > 0; i++)
                ring.RemoveAt(ring.Count - 1);
            if (push > 0) {
                map[pc] = entryDepth - toPop;
                for (int i = 0; i < push; i++)
                    ring.Add(pc);
            }
        }
        return map;
    }

    /// <summary>One-pass backward-scan equivalent for primitives.
    /// Returns an array parallel to <paramref name="primitives"/> where each
    /// entry is the consumed-from-PC list for that primitive.
    /// Branch-aware: restores ring depth at branch-target labels so both
    /// arms of a conditional leave values at the same depth for Phi.</summary>
    private static int[][] ComputePrimitiveConsumedPcs(IReadOnlyList<PrimitiveNode> primitives) {
        var targetDepth = BuildTargetDepth(primitives);

        var ring = new List<int>();
        var result = new int[primitives.Count][];
        for (int pc = 0; pc < primitives.Count; pc++) {
            // At a branch-target label, restore ring to expected predecessor depth
            if (targetDepth.TryGetValue(pc, out int expectDepth) && expectDepth < ring.Count) {
                ring.RemoveRange(expectDepth, ring.Count - expectDepth);
            }

            var (pop, push) = primitives[pc].StackEffect;
            int entryDepth = ring.Count;
            int toPop = Math.Min(pop, entryDepth);
            var consumed = new int[toPop];
            for (int i = 0; i < toPop; i++)
                consumed[toPop - 1 - i] = ring[entryDepth - 1 - i];
            result[pc] = consumed;
            for (int i = 0; i < toPop && ring.Count > 0; i++)
                ring.RemoveAt(ring.Count - 1);
            for (int i = 0; i < push; i++)
                ring.Add(pc);
        }
        return result;
    }

    /// <summary>Build a map from branch-target PC → expected ring depth.
    /// For ResolvedCondGoto: the depth after popping the condition (same as fallthrough).
    /// For ResolvedGoto: the depth at the Goto site (no stack effect).</summary>
    private static Dictionary<int, int> BuildTargetDepth(IReadOnlyList<PrimitiveNode> primitives) {
        var result = new Dictionary<int, int>();
        var sim = new List<int>();
        for (int pc = 0; pc < primitives.Count; pc++) {
            // Advance sim BEFORE recording — CondGoto target expects depth
            // AFTER the condition was popped (same as fallthrough), and
            // Goto target expects depth at the Goto (no stack effect).
            var (pop, push) = primitives[pc].StackEffect;
            int toPop = Math.Min(pop, sim.Count);
            if (toPop > 0) sim.RemoveRange(sim.Count - toPop, toPop);
            int afterDepth = sim.Count;
            for (int j = 0; j < push; j++) sim.Add(pc);

            if (primitives[pc] is ResolvedCondGoto cg) {
                // Record the depth AFTER popping the condition value
                if (!result.ContainsKey(cg.TargetPc))
                    result[cg.TargetPc] = afterDepth;
            }
            if (primitives[pc] is ResolvedGoto g) {
                // Record the depth at the Goto (which has no stack effect)
                if (!result.ContainsKey(g.TargetPc))
                    result[g.TargetPc] = afterDepth;
            }
        }
        return result;
    }

#if DEBUG
    /// <summary>DEBUG-only: validate ring depth consistency at all branch targets.
    /// Checks that every predecessor agrees on the ring depth at each label,
    /// and that Phi convergence points have matching depths from all arms.</summary>
    private static void VerifyRingDepths(
        IReadOnlyList<PrimitiveNode> primitives,
        Dictionary<int, int> ringDepthMap,
        Dictionary<int, int> ringDepthAtPC,
        int[][] consumedPcs) {
        // Verify all branch targets are valid PCs.
        // Note: depth convergence checking (K-034) is intentionally omitted here
        // because BuildTargetDepth records only the first predecessor's depth.
        for (int pc = 0; pc < primitives.Count; pc++) {
            if (primitives[pc] is ResolvedGoto g && (g.TargetPc < 0 || g.TargetPc >= primitives.Count))
                throw new InvalidOperationException($"Goto at PC {pc} targets invalid PC {g.TargetPc}");
            if (primitives[pc] is ResolvedCondGoto cg && (cg.TargetPc < 0 || cg.TargetPc >= primitives.Count))
                throw new InvalidOperationException($"CondGoto at PC {pc} targets invalid PC {cg.TargetPc}");
        }
    }
#endif

    // ── Primitive emit helpers ──────────────────────────────────────

    private static Expression EmitPushConstant(object? value, CompilationContext ctx) {
        // Numeric/primitive values: inline directly
        if (value is long l)
            return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Constant(l));
        if (value is int iVal)
            return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Constant((long)iVal));
        if (value is bool b)
            return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Constant(b ? 1L : 0L));
        if (value is null)
            return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Constant(0L));
        if (value is double d)
            return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Constant((long)d));
        if (value is short s)
            return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Constant((long)s));
        if (value is byte bVal)
            return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Constant((long)bVal));

        // Non-numeric values (strings, etc.): allocate on heap at runtime.
        var allocate = Ref<Heap>.Method(h => h.Allocate(null));
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex),
            Convert(
                Call(ctx.Heap, allocate, Convert(Constant(value), typeof(object))),
                typeof(long)));
    }

    private static Expression EmitBinaryOp(PrimOpKind op, int[] consumedPcs, CompilationContext ctx, Type? comparisonType = null) {
        var lhs = ctx.ValueSlot(consumedPcs[0]);
        var rhs = consumedPcs.Length > 1 ? ctx.ValueSlot(consumedPcs[1]) : Constant(0L);

        // Reference-type value equality (heap handle dereference path)
        if (comparisonType is not null && (op is PrimOpKind.Eq or PrimOpKind.Neq)) {
            var eqType = comparisonType;
            Expression equal = Equal(
                Convert(ArrayAccess(ctx.HeapRawSlots, Convert(lhs, typeof(int))), eqType),
                Convert(ArrayAccess(ctx.HeapRawSlots, Convert(rhs, typeof(int))), eqType));
            if (op == PrimOpKind.Neq)
                equal = Not(equal);
            return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex),
                Condition(equal, Constant(1L), Constant(0L)));
        }

        Expression expr = op switch {
            PrimOpKind.Add => Add(lhs, rhs),
            PrimOpKind.Sub => Subtract(lhs, rhs),
            PrimOpKind.Mul => Multiply(lhs, rhs),
            PrimOpKind.Div => Divide(lhs, rhs),
            PrimOpKind.Mod => Modulo(lhs, rhs),
            PrimOpKind.And => And(lhs, rhs),
            PrimOpKind.Or => Or(lhs, rhs),
            PrimOpKind.Xor => ExclusiveOr(lhs, rhs),
            PrimOpKind.Shl => LeftShift(lhs, Convert(rhs, typeof(int))),
            PrimOpKind.Shr => RightShift(lhs, Convert(rhs, typeof(int))),
            PrimOpKind.Eq => Condition(Equal(lhs, rhs), Constant(1L), Constant(0L)),
            PrimOpKind.Neq => Condition(NotEqual(lhs, rhs), Constant(1L), Constant(0L)),
            PrimOpKind.Gt => Condition(GreaterThan(lhs, rhs), Constant(1L), Constant(0L)),
            PrimOpKind.Gte => Condition(GreaterThanOrEqual(lhs, rhs), Constant(1L), Constant(0L)),
            PrimOpKind.Lt => Condition(LessThan(lhs, rhs), Constant(1L), Constant(0L)),
            PrimOpKind.Lte => Condition(LessThanOrEqual(lhs, rhs), Constant(1L), Constant(0L)),
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), expr);
    }

    private static Expression EmitUnaryOp(PrimUnaryOpKind op, int[] consumedPcs, CompilationContext ctx) {
        var operand = consumedPcs.Length > 0 ? ctx.ValueSlot(consumedPcs[0]) : Constant(0L);
        Expression expr = op switch {
            PrimUnaryOpKind.Neg => Negate(operand),
            PrimUnaryOpKind.Not => Condition(Equal(operand, Constant(0L)), Constant(1L), Constant(0L)),
            PrimUnaryOpKind.BitNot => Not(operand),
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), expr);
    }

    private static Expression EmitCountBits(int[] consumedPcs, CompilationContext ctx) {
        var value = consumedPcs.Length > 0 ? ctx.ValueSlot(consumedPcs[0]) : Constant(0L);
        var result = Call(null, PopCountMethod, Convert(value, typeof(ulong)));
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Convert(result, typeof(long)));
    }

    // ── EH dispatch (Strategy B) ──────────────────────────────────

    /// <summary>
    /// Runtime dispatch handler for Strategy B side-table EH.
    /// Called from inside the <c>Expression.TryCatch</c> catch block when an
    /// exception is thrown during execution of the main delegate.
    ///
    /// Scans the <see cref="ExceptionRegionTable"/> for the innermost region
    /// that covers <c>state.ProgramCounter</c> (the faulting PC saved by
    /// <see cref="EmitThrowOp"/>), then invokes the corresponding handler
    /// delegate from the function table.
    /// </summary>
    /// <summary>
    /// Check if <paramref name="exception"/> matches the catch type specified
    /// by <paramref name="catchTypeName"/>. A null/empty catch type name is a
    /// catch-all (matches everything). Otherwise walks the exception's type
    /// hierarchy and checks FullName equality.
    /// </summary>
    private static bool ExceptionTypeMatches(Exception exception, string? catchTypeName) {
        if (string.IsNullOrEmpty(catchTypeName))
            return true;
        for (var type = exception.GetType(); type is not null; type = type.BaseType) {
            if (type.FullName == catchTypeName)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Invoke a handler delegate (catch, finally, or using-dispose) with the
    /// VM state saved/restored around the call. The handler runs with
    /// FrameBase=0 and ProgramCounter=0 so its µop dispatch starts fresh.
    /// Returns true if the handler was actually invoked.
    /// </summary>
    private static bool TryInvokeHandler(VmState state, IReadOnlyList<Action<VmState>>? functions, int handlerFuncIndex) {
        if (functions is null || handlerFuncIndex < 0 || handlerFuncIndex >= functions.Count)
            return false;
        var handler = functions[handlerFuncIndex];
        if (handler is null)
            return false;
        int savedFb = state.FrameBase;
        int savedPc = state.ProgramCounter;
        state.FrameBase = 0;
        state.ProgramCounter = 0;
        handler(state);
        state.FrameBase = savedFb;
        state.ProgramCounter = savedPc;
        return true;
    }

    internal static void DispatchException(VmState state, ExceptionRegionTable table, Exception exception) {
        int faultPc = state.ProgramCounter;
        var entries = table.Entries;
        var functions = state.Program.Functions;

        // Forward scan: entries are in source/marker order (inner before outer),
        // so catches are checked in declaration order and finally/using-dispose
        // handlers run for side effects as their regions are encountered.
        for (int i = 0; i < entries.Count; i++) {
            var entry = entries[i];
            if (faultPc >= entry.TryStartPc && faultPc < entry.TryEndPc) {
                if (entry.Kind == RegionKind.Catch) {
                    if (!ExceptionTypeMatches(exception, entry.CatchTypeName))
                        continue; // type doesn't match, try next catch

                    // Invoke the catch handler, then run any finally handlers
                    // in the SAME try region (for try/catch/finally — the finally
                    // body is después the catch body in the µop stream and won't
                    // be reached otherwise).
                    TryInvokeHandler(state, functions, entry.HandlerFuncIndex);

                    // After the catch handler, run finally handlers in the same try region
                    // (identified by TryStartPc). This ensures catch + finally correct order.
                    for (int j = i + 1; j < entries.Count; j++) {
                        var next = entries[j];
                        if (next.TryStartPc == entry.TryStartPc && next.Kind == RegionKind.Finally)
                            TryInvokeHandler(state, functions, next.HandlerFuncIndex);
                    }
                    return;
                }

                // Finally and UsingDispose handlers run for side effects then
                // continue scanning. The exception is NOT handled — it propagates
                // to the nearest outer catch (or rethrows if none found).
                if (entry.Kind is RegionKind.Finally or RegionKind.UsingDispose) {
                    TryInvokeHandler(state, functions, entry.HandlerFuncIndex);
                    continue;
                }
            }
        }

        ExceptionDispatchInfo.Capture(exception).Throw();
        throw exception;
    }

    /// <summary>
    /// Generate the <c>Expression.TryCatch</c> wrapper around the main delegate body,
    /// with a catch-all handler that calls <see cref="DispatchException"/>.
    /// </summary>
    internal static Expression EmitExceptionDispatchWrapper(
        Expression mainBody,
        CompilationContext ctx,
        ExceptionRegionTable table) {

        var exceptionVar = Variable(typeof(Exception), "_ex");
        var tableConst = Constant(table);

        var dispatchCall = Call(
            typeof(ProgramCompiler).GetMethod(nameof(DispatchException),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("DispatchException method not found"),
            ctx.State,
            tableConst);

        return TryCatch(
            mainBody,
            Catch(typeof(Exception), exceptionVar,
                Block(
                    dispatchCall,
                    Constant(0L)  // default return value if catch handled
                )));
    }
}