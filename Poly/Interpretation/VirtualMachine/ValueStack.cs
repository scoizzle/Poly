using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Poly.Interpretation.VirtualMachine;

internal sealed class ValueStack(int initialSlots = 256) : IDisposable {
    private int[] _slots = ArrayPool<int>.Shared.Rent(initialSlots);

    public int SP { get; private set; } = 0;
    public bool IsEmpty => SP == 0;

    public Span<int> AsSpan() => _slots.AsSpan(0, SP);

    public void Push<T>(T value) where T : unmanaged {
        int slots = SlotCountOf<T>();
        if (SP + slots > _slots.Length)
            Grow();
        MemoryMarshal.Write(MemoryMarshal.AsBytes(_slots.AsSpan(SP, slots)), in value);
        SP += slots;
    }

    public T Pop<T>() where T : unmanaged {
        int slots = SlotCountOf<T>();
        if (SP < slots)
            throw new InvalidOperationException("Stack underflow");
        SP -= slots;
        return MemoryMarshal.Read<T>(MemoryMarshal.AsBytes(_slots.AsSpan(SP, slots)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(int value) {
        if (SP < _slots.Length) { _slots[SP++] = value; return; }
        GrowNoInline(value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowNoInline(int value) { Grow(); _slots[SP++] = value; }

    public void Push(long value) => Push<long>(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int PopInt() {
        if (SP > 0) return _slots[--SP];
        return ThrowUnderflow<int>();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T ThrowUnderflow<T>() => throw new InvalidOperationException("Stack underflow");

    public long PopLong() => Pop<long>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushTwo(int low, int high) {
        if (SP + 1 < _slots.Length) { _slots[SP++] = low; _slots[SP++] = high; return; }
        GrowNoInline(low, high);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowNoInline(int low, int high) { Grow(); _slots[SP++] = low; _slots[SP++] = high; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int low, int high) PopTwo() {
        if (SP >= 2) { int h = _slots[--SP]; int l = _slots[--SP]; return (l, h); }
        return ThrowUnderflow<(int, int)>();
    }

    public void Drop(int slots) {
        if (slots < 0 || SP < slots)
            throw new InvalidOperationException("Stack underflow");
        SP -= slots;
    }

    public void TruncateTo(int targetSp) {
        if (targetSp < 0 || targetSp > SP)
            throw new ArgumentOutOfRangeException(nameof(targetSp));
        SP = targetSp;
    }

    public void Reserve(int slots) {
        if (SP + slots > _slots.Length)
            Grow();
        SP += slots;
    }

    public int PeekInt(int offset = 0) {
        var idx = SP - 1 - offset;
        if (idx < 0)
            throw new InvalidOperationException("Peek out of range");
        return _slots[idx];
    }

    public int ReadSlot(int index) => _slots[index];

    public void CopyFrom(int srcSlot, int destSlot, int count) {
        Array.Copy(_slots, srcSlot, _slots, destSlot, count);
    }

    public void Dispose() {
        if (_slots is not null) {
            ArrayPool<int>.Shared.Return(_slots);
            _slots = null!;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SlotCountOf<T>() where T : unmanaged =>
        (Unsafe.SizeOf<T>() + 3) / 4;

    private void Grow() {
        var newSize = _slots.Length * 2;
        var newSlots = ArrayPool<int>.Shared.Rent(newSize);
        Array.Copy(_slots, newSlots, SP);
        ArrayPool<int>.Shared.Return(_slots);
        _slots = newSlots;
    }
}