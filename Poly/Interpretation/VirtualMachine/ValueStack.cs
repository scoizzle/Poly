using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Poly.Interpretation.VirtualMachine;

internal sealed class ValueStack : IDisposable {
    private int[] _slots;
    private readonly int _initialSize;

    public ValueStack(int initialSlots = 256) {
        _initialSize = initialSlots;
        _slots = ArrayPool<int>.Shared.Rent(initialSlots);
        SP = 0;
    }

    public int SP { get; private set; }
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

    public void Push(int value) => Push<int>(value);
    public void Push(long value) => Push<long>(value);
    public int PopInt() => Pop<int>();
    public long PopLong() => Pop<long>();

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

    public Span<int> Reserve(int slots) {
        if (SP + slots > _slots.Length)
            Grow();
        var span = _slots.AsSpan(SP, slots);
        SP += slots;
        return span;
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