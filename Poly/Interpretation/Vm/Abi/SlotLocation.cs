using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

using Context = Poly.Interpretation.Vm.Abi.AbiEmitter.Context;

namespace Poly.Interpretation.Vm.Abi;

/// <summary>
/// Where a value physically lives in the emitted body. The VM has one value
/// representation (a <c>long</c> slot, inline word or heap handle) but five
/// storage locations, and before this type every site re-derived the address:
/// <list type="bullet">
/// <item><see cref="Ring"/> — a transient expression-stack local (<c>_rN</c>);</item>
/// <item><see cref="Frame"/> — one frame slot, <c>_slots[_fp + n]</c> (a local,
/// a parameter, or the result slot);</item>
/// <item><see cref="Window"/> — <c>n</c> contiguous frame slots, a promoted
/// small array, addressed by element;</item>
/// <item><see cref="Local"/> — a plain CLR local (a <c>_regN</c> register or the
/// instance handle);</item>
/// <item><see cref="Cell"/> — a <c>long[1]</c> capture cell reached through a
/// closure array or through a handle stored in a slot.</item>
/// </list>
/// A binding resolves to exactly one of these (<see cref="Context.Resolve(Variable)"/>),
/// so a read or a write is one call instead of a ladder re-implemented per site.
/// The location carries no value shape: shape is <see cref="RingCodec"/>'s job,
/// and the slot always holds the same <c>long</c> either way.
/// </summary>
internal abstract record SlotLocation {

    /// <summary>An expression yielding the stored slot value.</summary>
    public abstract Expression Read(Context ctx);

    /// <summary>An expression storing <paramref name="value"/>; yields the stored value.</summary>
    public abstract Expression Write(Context ctx, Expression value);

    /// <summary>A ring slot: a transient expression-stack local (<c>_rN</c>).</summary>
    internal sealed record Ring(int Slot) : SlotLocation {
        public override Expression Read(Context ctx) => ctx.RingVar(Slot);
        public override Expression Write(Context ctx, Expression value) =>
            Assign(ctx.RingVar(Slot), value);
    }

    /// <summary>One frame slot: a local, a parameter, or the result slot.</summary>
    internal sealed record Frame(int Base) : SlotLocation {
        public override Expression Read(Context ctx) => ctx.FrameSlotRead(Base);
        public override Expression Write(Context ctx, Expression value) =>
            Assign(ctx.FrameSlotRead(Base), value);
    }

    /// <summary>A plain CLR local: a register (<c>_regN</c>) or the instance
    /// handle (<c>_this</c>).</summary>
    internal sealed record Local(ParameterExpression Variable) : SlotLocation {
        public override Expression Read(Context ctx) => Variable;
        public override Expression Write(Context ctx, Expression value) =>
            Assign(Variable, value);
    }

    /// <summary>A capture cell: element 0 of a <c>long[1]</c> that both the
    /// enclosing frame and a closure body reach by the same reference.</summary>
    internal sealed record Cell(Expression Array) : SlotLocation {
        public override Expression Read(Context ctx) =>
            ArrayAccess(Array, Constant(0));

        public override Expression Write(Context ctx, Expression value) =>
            Assign(ArrayAccess(Array, Constant(0)), value);
    }

    /// <summary><paramref name="Length"/> contiguous frame slots: a promoted
    /// small array. A window has no single-slot value, so it is only read and
    /// written by element; an index outside it fails the way a heap array does
    /// rather than silently touching a neighbouring local. <paramref name="ElementClr"/>
    /// is part of the window's contract — it fixes how a slot decodes — not a
    /// duplicate of the value shape.</summary>
    internal sealed record Window(int Base, int Length, Type ElementClr) : SlotLocation {
        private static readonly ConstructorInfo IndexOutOfRangeConstructor =
            typeof(IndexOutOfRangeException).GetConstructor(Type.EmptyTypes)!;

        public override Expression Read(Context ctx) =>
            throw new InvalidOperationException(
                "VM compile rejected: a frame window is read by element, not as one slot.");

        public override Expression Write(Context ctx, Expression value) =>
            throw new InvalidOperationException(
                "VM compile rejected: a frame window is written by element, not as one slot.");

        public Expression ReadAt(Context ctx, Expression index) {
            var i = Variable(typeof(int), "_ai");
            return Block([i],
                Assign(i, Convert(index, typeof(int))),
                BoundsCheck(i),
                ArrayAccess(ctx.SlotsLocal, Add(ctx.FramePosLocal, Add(Constant(Base), i))));
        }

        public Expression WriteAt(Context ctx, Expression index, Expression value) {
            var i = Variable(typeof(int), "_ai");
            return Block([i],
                Assign(i, Convert(index, typeof(int))),
                BoundsCheck(i),
                Assign(ArrayAccess(ctx.SlotsLocal, Add(ctx.FramePosLocal, Add(Constant(Base), i))), value));
        }

        /// <summary>Preserve the heap path's failure for an index outside the window.</summary>
        public Expression BoundsCheck(Expression index) =>
            IfThen(
                Or(LessThan(index, Constant(0)), GreaterThanOrEqual(index, Constant(Length))),
                Throw(New(IndexOutOfRangeConstructor)));
    }
}