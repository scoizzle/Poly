using System.Collections;
using System.Runtime.CompilerServices;

namespace Poly.Interpretation.Vm;

/// <summary>
/// The private-frame layout of one compiled iterator body (#338 slice 2): how many words its
/// frame holds, and which two of them carry the body's answer to <see cref="VmIterator{T}.MoveNext"/>.
/// Registered against the compiled step delegate, because the call site holds only the callable slot.
/// </summary>
internal sealed record VmIteratorShape(int FrameSize, int MovedSlot, int CurrentSlot) {
    private static readonly ConditionalWeakTable<Action<VmState>, VmIteratorShape> Shapes = new();

    public static void Register(Action<VmState> step, VmIteratorShape shape) =>
        Shapes.AddOrUpdate(step, shape);

    public static VmIteratorShape Of(Action<VmState> step) =>
        Shapes.TryGetValue(step, out var shape)
            ? shape
            : throw new InvalidOperationException("VM: iterator body has no registered frame shape.");
}

/// <summary>
/// The enumerable and enumerator an iterator method returns. The first
/// <see cref="GetEnumerator"/> hands out this instance, as C# does; a second call on one that
/// has been handed out returns a fresh iterator over the same arguments, so two live enumerators
/// over one call are independent.
/// </summary>
/// <remarks>
/// The iterator owns its frame: a private <c>long[]</c> holding the receiver, the arguments,
/// the body's locals, its spills, and the resume state. <c>SpillBarrier</c> writes
/// <c>_slots[_fp + slot]</c>, and the shared stack pointer covers only a call's header and
/// arguments, so a frame left on the shared stack between two yields could be overwritten by a
/// nested call. <see cref="MoveNext"/> therefore copies the private frame onto the stack above the
/// stack pointer, runs the body, and copies it back.
/// </remarks>
public sealed class VmIterator<T> : IEnumerable<T>, IEnumerator<T> {
    private readonly VmState _state;
    private readonly Action<VmState>[] _table;
    private readonly long[] _arguments;
    private readonly Func<VmManagedObjectHeap, long, T> _decode;
    private long[]? _frame;
    private VmIteratorShape? _shape;
    private bool _handedOut;
    private bool _done;
    private T _current = default!;

    internal VmIterator(
        VmState state, Action<VmState>[] table, long[] arguments, Func<VmManagedObjectHeap, long, T> decode) {
        _state = state;
        _table = table;
        _arguments = arguments;
        _decode = decode;
    }

    /// <inheritdoc />
    public T Current => _current;

    object? IEnumerator.Current => _current;

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() {
        if (!_handedOut) {
            _handedOut = true;
            return this;
        }
        return new VmIterator<T>(_state, _table, _arguments, _decode) { _handedOut = true };
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Runs the body from where it last yielded to the next <c>yield return</c>, or
    /// to its end. The body does not run before the first call, and not at all once finished.</summary>
    public bool MoveNext() {
        if (_done)
            return false;
        var step = _table[0] ?? throw new InvalidOperationException("VM: iterator body is not compiled.");
        var shape = _shape ??= VmIteratorShape.Of(step);
        var frame = _frame ??= NewFrame(shape);

        var state = _state;
        var stack = state.Stack;
        int callSp = stack.StackPointer;
        int framePos = VmCall.FramePointer(callSp);
        int need = framePos + frame.Length;
        if (need > stack.RawSlots.Length)
            throw new InvalidOperationException("VM: value stack overflow.");
        stack.SetStackPointer(need);

        var slots = stack.RawSlots;
        slots[callSp] = state.FramePos;
        slots[callSp + 1] = callSp;
        // Copy-in: the private frame onto the shared stack, above the stack pointer.
        Array.Copy(frame, 0, slots, framePos, frame.Length);

        int savedFp = state.FramePos;
        int savedClosure = state.ClosureHandle;
        state.FramePos = framePos;
        state.ClosureHandle = 0;
        try {
            step(state);
            // Copy-out: whatever the body spilled or advanced goes back to the private frame.
            Array.Copy(slots, framePos, frame, 0, frame.Length);
        }
        catch {
            _done = true;
            throw;
        }
        finally {
            state.FramePos = savedFp;
            state.ClosureHandle = savedClosure;
            stack.SetStackPointer(callSp);
        }

        if (frame[shape.MovedSlot] == 0L) {
            _done = true;
            _current = default!;
            return false;
        }
        _current = _decode(state.Heap, frame[shape.CurrentSlot]);
        return true;
    }

    private long[] NewFrame(VmIteratorShape shape) {
        var frame = new long[Math.Max(shape.FrameSize, _arguments.Length)];
        Array.Copy(_arguments, frame, _arguments.Length);
        return frame;
    }

    /// <inheritdoc />
    public void Reset() => throw new NotSupportedException();

    /// <inheritdoc />
    public void Dispose() => _done = true;
}