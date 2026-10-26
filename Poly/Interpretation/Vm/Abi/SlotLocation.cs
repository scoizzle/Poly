using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

using Context = Poly.Interpretation.Vm.Abi.AbiEmitter.Context;

namespace Poly.Interpretation.Vm.Abi;

/// <summary>
/// Where a value physically lives in the emitted body. The VM has one value
/// representation (a <c>long</c> slot, inline word or heap handle) but four
/// storage locations, and before this type every site re-derived the address:
/// <list type="bullet">
/// <item><see cref="Ring"/> — a transient expression-stack local (<c>_rN</c>);</item>
/// <item><see cref="Frame"/> — one frame slot, <c>_slots[_fp + n]</c> (a local,
/// a parameter, or the result slot);</item>
/// <item><see cref="Local"/> — a plain CLR local (a <c>_regN</c> register or the
/// instance handle);</item>
/// <item><see cref="Cell"/> — a <c>long[1]</c> capture cell reached through a
/// closure array or through a handle stored in a slot.</item>
/// </list>
/// A binding resolves to exactly one of these (<see cref="Context.Resolve(Variable)"/>),
/// so a read or a write is one call instead of a ladder re-implemented per site.
/// Each location states the <see cref="Representation"/> it holds: a variable's register
/// holds the value itself when the variable is IEEE (<c>double</c> / <c>float</c>) and the
/// <c>long</c> word otherwise, while a ring slot, a frame slot, and a capture cell hold
/// whatever the slot table or the word array says. A read converts only when the
/// requested representation differs from the storage's, and a write does the same, so a
/// value crosses the word boundary once at the crossing and never per access.
/// </summary>
internal abstract record SlotLocation {

    /// <summary>The CLR type the storage itself holds.</summary>
    public abstract Type Representation { get; }

    /// <summary>An expression yielding the stored value at <see cref="Representation"/>.</summary>
    public abstract Expression Read(Context ctx);

    /// <summary>An expression yielding the stored value at
    /// <paramref name="representation"/>; a storage of that representation is a copy.</summary>
    public Expression ReadAs(Context ctx, Type representation) {
        var raw = Read(ctx);
        if (representation == Representation)
            return raw;
        if (Nullable.GetUnderlyingType(representation) is not null && Representation == typeof(long))
            return RingCodec.UnboxToNullable(raw, representation, ctx.HeapLocal);
        if (Nullable.GetUnderlyingType(Representation) is not null && representation == typeof(long))
            return RingCodec.BoxNullable(raw, ctx.HeapLocal);
        return RingCodec.AsRepresentation(raw, representation);
    }

    /// <summary>An expression storing <paramref name="value"/>, which may be at any
    /// representation; it converts once when it differs from the storage's.
    /// A <c>Nullable&lt;T&gt;</c> crossing a <c>long</c> frame or cell boxes or unboxes
    /// here, which is the storage boundary.</summary>
    public Expression Write(Context ctx, Expression value) {
        if (value.Type == Representation)
            return Store(ctx, value);
        if (Nullable.GetUnderlyingType(value.Type) is not null && Representation == typeof(long))
            return Store(ctx, RingCodec.BoxNullable(value, ctx.HeapLocal));
        if (Nullable.GetUnderlyingType(Representation) is not null && value.Type == typeof(long))
            return Store(ctx, RingCodec.UnboxToNullable(value, Representation, ctx.HeapLocal));
        return Store(ctx, RingCodec.AsRepresentation(value, Representation));
    }

    /// <summary>Store a value already at <see cref="Representation"/>.</summary>
    protected abstract Expression Store(Context ctx, Expression value);

    /// <summary>A ring slot: a transient expression-stack local, named for the
    /// representation it carries (<c>_rN</c> word, <c>_dN</c> double, <c>_fN</c> float).</summary>
    internal sealed record Ring(int Slot, Type? SlotRepresentation = null) : SlotLocation {
        public override Type Representation => RingCodec.SlotTypeOf(SlotRepresentation);

        public override Expression Read(Context ctx) => ctx.RingVar(Slot, Representation);

        protected override Expression Store(Context ctx, Expression value) =>
            Assign(ctx.RingVar(Slot, Representation), value);
    }

    /// <summary>One frame slot: a local, a parameter, or the result slot. The value stack
    /// is a monomorphic <c>long[]</c>, so a frame slot holds the word.</summary>
    internal sealed record Frame(int Base) : SlotLocation {
        public override Type Representation => typeof(long);

        public override Expression Read(Context ctx) => ctx.FrameSlotRead(Base);

        protected override Expression Store(Context ctx, Expression value) =>
            Assign(ctx.FrameSlotRead(Base), value);
    }

    /// <summary>A plain CLR local: a register (<c>_regN</c> word, <c>_dregN</c> double,
    /// <c>_fregN</c> float) or the instance handle (<c>_this</c>).</summary>
    internal sealed record Local(ParameterExpression Variable, Type Storage) : SlotLocation {
        public override Type Representation => Storage;

        public override Expression Read(Context ctx) => Variable;

        protected override Expression Store(Context ctx, Expression value) =>
            Assign(Variable, value);
    }

    /// <summary>A capture cell: element 0 of a <c>long[1]</c> that both the
    /// enclosing frame and a closure body reach by the same reference, so the cell holds
    /// the word.</summary>
    internal sealed record Cell(Expression Array) : SlotLocation {
        public override Type Representation => typeof(long);

        public override Expression Read(Context ctx) =>
            ArrayAccess(Array, Constant(0));

        protected override Expression Store(Context ctx, Expression value) =>
            Assign(ArrayAccess(Array, Constant(0)), value);
    }
}