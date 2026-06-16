using System.IO;
using System.Linq.Expressions;
using System.Reflection;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>Context passed to <see cref="MicroOp.ToExpression"/> during
/// program compilation.  Carries <c>ParameterExpression</c>s and helpers
/// shared across all µops in a program.</summary>
public sealed record CompilationContext(
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

    // ── Cached PropertyInfo via compile-time-safe expression trees ──
    private static readonly PropertyInfo PcProp = MemberHelper.PropertyOf(() => default(VmState)!.PC);
    private static readonly PropertyInfo StackProp = MemberHelper.PropertyOf(() => default(VmState)!.Stack);
    private static readonly PropertyInfo SpProp = MemberHelper.PropertyOf(() => default(ValueStack)!.SP);
    private static readonly PropertyInfo HeapProp = MemberHelper.PropertyOf(() => default(VmState)!.Heap);

    public Expression ResyncPC() => Expression.Assign(PC, Expression.Property(State, PcProp));
    public Expression ResyncSP() => Expression.Assign(SP, Expression.Property(Expression.Property(State, StackProp), SpProp));
    public Expression HeapExpression => Expression.Property(State, HeapProp);

    /// <summary>Write local <c>sp</c> back to <c>state.Stack.SetSP(sp)</c>
    /// so that handler methods reading <c>state.Stack.SP</c> see the
    /// current value.</summary>
    public Expression WritebackSP() => Expression.Invoke(
        SetSPExpr, Expression.Property(State, StackProp), SP);
    /// <summary>Write local <c>pc</c> back to <c>state.PC</c>
    /// so that handler methods reading <c>state.PC</c> see the
    /// current value.</summary>
    public Expression WritebackPC() => Expression.Assign(
        Expression.Property(State, PcProp), PC);

    /// <summary>If the µop has <c>SourceName</c>, emit a conditional call
    /// to <c>VmTrace.LogUop</c>, gated by <c>state.Trace != null</c>.
    /// When tracing is disabled (the common case) the cost is a single
    /// null check + branch — the LogUop call is never made.</summary>
    public Expression TraceBefore(MicroOp op) {
        if (op.SourceName is not null) {
            var traceProp = Expression.Property(State, "Trace");
            return Expression.IfThen(
                Expression.NotEqual(traceProp, Expression.Constant(null, typeof(TextWriter))),
                Expression.Call(TraceLogMethod, PC,
                    Expression.Constant($"{op}  ← {op.SourceName}"), SP, FB, State));
        }
        return Expression.Empty();
    }

    private static readonly MethodInfo TraceLogMethod =
        MemberHelper.MethodOf(() => VmTrace.LogUop(default, default!, default, default, default!));

    private readonly Dictionary<string, ParameterExpression> _aliasVars = new();
    /// <summary>Get or create a typed alias variable in the compiled delegate's
    /// local scope.  Used by µops to hold direct CLR references, avoiding
    /// heap lookups.</summary>
    public ParameterExpression GetOrCreateAlias(string name, Type type) {
        if (!_aliasVars.TryGetValue(name, out var alias)) {
            alias = Expression.Variable(type, name);
            _aliasVars[name] = alias;
            Variables.Add(alias);
        }
        return alias;
    }
}

/// <summary>Base record for all micro-operations.  Each µop defines its
/// contribution to the compiled expression tree via <see cref="ToExpression"/>.
/// Only the compiled delegate executes — there is no interpretive path.</summary>
public abstract record MicroOp(NodeId? Source) {
    /// <summary>Human-readable description of the AST node that produced
    /// this µop, set during lowering.  Used by the compiled delegate to
    /// emit a trace before the operation.</summary>
    public string? SourceName { get; init; }
    public abstract Expression ToExpression(CompilationContext ctx);
}

// ── Cached MethodInfo via compile-time-safe expression trees ──
file static class UopReflection {
    public static readonly MethodInfo HeapGet =
        MemberHelper.MethodOf(() => default(Heap)!.Get(0));
    public static readonly MethodInfo HeapSet =
        MemberHelper.MethodOf(() => default(Heap)!.Set(0, null));
    public static readonly MethodInfo HeapAlloc =
        MemberHelper.MethodOf(() => default(Heap)!.Allocate(null));
}

// ═══════════════════════════════════════════════════════════════════
//  Stack manipulations
// ═══════════════════════════════════════════════════════════════════

public sealed record PushOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"push {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        ctx.Push(Expression.Constant(Value));
}

public sealed record PopOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "pop";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.PreDecrementAssign(ctx.SP);
}

public sealed record DupOp(NodeId? Source = null) : MicroOp(Source) {
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

public sealed record AddOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"addi {imm}" : "add";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(), Expression.Add(ctx.Top(), Expression.Constant(imm)))
            : ctx.BinaryArith(Expression.Add);
}
public sealed record SubOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"subi {imm}" : "sub";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(), Expression.Subtract(ctx.Top(), Expression.Constant(imm)))
            : ctx.BinaryArith(Expression.Subtract);
}
public sealed record MulOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"muli {imm}" : "mul";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(), Expression.Multiply(ctx.Top(), Expression.Constant(imm)))
            : ctx.BinaryArith(Expression.Multiply);
}
public sealed record DivOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"divi {imm}" : "div";
    public override Expression ToExpression(CompilationContext ctx) {
        if (Immediate is { } imm) {
            return Expression.Block(
                Expression.IfThen(Expression.Equal(Expression.Constant(imm), Expression.Constant(0L)),
                    Expression.Throw(Expression.Constant(new DivideByZeroException()))),
                Expression.Assign(ctx.Top(), Expression.Divide(ctx.Top(), Expression.Constant(imm))));
        }
        var r = Expression.Variable(typeof(long), "r");
        ctx.Variables.Add(r);
        return Expression.Block(
            Expression.Assign(r, ctx.Pop()),
            Expression.IfThen(Expression.Equal(r, Expression.Constant(0L)),
                Expression.Throw(Expression.Constant(new DivideByZeroException()))),
            Expression.Assign(ctx.Top(), Expression.Divide(ctx.Top(), r)));
    }
}

public sealed record EqOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"eqi {imm}" : "eq";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(),
                Expression.Condition(Expression.Equal(ctx.Top(), Expression.Constant(imm)),
                    Expression.Constant(1L), Expression.Constant(0L)))
            : ctx.BinaryCmp(Expression.Equal);
}
public sealed record NeOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"nei {imm}" : "ne";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(),
                Expression.Condition(Expression.NotEqual(ctx.Top(), Expression.Constant(imm)),
                    Expression.Constant(1L), Expression.Constant(0L)))
            : ctx.BinaryCmp(Expression.NotEqual);
}
public sealed record LtOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"lti {imm}" : "lt";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(),
                Expression.Condition(Expression.LessThan(ctx.Top(), Expression.Constant(imm)),
                    Expression.Constant(1L), Expression.Constant(0L)))
            : ctx.BinaryCmp(Expression.LessThan);
}
public sealed record LeOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"lei {imm}" : "le";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(),
                Expression.Condition(Expression.LessThanOrEqual(ctx.Top(), Expression.Constant(imm)),
                    Expression.Constant(1L), Expression.Constant(0L)))
            : ctx.BinaryCmp(Expression.LessThanOrEqual);
}
public sealed record GtOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"gti {imm}" : "gt";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(),
                Expression.Condition(Expression.GreaterThan(ctx.Top(), Expression.Constant(imm)),
                    Expression.Constant(1L), Expression.Constant(0L)))
            : ctx.BinaryCmp(Expression.GreaterThan);
}
public sealed record GeOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"gei {imm}" : "ge";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(),
                Expression.Condition(Expression.GreaterThanOrEqual(ctx.Top(), Expression.Constant(imm)),
                    Expression.Constant(1L), Expression.Constant(0L)))
            : ctx.BinaryCmp(Expression.GreaterThanOrEqual);
}

// ═══════════════════════════════════════════════════════════════════
//  Unary arithmetic
// ═══════════════════════════════════════════════════════════════════

public sealed record NegOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "neg";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.Negate(ctx.Top()));
}
public sealed record NotOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "not";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.Equal(ctx.Top(), Expression.Constant(0L)),
                Expression.Constant(1L), Expression.Constant(0L)));
}

// ═══════════════════════════════════════════════════════════════════
//  Bitwise
// ═══════════════════════════════════════════════════════════════════

public sealed record BitNotOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "bitnot";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.OnesComplement(ctx.Top()));
}
public sealed record BitAndOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"bitandi {imm}" : "bitand";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(), Expression.And(ctx.Top(), Expression.Constant(imm)))
            : ctx.BinaryArith(Expression.And);
}
public sealed record BitOrOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"bitori {imm}" : "bitor";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(), Expression.Or(ctx.Top(), Expression.Constant(imm)))
            : ctx.BinaryArith(Expression.Or);
}
public sealed record BitXorOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"bitxori {imm}" : "bitxor";
    public override Expression ToExpression(CompilationContext ctx) =>
        Immediate is { } imm
            ? Expression.Assign(ctx.Top(), Expression.ExclusiveOr(ctx.Top(), Expression.Constant(imm)))
            : ctx.BinaryArith(Expression.ExclusiveOr);
}
public sealed record ShlOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"shli {imm}" : "shl";
    public override Expression ToExpression(CompilationContext ctx) {
        if (Immediate is { } imm)
            return Expression.Assign(ctx.Top(), Expression.LeftShift(ctx.Top(), Expression.Constant((int)imm)));
        var r = Expression.Variable(typeof(int), "r");
        ctx.Variables.Add(r);
        return Expression.Block(
            Expression.Assign(r, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.Assign(ctx.Top(), Expression.LeftShift(ctx.Top(), r)));
    }
}
public sealed record ShrOp(long? Immediate = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Immediate is { } imm ? $"shri {imm}" : "shr";
    public override Expression ToExpression(CompilationContext ctx) {
        if (Immediate is { } imm)
            return Expression.Assign(ctx.Top(), Expression.RightShift(ctx.Top(), Expression.Constant((int)imm)));
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

public sealed record AddImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"addi {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.Add(ctx.Top(), Expression.Constant(Value)));
}
public sealed record SubImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"subi {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.Subtract(ctx.Top(), Expression.Constant(Value)));
}
public sealed record MulImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"muli {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.Multiply(ctx.Top(), Expression.Constant(Value)));
}
public sealed record EqImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"eqi {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.Equal(ctx.Top(), Expression.Constant(Value)),
                Expression.Constant(1L), Expression.Constant(0L)));
}
public sealed record LtImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"lti {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.LessThan(ctx.Top(), Expression.Constant(Value)),
                Expression.Constant(1L), Expression.Constant(0L)));
}
public sealed record LeImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"lei {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.LessThanOrEqual(ctx.Top(), Expression.Constant(Value)),
                Expression.Constant(1L), Expression.Constant(0L)));
}
public sealed record NegImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"negi {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.Negate(Expression.Constant(Value)));
}
public sealed record NotImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"noti {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(),
            Expression.Condition(Expression.Equal(Expression.Constant(Value), Expression.Constant(0L)),
                Expression.Constant(1L), Expression.Constant(0L)));
}

// ═══════════════════════════════════════════════════════════════════
//  Local / argument access
// ═══════════════════════════════════════════════════════════════════

public sealed record LoadLocalOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"loadlocal {Index}";
    public override Expression ToExpression(CompilationContext ctx) =>
        ctx.Push(Expression.ArrayAccess(ctx.Slots,
            Expression.Add(Expression.Add(ctx.FB, ctx.CAS), Expression.Constant(1 + Index))));
}
public sealed record StoreLocalOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"storelocal {Index}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(Expression.ArrayAccess(ctx.Slots,
            Expression.Add(Expression.Add(ctx.FB, ctx.CAS), Expression.Constant(1 + Index))),
            ctx.Pop());
}
public sealed record IncLocalOp(int Index, long Increment, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"inclocal {Index} +{Increment}";
    public override Expression ToExpression(CompilationContext ctx) {
        var off = Expression.Add(Expression.Add(ctx.FB, ctx.CAS), Expression.Constant(1 + Index));
        return Expression.Block(
            Expression.AddAssign(Expression.ArrayAccess(ctx.Slots, off), Expression.Constant(Increment)),
            ctx.Push(Expression.ArrayAccess(ctx.Slots, off)));
    }
}
public sealed record LoadArgOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"loadarg {Index}";
    public override Expression ToExpression(CompilationContext ctx) =>
        ctx.Push(Expression.ArrayAccess(ctx.Slots, Expression.Add(ctx.FB, Expression.Constant(Index))));
}
public sealed record StoreArgOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"storearg {Index}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(Expression.ArrayAccess(ctx.Slots, Expression.Add(ctx.FB, Expression.Constant(Index))),
            ctx.Pop());
}

// ═══════════════════════════════════════════════════════════════════
//  Control flow
// ═══════════════════════════════════════════════════════════════════

public sealed record JumpOp(int Target, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"jump {Target}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.PC, Expression.Constant(Target));
}
public sealed record JumpIfFalseOp(int Target, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"jmpf {Target}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.IfThenElse(
            Expression.Equal(ctx.Pop(), Expression.Constant(0L)),
            Expression.Assign(ctx.PC, Expression.Constant(Target)),
            Expression.AddAssign(ctx.PC, Expression.Constant(1)));
}
public sealed record ReturnOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "return";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.IfThen(Expression.LessThan(ctx.FB, Expression.Constant(0)),
            Expression.Assign(ctx.PC, ctx.CodeLen));
}
public sealed record ReturnFromCallOp(int ArgSlots, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"ret_call {ArgSlots}";
    static readonly PropertyInfo FBprop = MemberHelper.PropertyOf(() => default(VmState)!.FrameBase);
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

public sealed record BitNotImmOp(long Value, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"bitnoti {Value}";
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Assign(ctx.Top(), Expression.OnesComplement(Expression.Constant(Value)));
}

// ═══════════════════════════════════════════════════════════════════
//  DivRem (pop two, push remainder then quotient)
// ═══════════════════════════════════════════════════════════════════

public sealed record DivRemOp(NodeId? Source = null) : MicroOp(Source) {
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
            ctx.Push(Expression.Modulo(q, r)),   // remainder (on top)
            ctx.Push(Expression.Divide(q, r)));   // quotient
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Heap operations (LoadValue / StoreValue)
// ═══════════════════════════════════════════════════════════════════

public sealed record LoadValueOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "loadval";
    public override Expression ToExpression(CompilationContext ctx) {
        var handle = Expression.Variable(typeof(int), "h");
        var val = Expression.Variable(typeof(long), "v");
        ctx.Variables.Add(handle); ctx.Variables.Add(val);
        return Expression.Block(
            Expression.Assign(handle, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.IfThenElse(
                Expression.GreaterThanOrEqual(Expression.Constant(handle), Expression.Constant(0)),
                Expression.Assign(val, Expression.Convert(
                    Expression.Call(ctx.HeapExpression, UopReflection.HeapGet, handle), typeof(long))),
                Expression.Assign(val, Expression.ArrayAccess(ctx.Slots, Expression.Negate(handle)))),
            ctx.Push(val));
    }
}
public sealed record StoreValueOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "storeval";
    public override Expression ToExpression(CompilationContext ctx) {
        var handle = Expression.Variable(typeof(int), "h");
        var val = Expression.Variable(typeof(long), "v");
        ctx.Variables.Add(handle); ctx.Variables.Add(val);
        return Expression.Block(
            Expression.Assign(handle, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.Assign(val, ctx.Pop()),
            Expression.IfThenElse(
                Expression.GreaterThanOrEqual(Expression.Constant(handle), Expression.Constant(0)),
                Expression.Call(ctx.HeapExpression, UopReflection.HeapSet, handle, Expression.Convert(val, typeof(object))),
                Expression.Assign(Expression.ArrayAccess(ctx.Slots, Expression.Negate(handle)), val)));
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Closures
// ═══════════════════════════════════════════════════════════════════

public sealed record AllocClosureOp(int FuncIndex, int CaptureCount, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"allocclo func={FuncIndex} caps={CaptureCount}";
    static readonly MethodInfo _method = MemberHelper.MethodOf(() => Vm.HandleAllocClosure(default!, default!, default!));
    public override Expression ToExpression(CompilationContext ctx) {
        return Expression.Block(
            ctx.WritebackSP(),
            Expression.Call(_method, ctx.State, Expression.Constant(FuncIndex), Expression.Constant(CaptureCount)),
            ctx.ResyncSP());
    }
}

public sealed record LoadUpvalueOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"loadupv {Index}";
    static readonly MethodInfo _method = MemberHelper.MethodOf(() => Vm.HandleLoadUpvalue(default!, default));
    public override Expression ToExpression(CompilationContext ctx) =>
        ctx.Push(Expression.Call(_method, ctx.State, Expression.Constant(Index)));
}
public sealed record StoreUpvalueOp(int Index, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"storeupv {Index}";
    static readonly MethodInfo _method = MemberHelper.MethodOf(() => Vm.HandleStoreUpvalue(default!, default!, default));
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Call(_method, ctx.State, Expression.Constant(Index), ctx.Pop());
}

// ═══════════════════════════════════════════════════════════════════
//  Exceptions
// ═══════════════════════════════════════════════════════════════════

public sealed record ThrowOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "throw";
    static readonly MethodInfo _method = MemberHelper.MethodOf(() => Vm.HandleThrow(default!, default));
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Block(
            ctx.WritebackSP(),
            Expression.Call(_method, ctx.State, ctx.Pop()),
            ctx.ResyncPC());
}
public sealed record EndFinallyOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "endfinally";
    static readonly MethodInfo _method = MemberHelper.MethodOf(() => Vm.HandleEndFinally(default!));
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Block(
            ctx.WritebackSP(),
            Expression.Call(_method, ctx.State),
            ctx.ResyncPC());
}

// ═══════════════════════════════════════════════════════════════════
//  Call / CallClosure / External
// ═══════════════════════════════════════════════════════════════════

public sealed record CallOp(int FuncIndex, int ArgSlots, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"call func={FuncIndex} args={ArgSlots}";
    static readonly MethodInfo _method = MemberHelper.MethodOf(() => Vm.HandleCall(default!, default!, default!));
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Block(
            ctx.WritebackSP(),
            ctx.WritebackPC(),
            Expression.Call(_method, ctx.State, Expression.Constant(FuncIndex), Expression.Constant(ArgSlots)),
            ctx.ResyncPC(), ctx.ResyncSP());
}
public sealed record CallClosureOp(NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => "callclo";
    static readonly MethodInfo _method = MemberHelper.MethodOf(() => Vm.HandleCallClosure(default!));
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Block(
            ctx.WritebackSP(),
            ctx.WritebackPC(),
            Expression.Call(_method, ctx.State),
            ctx.ResyncPC(), ctx.ResyncSP());
}
public sealed record CallExternalOp(int SiteIndex, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"callext site={SiteIndex}";
    static readonly MethodInfo _method = MemberHelper.MethodOf(() => Vm.HandleCallExternal(default!, default));
    public override Expression ToExpression(CompilationContext ctx) =>
        Expression.Block(
            ctx.WritebackSP(),
            ctx.WritebackPC(),
            Expression.Call(_method, ctx.State, Expression.Constant(SiteIndex)),
            ctx.ResyncPC(), ctx.ResyncSP());
}

// ═══════════════════════════════════════════════════════════════════
//  Direct array access (no CLR call site overhead)
// ═══════════════════════════════════════════════════════════════════

/// <summary>Load <c>arr[index]</c> where <c>arr</c> is a heap-stored
/// <c>long[]</c>.  Stack: <c>[..., arr_handle, index] → [..., value]</c>.
/// When <c>Alias</c> is set, <c>arr_handle</c> is omitted — the alias
/// variable holds the direct <c>long[]</c> reference.</summary>
public sealed record ArrayLoadOp(string? Alias = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Alias is null ? "arrayload" : $"arrayload alias={Alias}";
    static readonly System.Reflection.MethodInfo HeapGet =
        UopReflection.HeapGet;
    public override Expression ToExpression(CompilationContext ctx) {
        var idx = Expression.Variable(typeof(int), "i");
        ctx.Variables.Add(idx);
        if (Alias is not null) {
            var arr = ctx.GetOrCreateAlias(Alias, typeof(long[]));
            return Expression.Block(
                Expression.Assign(idx, Expression.Convert(ctx.Pop(), typeof(int))),
                ctx.Push(Expression.ArrayAccess(arr, idx)));
        }
        var handle = Expression.Variable(typeof(int), "h");
        var arr2 = Expression.Variable(typeof(long[]), "a");
        ctx.Variables.Add(handle); ctx.Variables.Add(arr2);
        return Expression.Block(
            Expression.Assign(idx, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.Assign(handle, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.Assign(arr2, Expression.Convert(
                Expression.Call(ctx.HeapExpression, HeapGet, handle),
                typeof(long[]))),
            ctx.Push(Expression.ArrayAccess(arr2, idx)));
    }
}

/// <summary>Create a <c>long[size]</c> and push its handle.
/// Stack: <c>[..., size] → [..., handle]</c>.
/// When <c>Alias</c> is set, heap allocation is skipped — the array
/// reference is stored directly in the alias variable and a dummy
/// value (0) is pushed for the slot.</summary>
public sealed record NewArrayOp(string? Alias = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Alias is null ? "newarray" : $"newarray alias={Alias}";
    public override Expression ToExpression(CompilationContext ctx) {
        var size = Expression.Variable(typeof(int), "s");
        var arr = Expression.Variable(typeof(long[]), "a");
        ctx.Variables.Add(size); ctx.Variables.Add(arr);
        if (Alias is not null) {
            var aliasVar = ctx.GetOrCreateAlias(Alias, typeof(long[]));
            return Expression.Block(
                Expression.Assign(size, Expression.Convert(ctx.Pop(), typeof(int))),
                Expression.Assign(arr, Expression.NewArrayBounds(typeof(long), size)),
                Expression.Assign(aliasVar, arr),
                ctx.Push(Expression.Constant(0L)));
        }
        var heapAlloc = UopReflection.HeapAlloc;
        return Expression.Block(
            Expression.Assign(size, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.Assign(arr, Expression.NewArrayBounds(typeof(long), size)),
            ctx.Push(Expression.Convert(
                Expression.Call(ctx.HeapExpression, heapAlloc,
                    Expression.Convert(arr, typeof(object))),
                typeof(long))));
    }
}

/// <summary>Create a <c>long[size]</c> with an embedded constant size.
/// Stack: <c>[...] → [..., handle]</c>.
/// When <c>Alias</c> is set, heap allocation is skipped — the array
/// reference is stored directly in the alias variable and a dummy
/// value (0) is pushed for the slot.</summary>
public sealed record NewArrayImmOp(int Size, string? Alias = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Alias is null ? $"newarray {Size}" : $"newarray {Size} alias={Alias}";
    public override Expression ToExpression(CompilationContext ctx) {
        var arr = Expression.Variable(typeof(long[]), "a");
        ctx.Variables.Add(arr);
        if (Alias is not null) {
            var aliasVar = ctx.GetOrCreateAlias(Alias, typeof(long[]));
            return Expression.Block(
                Expression.Assign(arr, Expression.NewArrayBounds(typeof(long), Expression.Constant(Size))),
                Expression.Assign(aliasVar, arr),
                ctx.Push(Expression.Constant(0L)));
        }
        var heapAlloc = UopReflection.HeapAlloc;
        return Expression.Block(
            Expression.Assign(arr, Expression.NewArrayBounds(typeof(long), Expression.Constant(Size))),
            ctx.Push(Expression.Convert(
                Expression.Call(ctx.HeapExpression, heapAlloc,
                    Expression.Convert(arr, typeof(object))),
                typeof(long))));
    }
}

/// <summary>Store <c>arr[index] = val</c> where <c>arr</c> is a heap-stored
/// <c>long[]</c>.  Stack: <c>[..., arr_handle, index, val] → [...]</c>.
/// When <c>Alias</c> is set, <c>arr_handle</c> is omitted — the alias
/// variable holds the direct <c>long[]</c> reference.</summary>
public sealed record ArrayStoreOp(string? Alias = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Alias is null ? "arraystore" : $"arraystore alias={Alias}";
    static readonly System.Reflection.MethodInfo HeapGet =
        UopReflection.HeapGet;
    public override Expression ToExpression(CompilationContext ctx) {
        var val = Expression.Variable(typeof(long), "v");
        var idx = Expression.Variable(typeof(int), "i");
        ctx.Variables.Add(val); ctx.Variables.Add(idx);
        if (Alias is not null) {
            var arr = ctx.GetOrCreateAlias(Alias, typeof(long[]));
            return Expression.Block(
                Expression.Assign(val, ctx.Pop()),
                Expression.Assign(idx, Expression.Convert(ctx.Pop(), typeof(int))),
                Expression.Assign(Expression.ArrayAccess(arr, idx), val));
        }
        var handle = Expression.Variable(typeof(int), "h");
        var arr2 = Expression.Variable(typeof(long[]), "a");
        ctx.Variables.Add(handle); ctx.Variables.Add(arr2);
        return Expression.Block(
            Expression.Assign(val, ctx.Pop()),
            Expression.Assign(idx, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.Assign(handle, Expression.Convert(ctx.Pop(), typeof(int))),
            Expression.Assign(arr2, Expression.Convert(
                Expression.Call(ctx.HeapExpression, HeapGet, handle),
                typeof(long[]))),
            Expression.Assign(Expression.ArrayAccess(arr2, idx), val));
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Batch reduce over a heap-stored long[].
//  Applies `reducer(state, element) → newState` to each element,
//  running the entire loop in a single compiled expression (no µop
//  dispatch per element).  Stack: [arr_handle, wordCount, initialState] → [finalState]
//
//  The reducer is a `Func<Expression, Expression, Expression>` captured
//  at µop creation time.  Common reducers (Sum, CountNonZero, Max, Min)
//  are available as static factory methods.
// ═══════════════════════════════════════════════════════════════════

public sealed record BatchReduceOp(
    Func<Expression, Expression, Expression> Reducer,
    string? Alias = null,
    NodeId? Source = null
) : MicroOp(Source) {
    public override string ToString() => Alias is null ? "batchreduce" : $"batchreduce alias={Alias}";
    static readonly System.Reflection.MethodInfo HeapGet =
        UopReflection.HeapGet;

    public override Expression ToExpression(CompilationContext ctx) {
        var wc = Expression.Variable(typeof(int), "wc");
        var arr = Expression.Variable(typeof(long[]), "a");
        var state = Expression.Variable(typeof(long), "s");
        var i = Expression.Variable(typeof(int), "i");
        ctx.Variables.Add(wc); ctx.Variables.Add(arr);
        ctx.Variables.Add(state); ctx.Variables.Add(i);
        var start = Expression.Label("start");
        var done = Expression.Label("done");

        // Stack: [arr_handle, wordCount, initialState]
        List<Expression> setup =
        [
            Expression.Assign(state, ctx.Pop()),  // pop initialState (top)
            Expression.Assign(wc, Expression.Convert(ctx.Pop(), typeof(int))),  // pop wordCount
        ];

        if (Alias is not null) {
            setup.Add(Expression.Assign(arr, ctx.GetOrCreateAlias(Alias, typeof(long[]))));
        }
        else {
            var handle = Expression.Variable(typeof(int), "h");
            ctx.Variables.Add(handle);
            setup.Add(Expression.Assign(handle, Expression.Convert(ctx.Pop(), typeof(int))));  // pop handle (bottom)
            setup.Add(Expression.Assign(arr, Expression.Convert(
                Expression.Call(ctx.HeapExpression, HeapGet, handle),
                typeof(long[]))));
        }

        setup.Add(Expression.Assign(i, Expression.Constant(0)));
        setup.Add(Expression.Label(start));
        setup.Add(Expression.IfThen(
            Expression.LessThan(i, wc),
            Expression.Block(
                Expression.Assign(state, Reducer(state, Expression.ArrayAccess(arr, i))),
                Expression.PreIncrementAssign(i),
                Expression.Goto(start))));
        setup.Add(Expression.Label(done));
        setup.Add(ctx.Push(state));

        return Expression.Block(setup);
    }

    // ── Common reducers ──

    public static Func<Expression, Expression, Expression> Sum =>
        (s, e) => Expression.Add(s, e);

    public static Func<Expression, Expression, Expression> CountNonZero =>
        (s, e) => Expression.Condition(
            Expression.NotEqual(e, Expression.Constant(0L)),
            Expression.Add(s, Expression.Constant(1L)),
            s);

    public static Func<Expression, Expression, Expression> BitwiseOr =>
        (s, e) => Expression.Or(s, e);

    public static Func<Expression, Expression, Expression> BitwiseAnd =>
        (s, e) => Expression.And(s, e);

    public static Func<Expression, Expression, Expression> Min =>
        (s, e) => Expression.Condition(
            Expression.LessThan(e, s),
            e, s);

    public static Func<Expression, Expression, Expression> Max =>
        (s, e) => Expression.Condition(
            Expression.GreaterThan(e, s),
            e, s);
}

// ═══════════════════════════════════════════════════════════════════
//  CountBitsOp — count set bits in a long[] range.
//  Uses BitOperations.PopCount (JIT emits CNT/POPCNT).
//  Stack: [arr_handle, wordCount] → [bitCount]
// ═══════════════════════════════════════════════════════════════════

public sealed record CountBitsOp(string? Alias = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Alias is null ? "countbits" : $"countbits alias={Alias}";

    public override Expression ToExpression(CompilationContext ctx) {
        var arr = Expression.Variable(typeof(long[]), "a");
        var wc = Expression.Variable(typeof(int), "wc");
        ctx.Variables.Add(arr); ctx.Variables.Add(wc);

        var setup = new List<Expression>
        {
            Expression.Assign(wc, Expression.Convert(ctx.Pop(), typeof(int))),
        };
        if (Alias is not null) {
            setup.Add(Expression.Assign(arr, ctx.GetOrCreateAlias(Alias, typeof(long[]))));
            setup.Add(ctx.Pop());
        }
        else {
            var handle = Expression.Variable(typeof(int), "h");
            ctx.Variables.Add(handle);
            var heapGet = UopReflection.HeapGet;
            setup.Add(Expression.Assign(handle, Expression.Convert(ctx.Pop(), typeof(int))));
            setup.Add(Expression.Assign(arr, Expression.Convert(
                Expression.Call(ctx.HeapExpression, heapGet, handle),
                typeof(long[]))));
        }
        setup.Add(ctx.Push(Expression.Call(
            MemberHelper.MethodOf(() => Vm.CountBitsVectorized(default!, default!)),
            arr, wc)));
        return Expression.Block(setup);
    }
}

// ═══════════════════════════════════════════════════════════════════
//  StridedSetOp — for each j in [start..limit] step step, set
//  bits[j >> 6] |= 1L << (j & 63).  Runs the entire loop in a
//  single compiled expression (no µop dispatch per iteration).
//  Stack: [arr_handle, startValue, step, limit] → []
// ═══════════════════════════════════════════════════════════════════

/// <summary>Batch-set bits in a word array: for each word from
/// <c>start/64</c> to <c>limit/64</c>, load the word once, set
/// all relevant bit positions, and write back once.  Uses
/// <c>Vm.StridedBatchSet</c> for the actual work.
/// Stack: [arr_handle_or_dummy, startValue, step, limit] → []</summary>
public sealed record StridedSetOp(string? Alias = null, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => Alias is null ? "stridedbatchset" : $"stridedbatchset alias={Alias}";

    public override Expression ToExpression(CompilationContext ctx) {
        var arr = Expression.Variable(typeof(long[]), "a");
        var limit = Expression.Variable(typeof(long), "lim");
        var step = Expression.Variable(typeof(long), "stp");
        var j = Expression.Variable(typeof(long), "j");
        var word = Expression.Variable(typeof(long), "w");
        var v = Expression.Variable(typeof(long), "v");
        var lastInWord = Expression.Variable(typeof(long), "last");
        ctx.Variables.Add(arr); ctx.Variables.Add(limit);
        ctx.Variables.Add(step); ctx.Variables.Add(j);
        ctx.Variables.Add(word); ctx.Variables.Add(v); ctx.Variables.Add(lastInWord);

        var setup = new List<Expression>
        {
            Expression.Assign(limit, ctx.Pop()),
            Expression.Assign(step, ctx.Pop()),
            Expression.Assign(j, ctx.Pop()),
        };
        if (Alias is not null) {
            setup.Add(Expression.Assign(arr, ctx.GetOrCreateAlias(Alias, typeof(long[]))));
            setup.Add(ctx.Pop());
        }
        else {
            var handle = Expression.Variable(typeof(int), "h");
            ctx.Variables.Add(handle);
            var heapGet = UopReflection.HeapGet;
            setup.Add(Expression.Assign(handle, Expression.Convert(ctx.Pop(), typeof(int))));
            setup.Add(Expression.Assign(arr, Expression.Convert(
                Expression.Call(ctx.HeapExpression, heapGet, handle),
                typeof(long[]))));
        }

        // Align j to step boundary
        var rem = Expression.Variable(typeof(long), "rem");
        ctx.Variables.Add(rem);
        setup.Add(Expression.Assign(rem, Expression.Modulo(Expression.Constant(0L), Expression.Constant(1L))));
        Expression.IfThen(Expression.Constant(false), Expression.Empty());

        var wordLoop = Expression.Label("wl");
        var wordDone = Expression.Label("wd");
        var bitLoop = Expression.Label("bl");
        var done = Expression.Label("done");

        // Outer word loop
        setup.Add(Expression.Label(wordLoop));
        setup.Add(Expression.IfThen(Expression.GreaterThan(j, limit),
            Expression.Goto(done)));
        setup.Add(Expression.Assign(word, Expression.RightShift(j, Expression.Constant(6))));
        setup.Add(Expression.Assign(v, Expression.ArrayAccess(arr,
            Expression.Convert(word, typeof(int)))));
        setup.Add(Expression.Assign(lastInWord, Expression.Condition(
            Expression.LessThan(limit, Expression.Add(Expression.LeftShift(word, Expression.Constant(6)), Expression.Constant(63L))),
            limit,
            Expression.Add(Expression.LeftShift(word, Expression.Constant(6)), Expression.Constant(63L)))));

        // Inner bit loop
        setup.Add(Expression.Label(bitLoop));
        setup.Add(Expression.IfThen(Expression.GreaterThan(j, lastInWord),
            Expression.Goto(wordDone)));
        setup.Add(Expression.Assign(v,
            Expression.Or(v, Expression.LeftShift(Expression.Constant(1L),
                Expression.Convert(Expression.And(j, Expression.Constant(63L)), typeof(int))))));
        setup.Add(Expression.AddAssign(j, step));
        setup.Add(Expression.Goto(bitLoop));

        // End of word: write back, continue outer loop
        setup.Add(Expression.Label(wordDone));
        setup.Add(Expression.Assign(Expression.ArrayAccess(arr,
            Expression.Convert(word, typeof(int))), v));
        setup.Add(Expression.Goto(wordLoop));

        setup.Add(Expression.Label(done));
        return Expression.Block(setup);
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Markers (no-op labels for µop list readability)
// ═══════════════════════════════════════════════════════════════════

/// <summary>No-op marker µop for readability of the µop list during
/// debugging.  Generates zero code at runtime.</summary>
public sealed record CommentOp(string Text, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"; {Text}";
    public override Expression ToExpression(CompilationContext ctx) => Expression.Empty();
}

// ═══════════════════════════════════════════════════════════════════
//  Fused µops (discovered patterns)
// ═══════════════════════════════════════════════════════════════════

/// <summary>Fused: LoadLocal K; Push V; Le — no intermediate stack.</summary>
public sealed record CmpLocalLeOp(int LocalIndex, long Constant, NodeId? Source = null) : MicroOp(Source) {
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
public sealed record CmpLocalJmpOp(int LocalIndex, long Constant, int TargetPC, NodeId? Source = null) : MicroOp(Source) {
    public override string ToString() => $"cmplocaljmp loc={LocalIndex} cmp={Constant} -> {TargetPC}";
    public override Expression ToExpression(CompilationContext ctx) {
        var off = Expression.Add(Expression.Add(ctx.FB, ctx.CAS), Expression.Constant(1 + LocalIndex));
        return Expression.IfThenElse(
            Expression.LessThanOrEqual(Expression.ArrayAccess(ctx.Slots, off), Expression.Constant(Constant)),
            Expression.Assign(ctx.PC, Expression.Constant(TargetPC)), // true→jump
            Expression.AddAssign(ctx.PC, Expression.Constant(1)));    // false→fall through (1 µop)
    }
}