using System.Buffers;
using System.Runtime.CompilerServices;

namespace Poly.Interpretation.Vm;

public sealed class ValueStack(int initialSlots = 256) : IDisposable {
    private long[] _slots = ArrayPool<long>.Shared.Rent(initialSlots);

    public int StackPointer { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(long value) {
        if (StackPointer < _slots.Length) { _slots[StackPointer++] = value; return; }
        GrowNoInline(value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowNoInline(long value) { Grow(); _slots[StackPointer++] = value; }

    public void Push(int value) => Push((long)value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long Pop() {
        if (StackPointer > 0) return _slots[--StackPointer];
        return ThrowUnderflow();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ThrowUnderflow() =>
        throw new InvalidOperationException("Stack underflow");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Drop(int count) {
        var sp = StackPointer - count;
        if ((uint)sp > (uint)StackPointer)
            ThrowUnderflow();
        StackPointer = sp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reserve(int count) {
        var sp = StackPointer + count;
        if (sp > _slots.Length) Grow();
        StackPointer += count;
    }

    public long[] RawSlots => _slots;

    public void SetStackPointer(int value) => StackPointer = value;

    public void Reset() => StackPointer = 0;

    public void Dispose() {
        if (_slots is not null) {
            ArrayPool<long>.Shared.Return(_slots);
            _slots = null!;
        }
    }

    private void Grow() {
        var newSize = _slots.Length * 2;
        var newSlots = ArrayPool<long>.Shared.Rent(newSize);
        Array.Copy(_slots, newSlots, StackPointer);
        ArrayPool<long>.Shared.Return(_slots);
        _slots = newSlots;
    }
}