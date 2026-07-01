using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Vm.Instructions;
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

            var pc = consumedPcs[idx];
            Expression Resolve(int i) => ctx.ValueSlot(pc[i]);

            Expression? result = prim switch {
                ResolvedGoto rg => new Jump(rg.TargetPc).ToExpression(ctx),
                ResolvedCondGoto rcg => new BranchIfFalse(rcg.TargetPc) { ConsumedFromPcs = pc.Length > 0 ? [pc[0]] : [] }.ToExpression(ctx),
                PrimReturn => EmitReturnOp(pc, ctx),
                PushConstant pv => EmitPushConstant(pv.Value, ctx),
                LoadLocal ll => Assign(ctx.ValueSlot(idx), ArrayAccess(ctx.RawSlots, Add(ctx.FrameBase, Constant(ll.SlotIndex)))),
                StoreLocal sl => EmitStoreLocal(sl.SlotIndex, pc, ctx),
                BinaryOp bo => EmitBinaryOp(bo.Op, pc, ctx, bo.ComparisonType),
                PrimUnaryOp uo => EmitUnaryOp(uo.Op, pc, ctx),
                PrimLabel => null,
                Discard => null,
                Dup => Assign(ctx.ValueSlot(idx), Resolve(0)),
                PrimCountBits => EmitCountBits(pc, ctx),
                PrimParameter p => Assign(ctx.ValueSlot(idx), ArrayAccess(ctx.RawSlots, Add(ctx.FrameBase, Constant(p.SlotIndex)))),
                PrimArrayLoad => new Instructions.ArrayLoad { ConsumedFromPcs = pc }.ToExpression(ctx),
                PrimArrayStore => new Instructions.ArrayStore { ConsumedFromPcs = pc }.ToExpression(ctx),
                PrimNewArray => new NewArrayOp { ConsumedFromPcs = pc }.ToExpression(ctx),
                PrimStridedSet => new StridedSetOp { ConsumedFromPcs = pc }.ToExpression(ctx),
                PrimThrow => new Instructions.Throw { ConsumedFromPcs = pc }.ToExpression(ctx),
                PrimCall c => EmitPrimitiveCall(c, pc, ctx, idx),
                PrimExternalCall ec => new Instructions.CallExternalDirect(ec.Target, ec.ArgCount, ec.IsStatic) { ConsumedFromPcs = pc }.ToExpression(ctx),
                PrimHeapConst lhc => new Instructions.LoadHeapConst(0L, lhc.Handle) { ConsumedFromPcs = pc }.ToExpression(ctx),
                PrimAllocClosure ac => new Instructions.AllocClosure(ac.LambdaIndex, ac.UpvalueCount) { ConsumedFromPcs = pc }.ToExpression(ctx),
                PrimLoadUpvalue lu => new Instructions.LoadCapture(lu.UpvalueIndex) { ConsumedFromPcs = pc }.ToExpression(ctx),
                PrimStoreUpvalue su => new Instructions.StoreCapture(su.UpvalueIndex) { ConsumedFromPcs = pc }.ToExpression(ctx),
                _ => throw new NotSupportedException($"Primitive not supported: {prim.GetType().Name}")
            };

            if (result is not null)
                body.Add(result);
        }

        body.Add(Label(ctx.ExitLabel));

        var delegateExpr = Lambda<Action<VmState>>(Block(ctx.Locals, body), ctx.State);
        var del = delegateExpr.Compile();
        return new VmProgram(del, new List<Instruction>(), new Dictionary<NodeId, SourceRange>(), [], null, null, 32);
    }

    private static Expression EmitPrimitiveCall(PrimCall call, int[] consumedPcs, CompilationContext ctx, int pc) {
        var state = ctx.State;
        var slots = ctx.RawSlots;
        var sp = Property(Property(state, "Stack"), "StackPointer");
        var regs = ctx.Registers;

        var body = new List<Expression>();
        for (int i = 0; i < consumedPcs.Length; i++) {
            var arg = ctx.ValueSlot(consumedPcs[i]);
            body.Add(Assign(ArrayAccess(slots, sp), arg));
            body.Add(Call(Property(state, "Stack"), "SetStackPointer", null, Add(sp, Constant(1))));
        }
        body.Add(Instructions.Call.CtxPushRegisters(ctx));
        body.Add(Assign(ctx.StateProgramCounter, Constant(pc)));
        body.Add(Call(
            Ref<VmState>.Method(s => Vm.HandleCall(s, default, default)),
            state, Constant(0), Constant(call.ArgCount + 1)));
        body.Add(Assign(ctx.FrameBaseLocal, Property(ctx.State, "FrameBase")));
        body.Add(Assign(ctx.ProgramCounter, ctx.StateProgramCounter));
        body.Add(Instructions.Call.CtxPopRegisters(ctx));
        var rv = ctx.ValueSlot(pc);
        body.Add(Assign(rv, ArrayAccess(slots,
            Subtract(Property(Property(state, "Stack"), "StackPointer"), Constant(1)))));
        body.Add(Goto(ctx.EntryLabel));
        return Block(body);
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
            Call(Property(ctx.State, "Stack"), "SetStackPointer", null, Add(targetSlot, Constant(1))),
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
        var result = Call(null, Instructions.CountBits.PopCountMethod, Convert(value, typeof(ulong)));
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Convert(result, typeof(long)));
    }


}