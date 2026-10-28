using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

using Context = Poly.Interpretation.Vm.Abi.AbiEmitter.Context;

namespace Poly.Interpretation.Vm.Abi;

/// <summary>
/// Where a value physically lives in the emitted body. Four places, and a
/// binding resolves to exactly one (<see cref="Context.Resolve(Variable)"/>):
/// <list type="bullet">
/// <item><see cref="Ring"/> — a transient pool local (<c>_rN</c>, <c>_dN</c>,
/// <c>_fN</c>, <c>_iN</c>, <c>_nN_T</c>);</item>
/// <item><see cref="Frame"/> — one frame word, <c>_slots[_fp + n]</c>;</item>
/// <item><see cref="Local"/> — a plain CLR local (a pool lease or the instance
/// handle);</item>
/// <item><see cref="Cell"/> — element 0 of a <c>long[1]</c> capture cell.</item>
/// </list>
/// A typed read of a stored word is <see cref="LocalType.FromWord"/>. There is
/// no view layered on a place.
/// </summary>
internal abstract record SlotLocation {

    /// <summary>The CLR type the storage itself holds.</summary>
    public abstract Type Representation { get; }

    /// <summary>An expression yielding the stored value at <see cref="Representation"/>.</summary>
    public abstract Expression Read(Context ctx);

    /// <summary>The stored word read as <paramref name="representation"/>. A
    /// <c>long</c> slot uses that form directly. A slot that already holds the
    /// value converts from its own form.</summary>
    public Expression ReadAs(Context ctx, Type representation) {
        if (representation == Representation)
            return Read(ctx);
        if (Representation == typeof(long)) {
            var form = LocalType.Of(representation);
            return form.FromWord(Read(ctx), form.Clr, ctx.HeapLocal);
        }
        if (representation == typeof(long))
            return LocalType.Encode(Read(ctx), Representation, ctx.HeapLocal);
        return LocalType.Of(Representation).FromWord(Read(ctx), representation, ctx.HeapLocal);
    }

    /// <summary>Store <paramref name="value"/> in this slot's form. A <c>long</c>
    /// slot encodes the value's own form. A typed slot decodes into its form.</summary>
    public Expression Write(Context ctx, Expression value) {
        if (value.Type == Representation)
            return Store(ctx, value);
        if (Representation == typeof(long))
            return Store(ctx, LocalType.Encode(value, value.Type, ctx.HeapLocal));
        if (value.Type == typeof(long))
            return Store(ctx, LocalType.Decode(value, Representation, Representation, ctx.HeapLocal));
        var word = LocalType.Encode(value, value.Type, ctx.HeapLocal);
        return Store(ctx, LocalType.Decode(word, Representation, Representation, ctx.HeapLocal));
    }

    /// <summary>Store a value already at <see cref="Representation"/>.</summary>
    protected abstract Expression Store(Context ctx, Expression value);

    /// <summary>A ring slot: a transient expression-stack local, named for the
    /// representation it carries (<c>_rN</c> word, <c>_dN</c> double, <c>_fN</c> float).</summary>
    internal sealed record Ring(int Slot, Type? SlotRepresentation = null) : SlotLocation {
        public override Type Representation =>
            SlotRepresentation is { } clr ? LocalType.Of(clr).SlotType : typeof(long);

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

    /// <summary>A plain CLR local: a pool lease, or the instance handle (<c>_this</c>).</summary>
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