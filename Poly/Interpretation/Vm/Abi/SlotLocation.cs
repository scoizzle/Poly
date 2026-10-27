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
/// <item><see cref="View"/> — a word location read and written as a CLR type.
/// <see cref="RingCodec.ToFrame"/> and <see cref="RingCodec.FromFrame"/> are the
/// only conversion, and they follow <see cref="ValueRepresentationClassifier.KindFor(Type)"/>.</item>
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

    /// <summary>The stored word read as <paramref name="representation"/>. A
    /// <c>long</c> slot uses that form directly. A slot that already holds the
    /// value converts from its own form.</summary>
    public Expression ReadAs(Context ctx, Type representation) {
        if (representation == Representation)
            return Read(ctx);
        if (Representation == typeof(long))
            return Holding(representation).Read(ctx);
        if (representation == typeof(long))
            return FrameRepresentation.Encode(Read(ctx), Representation, ctx.HeapLocal);
        return FrameRepresentation.For(Representation).FromWord(Read(ctx), representation, ctx.HeapLocal);
    }

    /// <summary>Store <paramref name="value"/> in this slot's form. A <c>long</c>
    /// slot encodes the value's own form. A typed slot decodes into its form.</summary>
    public Expression Write(Context ctx, Expression value) {
        if (value.Type == Representation)
            return Store(ctx, value);
        if (Representation == typeof(long))
            return Store(ctx, FrameRepresentation.Encode(value, value.Type, ctx.HeapLocal));
        if (value.Type == typeof(long))
            return Store(ctx, FrameRepresentation.Decode(value, Representation, Representation, ctx.HeapLocal));
        var word = FrameRepresentation.Encode(value, value.Type, ctx.HeapLocal);
        return Store(ctx, FrameRepresentation.Decode(word, Representation, Representation, ctx.HeapLocal));
    }

    /// <summary>This place, read and written as <paramref name="clr"/>.</summary>
    public View Holding(Type clr) => new(this, clr);

    /// <summary>Store a value already at <see cref="Representation"/>.</summary>
    protected abstract Expression Store(Context ctx, Expression value);

    /// <summary>A ring slot: a transient expression-stack local, named for the
    /// representation it carries (<c>_rN</c> word, <c>_dN</c> double, <c>_fN</c> float).</summary>
    internal sealed record Ring(int Slot, Type? SlotRepresentation = null) : SlotLocation {
        public override Type Representation =>
            SlotRepresentation is { } clr ? FrameRepresentation.For(clr).SlotType : typeof(long);

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

    /// <summary>A word location read and written as <see cref="Form"/>. The
    /// representation owns the conversion. The location owns the address.</summary>
    internal sealed record View(SlotLocation Storage, FrameRepresentation Form) : SlotLocation {
        public View(SlotLocation storage, Type clr) : this(storage, FrameRepresentation.For(clr)) { }

        public override Type Representation => Form.Clr;

        public override Expression Read(Context ctx) =>
            Form.FromWord(Storage.Read(ctx), Form.Clr, ctx.HeapLocal);

        protected override Expression Store(Context ctx, Expression value) =>
            Storage.Store(ctx, FrameRepresentation.Encode(value, Form.Clr, ctx.HeapLocal));
    }
}