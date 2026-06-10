using System.Buffers;
using System.Runtime.CompilerServices;

namespace Poly.Interpretation.VirtualMachine;

internal sealed class ValueStack(int initialSlots = 256) : IDisposable {
    private long[] _slots = ArrayPool<long>.Shared.Rent(initialSlots);

    public int SP { get; private set; }
    public bool IsEmpty => SP == 0;
    public Span<long> AsSpan() => _slots.AsSpan(0, SP);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(long value) {
        if (SP < _slots.Length) { _slots[SP++] = value; return; }
        GrowNoInline(value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowNoInline(long value) { Grow(); _slots[SP++] = value; }

    public void Push(int value) => Push((long)value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long Pop() {
        if (SP > 0) return _slots[--SP];
        return ThrowUnderflow();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ThrowUnderflow() =>
        throw new InvalidOperationException("Stack underflow");

    public int PopInt() => (int)Pop();

    public void Drop(int count) {
        if (count < 0 || SP < count)
            throw new InvalidOperationException("Stack underflow");
        SP -= count;
    }

    public void Reserve(int count) {
        if (SP + count > _slots.Length)
            Grow();
        SP += count;
    }

    internal long[] RawSlots => _slots;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int ReadSlot(int index) =>
        index >= 0 && index < _slots.Length ? (int)_slots[index] : 0;

    internal void SetSP(int value) => SP = value;

    public void Reset() => SP = 0;

    public void Dispose() {
        if (_slots is not null) {
            ArrayPool<long>.Shared.Return(_slots);
            _slots = null!;
        }
    }

    private void Grow() {
        var newSize = _slots.Length * 2;
        var newSlots = ArrayPool<long>.Shared.Rent(newSize);
        Array.Copy(_slots, newSlots, SP);
        ArrayPool<long>.Shared.Return(_slots);
        _slots = newSlots;
    }
}