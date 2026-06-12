using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;

using Poly.Syntax.Analysis;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>Context passed to <see cref="MicroOp.ToExpression"/> during
/// program compilation.  Carries <c>ParameterExpression</c>s and helpers
/// shared across all µops in a program.</summary>
internal sealed record CompilationContext(
    ParameterExpression State,          // VmState
    Expression Slots,          // state.Stack.RawSlots
    ParameterExpression SP,             // stack pointer
    ParameterExpression PC,             // program counter
    Expression FB,             // state.FrameBase
    Expression CAS,            // state.CachedArgSlots
    Expression CodeLen         // prog.Code.Length
) {
    public List<ParameterExpression> Variables { get; } = [];

    public Expression SlotAt(Expression index) => Expression.ArrayAccess(Slots, index);
    public Expression SlotAt(int index) => Expression.ArrayAccess(Slots, Expression.Constant(index));

    public Expression Push(Expression value) =>
        Expression.Assign(SlotAt(Expression.PostIncrementAssign(SP)), value);
    public Expression Pop() =>
        SlotAt(Expression.PreDecrementAssign(SP));
    public Expression Top() =>
        SlotAt(Expression.Subtract(SP, Expression.Constant(1)));

    public Expression BinaryArith(Func<Expression, Expression, Expression> op) {
        var r = Expression.Variable(typeof(long), "r");
        Variables.Add(r);
        return Expression.Block(
            Expression.Assign(r, Pop()),
            Expression.Assign(Top(), op(Top(), r)));
    }
    public Expression BinaryCmp(Func<Expression, Expression, Expression> cmp) {
        var r = Expression.Variable(typeof(long), "r");
        var l = Expression.Variable(typeof(long), "l");
        Variables.Add(r); Variables.Add(l);
        return Expression.Block(
            Expression.Assign(r, Pop()),
            Expression.Assign(l, Pop()),
            Push(Expression.Condition(cmp(l, r), Expression.Constant(1L), Expression.Constant(0L))));
    }

    const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    /// <summary>Inline expression to avoid reflection-based MethodInfo lookup.</summary>
    static readonly Expression<Action<ValueStack, int>> SetSPExpr = static (s, v) => s.SetSP(v);

    public Expression ResyncPC() => Expression.Assign(PC, Expression.Property(State, "PC"));
    public Expression ResyncSP() => Expression.Assign(SP, Expression.Property(Expression.Property(State, "Stack"), "SP"));

    /// <summary>Write local <c>sp</c> back to <c>state.Stack.SetSP(sp)</c>
    /// so that handler methods reading <c>state.Stack.SP</c> see the
    /// current value.</summary>
    public Expression WritebackSP() => Expression.Invoke(
        SetSPExpr, Expression.Property(State, "Stack"), SP);
    /// <summary>Write local <c>pc</c> back to <c>state.PC</c>
    /// so that handler methods reading <c>state.PC</c> see the
    /// current value.</summary>
    public Expression WritebackPC() => Expression.Assign(
        Expression.Property(State, "PC"), PC);
}

/// <summary>Base record for all micro-operations.  Each µop defines its
/// contribution to the compiled expression tree via <see cref="ToExpression"/>.
/// Only the compiled delegate executes — there is no interpretive path.</summary>
internal abstract record MicroOp(NodeId? Source) {
    public abstract Expression ToExpression(CompilationContext ctx);
}

// ═══════════════════════════════════════════════════════════════════
//  Stack manipulations
// ═══════════════════════════════════════════════════════════════════

internal sealed record PushOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"push {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        ctx.Push(Expression.Constant(Value));
}

internal sealed record PopOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "pop";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.PreDecrementAssign(ctx.SP);
}

internal sealed record DupOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "dup";
    public override Expression ToExpression(CompilationContext ctx) {
        var v = Expression.Variable(typeof(long), "v");
        ctx.Variables.Add(v);
        return Expression.Block(
            Expression.Assign(v, ctx.Top()),
            ctx.Push(v));
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Nullary arithmetic (pop right, op on left, push result)
// ═══════════════════════════════════════════════════════════════════
//  All use local variables for operands so the stack pointer is
//  stable during evaluation.

internal sealed record AddOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "add";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryArith(Expression.Add);
}
internal sealed record SubOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "sub";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryArith(Expression.Subtract);
}
internal sealed record MulOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "mul";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryArith(Expression.Multiply);
}
internal sealed record DivOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "div";
    public override Expression ToExpression(CompilationContext ctx) {
        var r = Expression.Variable(typeof(long), "r");
        ctx.Variables.Add(r);
        return Expression.Block(
            Expression.Assign(r, ctx.Pop()),
            Expression.IfThen(Expression.Equal(r, Expression.Constant(0L)),
                Expression.Throw(Expression.Constant(new DivideByZeroException()))),
            Expression.Assign(ctx.Top(), Expression.Divide(ctx.Top(), r)));
    }
}

internal sealed record EqOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "eq";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryCmp(Expression.Equal);
}
internal sealed record NeOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "ne";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryCmp(Expression.NotEqual);
}
internal sealed record LtOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "lt";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryCmp(Expression.LessThan);
}
internal sealed record LeOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "le";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryCmp(Expression.LessThanOrEqual);
}
internal sealed record GtOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "gt";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryCmp(Expression.GreaterThan);
}
internal sealed record GeOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "ge";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryCmp(Expression.GreaterThanOrEqual);
}

// ═══════════════════════════════════════════════════════════════════
//  Unary arithmetic
// ═══════════════════════════════════════════════════════════════════

internal sealed record NegOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "neg";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.Negate(ctx.Top()));
}
internal sealed record NotOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "not";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.Equal(ctx.Top(), Expression.Constant(0L)),
                Expression.Constant(1L), Expression.Constant(0L)));
}

// ═══════════════════════════════════════════════════════════════════
//  Bitwise
// ═══════════════════════════════════════════════════════════════════

internal sealed record BitNotOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "bitnot";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.OnesComplement(ctx.Top()));
}
internal sealed record BitAndOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "bitand";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryArith(Expression.And);
}
internal sealed record BitOrOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "bitor";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryArith(Expression.Or);
}
internal sealed record BitXorOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "bitxor";
    public override Expression ToExpression(CompilationContext ctx) => ctx.BinaryArith(Expression.ExclusiveOr);
}
internal sealed record ShlOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "shl";
    public override Expression ToExpression(CompilationContext ctx) {
        var r = Expression.Variable(typeof(int), "r");
        ctx.Variables.Add(r);
        return Expression.Block(
            Expression.Assign(r, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.Assign(ctx.Top(), Expression.LeftShift(ctx.Top(), r)));
    }
}
internal sealed record ShrOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "shr";
    public override Expression ToExpression(CompilationContext ctx) {
        var r = Expression.Variable(typeof(int), "r");
        ctx.Variables.Add(r);
        return Expression.Block(
            Expression.Assign(r, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.Assign(ctx.Top(), Expression.RightShift(ctx.Top(), r)));
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Fused push + op (operand-bearing forms)
// ═══════════════════════════════════════════════════════════════════

internal sealed record AddImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"addi {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.Add(ctx.Top(), Expression.Constant(Value)));
}
internal sealed record SubImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"subi {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.Subtract(ctx.Top(), Expression.Constant(Value)));
}
internal sealed record MulImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"muli {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.Multiply(ctx.Top(), Expression.Constant(Value)));
}
internal sealed record EqImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"eqi {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.Equal(ctx.Top(), Expression.Constant(Value)),
                Expression.Constant(1L), Expression.Constant(0L)));
}
internal sealed record LtImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"lti {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.LessThan(ctx.Top(), Expression.Constant(Value)),
                Expression.Constant(1L), Expression.Constant(0L)));
}
internal sealed record LeImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"lei {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.LessThanOrEqual(ctx.Top(), Expression.Constant(Value)),
                Expression.Constant(1L), Expression.Constant(0L)));
}
internal sealed record NegImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"negi {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.Negate(Expression.Constant(Value)));
}
internal sealed record NotImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"noti {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.Equal(Expression.Constant(Value), Expression.Constant(0L)),
                Expression.Constant(1L), Expression.Constant(0L)));
}

// ═══════════════════════════════════════════════════════════════════
//  Local / argument access
// ═══════════════════════════════════════════════════════════════════

internal sealed record LoadLocalOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"loadlocal {Index}";
    public override Expression ToExpression(CompilationContext ctx) =>
        ctx.Push(Expression.ArrayAccess(ctx.Slots,
            Expression.Add(Expression.Add(ctx.FB, ctx.CAS), Expression.Constant(1 + Index))));
}
internal sealed record StoreLocalOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"storelocal {Index}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(Expression.ArrayAccess(ctx.Slots,
            Expression.Add(Expression.Add(ctx.FB, ctx.CAS), Expression.Constant(1 + Index))),
            ctx.Pop());
}
internal sealed record IncLocalOp(int Index, long Increment, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"inclocal {Index} +{Increment}";
    public override Expression ToExpression(CompilationContext ctx) {
        var off = Expression.Add(Expression.Add(ctx.FB, ctx.CAS), Expression.Constant(1 + Index));
        return Expression.Block(
            Expression.AddAssign(Expression.ArrayAccess(ctx.Slots, off), Expression.Constant(Increment)),
            ctx.Push(Expression.ArrayAccess(ctx.Slots, off)));
    }
}
internal sealed record LoadArgOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"loadarg {Index}";
    public override Expression ToExpression(CompilationContext ctx) =>
        ctx.Push(Expression.ArrayAccess(ctx.Slots, Expression.Add(ctx.FB, Expression.Constant(Index))));
}
internal sealed record StoreArgOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"storearg {Index}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(Expression.ArrayAccess(ctx.Slots, Expression.Add(ctx.FB, Expression.Constant(Index))),
            ctx.Pop());
}

// ═══════════════════════════════════════════════════════════════════
//  Control flow
// ═══════════════════════════════════════════════════════════════════

internal sealed record JumpOp(int Target, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"jump {Target}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.PC, Expression.Constant(Target));
}
internal sealed record JumpIfFalseOp(int Target, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"jmpf {Target}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.IfThenElse(
            Expression.Equal(ctx.Pop(), Expression.Constant(0L)),
            Expression.Assign(ctx.PC, Expression.Constant(Target)),
            Expression.AddAssign(ctx.PC, Expression.Constant(1)));
}
internal sealed record ReturnOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "return";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.IfThen(Expression.LessThan(ctx.FB, Expression.Constant(0)),
            Expression.Assign(ctx.PC, ctx.CodeLen));
}
internal sealed record ReturnFromCallOp(int ArgSlots, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"ret_call {ArgSlots}";
    static readonly PropertyInfo FBprop = typeof(VmState).GetProperty(nameof(VmState.FrameBase))!;
    public override Expression ToExpression(CompilationContext ctx) {
        var packed = Expression.Variable(typeof(long), "packed");
        var result = Expression.Variable(typeof(long), "result");
        ctx.Variables.Add(packed); ctx.Variables.Add(result);
        return Expression.Block(
            Expression.Assign(result, ctx.Pop()),
            Expression.Assign(packed, Expression.ArrayAccess(ctx.Slots,
                Expression.Add(ctx.FB, Expression.Constant(ArgSlots)))),
            // Write result at the callee's frame base, then set SP before restoring FB
            Expression.Assign(Expression.ArrayAccess(ctx.Slots,
                Expression.Add(ctx.FB, Expression.Constant(0))), result),
            Expression.Assign(ctx.SP, Expression.Add(ctx.FB, Expression.Constant(1))),
            Expression.Assign(ctx.PC, Expression.Convert(Expression.RightShift(packed, Expression.Constant(32)), typeof(int))),
            Expression.Assign(Expression.Property(ctx.State, FBprop), Expression.Convert(packed, typeof(int))));
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Fused Imm (remaining operand-bearing forms)
// ═══════════════════════════════════════════════════════════════════

internal sealed record GtImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"gti {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.GreaterThan(ctx.Top(), Expression.Constant(Value)),
                Expression.Constant(1L), Expression.Constant(0L)));
}
internal sealed record GeImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"gei {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.GreaterThanOrEqual(ctx.Top(), Expression.Constant(Value)),
                Expression.Constant(1L), Expression.Constant(0L)));
}
internal sealed record NeImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"nei {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.NotEqual(ctx.Top(), Expression.Constant(Value)),
                Expression.Constant(1L), Expression.Constant(0L)));
}
internal sealed record DivImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"divi {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Block(
            Expression.IfThen(Expression.Equal(Expression.Constant(Value), Expression.Constant(0L)),
                Expression.Throw(Expression.Constant(new DivideByZeroException()))),
            Expression.Assign(ctx.Top(), Expression.Divide(ctx.Top(), Expression.Constant(Value))));
}

internal sealed record BitNotImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"bitnoti {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.OnesComplement(Expression.Constant(Value)));
}
internal sealed record BitAndImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"bitandi {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.And(ctx.Top(), Expression.Constant(Value)));
}
internal sealed record BitOrImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"bitori {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.Or(ctx.Top(), Expression.Constant(Value)));
}
internal sealed record BitXorImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"bitxori {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.ExclusiveOr(ctx.Top(), Expression.Constant(Value)));
}
internal sealed record ShlImmOp(int Shift, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"shli {Shift}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.LeftShift(ctx.Top(), Expression.Constant(Shift)));
}
internal sealed record ShrImmOp(int Shift, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"shri {Shift}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.RightShift(ctx.Top(), Expression.Constant(Shift)));
}

// ═══════════════════════════════════════════════════════════════════
//  DivRem (pop two, push remainder then quotient)
// ═══════════════════════════════════════════════════════════════════

internal sealed record DivRemOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "divrem";
    public override Expression ToExpression(CompilationContext ctx) {
        var r = Expression.Variable(typeof(long), "r");
        var q = Expression.Variable(typeof(long), "q");
        ctx.Variables.Add(r); ctx.Variables.Add(q);
        return Expression.Block(
            Expression.Assign(r, ctx.Pop()),
            Expression.Assign(q, ctx.Pop()),
            Expression.IfThen(Expression.Equal(r, Expression.Constant(0L)),
                Expression.Throw(Expression.Constant(new DivideByZeroException()))),
            ctx.Push(Expression.Divide(q, r)),   // quotient
            ctx.Push(Expression.Modulo(q, r)));   // remainder
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Heap operations (LoadValue / StoreValue)
// ═══════════════════════════════════════════════════════════════════

internal sealed record LoadValueOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "loadval";
    public override Expression ToExpression(CompilationContext ctx) {
        var handle = Expression.Variable(typeof(int), "h");
        var val = Expression.Variable(typeof(long), "v");
        ctx.Variables.Add(handle); ctx.Variables.Add(val);
        var heapGet = typeof(Heap).GetMethod("Get", [typeof(int)])!;
        return Expression.Block(
            Expression.Assign(handle, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.IfThenElse(
                Expression.GreaterThanOrEqual(Expression.Constant(handle), Expression.Constant(0)),
                Expression.Assign(val, Expression.Convert(
                    Expression.Call(Expression.Property(ctx.State, "Heap"), heapGet, handle), typeof(long))),
                Expression.Assign(val, Expression.ArrayAccess(ctx.Slots, Expression.Negate(handle)))),
            ctx.Push(val));
    }
}
internal sealed record StoreValueOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "storeval";
    public override Expression ToExpression(CompilationContext ctx) {
        var handle = Expression.Variable(typeof(int), "h");
        var val = Expression.Variable(typeof(long), "v");
        ctx.Variables.Add(handle); ctx.Variables.Add(val);
        var heapSet = typeof(Heap).GetMethod("Set", [typeof(int), typeof(object)])!;
        return Expression.Block(
            Expression.Assign(handle, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.Assign(val, ctx.Pop()),
            Expression.IfThenElse(
                Expression.GreaterThanOrEqual(Expression.Constant(handle), Expression.Constant(0)),
                Expression.Call(Expression.Property(ctx.State, "Heap"), heapSet, handle, Expression.Convert(val, typeof(object))),
                Expression.Assign(Expression.ArrayAccess(ctx.Slots, Expression.Negate(handle)), val)));
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Closures
// ═══════════════════════════════════════════════════════════════════

internal sealed record AllocClosureOp(int FuncIndex, int CaptureCount, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"allocclo func={FuncIndex} caps={CaptureCount}";
    static readonly System.Reflection.MethodInfo _method = typeof(Vm).GetMethod("HandleAllocClosure",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    public override Expression ToExpression(CompilationContext ctx) {
        return Expression.Block(
            ctx.WritebackSP(),
            Expression.Call(_method, ctx.State, Expression.Constant(FuncIndex), Expression.Constant(CaptureCount)),
            ctx.ResyncSP());
    }
}

internal sealed record LoadUpvalueOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"loadupv {Index}";
    static readonly System.Reflection.MethodInfo _method = typeof(Vm).GetMethod("HandleLoadUpvalue",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    public override Expression ToExpression(CompilationContext ctx) =>
        ctx.Push(Expression.Call(_method, ctx.State, Expression.Constant(Index)));
}
internal sealed record StoreUpvalueOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"storeupv {Index}";
    static readonly System.Reflection.MethodInfo _method = typeof(Vm).GetMethod("HandleStoreUpvalue",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Call(_method, ctx.State, Expression.Constant(Index), ctx.Pop());
}

// ═══════════════════════════════════════════════════════════════════
//  Exceptions
// ═══════════════════════════════════════════════════════════════════

internal sealed record ThrowOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "throw";
    static readonly System.Reflection.MethodInfo _method = typeof(Vm).GetMethod("HandleThrow",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Block(
            ctx.WritebackSP(),
            Expression.Call(_method, ctx.State, ctx.Pop()),
            ctx.ResyncPC());
}
internal sealed record EndFinallyOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "endfinally";
    static readonly System.Reflection.MethodInfo _method = typeof(Vm).GetMethod("HandleEndFinally",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Block(
            ctx.WritebackSP(),
            Expression.Call(_method, ctx.State),
            ctx.ResyncPC());
}

// ═══════════════════════════════════════════════════════════════════
//  Call / CallClosure / External
// ═══════════════════════════════════════════════════════════════════

internal sealed record CallOp(int FuncIndex, int ArgSlots, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"call func={FuncIndex} args={ArgSlots}";
    static readonly System.Reflection.MethodInfo _method = typeof(Vm).GetMethod("HandleCall",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Block(
            ctx.WritebackSP(),
            ctx.WritebackPC(),
            Expression.Call(_method, ctx.State, Expression.Constant(FuncIndex), Expression.Constant(ArgSlots)),
            ctx.ResyncPC(), ctx.ResyncSP());
}
internal sealed record CallClosureOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "callclo";
    static readonly System.Reflection.MethodInfo _method = typeof(Vm).GetMethod("HandleCallClosure",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Block(
            ctx.WritebackSP(),
            ctx.WritebackPC(),
            Expression.Call(_method, ctx.State),
            ctx.ResyncPC(), ctx.ResyncSP());
}
internal sealed record CallExternalOp(int SiteIndex, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"callext site={SiteIndex}";
    static readonly System.Reflection.MethodInfo _method = typeof(Vm).GetMethod("HandleCallExternal",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Block(
            ctx.WritebackSP(),
            ctx.WritebackPC(),
            Expression.Call(_method, ctx.State, Expression.Constant(SiteIndex)),
            ctx.ResyncPC(), ctx.ResyncSP());
}

// ═══════════════════════════════════════════════════════════════════
//  Fused µops (discovered patterns)
// ═══════════════════════════════════════════════════════════════════

/// <summary>Fused: LoadLocal K; Push V; Le — no intermediate stack.</summary>
internal sealed record CmpLocalLeOp(int LocalIndex, long Constant, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"cmplocalle loc={LocalIndex} cmp={Constant}";
    public override Expression ToExpression(CompilationContext ctx) {
        var off = Expression.Add(Expression.Add(ctx.FB, ctx.CAS), Expression.Constant(1 + LocalIndex));
        return ctx.Push(
            Expression.Condition(
                Expression.LessThanOrEqual(Expression.ArrayAccess(ctx.Slots, off),
                    Expression.Constant(Constant)),
                Expression.Constant(1L), Expression.Constant(0L)));
    }
}

/// <summary>Fused: LoadLocal K; Push V; Cmp; JumpIfFalse target — all inline.</summary>
internal sealed record CmpLocalJmpOp(int LocalIndex, long Constant, int TargetPC, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"cmplocaljmp loc={LocalIndex} cmp={Constant} -> {TargetPC}";
    public override Expression ToExpression(CompilationContext ctx) {
        var off = Expression.Add(Expression.Add(ctx.FB, ctx.CAS), Expression.Constant(1 + LocalIndex));
        return Expression.IfThenElse(
            Expression.LessThanOrEqual(Expression.ArrayAccess(ctx.Slots, off), Expression.Constant(Constant)),
            Expression.Assign(ctx.PC, Expression.Constant(TargetPC)), // true→jump
            Expression.AddAssign(ctx.PC, Expression.Constant(1)));    // false→fall through (1 µop)
    }
}