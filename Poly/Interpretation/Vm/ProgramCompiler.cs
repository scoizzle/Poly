using System.Linq.Expressions;
using System.Reflection;

using Poly.Syntax.Primitives;

using static System.Linq.Expressions.Expression;

using PrimAllocClosure = Poly.Syntax.Primitives.AllocClosure;
using PrimArrayLoad = Poly.Syntax.Primitives.ArrayLoad;
using PrimArrayStore = Poly.Syntax.Primitives.ArrayStore;
using PrimCall = Poly.Syntax.Primitives.Call;
using PrimCountBits = Poly.Syntax.Primitives.CountBits;
using PrimExternalCall = Poly.Syntax.Primitives.CallExternal;
using PrimHeapConst = Poly.Syntax.Primitives.LoadHeapConstant;
using PrimLabel = Poly.Syntax.Primitives.Label;
using PrimLoadUpvalue = Poly.Syntax.Primitives.LoadUpvalue;
using PrimNewArray = Poly.Syntax.Primitives.NewArray;
using PrimOpKind = Poly.Syntax.Primitives.OpKind;
using PrimParameter = Poly.Syntax.Primitives.Parameter;
using PrimReturn = Poly.Syntax.Primitives.Return;
using PrimStoreUpvalue = Poly.Syntax.Primitives.StoreUpvalue;
using PrimStridedSet = Poly.Syntax.Primitives.StridedSet;
using PrimThrow = Poly.Syntax.Primitives.Throw;
using PrimUnaryOp = Poly.Syntax.Primitives.UnaryOp;
using PrimUnaryOpKind = Poly.Syntax.Primitives.UnaryOpKind;

namespace Poly.Interpretation.Vm;

public enum CompilationMode { NoDebug, Debug, Normal, Profiling, TraceExpressions }

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
        CompilationMode mode = CompilationMode.Normal) {
        var ctx = new CompilationContext();
        var body = new List<Expression>();
        int n = primitives.Count;

        ctx.LimitLoops = mode is CompilationMode.Normal or CompilationMode.Profiling;

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

        if (mode == CompilationMode.Profiling)
            body.Add(Assign(ctx.InstructionCounters, NewArrayBounds(typeof(long), Constant(n))));

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

        if (n > 0) {
            var swCases = new System.Linq.Expressions.SwitchCase[n];
            for (int i = 0; i < n; i++)
                swCases[i] = SwitchCase(Goto(ctx.GetLabel(i)), Constant(i));
            body.Add(IfThen(
                GreaterThanOrEqual(ctx.ProgramCounter, Constant(0)),
                Switch(ctx.ProgramCounter, Goto(ctx.ExitLabel), swCases)));
        }

        // Emit each primitive
        for (int idx = 0; idx < n; idx++) {
            var prim = primitives[idx];
            ctx.CurrentLabelIndex = idx;

            body.Add(Label(ctx.GetLabel(idx)));

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
                PrimThrow => null,
                PrimCall c => EmitPrimitiveCall(c, pcs, ctx, idx),
                PrimExternalCall ec => EmitCallExternalDirect(ec.Target, ec.ArgCount, ec.IsStatic, pcs, ctx, idx),
                PrimHeapConst lhc => EmitLoadHeapConstant(lhc.Handle, ctx, idx),
                PrimAllocClosure ac => EmitAllocClosure(ac.LambdaIndex, ac.UpvalueCount, pcs, ctx, idx),
                PrimLoadUpvalue lu => EmitLoadCapture(lu.UpvalueIndex, ctx, idx),
                PrimStoreUpvalue su => EmitStoreCapture(su.UpvalueIndex, pcs, ctx),
                _ => throw new NotSupportedException($"Primitive not supported: {prim.GetType().Name}")
            };

            if (result is not null)
                body.Add(result);
        }

        body.Add(Label(ctx.ExitLabel));

        var delegateExpr = Lambda<Action<VmState>>(Block(ctx.Locals, body), ctx.State);
        var del = delegateExpr.Compile();
        return new VmProgram(del, new Dictionary<NodeId, SourceRange>(), [], null, null, 32);
    }

    private static readonly MethodInfo HandleCallMethod = Ref.Method(() => Vm.HandleCall(null!, 0, 0));

    private static Expression EmitPrimitiveCall(PrimCall call, int[] consumedPcs, CompilationContext ctx, int pc) {
        var state = ctx.State;
        var slots = ctx.RawSlots;
        var sp = Property(Property(state, "Stack"), "StackPointer");

        var body = new List<Expression>();
        for (int i = 0; i < consumedPcs.Length; i++) {
            var arg = ctx.ValueSlot(consumedPcs[i]);
            body.Add(Assign(ArrayAccess(slots, sp), arg));
            body.Add(Call(Property(state, "Stack"), SetStackPointer, Add(sp, Constant(1))));
        }
        body.Add(CtxPushRegisters(ctx));
        // HandleCall now returns the function's entry PC directly.
        // Set _pc to the target and jump to EntryLabel; the dispatch
        // switch routes to the correct body label.
        body.Add(Assign(ctx.ProgramCounter,
            Call(HandleCallMethod, state, Constant(0), Constant(call.ArgCount + 1))));
        body.Add(Assign(ctx.FrameBaseLocal, Property(ctx.State, "FrameBase")));
        body.Add(CtxPopRegisters(ctx));
        var rv = ctx.ValueSlot(pc);
        body.Add(Assign(rv, ArrayAccess(slots,
            Subtract(Property(Property(state, "Stack"), "StackPointer"), Constant(1)))));
        body.Add(Goto(ctx.EntryLabel));
        return Block(body);
    }

    // ── Inline emit helpers (replacing old Instruction.ToExpression) ─────

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

    private static Expression EmitLoadHeapConstant(int handle, CompilationContext ctx, int idx) {
        // Heap constants are allocated at runtime — the handle indexes into
        // the heap constant pool which is populated during compilation.
        return Assign(ctx.ValueSlot(idx),
            Convert(Call(ctx.Heap, HeapAllocate, Constant(handle)), typeof(long)));
    }

    private static readonly MethodInfo HandleAllocClosureMethod =
        Ref<VmState>.Method(s => Vm.HandleAllocClosure(s, default, default));

    private static Expression EmitAllocClosure(int funcIndex, int captureCount, int[] consumedPcs, CompilationContext ctx, int idx) {
        var state = ctx.State;
        var slots = ctx.RawSlots;
        var sp = Property(Property(state, "Stack"), "StackPointer");
        var body = new List<Expression>();
        for (int i = 0; i < consumedPcs.Length; i++) {
            var cap = ctx.ValueSlot(consumedPcs[i]);
            body.Add(Assign(ArrayAccess(slots, sp), cap));
            body.Add(Call(Property(state, "Stack"), SetStackPointer, Add(sp, Constant(1))));
        }
        body.Add(CtxPushRegisters(ctx));
        body.Add(Assign(ctx.StateProgramCounter, Constant(idx)));
        body.Add(Call(HandleAllocClosureMethod, state, Constant(funcIndex), Constant(captureCount)));
        body.Add(Assign(ctx.ProgramCounter, ctx.StateProgramCounter));
        body.Add(CtxPopRegisters(ctx));
        var rv = ctx.ValueSlot(idx);
        body.Add(Assign(rv, ArrayAccess(slots, Subtract(sp, Constant(1)))));
        return Block(body);
    }

    internal static MethodInfo HandleLoadUpvalueMethod =
        Ref.Method(() => Vm.HandleLoadUpvalue(null!, 0));

    private static readonly MethodInfo SetStackPointer =
        Ref<ValueStack>.Method(s => s.SetStackPointer(0));

    private static Expression EmitLoadCapture(int upvalueIndex, CompilationContext ctx, int idx) {
        return Assign(ctx.ValueSlot(idx),
            Call(HandleLoadUpvalueMethod, ctx.State, Constant(upvalueIndex)));
    }

    internal static MethodInfo HandleStoreUpvalueMethod =
        Ref.Method(() => Vm.HandleStoreUpvalue(null!, 0, 0));

    private static Expression EmitStoreCapture(int upvalueIndex, int[] consumedPcs, CompilationContext ctx) {
        var value = consumedPcs.Length > 0 ? ctx.ValueSlot(consumedPcs[0]) : Constant(0L);
        return Call(HandleStoreUpvalueMethod, ctx.State, Constant(upvalueIndex), value);
    }

    internal static MethodInfo PopCountMethod =
        Ref.Method(() => System.Numerics.BitOperations.PopCount(0UL));

    private static Expression EmitCallExternalDirect(MethodInfo target, int argCount, bool isStatic, int[] consumedPcs, CompilationContext ctx, int idx) {
        var rawArgs = new Expression[consumedPcs.Length];
        for (int i = 0; i < consumedPcs.Length; i++)
            rawArgs[i] = ctx.ValueSlot(consumedPcs[i]);

        var paramInfos = target.GetParameters();
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
            var instanceType = target.DeclaringType;
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
        var call = Call(instance, target, callArgs);

        if (target.ReturnType == typeof(void)) return call;

        Expression result = call;
        var returnType = target.ReturnType;
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
        var stmts = new Expression[depth];
        for (int k = 0; k < depth; k++)
            stmts[k] = Assign(ArrayAccess(ctx.Registers, Constant(k)), ctx.RingSlot(k));
        return Block(stmts);
    }

    internal static Expression CtxPopRegisters(CompilationContext ctx) {
        int depth = ctx.GetRingDepth(ctx.CurrentLabelIndex);
        if (depth <= 0) return Empty();
        var stmts = new Expression[depth];
        for (int k = 0; k < depth; k++)
            stmts[k] = Assign(ctx.RingSlot(k), ArrayAccess(ctx.Registers, Constant(k)));
        return Block(stmts);
    }

    /// <summary>Simulate the eval-stack ring for a primitive sequence.</summary>
    private static Dictionary<int, int> ComputePrimitiveRingDepths(
        IReadOnlyList<PrimitiveNode> primitives,
        out Dictionary<int, int> ringDepthAtPC) {
        var ring = new List<int>();
        var map = new Dictionary<int, int>();
        ringDepthAtPC = new Dictionary<int, int>();
        for (int pc = 0; pc < primitives.Count; pc++) {
            var (pop, push) = primitives[pc].StackEffect;
            int entryDepth = ring.Count;
            ringDepthAtPC[pc] = entryDepth;
            int toPop = Math.Min(pop, entryDepth);
            for (int i = 0; i < toPop && ring.Count > 0; i++)
                ring.RemoveAt(ring.Count - 1);
            if (push > 0) {
                map[pc] = entryDepth - toPop;
                ring.Add(pc);
            }
        }
        return map;
    }

    /// <summary>One-pass backward-scan equivalent for primitives.
    /// Returns an array parallel to <paramref name="primitives"/> where each
    /// entry is the consumed-from-PC list for that primitive.
    /// Identical algorithm to <see cref="BackwardScan"/> but for
    /// <see cref="PrimitiveNode.StackEffect"/> instead of Instruction PopCount/PushCount.</summary>
    private static int[][] ComputePrimitiveConsumedPcs(IReadOnlyList<PrimitiveNode> primitives) {
        var ring = new List<int>();
        var result = new int[primitives.Count][];
        for (int pc = 0; pc < primitives.Count; pc++) {
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

    // ── Primitive emit helpers ──────────────────────────────────────

    private static Expression EmitReturnOp(int[] consumedPcs, CompilationContext ctx) {
        var returnVal = consumedPcs.Length > 0 ? ctx.ValueSlot(consumedPcs[0]) : Constant(0L);
        var slots = ctx.RawSlots;
        var targetSlot = Condition(Equal(ctx.FrameBase, Constant(-1)), Constant(0), ctx.FrameBase);
        return Block(
            Assign(ArrayAccess(slots, targetSlot), returnVal),
            Call(Property(ctx.State, "Stack"), SetStackPointer, Add(targetSlot, Constant(1))),
            Goto(ctx.ExitLabel));
    }

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

    private static Expression EmitStoreLocal(int slotIndex, int[] consumedPcs, CompilationContext ctx) {
        var value = ctx.ValueSlot(consumedPcs[0]);
        var index = Add(ctx.FrameBase, Constant(slotIndex));
        return Block(
            Assign(ArrayAccess(ctx.RawSlots, index), value),
            Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), value));
    }

    private static Expression EmitBinaryOp(PrimOpKind op, int[] consumedPcs, CompilationContext ctx, System.Type? comparisonType = null) {
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


}